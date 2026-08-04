#!/usr/bin/env bash
set -Eeuo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}"
: "${POSTGRES_DB:?POSTGRES_DB must be set}"
: "${INGEST_DB_PASSWORD:?INGEST_DB_PASSWORD must be set}"
: "${PROCESSOR_DB_PASSWORD:?PROCESSOR_DB_PASSWORD must be set}"
: "${INSIGHTS_DB_PASSWORD:?INSIGHTS_DB_PASSWORD must be set}"
: "${IDENTITY_DB_PASSWORD:?IDENTITY_DB_PASSWORD must be set}"

psql=(psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --set=ON_ERROR_STOP=1)

role_exists() {
  [[ -n "$("${psql[@]}" --tuples-only --no-align --command="SELECT 1 FROM pg_roles WHERE rolname = '$1'")" ]]
}

if role_exists ingest_role; then
  "${psql[@]}" --command="ALTER ROLE ingest_role LOGIN PASSWORD '${INGEST_DB_PASSWORD//\'/\'\'}'"
else
  "${psql[@]}" --command="CREATE ROLE ingest_role LOGIN PASSWORD '${INGEST_DB_PASSWORD//\'/\'\'}'"
fi

if role_exists processor_role; then
  "${psql[@]}" --command="ALTER ROLE processor_role LOGIN PASSWORD '${PROCESSOR_DB_PASSWORD//\'/\'\'}'"
else
  "${psql[@]}" --command="CREATE ROLE processor_role LOGIN PASSWORD '${PROCESSOR_DB_PASSWORD//\'/\'\'}'"
fi

if role_exists insights_role; then
  "${psql[@]}" --command="ALTER ROLE insights_role LOGIN PASSWORD '${INSIGHTS_DB_PASSWORD//\'/\'\'}'"
else
  "${psql[@]}" --command="CREATE ROLE insights_role LOGIN PASSWORD '${INSIGHTS_DB_PASSWORD//\'/\'\'}'"
fi

"${psql[@]}" <<'SQL'
CREATE SCHEMA IF NOT EXISTS ingest;
CREATE SCHEMA IF NOT EXISTS telemetry;
CREATE SCHEMA IF NOT EXISTS search;

CREATE EXTENSION IF NOT EXISTS btree_gist;

GRANT USAGE, CREATE ON SCHEMA ingest TO ingest_role;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA ingest TO ingest_role;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA ingest TO ingest_role;
ALTER DEFAULT PRIVILEGES IN SCHEMA ingest GRANT ALL ON TABLES TO ingest_role;
ALTER DEFAULT PRIVILEGES IN SCHEMA ingest GRANT ALL ON SEQUENCES TO ingest_role;

GRANT USAGE, CREATE ON SCHEMA telemetry TO processor_role;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA telemetry TO processor_role;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA telemetry TO processor_role;
ALTER DEFAULT PRIVILEGES IN SCHEMA telemetry GRANT ALL ON TABLES TO processor_role;
ALTER DEFAULT PRIVILEGES IN SCHEMA telemetry GRANT ALL ON SEQUENCES TO processor_role;
ALTER DEFAULT PRIVILEGES FOR ROLE processor_role IN SCHEMA telemetry GRANT ALL ON TABLES TO processor_role;
ALTER DEFAULT PRIVILEGES FOR ROLE processor_role IN SCHEMA telemetry GRANT ALL ON SEQUENCES TO processor_role;
ALTER DEFAULT PRIVILEGES FOR ROLE processor_role IN SCHEMA telemetry GRANT SELECT ON TABLES TO insights_role;
GRANT USAGE, CREATE ON SCHEMA search TO processor_role;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA search TO processor_role;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA search TO processor_role;
ALTER DEFAULT PRIVILEGES IN SCHEMA search GRANT ALL ON TABLES TO processor_role;
ALTER DEFAULT PRIVILEGES IN SCHEMA search GRANT ALL ON SEQUENCES TO processor_role;

GRANT USAGE ON SCHEMA telemetry TO insights_role;
GRANT USAGE ON SCHEMA search TO insights_role;
GRANT SELECT ON ALL TABLES IN SCHEMA telemetry TO insights_role;
GRANT SELECT ON ALL TABLES IN SCHEMA search TO insights_role;
ALTER DEFAULT PRIVILEGES IN SCHEMA telemetry GRANT SELECT ON TABLES TO insights_role;
ALTER DEFAULT PRIVILEGES IN SCHEMA search GRANT SELECT ON TABLES TO insights_role;
SQL

if [[ -z "$("${psql[@]}" --tuples-only --no-align \
  --command="SELECT 1 FROM pg_database WHERE datname = 'tokenburn_identity'")" ]]; then
  createdb --username "$POSTGRES_USER" tokenburn_identity
fi

psql_identity=(psql --username "$POSTGRES_USER" --dbname tokenburn_identity --set=ON_ERROR_STOP=1)

if [[ -n "$("${psql_identity[@]}" --tuples-only --no-align \
  --command="SELECT 1 FROM pg_roles WHERE rolname = 'identity_role'")" ]]; then
  "${psql_identity[@]}" --command="ALTER ROLE identity_role PASSWORD '${IDENTITY_DB_PASSWORD//\'/\'\'}'"
else
  "${psql_identity[@]}" --command="CREATE ROLE identity_role LOGIN PASSWORD '${IDENTITY_DB_PASSWORD//\'/\'\'}'"
fi

"${psql[@]}" --command="ALTER DATABASE tokenburn_identity OWNER TO identity_role"
