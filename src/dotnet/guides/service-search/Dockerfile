# syntax=docker/dockerfile:1
# Build from src/dotnet:  docker build -f guides/service-search/Dockerfile -t wayfinder/service-search .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so the layer is cached until a dependency changes.
COPY Directory.Build.props ./
COPY connector-llm/Llm.Core/Llm.Core.csproj connector-llm/Llm.Core/
COPY connector-llm/Llm.Infrastructure/Llm.Infrastructure.csproj connector-llm/Llm.Infrastructure/
COPY domains-shared/guides/DomainModels.Guides/DomainModels.Guides.csproj domains-shared/guides/DomainModels.Guides/
COPY guides/db-entities-guides/GuidesDbEntities/GuidesDbEntities.csproj guides/db-entities-guides/GuidesDbEntities/
COPY guides/service-search/SearchService/SearchService.csproj guides/service-search/SearchService/
RUN dotnet restore guides/service-search/SearchService/SearchService.csproj

COPY connector-llm/Llm.Core/ connector-llm/Llm.Core/
COPY connector-llm/Llm.Infrastructure/ connector-llm/Llm.Infrastructure/
COPY domains-shared/guides/DomainModels.Guides/ domains-shared/guides/DomainModels.Guides/
COPY guides/db-entities-guides/GuidesDbEntities/ guides/db-entities-guides/GuidesDbEntities/
COPY guides/service-search/SearchService/ guides/service-search/SearchService/
RUN dotnet publish guides/service-search/SearchService/SearchService.csproj \
    --configuration Release --no-restore --output /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080

# The base image defines a non-root user; never run as root.
USER $APP_UID
ENTRYPOINT ["dotnet", "SearchService.dll"]
