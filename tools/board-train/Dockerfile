# Trainingsdienst fürs Brettfeld-Netz der RookHub-E-Books (DGX Spark: arm64 + Blackwell-GPU).
# Schlank statt NGC-Image: PyTorch-Räder mit CUDA 13 für arm64 (sbsa) — die GPU-Treiber kommen vom Host (NVIDIA Container Toolkit).
FROM python:3.12-slim
RUN pip install --no-cache-dir torch==2.9.0 --index-url https://download.pytorch.org/whl/cu130 \
 && pip install --no-cache-dir numpy==2.3.3 onnx==1.19.0
WORKDIR /app
COPY train.py server.py /app/
ENV WORK_DIR=/work PORT=8000
EXPOSE 8000
CMD ["python", "/app/server.py"]
