# syntax=docker/dockerfile:1
# Build from src/dotnet:  docker build -f guides/importer-guides/Dockerfile -t wayfinder/importer-guides .
# Run a job:              docker run --rm wayfinder/importer-guides embedChunks
# No argument hosts the HTTP API (health checks) instead of running a job.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY connector-llm/Llm.Core/Llm.Core.csproj connector-llm/Llm.Core/
COPY connector-llm/Llm.Infrastructure/Llm.Infrastructure.csproj connector-llm/Llm.Infrastructure/
COPY domains-shared/guides/DomainModels.Guides/DomainModels.Guides.csproj domains-shared/guides/DomainModels.Guides/
COPY guides/db-entities-guides/GuidesDbEntities/GuidesDbEntities.csproj guides/db-entities-guides/GuidesDbEntities/
COPY guides/importer-guides/GuidesImporter/GuidesImporter.csproj guides/importer-guides/GuidesImporter/
RUN dotnet restore guides/importer-guides/GuidesImporter/GuidesImporter.csproj

COPY connector-llm/Llm.Core/ connector-llm/Llm.Core/
COPY connector-llm/Llm.Infrastructure/ connector-llm/Llm.Infrastructure/
COPY domains-shared/guides/DomainModels.Guides/ domains-shared/guides/DomainModels.Guides/
COPY guides/db-entities-guides/GuidesDbEntities/ guides/db-entities-guides/GuidesDbEntities/
COPY guides/importer-guides/GuidesImporter/ guides/importer-guides/GuidesImporter/
RUN dotnet publish guides/importer-guides/GuidesImporter/GuidesImporter.csproj \
    --configuration Release --no-restore --output /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "GuidesImporter.dll"]
