import logging
import sys
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import FastAPI, Request

from app.generation import run_simulated_pipeline
from app.schemas import GenerateRequest, GenerateResponse

LOG_FORMAT = "%(asctime)s | %(levelname)s | %(name)s | %(message)s"


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
    logging.getLogger(__name__).info("EnMesh API starting")
    yield
    logging.getLogger(__name__).info("EnMesh API shutting down")


app = FastAPI(title="EnMesh", lifespan=lifespan)


@app.middleware("http")
async def log_requests(request: Request, call_next):
    logger = logging.getLogger("enmesh.request")
    logger.info("%s %s", request.method, request.url.path)
    response = await call_next(request)
    logger.info("%s %s -> %s", request.method, request.url.path, response.status_code)
    return response


@app.post("/generate", response_model=GenerateResponse)
def generate(body: GenerateRequest) -> GenerateResponse:
    log = logging.getLogger(__name__)
    log.info(
        "POST /generate prompt=%r image_path=%r",
        body.prompt[:120] + ("…" if len(body.prompt) > 120 else ""),
        body.image_path,
    )
    mesh_path = run_simulated_pipeline(body.prompt, body.image_path)
    return GenerateResponse(mesh_path=str(mesh_path))


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("app.main:app", host="0.0.0.0", port=8000, reload=True)
