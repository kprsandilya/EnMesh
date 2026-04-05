import asyncio
import io
import logging
import sys
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import FastAPI, File, HTTPException, Query, Request, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from PIL import Image
from starlette.staticfiles import StaticFiles

from app import config
from app.classification.embedding import EmbeddingPlacementClassifier
from app.filename_safe import stem_from_upload_filename
from app.generation import MeshGenerator
from app.mesh_output import save_trimesh_mesh
from app.schemas import GenerateMeshResponse, HealthResponse

LOG_FORMAT = "%(asctime)s | %(levelname)s | %(name)s | %(message)s"

generator = MeshGenerator(device=config.DEVICE, chunk_size=config.CHUNK_SIZE)
placement_classifier = EmbeddingPlacementClassifier(config.EMBEDDING_MODEL_ID)


def _configure_logging() -> None:
    root = logging.getLogger()
    if root.handlers:
        return
    logging.basicConfig(
        level=logging.INFO,
        format=LOG_FORMAT,
        datefmt="%Y-%m-%d %H:%M:%S",
        stream=sys.stdout,
    )


@asynccontextmanager
async def lifespan(_app: FastAPI) -> AsyncIterator[None]:
    _configure_logging()
    log = logging.getLogger(__name__)
    config.OUTPUTS_DIR.mkdir(parents=True, exist_ok=True)
    log.info("EnMesh API starting — loading embedding classifier …")
    placement_classifier.load()
    log.info("EnMesh API — loading mesh model …")
    generator.load(config.MODEL_ID)
    log.info("EnMesh API ready")
    yield
    log.info("EnMesh API shutting down")


app = FastAPI(title="EnMesh", version="0.4.0", lifespan=lifespan)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

app.mount(
    "/output-files",
    StaticFiles(directory=str(config.OUTPUTS_DIR)),
    name="output-files",
)


@app.middleware("http")
async def log_requests(request: Request, call_next):
    logger = logging.getLogger("enmesh.http")
    logger.info("%s %s", request.method, request.url.path)
    response = await call_next(request)
    logger.info("%s %s -> %s", request.method, request.url.path, response.status_code)
    return response


@app.post("/generate", response_model=GenerateMeshResponse)
async def generate(
    image: UploadFile = File(..., description="Input image (PNG, JPG, etc.)"),
    format: str = Query("obj", pattern="^(obj|glb)$"),
    resolution: int = Query(config.DEFAULT_RESOLUTION, ge=64, le=512),
    remove_bg: bool = Query(True),
) -> GenerateMeshResponse:
    if not generator.is_ready:
        raise HTTPException(503, detail="Model is still loading — try again shortly.")

    raw = await image.read()
    try:
        pil_image = Image.open(io.BytesIO(raw))
    except Exception as exc:
        raise HTTPException(400, detail=f"Could not decode image: {exc}") from exc

    stem = stem_from_upload_filename(image.filename)
    loop = asyncio.get_running_loop()
    category = await loop.run_in_executor(
        None,
        lambda: placement_classifier.classify_stem(stem),
    )
    log = logging.getLogger(__name__)
    log.info(
        "Generate: filename=%r stem=%r category=%s",
        image.filename,
        stem,
        category,
    )

    mesh = await loop.run_in_executor(
        None,
        lambda: generator.generate(
            pil_image, resolution=resolution, remove_bg=remove_bg
        ),
    )

    abs_path, download_path = await loop.run_in_executor(
        None,
        lambda: save_trimesh_mesh(
            mesh,
            config.OUTPUTS_DIR,
            original_stem=stem or "upload",
            category=category,
            file_type=format,
        ),
    )

    return GenerateMeshResponse(
        mesh_path=str(abs_path),
        category=category,
        download_path=download_path,
    )


@app.get("/health", response_model=HealthResponse)
def health() -> HealthResponse:
    return HealthResponse(
        status="ok",
        model_loaded=generator.is_real,
        simulated=not generator.is_real,
        tsr_error=generator.tsr_import_error,
    )


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("app.main:app", host="0.0.0.0", port=8000, reload=True)
