FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY RookHub.Api.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0

# curl für den Docker-Healthcheck (GET /health); stockfish für den Tipp-Generator
# (StockfishAnalyzer, nur Import-/Reprocess-Pfad). --no-install-recommends hält das Image schlank.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl stockfish \
    && rm -rf /var/lib/apt/lists/*

# Ubuntu-24.04-Basis (.NET 10) hat kein adduser mehr — useradd nutzen
RUN useradd --no-create-home appuser

WORKDIR /app
COPY --from=build /app .
EXPOSE 8080

# DataProtection-Keyring auf gemountetem Volume (/keys) ablegen; Verzeichnis muss
# dem non-root appuser gehoeren, damit die Keys schreibbar sind.
RUN mkdir -p /keys && chown appuser /keys

# Commit + Ref des Builds (vom CI als --build-arg gesetzt, dasselbe Paar wie beim Frontend). Die API
# meldet damit ihren eigenen Stand an die Admin-CI-Seite (GithubActionsService.OwnApiBuild) — vorher
# kannte die nur den Frontend-Commit. Bewusst am ENDE: alles darueber bleibt je Commit dieselbe Schicht.
ARG GIT_SHA=unknown
ARG GIT_REF=
ENV BUILD_GIT_SHA=$GIT_SHA \
    BUILD_GIT_REF=$GIT_REF

USER appuser
ENTRYPOINT ["dotnet", "RookHub.Api.dll"]
