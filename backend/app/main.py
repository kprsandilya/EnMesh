import asyncio
import io
import logging
import sys
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from urllib.parse import quote

from fastapi import FastAPI, File, HTTPException, Query, Request, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from PIL import Image, ImageOps
from starlette.staticfiles import StaticFiles

from app import config
from app.classification import AutoLayoutClassifierService, SentenceEmbeddingRuntime
from app.generation import MeshGenerator
from app.filename_safe import sanitize_stem_for_storage, stem_from_upload
from app.mesh_normalize import apply_uniform_extent
from app.mesh_orientation import apply_triposr_gradio_view_alignment
from app.mesh_output import save_generated_mesh
from app.schemas import (
    AutoLayoutMeshOut,
    AutoLayoutRequest,
    AutoLayoutResponse,
    GenerateMeshResponse,
    HealthResponse,
)

# StaticFiles validates this directory at mount time (before lifespan runs).
config.OUTPUTS_DIR.mkdir(parents=True, exist_ok=True)

LOG_FORMAT = "%(asctime)s | %(levelname)s | %(name)s | %(message)s"

generator = MeshGenerator(device=config.DEVICE, chunk_size=config.CHUNK_SIZE)
_embedding_runtime = SentenceEmbeddingRuntime(config.EMBEDDING_MODEL_ID)
classifier = AutoLayoutClassifierService(_embedding_runtime)


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
    _embedding_runtime.load()
    classifier.init_category_embeddings()
    log.info("EnMesh API — loading mesh generator …")
    generator.load(config.MODEL_ID)
    log.info("EnMesh API ready")
    yield
    log.info("EnMesh API shutting down")


app = FastAPI(title="EnMesh", version="0.5.0", lifespan=lifespan)

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
    max_extent: float | None = Query(
        None,
        ge=0,
        le=500,
        description=(
            "Uniform scale: longest AABB edge becomes this (world units); "
            "makes every asset the same max size. 0 = off (preserve relative scale). "
            "Omit = ENMESH_MESH_NORMALIZE_MAX_EXTENT (default 0)."
        ),
    ),
    scale: float | None = Query(
        None,
        gt=0,
        le=1e6,
        description=(
            "Uniform multiplier after centering / max_extent (relative sizing vs that result). "
            "Omit = ENMESH_DEFAULT_MESH_SCALE (default 1)."
        ),
    ),
    align_view: bool | None = Query(
        None,
        description=(
            "Apply TripoSR Gradio view rotation so the mesh matches the conditioning image "
            "convention. Omit = ENMESH_ALIGN_TRIPOSR_VIEW."
        ),
    ),
) -> GenerateMeshResponse:
    if not generator.is_ready:
        raise HTTPException(503, detail="Model is still loading — try again shortly.")

    raw = await image.read()
    try:
        pil_image = Image.open(io.BytesIO(raw))
    except Exception as exc:
        raise HTTPException(400, detail=f"Could not decode image: {exc}") from exc

    if config.APPLY_EXIF_ORIENTATION:
        pil_image = ImageOps.exif_transpose(pil_image)

    filename = image.filename or "upload.png"
    source_stem = sanitize_stem_for_storage(stem_from_upload(filename))
    loop = asyncio.get_running_loop()
    mesh = await loop.run_in_executor(
        None,
        lambda: generator.generate(
            pil_image, resolution=resolution, remove_bg=remove_bg
        ),
    )

    extent = (
        config.MESH_NORMALIZE_MAX_EXTENT if max_extent is None else float(max_extent)
    )
    center = config.MESH_NORMALIZE_CENTER
    do_align = config.ALIGN_TRIPOSR_VIEW if align_view is None else align_view
    scale_f = config.DEFAULT_MESH_SCALE if scale is None else float(scale)

    def _prepare_and_save():
        m = (
            apply_triposr_gradio_view_alignment(mesh)
            if do_align
            else mesh.copy()
        )
        m = apply_uniform_extent(m, extent, center=center)
        if scale_f != 1.0:
            m.apply_scale(scale_f)
        return save_generated_mesh(
            m,
            config.OUTPUTS_DIR,
            upload_filename=filename,
            file_type=format,
        )

    abs_path, mesh_name = await loop.run_in_executor(None, _prepare_and_save)
    ext = "glb" if format.lower() == "glb" else "obj"
    fname = f"{mesh_name}.{ext}"
    download_url = f"/output-files/{quote(fname)}"
    return GenerateMeshResponse(
        mesh_path=str(abs_path),
        mesh_name=mesh_name,
        source_stem=source_stem,
        download_url=download_url,
    )


@app.post("/auto-layout", response_model=AutoLayoutResponse)
async def auto_layout(body: AutoLayoutRequest) -> AutoLayoutResponse:
    if not classifier.category_embeddings_ready:
        raise HTTPException(503, detail="Classifier is not ready.")
    if not body.meshes:
        return AutoLayoutResponse(results=[])

    names = [m.name.strip() for m in body.meshes]
    if any(not n for n in names):
        raise HTTPException(400, detail="Each mesh must have a non-empty name.")

    loop = asyncio.get_running_loop()
    classified = await loop.run_in_executor(
        None,
        lambda: classifier.classify_meshes(names, config.OUTPUTS_DIR),
    )
    results = [
        AutoLayoutMeshOut(
            mesh_name=c.mesh_name,
            absolute_path=c.absolute_path,
            category=c.category.value,
        )
        for c in classified
    ]
    return AutoLayoutResponse(results=results)


@app.get("/health", response_model=HealthResponse)
def health() -> HealthResponse:
    return HealthResponse(
        status="ok",
        model_loaded=generator.is_real,
        simulated=not generator.is_real,
        tsr_error=generator.tsr_import_error,
        classifier_loaded=classifier.category_embeddings_ready
        and _embedding_runtime.is_loaded,
    )


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("app.main:app", host="0.0.0.0", port=8000, reload=True)
