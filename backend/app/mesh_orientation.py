"""View alignment for meshes from TripoSR-style generators."""

from __future__ import annotations

import numpy as np
import trimesh
from trimesh.transformations import rotation_matrix


def apply_triposr_gradio_view_alignment(mesh: trimesh.Trimesh) -> trimesh.Trimesh:
    """
    Same rigid transform as TripoSR's ``to_gradio_3d_orientation`` (gradio_app.py):
    aligns the extracted mesh with the conditioning image / viewer convention used upstream.
    """
    m = mesh.copy()
    m.apply_transform(rotation_matrix(-np.pi / 2, [1, 0, 0]))
    m.apply_transform(rotation_matrix(np.pi / 2, [0, 1, 0]))
    return m
