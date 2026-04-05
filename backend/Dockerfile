# CPU-only image with TripoSR on PYTHONPATH (no GPU required).
# Order matters: LibTorch CPU wheels before building torchmcubes from source.

FROM python:3.11-slim

ENV DEBIAN_FRONTEND=noninteractive \
    PYTHONPATH=/opt/TripoSR \
    ENMESH_DEVICE=cpu \
    CUDA_VISIBLE_DEVICES="" \
    NVIDIA_VISIBLE_DEVICES=void

# git: clone TripoSR; build-essential + cmake: compile torchmcubes (CPU extension)
RUN apt-get update && apt-get install -y --no-install-recommends \
    git \
    build-essential \
    cmake \
    && rm -rf /var/lib/apt/lists/*

RUN git clone --depth 1 https://github.com/VAST-AI-Research/TripoSR.git /opt/TripoSR

WORKDIR /app

RUN pip install --no-cache-dir --upgrade pip setuptools wheel && \
    pip install --no-cache-dir \
    torch torchvision --index-url https://download.pytorch.org/whl/cpu

COPY requirements-triposr-inference.txt .
RUN pip install --no-cache-dir -r requirements-triposr-inference.txt

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt && \
    pip install --no-cache-dir onnxruntime

# Keep CPU wheels if a dependency tried to swap in a CUDA build
RUN pip install --no-cache-dir \
    torch torchvision --index-url https://download.pytorch.org/whl/cpu

COPY app/ app/

EXPOSE 8000

CMD ["python", "-m", "uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
