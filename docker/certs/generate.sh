#!/usr/bin/env bash
set -Eeuo pipefail

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cert="$script_dir/tls.pem"
key="$script_dir/tls.key"

# Generated development outputs are gitignored; never use this certificate in production.
if [[ -f "$cert" && -f "$key" ]]; then
  exit 0
fi

openssl req -x509 -nodes -newkey rsa:2048 -days 365 \
  -keyout "$key" \
  -out "$cert" \
  -subj "/CN=localhost" \
  -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"
chmod 600 "$key"
chmod 644 "$cert"
