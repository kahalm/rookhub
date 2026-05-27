#!/usr/bin/env bash
###############################################################################
# RookHub E2E Test Runner
#
# Startet einen isolierten Stack, seedet Testdaten, fuehrt Playwright aus
# und raeumt alles auf. Exit-Code = Playwright Exit-Code.
#
# Usage:  bash scripts/e2e.sh
###############################################################################
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_FILE="$PROJECT_ROOT/compose.e2e.yml"
ENV_FILE="$PROJECT_ROOT/.env.e2e"
PROJECT_NAME="rookhub-e2e"
FRONTEND_URL="http://localhost:8086"
WAIT_TIMEOUT=180  # seconds

# ── Cleanup function (always runs on EXIT) ────────────────────────────────
cleanup() {
  echo ""
  echo "==> Stopping E2E stack..."
  docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" -p "$PROJECT_NAME" down -v --remove-orphans 2>/dev/null || true
}
trap cleanup EXIT

# ── Ensure clean state ────────────────────────────────────────────────────
echo "==> Cleaning up any previous E2E stack..."
docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" -p "$PROJECT_NAME" down -v --remove-orphans 2>/dev/null || true

# ── Start stack ───────────────────────────────────────────────────────────
echo "==> Starting E2E stack (build + detach)..."
docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" -p "$PROJECT_NAME" up --build -d

# ── Wait for frontend to be reachable ─────────────────────────────────────
echo "==> Waiting for frontend at $FRONTEND_URL (timeout: ${WAIT_TIMEOUT}s)..."
elapsed=0
until curl -sf "$FRONTEND_URL" > /dev/null 2>&1; do
  if [ "$elapsed" -ge "$WAIT_TIMEOUT" ]; then
    echo "ERROR: Frontend did not become ready within ${WAIT_TIMEOUT}s"
    echo ""
    echo "==> Container logs:"
    docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" -p "$PROJECT_NAME" logs --tail=50
    exit 1
  fi
  sleep 2
  elapsed=$((elapsed + 2))
done
echo "==> Frontend is ready (${elapsed}s)"

# ── Run Playwright ────────────────────────────────────────────────────────
echo "==> Running Playwright E2E tests..."
cd "$PROJECT_ROOT/src/frontend/app"

set +e
npx playwright test --config=playwright.e2e.config.ts
PLAYWRIGHT_EXIT=$?
set -e

if [ "$PLAYWRIGHT_EXIT" -eq 0 ]; then
  echo ""
  echo "==> All E2E tests passed!"
else
  echo ""
  echo "==> E2E tests failed (exit code: $PLAYWRIGHT_EXIT)"
fi

exit $PLAYWRIGHT_EXIT
