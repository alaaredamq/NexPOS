import { useEffect, useMemo, useState } from 'react'
import { FormProvider, useForm } from 'react-hook-form'
import { I18nextProvider } from 'react-i18next'
import { Button } from '../Button'
import { ChipGroup, MultiChipGroup } from '../ChipGroup'
import { confirm } from '../confirm'
import { openContextMenu } from '../ContextMenu'
import { DataTable, type Column, type SortState } from '../DataTable'
import { DateRange, type DateRangeValue } from '../DateRange'
import { MoneyField } from '../form/MoneyField'
import { NumberField } from '../form/NumberField'
import { DateField } from '../form/DateField'
import { FormError } from '../form/FormError'
import { SelectField } from '../form/SelectField'
import { SwitchField } from '../form/SwitchField'
import { TextField } from '../form/TextField'
import { applyServerErrors } from '../form/serverErrors'
import { formatDate, formatMoney, formatMoneyCompact, formatQty, toDateInputValue } from '../format'
import { Icon, ICON_PATHS, type IconName } from '../Icon'
import { IconButton } from '../IconButton'
import { Loader } from '../Loader'
import { ManagerPinModal } from '../ManagerPinModal'
import { Modal, ModalBody, ModalFooter } from '../Modal'
import { PageHeader } from '../PageHeader'
import { Pagination } from '../Pagination'
import { PinPad } from '../PinPad'
import { ScanInput } from '../ScanInput'
import { SearchBox } from '../SearchBox'
import { Select, type SelectOption } from '../Select'
import { Skeleton } from '../Skeleton'
import { StatTile } from '../StatTile'
import { StatusBadge } from '../StatusBadge'
import { Stepper } from '../Stepper'
import { toast } from '../toast'
import { UiHost } from '../UiHost'
import { applyDocumentLang, getKitI18n, type KitLang } from './kitI18n'
import styles from './KitPage.module.css'

interface DemoRow {
  id: number
  sku: string
  name: string
  stock: number
  price: number
  status: 'active' | 'low' | 'out'
}

const ROWS: DemoRow[] = Array.from({ length: 57 }, (_, i) => ({
  id: i + 1,
  sku: `SKU-${String(1000 + i * 7)}`,
  name: ['Espresso beans 1kg', 'حليب كامل الدسم', 'USB-C cable 2m', 'علبة مناديل', 'Sparkling water 500ml'][i % 5]!,
  stock: (i * 13) % 40,
  price: 1.25 + i * 0.75,
  status: (i * 13) % 40 === 0 ? 'out' : (i * 13) % 40 < 10 ? 'low' : 'active',
}))

const CUSTOMERS: SelectOption<number>[] = [
  { value: 1, label: 'Walk-in customer' },
  { value: 2, label: 'Hassan Khalil', hint: '+961 3 123 456' },
  { value: 3, label: 'سلمى حداد', hint: '+961 70 987 654' },
  { value: 4, label: 'Maya Trading Co.', hint: 'Debt: USD 120.00' },
]

const loadCustomers = (q: string) =>
  new Promise<SelectOption<number>[]>((resolve) =>
    setTimeout(() => resolve(CUSTOMERS.filter((c) => `${c.label} ${c.hint ?? ''}`.toLowerCase().includes(q.toLowerCase()))), 350),
  )

interface DemoForm {
  name: string
  notes: string
  price: number | null
  qty: number | null
  customer: number | null
  active: boolean
  expiry: string
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className={styles.section}>
      <h2 className={styles.h2}>{title}</h2>
      <div className={styles.body}>{children}</div>
    </section>
  )
}

function KitContent({ lang, onLang }: { lang: KitLang; onLang: (l: KitLang) => void }) {
  const [railTheme, setRailTheme] = useState<'dark' | 'light'>('dark')
  const [chip, setChip] = useState('all')
  const [multi, setMulti] = useState<string[]>(['a'])
  const [qty, setQty] = useState(2)
  const [pin, setPin] = useState('')
  const [pinModal, setPinModal] = useState(false)
  const [modal, setModal] = useState(false)
  const [scan, setScan] = useState('')
  const [scanError, setScanError] = useState('')
  const [select, setSelect] = useState<SelectOption<number> | null>(null)
  const [range, setRange] = useState<DateRangeValue>(() => ({ from: '', to: toDateInputValue(new Date()) }))
  const [loadingBtn, setLoadingBtn] = useState(false)

  // Table state, as a real screen would hold it (page + sort + search → API query).
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [sort, setSort] = useState<SortState | null>({ key: 'name', dir: 'asc' })
  const [tableMode, setTableMode] = useState<'data' | 'loading' | 'empty' | 'error'>('data')

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    const list = q ? ROWS.filter((r) => `${r.sku} ${r.name}`.toLowerCase().includes(q)) : ROWS
    if (!sort) return list
    const dir = sort.dir === 'asc' ? 1 : -1
    return [...list].sort((a, b) => {
      const av = a[sort.key as keyof DemoRow]
      const bv = b[sort.key as keyof DemoRow]
      return (av < bv ? -1 : av > bv ? 1 : 0) * dir
    })
  }, [query, sort])
  const pageRows = tableMode === 'empty' ? [] : filtered.slice((page - 1) * pageSize, page * pageSize)

  const columns: Column<DemoRow>[] = [
    { key: 'sku', header: 'SKU', cell: (r) => r.sku, sortable: true, mono: true, hideBelow: 900 },
    { key: 'name', header: lang === 'ar' ? 'الاسم' : 'Name', cell: (r) => r.name, sortable: true, required: true },
    { key: 'stock', header: lang === 'ar' ? 'المخزون' : 'Stock', cell: (r) => formatQty(r.stock), sortable: true, align: 'end' },
    { key: 'price', header: lang === 'ar' ? 'السعر' : 'Price', cell: (r) => formatMoney(r.price, 'USD'), sortable: true, align: 'end' },
    {
      key: 'status',
      header: lang === 'ar' ? 'الحالة' : 'Status',
      cell: (r) => <StatusBadge label={r.status} tone={r.status === 'active' ? 'success' : r.status === 'low' ? 'warning' : 'danger'} />,
    },
  ]

  const form = useForm<DemoForm>({
    defaultValues: { name: '', notes: '', price: null, qty: null, customer: null, active: true, expiry: '' },
  })
  const [formErrors, setFormErrors] = useState<string[]>([])
  const submit = form.handleSubmit(() => {
    // Simulate an API validation failure so the inline-error path is visible.
    const unmatched = applyServerErrors(form, {
      errors: { Name: ['Name already exists'], Price: ['Price must be above cost'], Discount: ['Unknown field error'] },
    })
    setFormErrors(unmatched)
  })

  return (
    <div className={styles.page}>
      <header className={styles.top}>
        <h1 className={styles.h1}>Coduo POS UI kit</h1>
        <div className={styles.toggles}>
          <ChipGroup
            aria-label="Language"
            size="sm"
            value={lang}
            onChange={(v) => onLang(v as KitLang)}
            options={[
              { value: 'en', label: 'EN' },
              { value: 'ar', label: 'عربي' },
            ]}
          />
          <ChipGroup
            aria-label="Rail theme"
            size="sm"
            value={railTheme}
            onChange={(v) => setRailTheme(v as 'dark' | 'light')}
            options={[
              { value: 'dark', label: 'Dark rail' },
              { value: 'light', label: 'Light rail' },
            ]}
          />
        </div>
      </header>

      <Section title="Rail (nav tokens)">
        <div className={styles.rail} data-theme={railTheme}>
          <span className={styles.railItem} data-active>Dashboard</span>
          <span className={styles.railItem}>Orders</span>
          <span className={styles.railItem}>Products</span>
          <IconButton icon="more" label="More" size="sm" />
        </div>
      </Section>

      <Section title="Buttons">
        <Button variant="primary">Primary</Button>
        <Button>Secondary</Button>
        <Button variant="ghost">Ghost</Button>
        <Button variant="danger">Danger</Button>
        <Button variant="primary" disabled>Disabled</Button>
        <Button
          variant="primary"
          loading={loadingBtn}
          onClick={() => {
            setLoadingBtn(true)
            setTimeout(() => setLoadingBtn(false), 1500)
          }}
        >
          Loading…
        </Button>
        <Button variant="primary" size="lg">Large (POS)</Button>
      </Section>

      <Section title="Icons / IconButton">
        {(Object.keys(ICON_PATHS) as IconName[]).map((n) => (
          <span key={n} className={styles.icon} title={n}>
            <Icon name={n} size={20} />
          </span>
        ))}
        <IconButton icon="edit" label="Edit" />
        <IconButton icon="print" label="Print" tone="primary" />
        <IconButton icon="trash" label="Delete" tone="danger" />
        <IconButton icon="trash" label="Delete (armed)" tone="danger" active />
        <IconButton icon="edit" label="Edit (disabled)" disabled />
      </Section>

      <Section title="PageHeader / StatTile / StatusBadge / Loader / Skeleton">
        <div className={styles.full}>
          <PageHeader title="Products" count="57" primaryActionLabel="Add product" onPrimaryAction={() => toast.info('Add product')}>
            <Button>Import</Button>
          </PageHeader>
        </div>
        <div className={styles.tiles}>
          <StatTile label="Sales today" value={formatMoney(1284.5, 'USD')} sub="+12% vs yesterday" tone="success" />
          <StatTile label="Debt outstanding" value={formatMoney(-320, 'USD')} tone="danger" />
          <StatTile label="Compact" value={formatMoneyCompact(1_250_000, 'LBP')} tone="info" />
          <StatTile label="Items" value={formatQty(1234.5)} />
        </div>
        <StatusBadge label="Paid" tone="success" />
        <StatusBadge label="Pending" tone="warning" />
        <StatusBadge label="Voided" tone="danger" filled />
        <StatusBadge label="Draft" />
        <StatusBadge label="Info" tone="info" />
        <StatusBadge label="Admin" tone="ink" />
        <Loader size="sm" />
        <Loader />
        <Loader size="lg" label="Loading" />
        <div className={styles.skeletons}>
          <Skeleton width="60%" />
          <Skeleton width="40%" />
          <Skeleton circle width={32} height={32} />
        </div>
      </Section>

      <Section title="Chips / Stepper / ScanInput">
        <ChipGroup
          value={chip}
          onChange={setChip}
          options={[
            { value: 'all', label: 'All' },
            { value: 'drinks', label: 'Drinks' },
            { value: 'snacks', label: 'Snacks' },
          ]}
        />
        <MultiChipGroup
          value={multi}
          onChange={setMulti}
          options={[
            { value: 'a', label: 'In stock' },
            { value: 'b', label: 'Low' },
            { value: 'c', label: 'Out' },
          ]}
        />
        <Stepper qty={qty} onDec={() => setQty((q) => Math.max(1, q - 1))} onInc={() => setQty((q) => q + 1)} decDisabled={qty <= 1} />
        <div className={styles.full}>
          <ScanInput
            size="lg"
            value={scan}
            onChange={(v) => {
              setScan(v)
              setScanError('')
            }}
            onEnter={() => setScanError(scan ? 'Not found' : '')}
            placeholder="Scan barcode or type to search"
            hint="Press Enter after typing a barcode"
            error={scanError}
          />
        </div>
      </Section>

      <Section title="Overlays: Modal, confirm(), toast, PIN, context menu">
        <Button onClick={() => setModal(true)}>Open modal</Button>
        <Button
          onClick={async () => {
            const ok = await confirm({
              title: 'Delete product',
              message: 'Delete “Espresso beans 1kg”?',
              detail: 'Sales history is not affected.',
              confirmLabel: 'Delete',
              danger: true,
              onConfirm: () => new Promise((r) => setTimeout(r, 700)),
            })
            toast[ok ? 'success' : 'info'](ok ? 'Deleted' : 'Cancelled')
          }}
        >
          confirm() danger
        </Button>
        <Button
          onClick={async () => {
            const ok = await confirm({
              title: 'Failing action',
              message: 'This one throws so the error stays inside the dialog.',
              confirmLabel: 'Run',
              onConfirm: () => Promise.reject(new Error('Server said no (drawer_not_open)')),
            })
            toast.info(String(ok))
          }}
        >
          confirm() fails
        </Button>
        <Button onClick={() => toast.success('Saved')}>Toast success</Button>
        <Button onClick={() => toast.error('Cannot sell: licence stopped (license_sales_stopped)')}>Toast error</Button>
        <Button onClick={() => toast.warning('Drawer has not been opened today')}>Toast warning</Button>
        <Button onClick={() => toast.info('3 new notifications')}>Toast info</Button>
        <Button onClick={() => setPinModal(true)}>Manager PIN (use 1234)</Button>
        <div
          className={styles.ctx}
          onContextMenu={(e) =>
            openContextMenu(e, [
              { key: 'open', label: 'Open', onSelect: () => toast.info('Open') },
              { key: 'sep', type: 'separator' },
              { key: 'del', label: 'Delete', danger: true, onSelect: () => toast.info('Delete') },
              { key: 'dis', label: 'Disabled', disabled: true, onSelect: () => {} },
            ])
          }
        >
          Right-click me
        </div>
        <div className={styles.pad}>
          <PinPad
            pin={pin}
            keyboard={false}
            onKey={(k) => setPin((p) => (k === 'C' ? '' : k === '⌫' ? p.slice(0, -1) : p.length < 4 ? p + k : p))}
          />
        </div>
      </Section>

      <Section title="Select / DateRange">
        <div className={styles.field}>
          <Select<number>
            aria-label="Customer"
            value={select}
            onChange={setSelect}
            loadOptions={loadCustomers}
            placeholder="Search customers (async)…"
            clearable
          />
        </div>
        <div className={styles.field}>
          <Select aria-label="Static" value={null} onChange={() => {}} options={[{ value: 'a', label: 'Cash' }, { value: 'b', label: 'Card' }, { value: 'c', label: 'On account' }]} placeholder="Static options" />
        </div>
        <div className={styles.field}>
          <Select aria-label="Disabled" value={null} onChange={() => {}} options={[]} placeholder="Disabled" disabled />
        </div>
        <DateRange value={range} onChange={setRange} />
        <span className={styles.note}>{range.from ? formatDate(range.from) : '…'} → {range.to ? formatDate(range.to) : '…'}</span>
      </Section>

      <Section title="Form fields (react-hook-form + server errors)">
        <FormProvider {...form}>
          <form className={styles.form} onSubmit={submit} noValidate>
            <TextField<DemoForm> name="name" label="Name" required placeholder="Product name" />
            <MoneyField<DemoForm> name="price" label="Price" currency="USD" required hint="Entered in the major unit" />
            <NumberField<DemoForm> name="qty" label="Quantity" unit="kg" min={0} />
            <SelectField<DemoForm, number> name="customer" label="Customer" loadOptions={loadCustomers} clearable placeholder="Search…" />
            <DateField<DemoForm> name="expiry" label="Expiry" />
            <TextField<DemoForm> name="notes" label="Notes" multiline />
            <SwitchField<DemoForm> name="active" label="Active" hint="Inactive products are hidden from the POS" />
            <FormError messages={formErrors} />
            <div className={styles.row}>
              <Button type="submit" variant="primary">Save (shows server errors)</Button>
              <Button onClick={() => { form.reset(); setFormErrors([]) }}>Reset</Button>
            </div>
          </form>
        </FormProvider>
      </Section>

      <Section title="DataTable + SearchBox + Pagination">
        <div className={styles.full}>
          <ChipGroup
            aria-label="Table state"
            size="sm"
            value={tableMode}
            onChange={(v) => setTableMode(v as typeof tableMode)}
            options={[
              { value: 'data', label: 'Data' },
              { value: 'loading', label: 'Loading' },
              { value: 'empty', label: 'Empty' },
              { value: 'error', label: 'Error' },
            ]}
          />
          <DataTable<DemoRow>
            columns={columns}
            rows={tableMode === 'loading' || tableMode === 'error' ? [] : pageRows}
            rowKey={(r) => r.id}
            caption="Demo products"
            loading={tableMode === 'loading'}
            error={tableMode === 'error' ? 'Could not load products (HTTP 500)' : null}
            onRetry={() => setTableMode('data')}
            emptyTitle="No products found"
            emptyMessage="Try a different search, or add your first product."
            emptyAction={<Button variant="primary" onClick={() => toast.info('Add')}>Add product</Button>}
            sort={sort}
            onSortChange={(s) => { setSort(s); setPage(1) }}
            columnChooserKey="kit.table.columns"
            toolbar={<SearchBox value={query} onSearch={(q) => { setQuery(q); setPage(1) }} placeholder="Search products…" />}
            rowActions={(r) => (
              <>
                <IconButton icon="edit" label={`Edit ${r.name}`} size="sm" onClick={() => toast.info(`Edit ${r.id}`)} />
                <IconButton icon="trash" label={`Delete ${r.name}`} size="sm" tone="danger" onClick={() => toast.info(`Delete ${r.id}`)} />
              </>
            )}
            onRowClick={(r) => toast.info(`Row ${r.id}`)}
            rowMenu={(r) => [{ key: 'o', label: `Open ${r.sku}`, onSelect: () => toast.info(r.sku) }]}
            footer={<Pagination page={page} pageSize={pageSize} total={tableMode === 'empty' ? 0 : filtered.length} onPageChange={setPage} onPageSizeChange={(s) => { setPageSize(s); setPage(1) }} />}
          />
        </div>
      </Section>

      {modal && (
        <Modal title={lang === 'ar' ? 'مثال نافذة' : 'Example modal'} onClose={() => setModal(false)} width={520}>
          <ModalBody>
            <FormProvider {...form}>
              <TextField<DemoForm> name="name" label="Name" autoFocus />
              <SelectField<DemoForm, number> name="customer" label="Customer (popup is not clipped)" options={CUSTOMERS} />
            </FormProvider>
          </ModalBody>
          <ModalFooter>
            <Button variant="ghost" onClick={() => setModal(false)}>Cancel</Button>
            <Button variant="primary" onClick={() => setModal(false)}>Save</Button>
          </ModalFooter>
        </Modal>
      )}

      {pinModal && (
        <ManagerPinModal
          action={lang === 'ar' ? 'إلغاء الطلب' : 'void this order'}
          verify={(p) => new Promise((r) => setTimeout(() => r(p === '1234'), 250))}
          onSuccess={() => {
            setPinModal(false)
            toast.success('PIN accepted')
          }}
          onCancel={() => setPinModal(false)}
        />
      )}
    </div>
  )
}

/** Dev-only component gallery at `/app/_kit`: every `ui/` component in EN/AR, LTR/RTL, dark/light rail. */
export default function KitPage() {
  const [lang, setLang] = useState<KitLang>(() => (document.documentElement.lang === 'ar' ? 'ar' : 'en'))
  const i18n = getKitI18n(lang)

  useEffect(() => {
    applyDocumentLang(lang)
    void i18n.changeLanguage(lang)
    return () => applyDocumentLang('en')
  }, [lang, i18n])

  return (
    <I18nextProvider i18n={i18n}>
      <KitContent lang={lang} onLang={setLang} />
      <UiHost />
    </I18nextProvider>
  )
}
