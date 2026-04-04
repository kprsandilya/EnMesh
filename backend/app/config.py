import os


def get(key: str, default: str = "") -> str:
    return os.environ.get(f"ENMESH_{key}", default)


DEVICE: str = get("DEVICE", "cuda:0")
MODEL_ID: str = get("MODEL_ID", "stabilityai/TripoSR")
CHUNK_SIZE: int = int(get("CHUNK_SIZE", "8192"))
DEFAULT_RESOLUTION: int = int(get("RESOLUTION", "256"))
