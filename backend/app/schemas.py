from pydantic import BaseModel, Field


class GenerateRequest(BaseModel):
    prompt: str = Field(..., min_length=1, description="Text prompt for mesh generation")
    image_path: str | None = Field(
        default=None,
        description="Optional path to a reference image used by the pipeline",
    )


class GenerateResponse(BaseModel):
    mesh_path: str = Field(..., description="Absolute filesystem path to the generated .obj file")
