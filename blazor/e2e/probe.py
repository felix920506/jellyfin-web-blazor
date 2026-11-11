"""Opens a URL (argv[1], an app hash like '#/home'), waits, prints console output and saves a screenshot."""
import os
import sys

from playwright.sync_api import sync_playwright

APP = os.environ.get("APP_URL", "http://localhost:5080")
OUT = os.environ.get("OUT_DIR", "/out")
target = sys.argv[1] if len(sys.argv) > 1 else ""
wait_ms = int(sys.argv[2]) if len(sys.argv) > 2 else 15000

with sync_playwright() as p:
    browser = p.chromium.launch()
    page = browser.new_page(viewport={"width": 1366, "height": 768})
    page.on("console", lambda m: print(f"[{m.type}] {m.text}"[:600]))
    page.on("pageerror", lambda e: print("[pageerror]", str(e)[:600]))
    page.on("requestfailed", lambda r: print("[requestfailed]", r.url, r.failure))
    page.on("response", lambda r: print("[http]", r.status, r.url) if r.status >= 400 else None)
    page.goto(APP + "/" + target)
    page.wait_for_timeout(wait_ms)
    print("hash:", page.evaluate("location.hash"))
    print("body:", page.inner_text("body")[:500])
    page.screenshot(path=f"{OUT}/probe.png")
    browser.close()
