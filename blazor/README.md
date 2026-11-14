# Jellyfin Web: Blazor WebAssembly port

A port of the Jellyfin web client (`../src`, React + legacy controllers) to Blazor WebAssembly
on .NET 10. Work in progress; see `../docs/blazor-port/ARCHITECTURE.md` for the design and the
working rules, and the other files in that folder for the behaviour specifications.

Everything runs in Docker through `./dev` (run it without arguments for the command list):

```
./dev check                 # build (warnings are errors) + tests
scripts/test-server.sh up   # a throwaway Jellyfin server with sample media on http://localhost:18096 (admin / jellyfin)
./dev serve                 # the app on http://localhost:5080
./dev e2e smoke.py          # browser smoke test (Playwright in Docker)
./dev e2e --publish smoke.py # smoke test the trimmed publish output
./dev image                 # build jellyfin-web-blazor:local (nginx on port 8080)
```

The production image serves the hash-routed static app on port 8080. Only the fingerprinted
`_framework` files are served with immutable cache headers, everything else is revalidated, and the precompressed Brotli/gzip assets are selected from `Accept-Encoding`.

## Preview builds

`.github/workflows/blazor-preview.yml` builds every push to `blazor-port` (after `./dev check`) and publishes:

- the static site on GitHub Pages;
- `jellyfin-web-blazor.zip` on the rolling `preview` pre-release;
- the container image `ghcr.io/<owner>/<repo>:preview` (`docker run -p 8080:8080 …`, amd64 and arm64).

The Pages site is served over HTTPS, so browsers only let it talk to Jellyfin servers that are
also reachable over HTTPS (or on `localhost`). For a server on plain HTTP use the zip or the image.
