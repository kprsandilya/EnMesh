import os
from pathlib import Path


def get(key: str, default: str = "") -> str:
    return os.environ.get(f"ENMESH_{key}", default)


BACKEND_ROOT: Path = Path(__file__).resolve().parent.parent
OUTPUTS_DIR: Path = BACKEND_ROOT / "outputs"

DEVICE: str = get("DEVICE", "cpu")
MODEL_ID: str = get("MODEL_ID", "stabilityai/TripoSR")
CHUNK_SIZE: int = int(get("CHUNK_SIZE", "4096"))
DEFAULT_RESOLUTION: int = int(get("RESOLUTION", "128"))

# Local sentence-transformers model for placement classification (no external API).
EMBEDDING_MODEL_ID: str = get(
    "EMBEDDING_MODEL",
    "sentence-transformers/all-MiniLM-L6-v2",
)
