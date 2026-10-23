# Building a New Installer After a Change

Do these steps every time you change the program and want to give users a new `NexPos Setup X.Y.Z.exe`.

---

## 1. Make and test your change

Run the app normally (Visual Studio / `dotnet run --project Web`) and check that your change works.

## 2. Did you change the database? Add a migration

If you added or changed an **entity**, a **property**, or anything in `ErpDbContext`, create a migration:

```powershell
dotnet ef migrations add <ShortDescriptiveName> --project Infrastructure --startup-project Web
```

Example: `dotnet ef migrations add AddSupplierPhone --project Infrastructure --startup-project Web`

- Commit the new files in `Infrastructure/Migrations/`.
- You **don't** have to do anything on the customer's PC. The app runs `Database.Migrate()` on startup, so an existing database is upgraded automatically the first time the new version opens.
- **Never delete or edit a migration that has already shipped in an installer.** Customer databases depend on it. Add a new migration instead.

Skip this step if you only changed views, CSS, JS, translations, or service logic.

## 3. Bump the version number

Open `package.json` and increase `"version"`:

```json
"version": "1.0.1",
```

| Kind of change              | Example           |
|-----------------------------|-------------------|
| Bug fix, small tweak        | `1.0.0` → `1.0.1` |
| New feature                 | `1.0.1` → `1.1.0` |
| Big / breaking change       | `1.1.0` → `2.0.0` |

The version appears in the installer's file name and in Windows "Apps & features". If you don't bump it, the new installer has the same name as the old one and you can't tell them apart.

## 4. Close NexPos

Close any running NexPos window, and stop the app in Visual Studio if it's running. Otherwise the build can fail because files are locked.

## 5. Build

From the repo root, in PowerShell:

```powershell
.\build-electron.ps1 win-installer
```

This will:
1. Publish the .NET app (self-contained, so the customer PC doesn't need .NET installed) to `publish\app`
2. Install npm packages if they're missing
3. Package everything into an installer

It takes a few minutes. If PowerShell refuses to run the script, use:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-electron.ps1 win-installer
```

## 6. Get the installer

The new installer is in the **`dist`** folder:

```
dist\NexPos Setup 1.0.1.exe
```

That's the only file you give to users. The rest of `dist` (`win-unpacked`, `.blockmap`, `.yml`) is build output you can ignore.

## 7. Test it before you send it

1. Run the new `NexPos Setup X.Y.Z.exe` on your machine, over the top of the previous version.
2. Open NexPos and check that:
   - it starts and shows the login page
   - your change is there
   - your existing data (products, customers, sales) is still there
3. Ideally, also test on a second PC that never had NexPos installed.

## 8. Commit

```powershell
git add -A
git commit -m "Release 1.0.1: <what changed>"
git tag v1.0.1
```

Tagging each release lets you find the exact code behind any installer later.

---

## What happens on the customer's PC

| Thing                  | Location                                   | Kept on update/uninstall? |
|------------------------|--------------------------------------------|---------------------------|
| Program files          | `%LocalAppData%\Programs\NexPos` (default) | Replaced by the update    |
| Database (`nexpos.db`) | `%LocalAppData%\NexPOS\`                   | **Kept**                  |
| Login key (`jwt.key`)  | `%AppData%\NexPos\`                        | **Kept**                  |
| Logs                   | `%AppData%\NexPos\logs\`                   | **Kept**                  |

Installing a new version over an old one keeps all the customer's data. To troubleshoot a problem on their PC, ask them for the newest file in `%AppData%\NexPos\logs\`.

---

## Licensing a customer's PC

Release builds only let a shop sell once the PC has a license key (Debug builds never ask for one). Do this on every new install, and again when a customer renews or replaces their PC:

1. Install NexPos on the customer's PC and open it. Go to **License** in the sidebar (or `http://localhost:5000/en/License`; no login needed). It shows the PC's **Machine ID** (`XXXXX-XXXXX-XXXXX-XXXXX`). Have the customer copy it to you, or read it over AnyDesk.
2. On **your** PC, from the repo root:

   ```powershell
   dotnet run --project tools/LicenseIssuer -- issue --machine <MACHINE-ID> --customer "<shop name>" --kind full
   ```

   | `--kind` | Meaning | Default length |
   |---|---|---|
   | `full` | Perpetual till. The date is the end of the **updates plan**; builds compiled after it will not run. | 365 days |
   | `provisional` | Expires. For instalment sales: issue the `full` key after the last payment. | 45 days |
   | `trial` | Expires. For evaluations. | 14 days |

   Use `--days N` or `--until 2027-12-31` to change the length.
3. Send the printed key (`NXL1-...`) to the customer. They paste it into the License page and press **Activate**. No internet needed.

For a time-limited key the app warns for the **7 days before** it expires, warns again for **7 days after** (selling still works), and then stops the POS and new sales. Reports, products, orders and backups keep working and no data is touched. Renewing is just issuing a new key and pasting it in.

If a customer reinstalls Windows or swaps a disk the key usually keeps working (2 of the PC's 4 hardware fingerprints must still match). If they get a new PC, issue a new key for the new Machine ID and move their data with Backup/Restore.

**Keep `%USERPROFILE%\.nexpos-license\private.pem` safe**: copy it to two offline places (USB stick, password manager). If you lose it you cannot issue licenses; if it leaks anyone can. `issued.csv` next to it lists everything you issued. Never commit either file.

---

## Troubleshooting

**"Windows protected your PC" when running the installer.**
The installer isn't code-signed, so Windows SmartScreen warns about it. Click **More info → Run anyway**. The only way to remove the warning is to buy a code-signing certificate.

**Build fails with "file is being used by another process" / access denied.**
NexPos or `Web.exe` is still running. Close it (check Task Manager for `Web.exe`), delete the `dist` and `publish` folders, and build again.

**Build fails with "Cannot create symbolic link" (winCodeSign).**
Turn on Windows **Developer Mode** (Settings → System → For developers), or run PowerShell as Administrator once, then build again.

**The installed app shows "NexPos failed to start".**
Read the newest log in `%AppData%\NexPos\logs\`. A common cause is a broken migration: run the app from Visual Studio against a copy of the database to reproduce it.

**Port 5000 already in use.**
Another program (or a leftover `Web.exe`) is using port 5000. End `Web.exe` in Task Manager, or change `"Urls"` in `Web/appsettings.json` **and** `APP_URL` in `electron-main.js` to the same new port, then rebuild.

---

## Quick checklist

- [ ] Change made and tested
- [ ] Migration added (only if the database changed)
- [ ] `version` bumped in `package.json`
- [ ] NexPos / Visual Studio app closed
- [ ] `.\build-electron.ps1 win-installer`
- [ ] Installed `dist\NexPos Setup X.Y.Z.exe` over the old version and tested it
- [ ] Only give this installer to customers whose updates plan runs past today (a `full` key refuses builds compiled after its "updates until" date)
- [ ] Committed and tagged `vX.Y.Z`
