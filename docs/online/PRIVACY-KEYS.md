# Private data: the provider cannot read a shop's data

Branch `online/platform`. Agreed 2026-11-07 (the proposals in section 6 were accepted). **Steps 1 and 2 of section 4 are
built**: a shop PC seals what the cloud does not need, behind a recovery slip the owner confirms. Steps 3 to 5 (owner access and
the phone app, assisted recovery, link my phone) are not built.
It builds on `PLATFORM.md` (how the copy reaches the cloud) and `STORE-SERVER.md` (several PCs, one main database).
A picture version of this page: `../visual/PRIVACY-KEYS.html` (open it in a browser).

## 1. In plain words

Today every shop PC sends its data to the cloud as readable data. The portal only shows counts, but nothing stops someone
with access to the cloud server from opening the files and reading customers, orders and receipts. "We don't look" is a
promise, not a protection.

The fix is a locked box:

- The shop PC locks the sensitive data before it leaves the shop. The key is made on that PC.
- The cloud stores the locked data. It cannot open it, and neither can you.
- The owner's phone gets a copy of the key by scanning a QR code shown on the shop PC (like linking WhatsApp Web). The phone
  downloads the locked data and opens it on the phone.

The owner loses nothing: the shop PC keeps working with its own data exactly as now, and the phone can read everything.

**The risk and how it is handled.** Owners lose things. A lost phone is harmless (scan the QR again from the PC). The one
bad case is: PC lost **and** no phone linked **and** recovery slip lost. Then the cloud copy cannot be read by anyone. To
make that very unlikely:

1. A **recovery slip** is printed when Online is switched on (receipt printer or PDF) and the owner must confirm they kept it.
2. **Assisted recovery** is an explicit tick at set-up. If ticked, a copy of the key sealed so that only you can open it is
   stored in the cloud, and you can help the owner after a disaster. If not ticked, nobody can. It is the owner's informed
   choice and can be switched off later.
3. A 4-digit PIN is **never** used to protect the key (10,000 guesses). The recovery code is long and random.

## 2. What the cloud can and cannot see afterwards

| | Cloud can read |
|---|---|
| Customers, balances, debt history | No (sealed) |
| Orders, receipts, receipt lines and payments, cash drawer sessions and drops | No (sealed) |
| Stock history, the shop's address, phone and tax number | No (sealed) |
| Purchases and suppliers | No (sealed) |
| Staff accounts (names, roles) | No (sealed) |
| Product **cost price** | No (sealed) |
| Products, categories, selling prices, taxes, payment methods | **Yes**, on purpose: the owner can change prices remotely later |
| Table names, row numbers, how many rows, when changes arrive and how big | **Yes**: unavoidable metadata |
| Whether the shop is connected, the 24-hour strip, subscription dates | Yes |

The portal keeps showing product and order **counts**. They come from counting sealed rows, so they stay available without
reading anything.

**Rule for the future: a table is sealed unless it is on an explicit "readable" list.** A new table added next year is
private by default, and a test fails if a table is in neither list (section 5).

What this does **not** protect against (state it to clients honestly):
- Someone with server access could **delete or roll back** sealed rows. They cannot read or edit them (a tampered row fails
  to open), but missing rows go unnoticed unless the phone app compares counts with the shop.
- The key is only as safe as the places it is kept: the shop PC, the owner's phone and the slip.
- A browser page served by your own server is weaker than a phone app: you could in principle change the page to capture the
  key. The installed phone app does not have that weakness (section 4, step 3).
- Anything already uploaded unsealed (the current test shops) stays in old disk copies and server backups. Seal before real
  clients connect.

## 3. How it works

**Key.** One random 256-bit key per branch, made on the shop PC when Online is first switched on. Stored beside the database,
protected with Windows DPAPI (machine scope, so it also works under IIS). It is **not** part of the shop's backups: a
restore on a new PC asks for the recovery slip (or a linked phone's QR) when Online is on.

**Recovery code = the key itself**, printed as 14 groups of 4 letters and digits: 13 carry the key, the 14th is a check, so a
typing mistake is caught instead of making a wrong key (`Sync/RecoveryCode.cs`; base32 without 0, 1, 8, 9, and those are
read as O, I, B when typed). A QR code of the same code comes with the phone link (step 5). No password-based derivation,
nothing for the cloud to hold, nothing to guess.

**Sealing a row.** The sync is state-based: each change names a row and carries its current values (`SyncChange`). For a
sealed row the PC takes those same values, encrypts them with AES-256-GCM and sends only the result:

```
SyncChange(Table, Id, Deleted, Values = null, Sealed = base64(version | keyId | nonce | ciphertext | tag))
```

- `nonce`: fresh random 12 bytes per row version.
- Bound data (authenticated, not secret): `keyId | table | rowId`. The cloud cannot move a sealed row to another id or
  table, or relabel it with another key id, without it failing to open. The shop PC does not know the cloud's site id, but
  each shop has its own key, which already stops a row being moved to another shop.
- `Deleted` changes carry no values, as today.
- Mixed tables (`Products`): the readable columns travel as `Values`, only `CostPrice` goes in `Sealed`.
- `SyncBatch` gets a `Format` number (1 = readable as before, 2 = sealed). A cloud that does not know the format refuses
  the batch loudly instead of storing rows with no values.
- **The cloud checks shapes even though it has no key.** In a format-2 batch it refuses a fully sealed table sent in the
  clear, a sealed column sent in the clear, a sealed part that is missing, and a malformed sealed value. So a till bug cannot
  leak a sealed table through a batch that claims to be sealed. (A till that sends format 1 sends readable rows; that is the
  shop's own setting, off to on is a step-2 flow.)

**Storing it in the cloud.** The mirror stays a database with the shop's own schema for readable tables. Sealed rows go into
one extra table created by the cloud itself (not part of the till's migrations):

```
SealedRows(TableName TEXT, Id INTEGER, KeyId TEXT, Blob TEXT /* base64 */, PRIMARY KEY(TableName, Id))
```

Fully sealed tables stay empty in their typed form. An **initial** batch (full resync) also empties `SealedRows`. Portal counts
for sealed tables come from `SealedRows` (the larger of the two counts, so a shop that was readable before and sealed since
shows the right number until its resync).

**Phone link.** On the shop PC: Settings, Online, "Link my phone". The PC asks for the signed-in user's PIN again, then shows
a QR containing the key and the cloud address, and writes an entry to the audit trail. The phone app scans it, keeps the key
in the phone's secure storage, signs in to the cloud as the owner, downloads sealed rows and opens them locally. A second or
a new phone is the same scan.

**Assisted recovery (only if ticked).** The PC seals a copy of the key to **your recovery public key** (RSA-3072 OAEP,
SHA-256) and uploads that blob. The matching private key lives only on an offline USB stick in a safe; it is never on the
cloud server. To help a client you run an offline recovery tool, check who is asking, and give them back a recovery slip.
Then you write a "Recovery performed" entry on that client's history in the portal. That entry is a procedure, not a
technical lock: be clear about it. Switching the tick off on the PC deletes the blob from the cloud (copies already in server
backups cannot be proved gone).

**Turning it on for a shop that is already connected** (your Main till): after the upgrade the PC makes the key, shows the
recovery slip step, and only after the owner confirms does it switch sealing on and request a **full resync**, which wipes
the readable copy in the cloud and uploads it sealed. New connections are always sealed. There is no "unsealed" option.

## 4. Order of work

Each step ends in something that works and is tested on its own.

1. **Seal and store.** **Built 2026-11-07, switched off.** Key creation and protection, readable/sealed lists, `Sealed` in the
   wire format, `SealedRows` and the changed counts in the cloud, tests. No screens yet: with the flag on, the key exists, data
   is sealed, counts still work. Where it lives: `Sync/SealPolicy.cs` (the lists), `Sync/RowSealer.cs` (AES-GCM),
   `Sync/SealKeyStore.cs` (key file `online.key` beside `online.json`, DPAPI machine scope on Windows),
   `Sync/SyncPusher.cs` (sealing), `Sync/MirrorApplier.cs` (validation and `SealedRows`), `Cloud.Tests/SealingTests.cs`.
   It only takes effect once the owner has confirmed the slip (step 2): a key with no recovery slip can strand the cloud
   copy. If the key cannot be opened while sealing is wanted, the till uploads **nothing** and reports `seal_key` (or
   `seal_key_missing` when this PC has no key); it never falls back to readable and never makes a new key by itself.
2. **Shop PC screens.** **Built 2026-11-07.** Recovery slip (printed on the receipt printer or a PDF, never `window.print()`),
   the "I kept it" confirmation, a full re-upload on first switch-on, and the code prompt on a restore. The portal shows "Sealed,
   key `ab12...`" per branch and a sentence in "What the cloud holds".
   - **Screen:** Settings, Online, a *Privacy* card with four steps: `off` (a connected shop is offered "Turn on privacy"),
     `needsSlip` (nothing is uploaded until the slip is kept), `keyMissing` (confirmed, but this PC has no key: enter the
     recovery code, or make a new key knowing the old cloud copy is then lost) and `on`. Code: `client/src/modules/settings/SealingFlow.tsx`.
   - **PIN:** showing, printing or saving the code, and restoring a key, take the signed-in user's PIN in the request itself;
     five wrong PINs lock the check for five minutes. The audit trail records who showed or printed it, who failed, who confirmed and
     who restored (`RecoverySlipShown`, `RecoverySlipFailed`, `SealingConfirmed`, `SealKeyRestored`), never the code.
   - **Every new connection is sealed.** Redeeming a link switches `SealData` on, and the shop uploads nothing until the slip is
     confirmed. The setting `Online:SealOnConnect` (default true) is not on any screen: it exists for the test rigs and for
     diagnosing with support.
   - **The re-upload replaces the readable copy for real.** The cloud's mirror deletes with `secure_delete` on and truncates its
     write-ahead log after an initial batch; a test proves the old readable text is in neither the file nor the log afterwards.
   - **Not in step 2, on purpose:** the *assisted recovery* tick (it does nothing until step 4 can upload a sealed key, and a
     control that does nothing is worse than none), and the QR code on the slip (it arrives with the phone link, step 5; the slip
     prints the code as text, which is what a restore types).
   - **Not looked at yet:** the slip's PDF and receipt layout in a viewer (the tests check that a PDF is produced, not how it
     looks), and the Arabic wording (not proofread by a native speaker).
3. **Owner access.** The portal today is for the provider only. Owners need their own sign-in and a read API for sealed
   rows. Build the **phone app** against it. A small browser viewer is possible but weaker (section 2); if built, say so on
   the page and keep it optional.
4. **Assisted recovery.** The provider key pair, the offline recovery tool, the upload on the PC and the portal entry.
5. **Link my phone** QR on the PC (can come together with step 3).

Steps 1 and 2 already stop you reading client data, even before the phone app exists. Nobody reads the sealed copy yet, so
nothing is lost in the meantime.

## 5. Tests that must exist

- Round trip: seal then open gives the same values (strings, decimals, nulls, dates, bytes).
- Wrong key fails. A row moved to another id, table or site fails. A flipped byte fails.
- **Raw-file check:** push a shop with a known customer name, then read the mirror `.db` file's raw bytes and assert the name
  and the amounts are not in it.
- Every table of the EF model is in exactly one list (excluded, readable, sealed); adding a table without choosing fails the build.
- An old cloud refuses a `Format` it does not know; a new cloud still accepts unsealed batches only while the feature is off.
- Resync wipes `SealedRows`. Counts of sealed tables are right after a push and after deletes.
- A restore on a new PC with a wrong or right recovery code. Assisted recovery: seal, offline open, same key.
- Backups do not contain the key file.

## 6. Decisions (taken 2026-11-07 by accepting the proposals; change any of them by saying so)

1. **Product cost price: sealed.** It shows the shop's margin. Selling prices stay readable.
2. **Staff accounts: sealed**, together with roles and permissions. Names are personal data; the portal does not need them.
3. **New connections: always sealed, no opt-out** (applies from step 2, when the screens exist).
4. **`StoreSettings` and `ProductHistories`: sealed.** Checked: settings hold the shop's address, phone, tax id and registration
   number; history holds stock movements, order links and staff names. The cash drawer tables are sealed too (found by the
   "every table has a decision" test, which is what it is for).
5. **Key protection on the PC: Windows DPAPI, machine scope.** Still to be checked on the `F:\SItes\ERP` IIS site (its pool
   identity has no profile), which is why the key file is machine-scoped and not user-scoped.
6. **Browser viewer: not planned.** Phone app first.
