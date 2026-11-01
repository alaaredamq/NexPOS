import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { ChipGroup } from '@/ui/ChipGroup'
import { formatMoney } from '@/ui/format'
import { Loader } from '@/ui/Loader'
import { Modal, ModalBody } from '@/ui/Modal'
import { SearchBox } from '@/ui/SearchBox'
import { toast } from '@/ui/toast'
import { useAddToDisplayOrder, useAvailableProducts, type AvailableProduct } from './api'
import { QuickAddForm } from './QuickAddForm'
import { Thumb } from './Thumb'
import styles from './displayOrder.module.css'

type Tab = 'existing' | 'quick'

interface Props {
  onClose: () => void
}

/** "Add product": pick an existing product that is not on the main screen yet, or quick-add a brand new one. */
export function AddProductModal({ onClose }: Props) {
  const { t } = useTranslation('productDisplayOrder')
  const [tab, setTab] = useState<Tab>('existing')

  return (
    <Modal onClose={onClose} width={720} title={t('AddProductToDisplayOrder')}>
      <div className={styles.tabs}>
        <ChipGroup
          aria-label={t('AddProductToDisplayOrder')}
          value={tab}
          onChange={(value) => setTab(value as Tab)}
          options={[
            { value: 'existing', label: t('ExistingProducts') },
            { value: 'quick', label: t('QuickAddProduct') },
          ]}
        />
      </div>
      {tab === 'existing' ? <ExistingProducts /> : <QuickAddForm onCancel={onClose} onDone={onClose} />}
    </Modal>
  )
}

function ExistingProducts() {
  const { t } = useTranslation('productDisplayOrder')
  const [q, setQ] = useState('')
  const query = useAvailableProducts(q)
  const add = useAddToDisplayOrder()
  const [addingId, setAddingId] = useState<number | null>(null)

  const products = query.data?.pages.flatMap((page) => page.items) ?? []
  // The preview (nothing typed) shows only the first few rows: say there are more to find by searching.
  const previewTruncated = !q && (query.data?.pages.at(-1)?.hasMore ?? false)

  const addProduct = async (product: AvailableProduct) => {
    setAddingId(product.id)
    try {
      await add.mutateAsync(product.id)
      toast.success(t('ProductAddedToDisplayOrder'))
    } catch (error) {
      toast.error(isApiError(error) ? error.message : t('AnErrorOccurredWhileSavingTheOrder'))
    } finally {
      setAddingId(null)
    }
  }

  return (
    <ModalBody>
      <div className={styles.form}>
        <SearchBox value={q} onSearch={setQ} placeholder={t('TypeToSearchProducts')} aria-label={t('SearchProducts')} autoFocus />

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
            <div className={styles.pickerNote}>{t('NoProductsFound')}</div>
          ) : (
            <ul className={styles.pickerList}>
              {products.map((product) => (
                <li key={product.id} className={styles.pickerRow}>
                  <Thumb src={product.image} size={40} />
                  <div className={styles.pickerMain}>
                    <strong className={styles.clip}>
                      <bdi>{product.name}</bdi>
                    </strong>
                    <span className={styles.muted}>
                      <bdi>{product.categoryName ?? t('NoCategory')}</bdi>
                    </span>
                  </div>
                  <span className={styles.price}>{formatMoney(product.price, product.currencyNotation)}</span>
                  <Button variant="primary" size="md" loading={addingId === product.id} disabled={addingId !== null} onClick={() => void addProduct(product)}>
                    {t('Add')}
                  </Button>
                </li>
              ))}
            </ul>
          )}
          {query.isFetchingNextPage && <Loader label={t('Loading')} />}
          {previewTruncated && <div className={styles.pickerNote}>{t('SearchToFindMoreProducts')}</div>}
          {query.hasNextPage && !query.isFetchingNextPage && (
            <div className={styles.pickerNote}>
              <Button variant="ghost" onClick={() => void query.fetchNextPage()}>
                {t('LoadMore')}
              </Button>
            </div>
          )}
        </div>
      </div>
    </ModalBody>
  )
}
