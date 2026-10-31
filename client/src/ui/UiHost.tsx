import { ConfirmHost } from './confirm'
import { ContextMenuHost } from './ContextMenu'
import { Toaster } from './toast'

/** Mount once near the app root (0D): hosts the imperative `toast.*`, `confirm()` and context menu. */
export function UiHost() {
  return (
    <>
      <Toaster />
      <ConfirmHost />
      <ContextMenuHost />
    </>
  )
}
