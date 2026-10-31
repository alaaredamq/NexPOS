import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { useState, type ReactNode } from 'react'
import { FormProvider, useForm } from 'react-hook-form'
import { I18nextProvider } from 'react-i18next'
import { Button } from './Button'
import { confirm } from './confirm'
import { DataTable, type Column, type SortState } from './DataTable'
import { applyServerErrors } from './form/serverErrors'
import { MoneyField } from './form/MoneyField'
import { SelectField } from './form/SelectField'
import { TextField } from './form/TextField'
import { getKitI18n } from './kit/kitI18n'
import { ManagerPinModal } from './ManagerPinModal'
import { Modal } from './Modal'
import { Pagination } from './Pagination'
import { SearchBox } from './SearchBox'
import { Select, type SelectOption } from './Select'
import { toast } from './toast'
import { UiHost } from './UiHost'

const wrap = (ui: ReactNode) => render(<I18nextProvider i18n={getKitI18n('en')}>{ui}<UiHost /></I18nextProvider>)

afterEach(() => {
  toast.clear()
  vi.useRealTimers()
})

describe('Modal', () => {
  it('is a labelled dialog, focuses inside, traps Tab, closes on Escape and restores focus', () => {
    const onClose = vi.fn()
    function Host() {
      const [open, setOpen] = useState(false)
      return (
        <>
          <button onClick={() => setOpen(true)}>open</button>
          {open && (
            <Modal title="Edit" onClose={() => { onClose(); setOpen(false) }}>
              <input aria-label="first" />
              <input aria-label="last" />
            </Modal>
          )}
        </>
      )
    }
    wrap(<Host />)
    const opener = screen.getByText('open')
    opener.focus()
    fireEvent.click(opener)

    const dialog = screen.getByRole('dialog', { name: 'Edit' })
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    expect(dialog.contains(document.activeElement)).toBe(true)

    // Tab from the last focusable wraps to the first, Shift+Tab from first wraps to last.
    const close = within(dialog).getByRole('button', { name: 'Close' })
    const last = within(dialog).getByLabelText('last')
    last.focus()
    fireEvent.keyDown(last, { key: 'Tab' })
    expect(document.activeElement).toBe(close)
    fireEvent.keyDown(close, { key: 'Tab', shiftKey: true })
    expect(document.activeElement).toBe(last)

    fireEvent.keyDown(last, { key: 'Escape' })
    expect(onClose).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(document.activeElement).toBe(opener)
  })
})

describe('confirm() and toast', () => {
  it('resolves true on confirm, false on cancel', async () => {
    wrap(null)
    let result: boolean | undefined
    void confirm({ title: 'Delete', message: 'Sure?', confirmLabel: 'Yes', danger: true }).then((r) => (result = r))
    fireEvent.click(await screen.findByRole('button', { name: 'Yes' }))
    await waitFor(() => expect(result).toBe(true))

    void confirm({ title: 'Delete', message: 'Sure?', confirmLabel: 'Yes' }).then((r) => (result = r))
    fireEvent.click(await screen.findByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(result).toBe(false))
  })

  it('keeps the dialog open and shows the error when onConfirm throws', async () => {
    wrap(null)
    let result: boolean | undefined
    void confirm({ title: 'Run', message: 'Go?', confirmLabel: 'Run', onConfirm: () => Promise.reject(new Error('drawer_not_open')) }).then((r) => (result = r))
    fireEvent.click(await screen.findByRole('button', { name: 'Run' }))
    expect(await screen.findByText('drawer_not_open')).toBeInTheDocument()
    expect(result).toBeUndefined()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('announces toasts; errors use role=alert and are dismissible', () => {
    wrap(null)
    act(() => {
      toast.success('Saved')
      toast.error('Boom')
    })
    expect(screen.getByRole('status')).toHaveTextContent('Saved')
    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent('Boom')
    fireEvent.click(within(alert).getByRole('button', { name: 'Dismiss' }))
    expect(screen.queryByText('Boom')).toBeNull()
  })

  it('auto-dismisses after its duration', () => {
    vi.useFakeTimers()
    wrap(null)
    act(() => {
      toast.info('Temp', 1000)
    })
    expect(screen.getByText('Temp')).toBeInTheDocument()
    act(() => {
      vi.advanceTimersByTime(1100)
    })
    expect(screen.queryByText('Temp')).toBeNull()
  })
})

describe('ManagerPinModal', () => {
  const press = (digits: string) => digits.split('').forEach((d) => fireEvent.click(screen.getByRole('button', { name: d })))

  it('verifies on the last digit and reports the PIN', async () => {
    const onSuccess = vi.fn()
    const verify = vi.fn().mockResolvedValue(true)
    wrap(<ManagerPinModal action="void" verify={verify} onSuccess={onSuccess} onCancel={() => {}} />)
    press('1234')
    await waitFor(() => expect(onSuccess).toHaveBeenCalledWith('1234'))
    expect(verify).toHaveBeenCalledWith('1234')
  })

  it('shows an error and clears when the PIN is wrong', async () => {
    const onSuccess = vi.fn()
    wrap(<ManagerPinModal action="void" verify={() => Promise.resolve(false)} onSuccess={onSuccess} onCancel={() => {}} />)
    press('0000')
    expect(await screen.findByText('Incorrect PIN')).toBeInTheDocument()
    expect(onSuccess).not.toHaveBeenCalled()
  })
})

interface Row {
  id: number
  name: string
  price: number
}
const rows: Row[] = [
  { id: 1, name: 'Beans', price: 9 },
  { id: 2, name: 'Milk', price: 2 },
]
const cols: Column<Row>[] = [
  { key: 'name', header: 'Name', cell: (r) => r.name, sortable: true, required: true },
  { key: 'price', header: 'Price', cell: (r) => r.price, sortable: true, align: 'end' },
]

describe('DataTable', () => {
  it('cycles sort asc → desc → none and exposes aria-sort', () => {
    function Host() {
      const [sort, setSort] = useState<SortState | null>(null)
      return <DataTable columns={cols} rows={rows} rowKey={(r) => r.id} caption="Items" sort={sort} onSortChange={setSort} />
    }
    wrap(<Host />)
    const th = screen.getByRole('columnheader', { name: 'Name' })
    expect(th).toHaveAttribute('aria-sort', 'none')
    fireEvent.click(within(th).getByRole('button'))
    expect(th).toHaveAttribute('aria-sort', 'ascending')
    fireEvent.click(within(th).getByRole('button'))
    expect(th).toHaveAttribute('aria-sort', 'descending')
    fireEvent.click(within(th).getByRole('button'))
    expect(th).toHaveAttribute('aria-sort', 'none')
  })

  it('shows skeletons while loading, then the empty state, and an error with retry', () => {
    const onRetry = vi.fn()
    const { rerender } = wrap(<DataTable columns={cols} rows={[]} rowKey={(r) => r.id} caption="Items" loading />)
    expect(document.querySelector('section')).toHaveAttribute('aria-busy', 'true')
    expect(screen.queryByText('Nothing here yet')).toBeNull()

    rerender(<I18nextProvider i18n={getKitI18n('en')}><DataTable columns={cols} rows={[]} rowKey={(r) => r.id} caption="Items" emptyTitle="No items" /></I18nextProvider>)
    expect(screen.getByText('No items')).toBeInTheDocument()

    rerender(<I18nextProvider i18n={getKitI18n('en')}><DataTable columns={cols} rows={[]} rowKey={(r) => r.id} caption="Items" error="HTTP 500" onRetry={onRetry} /></I18nextProvider>)
    expect(screen.getByRole('alert')).toHaveTextContent('HTTP 500')
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
    expect(onRetry).toHaveBeenCalled()
  })

  it('activates rows by click and Enter, but row actions do not trigger the row', () => {
    const onRow = vi.fn()
    const onEdit = vi.fn()
    wrap(
      <DataTable
        columns={cols}
        rows={rows}
        rowKey={(r) => r.id}
        caption="Items"
        onRowClick={onRow}
        rowActions={(r) => <button onClick={() => onEdit(r.id)}>edit {r.id}</button>}
      />,
    )
    const row = screen.getByText('Beans').closest('tr')!
    fireEvent.click(row)
    fireEvent.keyDown(row, { key: 'Enter' })
    expect(onRow).toHaveBeenCalledTimes(2)
    fireEvent.click(screen.getByText('edit 1'))
    expect(onEdit).toHaveBeenCalledWith(1)
    expect(onRow).toHaveBeenCalledTimes(2)
  })

  it('lets the user hide optional columns and remembers it', () => {
    localStorage.removeItem('test.cols')
    wrap(<DataTable columns={cols} rows={rows} rowKey={(r) => r.id} caption="Items" columnChooserKey="test.cols" />)
    fireEvent.click(screen.getByRole('button', { name: 'Columns' }))
    expect(screen.getByRole('checkbox', { name: 'Name' })).toBeDisabled() // required
    fireEvent.click(screen.getByRole('checkbox', { name: 'Price' }))
    expect(screen.queryByRole('columnheader', { name: 'Price' })).toBeNull()
    expect(JSON.parse(localStorage.getItem('test.cols')!)).toEqual(['price'])
  })
})

describe('Pagination', () => {
  it('reports ranges, moves pages and hides itself when empty', () => {
    const onPage = vi.fn()
    const { container, rerender } = wrap(<Pagination page={2} pageSize={25} total={60} onPageChange={onPage} />)
    expect(screen.getByText('26–50 of 60')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Next page' }))
    expect(onPage).toHaveBeenCalledWith(3)
    expect(screen.getByRole('button', { name: 'Page 2' })).toHaveAttribute('aria-current', 'page')
    rerender(<I18nextProvider i18n={getKitI18n('en')}><Pagination page={1} pageSize={25} total={0} onPageChange={onPage} /></I18nextProvider>)
    expect(container.querySelector('nav')).toBeNull()
  })
})

describe('SearchBox', () => {
  it('debounces typing, searches immediately on Enter and clears on Escape', () => {
    vi.useFakeTimers()
    const onSearch = vi.fn()
    wrap(<SearchBox value="" onSearch={onSearch} placeholder="Find" delay={300} />)
    const input = screen.getByRole('searchbox', { name: 'Find' })
    fireEvent.change(input, { target: { value: 'be' } })
    fireEvent.change(input, { target: { value: 'bean' } })
    expect(onSearch).not.toHaveBeenCalled()
    act(() => {
      vi.advanceTimersByTime(300)
    })
    expect(onSearch).toHaveBeenCalledTimes(1)
    expect(onSearch).toHaveBeenLastCalledWith('bean')

    fireEvent.change(input, { target: { value: 'milk' } })
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(onSearch).toHaveBeenLastCalledWith('milk')

    fireEvent.keyDown(input, { key: 'Escape' })
    expect(onSearch).toHaveBeenLastCalledWith('')
    expect(input).toHaveValue('')
  })
})

describe('Select', () => {
  const options: SelectOption<string>[] = [
    { value: 'cash', label: 'Cash' },
    { value: 'card', label: 'Card' },
    { value: 'acct', label: 'On account' },
  ]

  it('filters, navigates with the keyboard and picks with Enter', () => {
    const onChange = vi.fn()
    wrap(<Select<string> aria-label="Method" value={null} onChange={onChange} options={options} />)
    const box = screen.getByRole('combobox', { name: 'Method' })
    fireEvent.focus(box)
    expect(screen.getAllByRole('option')).toHaveLength(3)
    fireEvent.change(box, { target: { value: 'ca' } })
    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual(['Cash', 'Card'])
    fireEvent.keyDown(box, { key: 'ArrowDown' })
    fireEvent.keyDown(box, { key: 'Enter' })
    expect(onChange).toHaveBeenCalledWith(options[1])
  })

  it('closes on Escape without letting a parent Modal see it', () => {
    const onClose = vi.fn()
    wrap(
      <Modal title="Pick" onClose={onClose}>
        <Select<string> aria-label="Method" value={null} onChange={() => {}} options={options} />
      </Modal>,
    )
    const box = screen.getByRole('combobox', { name: 'Method' })
    fireEvent.focus(box)
    expect(screen.getByRole('listbox')).toBeInTheDocument()
    fireEvent.keyDown(box, { key: 'Escape' })
    expect(screen.queryByRole('listbox')).toBeNull()
    expect(onClose).not.toHaveBeenCalled()
    fireEvent.keyDown(box, { key: 'Escape' }) // second Escape reaches the modal
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('queries loadOptions after a debounce and drops stale responses', async () => {
    const loadOptions = vi.fn(async (q: string) => options.filter((o) => o.label.toLowerCase().includes(q.toLowerCase())))
    wrap(<Select<string> aria-label="Method" value={null} onChange={() => {}} loadOptions={loadOptions} />)
    const box = screen.getByRole('combobox', { name: 'Method' })
    fireEvent.focus(box)
    await waitFor(() => expect(loadOptions).toHaveBeenCalledWith(''))
    fireEvent.change(box, { target: { value: 'acc' } })
    await waitFor(() => expect(screen.getAllByRole('option')).toHaveLength(1))
    expect(screen.getByRole('option', { name: 'On account' })).toBeInTheDocument()
    expect(loadOptions).toHaveBeenLastCalledWith('acc')
  })
})

describe('form fields', () => {
  interface F {
    name: string
    price: number | null
    method: string | null
  }

  function Harness({ onValues, onForm }: { onValues?: (v: F) => void; onForm?: (f: ReturnType<typeof useForm<F>>) => void }) {
    const form = useForm<F>({ defaultValues: { name: '', price: null, method: null } })
    onForm?.(form)
    return (
      <FormProvider {...form}>
        <form onSubmit={form.handleSubmit((v) => onValues?.(v))}>
          <TextField<F> name="name" label="Name" required />
          <MoneyField<F> name="price" label="Price" currency="USD" />
          <SelectField<F, string> name="method" label="Method" options={[{ value: 'cash', label: 'Cash' }]} />
          <Button type="submit">Save</Button>
        </form>
      </FormProvider>
    )
  }

  it('links label, hint and errors to the input via aria', async () => {
    let form!: ReturnType<typeof useForm<F>>
    wrap(<Harness onForm={(f) => (form = f)} />)
    const input = screen.getByLabelText(/Name/)
    expect(input).toHaveAttribute('aria-required', 'true')
    act(() => form.setError('name', { type: 'server', message: 'Name already exists' }))
    const msg = await screen.findByText('Name already exists')
    expect(msg).toHaveAttribute('role', 'alert')
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(input.getAttribute('aria-describedby')).toContain(msg.id)
  })

  it('money: swallows junk, limits decimals, pads and rounds half-even on blur, emits numbers', () => {
    const onValues = vi.fn()
    wrap(<Harness onValues={onValues} />)
    const price = screen.getByLabelText('Price')
    fireEvent.change(price, { target: { value: 'abc' } })
    expect(price).toHaveValue('')
    fireEvent.change(price, { target: { value: '12.345' } }) // 3 decimals refused
    expect(price).toHaveValue('')
    fireEvent.change(price, { target: { value: '1,234.5' } })
    fireEvent.blur(price)
    expect(price).toHaveValue('1234.50')
    fireEvent.change(price, { target: { value: '٣٫٥' } })
    fireEvent.blur(price)
    expect(price).toHaveValue('3.50')
    fireEvent.change(price, { target: { value: '-5' } }) // money is non-negative by default
    expect(price).toHaveValue('3.50')

    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'x' } })
    fireEvent.submit(price.closest('form')!)
    return waitFor(() => expect(onValues).toHaveBeenCalledWith({ name: 'x', price: 3.5, method: null }))
  })

  it('maps API validation errors onto fields and returns the unmatched ones', async () => {
    let form!: ReturnType<typeof useForm<F>>
    wrap(<Harness onForm={(f) => (form = f)} />)
    let unmatched: string[] = []
    act(() => {
      unmatched = applyServerErrors(form, { errors: { Name: ['Name already exists'], Price: ['Too low'], Discount: ['Unknown thing'] } })
    })
    expect(await screen.findByText('Name already exists')).toBeInTheDocument()
    expect(screen.getByText('Too low')).toBeInTheDocument()
    expect(unmatched).toEqual(['Unknown thing'])
  })

  it('select field stores the option value', async () => {
    const onValues = vi.fn()
    wrap(<Harness onValues={onValues} />)
    const box = screen.getByRole('combobox', { name: 'Method' })
    fireEvent.focus(box)
    fireEvent.click(screen.getByRole('option', { name: 'Cash' }))
    expect(box).toHaveValue('Cash')
    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'x' } })
    fireEvent.submit(box.closest('form')!)
    await waitFor(() => expect(onValues).toHaveBeenCalledWith(expect.objectContaining({ method: 'cash' })))
  })
})
