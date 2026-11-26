import { useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { motion } from 'motion/react'
import { api, type Attention, type ClientRow } from '../api'
import { planPill } from '../format'
import { CountUp, Item, Stagger, rowMotion } from '../fx'
import { AttentionList, ContactChart, Donut, Icon, Loading, Pill, Problem, Pulse, Ring } from '../ui'

const CHIPS = [
  ['all', 'All'], ['connected', 'Connected'], ['waiting', 'Waiting for a link'], ['attention', 'Needs you'], ['offline', 'POS only'], ['menu', 'Menu only'],
] as const

/** Where an attention item's button goes, in the console's own addresses. */
export function attentionTarget(a: Attention): string {
  const p = a.portalPath ?? ''
  if (p.startsWith('/admin/Licenses')) return '/licences' + (p.includes('?') ? p.slice(p.indexOf('?')) : '')
  if (p.startsWith('/admin/Finance')) return '/finance'
  const site = p.match(/\/admin\/Sites\/Details\/([0-9a-f-]{36})/i)
  if (site) return '/sites/' + site[1]
  if (a.clientId) return '/clients/' + a.clientId + (p.includes('#') ? p.slice(p.indexOf('#')) : '')
  return '/'
}

export function Clients() {
  const navigate = useNavigate()
  const overview = useQuery({ queryKey: ['overview'], queryFn: api.overview })
  const clients = useQuery({ queryKey: ['clients'], queryFn: api.clients })
  const [filter, setFilter] = useState<string>('all')
  const [search, setSearch] = useState('')
  const rows = useMemo(() => {
    const q = search.trim().toLowerCase()
    return (clients.data ?? []).filter((r) => (filter === 'all' || r.tags.includes(filter)) && (!q || r.name.toLowerCase().includes(q)))
  }, [clients.data, filter, search])

  if (overview.isPending || clients.isPending) return <Loading />
  if (overview.isError) return <Problem error={overview.error} retry={() => overview.refetch()} />
  if (clients.isError) return <Problem error={clients.error} retry={() => clients.refetch()} />
  const o = overview.data, all = clients.data
  const total = Math.max(1, o.clients)

  return (
    <Stagger>
      <Item as="div" className="page-head">
        <div>
          <h1>Clients</h1>
          <p className="sub">{o.summary}</p>
        </div>
        <Link className="btn" to="/new"><Icon id="plus" />New client</Link>
      </Item>

      {o.clients === 0 ? (
        <Item as="div" className="list"><div className="empty">
          <h3>No clients yet</h3>
          <p>Add the first shop. You get a link to send the owner; they paste it on the shop PC and the shop connects.</p>
          <Link className="btn" to="/new"><Icon id="plus" />Add the first client</Link>
        </div></Item>
      ) : <>
        <Item as="div" className="g">
          <section className="card s8" aria-labelledby="h-contact">
            <div className="ph">
              <h2 id="h-contact">Shops in contact</h2>
              <Pill tone={o.connected === o.clients ? 'ok' : 'warn'}>{o.litHours} of the last 24 hours had contact</Pill>
            </div>
            <div className="hero-row">
              <Ring share={o.connected / total} />
              <div>
                <div className="big"><CountUp value={o.connected} /> <span className="of">of {o.clients} clients</span></div>
                <p className="muted" style={{ margin: '8px 0 0' }}>have a shop PC connected and sending data.</p>
              </div>
            </div>
            <ContactChart cells={o.pulse} hourLabel={(d) => String(d.getHours()).padStart(2, '0')} />
          </section>
          <section className="card s4 plans-card" aria-labelledby="h-plans">
            <div className="ph"><h2 id="h-plans">Online plans</h2></div>
            <div className="donut-row">
              <Donut total={total} label={`Plans: ${o.plans.active} active, ${o.plans.endingSoon} ending soon, ${o.plans.ended} ended, of ${o.clients} clients`}
                parts={[{ value: o.plans.active, color: 'var(--data)' }, { value: o.plans.endingSoon, color: 'var(--amber)' }, { value: o.plans.ended, color: 'var(--bad)' }]}
                center={<><b><CountUp value={o.plans.active} /></b><small>active</small></>} />
              <div className="legend-list">
                <div className="pr"><span className="sw" style={{ background: 'var(--data)' }} /><span className="nm">Active</span><span className="am"><CountUp value={o.plans.active} /></span></div>
                <div className="pr"><span className="sw" style={{ background: 'var(--amber)' }} /><span className="nm">Ends within 14 days</span><span className="am"><CountUp value={o.plans.endingSoon} /></span></div>
                <div className="pr"><span className="sw" style={{ background: 'var(--bad)' }} /><span className="nm">Ended or not started</span><span className="am"><CountUp value={o.plans.ended} /></span></div>
              </div>
            </div>
            <div className="kv">
              <div><b><CountUp value={o.productsHeld} /></b><span>products held</span></div>
              <div><b><CountUp value={o.ordersHeld} /></b><span>orders held</span></div>
            </div>
          </section>
        </Item>

        {o.attention.length > 0 && (
          <Item className="card" aria-labelledby="needs">
            <div className="ph"><h2 id="needs">Needs you</h2><Pill tone="warn">{o.attention.length}</Pill></div>
            <AttentionList items={o.attention} onOpen={(a) => navigate(attentionTarget(a))} />
          </Item>
        )}

        <Item as="div" className="toolbar">
          <div className="search">
            <Icon id="search" />
            <input type="text" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search clients" aria-label="Search clients" autoComplete="off" />
          </div>
          <div className="chips" role="group" aria-label="Filter clients">
            {CHIPS.map(([key, label]) => (
              <button key={key} type="button" className="chip" aria-pressed={filter === key} onClick={() => setFilter(key)}>
                {label}<span className="n">{key === 'all' ? all.length : all.filter((r) => r.tags.includes(key)).length}</span>
              </button>
            ))}
          </div>
        </Item>

        <Item as="div" className="list">
          <div className="list-head"><span>Client</span><span>Online plan</span><span>Data</span><span>Last 24 hours</span><span>Last contact</span><span /></div>
          {rows.map((r, i) => <Row key={r.id} r={r} i={i} onOpen={() => navigate('/clients/' + r.id)} />)}
          {rows.length === 0 && <div className="empty"><h3>No match</h3><p>No client matches that search and filter.</p></div>}
        </Item>
      </>}
    </Stagger>
  )
}

function Row({ r, i, onOpen }: { r: ClientRow; i: number; onOpen: () => void }) {
  const [tone, label] = planPill(r.subscription?.state)
  const sub = (r.type === 'pos' && r.branches === 0 ? 'POS only' : r.type === 'menu' && r.branches === 0 ? 'Online menu only'
    : r.branches === 0 ? 'No branch yet' : r.branches === 1 ? '1 branch' : `${r.branches} branches`) + (r.phone ? ' · ' + r.phone : '')
  return (
    <motion.a className="item" href={'/console/clients/' + r.id} onClick={(e) => { if (!e.metaKey && !e.ctrlKey) { e.preventDefault(); onOpen() } }} {...rowMotion(i)}>
      <span>
        <span className="name">{r.name} {!r.isActive && <Pill plain>inactive</Pill>}</span>
        <span className="sub">{sub}</span>
      </span>
      <span className="plan">
        {r.type === 'pos' && (!r.subscription || r.subscription.state === 'None')
          ? <><Pill tone="offline" plain>POS only</Pill><small>no Online</small></>
          : <><Pill tone={tone}>{label}</Pill><small>{r.subscription?.note}</small></>}
      </span>
      <span className="data">
        {r.products == null ? <small>No data yet</small> : <>
          <span><b>{r.products.toLocaleString('en-US')}</b> products</span>
          <span><b>{(r.orders ?? 0).toLocaleString('en-US')}</b> orders</span>
        </>}
      </span>
      <Pulse cells={r.pulse} row={i} />
      <span className="seen">{r.seen}</span>
      <Icon id="chev" className="icon chev" />
    </motion.a>
  )
}
