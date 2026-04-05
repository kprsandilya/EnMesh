from typing import Literal

from pydantic import BaseModel, Field


class HealthResponse(BaseModel):
    status: str
    model_loaded: bool
    simulated: bool
    tsr_error: str | None = None


PlacementCategory = Literal["Anchor", "Support", "Fill"]


class GenerateMeshResponse(BaseModel):
    """Response after mesh generation and write to ``outputs/``."""

    mesh_path: str = Field(
        ...,
        description="Absolute filesystem path to the saved mesh on the server.",
    )
    category: PlacementCategory = Field(
        ...,
        description="Keyword-derived category from the original upload filename.",
    )
    download_path: str = Field(
        ...,
        description="URL path (e.g. /output-files/...) to fetch the same file over HTTP.",
    )
