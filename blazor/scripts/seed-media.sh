#!/usr/bin/env bash
# Generates a small synthetic media collection for the test server (called by test-server.sh).
#   seed-media.sh <media dir> <jellyfin image>
# Uses the ffmpeg that ships in the Jellyfin image; skipped when the collection already exists.
set -euo pipefail
MEDIA="$1"; IMAGE="$2"
STAMP="$MEDIA/.seeded-v3-subtitles"
[ -f "$STAMP" ] && exit 0
mkdir -p "$MEDIA"

ff() {
    docker run --rm --user "$(id -u):$(id -g)" --memory 1g --entrypoint /usr/lib/jellyfin-ffmpeg/ffmpeg \
        -v "$MEDIA:/media" -w /media "$IMAGE" -hide_banner -loglevel error -y "$@"
}

# video <file> <seconds> <lavfi source> <tone hz> [extra args...]
video() {
    local file="$1" seconds="$2" source="$3" tone="$4"; shift 4
    [ -f "$MEDIA/$file" ] && return 0
    mkdir -p "$MEDIA/$(dirname "$file")"
    ff -f lavfi -i "$source=size=640x360:rate=24" -f lavfi -i "sine=frequency=$tone:sample_rate=44100" \
        -t "$seconds" -c:v libx264 -preset ultrafast -pix_fmt yuv420p -g 48 -c:a aac -b:a 96k -ac 2 \
        -movflags +faststart "$@" "$file"
}

# image <file> <WxH> <color>
image() {
    [ -f "$MEDIA/$1" ] && return 0
    mkdir -p "$MEDIA/$(dirname "$1")"
    ff -f lavfi -i "gradients=size=$2:c0=$3:c1=black:x0=0:y0=0:x1=${2%x*}:y1=${2#*x}" -frames:v 1 -update 1 "$1"
}

# song <file> <seconds> <tone hz> <title> <artist> <album> <track>
song() {
    [ -f "$MEDIA/$1" ] && return 0
    mkdir -p "$MEDIA/$(dirname "$1")"
    ff -f lavfi -i "sine=frequency=$3:sample_rate=44100" -t "$2" -c:a libmp3lame -b:a 128k \
        -metadata title="$4" -metadata artist="$5" -metadata album_artist="$5" -metadata album="$6" \
        -metadata track="$7" -metadata date=2020 "$1"
}

echo "[seed] generating test media in $MEDIA" >&2

video "movies/Big Test Movie (2019)/Big Test Movie (2019).mp4" 90 testsrc2 440
image "movies/Big Test Movie (2019)/poster.jpg" 400x600 0x00a4dc
image "movies/Big Test Movie (2019)/backdrop.jpg" 1280x720 0x00a4dc

# Two audio tracks plus an external subtitle file.
mkdir -p "$MEDIA/movies/Second Feature (2021)"
[ -f "$MEDIA/movies/Second Feature (2021)/Second Feature (2021).mkv" ] || ff -f lavfi -i "smptebars=size=640x360:rate=24" -f lavfi -i "sine=frequency=330:sample_rate=44100" \
    -f lavfi -i "sine=frequency=660:sample_rate=44100" -t 60 -map 0:v -map 1:a -map 2:a \
    -c:v libx264 -preset ultrafast -pix_fmt yuv420p -g 48 -c:a aac -b:a 96k -ac 2 \
    -metadata:s:a:0 language=eng -metadata:s:a:0 title=English \
    -metadata:s:a:1 language=fra -metadata:s:a:1 title=French \
    "movies/Second Feature (2021)/Second Feature (2021).mkv"
{
    for i in 0 1 2 3 4 5 6 7 8 9 10; do
        printf '%d\n00:00:%02d,000 --> 00:00:%02d,000\nSubtitle line %d\n\n' "$((i + 1))" "$((i * 5))" "$((i * 5 + 4))" "$((i + 1))"
    done
} > "$MEDIA/movies/Second Feature (2021)/Second Feature (2021).en.srt"
image "movies/Second Feature (2021)/poster.jpg" 400x600 0xaa5cc3
image "movies/Second Feature (2021)/backdrop.jpg" 1280x720 0xaa5cc3

# ASS is a sidecar so the playback e2e can verify libass without changing the video stream.
cat > "$MEDIA/movies/Second Feature (2021)/Second Feature (2021).en.ass" <<'EOF'
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

# A tiny valid PGS/SUP stream: a white rectangle is displayed from 2 to 12 seconds.
# Keeping this generated here avoids adding binary fixtures to the repository.
python3 - "$MEDIA/movies/Second Feature (2021)/Second Feature (2021).en.sup" <<'PY'
import struct
import sys

out = open(sys.argv[1], "wb")

def segment(pts, kind, payload):
    out.write(b"PG" + struct.pack(">IIBH", pts * 90, 0, kind, len(payload)) + payload)

def display(pts, end, number):
    # 640x360 epoch, one object at (220, 285), 200x40 pixels.
    pcs = struct.pack(">HHBHB BBB", 640, 360, 0x10, number, 0x80, 0, 0, 1)
    pcs += struct.pack(">HBBHH", 1, 0, 0, 220, 285)
    segment(pts, 0x16, pcs)

    wds = struct.pack(">BHHHH", 1, 0, 0, 640, 360)
    segment(pts, 0x17, wds)

    # Palette 0: transparent index 0, opaque white index 1.
    pds = bytes([0, 0, 0, 16, 128, 128, 0, 1, 235, 128, 128, 255])
    segment(pts, 0x14, pds)

    width, height = 200, 40
    # Each line is a long run of palette index 1 followed by end-of-line. PGS uses a two-byte
    # length for runs longer than 63 pixels: 00 C0 00 <length> <palette-index>.
    rle = b"".join(bytes([0, 0xC0, 0, width, 1, 0, 0]) for _ in range(height))
    object_length = len(rle) + 4
    body = struct.pack(">HBB", 1, 0, 0xC0) + object_length.to_bytes(3, "big") + struct.pack(">HH", width, height) + rle
    segment(pts, 0x15, body)
    segment(pts, 0x80, b"")

display(2, 12, 1)
display(12, 20, 2)
out.close()
PY

video "movies/Third Film (2008)/Third Film (2008).mp4" 45 rgbtestsrc 550
image "movies/Third Film (2008)/poster.jpg" 400x600 0xcc7a00

# VP9 + Opus: plays directly in browsers without H.264/AAC decoders (Playwright's Chromium).
mkdir -p "$MEDIA/movies/Open Codec Movie (2015)"
[ -f "$MEDIA/movies/Open Codec Movie (2015)/Open Codec Movie (2015).webm" ] || ff -f lavfi -i "testsrc2=size=640x360:rate=24" \
    -f lavfi -i "sine=frequency=587:sample_rate=48000" -t 60 -c:v libvpx-vp9 -deadline realtime -cpu-used 8 \
    -b:v 400k -g 48 -pix_fmt yuv420p -c:a libopus -b:a 64k -ac 2 \
    "movies/Open Codec Movie (2015)/Open Codec Movie (2015).webm"
image "movies/Open Codec Movie (2015)/poster.jpg" 400x600 0x00897b
image "movies/Open Codec Movie (2015)/backdrop.jpg" 1280x720 0x00897b

for episode in "Season 01/Test Show S01E01" "Season 01/Test Show S01E02" "Season 01/Test Show S01E03" "Season 02/Test Show S02E01"; do
    video "shows/Test Show/$episode.mp4" 40 testsrc 500
done
image "shows/Test Show/poster.jpg" 400x600 0x2e7d32
image "shows/Test Show/backdrop.jpg" 1280x720 0x2e7d32

song "music/Test Artist/First Album/01 - Opening.mp3" 30 440 "Opening" "Test Artist" "First Album" 1
song "music/Test Artist/First Album/02 - Middle.mp3" 30 494 "Middle" "Test Artist" "First Album" 2
song "music/Test Artist/First Album/03 - Closing.mp3" 30 523 "Closing" "Test Artist" "First Album" 3
image "music/Test Artist/First Album/folder.jpg" 500x500 0xc62828
song "music/Other Band/Second Album/01 - Single.mp3" 30 392 "Single" "Other Band" "Second Album" 1
image "music/Other Band/Second Album/folder.jpg" 500x500 0x1565c0

touch "$STAMP"
