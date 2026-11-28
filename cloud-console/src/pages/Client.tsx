import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api, type ClientDetail, type LinkResult, type LoginHandOver, type OwnerLogin } from '../api'
import { ago, clock, connectionTone, day, licencePill, money, planPill, until, utc } from '../format'
import { CountUp, Item, Stagger } from '../fx'
import { PaymentForm } from '../forms'
import { CopyButton, Count, Fold, Icon, LinkSlip, Loading, Pill, Problem, Pulse, useAction, useConfirm } from '../ui'
import { AnimatePresence } from 'motion/react'
import { MenuImport } from '../MenuImport'

const ROLES = [
  ['owner', 'Owner: everything, including profit and the cash drawer'],
  ['manager', 'Manager: sales, stock, customers and debts; can add and edit; no profit or cash drawer'],
  ['staff', 'Staff: sales and stock only, nothing can be changed'],
] as const

export function ClientPage() {
  const id = Number(useParams().id)
  const client = useQuery({ queryKey: ['client', id], queryFn: () => api.client(id) })
  const [link, setLink] = useState<LinkResult | null>(null)
  const [handOver, setHandOver] = useState<LoginHandOver | null>(null)
  const [importing, setImporting] = useState(false)
  useEffect(() => { if (location.hash) setTimeout(() => document.querySelector(location.hash)?.scrollIntoView({ behavior: 'smooth' }), 900) }, [])

  if (client.isPending) return <Loading />
  if (client.isError) return client.error && (client.error as { status?: number }).status === 404
    ? <div className="list"><div className="empty"><h3>Client not found</h3><Link className="btn secondary" to="/">Back to clients</Link></div></div>
    : <Problem error={client.error} retry={() => client.refetch()} />
  const c = client.data
  const menuOnly = c.hasOnline && !c.hasPos
  const contact = [c.phone, c.email].filter((x) => x && x.trim()).join(' · ')
  const editorUrl = `/owner/menu.html?client=${c.id}&from=console`

  return (
    <Stagger>
      <Item as="div" className="page-head">
        <div>
          <Link className="crumb" to="/"><Icon id="back" />Clients</Link>
          <h1>{c.name} {menuOnly && <Pill plain style={{ verticalAlign: 'middle' }}>online menu only</Pill>} {!c.isActive && <Pill plain style={{ verticalAlign: 'middle' }}>inactive</Pill>}</h1>
          <p className="sub">{contact || 'No contact details'}</p>
        </div>
      </Item>

      {link?.url && <Item as="div"><LinkSlip store={c.name} url={link.url} expires={day(utc(link.expiresAtUtc))} whatsApp={link.whatsAppUrl} /></Item>}

      {c.hasOnline ? (
        <Item as="div" className="g">
          <Subscription c={c} />
          {menuOnly ? <MenuOnly c={c} onLink={setLink} /> : <Branches c={c} />}
        </Item>
      ) : <PosOnly c={c} onLink={setLink} />}

      {(c.hasPos) && <Licences c={c} />}
      <Payments c={c} />
      {handOver && <HandOver h={handOver} menuOnly={menuOnly} onClose={() => setHandOver(null)} />}
      <Item className="card" aria-labelledby="menu">
        <h2 id="menu">Online menu <Pill tone={c.menuLive ? 'ok' : 'bad'} style={{ marginInlineStart: 6 }}>{c.menuLive ? 'live' : 'offline'}</Pill></h2>
        <p className="help">A public page with this shop's products, pictures and prices, shared by link or QR code. {menuOnly ? 'The owner fills it by hand.' : 'Fill it by hand, or link it to a branch so it follows the POS.'} It is part of Online: without Online months (after the grace week) it is offline.</p>
        <div className="row">
          <a className="btn secondary" href={editorUrl}><Icon id="book" />Open the menu editor</a>
          <button type="button" className="btn secondary" onClick={() => setImporting(true)}><Icon id="upload" />Import dishes from Excel</button>
        </div>
      </Item>
      <AnimatePresence>
        {importing && <MenuImport key="import" clientId={c.id} clientName={c.name} editorUrl={editorUrl} onClose={() => setImporting(false)} />}
      </AnimatePresence>
      <Logins c={c} menuOnly={menuOnly} onHandOver={setHandOver} />
      <Item as="div" className="grid2">
        <Details c={c} />
        <History id={c.id} />
      </Item>
    </Stagger>
  )
}

function Subscription({ c }: { c: ClientDetail }) {
  const { run, busy } = useAction()
  const confirm = useConfirm()
  const sub = c.subscription
  const menuOnly = c.hasOnline && !c.hasPos
  const [tone, label] = planPill(c.subscriptionState)
  const ends = utc(sub?.expiresAtUtc)
  const grace = utc(sub?.graceEndsAtUtc)
  const suspendToggle = async () => {
    const on = !sub?.suspended
    const ok = await confirm(on
      ? { title: 'Suspend Online?', yes: 'Suspend', text: menuOnly ? 'Their online menu goes offline until you resume.' : 'Their shop stops sending data to the cloud and their online menu goes offline. It keeps selling, and sends what it saved when you resume.' }
      : { title: 'Resume Online?', yes: 'Resume', danger: false, text: menuOnly ? 'Their online menu opens again.' : 'Their shop will be allowed to send data again, and their online menu opens again.' })
    if (ok) await run(() => api.suspend(c.id, on), on ? 'Online suspended.' : 'Online resumed.')
  }
  return (
    <section className="card s5" aria-labelledby="plan">
      <h2 id="plan">Online subscription</h2>
      <div className="row" style={{ justifyContent: 'space-between', alignItems: 'flex-start', gap: 20 }}>
        <div>
          <p style={{ margin: '0 0 4px' }}><Pill tone={tone}>{label}</Pill></p>
          {sub ? <>
            <p className="num" style={{ fontSize: 28, margin: '6px 0 0' }}>Ends <b>{day(ends)}</b></p>
            <p className="muted" style={{ margin: '2px 0 0' }}>
              {until(ends)}
              {c.subscriptionState === 'Grace' && <> · in the grace week: {menuOnly ? 'their menu stays open' : 'data is still accepted and their menu stays open'} until {day(grace)}</>}
              {c.subscriptionState === 'Expired' && <> · their online menu is offline until you add months</>}
            </p>
          </> : <p style={{ margin: '6px 0 0' }}>No subscription yet. {menuOnly ? 'Their online menu is offline.' : 'Their shop cannot send data and their online menu is offline.'} Add the months they paid for.</p>}
        </div>
        <div>
          <p className="muted" style={{ margin: '0 0 6px', fontSize: 13, fontWeight: 600 }}>Add time</p>
          <div className="row">
            {[1, 3, 6, 12].map((m) => <button key={m} type="button" className="btn secondary" disabled={busy}
              onClick={() => run(() => api.extend(c.id, m), `Added ${m} month${m === 1 ? '' : 's'} of Online.`)}>+{m} mo</button>)}
          </div>
          <p className="help">Renewing early keeps the time left. Renewing late starts today. Got paid? <a href="#payments">Record the payment</a>.</p>
        </div>
      </div>
      {sub && <button type="button" className={'btn sm ' + (sub.suspended ? 'secondary' : 'danger')} style={{ marginTop: 6 }} disabled={busy} onClick={suspendToggle}>{sub.suspended ? 'Resume' : 'Suspend'}</button>}
    </section>
  )
}

function MenuOnly({ c, onLink }: { c: ClientDetail; onLink: (l: LinkResult) => void }) {
  const { run, busy } = useAction()
  const confirm = useConfirm()
  return (
    <section className="card s7" aria-labelledby="h-menuonly">
      <div className="ph">
        <h2 id="h-menuonly">Online menu only</h2>
        <button type="button" className="btn secondary sm" disabled={busy} onClick={async () => {
          if (!await confirm({ title: 'They bought the POS?', yes: 'Add the POS', danger: false, text: 'They become POS + Online, keeping their months and their menu. Their first branch is added, with a connection link to send them; issue their licence key as usual.' })) return
          const r = await run(() => api.addPos(c.id), 'They are POS + Online now.')
          if (r?.link) onLink(r.link)
        }}>They bought the POS</button>
      </div>
      <p className="muted" style={{ margin: 0 }}>No Coduo POS: no branch to connect and no licence key. The owner edits the menu on the owner website with their phone login. The menu is part of their Online months: it stays up while those run (plus a grace week), then goes offline until you add more.</p>
    </section>
  )
}

function PosOnly({ c, onLink }: { c: ClientDetail; onLink: (l: LinkResult) => void }) {
  const { run, busy } = useAction()
  const confirm = useConfirm()
  return (
    <Item className="card" aria-labelledby="h-type">
      <div className="ph">
        <h2 id="h-type">POS only <Pill tone="offline" plain>offline</Pill></h2>
        <button type="button" className="btn secondary sm" disabled={busy} onClick={async () => {
          if (!await confirm({ title: 'Turn Online on?', yes: 'Turn on', danger: false, text: c.sites.length === 0 ? 'Their first branch is added, with a connection link to send them.' : 'Their branches are followed again.' })) return
          const r = await run(() => api.setOnline(c.id, true), 'Online is on.')
          if (r?.link) onLink(r.link)
        }}>They bought Online</button>
      </div>
      <p className="muted" style={{ margin: 0 }}>This shop runs Coduo POS on its own PC without the cloud. There is nothing to connect; the portal follows their licence and payments.</p>
    </Item>
  )
}

function Branches({ c }: { c: ClientDetail }) {
  const { run, busy } = useAction()
  const confirm = useConfirm()
  const navigate = useNavigate()
  const [name, setName] = useState('')
  const [machine, setMachine] = useState('')
  return (
    <section className="card s7" aria-labelledby="branches">
      <h2 id="branches">Branches <Count n={c.sites.length} /></h2>
      {c.sites.length === 0 && <p className="muted">No branch yet. Add one for each separate shop that has its own Coduo POS database. Extra PCs in the same shop do not need one.</p>}
      {c.sites.map((s, i) => (
        <div key={s.id} className="site-card">
          <div>
            <b>{s.name}</b>
            <Pill tone={connectionTone(s.connection)} style={{ marginInlineStart: 8 }}>{s.connectionLabel}</Pill>
            {s.isDisabled && <Pill tone="bad">disabled</Pill>}
            <div className="muted" style={{ fontSize: 13, marginTop: 2 }}>{s.lastSeenAtUtc ? 'Last contact ' + ago(utc(s.lastSeenAtUtc)) : 'Never in contact'}{s.machineId ? ' · ' + s.machineId : ''}</div>
            <Pulse cells={s.pulse} row={i} />
          </div>
          <div className="row">
            <Link className="btn secondary" to={'/sites/' + s.id}>Open</Link>
            <button type="button" className="btn danger" disabled={busy} onClick={async () => {
              if (await confirm({ title: `Remove ${s.name}?`, yes: 'Remove branch', text: (s.connection === 'Connected' ? 'This branch is connected. Its cloud copy of the data is deleted for good and it stops syncing. The shop keeps selling and everything stays on its PC. ' : 'Its cloud copy of the data is deleted for good. ') + 'This cannot be undone.' }))
                await run(() => api.removeSite(s.id), `${s.name} removed.`)
            }}>Remove</button>
          </div>
        </div>
      ))}
      <Fold title="Add a branch">
        <form onSubmit={async (e) => {
          e.preventDefault()
          const r = await run(() => api.addSite(c.id, name.trim(), machine.trim()), 'Branch added.')
          if (r) { setName(''); setMachine(''); navigate('/sites/' + r.site.id) }
        }}>
          <div className="fields2">
            <div><label htmlFor="siteName">Name</label><input id="siteName" value={name} onChange={(e) => setName(e.target.value)} placeholder="Second shop" required maxLength={200} /></div>
            <div><label htmlFor="machineId">Machine ID <span className="opt">(from the shop's License page)</span></label><input id="machineId" value={machine} onChange={(e) => setMachine(e.target.value)} placeholder="XXXXX-XXXXX-XXXXX-XXXXX" /></div>
          </div>
          <p style={{ marginTop: 14 }}><button type="submit" className="btn" disabled={busy}>Add branch</button></p>
        </form>
      </Fold>
    </section>
  )
}

function Licences({ c }: { c: ClientDetail }) {
  const lic = useQuery({ queryKey: ['client-licences', c.id], queryFn: () => api.clientLicences(c.id) })
  const rows = lic.data ?? []
  return (
    <Item className="card" id="licences" aria-labelledby="h-lic">
      <div className="ph">
        <h2 id="h-lic">POS licences <Count n={rows.filter((l) => l.status !== 'Superseded').length} /></h2>
        <Link className="btn sm" to={`/licences?client=${c.id}#issue`}><Icon id="key" />Issue a key</Link>
      </div>
      {rows.length === 0 ? <p className="muted">No licence on record. Issue their key here, or add one you issued before on the <Link to="/licences">Licences</Link> page.</p> : (
        <div className="tscroll">
          <table className="t">
            <thead><tr><th>Machine ID</th><th>Licence</th><th>Ends</th><th /></tr></thead>
            <tbody>
              {rows.map((l) => (
                <tr key={l.id}>
                  <td><span className="mono">{l.machineId}</span><small className="muted d">serial {l.serial}</small></td>
                  <td>{l.plan} · {l.kind}{l.seats > 1 ? ` · ${l.seats} PCs` : ''}<small className="muted d">issued {l.issuedOn}</small></td>
                  <td className="nowrap"><Pill tone={licencePill(l.status, l.statusLabel)}>{l.statusLabel}</Pill><small className="muted d">{l.timeLimited ? 'ends' : 'updates until'} {l.validUntil}</small></td>
                  <td className="r nowrap">
                    {l.key && <CopyButton text={l.key} label="Copy key" className="btn ghost sm" />}
                    {l.status !== 'Superseded' && <Link className="btn ghost sm" to={`/licences?renew=${l.id}#issue`}>Renew</Link>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Item>
  )
}

function Payments({ c }: { c: ClientDetail }) {
  const payments = useQuery({ queryKey: ['client-payments', c.id], queryFn: () => api.clientPayments(c.id) })
  const { run } = useAction()
  const confirm = useConfirm()
  const [all, setAll] = useState(false)
  const rows = payments.data ?? []
  const last = rows[0]
  const yearAgo = new Date(); yearAgo.setFullYear(yearAgo.getFullYear() - 1)
  return (
    <Item className="card" id="payments" aria-labelledby="h-payments">
      <div className="ph">
        <h2 id="h-payments">Payments <Count n={rows.length} /></h2>
        <Link className="btn ghost sm" to="/finance">Finance</Link>
      </div>
      {rows.length > 0 ? <>
        <div className="ledger" style={{ marginBottom: 14 }}>
          <div><b><CountUp value={rows.reduce((s, x) => s + x.amount, 0)} format={money} /></b><span>collected in all</span></div>
          <div><b><CountUp value={rows.filter((x) => new Date(x.date) > yearAgo).reduce((s, x) => s + x.amount, 0)} format={money} /></b><span>in the last 12 months</span></div>
          <div><b>{last.date}</b><span>last payment, {money(last.amount)}</span></div>
        </div>
        <div className="tscroll">
          <table className="t">
            <thead><tr><th>Day</th><th>For</th><th>Paid by</th><th className="r">Amount</th><th /></tr></thead>
            <tbody>
              {rows.slice(0, all ? undefined : 10).map((x) => (
                <tr key={x.id}>
                  <td className="nowrap">{x.date}</td>
                  <td>{x.purpose}{x.note && <small className="muted d">{x.note}</small>}</td>
                  <td>{x.method}{x.reference && <small className="muted d">{x.reference}</small>}</td>
                  <td className="r num nowrap">{money(x.amount)}</td>
                  <td className="r"><button type="button" className="btn ghost sm" onClick={async () => {
                    if (await confirm({ title: 'Delete this payment?', yes: 'Delete', text: `${money(x.amount)} on ${x.date}. It comes off every total.` }))
                      await run(() => api.deletePayment(x.id), 'Payment deleted.')
                  }}>Delete</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {rows.length > 10 && !all && <p className="help"><button type="button" className="btn link" onClick={() => setAll(true)}>Show all {rows.length} payments</button></p>}
      </> : <p className="muted">Nothing recorded from {c.name} yet.</p>}
      <Fold title="Record a payment"><PaymentForm clientId={c.id} /></Fold>
    </Item>
  )
}

function HandOver({ h, menuOnly, onClose }: { h: LoginHandOver; menuOnly: boolean; onClose: () => void }) {
  const text = `Sign in with: ${h.login.signIn}\nPassword: ${h.password}`
  return (
    <Item className="card new-key" aria-labelledby="newlogin">
      <h2 id="newlogin">Phone login ready: send it to the owner</h2>
      <p className="muted" style={{ marginTop: 0 }}>The password is shown only now. If it is lost, reset it and a new one is made.</p>
      <div className="token" style={{ whiteSpace: 'pre-line', userSelect: 'all' }}>{text}</div>
      {menuOnly ? (
        <ol className="help" style={{ margin: '12px 0 0', paddingInlineStart: 20 }}>
          <li>The owner opens the owner website and signs in with the name and password above.</li>
          <li>It opens their online menu editor: they add their dishes, pictures and prices, then publish it and print the QR code.</li>
        </ol>
      ) : (
        <ol className="help" style={{ margin: '12px 0 0', paddingInlineStart: 20 }}>
          <li>The owner installs the Coduo POS Owner app and signs in with the name and password above.</li>
          <li>The shop's data is encrypted, so the app then asks for the <b>recovery code</b>. The owner finds it on the shop PC: Coduo POS &gt; Settings &gt; Online &gt; Show recovery code.</li>
          <li>They type or paste it once. You never see it and cannot recover it for them.</li>
        </ol>
      )}
      <div className="row" style={{ marginTop: 12 }}>
        <CopyButton text={h.message || text} />
        {h.whatsAppUrl && <a className="btn secondary" target="_blank" rel="noopener" href={h.whatsAppUrl}><Icon id="send" />Send on WhatsApp</a>}
        <button type="button" className="btn ghost" onClick={onClose}>Done</button>
      </div>
    </Item>
  )
}

function Logins({ c, menuOnly, onHandOver }: { c: ClientDetail; menuOnly: boolean; onHandOver: (h: LoginHandOver) => void }) {
  const { run, busy } = useAction()
  const confirm = useConfirm()
  const [signIn, setSignIn] = useState('')
  const [name, setName] = useState('')
  const [role, setRole] = useState('owner')
  const [password, setPassword] = useState('')
  const roleLabel = (r: string) => r === 'owner' ? 'Owner' : r === 'manager' ? 'Manager' : 'Staff'
  const show = (h: LoginHandOver | undefined) => { if (h) { onHandOver(h); setTimeout(() => window.scrollTo({ top: document.getElementById('newlogin')?.offsetTop ?? 0, behavior: 'smooth' }), 400) } }
  const setRoleOf = async (o: OwnerLogin, r: string) => {
    if (r === o.role) return
    if (await confirm({ title: 'Change what this login can do?', yes: 'Change', danger: false, text: 'Their phone is signed out so the new limits apply straight away. They sign in again with the same password.' }))
      await run(() => api.updateLogin(c.id, o.id, { role: r }), 'Role changed.')
  }
  return (
    <Item className="card" aria-labelledby="phone">
      <h2 id="phone">Phone logins <Count n={c.logins.length} /></h2>
      <p className="muted" style={{ marginTop: 0 }}>{menuOnly
        ? 'What the owner types on the owner website (this site, /owner) to edit their online menu. Give them the Owner or Manager role: Staff cannot change the menu.'
        : 'What the shop owner types in the Coduo POS phone app or on the owner website (this site, /owner). It is not the till PIN, and it only ever opens this shop. Sales, stock and debts also need the shop\'s recovery code, typed once on the phone.'}</p>
      {c.logins.length === 0 && <p className="muted">No phone login yet.</p>}
      {c.logins.map((o) => (
        <div key={o.id} className="site-card">
          <div>
            <b>{o.signIn}</b>
            <Pill tone={o.isActive ? 'ok' : 'none'} style={{ marginInlineStart: 8 }}>{o.isActive ? 'active' : 'disabled'}</Pill>
            <Pill tone={o.role === 'owner' ? 'ok' : 'none'} style={{ marginInlineStart: 4 }}>{roleLabel(o.role)}</Pill>
            <div className="muted" style={{ fontSize: 13, marginTop: 2 }}>{o.displayName ?? ''} · {o.lastSignInAtUtc ? 'Last sign-in ' + ago(utc(o.lastSignInAtUtc)) : 'Never signed in'}</div>
          </div>
          <div className="row">
            <select className="inline" aria-label={'Role for ' + o.signIn} value={o.role} onChange={(e) => setRoleOf(o, e.target.value)} disabled={busy}>
              <option value="owner">Owner</option><option value="manager">Manager</option><option value="staff">Staff</option>
            </select>
            <button type="button" className="btn secondary sm" disabled={busy} onClick={async () => {
              if (await confirm({ title: 'Reset this password?', yes: 'Reset', danger: false, text: 'A new password is made and the old one stops working. Their phone is signed out.' }))
                show(await run(() => api.resetPassword(c.id, o.id), 'New password made.'))
            }}>Reset password</button>
            <button type="button" className={'btn sm ' + (o.isActive ? 'danger' : 'secondary')} disabled={busy} onClick={async () => {
              if (await confirm(o.isActive
                ? { title: 'Disable this login?', yes: 'Disable', text: 'They can no longer sign in on the phone, and their phone is signed out. The shop is not affected.' }
                : { title: 'Enable this login?', yes: 'Enable', danger: false, text: 'They can sign in on the phone again.' }))
                await run(() => api.updateLogin(c.id, o.id, { isActive: !o.isActive }), o.isActive ? 'Login disabled.' : 'Login enabled.')
            }}>{o.isActive ? 'Disable' : 'Enable'}</button>
            <button type="button" className="btn sm danger" disabled={busy} onClick={async () => {
              if (await confirm({ title: `Remove ${o.signIn}?`, yes: 'Remove login', text: `${o.signIn} is signed out of the phone app and can no longer sign in. The shop's data is not touched.` }))
                await run(() => api.removeLogin(c.id, o.id), 'Login removed.')
            }}>Remove</button>
          </div>
        </div>
      ))}
      <Fold title="Add a phone login" open={c.logins.length === 0}>
        <form onSubmit={async (e) => {
          e.preventDefault()
          const h = await run(() => api.addLogin(c.id, { signIn: signIn.trim(), displayName: name.trim() || undefined, password: password || undefined, role }), 'Phone login added.')
          if (h) { setSignIn(''); setName(''); setPassword(''); show(h) }
        }}>
          <div className="fields2">
            <div>
              <label htmlFor="ownerEmail">Sign-in name <span className="opt">(email, phone number or username)</span></label>
              <input id="ownerEmail" value={signIn} onChange={(e) => setSignIn(e.target.value)} placeholder="owner@example.com, 03 123 456 or hassan" required maxLength={255} autoComplete="off" autoCapitalize="off" spellCheck={false} />
              <div className="help" style={{ marginTop: 4 }}>A phone number works with or without the country code.</div>
            </div>
            <div>
              <label htmlFor="ownerName">Name <span className="opt">(shown in the app)</span></label>
              <input id="ownerName" value={name} onChange={(e) => setName(e.target.value)} placeholder="Hassan" maxLength={100} autoComplete="off" />
            </div>
          </div>
          <label htmlFor="ownerRole">What can they do?</label>
          <select id="ownerRole" value={role} onChange={(e) => setRole(e.target.value)}>{ROLES.map(([v, t]) => <option key={v} value={v}>{t}</option>)}</select>
          <p className="help">Limits are enforced by the cloud: a table a login may not read is never sent, and a change it may not make is never queued. Cost prices live inside the stock data, so only the app hides them from a Manager or Staff login.</p>
          <label htmlFor="ownerPassword">Password <span className="opt">(leave empty to make a strong one)</span></label>
          <input id="ownerPassword" value={password} onChange={(e) => setPassword(e.target.value)} minLength={8} maxLength={100} autoComplete="off" />
          <p className="help">At least 8 characters. Shown once after you press Add, so you can send it.</p>
          <p style={{ marginTop: 14 }}><button type="submit" className="btn" disabled={busy}>Add phone login</button></p>
        </form>
      </Fold>
    </Item>
  )
}

function Details({ c }: { c: ClientDetail }) {
  const { run, busy } = useAction()
  const confirm = useConfirm()
  const navigate = useNavigate()
  const [f, setF] = useState({ name: c.name, phone: c.phone ?? '', email: c.email ?? '', notes: c.notes ?? '' })
  useEffect(() => setF({ name: c.name, phone: c.phone ?? '', email: c.email ?? '', notes: c.notes ?? '' }), [c.name, c.phone, c.email, c.notes])
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setF({ ...f, [k]: e.target.value })
  const n = c.sites.length
  return (
    <section className="card" aria-labelledby="details">
      <h2 id="details">Details</h2>
      <form onSubmit={(e) => { e.preventDefault(); void run(() => api.updateClient(c.id, { name: f.name.trim(), phone: f.phone || null, email: f.email || null, notes: f.notes || null }), 'Saved.') }}>
        <label htmlFor="d-name" style={{ marginTop: 0 }}>Shop name</label>        <input id="d-name" value={f.name} onChange={set('name')} required maxLength={200} />
        <label htmlFor="d-phone">Phone</label>
        <input id="d-phone" value={f.phone} onChange={set('phone')} />
        <label htmlFor="d-email">Email</label>
        <input id="d-email" type="email" value={f.email} onChange={set('email')} />
        <label htmlFor="d-notes">Notes</label>
        <textarea id="d-notes" value={f.notes} onChange={set('notes')} />
        <p style={{ margin: '16px 0 0' }}><button type="submit" className="btn" disabled={busy}>Save changes</button></p>
      </form>
      <div style={{ display: 'grid', justifyItems: 'start', gap: 10, marginTop: 14 }}>
        <button type="button" className={'btn sm ' + (c.isActive ? 'danger' : 'secondary')} disabled={busy} onClick={async () => {
          if (await confirm(c.isActive
            ? { title: 'Deactivate this client?', yes: 'Deactivate', text: 'All their branches are blocked from the cloud. Their shop keeps selling.' }
            : { title: 'Reactivate this client?', yes: 'Reactivate', danger: false, text: 'Their branches can connect again.' }))
            await run(() => api.setActive(c.id, !c.isActive), c.isActive ? 'Client deactivated.' : 'Client reactivated.')
        }}>{c.isActive ? 'Deactivate client' : 'Reactivate client'}</button>
        {c.hasOnline && c.hasPos && <button type="button" className="btn sm secondary" disabled={busy} onClick={async () => {
          if (await confirm({ title: 'Make this client POS only?', yes: 'POS only', danger: false, text: 'For a shop that stopped using Online. Its branches and cloud data stay; the portal just stops asking about them and its Online plan.' }))
            await run(() => api.setOnline(c.id, false), 'They are POS only now.')
        }}>Make POS only (no Online)</button>}
        <button type="button" className="btn sm danger" disabled={busy} onClick={async () => {
          if (!await confirm({ title: `Remove ${c.name}?`, yes: 'Remove client', text: (n === 0 ? '' : `This also removes its ${n} branch${n === 1 ? '' : 'es'} and deletes their cloud copy of the data for good. `) + 'The subscription and history go too; its payments stay in Finance. Nothing is deleted at the shop, and it keeps selling. This cannot be undone.' })) return
          if (await run(() => api.removeClient(c.id), `${c.name} removed.`) !== undefined) navigate('/')
        }}>Remove client</button>
      </div>
    </section>
  )
}

function History({ id }: { id: number }) {
  const events = useQuery({ queryKey: ['events', id], queryFn: () => api.events(id) })
  return (
    <section className="card" aria-labelledby="history">
      <h2 id="history">History</h2>
      <ul className="timeline">
        {(events.data ?? []).map((e) => <li key={e.id}><time>{clock(utc(e.atUtc))}</time><span>{e.text}</span></li>)}
      </ul>
    </section>
  )
}
