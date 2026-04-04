import logging

import numpy as np
import trimesh
from PIL import Image

logger = logging.getLogger(__name__)

try:
    from tsr.system import TSR  # type: ignore[import-untyped]

    _HAS_TSR = True
except ImportError:
    _HAS_TSR = False
    logger.warning(
        "TripoSR (tsr) not installed — running in simulated mode. "
        "Install with:  pip install git+https://github.com/VAST-AI-Research/TripoSR.git"
    )


class MeshGenerator:
    """Wraps TripoSR to turn a single image into a 3-D mesh."""

    def __init__(self, device: str = "cuda:0", chunk_size: int = 8192) -> None:
        self.device = device
        self.chunk_size = chunk_size
        self._model = None
        self._rembg_session = None

    @property
    def is_loaded(self) -> bool:
        return self._model is not None or not _HAS_TSR

    def load(self, model_id: str = "stabilityai/TripoSR") -> None:
        if not _HAS_TSR:
            logger.info("Simulated mode — no model to load")
            return

        import torch  # noqa: F811 — heavy import deferred

        logger.info("Loading TripoSR model '%s' on %s …", model_id, self.device)
        self._model = TSR.from_pretrained(
            model_id,
            config_name="config.yaml",
            weight_name="model.ckpt",
        )
        self._model.renderer.set_chunk_size(self.chunk_size)
        self._model.to(self.device)
        logger.info("TripoSR model loaded")

        try:
            import rembg  # type: ignore[import-untyped]

            self._rembg_session = rembg.new_session()
            logger.info("Background-removal session ready")
        except ImportError:
            logger.warning("rembg not installed — background removal disabled")

    def generate(
        self,
        image: Image.Image,
        *,
        resolution: int = 256,
        remove_bg: bool = True,
    ) -> trimesh.Trimesh:
        if not _HAS_TSR:
            logger.info("Returning simulated mesh (TripoSR not available)")
            return self._simulated_mesh()

        import torch  # noqa: F811

        if remove_bg and self._rembg_session is not None:
            import rembg  # type: ignore[import-untyped]

            image = rembg.remove(image, session=self._rembg_session)

        if image.mode != "RGBA":
            image = image.convert("RGBA")

        with torch.no_grad():
            scene_codes = self._model([image], device=self.device)

        meshes = self._model.extract_mesh(scene_codes, resolution=resolution)
        mesh = meshes[0]
        logger.info(
            "Mesh extracted — %d vertices, %d faces",
            len(mesh.vertices),
            len(mesh.faces),
        )
        return mesh

    @staticmethod
    def _simulated_mesh() -> trimesh.Trimesh:
        """Tetrahedron placeholder used when no ML model is available."""
        vertices = np.array(
            [[0, 0, 0], [1, 0, 0], [0.5, 1, 0], [0.5, 0.5, 1]],
            dtype=np.float64,
        )
        faces = np.array(
            [[0, 1, 2], [0, 2, 3], [0, 3, 1], [1, 3, 2]],
            dtype=np.int64,
        )
        return trimesh.Trimesh(vertices=vertices, faces=faces)
