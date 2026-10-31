# Baseline — current Razor/Metronic app (before the React migration)

Measured 2026-10-31 for decision D12. Re-run the same way for `result.md` at cutover.

**Setup:** isolated demo DB (`--seed-demo`), Kestrel on localhost:5055, built from the `master` working tree
(BoussiCell restyle included), headless Chromium via Playwright, viewport 1366×768, logged in as the Admin demo user
with the till open. Medians of 5 runs. "Ready" = first navigation request → the page's main content
(`.product-card` / `#salesChart` / first table row) is visible.

**Caveat:** localhost has no network latency, so absolute milliseconds are far better than on a counter PC. Use
the *ratios* and the *byte/request counts* for before/after, and re-measure on the real hardware for the D12 timing
target ("route change < 100 ms on the counter PC").

## Warm cache (second and later visits — the normal case at a till)

| Page | ready (ms) | DCL | load | requests | JS KB | CSS KB | fonts KB | HTML KB |
|---|---|---|---|---|---|---|---|---|
| POS | 66 | 40 | 47 | 38 | 396 | 136 | 253 | 9 |
| Dashboard | 112 | 49 | 64 | 36 | 261 | 271 | 253 | 13 |
| Products list | 300 | 71 | 81 | 40 | 287 | 273 | 253 | 16 |
| In-app navigation, Dashboard → Products (sidebar click) | 352 | – | – | – | – | – | – | – |

JS/CSS/font sizes are the decoded response bodies per page (`encodedBodySize`; Kestrel isn't compressing on localhost,
so these are *uncompressed* — compare against the SPA's raw size, not its gzip size).

## Cold cache (first visit after install / cache clear)

| Page | ready (ms) | body KB | requests |
|---|---|---|---|
| POS | 217 | 435 | 75 |
| Dashboard | 293 | 926 | 36 |
| Products list | 323 | 303 | 40 |

## What this says

- Every navigation in the Razor app is a full document load: ~36–40 requests, 0.4–0.9 MB of JS/CSS/fonts parsed again
  (jQuery, Bootstrap, select2, Font Awesome, Metronic, Chart.js on the dashboard).
- The navigation Dashboard → Products takes 352 ms even on localhost, of which the products query/table render is most.
- The SPA target: one ~100 KB-gzip shell load, then JSON only on navigation (no document, no CSS/JS re-parse).

## Reproduce

Follow the `nexpos-ui-audit-runbook` memory (build to a scratch dir, run with `NEXPOS_DB_PATH`, `--seed-demo`, port 5055).
Login is the PIN pad: click `.signin-user[data-username="layla"]`, then `button[data-digit]` 1-2-3-4; open the till at
`/en/CashDrawer/Open`. The measuring scripts were throw-away (scratchpad); the method is the table caption above.
