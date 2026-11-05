// Whether the "Sale Complete!" dialog opens after a sale. A per-computer choice (several tills can share one database), remembered in this
// browser's storage like the sticker printer; on unless switched off.
const KEY = 'nexpos-show-sale-complete'

export function getShowSaleComplete(): boolean {
  try {
    return localStorage.getItem(KEY) !== '0'
  } catch {
    return true
  }
}

export function setShowSaleComplete(show: boolean): void {
  try {
    localStorage.setItem(KEY, show ? '1' : '0')
  } catch {
    // storage blocked: not remembered
  }
}
