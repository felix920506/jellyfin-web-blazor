#!/usr/bin/env bash
# Adds deterministic photo and book fixtures for the managed-player port.
#   seed-players.sh <media dir> <jellyfin image>
set -euo pipefail
MEDIA="$1"; IMAGE="$2"
STAMP="$MEDIA/.seeded-players-v1"
[ -f "$STAMP" ] && exit 0
mkdir -p "$MEDIA/photos/Slideshow" "$MEDIA/books"

ff() {
    docker run --rm --user "$(id -u):$(id -g)" --memory 1g --entrypoint /usr/lib/jellyfin-ffmpeg/ffmpeg \
        -v "$MEDIA:/media" -w /media "$IMAGE" -hide_banner -loglevel error -y "$@"
}

for color in 0x00897b 0xc62828 0x6a1b9a; do
    name="${color#0x}.jpg"
    [ -f "$MEDIA/photos/Slideshow/$name" ] || \
        ff -f lavfi -i "color=c=$color:s=960x640" -frames:v 1 "photos/Slideshow/$name"
done

# A minimal, valid EPUB: epubjs only needs the container, package and one XHTML document.
if [ ! -f "$MEDIA/books/Reader Sample.epub" ]; then
    tmp="$(mktemp -d)"
    trap 'rm -rf "$tmp"' EXIT
    mkdir -p "$tmp/META-INF" "$tmp/OEBPS"
    printf 'application/epub+zip' > "$tmp/mimetype"
    cat > "$tmp/META-INF/container.xml" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?><container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles></container>
EOF
    cat > "$tmp/OEBPS/content.opf" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?><package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="bookid"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="bookid">reader-sample</dc:identifier><dc:title>Reader Sample</dc:title><dc:language>en</dc:language></metadata><manifest><item id="chapter" href="chapter.xhtml" media-type="application/xhtml+xml"/></manifest><spine><itemref idref="chapter"/></spine></package>
EOF
    cat > "$tmp/OEBPS/chapter.xhtml" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?><html xmlns="http://www.w3.org/1999/xhtml"><head><title>Reader Sample</title></head><body><h1>Reader Sample</h1><p>This EPUB is seeded for the Blazor reader smoke test.</p><p>Use the next and previous controls to move through the reader.</p></body></html>
EOF
    python3 - "$tmp" "$MEDIA/books/Reader Sample.epub" <<'PY'
import sys
import zipfile
from pathlib import Path

source, target = map(Path, sys.argv[1:])
with zipfile.ZipFile(target, "w") as archive:
    archive.write(source / "mimetype", "mimetype", compress_type=zipfile.ZIP_STORED)
    for path in sorted((source / "META-INF").glob("**/*")):
        if path.is_file():
            archive.write(path, path.relative_to(source).as_posix(), compress_type=zipfile.ZIP_DEFLATED)
    for path in sorted((source / "OEBPS").glob("**/*")):
        if path.is_file():
            archive.write(path, path.relative_to(source).as_posix(), compress_type=zipfile.ZIP_DEFLATED)
PY
    rm -rf "$tmp"
    trap - EXIT
fi

# A tiny PDF with one text page, and a CBZ reusing the generated JPEG fixtures.
[ -f "$MEDIA/books/Reader Sample.pdf" ] || python3 - "$MEDIA/books/Reader Sample.pdf" <<'PY'
import sys

content = b"BT /F1 24 Tf 72 720 Td (Reader Sample PDF) Tj ET\n"
objects = [
    b"<</Type/Catalog/Pages 2 0 R>>",
    b"<</Type/Pages/Count 1/Kids[3 0 R]>>",
    b"<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>",
    b"<</Length " + str(len(content)).encode() + b">>\nstream\n" + content + b"endstream",
    b"<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
]
data = b"%PDF-1.4\n"
offsets = [0]
for number, obj in enumerate(objects, 1):
    offsets.append(len(data))
    data += f"{number} 0 obj\n".encode() + obj + b"\nendobj\n"
xref = len(data)
data += b"xref\n0 6\n0000000000 65535 f \n"
data += b"".join(f"{offset:010d} 00000 n \n".encode() for offset in offsets[1:])
data += b"trailer\n<</Size 6/Root 1 0 R>>\nstartxref\n" + str(xref).encode() + b"\n%%EOF\n"
open(sys.argv[1], "wb").write(data)
PY
[ -f "$MEDIA/books/Comic Sample.cbz" ] || python3 - "$MEDIA/photos/Slideshow" "$MEDIA/books/Comic Sample.cbz" <<'PY'
import sys
import zipfile
from pathlib import Path

source, target = map(Path, sys.argv[1:])
with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(source.glob("*.jpg")):
        archive.write(path, path.name)
PY
touch "$STAMP"
