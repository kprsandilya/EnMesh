"""
Cross-platform safe filename components (no path separators, no reserved Windows names).
"""

from __future__ import annotations

import re
from pathlib import Path
from typing import Final

# Windows reserved device names (without extension).
_RESERVED_WINDOWS: Final[frozenset[str]] = frozenset(
    {
        "con",
        "prn",
        "aux",
        "nul",
        *(f"com{i}" for i in range(1, 10)),
        *(f"lpt{i}" for i in range(1, 10)),
    }
)

_MAX_COMPONENT_LEN: Final[int] = 180


def stem_from_upload_filename(filename: str | None) -> str:
    """Original basename without extension; empty string if missing."""
    if not filename or not str(filename).strip():
        return ""
    return Path(filename).stem


def sanitize_filename_component(name: str, *, fallback: str = "mesh") -> str:
    """
    Single path component safe on Windows, macOS, and Linux.
    No '/', '\\', ':', etc.; no leading/trailing dots or spaces.
    """
    if not name or not str(name).strip():
        return fallback

    # Replace path separators and illegal characters.
    s = str(name).strip()
    s = re.sub(r'[<>:"/\\|?*\x00-\x1f]', "_", s)
    s = re.sub(r"\s+", "_", s)
    s = re.sub(r"_+", "_", s).strip("._ ")

    if not s:
        s = fallback

    base_lower = s.lower()
    if base_lower in _RESERVED_WINDOWS:
        s = f"_{s}"

    if len(s) > _MAX_COMPONENT_LEN:
        s = s[:_MAX_COMPONENT_LEN].rstrip("._ ")

    return s or fallback
