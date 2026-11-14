# Stores with several PCs: the store server design

Branch `online/platform`. Agreed 2026-11-06. **Design only: nothing in sections 3 to 6 is built yet.** What exists today is
described in `PLATFORM.md` (one PC, one-way copy to the cloud) and `HOW-TO-CONNECT.html`.

## 1. The rule

> **Each store has one main database, on one PC in the store (the store server). The tills talk to it over the shop
> network. Only the store server talks to the cloud.**

```
 Cloud (your server)        owner portal, backup, reports, remote edits
        ▲   syncs whenever there is internet
        │
 Store server PC            the store's main database, always
   ▲        ▲        ▲      shop network: works with no internet
 Till 1   Till 2   Till 3
```

Why this and not "the cloud is the main database and PCs switch to the network when the internet drops": a switch means two
places can accept sales for the same stock for a while, and merging them afterwards is where data gets lost. Here nothing
switches. An internet cut does not touch the tills, because they never depended on it.

## 2. Who owns what (this is what avoids merge conflicts)

| Data | Owner | Direction |
|---|---|---|
| Sales, receipts, orders, cash drawer, stock, customer and supplier balances | The store server | Up to the cloud (the history is only ever added to) |
| Products, prices, categories | Edited at the store, or by the owner remotely | Remote edits go **down** to the store server and are applied there, on the one database |
| Staff, passwords, PINs | The store server | **Never** leave the shop |

Consequences:
- **No GUID migration is needed.** Each store still has exactly one database, so integer ids and every link stay as they are.
  GUIDs would only be needed for true multi-PC merging, which this design deliberately avoids.
- The cloud keeps **one copy per store** (as today), never one shared database for all clients.

## 3. The portal's part

- A store (today: an "installation") gets a list of **devices**. One is marked **store server**; the others are **tills**.
- Each till is registered with a one-time link, like the server today. The link tells the till where its server is.
- The server reports its network address; tills keep it cached, so they still find it when the internet is down.
- Only the store server holds the cloud credential. Tills never need one.

## 4. When things fail

| Failure | What happens |
|---|---|
| Internet down | Nothing changes in the shop. The store server syncs when the internet returns. |
| Power cut | Server and tills go down together. A UPS on the server PC and the router keeps the network alive. |
| Store server PC dies | Tills cannot sell. Fix: a **standby PC** that continuously copies the server over the shop network and is promoted with a **manual** button. |

Why manual promotion: automatic takeover can leave two servers each accepting sales (the problem we are avoiding). The standby
lags by a few seconds, so a sudden crash can lose the last sales that were not yet copied. That window must be shown to the owner.

## 5. Things this design has not solved yet (decide before building)

1. **Printing and the cash drawer.** Receipt printing and the drawer pulse run on the machine that runs the server (decision
   D11 of the React migration). With tills as screens, a receipt would print at the **server PC**. Each till needs its own
   printer: the server must hand the print job to the till's own NexPOS app, which prints it locally. This is real work.
2. **Licensing and pricing.** The licence is bound to one PC and checked by the server. A server with three tills is one
   licensed PC and three screens. Whether that is one licence or three is a **business decision**, and the code to count
   tills does not exist.
3. **Securing the shop network.** Tills would send PINs to the server over the LAN. Plain http on a private network is the
   simplest; certificates (or a pinned key) are better. Undecided.
4. **Finding the server.** A fixed address handed out by the portal is simplest; automatic discovery is nicer and can fail on
   some routers. Undecided.
5. **SQLite as the store server's database.** Fine for a few tills in principle (one process owns the file), not measured here.
6. **Selling when the server is down.** Out of scope: no till-level offline queue in the first versions.

## 6. Order of work

1. **Automatic backup before every upgrade, with a check afterwards** (built, see `PLATFORM.md` section 5a).
2. **Store server mode:** the server PC listens on the shop network, other PCs open it as tills, the portal records which PC is
   the server. Includes item 5.1 (printing from tills), which decides whether this is "small" or not.
3. **Owner read-only view** (what clients pay for).
4. **Standby PC with manual promotion.**
5. **Price and product changes pushed down from the cloud.**
6. **Till-level offline selling**, only if clients ask.

Not planned: multi-master sync with GUIDs.
