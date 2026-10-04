#!/usr/bin/env bash
# Stops the Hakutaku stack that start.sh started. The database is kept.
cd "$(dirname "$0")"

if ! docker compose -f compose.dev.yaml down; then
  echo
  echo "Could not stop Hakutaku. Is Docker running?"
  exit 1
fi
echo
echo "Hakutaku is stopped. Your data is kept for next time."
