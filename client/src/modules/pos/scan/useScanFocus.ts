import { useEffect, type RefObject } from 'react'
import { posBus } from '../slots'

/** Fields where a keystroke belongs to the field: typing into them must never be redirected to the scan box. */
export function isEditable(el: Element | null): boolean {
  if (!el) return false
  const tag = el.tagName
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || (el as HTMLElement).isContentEditable === true
}

/** A dialog (checkout, held carts, price check ...) is open: the page behind it must not grab keys or focus. */
export function dialogIsOpen(doc: Document = document): boolean {
  return doc.querySelector('[role="dialog"], [role="alertdialog"]') !== null
}

/**
 * The cashier's hands are on the scanner, so the scan box should always be where the next keystroke lands (the old POS did this with a
 * pile of jQuery focus hooks). Three rules:
 *  - a printable key pressed while nothing editable has focus moves focus to the scan box first, so the first character of a scan is not lost;
 *  - after a click on the page (a tile, a chip) focus returns to the scan box unless the click went into another field or a dialog;
 *  - when the window is focused again, or a slot calls `posBus.focusScan()`, the same.
 * It never steals focus from another field or from an open dialog.
 */
export function useScanFocus(inputRef: RefObject<HTMLInputElement | null>): void {
  useEffect(() => {
    const focusScan = () => {
      const input = inputRef.current
      if (!input || input.disabled || dialogIsOpen()) return
      if (isEditable(document.activeElement) && document.activeElement !== input) return
      input.focus()
    }

    const onKeyDown = (e: KeyboardEvent) => {
      // Space activates the focused tile / chip, so it is not a scan character.
      if (e.ctrlKey || e.altKey || e.metaKey || e.key.length !== 1 || e.key === ' ') return
      const input = inputRef.current
      if (!input || dialogIsOpen() || isEditable(document.activeElement)) return
      input.focus()
    }

    let timer = 0
    const onClick = (e: MouseEvent) => {
      // Let the click's own handlers run (a tile adds to the cart) before deciding.
      if (e.target instanceof Element && isEditable(e.target.closest('input, textarea, select'))) return
      window.clearTimeout(timer)
      timer = window.setTimeout(focusScan, 0)
    }

    focusScan()
    document.addEventListener('keydown', onKeyDown, true)
    document.addEventListener('click', onClick)
    window.addEventListener('focus', focusScan)
    const unsubscribe = posBus.onFocusScan(focusScan)
    return () => {
      window.clearTimeout(timer)
      document.removeEventListener('keydown', onKeyDown, true)
      document.removeEventListener('click', onClick)
      window.removeEventListener('focus', focusScan)
      unsubscribe()
    }
  }, [inputRef])
}
