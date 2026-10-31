/**
 * Icons are single stroke paths on a 24-grid, same idiom as the nav rail — a handful of glyphs
 * isn't worth an icon package, and keeping the paths in one map means every icon in the app shares
 * a stroke weight and cap style instead of drifting per component.
 *
 * Direction-implying glyphs (`chevron-end`, `arrow-end`) are drawn pointing toward the inline end
 * and mirror themselves under `dir="rtl"` (see `.flip` in Icon.module.css), so call sites never
 * branch on language.
 */
import styles from './Icon.module.css'

export const ICON_PATHS = {
  edit: 'M4 20h4L18.5 9.5a2.12 2.12 0 00-3-3L5 17v3z M13.5 6.5l4 4',
  trash: 'M4 7h16 M10 4h4 M6.5 7l.9 13h9.2l.9-13 M10 11v5.5 M14 11v5.5',
  print: 'M7.5 8.5V3.5h9v5 M7.5 17.5H5.5a2 2 0 01-2-2v-4a2 2 0 012-2h13a2 2 0 012 2v4a2 2 0 01-2 2h-2 M7.5 14.5h9v6h-9z',
  cart: 'M2.5 4h2.3l2.1 10.1a1.5 1.5 0 001.5 1.2h8.4a1.5 1.5 0 001.5-1.2L20 8H6 M9.7 20.5a1.2 1.2 0 100-2.4 1.2 1.2 0 000 2.4 M16.7 20.5a1.2 1.2 0 100-2.4 1.2 1.2 0 000 2.4',
  check: 'M4.5 12.5l5 5 10-11',
  close: 'M6 6l12 12 M18 6L6 18',
  plus: 'M12 5v14 M5 12h14',
  minus: 'M5 12h14',
  search: 'M10.5 17a6.5 6.5 0 100-13 6.5 6.5 0 000 13z M15.5 15.5L20 20',
  'chevron-down': 'M6 9.5l6 6 6-6',
  'chevron-up': 'M6 14.5l6-6 6 6',
  'chevron-end': 'M9.5 6l6 6-6 6',
  'chevron-start': 'M14.5 6l-6 6 6 6',
  'arrow-end': 'M5 12h14 M13 6l6 6-6 6',
  'sort-asc': 'M12 19V6 M6.5 11.5L12 6l5.5 5.5',
  'sort-desc': 'M12 5v13 M6.5 12.5L12 18l5.5-5.5',
  columns: 'M4 5h16v14H4z M9.5 5v14 M14.5 5v14',
  calendar: 'M4 6.5h16V20H4z M4 10.5h16 M8 4v4 M16 4v4',
  info: 'M12 21a9 9 0 100-18 9 9 0 000 18z M12 11v5 M12 7.8v.2',
  warning: 'M12 4l9 16H3z M12 10v4.5 M12 17.3v.2',
  error: 'M12 21a9 9 0 100-18 9 9 0 000 18z M9 9l6 6 M15 9l-6 6',
  more: 'M5 12h.01 M12 12h.01 M19 12h.01',
} as const

export type IconName = keyof typeof ICON_PATHS

/** Glyphs that point along the reading direction and therefore mirror in RTL. */
const DIRECTIONAL = new Set<IconName>(['chevron-end', 'chevron-start', 'arrow-end'])

interface IconProps {
  name: IconName
  size?: number
}

export function Icon({ name, size = 16 }: IconProps) {
  return (
    <svg
      className={DIRECTIONAL.has(name) ? styles.flip : undefined}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      <path d={ICON_PATHS[name]} />
    </svg>
  )
}
