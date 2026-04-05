from pydantic import BaseModel


class HealthResponse(BaseModel):
    status: str
    model_loaded: bool
    simulated: bool
    tsr_error: str | None = None
