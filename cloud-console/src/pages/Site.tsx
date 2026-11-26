import { useState } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api, type LinkResult } from '../api'
import { ago, clock, connectionTone, day, healthPill, planPill, utc } from '../format'
import { CountUp, Item, Stagger } from '../fx'
import { Fold, Icon, LinkSlip, Loading, Pill, Problem, Pulse, useAction, useConfirm } from '../ui'

export function SitePage() {
  const id = useParams().id!
  const navigate = useNavigate()
  const site = useQuery({ queryKey: ['site', id], queryFn: () => api.site(id) })
  const clientId = site.data?.site.clientId
  const client = useQuery({ queryKey: ['client', clientId], queryFn: () => api.client(clientId!), enabled: !!clientId })
  const { run, busy } = useAction()
  const confirm = useConfirm()
  // A brand-new client arrives here with its first link (shown once), passed by the New client page.
  const arrived = useLocation().state as { link?: LinkResult } | null
  const [link, setLink] = useState<LinkResult | null>(arrived?.link ?? null)
  const [token, setToken] = useState<string | null>(null)
  const [edit, setEdit] = useState<{ name: string; machine: string } | null>(null)

  if (site.isPending) return <Loading />
  if (site.isError) return (site.error as { status?: number }).status === 404
    ? <div className="list"><div className="empty"><h3>Branch not found</h3><Link className="btn secondary" to="/">Back to clients</Link></div></div>
    : <Problem error={site.error} retry={() => site.refetch()} />
  const { site: s, data, tokens, syncLog, openLink } = site.data
  const [hTone, hLabel] = healthPill(s.health)
  const [pTone, pLabel] = planPill(client.data?.subscriptionState)
  const seen = utc(s.lastSeenAtUtc), synced = utc(s.lastSyncAtUtc)
  const connected = s.connection === 'Connected'
  const counts = Object.entries(data.counts ?? {})
  const removeText = (connected ? 'This branch is connected. Its cloud copy of the data is deleted for good and it stops syncing. The shop keeps selling and everything stays on its PC. ' : 'Its cloud copy of the data is deleted for good. ') + 'This cannot be undone.'

  return (
    <Stagger>
      <Item as="div" className="page-head">
        <div>
          <Link className="crumb" to={'/clients/' + s.clientId}><Icon id="back" />{client.data?.name ?? 'Client'}</Link>
          <h1>{s.name} <Pill tone={connectionTone(s.connection)} style={{ verticalAlign: 'middle', marginInlineStart: 6 }}>{s.connectionLabel}</Pill>
            {s.isDisabled && <Pill tone="bad" style={{ verticalAlign: 'middle' }}>disabled</Pill>}</h1>
          <p className="sub">{s.connectionHelp}{openLink && !link ? ` A link is waiting to be used until ${clock(utc(openLink.expiresAtUtc))}.` : ''}</p>
        </div>
      </Item>

      {link?.url && <Item as="div"><LinkSlip store={client.data?.name ?? s.name} url={link.url} expires={day(utc(link.expiresAtUtc))} whatsApp={link.whatsAppUrl} /></Item>}

      <Item className="card" aria-labelledby="contact">
        <h2 id="contact">Contact</h2>
        <div style={{ marginBottom: 14 }}>
          <Pill tone={hTone}>{hLabel}</Pill>
          <span className="muted" style={{ marginInlineStart: 6 }}>{seen ? `Last contact ${ago(seen)} (${clock(seen)})` : 'This branch has not contacted the cloud yet.'}</span>
        </div>
        {seen || connected ? <Pulse cells={s.pulse} large /> : <p className="help" style={{ margin: 0 }}>The 24-hour strip appears here once this branch has contacted the cloud.</p>}
      </Item>

      <Item className="card" aria-labelledby="connection">
        <h2 id="connection">Connection</h2>
        <div className="row">
          <button type="button" className={'btn ' + (connected ? 'secondary' : '')} disabled={busy} onClick={async () => {
            const r = await run(() => api.newLink(s.id), 'New connection link made.')
            if (r) { setLink(r); window.scrollTo({ top: 0, behavior: 'smooth' }) }
          }}>{connected ? 'Reconnect this branch (new link)' : 'Create connection link'}</button>
          {(connected || s.connection === 'WaitingForShop') && <button type="button" className="btn danger" disabled={busy} onClick={async () => {
            if (await confirm({ title: 'Disconnect this branch?', yes: 'Disconnect', text: 'Its access is removed at once and any unused link stops working. The shop keeps selling; it only stops sending data. To connect it again you create a new link.' }))
              await run(() => api.disconnect(s.id), 'Branch disconnected.')
          }}>Disconnect</button>}
        </div>
      </Item>

      <Item as="div" className="grid2 gap">
        <section className="card" aria-labelledby="sync">
          <h2 id="sync">Sync</h2>
          <dl className="facts">
            <dt>Last data</dt><dd>{synced ? `${clock(synced)} · ${ago(synced)}` : 'none yet'}</dd>
            <dt>Online plan</dt><dd><Pill tone={pTone}>{pLabel}</Pill></dd>
            <dt>Licence plan</dt><dd>{s.shop?.plan ?? 'no key received yet'}</dd>
            {s.shop?.licenseKind && <><dt>Licence key</dt><dd>{s.shop.licenseKind} · {s.shop.seats ?? 1} PC(s) · {s.shop.licenseKind === 'Full' ? 'updates until' : 'expires'} {s.shop.licenseUntil}</dd></>}
            <dt>Machine ID</dt><dd className="mono">{s.machineId ?? '-'}</dd>
          </dl>
          <div className="row" style={{ marginTop: 16 }}>
            <button type="button" className="btn secondary sm" disabled={busy || s.resyncRequested} onClick={() => run(() => api.resync(s.id), 'Full resync requested.')}>{s.resyncRequested ? 'Resync requested' : 'Request full resync'}</button>
            <button type="button" className={'btn sm ' + (s.isDisabled ? 'secondary' : 'danger')} disabled={busy} onClick={async () => {
              if (await confirm(s.isDisabled
                ? { title: 'Enable this branch?', yes: 'Enable', danger: false, text: 'It can send data again.' }
                : { title: 'Disable this branch?', yes: 'Disable', text: 'It is blocked from the cloud until you enable it. The shop keeps selling.' }))
                await run(() => api.setSiteDisabled(s.id, !s.isDisabled), s.isDisabled ? 'Branch enabled.' : 'Branch disabled.')
            }}>{s.isDisabled ? 'Enable' : 'Disable'}</button>
            <button type="button" className="btn ghost sm" onClick={() => setEdit({ name: s.name, machine: s.machineId ?? '' })}>Rename</button>
          </div>
          {edit && (
            <form style={{ marginTop: 10 }} onSubmit={async (e) => {
              e.preventDefault()
              if (await run(() => api.updateSite(s.id, edit.name.trim(), edit.machine.trim()), 'Saved.')) setEdit(null)
            }}>
              <div className="fields2">
                <div><label htmlFor="s-name">Name</label><input id="s-name" value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} required maxLength={200} /></div>
                <div><label htmlFor="s-machine">Machine ID</label><input id="s-machine" value={edit.machine} onChange={(e) => setEdit({ ...edit, machine: e.target.value })} /></div>
              </div>
              <div className="row" style={{ marginTop: 12 }}><button type="submit" className="btn sm" disabled={busy}>Save</button><button type="button" className="btn ghost sm" onClick={() => setEdit(null)}>Cancel</button></div>
            </form>
          )}
          <p className="help">A full resync wipes this branch's cloud copy and uploads everything again. Use it after the shop restored a backup.</p>
        </section>
        <section className="card" aria-labelledby="mirror">
          <h2 id="mirror">What the cloud holds {data.encrypted && <Pill tone="ok" style={{ verticalAlign: 'middle', marginInlineStart: 6 }}>Encrypted by the shop</Pill>}</h2>
          {counts.length === 0 || !data.held ? <p className="muted">No data received yet.</p> : <>
            <div className="ledger">{counts.map(([k, v]) => <div key={k}><b><CountUp value={v} /></b><span>{k}</span></div>)}</div>
            <p className="help">These should match the numbers in the shop.</p>
          </>}
          {data.encrypted && <p className="help">The shop PC encrypts every row before sending it. You can count rows per table but not read them, and the owner's phone opens them with the shop's recovery code. If the owner loses the code and the PC, nobody can recover this data, including you.</p>}
        </section>
      </Item>

      <Item className="card">
        <Fold title={`Recent uploads (${syncLog.length})`}>
          {syncLog.length === 0 ? <p className="muted">Nothing uploaded yet.</p> : (
            <div className="tscroll"><table className="t">
              <thead><tr><th>When</th><th>Changes</th><th>Result</th><th>Took</th></tr></thead>
              <tbody>{syncLog.map((l, i) => (
                <tr key={i}>
                  <td>{clock(utc(l.receivedAtUtc))}</td>
                  <td>{l.changes.toLocaleString('en-US')} {l.initial ? '(first upload)' : ''}</td>
                  <td><Pill tone={l.outcome === 'applied' ? 'ok' : 'bad'}>{l.outcome}</Pill> {l.error}</td>
                  <td>{l.durationMs ?? '-'} ms</td>
                </tr>
              ))}</tbody>
            </table></div>
          )}
        </Fold>
      </Item>

      <Item className="card">
        <Fold title="Advanced: device tokens and manual set-up" open={!!token}>
          <table className="t"><tbody>
            {tokens.map((t, i) => <tr key={i}><td><code>{t.prefix}…</code></td><td>active</td><td className="muted">{t.lastUsedAtUtc ? 'used ' + ago(utc(t.lastUsedAtUtc)) : 'never used'}</td></tr>)}
            {tokens.length === 0 && <tr><td className="muted">No active device token.</td></tr>}
          </tbody></table>
          {token && (
            <div className="notice" style={{ marginTop: 12 }}>
              <strong>Copy this device token now. It is shown only once.</strong>
              <div className="token">{token}</div>
              <p>Manual alternative to the link: create <code>%LocalAppData%\NexPOS\online.json</code> on the shop PC and restart Coduo POS:</p>
              <pre>{`{\n  "Online": {\n    "Enabled": true,\n    "CloudUrl": "${location.origin}",\n    "DeviceToken": "${token}"\n  }\n}`}</pre>
            </div>
          )}
          <button type="button" className="btn secondary sm" style={{ marginTop: 12 }} disabled={busy} onClick={async () => {
            if (!await confirm({ title: 'Issue a raw token?', yes: 'Issue token', text: 'The current device token stops working at once. The shop PC must be given the new one by hand.' })) return
            const r = await run(() => api.issueToken(s.id), 'Device token issued.')
            if (r) setToken(r.token)
          }}>Issue a raw device token (revokes the current one)</button>
          <p className="help">Normally you never need this: the connection link does it for you.</p>
        </Fold>
      </Item>

      <Item className="card" aria-labelledby="remove">
        <h2 id="remove">Remove this branch</h2>
        <p className="muted" style={{ marginTop: 0 }}>Deletes this branch from the cloud: its copy of the shop's data, its history and its connection. Nothing is deleted at the shop, and it keeps selling. Use it for a branch that was added by mistake or has closed.</p>
        <button type="button" className="btn danger" disabled={busy} onClick={async () => {
          if (!await confirm({ title: `Remove ${s.name}?`, yes: 'Remove branch', text: removeText })) return
          if (await run(() => api.removeSite(s.id), `${s.name} removed.`) !== undefined) navigate('/clients/' + s.clientId)
        }}>Remove branch</button>
      </Item>
    </Stagger>
  )
}
