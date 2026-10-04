#!/usr/bin/env bash
# Starts the whole Hakutaku stack in Docker: server, database, admin UI and wiki.
# Run it with `bash start.sh`. `bash stop.sh` stops it again.
set -u
cd "$(dirname "$0")"

url=http://localhost:8090

fail() {
  echo
  echo "$1"
  exit 1
}

# wait_for <seconds> <command...>: retries the command every 2 seconds until it
# succeeds, or returns 1 once the time is up.
wait_for() {
  local deadline=$((SECONDS + $1))
  shift
  until "$@" >/dev/null 2>&1; do
    [ "$SECONDS" -ge "$deadline" ] && return 1
    sleep 2
  done
}

command -v docker >/dev/null 2>&1 ||
  fail "Docker is not installed. Install Docker Desktop from https://www.docker.com/products/docker-desktop/ then run this again."

if ! docker info >/dev/null 2>&1; then
  # Installed but not running. On macOS we can open Docker Desktop ourselves.
  if [ "$(uname)" = Darwin ] && open -a Docker 2>/dev/null; then
    echo "Starting Docker Desktop. This can take a minute..."
    wait_for 180 docker info ||
      fail "Docker Desktop did not start within 3 minutes. Open it yourself, wait until it says it is running, then run this again."
  else
    fail "Docker is not running. Open Docker Desktop (on Linux without it: sudo systemctl start docker), wait until it is running, then run this again."
  fi
fi

echo "Building and starting Hakutaku. The first time takes a few minutes..."
docker compose -f compose.dev.yaml up -d --build ||
  fail "Docker could not start Hakutaku, see the error above. If it mentions a port that is already allocated, another program is using it: see the readme."

echo "Waiting for the server to answer..."
if ! wait_for 120 curl -sf "$url/Health"; then
  echo "The server did not come up within 2 minutes. Its last log lines:"
  docker compose -f compose.dev.yaml logs --tail 50 app
  exit 1
fi

cat <<EOF

  Hakutaku is running.

    Admin UI:  $url
    Username:  admin
    Password:  hakutaku
    Wiki:      http://localhost:5020

  To stop it, run: bash stop.sh

EOF

# Open the admin UI in the default browser. Not `command -v open`: on some Linux
# distros `open` is openvt, which does something else entirely.
case "$(uname)" in
  Darwin) open "$url" ;;
  MINGW* | MSYS* | CYGWIN*) cmd.exe //c start "" "$url" ;;
  *) command -v xdg-open >/dev/null 2>&1 && xdg-open "$url" >/dev/null 2>&1 & ;;
esac
