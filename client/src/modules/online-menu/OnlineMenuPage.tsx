import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { confirm } from '@/ui/confirm'
import { formatMoney, formatNumber } from '@/ui/format'
import { IconButton } from '@/ui/IconButton'
import { Loader } from '@/ui/Loader'
import { PageHeader } from '@/ui/PageHeader'
import { Skeleton } from '@/ui/Skeleton'
import { StatTile } from '@/ui/StatTile'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import {
  moved,
  useDeleteSection,
  useOnlineMenu,
  useRemoveEntry,
  useMoveEntry,
  useReorderSections,
  useSectionEntries,
  useSendNow,
  type OnlineMenuEntry,
  type OnlineMenuSection,
  type OnlineMenuStatus,
} from './api'
import { PlaceModal } from './PlaceModal'
import { SectionNameModal } from './SectionNameModal'
import styles from './onlineMenu.module.css'

const fail = (error: unknown, fallback: string) => toast.error(isApiError(error) ? error.message : fallback)

/**
 * `/app/online-menu` - what the shop's public online menu shows. Sections here are the menu's own headings ("Breakfast",
 * "Today's specials"), separate from product categories; each one shows hand-picked products and/or whole categories. With no
 * sections, the online menu shows every product by category. Pictures and descriptions are added in the cloud's menu editor.
 */
export default function OnlineMenuPage() {
  const { t } = useTranslation('onlineMenu')
  const query = useOnlineMenu()
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [naming, setNaming] = useState<OnlineMenuSection | 'new' | null>(null)
  const [placing, setPlacing] = useState<OnlineMenuSection | null>(null)
  const reorder = useReorderSections()

  const data = query.data
  const sections = data?.sections ?? []
  // The picked section, or the first one (also after the picked one was deleted).
  const selected = sections.find((s) => s.id === selectedId) ?? sections[0] ?? null

  const moveSection = (index: number, step: -1 | 1) =>
    reorder.mutateAsync(moved(sections.map((s) => s.id), index, step)).catch((e) => fail(e, t('SomethingWentWrong')))

  return (
    <div className={styles.page}>
      <PageHeader title={t('OnlineMenu')}>
        <Button variant="primary" onClick={() => setNaming('new')}>
          {t('AddSection')}
        </Button>
      </PageHeader>
      <p className={styles.lead}>{t('Lead')}</p>

      {data && <StatusCard status={data.status} />}

      <div className={styles.tiles}>
        <StatTile label={t('Sections')} value={data ? formatNumber(sections.length, 0) : '-'} sub={t('OnTheMenu')} />
        <StatTile label={t('ProductsShown')} value={data ? formatNumber(data.productsShown, 0) : '-'} sub={t('CustomersSeeThese')} tone="success" />
        <StatTile
          label={t('LeftOut')}
          value={data ? formatNumber(data.productsLeftOut, 0) : '-'}
          sub={t('ActiveButNotInASection')}
          tone={data && data.productsLeftOut > 0 ? 'info' : 'default'}
        />
      </div>

      {query.isError ? (
        <section className={styles.card}>
          <div className={styles.state} role="alert">
            {query.error.message}
            <Button onClick={() => void query.refetch()}>{t('common.retry', { ns: 'common' })}</Button>
          </div>
        </section>
      ) : query.isPending ? (
        <section className={styles.card}>
          <div className={styles.state}>
            {Array.from({ length: 4 }, (_, i) => (
              <Skeleton key={i} height={44} />
            ))}
          </div>
        </section>
      ) : sections.length === 0 ? (
        <section className={styles.card}>
          <div className={styles.state}>
            <strong>{t('NoSectionsYet')}</strong>
            <span className={styles.muted}>{t('NoSectionsYetMessage')}</span>
            <Button variant="primary" onClick={() => setNaming('new')}>
              {t('AddFirstSection')}
            </Button>
          </div>
        </section>
      ) : (
        <div className={styles.split}>
          <nav className={`${styles.card} ${styles.sections}`} aria-label={t('Sections')}>
            <ol className={styles.sectionList}>
              {sections.map((s, i) => (
                <li key={s.id} className={styles.sectionRow} data-selected={s.id === selected?.id || undefined}>
                  <button type="button" className={styles.sectionPick} aria-current={s.id === selected?.id || undefined} onClick={() => setSelectedId(s.id)}>
                    <span className={styles.sectionNo}>{i + 1}</span>
                    <span className={styles.sectionName}>
                      <bdi>{s.name}</bdi>
                    </span>
                    <span className={styles.sectionCount}>{t('NProducts', { 0: formatNumber(s.productCount, 0) })}</span>
                  </button>
                  <div className={styles.rowActions}>
                    <IconButton icon="chevron-up" size="sm" label={t('MoveUp')} disabled={i === 0 || reorder.isPending} onClick={() => void moveSection(i, -1)} />
                    <IconButton
                      icon="chevron-down"
                      size="sm"
                      label={t('MoveDown')}
                      disabled={i === sections.length - 1 || reorder.isPending}
                      onClick={() => void moveSection(i, 1)}
                    />
                  </div>
                </li>
              ))}
            </ol>
          </nav>
          {selected && (
            <SectionDetail
              key={selected.id}
              section={selected}
              onRename={() => setNaming(selected)}
              onAdd={() => setPlacing(selected)}
              onDeleted={() => setSelectedId(null)}
            />
          )}
        </div>
      )}

      {naming && (
        <SectionNameModal
          section={naming === 'new' ? undefined : naming}
          onClose={() => setNaming(null)}
          onCreated={(id) => setSelectedId(id)}
        />
      )}
      {placing && <PlaceModal section={placing} onClose={() => setPlacing(null)} />}
    </div>
  )
}

function StatusCard({ status }: { status: OnlineMenuStatus }) {
  const { t } = useTranslation('onlineMenu')
  const sendNow = useSendNow()

  if (!status.connected)
    return (
      <div className={styles.banner} data-tone="neutral">
        <div>
          <strong>{t('NotConnected')}</strong>
          <span>{t('NotConnectedMessage')}</span>
        </div>
        <Link className={styles.bannerLink} to="/settings/online">
          {t('OpenOnlineSettings')}
        </Link>
      </div>
    )

  if (status.linked == null)
    return (
      <div className={styles.banner} data-tone="neutral" role="status">
        <div>
          <strong>{t('Checking')}</strong>
          <span>{t('CheckingMessage')}</span>
        </div>
      </div>
    )

  if (!status.linked)
    return (
      <div className={styles.banner} data-tone="warning">
        <div>
          <strong>{t('NotLinked')}</strong>
          <span>{t('NotLinkedMessage')}</span>
        </div>
        <div className={styles.bannerActions}>
          {status.editorUrl && (
            <a className={styles.bannerLink} href={status.editorUrl} target="_blank" rel="noopener">
              {t('OpenMenuEditor')}
            </a>
          )}
          <Button size="md" loading={sendNow.isPending} onClick={() => void sendNow.mutateAsync().then(() => toast.success(t('CheckingNow')))}>
            {t('CheckAgain')}
          </Button>
        </div>
      </div>
    )

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(status.menuUrl ?? '')
      toast.success(t('LinkCopied'))
    } catch {
      toast.error(status.menuUrl ?? '')
    }
  }

  return (
    <div className={styles.banner} data-tone="success">
      <div className={styles.bannerMain}>
        <strong>{t('Live')}</strong>
        <span className={styles.url} dir="ltr">
          {status.menuUrl}
        </span>
        <span className={styles.muted}>{status.lastSentUtc ? t('LastSent', { 0: new Date(status.lastSentUtc).toLocaleTimeString() }) : t('SendingSoon')}</span>
      </div>
      <div className={styles.bannerActions}>
        <Button size="md" onClick={() => void copy()}>
          {t('CopyLink')}
        </Button>
        {status.menuUrl && (
          <a className={styles.bannerLink} href={status.menuUrl} target="_blank" rel="noopener">
            {t('OpenMenu')}
          </a>
        )}
        {status.editorUrl && (
          <a className={styles.bannerLink} href={status.editorUrl} target="_blank" rel="noopener">
            {t('AddPictures')}
          </a>
        )}
      </div>
    </div>
  )
}

interface DetailProps {
  section: OnlineMenuSection
  onRename: () => void
  onAdd: () => void
  onDeleted: () => void
}

function SectionDetail({ section, onRename, onAdd, onDeleted }: DetailProps) {
  const { t } = useTranslation('onlineMenu')
  const remove = useRemoveEntry()
  const moveEntry = useMoveEntry()
  const del = useDeleteSection()
  // A section can hold thousands of picks: they load a page at a time as the list scrolls.
  const query = useSectionEntries(section.id)
  const entries = query.data?.pages.flatMap((page) => page.items) ?? []
  const sentinel = useLoadMore(() => {
    if (query.hasNextPage && !query.isFetchingNextPage) void query.fetchNextPage()
  })

  const askDelete = () =>
    confirm({
      title: t('DeleteSectionTitle'),
      message: t('DeleteSectionMessage'),
      detail: section.name,
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        await del.mutateAsync(section.id)
        onDeleted()
        toast.success(t('SectionDeleted'))
      },
    })

  const move = (entry: OnlineMenuEntry, direction: -1 | 1) =>
    moveEntry.mutateAsync({ id: entry.id, direction }).catch((e) => fail(e, t('SomethingWentWrong')))

  const take = (entry: OnlineMenuEntry) =>
    remove
      .mutateAsync(entry.id)
      .then(() => toast.success(t('TakenOff', { 0: entry.name })))
      .catch((e) => fail(e, t('SomethingWentWrong')))

  return (
    <section className={`${styles.card} ${styles.detail}`} aria-labelledby="om-section-title">
      <header className={styles.detailHead}>
        <div className={styles.detailTitle}>
          <h2 id="om-section-title">
            <bdi>{section.name}</bdi>
          </h2>
          <span className={styles.muted}>{t('NProducts', { 0: formatNumber(section.productCount, 0) })}</span>
        </div>
        <div className={styles.detailActions}>
          <Button variant="ghost" onClick={onRename}>
            {t('Rename')}
          </Button>
          <Button variant="ghost" onClick={() => void askDelete()}>
            {t('Delete')}
          </Button>
          <Button variant="primary" onClick={onAdd}>
            {t('AddProducts')}
          </Button>
        </div>
      </header>

      {query.isPending ? (
        <div className={styles.state}>
          {Array.from({ length: 3 }, (_, i) => (
            <Skeleton key={i} height={44} />
          ))}
        </div>
      ) : query.isError ? (
        <div className={styles.state} role="alert">
          {query.error.message}
          <Button onClick={() => void query.refetch()}>{t('common.retry', { ns: 'common' })}</Button>
        </div>
      ) : entries.length === 0 ? (
        <div className={styles.state}>
          <strong>{t('EmptySection')}</strong>
          <span className={styles.muted}>{t('EmptySectionMessage')}</span>
          <Button variant="primary" onClick={onAdd}>
            {t('AddProducts')}
          </Button>
        </div>
      ) : (
        <ol className={styles.entries}>
          {entries.map((e, i) => (
            <li key={e.id} className={styles.entry} data-off={!e.isActive || undefined}>
              <EntryThumb entry={e} />
              <div className={styles.entryText}>
                <strong className={styles.clip}>
                  <bdi>{e.name}</bdi>
                </strong>
                {e.kind === 'category' ? (
                  <span className={styles.muted}>{t('WholeCategory', { 0: formatNumber(e.productCount, 0) })}</span>
                ) : !e.isActive ? (
                  <span className={styles.muted}>{t('SwitchedOff')}</span>
                ) : null}
              </div>
              {e.kind === 'category' ? (
                <StatusBadge tone="info" label={t('Category')} />
              ) : (
                <span className={styles.price}>{formatMoney(e.price ?? 0, e.currencyNotation ?? '')}</span>
              )}
              <div className={styles.rowActions}>
                <IconButton icon="chevron-up" size="sm" label={t('MoveUp')} disabled={i === 0 || moveEntry.isPending} onClick={() => void move(e, -1)} />
                <IconButton
                  icon="chevron-down"
                  size="sm"
                  label={t('MoveDown')}
                  disabled={(i === entries.length - 1 && !query.hasNextPage) || moveEntry.isPending}
                  onClick={() => void move(e, 1)}
                />
                <IconButton icon="trash" size="sm" tone="danger" label={t('TakeOff', { 0: e.name })} onClick={() => void take(e)} />
              </div>
            </li>
          ))}
          {query.hasNextPage && (
            <li ref={sentinel} className={styles.loadMore}>
              {query.isFetchingNextPage ? (
                <Loader label={t('Loading')} />
              ) : (
                <Button variant="ghost" onClick={() => void query.fetchNextPage()}>
                  {t('ShowMore', { 0: formatNumber(section.entryCount - entries.length, 0) })}
                </Button>
              )}
            </li>
          )}
        </ol>
      )}
    </section>
  )
}

/** Calls `onNear` when the returned element comes within a screen of view (and again each time it comes back). */
function useLoadMore(onNear: () => void) {
  const callback = useRef(onNear)
  useEffect(() => {
    callback.current = onNear
  })
  return useCallback((node: HTMLElement | null) => {
    if (!node || typeof IntersectionObserver === 'undefined') return
    const observer = new IntersectionObserver((seen) => seen.some((e) => e.isIntersecting) && callback.current(), { rootMargin: '400px 0px' })
    observer.observe(node)
    return () => observer.disconnect()
  }, [])
}

function EntryThumb({ entry }: { entry: OnlineMenuEntry }) {
  const [failed, setFailed] = useState(false)
  if (entry.kind === 'category') return <span className={styles.thumbCategory} aria-hidden="true">{entry.name.trim().charAt(0).toUpperCase()}</span>
  return entry.image && !failed ? (
    <img className={styles.thumb} src={entry.image} alt="" loading="lazy" onError={() => setFailed(true)} />
  ) : (
    <span className={styles.thumbEmpty} aria-hidden="true" />
  )
}
