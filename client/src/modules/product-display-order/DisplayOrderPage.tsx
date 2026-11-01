import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useBlocker } from 'react-router-dom'
import {
  closestCenter,
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type Announcements,
  type DragEndEvent,
} from '@dnd-kit/core'
import { SortableContext, sortableKeyboardCoordinates, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { confirm } from '@/ui/confirm'
import { formatMoney, formatNumber, formatQty } from '@/ui/format'
import { IconButton } from '@/ui/IconButton'
import { PageHeader } from '@/ui/PageHeader'
import { Skeleton } from '@/ui/Skeleton'
import { StatTile } from '@/ui/StatTile'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import { useDisplayOrder, useRemoveFromDisplayOrder, useSaveOrder, type DisplayOrderItem } from './api'
import { AddProductModal } from './AddProductModal'
import { moveItem, reconcileOrder, sameOrder } from './order'
import { Thumb } from './Thumb'
import styles from './displayOrder.module.css'

/** `/app/product-display-order` - which products the POS main screen shows, and in what order. */
export default function DisplayOrderPage() {
  const { t } = useTranslation('productDisplayOrder')
  const query = useDisplayOrder()
  const save = useSaveOrder()
  const remove = useRemoveFromDisplayOrder()
  const [adding, setAdding] = useState(false)
  // null = the user has not rearranged anything: follow the server.
  const [arranged, setArranged] = useState<number[] | null>(null)

  const items = query.data?.items
  const serverIds = useMemo(() => items?.map((i) => i.id) ?? [], [items])
  const ids = useMemo(() => reconcileOrder(arranged, serverIds), [arranged, serverIds])
  const dirty = !sameOrder(ids, serverIds)
  const byId = useMemo(() => new Map<number, DisplayOrderItem>((items ?? []).map((i) => [i.id, i])), [items])

  // Leaving with an unsaved order asks first (router navigation and tab close/reload).
  const blocker = useBlocker(dirty)
  useEffect(() => {
    if (blocker.state !== 'blocked') return
    void confirm({
      title: t('UnsavedOrderTitle'),
      message: t('UnsavedOrderMessage'),
      confirmLabel: t('LeaveWithoutSaving'),
      danger: true,
    }).then((leave) => (leave ? blocker.proceed() : blocker.reset()))
  }, [blocker, t])
  useEffect(() => {
    if (!dirty) return
    const warn = (e: BeforeUnloadEvent) => e.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  )

  const onDragEnd = ({ active, over }: DragEndEvent) => {
    if (!over || active.id === over.id) return
    setArranged(moveItem(ids, ids.indexOf(Number(active.id)), ids.indexOf(Number(over.id))))
  }

  const nameOf = (id: unknown) => byId.get(Number(id))?.name ?? ''
  const announcements: Announcements = {
    onDragStart: ({ active }) => t('DragPickedUp', { 0: nameOf(active.id) }),
    onDragOver: ({ active, over }) => (over ? t('DragMovedTo', { 0: nameOf(active.id), 1: ids.indexOf(Number(over.id)) + 1 }) : undefined),
    onDragEnd: ({ active, over }) => (over ? t('DragDroppedAt', { 0: nameOf(active.id), 1: ids.indexOf(Number(over.id)) + 1 }) : t('DragDropped', { 0: nameOf(active.id) })),
    onDragCancel: ({ active }) => t('DragCancelled', { 0: nameOf(active.id) }),
  }

  const saveOrder = async () => {
    try {
      await save.mutateAsync(ids)
      setArranged(null)
      toast.success(t('OrderSaved'))
    } catch (error) {
      toast.error(isApiError(error) ? error.message : t('AnErrorOccurredWhileSavingTheOrder'))
    }
  }

  const askRemove = async (item: DisplayOrderItem) => {
    await confirm({
      title: t('ConfirmRemove'),
      message: t('AreYouSureYouWantToRemoveThisProductFromTheDisplayOrder'),
      detail: item.name,
      confirmLabel: t('Remove'),
      danger: true,
      onConfirm: async () => {
        await remove.mutateAsync(item.id)
        toast.success(t('ProductRemovedFromDisplayOrder'))
      },
    })
  }

  const data = query.data
  return (
    <div className={styles.page}>
      <PageHeader title={t('ProductDisplayOrderManagement')} count={data ? formatNumber(data.total, 0) : undefined}>
        <Button variant="ghost" onClick={() => setArranged(null)} disabled={!dirty || save.isPending}>
          {t('DiscardChanges')}
        </Button>
        <Button onClick={() => setAdding(true)}>{t('AddProduct')}</Button>
        <Button variant="primary" onClick={() => void saveOrder()} loading={save.isPending} disabled={!dirty}>
          {t('SaveOrder')}
        </Button>
      </PageHeader>
      <p className={styles.lead}>{t('ManageWhichProductsAppearOnThePOSMainScreenAndTheirOrder')}</p>

      <div className={styles.tiles}>
        <StatTile label={t('TotalProducts')} value={data ? formatNumber(data.total, 0) : '-'} sub={t('InDisplayOrder')} />
        <StatTile label={t('Active')} value={data ? formatNumber(data.active, 0) : '-'} sub={t('VisibleOnPOS')} tone="success" />
        <StatTile label={t('Inactive')} value={data ? formatNumber(data.inactive, 0) : '-'} sub={t('HiddenFromPOS')} />
        <StatTile label={t('LowStock')} value={data ? formatNumber(data.lowStock, 0) : '-'} sub={t('NeedsAttention')} tone={data && data.lowStock > 0 ? 'danger' : 'default'} />
      </div>

      <section className={styles.card} aria-busy={query.isPending || undefined}>
        {query.isError ? (
          <div className={styles.state} role="alert">
            {query.error.message}
            <Button onClick={() => void query.refetch()}>{t('common.retry', { ns: 'common' })}</Button>
          </div>
        ) : query.isPending ? (
          <div className={styles.state}>
            {Array.from({ length: 5 }, (_, i) => (
              <Skeleton key={i} height={44} />
            ))}
          </div>
        ) : ids.length === 0 ? (
          <div className={styles.state}>
            <strong>{t('NoProductsOnMainScreen')}</strong>
            <span className={styles.muted}>{t('NoProductsOnMainScreenMessage')}</span>
            <Button variant="primary" onClick={() => setAdding(true)}>
              {t('AddProduct')}
            </Button>
          </div>
        ) : (
          <DndContext
            sensors={sensors}
            collisionDetection={closestCenter}
            onDragEnd={onDragEnd}
            accessibility={{ announcements, screenReaderInstructions: { draggable: t('DragInstructions') } }}
          >
            <div className={styles.scroll}>
              <table className={styles.table}>
                <caption className="sr-only">{t('DisplayOrder')}</caption>
                <thead>
                  <tr>
                    <th scope="col" className={styles.orderCol}>{t('Order')}</th>
                    <th scope="col">{t('ProductName')}</th>
                    <th scope="col" className={styles.hideNarrow}>{t('Category')}</th>
                    <th scope="col" data-align="end">{t('Price')}</th>
                    <th scope="col" data-align="center">{t('Stock')}</th>
                    <th scope="col" data-align="center" className={styles.hideNarrow}>{t('Status')}</th>
                    <th scope="col"><span className="sr-only">{t('Actions')}</span></th>
                  </tr>
                </thead>
                <SortableContext items={ids} strategy={verticalListSortingStrategy}>
                  <tbody>
                    {ids.map((id, index) => {
                      const item = byId.get(id)
                      return item ? (
                        <SortableRow
                          key={id}
                          item={item}
                          position={index + 1}
                          isFirst={index === 0}
                          isLast={index === ids.length - 1}
                          onMove={(to) => setArranged(moveItem(ids, index, to))}
                          onRemove={() => void askRemove(item)}
                        />
                      ) : null
                    })}
                  </tbody>
                </SortableContext>
              </table>
            </div>
          </DndContext>
        )}
      </section>

      {adding && <AddProductModal onClose={() => setAdding(false)} />}
    </div>
  )
}

interface RowProps {
  item: DisplayOrderItem
  position: number
  isFirst: boolean
  isLast: boolean
  onMove: (to: number) => void
  onRemove: () => void
}

function SortableRow({ item, position, isFirst, isLast, onMove, onRemove }: RowProps) {
  const { t } = useTranslation('productDisplayOrder')
  const { attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging } = useSortable({ id: item.id })

  return (
    <tr ref={setNodeRef} className={styles.row} data-dragging={isDragging || undefined} style={{ transform: CSS.Transform.toString(transform), transition }}>
      <td className={styles.orderCol}>
        <div className={styles.orderCell}>
          <button
            type="button"
            ref={setActivatorNodeRef}
            className={styles.handle}
            aria-label={t('DragToReorder', { 0: item.name })}
            title={t('DragToReorder', { 0: item.name })}
            {...attributes}
            {...listeners}
          >
            <GripIcon />
          </button>
          <span className={styles.position}>{position}</span>
        </div>
      </td>
      <td>
        <div className={styles.product}>
          <Thumb src={item.image} />
          <div className={styles.productText}>
            <strong className={styles.clip}>
              <bdi>{item.name}</bdi>
            </strong>
            {item.description && (
              <span className={`${styles.muted} ${styles.clip}`}>
                <bdi>{item.description}</bdi>
              </span>
            )}
          </div>
        </div>
      </td>
      <td className={styles.hideNarrow}>
        <StatusBadge tone="info" label={item.categoryName ?? t('NoCategory')} />
      </td>
      <td data-align="end" className={styles.price}>
        {formatMoney(item.price, item.currencyNotation)}
      </td>
      <td data-align="center">
        <StatusBadge tone={item.isLowStock ? 'danger' : 'success'} label={formatQty(item.stockQuantity)} />
      </td>
      <td data-align="center" className={styles.hideNarrow}>
        <StatusBadge tone={item.isActive ? 'success' : 'neutral'} label={item.isActive ? t('Active') : t('Inactive')} />
      </td>
      <td>
        <div className={styles.actions}>
          <IconButton icon="chevron-up" label={t('MoveUp')} disabled={isFirst} onClick={() => onMove(position - 2)} />
          <IconButton icon="chevron-down" label={t('MoveDown')} disabled={isLast} onClick={() => onMove(position)} />
          <IconButton icon="trash" tone="danger" label={t('Remove')} onClick={onRemove} />
        </div>
      </td>
    </tr>
  )
}

function GripIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
      {[4, 8, 12].map((y) => [5.5, 10.5].map((x) => <circle key={`${x}-${y}`} cx={x} cy={y} r="1.2" />))}
    </svg>
  )
}
