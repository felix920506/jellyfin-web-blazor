#!/usr/bin/env bash
set -euo pipefail

INSTANCE="${JELLYFIN_INSTANCE:-livetv}"
PORT="${JELLYFIN_PORT:-18098}"
URL="http://localhost:${PORT}"
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

user_id="$(authed GET /Users | python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["Id"])')"
policy="$(authed GET "/Users/${user_id}" | python3 -c 'import json,sys; x=json.load(sys.stdin)["Policy"]; x["EnableLiveTvManagement"]=True; print(json.dumps(x))')"
authed POST "/Users/${user_id}/Policy" "$policy" >/dev/null
fi

mkdir -p "$MEDIA"
cat > "$M3U" <<'EOF'
#EXTM3U
#EXTINF:-1 tvg-id="jf-movie" group-title="Movies",Jellyfin Movies
https://example.invalid/jellyfin-movie
#EXTINF:-1 tvg-id="jf-sports" group-title="Sports",Jellyfin Sports
https://example.invalid/jellyfin-sports
#EXTINF:-1 tvg-id="jf-kids" group-title="Kids",Jellyfin Kids
https://example.invalid/jellyfin-kids
#EXTINF:-1 tvg-id="jf-news" group-title="News",Jellyfin News
https://example.invalid/jellyfin-news
#EXTINF:-1 tvg-id="jf-series" group-title="Series",Jellyfin Series
https://example.invalid/jellyfin-series
#EXTINF:-1 tvg-id="jf-extra" group-title="Entertainment",Jellyfin Extra
https://example.invalid/jellyfin-extra
EOF

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
    base="$(date -u -d "$day day" '+%Y%m%d')"
    cat >> "$XML" <<EOF
  <programme start="${base}000000 +0000" stop="${base}010000 +0000" channel="jf-movie"><title>Jellyfin Feature</title><desc>A movie in the Live TV guide.</desc><category>Movie</category><video><quality>HDTV</quality></video><premiere /></programme>
  <programme start="${base}010000 +0000" stop="${base}020000 +0000" channel="jf-sports"><title>Jellyfin Sports Live</title><desc>Live sports coverage.</desc><category>Sports</category><video><quality>HDTV</quality></video><live /></programme>
  <programme start="${base}020000 +0000" stop="${base}030000 +0000" channel="jf-kids"><title>Jellyfin Kids Club</title><desc>A kids programme.</desc><category>Kids</category><video><quality>HDTV</quality></video><new /></programme>
  <programme start="${base}030000 +0000" stop="${base}040000 +0000" channel="jf-news"><title>Jellyfin News Today</title><desc>News from the test guide.</desc><category>News</category><video><quality>HDTV</quality></video></programme>
  <programme start="${base}040000 +0000" stop="${base}050000 +0000" channel="jf-series"><title>Jellyfin Test Series</title><sub-title>Episode ${day#-}: The Test</sub-title><desc>A series episode.</desc><category>Series</category><episode-num system="xmltv_ns">${day#-}.0.0/1</episode-num><video><quality>HDTV</quality></video><premiere /><new /></programme>
  <programme start="${base}050000 +0000" stop="${base}060000 +0000" channel="jf-extra"><title>Jellyfin Repeat</title><desc>A repeat programme.</desc><category>Entertainment</category><video><quality>HDTV</quality></video><previously-shown /></programme>
EOF
done
echo '</tv>' >> "$XML"

if (( FILES_ONLY == 1 )); then
    echo "Live TV fixtures written to $MEDIA"
    exit 0
fi

tuner='{"Type":"m3u","Url":"/media/livetv.m3u","Source":"m3u","DeviceId":"jellyfin-livetv-m3u","FriendlyName":"Jellyfin Live TV M3U","TunerCount":6}'
listing='{"Type":"xmltv","Path":"/media/livetv.xml","Enabled":true}'

authed POST /LiveTv/TunerHosts "$tuner" >/dev/null || true
if ! authed GET /LiveTv/ListingProviders | grep -q 'livetv.xml'; then
    authed POST /LiveTv/ListingProviders "$listing" >/dev/null
fi
curl -fsS -X POST "$URL/LiveTv/GuideInfo/Refresh" -H "Authorization: $AUTH, Token=\"$token\"" >/dev/null 2>&1 || true

for _ in $(seq 1 45); do
    if authed GET /LiveTv/GuideInfo | grep -q 'StartDate'; then
        echo "Live TV guide seeded at $URL"
        exit 0
    fi
    sleep 2
done
echo "warning: Live TV guide did not become available" >&2
exit 1
