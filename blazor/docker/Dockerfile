# Production image. The SDK stage deliberately repeats the small asset preparation step so this
# image can be built from a clean checkout, while ./dev image supplies the same publish settings.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG TARGETARCH
ARG DART_SASS_VERSION=1.107.0
RUN set -eux; \
    case "${TARGETARCH:-$(dpkg --print-architecture)}" in arm64) sass_arch=arm64 ;; amd64) sass_arch=x64 ;; *) exit 1 ;; esac; \
    curl -fsSL "https://github.com/sass/dart-sass/releases/download/${DART_SASS_VERSION}/dart-sass-${DART_SASS_VERSION}-linux-${sass_arch}.tar.gz" | tar -xz -C /opt; \
    ln -s /opt/dart-sass/sass /usr/local/bin/sass
WORKDIR /work
COPY src ./src
COPY blazor ./blazor
RUN cd blazor && VENDOR_CACHE=/root/.cache/vendor bash ./scripts/prepare-assets.sh && \
    dotnet publish src/Jellyfin.Web/Jellyfin.Web.csproj -c Release -o /out \
      -nodeReuse:false -p:UseSharedCompilation=false

FROM nginx:alpine-slim
COPY blazor/docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /out/wwwroot /usr/share/nginx/html
EXPOSE 8080
