import { create } from 'zustand'
import { ConfirmDialog } from './ConfirmDialog'

export interface ConfirmOptions {
  title: string
  message: string
  detail?: string
  confirmLabel: string
  cancelLabel?: string
  danger?: boolean
  /**
   * Optional work to run while the dialog stays open (spinner on the button; a thrown error shows inside
   * the dialog). Without it `confirm()` resolves as soon as the user confirms.
   */
  onConfirm?: () => void | Promise<void>
}

interface Pending extends ConfirmOptions {
  resolve: (ok: boolean) => void
}

const useConfirmStore = create<{ pending: Pending | null }>(() => ({ pending: null }))

/**
 * Promise-based replacement for SweetAlert's `Swal.fire({showCancelButton}).then(r => r.isConfirmed)`:
 *
 *   if (await confirm({ title, message, confirmLabel, danger: true })) deleteIt()
 *
 * Callable from anywhere (no hook needed); requires `<ConfirmHost />` to be mounted once (see UiHost).
 * A second call while one is open cancels the first.
 */
export function confirm(options: ConfirmOptions): Promise<boolean> {
  return new Promise<boolean>((resolve) => {
    useConfirmStore.getState().pending?.resolve(false)
    useConfirmStore.setState({ pending: { ...options, resolve } })
  })
}

export function ConfirmHost() {
  const pending = useConfirmStore((s) => s.pending)
  if (!pending) return null

  const settle = (ok: boolean) => {
    pending.resolve(ok)
    useConfirmStore.setState({ pending: null })
  }

  return (
    <ConfirmDialog
      title={pending.title}
      message={pending.message}
      detail={pending.detail}
      confirmLabel={pending.confirmLabel}
      cancelLabel={pending.cancelLabel}
      danger={pending.danger}
      onConfirm={async () => {
        await pending.onConfirm?.()
        settle(true)
      }}
      onCancel={() => settle(false)}
    />
  )
}
