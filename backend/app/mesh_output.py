"""Write generated meshes to ``outputs/`` with safe, unique names."""

from __future__ import annotations

import logging
import uuid
from pathlib import Path
from urllib.parse import quote

import trimesh

from app.placement_labels import ANCHOR, FILL, SUPPORT
from app.filename_safe import sanitize_filename_component

logger = logging.getLogger(__name__)

_VALID_CATEGORIES = frozenset({ANCHOR, SUPPORT, FILL})


def unique_mesh_path(
    outputs_dir: Path,
    *,
    safe_stem: str,
    category: str,
    extension: str,
) -> Path:
    """Build ``{stem}_{category}.{ext}``, adding a short suffix if the file exists."""
    if category not in _VALID_CATEGORIES:
        category = FILL

    ext = extension.lstrip(".").lower()
    base = f"{safe_stem}_{category}"
    name = f"{base}.{ext}"
    path = outputs_dir / name
    if path.exists():
        suffix = uuid.uuid4().hex[:8]
        name = f"{base}_{suffix}.{ext}"
        path = outputs_dir / name
    return path


def save_trimesh_mesh(
    mesh: trimesh.Trimesh,
    outputs_dir: Path,
    *,
    original_stem: str,
    category: str,
    file_type: str,
) -> tuple[Path, str]:
    """
    Persist mesh under ``outputs_dir``. Returns ``(absolute_path, download_url_path)``.
    ``download_url_path`` is ``/output-files/<quoted-basename>`` for StaticFiles mount.
    """
    outputs_dir.mkdir(parents=True, exist_ok=True)
    safe_stem = sanitize_filename_component(original_stem)
    path = unique_mesh_path(
        outputs_dir, safe_stem=safe_stem, category=category, extension=file_type
    )

    mesh.export(str(path), file_type=file_type)
    logger.info("Saved mesh to %s (category=%s)", path, category)

    quoted = quote(path.name, safe="")
    download_path = f"/output-files/{quoted}"
    return path.resolve(), download_path
