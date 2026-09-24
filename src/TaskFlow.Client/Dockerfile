# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src

# .NET 10 Blazor restore must see Razor sources so internal asset references match the lock file.
# Shared contracts are copied explicitly because the client project references them.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY ["src/TaskFlow.Contracts/TaskFlow.Contracts.csproj", "src/TaskFlow.Contracts/packages.lock.json", "src/TaskFlow.Contracts/"]
COPY src/TaskFlow.Client/ src/TaskFlow.Client/

RUN dotnet restore src/TaskFlow.Client/TaskFlow.Client.csproj --locked-mode

COPY . .

RUN dotnet publish src/TaskFlow.Client/TaskFlow.Client.csproj \
    --configuration Release \
    --no-restore \
    --output /out

FROM nginxinc/nginx-unprivileged:1.31.6-alpine3.24 AS runtime
ARG TASKFLOW_RELEASE_ID=dev
USER root
RUN apk add --no-cache openssl curl \
    && rm -f /etc/nginx/conf.d/default.conf \
    && mkdir -p /etc/nginx/snippets
COPY deploy/nginx/default.conf.template /etc/nginx/conf.d/default.conf
COPY deploy/nginx/security-headers.conf /etc/nginx/snippets/taskflow-security-headers.conf
RUN sed -i "s/__TASKFLOW_SERVICE_VERSION__/${TASKFLOW_RELEASE_ID}/g" /etc/nginx/conf.d/default.conf \
    && openssl req -x509 -newkey rsa:2048 -sha256 -days 1 -nodes \
        -keyout /tmp/taskflow-tls.key -out /tmp/taskflow-tls.crt \
        -subj /CN=localhost -addext subjectAltName=DNS:localhost,IP:127.0.0.1 >/dev/null 2>&1 \
    && nginx -t \
    && rm -f /tmp/taskflow-tls.crt /tmp/taskflow-tls.key
COPY --from=build --chown=101:101 /out/wwwroot/ /usr/share/nginx/html/
USER 101
EXPOSE 8080 8443
ENTRYPOINT ["/bin/sh", "-c", "certificate=/tmp/taskflow-tls.crt; private_key=/tmp/taskflow-tls.key; if [ ! -s \"$certificate\" ] || [ ! -s \"$private_key\" ]; then openssl req -x509 -newkey rsa:2048 -sha256 -days 30 -nodes -keyout \"$private_key\" -out \"$certificate\" -subj /CN=localhost -addext subjectAltName=DNS:localhost,IP:127.0.0.1 >/dev/null 2>&1; chmod 600 \"$private_key\"; fi; exec nginx -g 'daemon off;'"]
