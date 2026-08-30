#!/usr/bin/env bash
# Stands up SQL Server via docker-compose and applies schema.sql + seed.sql
# by hand -- there's no `dotnet ef database update` here because there's no
# .NET SDK in the sandbox this repo was built in to run migrations with.
# If you have the SDK, generating a real EF Core migration from
# MatchingEngineDbContext is the better long-term source of truth; this
# script exists so the schema is standable-up right now, SDK or not.
set -euo pipefail

cd "$(dirname "$0")/.."

echo "--- starting SQL Server ---"
docker compose up -d sqlserver

echo "--- waiting for SQL Server to accept connections (healthcheck) ---"
until [ "$(docker compose ps -q sqlserver | xargs docker inspect -f '{{.State.Health.Status}}')" = "healthy" ]; do
    sleep 2
    echo "  still waiting..."
done

SA_PASSWORD="MatchingEngine!2026"

echo "--- applying schema.sql ---"
docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$SA_PASSWORD" -C -i /dev/stdin < docker/init/schema.sql

echo "--- applying seed.sql ---"
docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$SA_PASSWORD" -C -i /dev/stdin < docker/init/seed.sql

echo "--- done. Connection string for appsettings/local use: ---"
echo 'Server=localhost,1433;Database=MatchingEngine;User Id=sa;Password=MatchingEngine!2026;TrustServerCertificate=true'
