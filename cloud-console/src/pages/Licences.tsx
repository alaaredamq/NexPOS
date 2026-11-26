import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api, type Licence } from '../api'
import { getCurrency, licencePill } from '../format'
import { Item, Stagger, rowMotion } from '../fx'
import { useFinanceOptions } from '../forms'
import { AttentionList, Count, CopyButton, Icon, Loading, Pill, Problem, useAction, useConfirm } from '../ui'
import { attentionTarget } from './Clients'
import { motion } from 'motion/react'

const SHOW = [['current', 'In force'], ['attention', 'Ending or ended'], ['unassigned', 'Not linked'], ['all', 'All, with replaced']] as const

export function Licences() {
  const [params, setParams] = useSearchParams()
  const show = params.get('show') ?? 'current'
  const list = useQuery({ queryKey: ['licences', show], queryFn: () => api.licences(show) })
  const clients = useQuery({ queryKey: ['clients'], queryFn: api.clients })
  const overview = useQuery({ queryKey: ['overview'], queryFn: api.overview })
  const signer = useQuery({ queryKey: ['signer'], queryFn: api.signer })
  const navigate = useNavigate()
  const [fresh, setFresh] = useState<{ licence: Licence; whatsAppUrl?: string | null } | null>(null)
  const [filter, setFilter] = useState('')
  useEffect(() => { if (location.hash) setTimeout(() => document.querySelector(location.hash)?.scrollIntoView({ behavior: 'smooth' }), 900) }, [])

  const rows = useMemo(() => {
    const q = filter.trim().toLowerCase()
    return (list.data ?? []).filter((l) => !q || `${l.clientName ?? ''} ${l.customer} ${l.machineId} ${l.serial}`.toLowerCase().includes(q))
  }, [list.data, filter])

  if (list.isPending || clients.isPending || signer.isPending) return <Loading />
  if (list.isError) return <Problem error={list.error} retry={() => list.refetch()} />
  const people = (clients.data ?? []).map((c) => ({ id: c.id, name: c.name }))
  const licenceAttention = (overview.data?.attention ?? []).filter((a) => (a.portalPath ?? '').startsWith('/admin/Licenses'))

  return (
    <Stagger>
      <Item as="div" className="page-head">
        <div>
          <h1>Licences</h1>
          <p className="sub">The POS licence keys of every shop, online or not: what each PC runs, when it ends, and who it belongs to.</p>
        </div>
        <a className="btn" href="#issue" onClick={(e) => { e.preventDefault(); document.getElementById('issue')?.scrollIntoView({ behavior: 'smooth' }) }}><Icon id="key" />Issue a key</a>
      </Item>

      {fresh && (
        <Item className="card new-key" aria-labelledby="h-new">
          <h2 id="h-new">New key: send it to the shop</h2>
          <p className="muted" style={{ marginTop: 0 }}>{fresh.licence.clientName ?? fresh.licence.customer} · {fresh.licence.plan} · {fresh.licence.kind}{fresh.licence.seats > 1 ? ` · ${fresh.licence.seats} PCs` : ''} · {fresh.licence.machineId}</p>
          <div className="token" style={{ userSelect: 'all' }}>{fresh.licence.key}</div>
          <p className="help">They open Coduo POS &gt; License on that PC, paste it and press Activate. No internet needed. It stays on this page, so you can copy it again later.</p>
          <div className="row" style={{ marginTop: 12 }}>
            {fresh.licence.key && <CopyButton text={fresh.licence.key} label="Copy key" />}
            {fresh.whatsAppUrl && <a className="btn secondary" target="_blank" rel="noopener" href={fresh.whatsAppUrl}><Icon id="send" />Send on WhatsApp</a>}
          </div>
        </Item>
      )}

      {licenceAttention.length > 0 && (
        <Item className="card" aria-labelledby="l-needs">
          <div className="ph"><h2 id="l-needs">Needs you</h2><Pill tone="warn">{licenceAttention.length}</Pill></div>
          <AttentionList items={licenceAttention} onOpen={(a) => navigate(attentionTarget(a))} />
        </Item>
      )}

      <Item className="card" id="issue" aria-labelledby="h-issue">
        <h2 id="h-issue"><Icon id="key" />Issue a key</h2>
        {signer.data && !signer.data.ready
          ? <div className="notice" role="note"><b>Issuing here is not available.</b> {signer.data.message} You can still record keys made with the desktop keygen (it can send them here by itself), paste them below, or import issued.csv.</div>
          : <IssueForm clients={people} onIssued={(r) => { setFresh(r); window.scrollTo({ top: 0, behavior: 'smooth' }) }} />}
      </Item>

      <Item className="card" aria-labelledby="h-list">
        <div className="ph">
          <h2 id="h-list">All licences <Count n={rows.length} /></h2>
          <div className="chips" role="group" aria-label="Show">
            {SHOW.map(([k, label]) => <button key={k} type="button" className="chip" aria-pressed={show === k} onClick={() => setParams(k === 'current' ? {} : { show: k })}>{label}</button>)}
          </div>
        </div>
        {(list.data ?? []).length === 0 ? <p className="muted">No licences here yet. Issue one above, paste one you issued before, or import your issued.csv below.</p> : <>
          <div className="search" style={{ maxWidth: 320, marginBottom: 10 }}>
            <Icon id="search" />
            <input type="text" value={filter} onChange={(e) => setFilter(e.target.value)} placeholder="Filter by client, Machine ID, serial" aria-label="Filter licences" autoComplete="off" />
          </div>
          <div className="tscroll">
            <table className="t lic">
              <thead><tr><th>Client</th><th>Machine ID</th><th>Licence</th><th>Ends</th><th /></tr></thead>
              <tbody>{rows.map((l, i) => <LicenceRow key={l.id} l={l} i={i} clients={people} />)}</tbody>
            </table>
            {rows.length === 0 && <div className="empty"><p>Nothing matches.</p></div>}
          </div>
        </>}
      </Item>

      <Item as="div" className="g">
        <RecordKey clients={people} />
        <ImportCsv />
      </Item>
    </Stagger>
  )
}

function LicenceRow({ l, i, clients }: { l: Licence; i: number; clients: { id: number; name: string }[] }) {
  const { run, busy } = useAction()
  const confirm = useConfirm()
  const [pick, setPick] = useState(String(clients[0]?.id ?? ''))
  return (
    <motion.tr {...rowMotion(i)}>
      <td>
        {l.clientId ? <Link to={`/clients/${l.clientId}#licences`}>{l.clientName}</Link> : <>
          <span>{l.customer}</span><small className="muted d">not linked</small>
          <div className="row" style={{ marginTop: 6, flexWrap: 'nowrap' }}>
            <select className="inline" value={pick} onChange={(e) => setPick(e.target.value)} aria-label={'Client for ' + l.customer}>
              {clients.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
            <button type="button" className="btn sm secondary" disabled={busy || !pick} onClick={() => run(() => api.linkLicence(l.id, Number(pick), l.note), 'Licence linked.')}>Link</button>
          </div>
        </>}
        {l.clientId && l.clientName && l.customer.toLowerCase() !== l.clientName.toLowerCase() && <small className="muted d">key says “{l.customer}”</small>}
      </td>
      <td><span className="mono">{l.machineId}</span><small className="muted d">serial {l.serial} · {l.source.toLowerCase()}</small></td>
      <td>{l.plan} · {l.kind}{l.seats > 1 ? ` · ${l.seats} PCs` : ''}<small className="muted d">issued {l.issuedOn}</small>{l.note && <small className="muted d">{l.note}</small>}</td>
      <td className="nowrap"><Pill tone={licencePill(l.status, l.statusLabel)}>{l.statusLabel}</Pill><small className="muted d">{l.timeLimited ? 'ends' : 'updates until'} {l.validUntil}</small></td>
      <td className="r nowrap">
        {l.key && <CopyButton text={l.key} label="Copy key" className="btn ghost sm" />}
        {l.status !== 'Superseded' && <Link className="btn ghost sm" to={`/licences?renew=${l.id}#issue`}>Renew</Link>}
        {l.source !== 'Cloud' && <button type="button" className="btn ghost sm" disabled={busy} onClick={async () => {
          if (await confirm({ title: 'Remove this record?', yes: 'Remove', text: 'Only the record goes. The key keeps working at the shop: offline keys cannot be switched off.' }))
            await run(() => api.deleteLicence(l.id), 'Record removed.')
        }}>Remove</button>}
      </td>
    </motion.tr>
  )
}

function IssueForm({ clients, onIssued }: { clients: { id: number; name: string }[]; onIssued: (r: { licence: Licence; whatsAppUrl?: string | null }) => void }) {
  const [params] = useSearchParams()
  const options = useFinanceOptions()
  const { run, busy } = useAction()
  const [f, setF] = useState({ clientId: params.get('client') ?? '', customer: '', machineId: '', plan: '', kind: 'full', seats: '1', days: '', until: '', paid: '', method: 'Cash', note: '', passphrase: '' })
  const [error, setError] = useState<string | null>(null)
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setF((x) => ({ ...x, [k]: e.target.value }))
  // Renew: the same PC, client, plan, type and PCs as the key being renewed.
  const renew = params.get('renew')
  useEffect(() => {
    if (!renew) return
    api.licence(Number(renew)).then((l) => setF((x) => ({ ...x, clientId: l.clientId ? String(l.clientId) : '', customer: l.clientId ? '' : l.customer, machineId: l.machineId,
      plan: l.plan.toLowerCase(), kind: l.kind.toLowerCase(), seats: String(l.seats) }))).catch(() => { })
  }, [renew])
  useEffect(() => { const c = params.get('client'); if (c) setF((x) => ({ ...x, clientId: c })) }, [params])
  const basic = f.plan === 'basic'

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!f.plan) { setError('Pick Basic or Pro.'); return }
    setError(null)
    const paid = parseFloat(f.paid.replace(/,/g, ''))
    const r = await run(() => api.issueLicence({
      clientId: f.clientId ? Number(f.clientId) : null, machineId: f.machineId.trim().toUpperCase(), customer: f.customer || null,
      kind: f.kind, plan: f.plan, seats: basic ? 1 : Number(f.seats) || 1, days: f.days ? Number(f.days) : null, until: f.until || null,
      note: f.note || null, paymentAmount: isFinite(paid) && paid > 0 ? paid : null, paymentMethod: f.method || null, passphrase: f.passphrase,
    }), 'Key issued.')
    if (r) { onIssued({ licence: r.license, whatsAppUrl: r.whatsAppUrl }); setF((x) => ({ ...x, passphrase: '', paid: '', note: '' })) }
  }

  return (
    <form onSubmit={submit} autoComplete="off">
      <div className="fields2">
        <div>
          <label htmlFor="i-client">Client</label>
          <select id="i-client" value={f.clientId} onChange={set('clientId')}>
            <option value="">No client: type the customer name</option>
            {clients.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </div>
        <div>
          <label htmlFor="i-customer">Name in the key <span className="opt">(the client's name when empty)</span></label>
          <input id="i-customer" value={f.customer} onChange={set('customer')} maxLength={60} />
        </div>
      </div>
      <label htmlFor="i-machine">Machine ID <span className="opt">(Coduo POS &gt; License on the shop PC)</span></label>
      <input id="i-machine" className="mono" value={f.machineId} onChange={set('machineId')} required placeholder="XXXXX-XXXXX-XXXXX-XXXXX" style={{ textTransform: 'uppercase' }} />
      <fieldset className="bare">
        <legend>Plan</legend>
        <div className="plans two">
          <label><input type="radio" name="plan" checked={f.plan === 'basic'} onChange={() => setF((x) => ({ ...x, plan: 'basic', seats: '1' }))} /><span>Basic<small>1 PC, 3 users, no stock or suppliers</small></span></label>
          <label><input type="radio" name="plan" checked={f.plan === 'pro'} onChange={() => setF((x) => ({ ...x, plan: 'pro' }))} /><span>Pro<small>everything, extra tills</small></span></label>
        </div>
      </fieldset>
      <div className="fields2">
        <div>
          <label htmlFor="i-kind">Type</label>
          <select id="i-kind" value={f.kind} onChange={set('kind')}>
            <option value="full">Full: perpetual, the date ends the updates plan</option>
            <option value="provisional">Provisional: expires (instalment sale)</option>
            <option value="trial">Trial: expires (evaluation)</option>
          </select>
        </div>
        <div>
          <label htmlFor="i-seats">PCs <span className="opt">(server + tills; Pro only)</span></label>
          <input id="i-seats" type="number" min={1} max={255} value={basic ? '1' : f.seats} readOnly={basic} onChange={set('seats')} />
        </div>
      </div>
      <div className="fields2">
        <div><label htmlFor="i-days">Days from today</label><input id="i-days" type="number" min={1} max={3650} value={f.days} onChange={set('days')} placeholder="Full 365 · Provisional 45 · Trial 14" /></div>
        <div><label htmlFor="i-until">Or until <span className="opt">(wins over days)</span></label><input id="i-until" type="date" value={f.until} onChange={set('until')} /></div>
      </div>
      <div className="fields2">
        <div><label htmlFor="i-paid">Paid now <span className="opt">({getCurrency()}, optional: recorded in Finance)</span></label><input id="i-paid" inputMode="decimal" value={f.paid} onChange={set('paid')} /></div>
        <div>
          <label htmlFor="i-method">Paid by</label>
          <input id="i-method" list="i-methods" value={f.method} onChange={set('method')} />
          <datalist id="i-methods">{(options.data?.methods ?? []).map((m) => <option key={m} value={m} />)}</datalist>
        </div>
      </div>
      <label htmlFor="i-note">Note <span className="opt">(optional)</span></label>
      <input id="i-note" value={f.note} onChange={set('note')} maxLength={500} />
      <label htmlFor="i-pass">Signing key passphrase</label>
      <input id="i-pass" type="password" value={f.passphrase} onChange={set('passphrase')} required autoComplete="off" />
      <p className="help">The passphrase of private.pem. It unlocks the key for this one signature and is never stored.</p>
      {error && <p className="error" role="alert">{error}</p>}
      <p style={{ margin: '16px 0 0' }}><button type="submit" className="btn" disabled={busy}><Icon id="key" />Sign and issue the key</button></p>
    </form>
  )
}

function RecordKey({ clients }: { clients: { id: number; name: string }[] }) {
  const { run, busy } = useAction()
  const [key, setKey] = useState('')
  const [client, setClient] = useState('')
  return (
    <section className="card s6" aria-labelledby="h-paste">
      <h2 id="h-paste">Add a key you already issued</h2>
      <form onSubmit={async (e) => {
        e.preventDefault()
        if (await run(() => api.recordLicence(key.trim(), client ? Number(client) : null), 'Key recorded.')) { setKey(''); setClient('') }
      }}>
        <label htmlFor="r-key">Licence key</label>
        <textarea id="r-key" className="mono" value={key} onChange={(e) => setKey(e.target.value)} required placeholder="NXL1-..." style={{ minHeight: 96 }} />
        <label htmlFor="r-client">Client <span className="opt">(found by Machine ID or name when empty)</span></label>
        <select id="r-client" value={client} onChange={(e) => setClient(e.target.value)}>
          <option value="">Find it for me</option>
          {clients.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
        <p className="help">The key's signature is checked, so only real keys are recorded.</p>
        <p style={{ margin: '14px 0 0' }}><button type="submit" className="btn secondary" disabled={busy}>Add key</button></p>
      </form>
    </section>
  )
}

function ImportCsv() {
  const { run, busy } = useAction()
  const [file, setFile] = useState<File | null>(null)
  const [result, setResult] = useState<string | null>(null)
  return (
    <section className="card s6" aria-labelledby="h-import">
      <h2 id="h-import">Import issued.csv</h2>
      <form onSubmit={async (e) => {
        e.preventDefault()
        if (!file) return
        const r = await run(async () => api.importLicences(await file.text()))
        if (r) setResult(`Added ${r.added}, already known ${r.alreadyKnown}, linked ${r.linked}.` + (r.problems.length ? ` Problems: ${r.problems.join('; ')}` : ''))
      }}>
        <label htmlFor="c-file">The issued.csv next to your private.pem</label>
        <input id="c-file" type="file" accept=".csv,text/csv" required onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
        <p className="help">Every key you issued on your PC, with its details (not the key text). Each is linked to the client with that Machine ID or exact name; the rest wait under “Not linked”. Importing again skips what is already here.</p>
        {result && <p className="notice result">{result}</p>}
        <p style={{ margin: '14px 0 0' }}><button type="submit" className="btn secondary" disabled={busy}>Import</button></p>
      </form>
    </section>
  )
}
