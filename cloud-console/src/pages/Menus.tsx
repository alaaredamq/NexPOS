// Every shop's online menu in one place, so the provider can open any of them, for example when a client asks to have their
// dishes filled in for them: the editor opens as that client, and a whole list of dishes comes in from Excel.
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { AnimatePresence, motion } from 'motion/react'
import { api, type MenuRow } from '../api'
import { ago, plural, utc } from '../format'
import { CountUp, Item, Stagger, rowMotion } from '../fx'
import { MenuImport } from '../MenuImport'
import { Icon, Loading, Pill, Problem } from '../ui'

const CHIPS = [
  ['all', 'All'], ['published', 'Published'], ['draft', 'Not published'], ['none', 'No menu yet'], ['offline', 'Offline'], ['empty', 'No dishes'],
] as const

const matches = (r: MenuRow, chip: string) =>
  chip === 'all' ? true
    : chip === 'offline' ? r.state === 'published' && !r.live
      : chip === 'empty' ? r.state !== 'none' && (r.menu?.items ?? 0) === 0
        : r.state === chip

export function Menus() {
  const menus = useQuery({ queryKey: ['menus'], queryFn: api.menus })
  const [filter, setFilter] = useState<string>('all')
  const [search, setSearch] = useState('')
  const [importing, setImporting] = useState<MenuRow | null>(null)
  const rows = useMemo(() => {
    const q = search.trim().toLowerCase()
    return (menus.data ?? []).filter((r) => matches(r, filter)
      && (!q || r.clientName.toLowerCase().includes(q) || (r.menu?.title ?? '').toLowerCase().includes(q) || (r.menu?.slug ?? '').includes(q)))
  }, [menus.data, filter, search])

  if (menus.isPending) return <Loading />
  if (menus.isError) return <Problem error={menus.error} retry={() => menus.refetch()} />
  const all = menus.data
  const published = all.filter((r) => r.state === 'published').length
  const live = all.filter((r) => r.state === 'published' && r.live).length
  const dishes = all.reduce((n, r) => n + (r.menu?.items ?? 0), 0)

  return (
    <Stagger>
      <Item as="div" className="page-head">
        <div>
          <h1>Online menus</h1>
          <p className="sub">Open any client's menu to fill it in for them: the editor opens as that shop. A long list of dishes? Import it from Excel.</p>
        </div>
      </Item>

      <Item as="div" className="g">
        <section className="card s12" aria-labelledby="h-menus">
          <div className="ph"><h2 id="h-menus">At a glance</h2></div>
          <div className="kv" style={{ marginTop: 0, paddingTop: 0, borderTop: 0 }}>
            <div><b><CountUp value={all.filter((r) => r.state !== 'none').length} /></b><span>menus made</span></div>
            <div><b><CountUp value={published} /></b><span>published</span></div>
            <div><b><CountUp value={live} /></b><span>live now</span></div>
            <div><b><CountUp value={dishes} /></b><span>dishes in all</span></div>
          </div>
        </section>
      </Item>

      <Item as="div" className="toolbar">
        <div className="search">
          <Icon id="search" />
          <input type="text" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search clients or menus" aria-label="Search clients or menus" autoComplete="off" />
        </div>
        <div className="chips" role="group" aria-label="Filter menus">
          {CHIPS.map(([key, label]) => (
            <button key={key} type="button" className="chip" aria-pressed={filter === key} onClick={() => setFilter(key)}>
              {label}<span className="n">{all.filter((r) => matches(r, key)).length}</span>
            </button>
          ))}
        </div>
      </Item>

      <Item as="div" className="list">
        {all.length === 0 ? (
          <div className="empty">
            <h3>No online menus</h3>
            <p>A client with Online gets a menu. Add one, and it shows here.</p>
            <Link className="btn" to="/new"><Icon id="plus" />New client</Link>
          </div>
        ) : <>
          <div className="list-head mrow"><span>Client</span><span>Menu</span><span>Dishes</span><span>Last change</span><span /></div>
          {rows.map((r, i) => <Row key={r.clientId} r={r} i={i} onImport={() => setImporting(r)} />)}
          {rows.length === 0 && <div className="empty"><h3>No match</h3><p>No menu matches that search and filter.</p></div>}
        </>}
      </Item>

      <AnimatePresence>
        {importing && <MenuImport key="import" clientId={importing.clientId} clientName={importing.clientName} editorUrl={importing.editorUrl}
          onClose={() => setImporting(null)} />}
      </AnimatePresence>
    </Stagger>
  )
}

function Row({ r, i, onImport }: { r: MenuRow; i: number; onImport: () => void }) {
  const m = r.menu
  const [tone, label] = r.state === 'none' ? ['offline', 'No menu yet'] : r.state === 'draft' ? ['warn', 'Not published'] : r.live ? ['ok', 'Live'] : ['bad', 'Offline']
  return (
    <motion.div className="item mrow" {...rowMotion(i)}>
      <span>
        <Link className="name" to={'/clients/' + r.clientId}>{r.clientName}</Link>
        <span className="sub">{r.type === 'menu' ? 'Online menu only' : 'POS + Online'}{!r.isActive && ' · inactive'}</span>
      </span>
      <span className="plan">
        <Pill tone={tone}>{label}</Pill>
        <small dir="auto">{m ? m.title + ' · /m/' + m.slug : 'Made when the editor is first opened'}</small>
      </span>
      <span className="data">
        {m ? <>
          <span><b>{m.items.toLocaleString('en-US')}</b> {m.items === 1 ? 'dish' : 'dishes'} · {plural(m.sections, 'section')}</span>
          <span>{m.fromPos ? 'Follows the POS' : 'Filled by hand'}{m.noPicture > 0 && ` · ${m.noPicture} no picture`}</span>
        </> : <small>-</small>}
      </span>
      <span className="seen">{m ? ago(utc(m.updatedAtUtc)) : '-'}</span>
      <span className="mrow-acts">
        <a className="btn sm" href={r.editorUrl}><Icon id="book" />Open editor</a>
        <button type="button" className="btn sm secondary" onClick={onImport} aria-label={'Import dishes for ' + r.clientName}><Icon id="upload" />Import</button>
        {m && <a className="btn sm ghost" href={m.previewUrl} target="_blank" rel="noopener" aria-label={'See the menu of ' + r.clientName} title="See the menu as guests will (works before publishing too)"><Icon id="eye" /></a>}
      </span>
    </motion.div>
  )
}
