#!/usr/bin/env bash
# Add subtitle-only fixtures to a private test server, without changing the shared seed.
#
# JELLYFIN_INSTANCE=subtitles JELLYFIN_PORT=18106 scripts/test-server.sh up
# JELLYFIN_INSTANCE=subtitles JELLYFIN_PORT=18106 scripts/seed-subtitles.sh
# JELLYFIN_INSTANCE=subtitles JELLYFIN_PORT=18106 scripts/test-server.sh reset
set -euo pipefail

MEDIA="${1:-${JFWEB_CACHE:-$HOME/.cache/jellyfin-web-blazor}/testserver-${JELLYFIN_INSTANCE:-subtitles}/media}"
IMAGE="${JELLYFIN_IMAGE:-jellyfin/jellyfin:12.4.20261026-211555}"
MOVIE="$MEDIA/movies/Second Feature (2021)"
URL="${JELLYFIN_URL:-http://localhost:${JELLYFIN_PORT:-18106}}"
USER_NAME="${TEST_USER:-admin}"
PASSWORD="${TEST_PASSWORD:-jellyfin}"

mkdir -p "$MOVIE"
cat > "$MOVIE/Second Feature (2021).en.ass" <<'EOF'
[Script Info]
ScriptType: v4.00+
PlayResX: 640
PlayResY: 360

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,Arial,30,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,2,1,2,30,30,24,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:02.00,0:00:12.00,Default,,0,0,24,,ASS renderer subtitle
EOF

# Generate the binary SUP inside the Jellyfin image; no host Python is needed.
docker run --rm --user "$(id -u):$(id -g)" --memory 1g --entrypoint /usr/bin/perl \
    -v "$MOVIE:/media" "$IMAGE" -e '
use strict; use warnings;
open my $out, ">:raw", "/media/Second Feature (2021).en.sup" or die $!;
sub segment { my ($pts, $type, $payload) = @_; print {$out} "PG", pack("NNCn", $pts * 90000, 0, $type, length($payload)), $payload; }
sub page {
    my ($pts, $number) = @_;
    my $pcs = pack("n n C n C C C C", 640, 360, 0x10, $number, 0x80, 0, 0, 1);
    $pcs .= pack("n C C n n", 1, 0, 0, 220, 285);
    segment($pts, 0x16, $pcs);
    segment($pts, 0x17, pack("C n n n n", 1, 0, 0, 640, 360));
    segment($pts, 0x14, pack("C*", 0, 0, 0, 16, 128, 128, 0, 1, 235, 128, 128, 255));
    my $rle = "";
    $rle .= pack("C*", 0, 0xc0, 0, 200, 1, 0, 0) for 1 .. 40;
    my $length = length($rle) + 4;
    my $body = pack("n C C", 1, 0, 0xc0) . pack("C*", ($length >> 16) & 255, ($length >> 8) & 255, $length & 255);
    $body .= pack("n n", 200, 40) . $rle;
    segment($pts, 0x15, $body);
    segment($pts, 0x80, "");
}
page(2, 1); page(12, 2); close $out;
'

token="$(curl -fsS -X POST "$URL/Users/AuthenticateByName" \
    -H 'Content-Type: application/json' \
    -H 'Authorization: MediaBrowser Client="seed-subtitles", Device="seed-subtitles", DeviceId="seed-subtitles", Version="1"' \
    -d "{\"Username\":\"$USER_NAME\",\"Pw\":\"$PASSWORD\"}" | sed -E 's/.*"AccessToken":"([^"]+)".*/\1/')"
curl -fsS -X POST "$URL/Library/Refresh?ApiKey=$token" >/dev/null
echo "[seed-subtitles] added ASS and PGS fixtures under $MOVIE"
