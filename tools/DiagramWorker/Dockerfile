# Externer Diagramm-Worker (0.873.0) → ghcr.io/<owner>/rookhub-diagram-worker (amd64 + arm64/DGX Spark).
# Bau-Kontext = Repo-Wurzel (DiagramCli verweist auf das API-Projekt):
#   docker build -f tools/DiagramWorker/Dockerfile --build-arg VERSION=<APP_VERSION> -t rookhub-diagram-worker .
# Was mitkommt, regelt Dockerfile.dockerignore daneben (nur API + DiagramCli + Worker).

# Gebaut wird auf der Plattform des Bau-Rechners: das Programm ist IL (ohne App-Host) und bringt die nativen Teile
# (SkiaSharp, ONNX Runtime) für alle Linux-Architekturen unter runtimes/ mit — kein .NET-SDK unter QEMU.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG VERSION=0.0.0
WORKDIR /src
COPY src/api/RookHub.Api/RookHub.Api.csproj src/api/RookHub.Api/
COPY tools/DiagramCli/DiagramCli.csproj tools/DiagramCli/
RUN dotnet restore tools/DiagramCli/DiagramCli.csproj
COPY src/api/RookHub.Api/ src/api/RookHub.Api/
COPY tools/DiagramCli/ tools/DiagramCli/
RUN dotnet publish tools/DiagramCli -c Release --no-restore -p:UseAppHost=false -p:Version=$VERSION -o /out

# aspnet statt runtime: über den Projektverweis hängt das Programm am ASP.NET-Rahmen der API.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
# poppler-utils: pdftoppm/pdfinfo für Scans; python3-requests: der Worker selbst; numpy/scipy/PIL: Brett-Ausschneiden (0.874.0).
RUN apt-get update \
    && apt-get install -y --no-install-recommends poppler-utils python3 python3-requests python3-numpy python3-scipy python3-pil \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --no-create-home worker
WORKDIR /app
COPY --from=build /out cli/
COPY tools/DiagramWorker/worker.py .
COPY tools/DiagramWorker/boardcut/ boardcut/
RUN printf '#!/bin/sh\nexec dotnet /app/cli/rookhub-diagrams.dll "$@"\n' > cli/rookhub-diagrams && chmod +x cli/rookhub-diagrams
USER worker
ENTRYPOINT ["python3", "-u", "/app/worker.py"]
