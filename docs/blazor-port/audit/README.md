You are working in a git worktree of the Jellyfin web client. The original client (React + legacy JS) is in `src/`; its port to Blazor WebAssembly is in `blazor/` (C#/Razor under `blazor/src/Jellyfin.Web/`, a little JS under `wwwroot/js/`, docs in `docs/blazor-port/`, earlier coarse audit in `docs/blazor-port/99-coverage-audit.md`).

## Task: file-level parity audit of one slice of the original. Read-only for code; you write one document.
The slice (`__SCOPE__`) and the document name (`__NAME__`) are given in your task message.

For every source file in the slice (`.js`, `.jsx`, `.ts`, `.tsx`; skip `*.test.*`, pure type declarations and styles) determine, by reading the original AND the port's code (not by matching names), whether its behaviour exists in the port. Work through the files systematically (list them with `git ls-files`), and for each behaviour you think is missing, search the port (`grep -rn` over `blazor/src`) for the strings, API calls, CSS classes, setting keys and localisation keys the original uses before concluding that it is absent.

Write `docs/blazor-port/audit/__NAME__.md`:
1. A table with one row per original file: file | lines | what it does (a few words) | port counterpart (path) | status: `ported`, `partial`, `missing`, `not needed` (framework glue that Blazor replaces, e.g. React query hooks, webpack loaders, polyfills: say why in a few words).
2. For every `partial` and `missing` row a bullet list of the concrete behaviours that are absent, each specific enough to implement and verify (which setting, button, menu entry, keyboard shortcut, API call, edge case), with the original's file:line. Include TV-layout and multi-server behaviour as normal findings and tag them `[tv]` / `[multiserver]`; do not skip them as out of scope.
3. A final "Top gaps" list, ordered by user impact, with a rough size (S/M/L) each.

Be accurate rather than generous: an earlier audit marked things as ported that were only stubs. Equally, do not report something as missing when the port implements it under another name. Do not report styling nitpicks.

Rules: do not modify anything except your one document. No builds or tests are needed; do not start Docker containers, dev servers or subagents. Nothing on the host besides git, grep and reading files. No files in `/tmp`. Commit the document locally on your worktree branch (commit a first version early and extend it, so progress is not lost); never push.

Final message: at most 40 lines: counts per status, and the top gaps.
