from pydantic import BaseModel, Field


class GenerateMeshResponse(BaseModel):
    mesh_path: str = Field(description="Absolute path to the saved mesh on the server.")
    mesh_name: str = Field(
        description="Basename without extension; no category; unique under outputs/."
    )
    download_url: str = Field(
        description="Relative URL to GET the mesh bytes (same host as the API)."
    )


class AutoLayoutMeshIn(BaseModel):
    name: str = Field(min_length=1)


class AutoLayoutRequest(BaseModel):
    meshes: list[AutoLayoutMeshIn]


class AutoLayoutMeshOut(BaseModel):
    mesh_name: str
    absolute_path: str = ""
    category: str


class AutoLayoutResponse(BaseModel):
    results: list[AutoLayoutMeshOut]


class HealthResponse(BaseModel):
    status: str
    model_loaded: bool
    simulated: bool
    tsr_error: str | None = None
    classifier_loaded: bool = False
