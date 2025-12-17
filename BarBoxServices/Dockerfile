# Use official Python 3.13 slim image
FROM python:3.13-slim

# Install uv package manager
RUN pip install uv

# Set working directory
WORKDIR /app

# Copy project files
COPY pyproject.toml uv.lock ./
COPY src/ ./src/

# Install dependencies
RUN uv venv .venv && \
    . .venv/bin/activate && \
    uv pip install .

# Expose port 8080 (Fly.io standard)
EXPOSE 8080

# Set environment variables
ENV PYTHONUNBUFFERED=1
ENV ENV=prod

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD python -c "import urllib.request; urllib.request.urlopen('http://localhost:8080/alive').read()"

# Run FastAPI with uvicorn
CMD [".venv/bin/uvicorn", "bxctl.web.main:app", "--host", "0.0.0.0", "--port", "8080"]
