import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { isApiError } from '@/api/http'
import { useSession } from '@/core/auth/useSession'
import { useCan } from '@/core/permissions/permissions'
import { flattenNav } from '@/shell/nav'
import { NotFoundPage } from '@/shell/pages/pages'
import { Button } from '@/ui/Button'
import { confirm } from '@/ui/confirm'
import { DataTable, type Column } from '@/ui/DataTable'
import { formatDate, formatDateTime, formatMoney, formatNumber } from '@/ui/format'
import { Loader } from '@/ui/Loader'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import { LEDGER_PAGE_SIZE, useCustomer, useDebtTransactions, useDeleteCustomer, type CustomerOrder, type DebtTransaction } from './api'
import { Balance } from './Balance'
import { CustomerDebtDialog } from './CustomerDebtDialog'
import { CustomerFormModal } from './CustomerFormModal'
import styles from './customers.module.css'

type Tone = 'success' | 'neutral' | 'warning' | 'danger' | 'info'

const ORDER_TONE: Record<string, Tone> = { Pending: 'warning', Processing: 'info', Shipped: 'info', Delivered: 'success', Cancelled: 'danger', Returned: 'neutral' }
const LEDGER_TONE: Record<string, Tone> = { Sale: 'info', Payment: 'success', SaleReversal: 'warning', ManualAdjustment: 'neutral' }
const LEDGER_LABEL: Record<string, string> = { Sale: 'Cust_TypeSale', Payment: 'Cust_TypePayment', SaleReversal: 'Cust_TypeReversal', ManualAdjustment: 'Cust_TypeAdjustment' }

/**
 * Where one order lives: the SPA page once the Orders module is migrated (`/app/orders/:id`), otherwise the Razor details page
 * (`/en/Orders/Details/:id`). `ordersHref` is the nav entry's current link, so the switch happens by itself when track S1 flips its line.
 */
export function orderHref(ordersHref: string, id: number): string {
  return ordersHref.startsWith('/app/') ? `${ordersHref}/${id}` : `${ordersHref}/Details/${id}`
}

/** `/app/customers/:id` - contact details, balance and debt ledger, order history, summary. */
export default function CustomerDetailsPage() {
  const { t } = useTranslation(['customers', 'common'])
  const navigate = useNavigate()
  const id = Number(useParams().id)
  const canEdit = useCan(Permissions.Customers_Edit)
  const canDelete = useCan(Permissions.Customers_Delete)
  const canSeeOrders = useCan(Permissions.Orders_View)
  const ordersHref = flattenNav(useSession().nav).find((item) => item.key === 'Orders')?.href
  const query = useCustomer(id)
  const remove = useDeleteCustomer()
  const [ledgerPage, setLedgerPage] = useState(1)
  const ledger = useDebtTransactions(id, ledgerPage)
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

  const { customer, currencyNotation: notation } = query.data
  const name = `${customer.firstName} ${customer.lastName}`.trim()
  const owesMe = customer.balance > 0 ? customer.balance : 0
  const iOwe = customer.balance < 0 ? -customer.balance : 0
  const state = customer.balance > 0 ? 'owes-me' : customer.balance < 0 ? 'i-owe' : 'even'
  const place = [customer.address, query.data.city, query.data.country].filter(Boolean).join(', ') + (query.data.postalCode ? ` - ${query.data.postalCode}` : '')

  const askDelete = async () => {
    const deleted = await confirm({
      title: t('Cust_AreYouSure', { ns: 'common' }),
      message: t('AreYouSureYouWantToDeleteThisCustomer', { ns: 'customers' }),
      detail: name,
      confirmLabel: t('Cust_YesDelete', { ns: 'common' }),
      danger: true,
      onConfirm: async () => {
        await remove.mutateAsync(customer.id)
      },
    })
    if (deleted) {
      toast.success(t('CustomerDeleted', { ns: 'customers' }))
      void navigate('/customers', { replace: true })
    }
  }

  const ledgerColumns: Column<DebtTransaction>[] = [
    { key: 'date', header: t('Date', { ns: 'common' }), required: true, cell: (r) => formatDateTime(r.createdAt) },
    {
      key: 'type',
      header: t('Type', { ns: 'common' }),
      cell: (r) => <StatusBadge tone={LEDGER_TONE[r.type] ?? 'neutral'} label={t(LEDGER_LABEL[r.type] ?? 'Cust_TypeAdjustment', { ns: 'common' })} />,
    },
    {
      key: 'note',
      header: t('Cust_Note', { ns: 'common' }),
      hideBelow: 700,
      cell: (r) => (r.note ? <bdi className={styles.clip} title={r.note}>{r.note}</bdi> : r.orderId ? t('Cust_OrderRef', { ns: 'common', 0: r.orderId }) : <span className={styles.muted}>-</span>),
    },
    { key: 'by', header: t('RecordedBy', { ns: 'customers' }), hideBelow: 1000, cell: (r) => (r.recordedBy ? <bdi>{r.recordedBy}</bdi> : <span className={styles.muted}>-</span>) },
    {
      key: 'amount',
      header: t('Amount', { ns: 'common' }),
      align: 'end',
      cell: (r) => (
        <span className={styles.balance} data-direction={r.amount > 0 ? 'owes-me' : 'i-owe'}>
          {r.amount > 0 ? '+' : ''}
          {formatMoney(r.amount, notation)}
        </span>
      ),
    },
  ]

  const orderColumns: Column<CustomerOrder>[] = [
    {
      key: 'id',
      header: t('Cust_OrderNumber', { ns: 'common' }),
      required: true,
      cell: (o) =>
        canSeeOrders && ordersHref ? (
          <a className={styles.name} href={orderHref(ordersHref, o.id)}>
            #{o.id}
          </a>
        ) : (
          <strong>#{o.id}</strong>
        ),
    },
    { key: 'date', header: t('Date', { ns: 'common' }), cell: (o) => formatDateTime(o.createdAt) },
    { key: 'total', header: t('Cust_TotalAmount', { ns: 'common' }), align: 'end', cell: (o) => formatMoney(o.totalAmount, o.currencyNotation) },
    { key: 'status', header: t('Status', { ns: 'common' }), align: 'center', cell: (o) => <StatusBadge tone={ORDER_TONE[o.status] ?? 'neutral'} label={t(`Cust_St_${o.status}`, { ns: 'common', defaultValue: o.status })} /> },
  ]

  return (
    <div className={styles.page}>
      <PageHeader
        title={name}
        primaryActionLabel={canEdit ? t('Cust_EditTitle', { ns: 'common' }) : undefined}
        onPrimaryAction={canEdit ? () => setEditing(true) : undefined}
      >
        <Link className={styles.link} to="/customers">{t('Cust_BackToCustomers', { ns: 'common' })}</Link>
      </PageHeader>

      <div className={styles.details}>
        <div className={styles.column}>
          <section className={styles.card}>
            <h2 className={styles.cardTitle}>{t('Cust_Information', { ns: 'common' })}</h2>
            <dl className={styles.facts}>
              <div>
                <dt>{t('First Name', { ns: 'common' })}</dt>
                <dd><bdi>{customer.firstName}</bdi></dd>
              </div>
              <div>
                <dt>{t('Last Name', { ns: 'common' })}</dt>
                <dd><bdi>{customer.lastName}</bdi></dd>
              </div>
              <div>
                <dt>{t('Email Address', { ns: 'common' })}</dt>
                <dd>{customer.email ? <a className={styles.link} href={`mailto:${customer.email}`}><bdi>{customer.email}</bdi></a> : <span className={styles.muted}>{t('Cust_NotProvided', { ns: 'common' })}</span>}</dd>
              </div>
              <div>
                <dt>{t('Phone Number', { ns: 'common' })}</dt>
                <dd>{customer.phoneNumber ? <a className={styles.link} href={`tel:${customer.phoneNumber}`}><bdi>{customer.phoneNumber}</bdi></a> : <span className={styles.muted}>{t('Cust_NotProvided', { ns: 'common' })}</span>}</dd>
              </div>
              <div className={styles.factWide}>
                <dt>{t('Address', { ns: 'common' })}</dt>
                <dd>{customer.address ? <bdi>{place}</bdi> : <span className={styles.muted}>{t('Cust_NoAddress', { ns: 'common' })}</span>}</dd>
              </div>
              <div>
                <dt>{t('Status', { ns: 'common' })}</dt>
                <dd><StatusBadge tone={customer.isActive ? 'success' : 'danger'} label={customer.isActive ? t('Active', { ns: 'customers' }) : t('Inactive', { ns: 'customers' })} /></dd>
              </div>
              <div>
                <dt>{t('CustomerSince', { ns: 'customers' })}</dt>
                <dd>{formatDate(customer.createdAt)}</dd>
              </div>
            </dl>
          </section>

          <section className={styles.card} aria-labelledby="debt-title">
            <div className={styles.cardHead}>
              <h2 className={styles.cardTitle} id="debt-title">{t('Cust_DebtInformation', { ns: 'common' })}</h2>
              {canEdit && (
                <Button variant="primary" onClick={() => setAddingDebt(true)}>
                  {t('Cust_AddTransaction', { ns: 'common' })}
                </Button>
              )}
            </div>
            <div className={styles.debtPair}>
              <div>
                <div className={styles.debtLabel}>{t('Cust_OwesMe', { ns: 'common' })}</div>
                <div className={styles.debtValue} data-direction="owes-me">{formatMoney(owesMe, notation)}</div>
              </div>
              <div>
                <div className={styles.debtLabel}>{t('Cust_IOwe', { ns: 'common' })}</div>
                <div className={styles.debtValue} data-direction="i-owe">{formatMoney(iOwe, notation)}</div>
              </div>
            </div>
            <div className={styles.netBanner} data-state={state} role="status">
              <div>
                <strong>{t('Cust_NetBalance', { ns: 'common' })}</strong>
                <div>
                  {state === 'owes-me'
                    ? t('Cust_CustomerOwesMe', { ns: 'common', 0: formatMoney(owesMe, notation) })
                    : state === 'i-owe'
                      ? t('Cust_IOweCustomer', { ns: 'common', 0: formatMoney(iOwe, notation) })
                      : t('Cust_BalancedNoDebt', { ns: 'common' })}
                </div>
              </div>
              <StatusBadge
                tone={state === 'owes-me' ? 'success' : state === 'i-owe' ? 'danger' : 'neutral'}
                label={state === 'owes-me' ? t('Cust_Receivable', { ns: 'common' }) : state === 'i-owe' ? t('Cust_Payable', { ns: 'common' }) : t('Cust_Balanced', { ns: 'common' })}
              />
            </div>
            <DataTable<DebtTransaction>
              caption={t('DebtLedger', { ns: 'customers' })}
              columns={ledgerColumns}
              rows={ledger.data?.items ?? []}
              rowKey={(r) => r.id}
              loading={ledger.isPending}
              refreshing={ledger.isFetching && !ledger.isPending}
              error={ledger.isError ? ledger.error.message : null}
              onRetry={() => void ledger.refetch()}
              emptyTitle={t('NoDebtTransactions', { ns: 'customers' })}
              footer={
                (ledger.data?.total ?? 0) > LEDGER_PAGE_SIZE ? (
                  <Pagination page={ledgerPage} pageSize={LEDGER_PAGE_SIZE} total={ledger.data?.total ?? 0} pageSizes={[LEDGER_PAGE_SIZE]} onPageChange={setLedgerPage} />
                ) : undefined
              }
            />
          </section>

          <section className={styles.card} aria-labelledby="orders-title">
            <h2 className={styles.cardTitle} id="orders-title">{t('Cust_OrderHistory', { ns: 'common' })}</h2>
            <DataTable<CustomerOrder>
              caption={t('Cust_OrderHistory', { ns: 'common' })}
              columns={orderColumns}
              rows={query.data.recentOrders}
              rowKey={(o) => o.id}
              emptyTitle={t('Cust_NoOrders', { ns: 'common' })}
            />
            {query.data.allOrdersCount > query.data.recentOrders.length && canSeeOrders && ordersHref && (
              <a className={`${styles.link} ${styles.viewAll}`} href={`${ordersHref}?customerId=${customer.id}`}>
                {t('Cust_ViewAllOrders', { ns: 'common', 0: formatNumber(query.data.allOrdersCount, 0) })}
              </a>
            )}
          </section>
        </div>

        <div className={styles.column}>
          <section className={styles.card}>
            <h2 className={styles.cardTitle}>{t('Cust_Summary', { ns: 'common' })}</h2>
            <div className={styles.avatarBlock}>
              <span className={styles.avatar} aria-hidden="true">
                {(customer.firstName[0] ?? '').toUpperCase()}
                {(customer.lastName[0] ?? '').toUpperCase()}
              </span>
              <strong><bdi>{name}</bdi></strong>
            </div>
            <dl className={styles.summary}>
              <div className={styles.summaryRow}>
                <dt>{t('Cust_TotalOrders', { ns: 'common' })}</dt>
                <dd>{formatNumber(customer.ordersCount, 0)}</dd>
              </div>
              <div className={styles.summaryRow}>
                <dt>{t('Cust_TotalSpent', { ns: 'common' })}</dt>
                <dd>{formatMoney(customer.totalSpent, notation)}</dd>
              </div>
              <div className={styles.summaryRow}>
                <dt>{t('Cust_AverageOrder', { ns: 'common' })}</dt>
                <dd>{formatMoney(query.data.averageOrder, notation)}</dd>
              </div>
              <div className={styles.summaryRow}>
                <dt>{t('Cust_LastOrder', { ns: 'common' })}</dt>
                <dd>{query.data.lastOrderAt ? formatDate(query.data.lastOrderAt) : <span className={styles.muted}>{t('Cust_Never', { ns: 'common' })}</span>}</dd>
              </div>
              <div className={styles.summaryRow}>
                <dt>{t('Cust_NetBalance', { ns: 'common' })}</dt>
                <dd><Balance amount={customer.balance} notation={notation} /></dd>
              </div>
            </dl>
          </section>

          {(canEdit || canDelete || (canSeeOrders && ordersHref) || customer.email) && (
            <section className={styles.card}>
              <h2 className={styles.cardTitle}>{t('QuickActions', { ns: 'common' })}</h2>
              <div className={styles.actions}>
                {canEdit && <Button onClick={() => setEditing(true)}>{t('Cust_EditTitle', { ns: 'common' })}</Button>}
                {canSeeOrders && ordersHref && (
                  <a className={styles.actionLink} href={`${ordersHref}?customerId=${customer.id}`}>
                    {t('Cust_ViewOrders', { ns: 'common' })}
                  </a>
                )}
                {customer.email && (
                  <a className={styles.actionLink} href={`mailto:${customer.email}`}>
                    {t('Cust_SendEmail', { ns: 'common' })}
                  </a>
                )}
                {canDelete && (
                  <Button variant="danger" onClick={() => void askDelete()}>
                    {t('Cust_DeleteCustomer', { ns: 'common' })}
                  </Button>
                )}
              </div>
            </section>
          )}
        </div>
      </div>

      {editing && <CustomerFormModal customer={customer} onClose={() => setEditing(false)} />}
      {addingDebt && <CustomerDebtDialog customer={{ id: customer.id, name, balance: customer.balance }} currencyNotation={notation} onClose={() => setAddingDebt(false)} />}
    </div>
  )
}
