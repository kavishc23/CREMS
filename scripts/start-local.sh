#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

if [[ ! -f .env ]]; then
  echo "Missing .env. Run: cp .env.example .env, then fill in the required values."
  exit 1
fi

# Load simple KEY=VALUE entries without evaluating passwords as shell code.
while IFS='=' read -r key value || [[ -n "$key" ]]; do
  [[ -z "$key" || "$key" == \#* ]] && continue
  value="${value%$'\r'}"
  if [[ "$value" == \"*\" && "$value" == *\" ]]; then value="${value:1:${#value}-2}"; fi
  if [[ "$value" == \'*\' && "$value" == *\' ]]; then value="${value:1:${#value}-2}"; fi
  export "$key=$value"
done < .env

required=(CREMS_DB_PASSWORD CREMS_ADMIN_EMAIL CREMS_ADMIN_PASSWORD)
for name in "${required[@]}"; do
  if [[ -z "${!name:-}" || "${!name}" == replace-with-* ]]; then
    echo "Set $name in .env before starting CREMS."
    exit 1
  fi
done

for command_name in docker npm; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "$command_name is required but was not found."
    exit 1
  fi
done

if command -v dotnet >/dev/null 2>&1; then
  DOTNET=dotnet
elif [[ -x ./.dotnet/dotnet ]]; then
  DOTNET=./.dotnet/dotnet
else
  echo ".NET 10 SDK is required but was not found."
  exit 1
fi

export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__DefaultConnection="Server=localhost,1433;Database=${CREMS_DB_NAME:-Crems};User Id=sa;Password=${CREMS_DB_PASSWORD};TrustServerCertificate=True"
export FrontendUrl="${CREMS_FRONTEND_URL:-http://localhost:5173}"
export BootstrapAdmin__Email="$CREMS_ADMIN_EMAIL"
export BootstrapAdmin__Password="$CREMS_ADMIN_PASSWORD"
export BootstrapAdmin__FullName="${CREMS_ADMIN_FULL_NAME:-System Administrator}"
export Email__Enabled="${EMAIL_ENABLED:-false}"
export Email__ApiKey="${EMAIL_API_KEY:-}"
export Email__FromAddress="${EMAIL_FROM_ADDRESS:-}"
export Email__FromName="${EMAIL_FROM_NAME:-Carpenters Rentals}"
export Email__RedirectAllTo="${EMAIL_REDIRECT_ALL_TO:-}"
export Email__NotificationCopyTo="${EMAIL_NOTIFICATION_COPY_TO:-}"
export Email__NotificationDirectDelivery="${EMAIL_NOTIFICATION_DIRECT_DELIVERY:-false}"
export Email__AllowDirectDeliveryOutsideProduction="${EMAIL_ALLOW_DIRECT_DELIVERY_OUTSIDE_PRODUCTION:-false}"
export Email__Host="${EMAIL_SMTP_HOST:-}"
export Email__Port="${EMAIL_SMTP_PORT:-587}"
export Email__UseSsl="${EMAIL_SMTP_USE_SSL:-true}"
export Email__Username="${EMAIL_SMTP_USERNAME:-}"
export Email__Password="${EMAIL_SMTP_PASSWORD:-}"
export Email__PreferHttpApi=true

echo "Starting SQL Server..."
docker compose up -d sqlserver
container_id="$(docker compose ps -q sqlserver)"
if [[ -z "$container_id" ]]; then
  echo "SQL Server container did not start."
  exit 1
fi

for _ in {1..30}; do
  health="$(docker inspect --format='{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$container_id")"
  [[ "$health" == healthy ]] && break
  sleep 2
done
if [[ "${health:-}" != healthy ]]; then
  echo "SQL Server did not become healthy. Run: docker compose logs sqlserver"
  exit 1
fi

echo "Restoring application dependencies..."
"$DOTNET" restore CREMS.slnx
if [[ ! -d src/crems-web/node_modules ]]; then
  npm --prefix src/crems-web ci
fi

api_pid=""
cleanup() {
  trap - INT TERM EXIT
  [[ -n "$api_pid" ]] && kill "$api_pid" 2>/dev/null || true
  docker compose stop sqlserver >/dev/null 2>&1 || true
}
trap cleanup INT TERM EXIT

echo "Starting API at ${CREMS_API_URL:-http://localhost:5080}..."
"$DOTNET" run --project src/CREMS.Api --no-launch-profile --urls "${CREMS_API_URL:-http://localhost:5080}" &
api_pid=$!

echo "Starting frontend at ${CREMS_FRONTEND_URL:-http://localhost:5173}..."
frontend_port="${CREMS_FRONTEND_URL:-http://localhost:5173}"
frontend_port="${frontend_port##*:}"
frontend_port="${frontend_port%%/*}"
npm --prefix src/crems-web run dev -- --host 0.0.0.0 --port "$frontend_port"
