#!/usr/bin/env bash
# Produces everything under src/Jellyfin.Web/wwwroot/generated (git-ignored):
#   strings/   translation dictionaries from the original client (src/strings)
#   assets/    images, audio, splash screens from the original client (src/assets)
#   lib/       third-party browser libraries and fonts, fetched from the npm registry
#   css/       the original SCSS (compiled unchanged) plus this port's own styles
#   themes/    one stylesheet per theme id
# Runs inside the toolchain container (see ../dev). Safe to re-run; work is skipped when up to date.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

REPO=..
SRC="$REPO/src"
APP=src/Jellyfin.Web
OUT="$APP/wwwroot/generated"
VENDOR_CACHE="${VENDOR_CACHE:-$PWD/artifacts/vendor-cache}"
mkdir -p "$OUT"/{css,lib,strings,assets,themes} "$VENDOR_CACHE"

# --- 1. strings and static assets -------------------------------------------------------------
cp -u "$SRC"/strings/*.json "$OUT/strings/"
cp -ru "$SRC"/assets/. "$OUT/assets/"

# --- 2. vendor libraries ----------------------------------------------------------------------
# vendor <npm package> <version> <dest dir under lib/> <tar wildcard>...
vendor() {
    local pkg="$1" ver="$2" dest="$OUT/lib/$3"; shift 3
    local name="${pkg##*/}" flat="${pkg//\//__}"
    local tgz="$VENDOR_CACHE/$flat-$ver.tgz" stamp="$dest/.vendor-$ver"
    [ -f "$stamp" ] && return 0
    if [ ! -f "$tgz" ]; then
        echo "[prepare] fetching $pkg@$ver"
        curl -fsSL --retry 3 -o "$tgz.$$" "https://registry.npmjs.org/$pkg/-/$name-$ver.tgz"
        mv "$tgz.$$" "$tgz"
    fi
    rm -rf "$dest"; mkdir -p "$dest"
    tar -xzf "$tgz" -C "$dest" --strip-components=1 --wildcards "$@"
    touch "$stamp"
}

# Versions mirror the original client's package.json.
vendor material-design-icons-iconfont 6.7.0 material-design-icons-iconfont \
    'package/dist/material-design-icons.css' 'package/dist/fonts/*.woff2' 'package/dist/fonts/*.woff'
vendor @jellyfin/ux-web 1.0.0 jellyfin-ux-web 'package/*.png' 'package/favicons/*'
vendor hls.js 1.7.4 hls.js 'package/dist/hls.min.js'
vendor markdown-it 14.3.3 markdown-it 'package/dist/markdown-it.min.js'
vendor dompurify 2.5.9 dompurify 'package/dist/purify.min.js'
for font in noto-sans noto-sans-hk noto-sans-jp noto-sans-kr noto-sans-sc noto-sans-tc; do
    vendor "@fontsource/$font" 5.3.0 "fontsource/$font" \
        'package/files/*-400-normal.woff2' 'package/files/*-700-normal.woff2'
done
# Add further libraries here (same helper) when a feature needs them.

# --- 3. stylesheets ---------------------------------------------------------------------------
# The original client imports component stylesheets from JS on demand. Here every stylesheet is
# compiled into one bundle, in a stable order: global styles first, then everything else.
css_inputs_hash() {
    { find "$SRC" "$APP/Styles" -name '*.scss' -newer "$OUT/css/.stamp" 2>/dev/null | head -1; } || true
}

if [ ! -f "$OUT/css/.stamp" ] || [ -n "$(css_inputs_hash)" ] || [ "${BASH_SOURCE[0]}" -nt "$OUT/css/.stamp" ]; then
    echo "[prepare] compiling stylesheets"
    entry="$APP/obj/app.generated.scss"
    mkdir -p "$(dirname "$entry")"
    {
        i=0
        emit() { i=$((i + 1)); echo "@use \"$1\" as m$i;"; }
        for first in styles/fonts styles/fonts.noto styles/site styles/flexstyles styles/scrollstyles \
                     styles/librarybrowser styles/dashboard styles/detailtable styles/livetv; do
            emit "$first"
        done
        # Element stylesheets come before component ones, as in the original (components import
        # the elements they use), so single-class component rules win over element defaults.
        ( cd "$SRC" && find elements -name '*.scss' ! -name '_*' | LC_ALL=C sort ) |
            while read -r f; do i=$((i + 1)); echo "@use \"${f%.scss}\" as e$i;"; done
        # Everything else, except partials, per-platform sheets, themes and the ones emitted above.
        ( cd "$SRC" && find components lib plugins apps styles -name '*.scss' \
              ! -name '_*' ! -path 'styles/noto-sans/*' \
              ! -path 'styles/fonts.scss' ! -path 'styles/fonts.noto.scss' ! -path 'styles/fonts.sized.scss' \
              ! -path 'styles/site.scss' ! -path 'styles/flexstyles.scss' ! -path 'styles/scrollstyles.scss' \
              ! -path 'styles/librarybrowser.scss' ! -path 'styles/dashboard.scss' \
              ! -path 'styles/detailtable.scss' ! -path 'styles/livetv.scss' \
              ! -path 'styles/ios.scss' ! -path 'styles/rtl.scss' | LC_ALL=C sort ) |
            while read -r f; do emit "${f%.scss}"; done
        # This port's own styles (every partial in Styles/port) come last so they can override.
        ( cd "$APP/Styles/port" && ls _*.scss | LC_ALL=C sort ) |
            while read -r f; do f="${f#_}"; f="${f%.scss}"; echo "@use \"port/$f\" as port-$f;"; done
    } > "$entry"

    sass_opts=(--no-source-map --style=compressed --quiet-deps
               --load-path="$SRC" --load-path="$SRC/styles" --load-path="$APP/Styles")
    # URL rewrites: webpack resolved these; here they point into generated/.
    rewrite() {
        sed -i -E \
            -e 's#url\("?~?@fontsource/([a-z-]+)/files/#url("../lib/fontsource/\1/files/#g' \
            -e 's#(url\("../lib/fontsource/[^)"]+\.woff2)\)#\1")#g' \
            -e 's#url\("?@jellyfin/ux-web/([^)"]+)"?\)#url("'"$2"'lib/jellyfin-ux-web/\1")#g' \
            -e 's#url\("?(\.\./)+assets/([^)"]+)"?\)#url("'"$2"'assets/\2")#g' \
            "$1"
    }

    sass "${sass_opts[@]}" "$entry" "$OUT/css/app.css"
    rewrite "$OUT/css/app.css" "../"
    sass "${sass_opts[@]}" "$SRC/styles/rtl.scss" "$OUT/css/rtl.css"

    for theme_dir in "$SRC"/themes/*/; do
        id="$(basename "$theme_dir")"
        case "$id" in _*) continue ;; esac
        [ -f "$theme_dir/theme.scss" ] || continue
        mkdir -p "$OUT/themes/$id"
        sass "${sass_opts[@]}" "$theme_dir/theme.scss" "$OUT/themes/$id/theme.css"
        rewrite "$OUT/themes/$id/theme.css" "../../"
        find "$theme_dir" -maxdepth 1 -type f \( -name '*.jpg' -o -name '*.png' -o -name '*.svg' \) \
            -exec cp -u {} "$OUT/themes/$id/" \;
    done
    touch "$OUT/css/.stamp"
fi
