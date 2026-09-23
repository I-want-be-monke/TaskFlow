# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src

# Keep NuGet restore cacheable when only C# source files change.
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/TaskFlow.Domain/TaskFlow.Domain.csproj", "src/TaskFlow.Domain/packages.lock.json", "src/TaskFlow.Domain/"]
COPY ["src/TaskFlow.Application/TaskFlow.Application.csproj", "src/TaskFlow.Application/packages.lock.json", "src/TaskFlow.Application/"]
COPY ["src/TaskFlow.Infrastructure/TaskFlow.Infrastructure.csproj", "src/TaskFlow.Infrastructure/packages.lock.json", "src/TaskFlow.Infrastructure/"]
COPY ["src/TaskFlow.DbMigrator/TaskFlow.DbMigrator.csproj", "src/TaskFlow.DbMigrator/packages.lock.json", "src/TaskFlow.DbMigrator/"]
RUN dotnet restore src/TaskFlow.DbMigrator/TaskFlow.DbMigrator.csproj --locked-mode

COPY . .
RUN dotnet publish src/TaskFlow.DbMigrator/TaskFlow.DbMigrator.csproj \
    --configuration Release \
    --no-restore \
    --output /out \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS runtime
WORKDIR /app
COPY --from=build --chown=app:app /out/ ./
ENV DOTNET_EnableDiagnostics=0
USER app
ENTRYPOINT ["dotnet", "TaskFlow.DbMigrator.dll"]
