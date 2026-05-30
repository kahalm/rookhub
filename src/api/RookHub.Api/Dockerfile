FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY RookHub.Api.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0

RUN adduser --disabled-password --no-create-home appuser

WORKDIR /app
COPY --from=build /app .
EXPOSE 8080

# DataProtection-Keyring auf gemountetem Volume (/keys) ablegen; Verzeichnis muss
# dem non-root appuser gehoeren, damit die Keys schreibbar sind.
RUN mkdir -p /keys && chown appuser /keys

USER appuser
ENTRYPOINT ["dotnet", "RookHub.Api.dll"]
