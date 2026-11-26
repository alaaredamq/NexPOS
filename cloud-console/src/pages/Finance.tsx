import { useMemo, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { motion } from 'motion/react'
import { api } from '../api'
import { day, money, moneyShort, monthName, shortDay, utc, until } from '../format'
import { CountUp, Item, Stagger, rowMotion } from '../fx'
import { ExpenseForm, PaymentForm } from '../forms'
import { AttentionList, Count, Fill, Fold, Icon, Loading, PairChart, Pill, Problem, useAction, useConfirm } from '../ui'
import { attentionTarget } from './Clients'

const niceTop = (v: number) => {
  if (v <= 0) return 100
  const p = Math.pow(10, Math.floor(Math.log10(v)))
  const n = v / p
  return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10) * p
}

export function Finance() {
  const [params, setParams] = useSearchParams()
  const period = params.get('m') ? 'm=' + params.get('m') : params.get('y') ? 'y=' + params.get('y') : undefined
  const summary = useQuery({ queryKey: ['finance', period ?? 'now'], queryFn: () => api.financeSummary(period), placeholderData: keepPreviousData })
  const clients = useQuery({ queryKey: ['clients'], queryFn: api.clients })
  const navigate = useNavigate()
  const { run } = useAction()
  const confirm = useConfirm()
  const [filter, setFilter] = useState('')
  const go = (q?: string | null) => { if (!q) return; const [k, v] = q.split('='); setParams({ [k]: v }) }
  const lines = useMemo(() => {
    const q = filter.trim().toLowerCase()
    return (summary.data?.lines ?? []).filter((l) => !q || `${l.who} ${l.details} ${l.note ?? ''}`.toLowerCase().includes(q))
  }, [summary.data, filter])

  if (summary.isPending) return <Loading />
  if (summary.isError) return <Problem error={summary.error} retry={() => summary.refetch()} />
  const r = summary.data, p = r.period, isYear = p.kind === 'year'
  const was = isYear ? 'last year' : 'last month'
  const top = niceTop(Math.max(0, ...r.trend.map((t) => Math.max(t.collected, t.spent))))
  const maxPayer = Math.max(1, ...r.byPayer.map((x) => x.amount))
  const maxCat = Math.max(1, ...r.byCategory.map((x) => x.amount))
  const people = (clients.data ?? []).map((c) => ({ id: c.id, name: c.name }))
  const thisMonth = new Date()
  const avg = r.averages ?? { collected: 0, spent: 0, profit: 0 }
  const scrollTo = (id: string) => document.getElementById(id)?.scrollIntoView({ behavior: 'smooth' })

  return (
    <Stagger key={p.query}>
      <Item as="div" className="page-head">
        <div>
          <h1>Finance</h1>
          <p className="sub">What came in, what went out, and what is left. Amounts in {r.currency}.</p>
        </div>
        <div className="row">
          <button type="button" className="btn secondary" onClick={() => scrollTo('record-payment')}><Icon id="plus" />Record payment</button>
          <button type="button" className="btn secondary" onClick={() => scrollTo('record-expense')}><Icon id="plus" />Record expense</button>
        </div>
      </Item>

      <Item as="div" className="toolbar period">
        <button type="button" className="btn secondary sm" onClick={() => go(p.previous)} aria-label={'Previous ' + (isYear ? 'year' : 'month')}><Icon id="back" /></button>
        <h2 className="period-label num">{p.label}</h2>
        <button type="button" className="btn secondary sm" onClick={() => go(p.next)} aria-label={'Next ' + (isYear ? 'year' : 'month')}><Icon id="chev" /></button>
        <div className="chips">
          <button type="button" className="chip" aria-pressed={!isYear} onClick={() => setParams({ m: `${thisMonth.getFullYear()}-${String(thisMonth.getMonth() + 1).padStart(2, '0')}` })}>Month</button>
          <button type="button" className="chip" aria-pressed={isYear} onClick={() => setParams({ y: p.from.slice(0, 4) })}>Year</button>
        </div>
        <button type="button" className="btn ghost sm" style={{ marginInlineStart: 'auto' }} onClick={() => run(() => api.exportCsv(p.query))}>Export CSV</button>
      </Item>

      <Item as="div" className="g">
        <section className="card s3 tile" aria-labelledby="k-in">
          <h2 id="k-in"><span className="sw" style={{ background: 'var(--income)' }} />Collected</h2>
          <div className="kpi num"><CountUp value={r.collected} format={money} /></div>
          <p className="delta">{r.change?.collected ? `${r.change.collected} vs ${was}` : `${money(r.previous?.collected ?? 0)} ${was}`}</p>
        </section>
        <section className="card s3 tile" aria-labelledby="k-out">
          <h2 id="k-out"><span className="sw" style={{ background: 'var(--spend)' }} />Spent</h2>
          <div className="kpi num"><CountUp value={r.spent} format={money} /></div>
          <p className="delta">{r.change?.spent ? `${r.change.spent} vs ${was}` : `${money(r.previous?.spent ?? 0)} ${was}`}</p>
        </section>
        <section className="card s3 tile" aria-labelledby="k-profit">
          <h2 id="k-profit">Profit</h2>
          <div className={'kpi num' + (r.profit < 0 ? ' neg' : '')}><CountUp value={r.profit} format={money} /></div>
          <p className="delta">{r.marginPercent != null ? `${r.marginPercent}% of what came in` : 'Nothing collected yet'}</p>
        </section>
        <section className="card s3 tile" aria-labelledby="k-bal">
          <h2 id="k-bal">Balance</h2>
          <div className={'kpi num' + (r.balance < 0 ? ' neg' : '')}><CountUp value={r.balance} format={money} /></div>
          <p className="delta">All money in minus out, to {p.to}</p>
        </section>
      </Item>

      {r.attention.length > 0 && (
        <Item className="card" aria-labelledby="f-needs">
          <div className="ph"><h2 id="f-needs">Needs you</h2><Pill tone="warn">{r.attention.length}</Pill></div>
          <AttentionList items={r.attention} onOpen={(a) => navigate(attentionTarget(a))} />
        </Item>
      )}

      <Item as="div" className="g">
        <section className="card s8" aria-labelledby="h-trend">
          <div className="ph">
            <h2 id="h-trend">Last 12 months</h2>
            <div className="legend" aria-hidden="true"><span><i style={{ background: 'var(--income)' }} />Collected</span><span><i style={{ background: 'var(--spend)' }} />Spent</span></div>
          </div>
          <PairChart top={top} label={moneyShort} selected={isYear ? undefined : p.from.slice(0, 7)} onPick={(m) => setParams({ m })}
            tip={(m) => { const t = r.trend.find((x) => x.month === m)!; return `${monthName(m, true)}: collected ${money(t.collected)}, spent ${money(t.spent)}, profit ${money(t.profit)}` }}
            rows={r.trend.map((t) => ({ key: t.month, a: t.collected, b: t.spent, x: monthName(t.month) }))} />
          <div className="kv">
            <div><b><CountUp value={avg.collected} format={money} /></b><span>average collected a month</span></div>
            <div><b><CountUp value={avg.spent} format={money} /></b><span>average spent a month</span></div>
            <div><b><CountUp value={avg.profit} format={money} /></b><span>average profit a month</span></div>
          </div>
          <Fold title="Show as a table">
            <div className="tscroll"><table className="t">
              <thead><tr><th>Month</th><th className="r">Collected</th><th className="r">Spent</th><th className="r">Profit</th></tr></thead>
              <tbody>{[...r.trend].reverse().map((t) => (
                <tr key={t.month}><td>{monthName(t.month, true)}</td><td className="r">{money(t.collected)}</td><td className="r">{money(t.spent)}</td><td className={'r' + (t.profit < 0 ? ' neg' : '')}>{money(t.profit)}</td></tr>
              ))}</tbody>
            </table></div>
          </Fold>
        </section>
        <Budget r={r} />
      </Item>

      <Item as="div" className="g">
        <section className="card s6" aria-labelledby="h-from">
          <div className="ph"><h2 id="h-from">Collected from</h2><span className="muted num">{money(r.collected)}</span></div>
          {r.byPayer.length === 0 ? <p className="muted">Nothing collected in {p.label}.</p> : <>
            <ul className="hbars">
              {r.byPayer.slice(0, 12).map((x, i) => (
                <li key={x.name + i}>
                  <span className="nm">{x.clientId ? <Link to={'/clients/' + x.clientId}>{x.name}</Link> : x.name}<small>{x.count} payment{x.count === 1 ? '' : 's'}</small></span>
                  <span className="am num">{money(x.amount)}</span>
                  <Fill thin percent={(x.amount * 100) / maxPayer} color="var(--income)" i={i} />
                </li>
              ))}
            </ul>
            {r.byPayer.length > 12 && <p className="help">And {r.byPayer.length - 12} more; the list below has them all.</p>}
            <h3 className="minor">What it was for</h3>
            {r.byPurpose.map((x) => (
              <div key={x.name} className="pr"><span className="nm">{x.name}</span><span className="muted">{r.collected ? Math.round((x.amount * 100) / r.collected) : 0}%</span><span className="am">{money(x.amount)}</span></div>
            ))}
          </>}
        </section>
        <section className="card s6" aria-labelledby="h-on">
          <div className="ph"><h2 id="h-on">Spent on</h2><span className="muted num">{money(r.spent)}</span></div>
          {r.byCategory.length === 0 ? <p className="muted">Nothing spent in {p.label}.</p> : (
            <ul className="hbars">
              {r.byCategory.map((x, i) => (
                <li key={x.name}>
                  <span className="nm">{x.name}<small>{r.spent ? Math.round((x.amount * 100) / r.spent) : 0}% · {x.count} expense{x.count === 1 ? '' : 's'}</small></span>
                  <span className="am num">{money(x.amount)}</span>
                  <Fill thin percent={(x.amount * 100) / maxCat} color="var(--spend)" i={i} />
                </li>
              ))}
            </ul>
          )}
        </section>
      </Item>

      {r.renewals.length > 0 && (
        <Item className="card" aria-labelledby="h-due">
          <div className="ph">
            <h2 id="h-due">Renewals due in the next 30 days</h2>
            <span className="muted">about <b className="num">{money(r.renewalsExpected)}</b> to collect, going by what each client paid last time</span>
          </div>
          <div className="tscroll"><table className="t">
            <thead><tr><th>Client</th><th>What ends</th><th className="r">Paid last time</th><th /></tr></thead>
            <tbody>{r.renewals.map((d, i) => {
              const ends = utc(d.endsAtUtc)
              return (
                <tr key={i}>
                  <td><Link to={'/clients/' + d.clientId}>{d.name}</Link></td>
                  <td>{d.what ?? 'Online ends'} {day(ends)} <span className="muted">· {until(ends)}</span></td>
                  <td className="r">{d.lastPaid != null ? money(d.lastPaid) : 'none on record'}</td>
                  <td className="r nowrap">{d.licenseId
                    ? <Link className="btn sm secondary" to={`/licences?renew=${d.licenseId}#issue`}>Issue key</Link>
                    : <Link className="btn sm secondary" to={`/clients/${d.clientId}#payments`}>Record payment</Link>}</td>
                </tr>
              )
            })}</tbody>
          </table></div>
        </Item>
      )}

      <Item as="div" className="g">
        <section className="card s6" id="record-payment" aria-labelledby="h-pay">
          <h2 id="h-pay"><span className="sw" style={{ background: 'var(--income)' }} />Record payment</h2>
          <PaymentForm clients={people} />
        </section>
        <section className="card s6" id="record-expense" aria-labelledby="h-exp">
          <h2 id="h-exp"><span className="sw" style={{ background: 'var(--spend)' }} />Record expense</h2>
          <ExpenseForm />
        </section>
      </Item>

      <Item className="card" aria-labelledby="h-tx">
        <div className="ph">
          <h2 id="h-tx">Everything in {p.label} <Count n={r.lines.length} /></h2>
          {r.lines.length > 0 && <div className="search" style={{ maxWidth: 260 }}>
            <Icon id="search" />
            <input type="text" value={filter} onChange={(e) => setFilter(e.target.value)} placeholder="Filter" aria-label="Filter the list" autoComplete="off" />
          </div>}
        </div>
        {r.lines.length === 0 ? <p className="muted">Nothing recorded in {p.label} yet.</p> : (
          <div className="tscroll"><table className="t tx">
            <thead><tr><th>Day</th><th /><th>From / on</th><th>Details</th><th className="r">Amount</th><th /></tr></thead>
            <tbody>{lines.map((l, i) => {
              const income = l.type === 'payment'
              return (
                <motion.tr key={l.type + l.id} {...rowMotion(i)}>
                  <td className="nowrap">{shortDay(l.date)}</td>
                  <td><Pill tone={income ? 'in' : 'out'} plain>{income ? 'In' : 'Out'}</Pill></td>
                  <td>{l.clientId ? <Link to={'/clients/' + l.clientId}>{l.who}</Link> : l.who}</td>
                  <td><span>{l.details}</span>{l.note && <small className="muted d">{l.note}</small>}</td>
                  <td className={'r num nowrap' + (income ? '' : ' neg-soft')}>{income ? '+' : '−'}{money(l.amount)}</td>
                  <td className="r"><button type="button" className="btn ghost sm" aria-label="Delete" onClick={async () => {
                    if (await confirm({ title: income ? 'Delete this payment?' : 'Delete this expense?', yes: 'Delete', text: `${money(l.amount)} on ${l.date}. It comes off every total.` }))
                      await run(() => (income ? api.deletePayment(l.id) : api.deleteExpense(l.id)), income ? 'Payment deleted.' : 'Expense deleted.')
                  }}>Delete</button></td>
                </motion.tr>
              )
            })}</tbody>
          </table></div>
        )}
      </Item>
    </Stagger>
  )
}

function Budget({ r }: { r: import('../api').FinanceSummary }) {
  const { run, busy } = useAction()
  const p = r.period
  const b = r.budget
  const [target, setTarget] = useState(b?.monthBudget?.incomeTarget?.toString() ?? '')
  const [limit, setLimit] = useState(b?.monthBudget?.spendingLimit?.toString() ?? '')
  const num = (s: string) => { const v = parseFloat(s.replace(/,/g, '')); return isFinite(v) && v > 0 ? v : null }
  return (
    <section className="card s4" aria-labelledby="h-budget">
      <div className="ph"><h2 id="h-budget">Budget · {p.label}</h2></div>
      {!b?.income && !b?.spending && <p className="muted">No budget set. Set what you mean to collect and the most you mean to spend each month, and see how the month is going.</p>}
      {b?.income && <div className="meter-row">
        <div className="mh"><span>Collected of target</span><b className="num"><CountUp value={b.income.percent} />%</b></div>
        <Fill percent={b.income.percent} color="var(--income)" />
        <p className="help">{money(b.income.actual)} of {money(b.income.planned)} · {b.income.left > 0 ? `${money(b.income.left)} to go` : 'target reached'}</p>
      </div>}
      {b?.spending && <div className="meter-row">
        <div className="mh"><span>Spent of limit</span><b className="num"><CountUp value={b.spending.percent} />%</b></div>
        <Fill percent={b.spending.percent} color={b.spending.left < 0 ? 'var(--bad)' : 'var(--spend)'} i={1} />
        <p className="help">{money(b.spending.actual)} of {money(b.spending.planned)} · {b.spending.left >= 0 ? `${money(b.spending.left)} left` : `${money(-b.spending.left)} over the limit`}</p>
      </div>}
      {p.kind === 'month' ? (
        <Fold title={b?.monthBudget ? 'Change the budget' : 'Set a budget'}>
          <form onSubmit={(e) => { e.preventDefault(); void run(() => api.setBudget(p.from.slice(0, 7), { incomeTarget: num(target), spendingLimit: num(limit) }), 'Budget saved.') }}>
            <label htmlFor="b-target">Collect each month <span className="opt">(target)</span></label>
            <input id="b-target" inputMode="decimal" autoComplete="off" value={target} onChange={(e) => setTarget(e.target.value)} />
            <label htmlFor="b-limit">Spend at most each month <span className="opt">(limit)</span></label>
            <input id="b-limit" inputMode="decimal" autoComplete="off" value={limit} onChange={(e) => setLimit(e.target.value)} />
            <p className="help">Applies from {p.label} on, until you change it. Leave a box empty for none.</p>
            <p style={{ margin: '12px 0 0' }}><button type="submit" className="btn" disabled={busy}>Save budget</button></p>
          </form>
        </Fold>
      ) : <p className="help">The year adds up each month's budget. Open a month to change it.</p>}
      <div className="kv">
        <div><b><CountUp value={r.payingClients} /></b><span>client{r.payingClients === 1 ? '' : 's'} paid</span></div>
        <div><b>{r.payingClients === 0 ? '–' : money(Math.round((r.byPayer.filter((x) => x.clientId != null).reduce((s, x) => s + x.amount, 0) / r.payingClients) * 100) / 100)}</b><span>average per paying client</span></div>
      </div>
    </section>
  )
}
