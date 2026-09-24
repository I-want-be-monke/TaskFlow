#!/bin/sh
set -eu

certificate=/tmp/taskflow-tls.crt
private_key=/tmp/taskflow-tls.key

if [ ! -s "$certificate" ] || [ ! -s "$private_key" ]; then
    openssl req \
        -x509 \
        -newkey rsa:2048 \
        -sha256 \
        -days 30 \
        -nodes \
        -keyout "$private_key" \
        -out "$certificate" \
        -subj "/CN=localhost" \
        -addext "subjectAltName=DNS:localhost,IP:127.0.0.1" \
        >/dev/null 2>&1
    chmod 600 "$private_key"
fi

exec nginx -g 'daemon off;'
