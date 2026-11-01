import { describe, expect, it } from 'vitest'
import { emptyProductForm, productFormSchema, toSaveRequest, zodResolver, type ProductFormMessages, type ProductFormValues } from './form'
import { readListParams, writeListParams } from './listParams'
import {
  buildBarcodeBars,
  buildStickerHtml,
  clamp,
  clampCopies,
  clampHeight,
  clampWidth,
  escapeHtml,
  formatStickerPrice,
  getShowPrice,
  setShowPrice,
} from './labels/stickerLabel'

const m: ProductFormMessages = {
  nameRequired: 'name required',
  nameLength: 'name length',
  barcodeRequired: 'barcode required',
  categoryRequired: 'category required',
  supplierRequired: 'supplier required',
  purchasePrice: 'purchase > 0',
  sellingPrice: 'selling > 0',
  sellingAboveCost: 'selling above cost',
  packSize: 'pack size',
  singleProduct: 'single product',
  tierName: 'tier name',
  tierAmount: 'tier amount',
  tierBelowCost: 'tier below cost',
}

const valid = (over: Partial<ProductFormValues> = {}): ProductFormValues => ({
  ...emptyProductForm(undefined),
  name: 'Cable',
  barcode: '123',
  categoryId: 1,
  supplierId: 1,
  purchasePrice: 4,
  sellingPrice: 6,
  ...over,
})

const errorsOf = (values: ProductFormValues, mode: 'create' | 'edit' = 'create') => {
  const result = zodResolver(productFormSchema(m, mode))(values, undefined, {} as never) as { errors: Record<string, unknown> }
  return result.errors
}

describe('product form rules', () => {
  it('accepts a complete product', () => {
    expect(errorsOf(valid())).toEqual({})
  })

  it('requires name, barcode, category, supplier and positive prices', () => {
    const e = errorsOf(valid({ name: ' ', barcode: '', categoryId: null, supplierId: null, purchasePrice: null, sellingPrice: null }))
    expect(Object.keys(e).sort()).toEqual(['barcode', 'categoryId', 'name', 'purchasePrice', 'sellingPrice', 'supplierId'])
  })

  it('separates a missing name from a one-letter name', () => {
    expect((errorsOf(valid({ name: '' })).name as { message: string }).message).toBe('name required')
    expect((errorsOf(valid({ name: 'x' })).name as { message: string }).message).toBe('name length')
  })

  it('refuses to sell at or below cost, like the old page', () => {
    expect((errorsOf(valid({ purchasePrice: 5, sellingPrice: 5 })).sellingPrice as { message: string }).message).toBe('selling above cost')
    expect(errorsOf(valid({ purchasePrice: 5, sellingPrice: 5.01 }))).toEqual({})
  })

  it('checks extra prices: blank rows pass, a name and an amount are needed, none below cost', () => {
    const e = errorsOf(
      valid({
        purchasePrice: 5,
        sellingPrice: 9,
        priceTiers: [
          { name: '', price: null },
          { name: '', price: 6 },
          { name: 'Bulk', price: null },
          { name: 'Cheap', price: 4.99 },
          { name: 'Exact', price: 5 },
        ],
      }),
    ) as { priceTiers: Record<string, { name?: { message: string }; price?: { message: string } }> }
    expect(e.priceTiers['0']).toBeUndefined()
    expect(e.priceTiers['1']!.name!.message).toBe('tier name')
    expect(e.priceTiers['2']!.price!.message).toBe('tier amount')
    expect(e.priceTiers['3']!.price!.message).toBe('tier below cost')
    expect(e.priceTiers['4']).toBeUndefined()
  })

  it('needs pack size and single product only when creating a pack', () => {
    expect(Object.keys(errorsOf(valid({ isPack: true })))).toEqual(['packSize', 'singleProductId'])
    expect(errorsOf(valid({ isPack: true, packSize: 6, singleProductId: 9 }))).toEqual({})
    expect(errorsOf(valid({ isPack: true }), 'edit')).toEqual({})
    expect(Object.keys(errorsOf(valid({ isPack: true, packSize: 1.5, singleProductId: 9 })))).toEqual(['packSize'])
  })
})

describe('toSaveRequest', () => {
  it('trims, drops blank extra prices and the pack fields of a non-pack', () => {
    const body = toSaveRequest(
      valid({
        name: '  Cable  ',
        description: '   ',
        unit: '',
        packSize: 6,
        singleProductId: 9,
        priceTiers: [
          { name: ' ', price: null },
          { name: ' Wholesale ', price: 5 },
        ],
      }),
    )
    expect(body.name).toBe('Cable')
    expect(body.description).toBeNull()
    expect(body.unit).toBeNull()
    expect(body.isPack).toBe(false)
    expect(body.packSize).toBeNull()
    expect(body.singleProductId).toBeNull()
    expect(body.priceTiers).toEqual([{ name: 'Wholesale', price: 5 }])
  })

  it('sends 0 for "default currency" and keeps the pack fields of a pack', () => {
    const body = toSaveRequest(valid({ currencyId: null, isPack: true, packSize: 6, singleProductId: 9 }))
    expect(body.currencyId).toBe(0)
    expect([body.packSize, body.singleProductId]).toEqual([6, 9])
  })
})

describe('list URL state', () => {
  it('reads defaults from an empty query', () => {
    expect(readListParams(new URLSearchParams())).toEqual({
      q: '',
      page: 1,
      pageSize: 25,
      sort: 'name',
      dir: 'asc',
      categoryId: null,
      status: 'all',
      stock: 'all',
    })
  })

  it('round-trips and ignores junk values', () => {
    const params = readListParams(new URLSearchParams('q=usb&page=3&size=50&sort=stock&dir=desc&category=7&status=active&stock=low'))
    expect(params).toMatchObject({ q: 'usb', page: 3, pageSize: 50, sort: 'stock', dir: 'desc', categoryId: 7, status: 'active', stock: 'low' })
    expect(readListParams(writeListParams(params))).toEqual(params)
    expect(readListParams(new URLSearchParams('page=-2&size=7&sort=hack&dir=up&category=x&status=zzz&stock=zzz'))).toMatchObject({
      page: 1,
      pageSize: 25,
      sort: 'name',
      dir: 'asc',
      categoryId: null,
      status: 'all',
      stock: 'all',
    })
  })

  it('writes only what differs from the defaults', () => {
    expect(writeListParams(readListParams(new URLSearchParams())).toString()).toBe('')
  })
})

describe('sticker label logic', () => {
  it('clamps sizes and copies like the old dialog', () => {
    expect(clampCopies('')).toBe(1)
    expect(clampCopies(0)).toBe(1)
    expect(clampCopies(500)).toBe(100)
    expect(clampCopies('12')).toBe(12)
    expect(clampWidth(5)).toBe(15)
    expect(clampWidth(500)).toBe(150)
    expect(clampWidth(undefined)).toBe(40)
    expect(clampHeight(undefined)).toBe(25)
    expect(clamp('abc', 1, 10, 7)).toBe(7)
  })

  it('formats the price like CurrencyHelper.FormatCurrency', () => {
    expect(formatStickerPrice(1234.5, '$')).toBe('$1,234.50')
    expect(formatStickerPrice(4, 'LBP ')).toBe('LBP 4.00')
    expect(formatStickerPrice(Number.NaN, null)).toBe('$0.00')
  })

  it('escapes the product name in the label HTML', () => {
    expect(escapeHtml('<b>"A" & B</b>')).toBe('&lt;b&gt;&quot;A&quot; &amp; B&lt;/b&gt;')
  })

  it('builds one sticker page per copy, scaled to the label, price optional', () => {
    const data = { name: 'Cable <x>', code: '2261013123452', price: '$4.00' }
    const html = buildStickerHtml(data, '<svg></svg>', 40, 25, 3, true)
    expect(html.match(/<div class="sticker">/g)).toHaveLength(3)
    expect(html).toContain('@page { size: 40mm 25mm; margin: 0; }')
    expect(html).toContain('Cable &lt;x&gt;')
    expect(html).toContain('<div class="price">$4.00</div>')
    expect(buildStickerHtml(data, '<svg></svg>', 40, 25, 1, false)).not.toContain('class="price"')
    // double the label, double the type
    expect(buildStickerHtml(data, '<svg></svg>', 80, 50, 1, true)).toContain('font-size: 5.40mm')
  })

  it('encodes Code 128 bars (start + data + check + stop) and refuses what it cannot encode', async () => {
    const bars = await buildBarcodeBars('2261013123452')
    expect(bars).toMatch(/^[01]+$/)
    expect(bars).toMatch(/^(11010000100|11010010000|11010011100)/) // a Code 128 start pattern (A, B or C)
    expect(bars.endsWith('1100011101011')).toBe(true) // stop pattern
    await expect(buildBarcodeBars('باركود')).rejects.toBeTruthy()
  })

  it('remembers "show price" per computer, on by default', () => {
    localStorage.clear()
    expect(getShowPrice()).toBe(true)
    setShowPrice(false)
    expect(getShowPrice()).toBe(false)
    localStorage.clear()
  })
})
