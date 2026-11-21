import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Permissions } from '@/api/permissions'
import { isApiError } from '@/api/http'
import { useCan } from '@/core/permissions/permissions'
import { Button } from '@/ui/Button'
import { ChipGroup } from '@/ui/ChipGroup'
import { DataTable, type Column } from '@/ui/DataTable'
import { DateRange, type DateRangeValue } from '@/ui/DateRange'
import { formatDateTime, formatMoney, formatNumber, ltrIsolate } from '@/ui/format'
import { Loader } from '@/ui/Loader'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { SearchBox } from '@/ui/SearchBox'
import { Select } from '@/ui/Select'
import { StatTile } from '@/ui/StatTile'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import { ALL_TYPES, useSetChecked, useSetUpWhish, useWalletRows, useWallets, type Wallet, type WalletRow } from './api'
import { MovementModal } from './MovementModal'
import { SettingsModal } from './SettingsModal'
import styles from './wallet.module.css'

const PAGE_SIZES = [25, 50, 100]

/** `+$ 12.00` / `-$ 12.00`, the sign kept beside the number in Arabic text. */
function signed(amount: number, notation: string): string {
  const text = formatMoney(Math.abs(amount), notation)
  return ltrIsolate(`${amount < 0 ? '-' : '+'}${text}`)
}

/** `/app/wallet`: the shop's Whish Money account. Set it up, see the balance, record money moved by hand, tick rows off against the Whish app. */
export default function WalletPage() {
  const { t } = useTranslation('wallet')
  const wallets = useWallets()

  if (wallets.isPending) return <Loader fullSection label={t('loading')} />
  if (wallets.isError) {
    return (
      <div className={styles.page}>
        <div className={styles.card} role="alert">
          {t('loadFailed')}
          <Button onClick={() => void wallets.refetch()}>{t('retry')}</Button>
        </div>
      </div>
    )
  }

  const whish = wallets.data.find((w) => w.provider === 'Whish')
  return whish ? <WalletView wallet={whish} /> : <SetUp />
}

function SetUp() {
  const { t } = useTranslation('wallet')
  const canManage = useCan(Permissions.PaymentMethods_Manage)
  const setUp = useSetUpWhish()
  const [number, setNumber] = useState('')

  const turnOn = async () => {
    try {
      await setUp.mutateAsync({ accountNumber: number.trim() || null })
      toast.success(t('setupDone'))
    } catch (error) {
      toast.error(isApiError(error) ? error.message : t('setupFailed'))
    }
  }

  return (
    <div className={styles.page}>
      <PageHeader title={t('title')} />
      <section className={`${styles.card} ${styles.setup}`} aria-labelledby="whish-setup">
        <h2 id="whish-setup">{t('setupTitle')}</h2>
        <p className={styles.muted}>{t('setupBody')}</p>
        {canManage ? (
          <form
            className={styles.setupForm}
            onSubmit={(event) => {
              event.preventDefault()
              void turnOn()
            }}
          >
            <label className={styles.fieldGroup}>
              <span className={styles.fieldLabel}>{t('setupNumber')}</span>
              <input className={styles.input} type="tel" dir="ltr" maxLength={50} value={number} onChange={(e) => setNumber(e.target.value)} placeholder="70 123 456" />
              <span className={styles.hint}>{t('setupNumberHint')}</span>
            </label>
            <Button type="submit" variant="primary" size="lg" loading={setUp.isPending}>
              {t('setupButton')}
            </Button>
          </form>
        ) : (
          <p className={styles.tip}>{t('setupAskOwner')}</p>
        )}
      </section>
    </div>
  )
}

function WalletView({ wallet }: { wallet: Wallet }) {
  const { t } = useTranslation('wallet')
  const canManage = useCan(Permissions.PaymentMethods_Manage)
  const [range, setRange] = useState<DateRangeValue>({ from: '', to: '' })
  const [status, setStatus] = useState('')
  const [type, setType] = useState('')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [recording, setRecording] = useState(false)
  const [editing, setEditing] = useState(false)
  const rows = useWalletRows(wallet.id, { page, pageSize, from: range.from, to: range.to, status, type, q })
  const setChecked = useSetChecked()

  const resetPage = <T,>(set: (v: T) => void) => (value: T) => {
    set(value)
    setPage(1)
  }

  const tick = async (ids: number[], checked: boolean) => {
    if (ids.length === 0) return
    try {
      const result = await setChecked.mutateAsync({ id: wallet.id, ids, checked })
      if (checked) toast.success(t('ticked', { count: result.changed }))
    } catch {
      toast.error(t('tickFailed'))
    }
  }

  const items = rows.data?.items ?? []
  const shownUnchecked = items.filter((r) => !r.isChecked).map((r) => r.id)
  const main = wallet.balances[0]
  const typeOptions = [{ value: '', label: t('anyType') }, ...ALL_TYPES.map((value) => ({ value, label: t(`type_${value}`) }))]

  const columns: Column<WalletRow>[] = [
    { key: 'date', header: t('colDate'), required: true, cell: (r) => formatDateTime(r.occurredAt) },
    { key: 'type', header: t('colType'), cell: (r) => t(`type_${r.type}`) },
    {
      key: 'amount',
      header: t('colAmount'),
      align: 'end',
      required: true,
      cell: (r) => <span className={r.amount < 0 ? styles.out : styles.in}>{signed(r.amount, r.currencyNotation)}</span>,
    },
    { key: 'reference', header: t('colReference'), mono: true, cell: (r) => (r.reference ? <bdi dir="ltr">{r.reference}</bdi> : '-') },
    { key: 'receipt', header: t('colReceipt'), hideBelow: 900, cell: (r) => r.receiptNumber ?? '-' },
    { key: 'notes', header: t('colNotes'), hideBelow: 1100, cell: (r) => (r.notes ? <bdi>{r.notes}</bdi> : '-') },
    { key: 'by', header: t('colBy'), hideBelow: 1200, cell: (r) => <bdi>{r.userName}</bdi> },
    {
      key: 'checked',
      header: t('colChecked'),
      align: 'center',
      cell: (r) => <StatusBadge tone={r.isChecked ? 'success' : 'warning'} label={r.isChecked ? t('checked') : t('unchecked')} />,
    },
  ]

  return (
    <div className={styles.page}>
      <PageHeader title={wallet.name} primaryActionLabel={canManage ? t('record') : undefined} onPrimaryAction={canManage ? () => setRecording(true) : undefined}>
        {canManage && (
          <Button variant="ghost" onClick={() => setEditing(true)}>
            {t('settings')}
          </Button>
        )}
      </PageHeader>

      {!wallet.isActive && (
        <p className={styles.notice} role="status">
          {t('offNotice')}
        </p>
      )}

      <section className={styles.card} aria-label={t('title')}>
        <dl className={styles.facts}>
          <div>
            <dt>{t('number')}</dt>
            <dd>{wallet.accountNumber ? <bdi dir="ltr">{wallet.accountNumber}</bdi> : t('noNumber')}</dd>
          </div>
          <div>
            <dt>{t('paymentMethod')}</dt>
            <dd>
              <bdi>{wallet.paymentMethodName}</bdi>
            </dd>
          </div>
          <div>
            <dt>{t('status')}</dt>
            <dd>
              <StatusBadge tone={wallet.isActive ? 'success' : 'neutral'} label={wallet.isActive ? t('on') : t('off')} />
            </dd>
          </div>
        </dl>
        <p className={styles.tip}>{t('howTo')}</p>
      </section>

      <div className={styles.tiles}>
        {wallet.balances.map((b) => (
          <StatTile
            key={b.currencyId}
            label={t('balance', { code: b.currencyCode })}
            value={formatMoney(b.balance, b.currencyNotation)}
            tone={b.balance < 0 ? 'danger' : 'success'}
          />
        ))}
        {main && (
          <StatTile
            label={t('toCheck')}
            value={formatNumber(wallet.balances.reduce((n, b) => n + b.uncheckedCount, 0), 0)}
            sub={
              wallet.balances
                .filter((b) => b.uncheckedCount > 0)
                .map((b) => signed(b.uncheckedAmount, b.currencyNotation))
                .join('  ') || undefined
            }
            tone="info"
          />
        )}
      </div>

      <DataTable<WalletRow>
        caption={t('rows')}
        columns={columns}
        rows={items}
        rowKey={(r) => r.id}
        loading={rows.isPending}
        refreshing={rows.isFetching && !rows.isPending}
        error={rows.isError ? t('loadFailed') : null}
        onRetry={() => void rows.refetch()}
        emptyTitle={t('noRows')}
        emptyMessage={t('noRowsBody')}
        toolbar={
          <div className={styles.toolbar}>
            <ChipGroup
              aria-label={t('colChecked')}
              size="sm"
              options={[
                { value: '', label: t('all') },
                { value: 'unchecked', label: t('unchecked') },
                { value: 'checked', label: t('checked') },
              ]}
              value={status}
              onChange={resetPage(setStatus)}
            />
            <div className={styles.typeFilter}>
              <Select<string>
                aria-label={t('colType')}
                options={typeOptions}
                value={typeOptions.find((o) => o.value === type) ?? typeOptions[0]!}
                onChange={(option) => resetPage(setType)(option?.value ?? '')}
              />
            </div>
            <DateRange value={range} onChange={resetPage(setRange)} />
            <SearchBox value={q} onSearch={resetPage(setQ)} placeholder={t('search')} />
            {canManage && shownUnchecked.length > 0 && (
              <Button onClick={() => void tick(shownUnchecked, true)} loading={setChecked.isPending}>
                {t('tickAllShown')}
              </Button>
            )}
          </div>
        }
        rowActionsLabel={t('actions')}
        rowActions={
          canManage
            ? (r) => (
                <Button variant={r.isChecked ? 'ghost' : 'secondary'} onClick={() => void tick([r.id], !r.isChecked)} disabled={setChecked.isPending}>
                  {r.isChecked ? t('untick') : t('tick')}
                </Button>
              )
            : undefined
        }
        footer={
          <Pagination
            page={page}
            pageSize={pageSize}
            total={rows.data?.total ?? 0}
            pageSizes={PAGE_SIZES}
            onPageChange={setPage}
            onPageSizeChange={(size) => {
              setPageSize(size)
              setPage(1)
            }}
          />
        }
      />

      {recording && <MovementModal wallet={wallet} onClose={() => setRecording(false)} />}
      {editing && <SettingsModal wallet={wallet} onClose={() => setEditing(false)} />}
    </div>
  )
}
