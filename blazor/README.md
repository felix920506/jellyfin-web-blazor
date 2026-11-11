# Jellyfin Web: Blazor WebAssembly port

A port of the Jellyfin web client (`../src`, React + legacy controllers) to Blazor WebAssembly
on .NET 10. Work in progress; see `../docs/blazor-port/ARCHITECTURE.md` for the design and the
working rules, and the other files in that folder for the behaviour specifications.

Everything runs in Docker through `./dev` (run it without arguments for the command list):

```
./dev check                 # build (warnings are errors) + tests
scripts/test-server.sh up   # a throwaway Jellyfin server on http://localhost:18096 (admin / jellyfin)
./dev serve                 # the app on http://localhost:5080
./dev e2e smoke.py          # browser smoke test (Playwright in Docker)
```
