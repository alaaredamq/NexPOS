import { useEffect, useId, useRef, useState, type MouseEvent, type PointerEvent, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { IconButton } from './IconButton'
import { useTranslation } from 'react-i18next'
import styles from './Modal.module.css'

interface ModalProps {
  onClose: () => void
  width?: number
  /** Accessible name. Pass `title` for the built-in header, or `aria-label` when the body supplies its own heading. */
  title?: string
  'aria-label'?: string
  /** Hides the header close button (confirm dialogs and PIN pads have their own Cancel). */
  hideClose?: boolean
  children: ReactNode
}

const FOCUSABLE =
  'a[href],button:not([disabled]),input:not([disabled]):not([type="hidden"]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])'

/**
 * Dialog with a focus trap, Escape-to-close, focus restore and `aria-modal`. Portalled to <body>:
 * mounted in place, an overlay opened from inside a clipped/transformed ancestor would take that
 * ancestor as its containing block and get clipped to it instead of the viewport.
 */
export function Modal({ onClose, width = 680, title, 'aria-label': ariaLabel, hideClose, children }: ModalProps) {
  const { t } = useTranslation()
  const titleId = useId()
  const cardRef = useRef<HTMLDivElement>(null)
  // Track where the press started so a drag that ends on the overlay
  // (e.g. selecting text in an input and releasing outside) doesn't close.
  const pressedOnOverlay = useRef(false)
  const onCloseRef = useRef(onClose)
  useEffect(() => {
    onCloseRef.current = onClose
  })

  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null
    const card = cardRef.current
    if (card && !card.contains(document.activeElement)) {
      // Prefer the first field over the header's close button.
      const target = card.querySelector<HTMLElement>('[data-autofocus]') ?? card.querySelector<HTMLElement>(FOCUSABLE) ?? card
      target.focus()
    }

    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        // A nested popup (select list, context menu) closes itself first.
        if (e.defaultPrevented) return
        e.stopPropagation()
        onCloseRef.current()
        return
      }
      if (e.key !== 'Tab' || !card) return
      const items = Array.from(card.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(isVisible)
      if (items.length === 0) {
        e.preventDefault()
        return
      }
      const first = items[0]!
      const last = items[items.length - 1]!
      if (e.shiftKey && (document.activeElement === first || !card.contains(document.activeElement))) {
        e.preventDefault()
        last.focus()
      } else if (!e.shiftKey && (document.activeElement === last || !card.contains(document.activeElement))) {
        e.preventDefault()
        first.focus()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      previouslyFocused?.focus?.()
    }
  }, [])

  // Dragging by the header: the card stays centred by the overlay's flexbox and is shifted with a transform,
  // so it opens in the middle every time and a drag never fights the layout.
  const [offset, setOffset] = useState({ x: 0, y: 0 })
  const drag = useRef<{ pointerId: number; startX: number; startY: number; base: { x: number; y: number }; rect: DOMRect } | null>(null)

  const handleHeadPointerDown = (e: PointerEvent<HTMLDivElement>) => {
    if (e.button !== 0 || !cardRef.current) return
    // The close button (or anything else clickable in the header) keeps working as a click.
    if ((e.target as HTMLElement).closest('button, a, input, select, textarea')) return
    e.preventDefault()
    e.currentTarget.setPointerCapture(e.pointerId)
    drag.current = { pointerId: e.pointerId, startX: e.clientX, startY: e.clientY, base: offset, rect: cardRef.current.getBoundingClientRect() }
  }

  const handleHeadPointerMove = (e: PointerEvent<HTMLDivElement>) => {
    const d = drag.current
    if (!d || d.pointerId !== e.pointerId) return
    // Keep the header on screen: at least KEEP_VISIBLE px of the card sideways, and the header's top inside the window.
    const dx = clamp(e.clientX - d.startX, KEEP_VISIBLE - d.rect.right, window.innerWidth - KEEP_VISIBLE - d.rect.left)
    const dy = clamp(e.clientY - d.startY, -d.rect.top, window.innerHeight - HEAD_VISIBLE - d.rect.top)
    setOffset({ x: d.base.x + dx, y: d.base.y + dy })
  }

  const handleHeadPointerUp = (e: PointerEvent<HTMLDivElement>) => {
    if (drag.current?.pointerId !== e.pointerId) return
    drag.current = null
    if (e.currentTarget.hasPointerCapture(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId)
  }

  const handleMouseDown = (e: MouseEvent<HTMLDivElement>) => {
    pressedOnOverlay.current = e.target === e.currentTarget
  }

  const handleMouseUp = (e: MouseEvent<HTMLDivElement>) => {
    if (pressedOnOverlay.current && e.target === e.currentTarget) onClose()
    pressedOnOverlay.current = false
  }

  return createPortal(
    <div className={styles.overlay} onMouseDown={handleMouseDown} onMouseUp={handleMouseUp}>
      <div
        ref={cardRef}
        className={styles.card}
        style={{ inlineSize: width, transform: offset.x || offset.y ? `translate(${offset.x}px, ${offset.y}px)` : undefined }}
        role="dialog"
        aria-modal="true"
        aria-labelledby={title ? titleId : undefined}
        aria-label={title ? undefined : ariaLabel}
        tabIndex={-1}
      >
        {title && (
          <div
            className={styles.head}
            onPointerDown={handleHeadPointerDown}
            onPointerMove={handleHeadPointerMove}
            onPointerUp={handleHeadPointerUp}
            onPointerCancel={handleHeadPointerUp}
          >
            <h2 id={titleId} className={styles.title}>
              {title}
            </h2>
            {!hideClose && <IconButton icon="close" label={t('common.close', 'Close')} size="sm" onClick={onClose} />}
          </div>
        )}
        {children}
      </div>
    </div>,
    document.body,
  )
}

/** Scrollable middle of a dialog. */
export function ModalBody({ children }: { children: ReactNode }) {
  return <div className={styles.body}>{children}</div>
}

/** Action row; put the primary action last — it sits at the inline end in both directions. */
export function ModalFooter({ children }: { children: ReactNode }) {
  return <div className={styles.footer}>{children}</div>
}

/** How much of a dragged dialog must stay inside the window sideways, so it can always be grabbed back. */
const KEEP_VISIBLE = 120
/** How much of the header must stay above the window's bottom edge. */
const HEAD_VISIBLE = 48

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max)
}

function isVisible(el: HTMLElement): boolean {
  const style = getComputedStyle(el)
  return style.display !== 'none' && style.visibility !== 'hidden' && !el.hidden
}
