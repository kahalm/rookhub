FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY RookHub.Api.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0

# curl für den Docker-Healthcheck (GET /health); --no-install-recommends hält das Image schlank.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

RUN adduser --disabled-password --no-create-home appuser

WORKDIR /app
COPY --from=build /app .
EXPOSE 8080

# DataProtection-Keyring auf gemountetem Volume (/keys) ablegen; Verzeichnis muss
# dem non-root appuser gehoeren, damit die Keys schreibbar sind.
RUN mkdir -p /keys && chown appuser /keys

USER appuser
ENTRYPOINT ["dotnet", "RookHub.Api.dll"]
