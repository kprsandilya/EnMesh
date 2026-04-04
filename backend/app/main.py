import asyncio
import io
import logging
import sys
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import FastAPI, File, HTTPException, Query, Request, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import Response
from PIL import Image

from app import config
from app.generation import MeshGenerator
from app.schemas import HealthResponse

LOG_FORMAT = "%(asctime)s | %(levelname)s | %(name)s | %(message)s"

generator = MeshGenerator(device=config.DEVICE, chunk_size=config.CHUNK_SIZE)


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
    log.info("EnMesh API starting — loading model …")
    generator.load(config.MODEL_ID)
    log.info("EnMesh API ready")
    yield
    log.info("EnMesh API shutting down")


app = FastAPI(title="EnMesh", version="0.2.0", lifespan=lifespan)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.middleware("http")
async def log_requests(request: Request, call_next):
    logger = logging.getLogger("enmesh.http")
    logger.info("%s %s", request.method, request.url.path)
    response = await call_next(request)
    logger.info("%s %s -> %s", request.method, request.url.path, response.status_code)
    return response


@app.post("/generate")
async def generate(
    image: UploadFile = File(..., description="Input image (PNG, JPG, etc.)"),
    format: str = Query("obj", pattern="^(obj|glb)$"),
    resolution: int = Query(config.DEFAULT_RESOLUTION, ge=64, le=512),
    remove_bg: bool = Query(True),
) -> Response:
    if not generator.is_loaded:
        raise HTTPException(503, detail="Model is still loading — try again shortly.")

    raw = await image.read()
    try:
        pil_image = Image.open(io.BytesIO(raw))
    except Exception as exc:
        raise HTTPException(400, detail=f"Could not decode image: {exc}") from exc

    loop = asyncio.get_running_loop()
    mesh = await loop.run_in_executor(
        None,
        lambda: generator.generate(
            pil_image, resolution=resolution, remove_bg=remove_bg
        ),
    )

    buf = io.BytesIO()
    if format == "glb":
        mesh.export(buf, file_type="glb")
        media = "model/gltf-binary"
    else:
        mesh.export(buf, file_type="obj")
        media = "application/octet-stream"

    buf.seek(0)
    return Response(
        content=buf.read(),
        media_type=media,
        headers={"Content-Disposition": f'attachment; filename="mesh.{format}"'},
    )


@app.get("/health", response_model=HealthResponse)
def health() -> HealthResponse:
    return HealthResponse(status="ok", model_loaded=generator.is_loaded)


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("app.main:app", host="0.0.0.0", port=8000, reload=True)
