#!/bin/bash

set -euo pipefail

project_root="$(cd "$(dirname "$0")/.." && pwd)"
port="${PORT:-8765}"
url="http://127.0.0.1:$port/"

if lsof -nP -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1; then
    open -a "Google Chrome" "$url"
    echo "Opened existing local test server: $url"
    exit 0
fi

python3 -m http.server "$port" \
    --bind 127.0.0.1 \
    --directory "$project_root/tools/window-share-test" &
server_pid=$!

cleanup() {
    kill "$server_pid" >/dev/null 2>&1 || true
}

trap cleanup EXIT INT TERM

for _ in {1..30}; do
    if curl --silent --fail "$url" >/dev/null; then
        break
    fi
    sleep 0.1
done

open -a "Google Chrome" "$url"
echo "Local test server: $url"
echo "Press Ctrl+C to stop."

wait "$server_pid"
