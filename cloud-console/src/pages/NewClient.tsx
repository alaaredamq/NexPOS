import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api, type ClientType } from '../api'
import { Item, Stagger } from '../fx'
import { Icon, useAction } from '../ui'

const TYPES: [ClientType, string, string][] = [
  ['pos', 'POS only', 'offline: a licence, nothing to connect'],
  ['online', 'POS + Online', 'a licence, plus cloud copy, phone app and online menu'],
  ['menu', 'Online menu only', 'no POS, no licence: just their menu'],
]
const MONTHS: [number, string][] = [[0, 'None yet'], [1, 'month'], [3, 'months'], [6, 'months'], [12, 'months']]

export function NewClient() {
  const navigate = useNavigate()
  const { run, busy } = useAction()
  const [f, setF] = useState({ name: '', phone: '', email: '', notes: '' })
  const [type, setType] = useState<ClientType>('online')
  const [months, setMonths] = useState(1)
  const [picked, setPicked] = useState(false)
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setF({ ...f, [k]: e.target.value })
  const pickType = (t: ClientType) => {
    setType(t)
    // The menu is sold by the year and always needs months: 12 unless months were picked by hand.
    if (t === 'menu' && (!picked || months === 0)) setMonths(12)
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    const r = await run(() => api.createClient({ name: f.name.trim(), phone: f.phone || undefined, email: f.email || undefined, notes: f.notes || undefined, type, months: type === 'pos' ? 0 : months }),
      (r) => `${r.client.name} added.`)
    if (!r) return
    if (r.siteId) navigate('/sites/' + r.siteId, { state: { link: r.link ? { url: r.link.url, expiresAtUtc: r.link.expiresAtUtc } : null } })
    else navigate('/clients/' + r.client.id + (type === 'menu' ? '#phone' : '#licences'))
  }

  return (
    <Stagger>
      <Item as="div" className="page-head">
        <div>
          <Link className="crumb" to="/"><Icon id="back" />Clients</Link>
          <h1>New client</h1>
          <p className="sub">One form. For POS + Online you get their first branch and the link to send the owner; for POS only, issue their licence key next; for the online menu only, add their phone login next.</p>
        </div>
      </Item>
      <Item as="form" className="card" style={{ maxWidth: 720 }} onSubmit={submit}>
        <label htmlFor="name" style={{ marginTop: 0 }}>Shop name</label>
        <input id="name" value={f.name} onChange={set('name')} required maxLength={200} autoFocus />
        <div className="fields2">
          <div><label htmlFor="phone">Phone <span className="opt">(optional)</span></label><input id="phone" value={f.phone} onChange={set('phone')} /></div>
          <div><label htmlFor="email">Email <span className="opt">(optional)</span></label><input id="email" type="email" value={f.email} onChange={set('email')} /></div>
        </div>
        <label htmlFor="notes">Notes <span className="opt">(optional)</span></label>
        <textarea id="notes" value={f.notes} onChange={set('notes')} />

        <fieldset className="bare">
          <legend>What they bought</legend>
          <div className="plans three">
            {TYPES.map(([t, label, help]) => (
              <label key={t}><input type="radio" name="type" checked={type === t} onChange={() => pickType(t)} /><span>{label}<small>{help}</small></span></label>
            ))}
          </div>
        </fieldset>

        {type !== 'pos' && (
          <fieldset className="bare" style={{ marginTop: 14 }}>
            <legend>Months of Online, from today</legend>
            <div className="plans">
              {MONTHS.filter(([m]) => !(type === 'menu' && m === 0)).map(([m, text]) => (
                <label key={m}><input type="radio" name="months" checked={months === m} onChange={() => { setMonths(m); setPicked(true) }} /><span>{m}<small>{text}</small></span></label>
              ))}
            </div>
            <p className="help">What they paid for: one Online subscription covers the cloud copy, the phone app and the online menu. When the months run out (plus a grace week) all of it stops, the menu included, until you add more. You can add months any time. With none, nothing works yet.</p>
          </fieldset>
        )}
        <p style={{ margin: '22px 0 0' }}><button type="submit" className="btn" style={{ minHeight: 44, padding: '0 22px' }} disabled={busy}>Create client</button></p>
      </Item>
    </Stagger>
  )
}
