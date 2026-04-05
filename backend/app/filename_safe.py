"""Cross-platform safe filenames from upload names."""

import re
import unicodedata
from pathlib import Path

_ILLEGAL = re.compile(r'[<>:"/\\|?*\x00-\x1f]')
_UNDERSCORE_RUNS = re.compile(r"_+")
_TRAILING_DOT_SPACE = re.compile(r"[.\s]+$")


def stem_from_upload(filename: str) -> str:
    if not filename or not filename.strip():
        return "upload"
    return Path(filename.strip()).stem.strip() or "upload"


def sanitize_stem_for_storage(stem: str, *, max_len: int = 80) -> str:
    if not stem:
        return "mesh"
    norm = unicodedata.normalize("NFKC", stem)
    norm = norm.replace("\u00a0", " ")
    norm = _ILLEGAL.sub("_", norm)
    norm = norm.replace(" ", "_")
    norm = "".join(c if c.isalnum() or c in "._-" else "_" for c in norm)
    norm = _UNDERSCORE_RUNS.sub("_", norm).strip("._")
    norm = _TRAILING_DOT_SPACE.sub("", norm)
    if not norm or norm in {".", ".."}:
        norm = "mesh"
    if len(norm) > max_len:
        norm = norm[:max_len].rstrip("._")
    return norm or "mesh"


def unique_mesh_basename(safe_stem: str, suffix_hex: str) -> str:
    return f"{safe_stem}_{suffix_hex}"
