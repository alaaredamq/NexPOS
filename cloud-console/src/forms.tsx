// The money forms, shared by the client page and Finance. Choices (methods, purposes, categories) come from /finance/options.
import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from './api'
import { getCurrency, isoToday } from './format'
import { useAction } from './ui'

const num = (s: string) => { const v = parseFloat(s.replace(/,/g, '').trim()); return isFinite(v) ? v : NaN }

export function useFinanceOptions() {
  return useQuery({ queryKey: ['finance-options'], queryFn: api.financeOptions, staleTime: 10 * 60_000 })
}

/** Record a payment. With `clientId` it is that client's; otherwise "From" offers every client or other income. */
export function PaymentForm({ clientId, clients, onDone }: { clientId?: number; clients?: { id: number; name: string }[]; onDone?: () => void }) {
  const options = useFinanceOptions()
  const { run, busy } = useAction()
  const [from, setFrom] = useState<string>('')
  const [payer, setPayer] = useState('')
  const [amount, setAmount] = useState('')
  const [date, setDate] = useState(isoToday())
  const [purposes, setPurposes] = useState<string[]>(['Online subscription'])
  const [method, setMethod] = useState('Cash')
  const [reference, setReference] = useState('')
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)
  const o = options.data

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    const value = num(amount)
    if (!(value > 0)) { setError('Type the amount received.'); return }
    if (purposes.length === 0) { setError('Tick what the payment was for.'); return }
    setError(null)
    const ok = await run(() => api.savePayment({
      clientId: clientId ?? (from ? Number(from) : null), payer: clientId || from ? null : payer || null,
      amount: value, date, method: method || 'Cash', purposes, reference: reference || null, note: note || null,
    }), 'Payment recorded.')
    if (ok) { setAmount(''); setReference(''); setNote(''); setPayer(''); onDone?.() }
  }

  return (
    <form onSubmit={submit}>
      {clientId === undefined && (
        <div className="fields2">
          <div>
            <label htmlFor="p-client">From</label>
            <select id="p-client" value={from} onChange={(e) => setFrom(e.target.value)}>
              <option value="">Other income (not a client)</option>
              {(clients ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          </div>
          {!from && <div>
            <label htmlFor="p-payer">Where it came from <span className="opt">(if not a client)</span></label>
            <input id="p-payer" value={payer} onChange={(e) => setPayer(e.target.value)} maxLength={200} placeholder="Capital, loan, refund..." />
          </div>}
        </div>
      )}
      <div className="fields2">
        <div>
          <label htmlFor="p-amount">Amount ({getCurrency()})</label>
          <input id="p-amount" value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="decimal" autoComplete="off" required />
        </div>
        <div>
          <label htmlFor="p-date">Received on</label>
          <input id="p-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} required />
        </div>
      </div>
      <fieldset className="checks-field">
        <legend>For <span className="opt">(tick all it covers)</span></legend>
        <div className="checks">
          {(o?.purposes ?? []).map((p) => (
            <label key={p}><input type="checkbox" checked={purposes.includes(p)}
              onChange={(e) => setPurposes(e.target.checked ? [...purposes, p] : purposes.filter((x) => x !== p))} /><span>{p}</span></label>
          ))}
        </div>
      </fieldset>
      <div className="fields2">
        <div>
          <label htmlFor="p-method">Paid by</label>
          <input id="p-method" list="p-methods" value={method} onChange={(e) => setMethod(e.target.value)} maxLength={40} />
          <datalist id="p-methods">{(o?.methods ?? []).map((m) => <option key={m} value={m} />)}</datalist>
        </div>
        <div>
          <label htmlFor="p-ref">Receipt or transfer no. <span className="opt">(optional)</span></label>
          <input id="p-ref" value={reference} onChange={(e) => setReference(e.target.value)} maxLength={100} />
        </div>
      </div>
      <label htmlFor="p-note">Note <span className="opt">(optional)</span></label>
      <input id="p-note" value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} placeholder="For several things, the split if you want it: licence 300, Online 120..." />
      {error && <p className="error" role="alert">{error}</p>}
      <p style={{ margin: '16px 0 0' }}><button type="submit" className="btn" disabled={busy}>Record payment</button></p>
    </form>
  )
}

export function ExpenseForm({ onDone }: { onDone?: () => void }) {
  const options = useFinanceOptions()
  const { run, busy } = useAction()
  const [amount, setAmount] = useState('')
  const [date, setDate] = useState(isoToday())
  const [category, setCategory] = useState('')
  const [payee, setPayee] = useState('')
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    const value = num(amount)
    if (!(value > 0)) { setError('Type the amount spent.'); return }
    if (!category.trim()) { setError('Say what it was spent on.'); return }
    setError(null)
    const ok = await run(() => api.saveExpense({ amount: value, date, category: category.trim(), payee: payee || null, note: note || null }), 'Expense recorded.')
    if (ok !== undefined) { setAmount(''); setPayee(''); setNote(''); onDone?.() }
  }
  return (
    <form onSubmit={submit}>
      <div className="fields2">
        <div>
          <label htmlFor="e-amount">Amount ({getCurrency()})</label>
          <input id="e-amount" value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="decimal" autoComplete="off" required />
        </div>
        <div>
          <label htmlFor="e-date">Paid on</label>
          <input id="e-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} required />
        </div>
      </div>
      <label htmlFor="e-cat">Spent on</label>
      <input id="e-cat" list="e-cats" value={category} onChange={(e) => setCategory(e.target.value)} maxLength={60} placeholder="Pick or type" required />
      <datalist id="e-cats">{(options.data?.expenseCategories ?? []).map((c) => <option key={c} value={c} />)}</datalist>
      <label htmlFor="e-payee">Paid to <span className="opt">(optional)</span></label>
      <input id="e-payee" value={payee} onChange={(e) => setPayee(e.target.value)} maxLength={200} />
      <label htmlFor="e-note">Note <span className="opt">(optional)</span></label>
      <input id="e-note" value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} />
      {error && <p className="error" role="alert">{error}</p>}
      <p style={{ margin: '16px 0 0' }}><button type="submit" className="btn" disabled={busy}>Record expense</button></p>
    </form>
  )
}
