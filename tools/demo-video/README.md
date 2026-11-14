# Coduo POS demo video

Records a ~4 minute client walkthrough (sign in → open till → sales → customers → orders →
dashboard → inventory → reports → close shift → Arabic) with on-screen captions.

```powershell
powershell -File tools\demo-video\record.ps1            # add -SkipBuild to reuse the last build
```

Output: `tools\demo-video\out\CoduoPOS-Demo.mp4` (the script also leaves `raw.webm` and `demo.db` there).

It is fully isolated from your real data: the app runs against `out\demo.db`
(`NEXPOS_DB_PATH`), is seeded by `DemoDataSeeder` (`--seed-demo`), and `NEXPOS_DEMO=1`
swaps the receipt printer for a no-op. Logs in as `layla` / PIN `1234`.

To change the wording or flow, edit the scenes in `record-demo.js` (`ONLY=login,pos node record-demo.js`
re-records just some scenes against an already-running server). Closing-card text: `OUTRO_*` at the top.

## Demo data only (no video)

```powershell
$env:NEXPOS_DB_PATH = "C:\path\to\some.db"   # omit to seed your normal %LOCALAPPDATA%\NexPOS database
dotnet run --project Web -- --seed-demo
```
Idempotent - does nothing if the demo catalogue is already there. Logins: `layla`/1234 (Admin),
`sara`/1111 and `omar`/2222 (Cashiers).
