import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent, type UIEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Icon } from '@/ui/Icon'
import { toast } from '@/ui/toast'
import { formatMoney } from '@/ui/format'
import { useDebouncedValue } from '@/ui/hooks/useDebouncedValue'
import { lookupBarcode, usePosSearch, type PosProduct } from '../catalog/api'
import { stockLevel } from '../catalog/stock'
import { KeystrokeTimer } from '../scan/rapidInput'
import { posBus } from '../slots'
import { onFocusSearch } from './focusSearch'
import styles from './search.module.css'

export const SEARCH_DEBOUNCE_MS = 250
const NEAR_BOTTOM_PX = 80

/**
 * Product search for people: type part of a name or barcode, pick a match with the pointer or arrow keys, and it goes to the cart.
 * Results arrive 20 at a time and the next page loads as the list is scrolled. If a barcode scanner's burst of keystrokes ends up
 * here instead of in the scan box (focus drifted), Enter treats it as a scan rather than a name search.
 */
export function ProductSearch() {
  const { t } = useTranslation(['pos', 'common'])
  const listId = useId()
  const inputRef = useRef<HTMLInputElement>(null)
  const timer = useRef(new KeystrokeTimer())
  const [value, setValue] = useState('')
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(-1)
  const term = useDebouncedValue(value.trim(), SEARCH_DEBOUNCE_MS)

  const search = usePosSearch(term)
  const items = useMemo(() => (term ? (search.data?.pages.flatMap((page) => page.items) ?? []) : []), [term, search.data])
  const showList = open && value.trim() !== ''

  useEffect(() => onFocusSearch(() => inputRef.current?.focus()), [])

  const reset = () => {
    setValue('')
    setOpen(false)
    setActive(-1)
    timer.current.reset()
  }

  const pick = (product: PosProduct) => {
    posBus.addProduct({ product, quantity: 1, source: 'search' })
    reset()
    posBus.focusScan()
  }

  const scanFromHere = async (code: string) => {
    reset()
    try {
      pick(await lookupBarcode(code))
    } catch (cause) {
      toast.error(isApiError(cause) && cause.code === 'product_not_found' ? t('productNotFound') : isApiError(cause) ? cause.message : t('errorFetchingProduct'))
      posBus.focusScan()
    }
  }

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      if (items.length === 0) return
      e.preventDefault()
      setOpen(true)
      setActive((index) => {
        const next = e.key === 'ArrowDown' ? index + 1 : index - 1
        return (next + items.length) % items.length
      })
    } else if (e.key === 'Enter') {
      e.preventDefault()
      const code = value.trim()
      if (!code) return
      const highlighted = items[active]
      const first = items[0]
      if (highlighted) pick(highlighted)
      else if (timer.current.looksLikeScan()) void scanFromHere(code)
      else if (first && term === code) pick(first)
    } else if (e.key === 'Escape') {
      // Escape closes the list first; a second press clears the box and hands focus back to the scanner.
      e.preventDefault()
      if (showList) setOpen(false)
      else {
        reset()
        posBus.focusScan()
      }
    }
  }

  const onScroll = (e: UIEvent<HTMLUListElement>) => {
    const list = e.currentTarget
    if (list.scrollTop + list.clientHeight >= list.scrollHeight - NEAR_BOTTOM_PX && search.hasNextPage && !search.isFetchingNextPage) void search.fetchNextPage()
  }

  return (
    <div className={styles.search}>
      <span className={styles.icon}>
        <Icon name="search" size={16} />
      </span>
      <input
        ref={inputRef}
        className={styles.input}
        role="combobox"
        aria-expanded={showList}
        aria-controls={listId}
        aria-activedescendant={active >= 0 ? `${listId}-${active}` : undefined}
        aria-autocomplete="list"
        aria-label={t('Search')}
        placeholder={`${t('Search')}…`}
        autoComplete="off"
        spellCheck={false}
        value={value}
        onChange={(e) => {
          const next = e.target.value
          // Only typed or scanned characters count towards the speed check; deleting restarts it.
          if (next.length > value.length) timer.current.record()
          else timer.current.reset()
          setValue(next)
          setOpen(true)
          setActive(-1)
        }}
        onFocus={() => setOpen(true)}
        onBlur={() => window.setTimeout(() => setOpen(false), 120)}
        onKeyDown={onKeyDown}
      />
      {showList && (
        <ul id={listId} className={styles.list} role="listbox" aria-label={t('Search')} onScroll={onScroll}>
          {items.map((product, index) => (
            <li
              key={product.id}
              id={`${listId}-${index}`}
              role="option"
              aria-selected={index === active}
              className={styles.option}
              data-active={index === active}
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => pick(product)}
            >
              <span className={styles.optionMain}>
                <span className={styles.optionName}>{product.name}</span>
                <span className={styles.optionMeta} dir="ltr">
                  {product.barcode || '—'}
                </span>
              </span>
              <span className={styles.optionSide}>
                <span className={styles.optionPrice}>{formatMoney(product.sellingPrice, product.currencyNotation)}</span>
                <span className={styles.optionStock} data-level={stockLevel(product)}>
                  {stockLevel(product) === 'out' ? t('OutOfStock', { ns: 'common' }) : product.stockQuantity}
                </span>
              </span>
            </li>
          ))}
          {search.isFetching && <li className={styles.status}>{t('Searching')}</li>}
          {!search.isFetching && term === value.trim() && items.length === 0 && <li className={styles.status}>{t('NoProductsFound')}</li>}
          {search.isError && <li className={styles.status}>{isApiError(search.error) ? search.error.message : t('errorFetchingProduct')}</li>}
        </ul>
      )}
    </div>
  )
}
