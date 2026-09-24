# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/TaskFlow.DbMigrator/TaskFlow.DbMigrator.csproj --locked-mode
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
