FROM python:3.11-slim

ENV DEBIAN_FRONTEND=noninteractive

# git: clone TripoSR; build tools: compile torchmcubes C++/CUDA extension (CPU path works without GPU)
RUN apt-get update && \
    apt-get install -y --no-install-recommends git build-essential cmake && \
    rm -rf /var/lib/apt/lists/*

# TripoSR is not pip-installable — upstream expects a repo checkout on PYTHONPATH
RUN git clone --depth 1 https://github.com/VAST-AI-Research/TripoSR.git /opt/TripoSR

ENV PYTHONPATH=/opt/TripoSR

WORKDIR /app

# CPU-only PyTorch first (smaller than CUDA wheels)
RUN pip install --no-cache-dir --upgrade pip setuptools wheel && \
    pip install --no-cache-dir \
    torch torchvision --index-url https://download.pytorch.org/whl/cpu

# TripoSR's own dependency pins (transformers, rembg, torchmcubes from git, etc.)
RUN pip install --no-cache-dir -r /opt/TripoSR/requirements.txt && \
    pip install --no-cache-dir onnxruntime

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY app/ app/

EXPOSE 8000

CMD ["python", "-m", "uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
