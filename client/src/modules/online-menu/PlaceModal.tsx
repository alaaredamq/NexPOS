import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { ChipGroup } from '@/ui/ChipGroup'
import { formatMoney, formatNumber } from '@/ui/format'
import { Loader } from '@/ui/Loader'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { SearchBox } from '@/ui/SearchBox'
import { toast } from '@/ui/toast'
import { useCategoryOptions, usePlace, useProductOptions, type OnlineMenuSection } from './api'
import styles from './onlineMenu.module.css'

type Tab = 'categories' | 'products'

interface Props {
  section: OnlineMenuSection
  onClose: () => void
}

/**
 * "Add products" to a section: tick whole categories (they keep following the category, new products included) and/or single products.
 * Anything already in another section is marked, and moves here when added: a product shows in one place on the menu.
 */
export function PlaceModal({ section, onClose }: Props) {
  const { t } = useTranslation('onlineMenu')
  const [tab, setTab] = useState<Tab>('categories')
  const [products, setProducts] = useState<Set<number>>(new Set())
  const [categories, setCategories] = useState<Set<number>>(new Set())
  const place = usePlace()
  const count = products.size + categories.size

  const toggle = (set: Set<number>, update: (s: Set<number>) => void, id: number) => {
    const next = new Set(set)
    if (next.has(id)) next.delete(id)
    else next.add(id)
    update(next)
  }

  const submit = async () => {
    try {
      await place.mutateAsync({ sectionId: section.id, productIds: [...products], categoryIds: [...categories] })
      toast.success(t('AddedToSection', { 0: section.name }))
      onClose()
    } catch (error) {
      toast.error(isApiError(error) ? error.message : t('SomethingWentWrong'))
    }
  }

  return (
    <Modal onClose={onClose} width={720} title={t('AddToSection', { 0: section.name })}>
      <div className={styles.tabs}>
        <ChipGroup
          aria-label={t('AddToSection', { 0: section.name })}
          value={tab}
          onChange={(value) => setTab(value as Tab)}
          options={[
            { value: 'categories', label: categories.size ? `${t('WholeCategories')} (${categories.size})` : t('WholeCategories') },
            { value: 'products', label: products.size ? `${t('SingleProducts')} (${products.size})` : t('SingleProducts') },
          ]}
        />
      </div>
      <ModalBody>
        <p className={styles.muted}>{tab === 'categories' ? t('WholeCategoriesHelp') : t('SingleProductsHelp')}</p>
        {tab === 'categories' ? (
          <CategoryPicker sectionId={section.id} picked={categories} onToggle={(id) => toggle(categories, setCategories, id)} />
        ) : (
          <ProductPicker sectionId={section.id} picked={products} onToggle={(id) => toggle(products, setProducts, id)} />
        )}
      </ModalBody>
      <ModalFooter>
        <Button variant="ghost" onClick={onClose}>
          {t('Cancel')}
        </Button>
        <Button variant="primary" disabled={count === 0} loading={place.isPending} onClick={() => void submit()}>
          {count === 0 ? t('AddToSectionButton') : t('AddNToSection', { 0: formatNumber(count, 0) })}
        </Button>
      </ModalFooter>
    </Modal>
  )
}

interface PickerProps {
  sectionId: number
  picked: Set<number>
  onToggle: (id: number) => void
}

function CategoryPicker({ sectionId, picked, onToggle }: PickerProps) {
  const { t } = useTranslation('onlineMenu')
  const query = useCategoryOptions()
  if (query.isPending) return <Loader label={t('Loading')} />
  if (query.isError) return <div role="alert">{query.error.message}</div>
  const rows = query.data
  if (rows.length === 0) return <p className={styles.pickerNote}>{t('NoCategories')}</p>

  return (
    <ul className={styles.pickerList}>
      {rows.map((c) => {
        const here = c.sectionId === sectionId
        return (
          <li key={c.id}>
            <label className={styles.pickerRow} data-disabled={here || undefined}>
              <input type="checkbox" checked={here || picked.has(c.id)} disabled={here} onChange={() => onToggle(c.id)} />
              <span className={styles.pickerMain}>
                <strong className={styles.clip}>
                  <bdi>{c.name}</bdi>
                </strong>
                <span className={styles.muted}>{t('NActiveProducts', { 0: formatNumber(c.activeProducts, 0) })}</span>
              </span>
              <Where here={here} sectionName={c.sectionName} />
            </label>
          </li>
        )
      })}
    </ul>
  )
}

function ProductPicker({ sectionId, picked, onToggle }: PickerProps) {
  const { t } = useTranslation('onlineMenu')
  const [q, setQ] = useState('')
  const query = useProductOptions(q)
  const products = useMemo(() => query.data?.pages.flatMap((page) => page.items) ?? [], [query.data])

  return (
    <div className={styles.form}>
      <SearchBox value={q} onSearch={setQ} placeholder={t('SearchProducts')} aria-label={t('SearchProducts')} autoFocus />
      <div
        className={styles.picker}
        onScroll={(e) => {
          const el = e.currentTarget
          if (el.scrollTop + el.clientHeight >= el.scrollHeight - 40 && query.hasNextPage && !query.isFetchingNextPage) void query.fetchNextPage()
        }}
      >
        {query.isPending ? (
          <Loader label={t('Loading')} />
        ) : query.isError ? (
          <div role="alert" className={styles.pickerNote}>
            {query.error.message}
          </div>
        ) : products.length === 0 ? (
          <p className={styles.pickerNote}>{t('NoProductsFound')}</p>
        ) : (
          <ul className={styles.pickerList}>
            {products.map((p) => {
              const here = p.sectionId === sectionId
              return (
                <li key={p.id}>
                  <label className={styles.pickerRow} data-disabled={here || undefined}>
                    <input type="checkbox" checked={here || picked.has(p.id)} disabled={here} onChange={() => onToggle(p.id)} />
                    <span className={styles.pickerMain}>
                      <strong className={styles.clip}>
                        <bdi>{p.name}</bdi>
                      </strong>
                      <span className={styles.muted}>
                        <bdi>{p.categoryName ?? ''}</bdi>
                        {p.viaCategorySectionName ? ` · ${t('ShowsThroughCategory', { 0: p.viaCategorySectionName })}` : ''}
                      </span>
                    </span>
                    <span className={styles.price}>{formatMoney(p.price, p.currencyNotation)}</span>
                    <Where here={here} sectionName={p.sectionName} />
                  </label>
                </li>
              )
            })}
          </ul>
        )}
        {query.isFetchingNextPage && <Loader label={t('Loading')} />}
      </div>
    </div>
  )
}

/** Where a pick is now: already here, or in another section it would move from. */
function Where({ here, sectionName }: { here: boolean; sectionName?: string | null }) {
  const { t } = useTranslation('onlineMenu')
  if (here) return <span className={styles.where}>{t('AlreadyHere')}</span>
  if (sectionName)
    return (
      <span className={styles.where} data-move>
        {t('MovesFrom', { 0: sectionName })}
      </span>
    )
  return null
}
