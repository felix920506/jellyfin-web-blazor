#!/usr/bin/env bash
# A throwaway Jellyfin server for manual and end-to-end testing of the port.
#
#   scripts/test-server.sh up      start (or reuse) the server and seed it; prints its URL
#   scripts/test-server.sh down    stop and remove the container (data is kept)
#   scripts/test-server.sh reset   down + delete its data
#   scripts/test-server.sh url     print the URL
#
# One server is shared by every checkout on this machine. Seeded state: the startup wizard is
# completed with the administrator below. Data lives on disk under the port's cache directory.
set -euo pipefail

NAME="jellyfin-web-blazor-testserver"
IMAGE="${JELLYFIN_IMAGE:-jellyfin/jellyfin:12.4.20261026-211555}"
PORT="${JELLYFIN_PORT:-18096}"
DATA="${JFWEB_CACHE:-$HOME/.cache/jellyfin-web-blazor}/testserver"
URL="http://localhost:$PORT"
export TEST_USER="${TEST_USER:-admin}" TEST_PASSWORD="${TEST_PASSWORD:-jellyfin}"
AUTH='MediaBrowser Client="seed", Device="seed", DeviceId="seed", Version="1"'

api() { # api METHOD PATH [JSON]
    curl -fsS -X "$1" "$URL$2" -H "Authorization: $AUTH" -H 'Content-Type: application/json' ${3:+-d "$3"}
}

up() {
    if ! docker ps --format '{{.Names}}' | grep -qx "$NAME"; then
        docker rm -f "$NAME" >/dev/null 2>&1 || true
        mkdir -p "$DATA/config" "$DATA/cache" "$DATA/media/movies" "$DATA/media/shows" "$DATA/media/music"
        docker run -d --name "$NAME" --user "$(id -u):$(id -g)" -p "$PORT:8096" --memory 1g \
            -v "$DATA/config:/config" -v "$DATA/cache:/cache" -v "$DATA/media:/media:ro" "$IMAGE" >/dev/null
    fi
    for _ in $(seq 1 90); do
        if curl -fsS "$URL/System/Info/Public" >/dev/null 2>&1; then break; fi
        sleep 2
    done
    if curl -fsS "$URL/System/Info/Public" | grep -q '"StartupWizardCompleted":false'; then
        api POST /Startup/Configuration '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
        api GET /Startup/User >/dev/null
        api POST /Startup/User "{\"Name\":\"$TEST_USER\",\"Password\":\"$TEST_PASSWORD\"}"
        api POST /Startup/RemoteAccess '{"EnableRemoteAccess":true,"EnableAutomaticPortMapping":false}'
        api POST /Startup/Complete
    fi
    echo "$URL (user: $TEST_USER, password: $TEST_PASSWORD)"
}

case "${1:-up}" in
    up) up ;;
    down) docker rm -f "$NAME" >/dev/null 2>&1 || true ;;
    reset) docker rm -f "$NAME" >/dev/null 2>&1 || true; rm -rf "$DATA" ;;
    url) echo "$URL" ;;
    *) sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//' ;;
esac
