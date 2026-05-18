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

USER appuser
ENTRYPOINT ["dotnet", "RookHub.Api.dll"]
