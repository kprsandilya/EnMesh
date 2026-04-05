"""Optional centering and uniform max-extent scaling.

Setting a positive ``target_max_extent`` forces every mesh into the same bounding-box
size (good for uniform game props, bad for real-world relative scale). Use ``<= 0``
to keep the generator's scale; centering alone can still run for a stable pivot.
"""

from __future__ import annotations

import logging

import numpy as np
import trimesh

logger = logging.getLogger(__name__)


def apply_uniform_extent(
    mesh: trimesh.Trimesh,
    target_max_extent: float,
    *,
    center: bool = True,
) -> trimesh.Trimesh:
    """
    Scale uniformly so the axis-aligned bounding box's longest edge equals ``target_max_extent``.

    If ``center`` is True, translate so the centroid is at the origin before scaling
    (stable pivot for Unity spawns at local zero).

    ``target_max_extent <= 0`` skips scaling. If ``center`` is still True, only the
    centroid translation is applied (scale unchanged).
    """
    if target_max_extent <= 0:
        if not center:
            return mesh
        m = mesh.copy()
        c = m.centroid
        m.vertices = np.asarray(m.vertices, dtype=np.float64) - c
        return m

    m = mesh.copy()
    if center:
        c = m.centroid
        m.vertices = np.asarray(m.vertices, dtype=np.float64) - c

    bounds = m.bounds
    ext = bounds[1] - bounds[0]
    max_e = float(np.max(ext))
    if max_e < 1e-12:
        logger.warning("Mesh has near-zero extent; skipping scale normalization")
        return m

    scale = target_max_extent / max_e
    m.apply_scale(scale)
    return m
