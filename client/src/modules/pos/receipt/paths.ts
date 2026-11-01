/** The print / PDF endpoints of a sale (API paths without the `/api/v1` prefix). The server prints and builds the PDF (D11). */
export function salePaths(orderId: number) {
  return { print: `/pos/sales/${orderId}/print`, pdf: `/pos/sales/${orderId}/receipt.pdf` }
}
