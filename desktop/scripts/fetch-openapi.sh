#!/usr/bin/env bash
set -Eeuo pipefail

# Dev-only authoring helper: snapshots the LIVE Insights OpenAPI document.
# Requires the dev stack (nginx + identity + insights) to be up.
# Reads dev-user creds from the repo-root .env at runtime; never hardcodes or echoes them.

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CERT="$REPO_ROOT/docker/certs/tls.pem"
ENV_FILE="$REPO_ROOT/.env"
OUT_DIR="$REPO_ROOT/desktop/openapi"
OUT_FILE="$OUT_DIR/insights.openapi.yaml"

if [[ ! -f "$ENV_FILE" ]]; then
  echo "ERROR: $ENV_FILE not found. Copy from .env.example and set IDENTITY__DEVUSER__* creds." >&2
  exit 1
fi
if [[ ! -f "$CERT" ]]; then
  echo "ERROR: TLS cert not found at $CERT." >&2
  exit 1
fi

# Read a KEY from .env without exposing the value on the command line.
read_env() {
  python3 - "$ENV_FILE" "$1" <<'PY'
import sys

path, key = sys.argv[1], sys.argv[2]
value = None
with open(path, encoding="utf-8") as f:
    for line in f:
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        if line.startswith("export "):
            line = line[len("export "):]
        k, _, v = line.partition("=")
        if k.strip() == key:
            v = v.strip()
            if len(v) >= 2 and v[0] == v[-1] and v[0] in "\"'":
                v = v[1:-1]
            value = v
            break
if value is None:
    print(f"ERROR: {key} is not set in {path}", file=sys.stderr)
    sys.exit(1)
print(value)
PY
}

USERNAME="$(read_env IDENTITY__DEVUSER__USERNAME)"
PASSWORD="$(read_env IDENTITY__DEVUSER__PASSWORD)"

# 1. ROPC password grant against live identity (public client, no secret).
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT
TOKEN_FILE="$TMP_DIR/token.json"

if ! curl --cacert "$CERT" -sS -X POST https://localhost/connect/token \
      --data-urlencode grant_type=password \
      --data-urlencode "username=$USERNAME" \
      --data-urlencode "password=$PASSWORD" \
      --data-urlencode "scope=insights.read offline_access" \
      --data-urlencode client_id=tokenburn-desktop \
      -o "$TOKEN_FILE"; then
  echo "ERROR: token request failed (curl)." >&2
  exit 1
fi

TOK="$(python3 -c 'import json, sys
try:
    data = json.load(open(sys.argv[1], encoding="utf-8"))
except Exception as exc:
    print(f"ERROR: token response is not JSON: {exc}", file=sys.stderr)
    sys.exit(2)
tok = data.get("access_token")
if not tok:
    print("ERROR: no access_token in token response: " + str(data.get("error", "unknown")), file=sys.stderr)
    sys.exit(2)
print(tok)' "$TOKEN_FILE")"

# 2. R12: verify the token carries insights.read before fetching the doc.
SCOPES="$(python3 -c 'import base64, json, sys
tok = sys.argv[1]
seg = tok.split(".")[1]
seg += "=" * (-len(seg) % 4)
try:
    payload = json.loads(base64.urlsafe_b64decode(seg))
except Exception as exc:
    print(f"ERROR: cannot decode access token payload: {exc}", file=sys.stderr)
    sys.exit(2)
sc = payload.get("scope", "")
if isinstance(sc, str):
    for s in sc.split():
        print(s)
elif isinstance(sc, list):
    for s in sc:
        print(s)' "$TOK")"

if ! grep -Fxq 'insights.read' <<<"$SCOPES"; then
  echo "ERROR: access token does not carry insights.read scope (R12). Token is useless for the doc fetch." >&2
  exit 1
fi
echo "OK: token carries insights.read (R12 verified)."

# 3. Fetch the OpenAPI document (retry on HTTP 429 / transient failure).
mkdir -p "$OUT_DIR"

fetch_doc() {
  curl --cacert "$CERT" -sS -o "$OUT_FILE" -w '%{http_code}' \
    -H "Authorization: Bearer $TOK" \
    https://localhost/openapi/v1.json 2>>"$TMP_DIR/curl.err" || true
}

HTTP_CODE=""
for attempt in 1 2 3; do
  HTTP_CODE="$(fetch_doc)"
  if [[ "$HTTP_CODE" == "200" ]]; then
    break
  fi
  echo "WARN: doc fetch attempt $attempt returned HTTP ${HTTP_CODE:-<no response>}" >&2
  if [[ "$attempt" -lt 3 ]]; then
    sleep 2
  fi
done

if [[ "$HTTP_CODE" != "200" ]]; then
  echo "ERROR: could not fetch the OpenAPI document after 3 attempts." >&2
  if [[ -s "$TMP_DIR/curl.err" ]]; then
    cat "$TMP_DIR/curl.err" >&2
  fi
  exit 1
fi

# 4. Completeness check at fetch time.
python3 - "$OUT_FILE" <<'PY'
import json, sys

with open(sys.argv[1], encoding="utf-8") as f:
    spec = json.load(f)

paths = spec.get("paths", {})
expected = ["/api/search", "/api/runs", "/api/costs/summary", "/api/findings"]

print("Paths in snapshot:")
for p in sorted(paths):
    ops = [m for m in paths[p] if m in ("get", "post", "put", "patch", "delete")]
    print(f"  {p}  [{', '.join(ops)}]")

total = sum(
    1 for p in paths.values()
    for m in p if m in ("get", "post", "put", "patch", "delete")
)
print(f"Total operations: {total}")

missing = [e for e in expected if e not in paths]
if missing:
    print("ERROR: expected paths missing: " + ", ".join(missing), file=sys.stderr)
    sys.exit(1)

# Proof the A1 query-param refactor is live: /api/runs must expose query params.
runs_params = paths.get("/api/runs", {}).get("get", {}).get("parameters", [])
query_params = [str(p.get("name")) for p in runs_params if p.get("in") == "query"]
if not query_params:
    print("ERROR: /api/runs GET exposes no query parameters (A1 query-param refactor not live?).", file=sys.stderr)
    sys.exit(1)
print(f"/api/runs GET query params: {', '.join(query_params)}")
print("Completeness OK: all expected /api/* paths present (incl. note: /api/runs/{id} listed above).")
PY

if grep -q '"in": *"query"' "$OUT_FILE"; then
  echo "OK: snapshot contains query parameters (\"in\": \"query\")."
else
  echo "ERROR: no query parameters found in snapshot." >&2
  exit 1
fi

echo "---"
echo "Snapshot written: $OUT_FILE"
