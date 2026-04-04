import logging
import time
import uuid
from pathlib import Path

logger = logging.getLogger(__name__)

BACKEND_ROOT = Path(__file__).resolve().parent.parent
OUTPUT_DIR = BACKEND_ROOT / "outputs"


def _dummy_obj_content(prompt: str, image_path: str | None) -> str:
    safe_prompt = prompt.replace("\n", " ").strip()[:200]
    img_note = image_path if image_path else "(none)"
    return f"""# EnMesh simulated pipeline output
# prompt: {safe_prompt}
# image_path: {img_note}
o enmesh_dummy
v 0.0 0.0 0.0
v 1.0 0.0 0.0
v 0.0 1.0 0.0
v 0.0 0.0 1.0
f 1 2 3
f 1 3 4
f 1 4 2
f 2 4 3
"""


def run_simulated_pipeline(prompt: str, image_path: str | None) -> Path:
    """
    Simulate a 3D generation pipeline and write a minimal valid Wavefront OBJ.
    Returns the absolute path to the written file.
    """
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    mesh_id = uuid.uuid4().hex[:12]
    out_path = OUTPUT_DIR / f"mesh_{mesh_id}.obj"

    logger.info("Pipeline: preprocessing (prompt length=%d)", len(prompt))
    time.sleep(0.05)
    if image_path:
        logger.info("Pipeline: conditioning on image_path=%s", image_path)
        time.sleep(0.05)
    logger.info("Pipeline: neural inference (simulated)")
    time.sleep(0.05)
    logger.info("Pipeline: mesh extraction & cleanup")

    out_path.write_text(_dummy_obj_content(prompt, image_path), encoding="utf-8")
    logger.info("Pipeline: wrote %s", out_path)
    return out_path.resolve()
