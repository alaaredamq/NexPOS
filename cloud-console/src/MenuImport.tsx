// Filling a shop's online menu from Excel, for the provider only. Three steps in one popup:
//   1. download the template and choose the filled file (the cloud reads and checks it; nothing is saved),
//   2. review: the totals, every dish by section, each one editable or removable (every change is checked again by the cloud),
//   3. confirm, and only then are the dishes added.
import { useEffect, useMemo, useRef, useState } from 'react'
import { AnimatePresence, motion } from 'motion/react'
import { api, type MenuImportItem, type MenuImportResult, type MenuImportRow } from './api'
import { plural } from './format'
import { Icon, Pill, useAction, useConfirm } from './ui'

type Props = { clientId: number; clientName: string; editorUrl: string; onClose: () => void }
type Phase = 'start' | 'review' | 'done'

const STATUS: Record<MenuImportItem['status'], [tone: string, label: string]> = {
  add: ['ok', 'new'], update: ['warn', 'update'], same: ['offline', 'no change'], skip: ['bad', 'problem'],
}
const toRow = (i: MenuImportItem): MenuImportRow => ({
  row: i.row, section: i.section, name: i.name, description: i.description, price: i.price, typePrices: { ...i.typePrices }, hidden: i.hidden, soldOut: i.soldOut,
})
const yes = (v: string) => /^(yes|y|true|1|نعم)$/i.test(v.trim())

export function MenuImport({ clientId, clientName, editorUrl, onClose }: Props) {
  const { run, busy } = useAction()
  const confirm = useConfirm()
  const [phase, setPhase] = useState<Phase>('start')
  const [file, setFile] = useState<File | null>(null)
  const [reading, setReading] = useState(false)
  const [refused, setRefused] = useState<string | null>(null)
  const [rows, setRows] = useState<MenuImportRow[]>([])
  const [review, setReview] = useState<MenuImportResult | null>(null)
  const [done, setDone] = useState<MenuImportResult | null>(null)
  const [editing, setEditing] = useState<number | null>(null)
  const [onlyProblems, setOnlyProblems] = useState(false)
  const [loadingTemplate, setLoadingTemplate] = useState(false)
  const picker = useRef<HTMLInputElement>(null)
  const box = useRef<HTMLDivElement>(null)

  useEffect(() => { box.current?.focus() }, [phase])
  const close = () => { if (!busy && !reading) onClose() }

  const choose = async (f: File | null) => {
    setFile(f); setRefused(null)
    if (!f) return
    if (!/\.xlsx$/i.test(f.name)) { setRefused('Choose an Excel workbook (.xlsx). An old .xls or a .csv: open it in Excel and save it as .xlsx.'); return }
    setReading(true)
    try {
      const r = await api.checkMenuFile(clientId, f)
      setReview(r); setRows(r.items.map(toRow)); setEditing(null); setOnlyProblems(false); setPhase('review')
    } catch (e) { setRefused(e instanceof Error ? e.message : 'The file could not be read.') }
    finally { setReading(false) }
  }
  const template = async () => {
    setLoadingTemplate(true)
    await run(() => api.menuTemplate(clientId))
    setLoadingTemplate(false)
  }
  /** After an edit or a removal: the cloud checks the whole list again, so the totals and every row's outcome stay true. */
  const recheck = async (next: MenuImportRow[]) => {
    if (next.length === 0) { setRows(next); setPhase('start'); setReview(null); setFile(null); return true }
    const r = await run(() => api.importMenuRows(clientId, next, false))
    if (!r) return false
    setRows(next); setReview(r); setEditing(null)
    return true
  }
  const add = async () => {
    if (!review) return
    const n = review.added + review.updated
    const parts = [plural(review.added, 'new dish', 'new dishes')]
    if (review.updated) parts.push(`${review.updated} updated`)
    if (review.newSections.length) parts.push(plural(review.newSections.length, 'new section'))
    const ok = await confirm({
      title: `Add to ${clientName}'s menu?`, yes: `Add ${plural(n, 'dish', 'dishes')}`, danger: false,
      text: parts.join(', ') + '.' + (review.skipped ? ` ${plural(review.skipped, 'row')} with a problem ${review.skipped === 1 ? 'is' : 'are'} left out.` : '')
        + ' They show on the menu at once if it is published.',
    })
    if (!ok) return
    const r = await run(() => api.importMenuRows(clientId, rows, true), (x) => `${plural(x.added, 'dish', 'dishes')} added, ${x.updated} updated.`)
    if (r) { setDone(r); setPhase('done') }
  }

  return (
    <motion.div className="c-backdrop" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} onClick={close}>
      <motion.div ref={box} tabIndex={-1} className={'c-dialog mi' + (phase === 'review' ? ' wide' : '')} role="dialog" aria-modal="true" aria-labelledby="mi-title"
        onClick={(e) => e.stopPropagation()}
        initial={{ opacity: 0, y: 14, scale: 0.96 }} animate={{ opacity: 1, y: 0, scale: 1 }} exit={{ opacity: 0, y: 8, scale: 0.98 }}
        transition={{ type: 'spring', stiffness: 380, damping: 32 }}
        onKeyDown={(e) => { if (e.key !== 'Escape') return; if (editing !== null) { e.stopPropagation(); setEditing(null) } else close() }}>
        <div className="mi-head">
          <div>
            <h3 id="mi-title">{phase === 'review' ? 'Review before adding' : phase === 'done' ? 'Dishes added' : 'Import dishes from Excel'}</h3>
            <p className="muted">{clientName}{phase === 'review' && file ? <> · <span className="mi-name">{file.name}</span></> : null}</p>
          </div>
          <button type="button" className="mi-x" aria-label="Close" onClick={close}><Icon id="x" /></button>
        </div>

        {phase === 'start' && <Start file={file} reading={reading} refused={refused} loadingTemplate={loadingTemplate}
          onTemplate={template} onPick={() => picker.current?.click()} />}
        <input ref={picker} type="file" accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" hidden
          onChange={(e) => { void choose(e.target.files?.[0] ?? null); e.target.value = '' }} />

        {phase === 'review' && review && <Review review={review} rows={rows} busy={busy} editing={editing} onlyProblems={onlyProblems}
          setOnlyProblems={setOnlyProblems} onEdit={setEditing}
          onSave={(row) => recheck(rows.map((r) => (r.row === row.row ? row : r)))}
          onRemove={(n) => recheck(rows.filter((r) => r.row !== n))} />}

        {phase === 'done' && done && <Done r={done} />}

        <div className="row">
          {phase === 'start' && <button type="button" className="btn secondary" onClick={close} disabled={reading}>Cancel</button>}
          {phase === 'review' && review && <>
            <button type="button" className="btn ghost" onClick={() => picker.current?.click()} disabled={busy || reading}><Icon id="upload" />{reading ? 'Reading…' : 'Another file'}</button>
            <span className="mi-sp" />
            <button type="button" className="btn secondary" onClick={close} disabled={busy}>Cancel</button>
            <button type="button" className="btn" onClick={add} disabled={busy || editing !== null || review.added + review.updated === 0}>
              {busy ? 'Working…' : review.added + review.updated > 0 ? `Add ${plural(review.added + review.updated, 'dish', 'dishes')}` : 'Nothing to add'}
            </button>
          </>}
          {phase === 'done' && <>
            <a className="btn secondary" href={editorUrl}><Icon id="book" />Open the menu editor</a>
            <button type="button" className="btn" onClick={onClose}>Done</button>
          </>}
        </div>
      </motion.div>
    </motion.div>
  )
}

function Start({ file, reading, refused, loadingTemplate, onTemplate, onPick }:
  { file: File | null; reading: boolean; refused: string | null; loadingTemplate: boolean; onTemplate: () => void; onPick: () => void }) {
  return <>
    <ol className="mi-steps">
      <li>
        <span className="mi-n">1</span>
        <div>
          <b>Download the template</b>
          <p className="help">Made for this menu: its sections are in a dropdown and each order type has its own price column. The Guide sheet shows an example.</p>
          <button type="button" className="btn sm secondary" onClick={onTemplate} disabled={loadingTemplate}><Icon id="download" />{loadingTemplate ? 'Preparing…' : 'Download template'}</button>
        </div>
      </li>
      <li>
        <span className="mi-n">2</span>
        <div>
          <b>Choose the filled file</b>
          <p className="help">You then see every dish by section, fix or remove any of them, and confirm. Nothing is added before that.</p>
          <div className="mi-file">
            <button type="button" className="btn sm secondary" onClick={onPick} disabled={reading}><Icon id="upload" />{reading ? 'Reading…' : file ? 'Choose another file' : 'Choose file'}</button>
            {file && <span className="mi-name" title={file.name}>{file.name}</span>}
          </div>
        </div>
      </li>
    </ol>
    <AnimatePresence initial={false}>
      {refused && <motion.p key="refused" className="notice mi-bad" initial={{ opacity: 0, y: 6 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0 }}>
        <Icon id="alert" /><span>{refused}</span>
      </motion.p>}
    </AnimatePresence>
  </>
}

type ReviewProps = {
  review: MenuImportResult; rows: MenuImportRow[]; busy: boolean; editing: number | null; onlyProblems: boolean
  setOnlyProblems: (v: boolean) => void; onEdit: (row: number | null) => void
  onSave: (row: MenuImportRow) => Promise<boolean>; onRemove: (row: number) => Promise<boolean>
}

function Review({ review, rows, busy, editing, onlyProblems, setOnlyProblems, onEdit, onSave, onRemove }: ReviewProps) {
  const items = review.items
  // By section, in the order the file has them; dishes with no section last.
  const groups = useMemo(() => {
    const map = new Map<string, { name: string; items: MenuImportItem[] }>()
    for (const i of items) {
      const key = i.section.trim().toUpperCase()
      if (!map.has(key)) map.set(key, { name: i.section.trim(), items: [] })
      map.get(key)!.items.push(i)
    }
    return [...map.values()].sort((a, b) => (a.name ? 0 : 1) - (b.name ? 0 : 1))
  }, [items])
  const sectionCount = groups.filter((g) => g.name).length
  const isNew = (name: string) => review.newSections.some((s) => s.trim().toUpperCase() === name.toUpperCase())
  const allSections = useMemo(() => [...new Set([...review.sections, ...groups.map((g) => g.name).filter(Boolean)])], [review.sections, groups])
  const shown = groups.map((g) => ({ ...g, items: onlyProblems ? g.items.filter((i) => i.status === 'skip') : g.items })).filter((g) => g.items.length)

  return <>
    <div className="mi-sum">
      <div><b>{items.length}</b><span>{items.length === 1 ? 'dish' : 'dishes'} in the file</span></div>
      <div><b>{sectionCount}</b><span>{sectionCount === 1 ? 'section' : 'sections'}{review.newSections.length ? ` · ${review.newSections.length} new` : ''}</span></div>
      <div className="good"><b>{review.added + review.updated}</b><span>to add{review.updated ? ` (${plural(review.updated, 'update')})` : ''}</span></div>
      <div className={review.skipped ? 'bad' : ''}><b>{review.skipped}</b><span>with a problem</span></div>
    </div>

    {review.skipped > 0 && <div className="mi-bar">
      <p className="help">Fix a dish with a problem (<Icon id="edit" className="icon mi-inline" />) or remove it; what is left with a problem is not added.</p>
      <div className="chips" role="group" aria-label="Show">
        <button type="button" className="chip" aria-pressed={!onlyProblems} onClick={() => setOnlyProblems(false)}>All<span className="n">{items.length}</span></button>
        <button type="button" className="chip" aria-pressed={onlyProblems} onClick={() => setOnlyProblems(true)}>Problems<span className="n">{review.skipped}</span></button>
      </div>
    </div>}

    <div className="mi-list" aria-busy={busy}>
      {shown.map((g) => (
        <section key={g.name || '—'} className="mi-sec">
          <h4><span dir="auto">{g.name || 'No section'}</span>{g.name && isNew(g.name) && <Pill tone="in">new section</Pill>}<span className="n">{g.items.length}</span></h4>
          <ul>
            {g.items.map((i) => editing === i.row
              ? <Editor key={i.row} row={rows.find((r) => r.row === i.row)!} types={review.orderTypes} sections={allSections} busy={busy}
                  onCancel={() => onEdit(null)} onSave={onSave} />
              : <Dish key={i.row} i={i} types={review.orderTypes} busy={busy} locked={editing !== null} onEdit={() => onEdit(i.row)} onRemove={() => onRemove(i.row)} />)}
          </ul>
        </section>
      ))}
      {shown.length === 0 && <p className="muted mi-none">No dish has a problem now.</p>}
    </div>
  </>
}

function Dish({ i, types, busy, locked, onEdit, onRemove }:
  { i: MenuImportItem; types: MenuImportResult['orderTypes']; busy: boolean; locked: boolean; onEdit: () => void; onRemove: () => void }) {
  const [tone, label] = STATUS[i.status]
  const badPrice = !i.priceText && i.price.trim()
  return (
    <li className={'mi-dish ' + i.status}>
      <div className="mi-main">
        <b dir="auto">{i.name || <span className="muted">(no name)</span>}</b>
        {i.description && <small dir="auto">{i.description}</small>}
        {(yes(i.hidden) || yes(i.soldOut) || i.fromPos) && <span className="mi-flags">
          {yes(i.hidden) && <Pill plain>hidden</Pill>}{yes(i.soldOut) && <Pill plain>sold out</Pill>}{i.fromPos && <Pill plain>from the POS</Pill>}
        </span>}
        {i.message && <p className={'mi-msg' + (i.status === 'skip' ? ' bad' : '')}><Icon id="alert" />{i.message}</p>}
      </div>
      <div className="mi-price">
        {i.priceText ? <b>{i.priceText}</b> : badPrice ? <b className="bad">{i.price}</b> : <span className="muted">no price</span>}
        {types.length > 0 && i.typePriceTexts.map((t) => <small key={t.type}>{t.type} {t.price}</small>)}
      </div>
      <Pill tone={tone}>{label}</Pill>
      <span className="mi-acts">
        <button type="button" className="mi-ib" onClick={onEdit} disabled={busy || locked} aria-label={'Edit ' + i.name} title="Edit"><Icon id="edit" /></button>
        <button type="button" className="mi-ib" onClick={onRemove} disabled={busy || locked} aria-label={'Remove ' + i.name + ' from this import'} title="Remove from this import"><Icon id="x" /></button>
      </span>
    </li>
  )
}

function Editor({ row, types, sections, busy, onCancel, onSave }:
  { row: MenuImportRow; types: MenuImportResult['orderTypes']; sections: string[]; busy: boolean; onCancel: () => void; onSave: (r: MenuImportRow) => Promise<boolean> }) {
  const [r, setR] = useState<MenuImportRow>({ ...row, typePrices: { ...row.typePrices } })
  const set = (k: keyof MenuImportRow, v: string) => setR((x) => ({ ...x, [k]: v }))
  const first = useRef<HTMLInputElement>(null)
  useEffect(() => { first.current?.focus() }, [])
  const id = (k: string) => `mi-${row.row}-${k}`
  const flag = (k: 'hidden' | 'soldOut', label: string) => {
    const v = r[k].trim().toLowerCase()
    const valid = ['', 'yes', 'no'].includes(v)
    return (
      <div>
        <label htmlFor={id(k)}>{label}</label>
        <select id={id(k)} value={valid ? v : r[k]} onChange={(e) => set(k, e.target.value)}>
          <option value="">-</option><option value="yes">Yes</option><option value="no">No</option>
          {!valid && <option value={r[k]}>{r[k]} (not valid)</option>}
        </select>
      </div>
    )
  }
  return (
    <li className="mi-edit">
      <form onSubmit={async (e) => { e.preventDefault(); await onSave(r) }}>
        <div className="mi-grid">
          <div className="span2">
            <label htmlFor={id('name')}>Name</label>
            <input ref={first} id={id('name')} value={r.name} onChange={(e) => set('name', e.target.value)} maxLength={200} dir="auto" required />
          </div>
          <div>
            <label htmlFor={id('section')}>Section</label>
            <input id={id('section')} value={r.section} onChange={(e) => set('section', e.target.value)} maxLength={120} list={id('sections')} dir="auto" placeholder="No section" />
            <datalist id={id('sections')}>{sections.map((s) => <option key={s} value={s} />)}</datalist>
          </div>
          <div className="span3">
            <label htmlFor={id('desc')}>Description</label>
            <input id={id('desc')} value={r.description} onChange={(e) => set('description', e.target.value)} maxLength={500} dir="auto" />
          </div>
          <div>
            <label htmlFor={id('price')}>Price</label>
            <input id={id('price')} value={r.price} onChange={(e) => set('price', e.target.value)} inputMode="decimal" />
          </div>
          {types.map((t) => (
            <div key={t.id}>
              <label htmlFor={id('t' + t.id)}>{t.name} price</label>
              <input id={id('t' + t.id)} value={r.typePrices[String(t.id)] ?? ''} inputMode="decimal" placeholder="By the type's rule"
                onChange={(e) => setR((x) => ({ ...x, typePrices: { ...x.typePrices, [String(t.id)]: e.target.value } }))} />
            </div>
          ))}
          {flag('hidden', 'Hidden')}
          {flag('soldOut', 'Sold out')}
        </div>
        <div className="row">
          <button type="button" className="btn sm secondary" onClick={onCancel} disabled={busy}>Cancel</button>
          <button type="submit" className="btn sm" disabled={busy}>{busy ? 'Checking…' : 'Save'}</button>
        </div>
      </form>
    </li>
  )
}

function Done({ r }: { r: MenuImportResult }) {
  return <>
    <p className="mi-ok"><Icon id="check" /><span>On the menu now. Add pictures in the menu editor.</span></p>
    <div className="mi-sum">
      <div className="good"><b>{r.added}</b><span>added</span></div>
      <div><b>{r.updated}</b><span>updated</span></div>
      <div><b>{r.newSections.length}</b><span>new sections</span></div>
      <div className={r.skipped ? 'bad' : ''}><b>{r.skipped}</b><span>left out</span></div>
    </div>
    {r.newSections.length > 0 && <p className="help">New sections: {r.newSections.join(', ')}</p>}
  </>
}
