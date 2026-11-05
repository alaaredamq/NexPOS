import { formatMoney, formatQty } from '@/ui/format'
import type { ProductHistoryItem } from './api'

/** Same shape as i18next's `t`, so the sentence logic can be tested without a provider. */
export type Translate = (key: string, options?: Record<string, unknown>) => string

export type HistoryTone = 'success' | 'danger' | 'warning' | 'info' | 'neutral'

export interface DescribedEvent {
  /** The one sentence: "5 added to stock", "2 sold in sale ORD-000042", "Selling price changed from $6.00 to $7.50". */
  text: string
  tone: HistoryTone
  /** Shown as "Stock: 15 → 13" under the sentence, for the events that move units and don't already say it. */
  stock: { before: number; after: number } | null
}

interface Context {
  t: Translate
  /** The product's unit (piece, kg, ...). */
  unit?: string | null
  currencyNotation: string
}

const STOCK_LINE_TYPES = new Set(['stockAdded', 'stockRemoved', 'sold', 'saleReturned', 'received'])

/** Turns one stored history line into the sentence the user reads. The server stores facts, not sentences, so this follows the UI language. */
export function describeEvent(item: ProductHistoryItem, { t, unit, currencyNotation }: Context): DescribedEvent {
  const quantity = (value: number) => {
    const n = formatQty(Math.abs(value))
    return unit && unit !== 'piece' ? `${n} ${unit}` : t('history.units', { count: n, ns: 'products' })
  }
  const money = (value: number) => formatMoney(value, currencyNotation)
  const amount = (value: number | null | undefined) => (value == null ? t('history.none', { ns: 'products' }) : formatQty(value))
  const change = item.quantityChange ?? 0
  const imported = item.fromImport
  const params = { quantity: quantity(change), order: item.orderNumber ?? '' }

  const stock =
    STOCK_LINE_TYPES.has(item.type) && item.stockBefore != null && item.stockAfter != null
      ? { before: item.stockBefore, after: item.stockAfter }
      : null
  const done = (key: string, tone: HistoryTone, options: Record<string, unknown> = params): DescribedEvent => ({
    text: t(key, options),
    tone,
    stock,
  })

  switch (item.type) {
    case 'created':
      return done(imported ? 'history.event.createdImport' : 'history.event.created', 'success', { quantity: quantity(item.stockAfter ?? 0) })
    case 'stockAdded':
      return done('history.event.stockAdded', 'success')
    case 'received':
      return done('history.event.received', 'success')
    case 'saleReturned':
      return done('history.event.saleReturned', 'success')
    case 'stockRemoved':
      return done('history.event.stockRemoved', 'danger')
    case 'sold':
      return done('history.event.sold', 'info')
    case 'stockSet':
      return done(imported ? 'history.event.stockSetImport' : 'history.event.stockSet', 'warning', {
        from: amount(item.stockBefore),
        to: amount(item.stockAfter),
      })
    case 'sellingPriceChanged':
      return done('history.event.sellingPriceChanged', 'neutral', { from: money(item.oldValue ?? 0), to: money(item.newValue ?? 0) })
    case 'costPriceChanged': {
      if (item.oldValue == null) return done('history.event.costPriceSet', 'neutral', { to: money(item.newValue ?? 0) })
      if (item.newValue == null) return done('history.event.costPriceCleared', 'neutral', { from: money(item.oldValue) })
      return done('history.event.costPriceChanged', 'neutral', { from: money(item.oldValue), to: money(item.newValue) })
    }
    case 'minimumStockChanged': {
      if (item.oldValue == null) return done('history.event.minimumStockSet', 'neutral', { to: amount(item.newValue) })
      if (item.newValue == null) return done('history.event.minimumStockCleared', 'neutral', { from: amount(item.oldValue) })
      return done('history.event.minimumStockChanged', 'neutral', { from: amount(item.oldValue), to: amount(item.newValue) })
    }
    case 'updated':
      return done('history.event.updated', 'neutral', { fields: listOf(item.fields.map((f) => t(`history.field.${f}`))) })
    case 'deleted':
      return done('history.event.deleted', 'danger')
    default:
      return done('history.event.unknown', 'neutral')
  }
}

/** "name, barcode and category" in the UI language. */
function listOf(items: string[]): string {
  const lang = typeof document !== 'undefined' ? document.documentElement.lang || 'en' : 'en'
  try {
    return new Intl.ListFormat(lang, { style: 'long', type: 'conjunction' }).format(items)
  } catch {
    return items.join(', ')
  }
}
