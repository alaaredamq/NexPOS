import { get, set, type FieldErrors, type FieldValues, type Resolver } from 'react-hook-form'
import { z } from 'zod'
import type { ProductDetail, ProductLookups, ProductSaveRequest } from './api'

/** What the product form edits. Numbers are `null` while the field is empty. */
export interface ProductFormValues {
  name: string
  description: string
  barcode: string
  categoryId: number | null
  supplierId: number | null
  currencyId: number | null
  purchasePrice: number | null
  sellingPrice: number | null
  unit: string | null
  stockQuantity: number | null
  minStockLevel: number | null
  isActive: boolean
  isPack: boolean
  packSize: number | null
  singleProductId: number | null
  priceTiers: { name: string; price: number | null }[]
}

/** The units the old form offered (labels are the `Prod_Unit_*` keys). */
export const UNITS = ['piece', 'kg', 'lb', 'liter', 'gallon', 'box', 'pack'] as const

/** Localized texts of every rule, so the schema itself stays free of i18n (and testable without it). */
export interface ProductFormMessages {
  nameRequired: string
  nameLength: string
  barcodeRequired: string
  categoryRequired: string
  supplierRequired: string
  purchasePrice: string
  sellingPrice: string
  /** The old "selling price must be higher than purchase price" gate. */
  sellingAboveCost: string
  packSize: string
  singleProduct: string
  tierName: string
  tierAmount: string
  tierBelowCost: string
}

const positive = (n: number | null) => n !== null && n > 0

/**
 * The form's rules, mirroring the old page's and the API's (ProductSaveRequest + ProductCatalogService.ValidateAsync).
 * The API stays the real gate (barcode uniqueness only it can know); this just answers before the round trip.
 * `mode === 'edit'` skips the pack rules: a product's pack setting is fixed at creation.
 */
export function productFormSchema(m: ProductFormMessages, mode: 'create' | 'edit') {
  return z
    .object({
      name: z.string(),
      description: z.string().max(1000),
      barcode: z.string(),
      categoryId: z.number().nullable(),
      supplierId: z.number().nullable(),
      currencyId: z.number().nullable(),
      purchasePrice: z.number().nullable(),
      sellingPrice: z.number().nullable(),
      unit: z.string().nullable(),
      stockQuantity: z.number().nullable(),
      minStockLevel: z.number().nullable(),
      isActive: z.boolean(),
      isPack: z.boolean(),
      packSize: z.number().nullable(),
      singleProductId: z.number().nullable(),
      priceTiers: z.array(z.object({ name: z.string(), price: z.number().nullable() })),
    })
    .superRefine((v, ctx) => {
      const fail = (path: (string | number)[], message: string) => ctx.addIssue({ code: 'custom', path, message })

      const name = v.name.trim()
      if (!name) fail(['name'], m.nameRequired)
      else if (name.length < 2 || name.length > 255) fail(['name'], m.nameLength)

      const barcode = v.barcode.trim()
      if (!barcode || barcode.length > 50) fail(['barcode'], m.barcodeRequired)

      if (!positive(v.categoryId)) fail(['categoryId'], m.categoryRequired)
      if (!positive(v.supplierId)) fail(['supplierId'], m.supplierRequired)
      if (!positive(v.purchasePrice)) fail(['purchasePrice'], m.purchasePrice)
      if (!positive(v.sellingPrice)) fail(['sellingPrice'], m.sellingPrice)
      else if (positive(v.purchasePrice) && v.sellingPrice! <= v.purchasePrice!) fail(['sellingPrice'], m.sellingAboveCost)

      if (mode === 'create' && v.isPack) {
        if (!positive(v.packSize) || !Number.isInteger(v.packSize)) fail(['packSize'], m.packSize)
        if (!positive(v.singleProductId)) fail(['singleProductId'], m.singleProduct)
      }

      // Extra prices: a fully blank row is ignored; otherwise a name, an amount, and never below the purchase price.
      v.priceTiers.forEach((tier, i) => {
        const blank = !tier.name.trim() && !tier.price
        if (blank) return
        if (!tier.name.trim()) fail(['priceTiers', i, 'name'], m.tierName)
        else if (!positive(tier.price)) fail(['priceTiers', i, 'price'], m.tierAmount)
        else if (positive(v.purchasePrice) && tier.price! < v.purchasePrice!) fail(['priceTiers', i, 'price'], m.tierBelowCost)
      })
    })
}

/**
 * A react-hook-form resolver for a zod schema (the project has no `@hookform/resolvers`; the first message per field wins).
 * Candidate for `ui/` at cutover once a second module needs it.
 */
export function zodResolver<T extends FieldValues>(schema: z.ZodType<T>): Resolver<T> {
  return (values) => {
    const result = schema.safeParse(values)
    if (result.success) return { values: result.data, errors: {} }

    const errors: FieldErrors<T> = {}
    for (const issue of result.error.issues) {
      const path = issue.path.join('.')
      if (!get(errors, path)) set(errors, path, { type: issue.code, message: issue.message })
    }
    return { values: {}, errors }
  }
}

export function emptyProductForm(lookups: ProductLookups | undefined): ProductFormValues {
  return {
    name: '',
    description: '',
    barcode: '',
    categoryId: null,
    supplierId: lookups?.defaultSupplierId ?? null,
    currencyId: lookups?.defaultCurrencyId ?? null,
    purchasePrice: null,
    sellingPrice: null,
    unit: null,
    stockQuantity: 0,
    minStockLevel: 0,
    isActive: true,
    isPack: false,
    packSize: null,
    singleProductId: null,
    priceTiers: [],
  }
}

export function formFromProduct(p: ProductDetail): ProductFormValues {
  return {
    name: p.name,
    description: p.description ?? '',
    barcode: p.barcode,
    categoryId: p.categoryId,
    supplierId: p.supplierId,
    currencyId: p.currencyId,
    purchasePrice: p.purchasePrice,
    sellingPrice: p.sellingPrice,
    unit: p.unit ?? null,
    stockQuantity: p.stockQuantity,
    minStockLevel: p.minStockLevel,
    isActive: p.isActive,
    isPack: p.isPack,
    packSize: p.packSize ?? null,
    singleProductId: p.singleProductId ?? null,
    priceTiers: p.priceTiers.map((t) => ({ name: t.name ?? '', price: t.price ?? null })),
  }
}

/** The API body. Blank extra-price rows are dropped here; the server drops them too, but a row of spaces should not even travel. */
export function toSaveRequest(v: ProductFormValues): ProductSaveRequest {
  return {
    name: v.name.trim(),
    description: v.description.trim() || null,
    barcode: v.barcode.trim(),
    categoryId: v.categoryId ?? 0,
    supplierId: v.supplierId ?? 0,
    currencyId: v.currencyId ?? 0,
    purchasePrice: v.purchasePrice ?? 0,
    sellingPrice: v.sellingPrice ?? 0,
    stockQuantity: v.stockQuantity ?? 0,
    minStockLevel: v.minStockLevel ?? 0,
    isActive: v.isActive,
    unit: v.unit || null,
    isPack: v.isPack,
    packSize: v.isPack ? v.packSize : null,
    singleProductId: v.isPack ? v.singleProductId : null,
    priceTiers: v.priceTiers
      .filter((t) => t.name.trim() || t.price)
      .map((t) => ({ name: t.name.trim(), price: t.price ?? 0 })),
  }
}
