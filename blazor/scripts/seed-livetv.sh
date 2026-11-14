#!/usr/bin/env bash
# Seed six playable M3U channels and a relative-time XMLTV guide on a private server.
# Reuses the tuner/provider on repeated calls, explicitly runs the guide refresh scheduled task,
# and waits for six channels plus a current programme (GuideInfo alone is not a readiness check).
# All fixture generation and JSON tooling runs in the dev container; no host Python is required.
set -euo pipefail

if [[ "${JF_LIVETV_IN_DOCKER:-}" != 1 ]]; then
    exec "$(dirname "$0")/../dev" sh -c 'exec env JF_LIVETV_IN_DOCKER=1 JELLYFIN_INSTANCE="$1" JELLYFIN_PORT="$2" JFWEB_CACHE=/cache bash scripts/seed-livetv.sh "${@:3}"' \
        seed-livetv "${JELLYFIN_INSTANCE:-livetv}" "${JELLYFIN_PORT:-18098}" "$@"
fi

INSTANCE="${JELLYFIN_INSTANCE:-livetv}"
PORT="${JELLYFIN_PORT:-18098}"
URL="http://172.17.0.1:${PORT}"
DATA="${JFWEB_CACHE:-$HOME/.cache/jellyfin-web-blazor}/testserver-${INSTANCE}"
MEDIA="${DATA}/media"
if [[ "${1:-}" == "--files-only" ]]; then
    M3U="${MEDIA}/livetv-e2e.m3u"
    XML="${MEDIA}/livetv-e2e.xml"
    FILES_ONLY=1
else
    M3U="${MEDIA}/livetv.m3u"
    XML="${MEDIA}/livetv.xml"
    FILES_ONLY=0
fi
AUTH='MediaBrowser Client="seed-livetv", Device="seed-livetv", DeviceId="seed-livetv", Version="1"'

api() {
    curl -fsS -X "$1" "$URL$2" -H "Authorization: $AUTH" -H 'Content-Type: application/json' ${3:+-d "$3"}
}

if (( FILES_ONLY == 0 )); then
token="$(api POST /Users/AuthenticateByName '{"Username":"admin","Pw":"jellyfin"}' | sed -E 's/.*"AccessToken":"([^"]+)".*/\1/')"
authed() {
    curl -fsS -X "$1" "$URL$2" -H "Authorization: $AUTH, Token=\"$token\"" -H 'Content-Type: application/json' ${3:+-d "$3"}
}

user_id="$(authed GET /Users | perl -MJSON::PP -0777 -ne 'print decode_json($_)->[0]{Id}')"
policy="$(authed GET "/Users/${user_id}" | perl -MJSON::PP -0777 -ne '$x=decode_json($_)->{Policy}; $x->{EnableLiveTvManagement}=JSON::PP::true; print encode_json($x)')"
authed POST "/Users/${user_id}/Policy" "$policy" >/dev/null
fi

mkdir -p "$MEDIA"
cat > "$M3U" <<'EOF'
#EXTM3U
#EXTINF:-1 tvg-id="jf-movie" group-title="Movies",Jellyfin Movies
/media/livetv.webm
#EXTINF:-1 tvg-id="jf-sports" group-title="Sports",Jellyfin Sports
/media/livetv.webm
#EXTINF:-1 tvg-id="jf-kids" group-title="Kids",Jellyfin Kids
/media/livetv.webm
#EXTINF:-1 tvg-id="jf-news" group-title="News",Jellyfin News
/media/livetv.webm
#EXTINF:-1 tvg-id="jf-series" group-title="Series",Jellyfin Series
/media/livetv.webm
#EXTINF:-1 tvg-id="jf-extra" group-title="Entertainment",Jellyfin Extra
/media/livetv.webm
EOF

# M3U tuners reject non-HTTP paths (and remove all channels at the next refresh). Stream the
# seeded VP9/Opus movie from this same private server, using its internal address and API token.
if (( FILES_ONLY == 0 )); then
    movie_id="$(authed GET '/Items?Recursive=true&IncludeItemTypes=Movie&SearchTerm=Open%20Codec' | perl -MJSON::PP -0777 -ne 'print decode_json($_)->{Items}[0]{Id}')"
    [[ -n "$movie_id" ]] || { echo 'Open Codec Movie is missing from the media seed' >&2; exit 1; }
    # The tuner derives channel identity from the URL, so each channel needs its own query value.
    awk -v url="http://127.0.0.1:8096/Videos/$movie_id/stream.webm?Static=true&ApiKey=$token" \
        '/^\/media\/livetv.webm$/ { print url "&channel=" ++channel; next } { print }' "$M3U" > "$M3U.new"
    mv "$M3U.new" "$M3U"
else
    # Dashboard fixture only: registration checks the playlist, not playback.
    awk '/^\/media\/livetv.webm$/ { print "https://example.invalid/livetv-" ++channel ".webm"; next } { print }' "$M3U" > "$M3U.new"
    mv "$M3U.new" "$M3U"
fi

cat > "$XML" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE tv SYSTEM "xmltv.dtd">
<tv generator-info-name="Jellyfin Live TV port test">
  <channel id="jf-movie"><display-name>Jellyfin Movies</display-name></channel>
  <channel id="jf-sports"><display-name>Jellyfin Sports</display-name></channel>
  <channel id="jf-kids"><display-name>Jellyfin Kids</display-name></channel>
  <channel id="jf-news"><display-name>Jellyfin News</display-name></channel>
  <channel id="jf-series"><display-name>Jellyfin Series</display-name></channel>
  <channel id="jf-extra"><display-name>Jellyfin Extra</display-name></channel>
EOF

for day in -2 -1 0 1 2 3 4 5; do
    # Keep the six one-hour programmes around the current time of day. A midnight-only
    # fixture vanishes from the guide's visible window for most of the day, and cannot
    # reliably supply a current programme for Live TV playback.
    base="$(date -u -d "$day day -4 hours" '+%Y%m%d%H%M%S')"
    next1="$(date -u -d "$day day -3 hours" '+%Y%m%d%H%M%S')"
    next2="$(date -u -d "$day day -2 hours" '+%Y%m%d%H%M%S')"
    next3="$(date -u -d "$day day -1 hour" '+%Y%m%d%H%M%S')"
    next4="$(date -u -d "$day day" '+%Y%m%d%H%M%S')"
    next5="$(date -u -d "$day day +1 hour" '+%Y%m%d%H%M%S')"
    next6="$(date -u -d "$day day +2 hours" '+%Y%m%d%H%M%S')"
    cat >> "$XML" <<EOF
  <programme start="${base} +0000" stop="${next1} +0000" channel="jf-movie"><title>Jellyfin Feature</title><desc>A movie in the Live TV guide.</desc><category>Movie</category><video><quality>HDTV</quality></video><premiere /></programme>
  <programme start="${next1} +0000" stop="${next2} +0000" channel="jf-sports"><title>Jellyfin Sports Live</title><desc>Live sports coverage.</desc><category>Sports</category><video><quality>HDTV</quality></video><live /></programme>
  <programme start="${next2} +0000" stop="${next3} +0000" channel="jf-kids"><title>Jellyfin Kids Club</title><desc>A kids programme.</desc><category>Kids</category><video><quality>HDTV</quality></video><new /></programme>
  <programme start="${next3} +0000" stop="${next4} +0000" channel="jf-news"><title>Jellyfin News Today</title><desc>News from the test guide.</desc><category>News</category><video><quality>HDTV</quality></video></programme>
  <programme start="${next4} +0000" stop="${next5} +0000" channel="jf-series"><title>Jellyfin Test Series</title><sub-title>Episode ${day#-}: The Test</sub-title><desc>A series episode.</desc><category>Series</category><episode-num system="xmltv_ns">${day#-}.0.0/1</episode-num><video><quality>HDTV</quality></video><premiere /><new /></programme>
  <programme start="${next5} +0000" stop="${next6} +0000" channel="jf-extra"><title>Jellyfin Repeat</title><desc>A repeat programme.</desc><category>Entertainment</category><video><quality>HDTV</quality></video><previously-shown /></programme>
EOF
done
echo '</tv>' >> "$XML"

if (( FILES_ONLY == 1 )); then
    echo "Live TV fixtures written to $MEDIA"
    exit 0
fi

tuner='{"Type":"m3u","Url":"/media/livetv.m3u","Source":"m3u","DeviceId":"jellyfin-livetv-m3u","FriendlyName":"Jellyfin Live TV M3U","TunerCount":6,"ReadAtNativeFramerate":true}'
listing='{"Type":"xmltv","Path":"/media/livetv.xml","Enabled":true}'

if ! authed GET /System/Configuration/livetv | grep -q '"Url":"/media/livetv.m3u"'; then
    authed POST /LiveTv/TunerHosts "$tuner" >/dev/null
fi
if ! authed GET /System/Configuration/livetv | grep -q '"Path":"/media/livetv.xml"'; then
    authed POST /LiveTv/ListingProviders "$listing" >/dev/null
fi
# GuideInfo reports a date range even with zero channels. Registering a tuner can also queue a
# refresh before the XMLTV provider exists. Wait for that work, then run and await a fresh pass.
task_id="$(authed GET /ScheduledTasks | perl -MJSON::PP -0777 -ne 'for (@{decode_json($_)}) { print $_->{Id} if $_->{Key} eq "RefreshGuide" }')"
if [[ -z "$task_id" ]]; then
    echo 'The server has no RefreshGuide scheduled task' >&2
    exit 1
fi
wait_idle() {
    for _ in $(seq 1 90); do
        if authed GET "/ScheduledTasks/$task_id" | grep -q '"State":"Idle"'; then return 0; fi
        sleep 2
    done
    echo 'Guide refresh did not finish' >&2
    return 1
}
wait_idle
authed POST "/ScheduledTasks/Running/$task_id" >/dev/null
wait_idle
authed GET "/ScheduledTasks/$task_id" | perl -MJSON::PP -0777 -ne 'exit(decode_json($_)->{LastExecutionResult}{Status} eq "Completed" ? 0 : 1)'
channels="$(authed GET '/LiveTv/Channels?AddCurrentProgram=true')"
if echo "$channels" | perl -MJSON::PP -0777 -ne '$x=decode_json($_); exit(@{$x->{Items}} == 6 && grep({$_->{CurrentProgram}{Id}} @{$x->{Items}}) ? 0 : 1)'; then
    echo "Live TV guide seeded at http://localhost:$PORT (6 channels, current programme ready)"
    exit 0
fi
echo "Live TV seed is incomplete: $channels" >&2
exit 1
