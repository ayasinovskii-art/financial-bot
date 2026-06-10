#!/usr/bin/env bash
# Deploy FinanceBot on the host runner: backup DB, rebuild, force-recreate, health-check, rollback.
# Runs on the self-hosted runner whose working dir is the repo checkout.
# Requires: docker, a populated .env in repo root (NOT committed), $HOME writable for backups.
set -euo pipefail

# ENV_FILE: путь к .env с секретами бота. По умолчанию — repo root (host-runner).
# Runner-в-докере монтирует его read-only вне workspace, т.к. checkout чистит репо (git clean -ffdx).
ENV_FILE="${ENV_FILE:-.env}"
COMPOSE="docker compose -f docker/docker-compose.yml --env-file $ENV_FILE"
BACKUP_DIR="${BACKUP_DIR:-$HOME/financebot-backups}"
HEALTH_URL="${HEALTH_URL:-http://127.0.0.1:8080/health}"
HEALTH_RETRIES="${HEALTH_RETRIES:-30}"

mkdir -p "$BACKUP_DIR"
ts="$(date +%Y%m%d-%H%M%S)"

echo "==> 1/5 Backing up Postgres"
# shellcheck disable=SC1091
set -a; source "$ENV_FILE"; set +a
$COMPOSE exec -T postgres pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB" \
  > "$BACKUP_DIR/db-$ts.sql" || { echo "::error::pg_dump failed"; exit 1; }
echo "    backup: $BACKUP_DIR/db-$ts.sql"

echo "==> 2/5 Tagging current image as :previous"
if docker image inspect financebot:dev >/dev/null 2>&1; then
  docker tag financebot:dev financebot:previous
fi

echo "==> 3/5 Building new image"
$COMPOSE build bot

echo "==> 4/5 Recreating containers"
$COMPOSE up -d --force-recreate

echo "==> 5/5 Health check ($HEALTH_URL)"
ok=0
for i in $(seq 1 "$HEALTH_RETRIES"); do
  if curl -fsS "$HEALTH_URL" >/dev/null 2>&1; then ok=1; break; fi
  echo "    waiting for health ($i/$HEALTH_RETRIES)..."; sleep 5
done

if [[ "$ok" -ne 1 ]]; then
  echo "::error::health check failed; rolling back to financebot:previous"
  if docker image inspect financebot:previous >/dev/null 2>&1; then
    docker tag financebot:previous financebot:dev
    $COMPOSE up -d --force-recreate
  fi
  echo "::error::deploy rolled back. DB backup at $BACKUP_DIR/db-$ts.sql"
  exit 1
fi

echo "==> Deploy OK ($ts)"
