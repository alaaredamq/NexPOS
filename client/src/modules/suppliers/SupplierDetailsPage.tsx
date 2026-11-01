import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { isApiError } from '@/api/http'
import { useCan } from '@/core/permissions/permissions'
import { NotFoundPage } from '@/shell/pages/pages'
import { Button } from '@/ui/Button'
import { confirm } from '@/ui/confirm'
import { DataTable, type Column } from '@/ui/DataTable'
import { formatDateTime, formatMoney, formatNumber, formatPercent } from '@/ui/format'
import { Loader } from '@/ui/Loader'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import { SupplierDebtDialog } from './SupplierDebtDialog'
import { SupplierFormModal } from './SupplierFormModal'
import { balanceSign, useDeleteSupplier, useSupplier, useSupplierLedger, type DebtEntry, type DebtKind } from './api'
import styles from './suppliers.module.css'

const LEDGER_PAGE_SIZE = 20
const KIND_LABEL: Record<DebtKind, string> = { purchase: 'TypePurchase', payment: 'TypePayment', adjustment: 'TypeAdjustment' }
const kindOf = (value: string): DebtKind => (value === 'purchase' || value === 'payment' ? value : 'adjustment')

/** `/app/suppliers/:id` - the supplier, what is owed either way, and the debt ledger. */
export default function SupplierDetailsPage() {
  const { t } = useTranslation(['suppliers', 'common'])
  const id = Number(useParams().id)
  const navigate = useNavigate()
  const canEdit = useCan(Permissions.Suppliers_Edit)
  const canDelete = useCan(Permissions.Suppliers_Delete)
  const query = useSupplier(id)
  const remove = useDeleteSupplier()
  const [page, setPage] = useState(1)
  const ledger = useSupplierLedger(id, page, LEDGER_PAGE_SIZE)
  const [editing, setEditing] = useState(false)
  const [addingDebt, setAddingDebt] = useState(false)

  if (!Number.isInteger(id) || id < 1 || (isApiError(query.error) && query.error.status === 404)) return <NotFoundPage />
  if (query.isPending) return <Loader fullSection label={t('Loading', { ns: 'common' })} />
  if (query.isError) {
    return (
      <div className={styles.page}>
        <div className={styles.card} role="alert">
          {query.error.message}
          <Button onClick={() => void query.refetch()}>{t('common.retry', { ns: 'common' })}</Button>
        </div>
      </div>
    )
  }

  const supplier = query.data
  const sign = balanceSign(supplier.balance)
  const money = (amount: number) => formatMoney(amount, supplier.currencyNotation)
  const notProvided = <span className={styles.muted}>{t('NotProvided')}</span>
  const place = [supplier.address, supplier.city, supplier.country, supplier.postalCode].filter(Boolean).join(', ')

  const askDelete = async () => {
    const deleted = await confirm({
      title: t('ConfirmTitle'),
      message: t('DeleteConfirmText'),
      detail: supplier.name,
      confirmLabel: t('Delete', { ns: 'common' }),
      danger: true,
      onConfirm: async () => {
        await remove.mutateAsync(supplier.id)
        toast.success(t('SupplierDeleted'))
      },
    })
    if (deleted) void navigate('/suppliers', { replace: true })
  }

  const columns: Column<DebtEntry>[] = [
    { key: 'date', header: t('Date'), required: true, cell: (e) => formatDateTime(e.createdAt) },
    {
      key: 'kind',
      header: t('Type'),
      cell: (e) => <StatusBadge tone={kindOf(e.kind) === 'payment' ? 'success' : kindOf(e.kind) === 'purchase' ? 'info' : 'neutral'} label={t(KIND_LABEL[kindOf(e.kind)])} />,
    },
    { key: 'note', header: t('Note'), hideBelow: 700, cell: (e) => (e.note ? <bdi>{e.note}</bdi> : <span className={styles.muted}>-</span>) },
    { key: 'user', header: t('EnteredBy'), hideBelow: 900, cell: (e) => (e.userName ? <bdi>{e.userName}</bdi> : <span className={styles.muted}>-</span>) },
    {
      // A positive amount raises what the shop owes, the "bad" direction (red); a negative one lowers it (green).
      key: 'amount',
      header: t('Amount'),
      align: 'end',
      cell: (e) => (
        <span className={styles.balance} data-sign={e.amount > 0 ? 'owe' : 'owed'}>
          {e.amount > 0 ? '+' : ''}
          {money(e.amount)}
        </span>
      ),
    },
  ]

  return (
    <div className={styles.page}>
      <PageHeader
        title={supplier.name}
        primaryActionLabel={canEdit ? t('EditSupplier') : undefined}
        onPrimaryAction={canEdit ? () => setEditing(true) : undefined}
      >
        {canDelete && (
          <Button variant="danger" onClick={() => void askDelete()}>
            {t('DeleteSupplier')}
          </Button>
        )}
        <Link className={styles.link} to="/suppliers">
          {t('BackToSuppliers')}
        </Link>
      </PageHeader>

      <div className={styles.details}>
        <div className={styles.column}>
          <section className={styles.card}>
            <h2 className={styles.cardTitle}>{t('SupplierInformation')}</h2>
            <dl className={styles.facts}>
              <div>
                <dt>{t('SupplierName')}</dt>
                <dd><bdi>{supplier.name}</bdi></dd>
              </div>
              <div>
                <dt>{t('Status')}</dt>
                <dd><StatusBadge tone={supplier.isActive ? 'success' : 'neutral'} label={supplier.isActive ? t('Active') : t('Inactive')} /></dd>
              </div>
              <div>
                <dt>{t('EmailAddress')}</dt>
                <dd>{supplier.email ? <a className={styles.link} href={`mailto:${supplier.email}`}><bdi>{supplier.email}</bdi></a> : notProvided}</dd>
              </div>
              <div>
                <dt>{t('PhoneNumber')}</dt>
                <dd>{supplier.phone ? <a className={styles.link} href={`tel:${supplier.phone}`}><bdi>{supplier.phone}</bdi></a> : notProvided}</dd>
              </div>
              <div className={styles.factWide}>
                <dt>{t('Address')}</dt>
                <dd>{place ? <bdi>{place}</bdi> : <span className={styles.muted}>{t('NoAddressProvided')}</span>}</dd>
              </div>
              <div>
                <dt>{t('DiscountRate')}</dt>
                <dd>{formatPercent(supplier.discountRate, 2)}</dd>
              </div>
              <div>
                <dt>{t('Currency')}</dt>
                <dd>{supplier.currencyName ? <bdi>{supplier.currencyName}</bdi> : notProvided}</dd>
              </div>
            </dl>
          </section>

          <section className={styles.card}>
            <div className={styles.cardHead}>
              <h2 className={styles.cardTitle}>{t('DebtInformation')}</h2>
              {canEdit && (
                <Button onClick={() => setAddingDebt(true)}>{t('AddTransaction')}</Button>
              )}
            </div>
            <dl className={styles.facts}>
              <div>
                <dt>{t('DebtToSupplier')}</dt>
                <dd><span className={styles.balance} data-sign="owe">{money(Math.max(supplier.balance, 0))}</span></dd>
              </div>
              <div>
                <dt>{t('DebtFromSupplier')}</dt>
                <dd><span className={styles.balance} data-sign="owed">{money(Math.max(-supplier.balance, 0))}</span></dd>
              </div>
            </dl>
            <div className={styles.net} data-sign={sign} role="status">
              <div>
                <div className={styles.cardTitle}>{t('NetDebt')}</div>
                <div className={`${styles.netAmount} ${styles.balance}`} data-sign={sign}>
                  {sign === 'owe' && `${t('YouOwe')}: ${money(supplier.balance)}`}
                  {sign === 'owed' && `${t('TheyOweYouAmount')}: ${money(-supplier.balance)}`}
                  {sign === 'zero' && t('BalancedNoDebt')}
                </div>
              </div>
              <StatusBadge filled tone={sign === 'owe' ? 'danger' : sign === 'owed' ? 'success' : 'neutral'} label={sign === 'owe' ? t('PaymentDue') : sign === 'owed' ? t('Receivable') : t('Balanced')} />
            </div>
            <DataTable<DebtEntry>
              caption={t('DebtTransactions')}
              columns={columns}
              rows={ledger.data?.items ?? []}
              rowKey={(e) => e.id}
              loading={ledger.isPending}
              refreshing={ledger.isFetching && !ledger.isPending}
              error={ledger.isError ? ledger.error.message : null}
              onRetry={() => void ledger.refetch()}
              emptyTitle={t('NoTransactions')}
              footer={
                (ledger.data?.total ?? 0) > LEDGER_PAGE_SIZE ? (
                  <Pagination page={page} pageSize={LEDGER_PAGE_SIZE} total={ledger.data?.total ?? 0} pageSizes={[LEDGER_PAGE_SIZE]} onPageChange={setPage} onPageSizeChange={() => {}} />
                ) : undefined
              }
            />
          </section>
        </div>

        <div className={styles.column}>
          <section className={styles.card}>
            <h2 className={styles.cardTitle}>{t('SupplierSummary')}</h2>
            <dl className={styles.summary}>
              <div className={styles.summaryRow}>
                <dt>{t('Supplier')}</dt>
                <dd>#{supplier.id}</dd>
              </div>
              <div className={styles.summaryRow}>
                <dt>{t('TotalProducts')}</dt>
                <dd>{formatNumber(supplier.productCount, 0)}</dd>
              </div>
              <div className={styles.summaryRow}>
                <dt>{t('DiscountRate')}</dt>
                <dd>{formatPercent(supplier.discountRate, 2)}</dd>
              </div>
              <div className={styles.summaryRow}>
                <dt>{t('NetDebt')}</dt>
                <dd><span className={styles.balance} data-sign={sign}>{money(supplier.balance)}</span></dd>
              </div>
            </dl>
          </section>

          {(supplier.email || supplier.phone) && (
            <section className={styles.card}>
              <h2 className={styles.cardTitle}>{t('QuickActions')}</h2>
              <div className={styles.actions}>
                {supplier.email && (
                  <a className={styles.link} href={`mailto:${supplier.email}`}>
                    {t('SendEmail')}
                  </a>
                )}
                {supplier.phone && (
                  <a className={styles.link} href={`tel:${supplier.phone}`}>
                    {t('CallSupplier')}
                  </a>
                )}
              </div>
            </section>
          )}
        </div>
      </div>

      {editing && <SupplierFormModal supplier={supplier} onClose={() => setEditing(false)} />}
      {addingDebt && <SupplierDebtDialog supplier={supplier} onClose={() => setAddingDebt(false)} />}
    </div>
  )
}
