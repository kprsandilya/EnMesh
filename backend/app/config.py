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

EMBEDDING_MODEL_ID: str = get(
    "EMBEDDING_MODEL",
    "sentence-transformers/all-MiniLM-L6-v2",
)

# Largest AABB edge after uniform scale (world units). 0 = keep model scale (relative sizing).
def _mesh_normalize_max_extent() -> float:
    raw = get("MESH_NORMALIZE_MAX_EXTENT", "0").strip()
    try:
        return float(raw)
    except ValueError:
        return 0.0


MESH_NORMALIZE_MAX_EXTENT: float = _mesh_normalize_max_extent()
MESH_NORMALIZE_CENTER: bool = get("MESH_NORMALIZE_CENTER", "true").lower() in (
    "1",
    "true",
    "yes",
)

# TripoSR: same rotations as upstream gradio export (mesh vs input image).
ALIGN_TRIPOSR_VIEW: bool = get("ALIGN_TRIPOSR_VIEW", "true").lower() in (
    "1",
    "true",
    "yes",
)

# JPEG/TIFF: transpose pixels to match how viewers show the image (before inference).
APPLY_EXIF_ORIENTATION: bool = get("APPLY_EXIF_ORIENTATION", "true").lower() in (
    "1",
    "true",
    "yes",
)


def _default_mesh_scale() -> float:
    raw = get("DEFAULT_MESH_SCALE", "1.0").strip()
    try:
        v = float(raw)
    except ValueError:
        return 1.0
    return v if v > 0 else 1.0


DEFAULT_MESH_SCALE: float = _default_mesh_scale()
