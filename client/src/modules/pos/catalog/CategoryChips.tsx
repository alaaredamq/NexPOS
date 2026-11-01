import { useTranslation } from 'react-i18next'
import { categoryColor } from '../../_shared/categoryPalette'
import type { PosCategory } from './api'
import styles from './catalog.module.css'

interface CategoryChipsProps {
  categories: readonly PosCategory[]
  /** `null` is the main screen. */
  selected: number | null
  onSelect: (categoryId: number | null) => void
  /** Whether the main screen is the shop's configured display order (label "Main screen") or simply every product (label "All products"). */
  mainScreenConfigured: boolean
}

/** One scrollable row of category filters. Each chip carries the category's palette dot so it matches the tiles it filters to. */
export function CategoryChips({ categories, selected, onSelect, mainScreenConfigured }: CategoryChipsProps) {
  const { t } = useTranslation('pos')

  return (
    <div className={styles.chips} role="group" aria-label={t('category')}>
      <button type="button" className={styles.chip} data-active={selected === null} aria-pressed={selected === null} onClick={() => onSelect(null)}>
        {mainScreenConfigured ? t('MainScreen') : t('AllProducts')}
      </button>
      {categories.map((category) => (
        <button
          key={category.id}
          type="button"
          className={styles.chip}
          data-active={selected === category.id}
          aria-pressed={selected === category.id}
          onClick={() => onSelect(category.id)}
        >
          <span className={styles.chipDot} style={{ background: categoryColor(category.name) }} aria-hidden="true" />
          <span className={styles.chipText}>{category.name}</span>
          <span className={styles.chipCount}>{category.productCount}</span>
        </button>
      ))}
    </div>
  )
}
