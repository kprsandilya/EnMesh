"""Save generated meshes under outputs/ — no category in the filename."""

from __future__ import annotations

import secrets
from pathlib import Path

import trimesh

from .filename_safe import sanitize_stem_for_storage, stem_from_upload, unique_mesh_basename


def _pick_unique_basename(outputs_dir: Path, safe_stem: str, ext: str) -> str:
    for _ in range(64):
        candidate = unique_mesh_basename(safe_stem, secrets.token_hex(4))
        if not (outputs_dir / f"{candidate}.{ext}").exists():
            return candidate
    raise RuntimeError("Could not allocate a unique mesh filename")


def save_generated_mesh(
    mesh: trimesh.Trimesh,
    outputs_dir: Path,
    *,
    upload_filename: str,
    file_type: str,
) -> tuple[Path, str]:
    outputs_dir.mkdir(parents=True, exist_ok=True)
    ext = "glb" if file_type.lower() == "glb" else "obj"
    safe = sanitize_stem_for_storage(stem_from_upload(upload_filename))
    base = _pick_unique_basename(outputs_dir, safe, ext)
    out = (outputs_dir / f"{base}.{ext}").resolve()
    mesh.export(str(out), file_type=ext)
    return out, base
