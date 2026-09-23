# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src

# Restore is intentionally isolated from source-code changes. Only dependency
# descriptors are copied before restore so Docker can reuse this layer.
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/TaskFlow.Contracts/TaskFlow.Contracts.csproj", "src/TaskFlow.Contracts/packages.lock.json", "src/TaskFlow.Contracts/"]
COPY ["src/TaskFlow.Domain/TaskFlow.Domain.csproj", "src/TaskFlow.Domain/packages.lock.json", "src/TaskFlow.Domain/"]
COPY ["src/TaskFlow.Application/TaskFlow.Application.csproj", "src/TaskFlow.Application/packages.lock.json", "src/TaskFlow.Application/"]
COPY ["src/TaskFlow.Infrastructure/TaskFlow.Infrastructure.csproj", "src/TaskFlow.Infrastructure/packages.lock.json", "src/TaskFlow.Infrastructure/"]
COPY ["src/TaskFlow.Api/TaskFlow.Api.csproj", "src/TaskFlow.Api/packages.lock.json", "src/TaskFlow.Api/"]
RUN dotnet restore src/TaskFlow.Api/TaskFlow.Api.csproj --locked-mode

COPY . .
RUN dotnet publish src/TaskFlow.Api/TaskFlow.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /out \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS runtime
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build --chown=app:app /out/ ./
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "TaskFlow.Api.dll"]
