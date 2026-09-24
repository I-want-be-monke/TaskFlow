# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/TaskFlow.Client/TaskFlow.Client.csproj --locked-mode
RUN dotnet publish src/TaskFlow.Client/TaskFlow.Client.csproj \
    --configuration Release \
    --no-restore \
    --output /out \
    /p:UseAppHost=false

FROM nginxinc/nginx-unprivileged:1.31.6-alpine3.24 AS runtime
ARG TASKFLOW_RELEASE_ID=dev
USER root
RUN apk add --no-cache openssl \
    && rm -f /etc/nginx/conf.d/default.conf
COPY deploy/nginx/default.conf.template /etc/nginx/conf.d/default.conf
COPY deploy/nginx/taskflow-nginx-entrypoint.sh /usr/local/bin/taskflow-nginx-entrypoint.sh
RUN sed -i "s/__TASKFLOW_SERVICE_VERSION__/${TASKFLOW_RELEASE_ID}/g" /etc/nginx/conf.d/default.conf \
    && chmod 0555 /usr/local/bin/taskflow-nginx-entrypoint.sh
COPY --from=build --chown=101:101 /out/wwwroot/ /usr/share/nginx/html/
USER 101
EXPOSE 8080 8443
ENTRYPOINT ["/usr/local/bin/taskflow-nginx-entrypoint.sh"]
