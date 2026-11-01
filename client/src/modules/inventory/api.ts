import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { http } from '@/api/http'
import { productKeys, saveDownload } from '../products/api'

type Schemas = components['schemas']
export type InventoryItem = Schemas['InventoryItemDto']
export type InventoryList = Schemas['InventoryItemDtoPagedResult']
export type InventorySummary = Schemas['InventorySummaryDto']
export type StockAdjustmentInput = Schemas['StockAdjustmentRequest']
export type StockAdjustmentResult = Schemas['StockAdjustmentResultDto']
export type LowStockReport = Schemas['LowStockReportDto']
export type LowStockItem = Schemas['LowStockItemDto']
export type StockValueReport = Schemas['StockValueReportDto']
export type StockValueItem = Schemas['StockValueItemDto']
export type PrintResult = Schemas['InventoryPrintResultDto']

/** What the server decided about a product's stock; the browser never re-derives it. */
export type InventoryStatus = 'out' | 'low' | 'ok'
export type LowStockPriority = 'critical' | 'high' | 'medium'
export type ReportName = 'low-stock' | 'stock-value'

export interface InventoryListParams {
  page: number
  pageSize: number
  sort: string
  dir: 'asc' | 'desc'
  q: string
}

const root = ['inventory'] as const
export const inventoryKeys = {
  all: root,
  list: (params: InventoryListParams) => [...root, 'list', params] as const,
  summary: [...root, 'summary'] as const,
  lowStock: (params: InventoryListParams) => [...root, 'low-stock', params] as const,
  stockValue: (params: InventoryListParams) => [...root, 'stock-value', params] as const,
}

export function useInventory(params: InventoryListParams) {
  return useQuery({
    queryKey: inventoryKeys.list(params),
    queryFn: ({ signal }) => http.get<InventoryList>('/inventory', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
  })
}

export function useInventorySummary() {
  return useQuery({ queryKey: inventoryKeys.summary, queryFn: ({ signal }) => http.get<InventorySummary>('/inventory/summary', { signal }) })
}

export function useLowStockReport(params: InventoryListParams) {
  return useQuery({
    queryKey: inventoryKeys.lowStock(params),
    queryFn: ({ signal }) => http.get<LowStockReport>('/inventory/reports/low-stock', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
  })
}

export function useStockValueReport(params: InventoryListParams) {
  return useQuery({
    queryKey: inventoryKeys.stockValue(params),
    queryFn: ({ signal }) => http.get<StockValueReport>('/inventory/reports/stock-value', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
  })
}

/** Stock moved: the list, both reports and the tiles are refetched, and so is the products module (it shows stock too). */
function useInvalidateStock() {
  const client = useQueryClient()
  return () => Promise.all([client.invalidateQueries({ queryKey: root }), client.invalidateQueries({ queryKey: productKeys.all })])
}

export function useAdjustStock() {
  const invalidate = useInvalidateStock()
  return useMutation({
    mutationFn: ({ productId, input }: { productId: number; input: StockAdjustmentInput }) =>
      http.post<StockAdjustmentResult>(`/inventory/${productId}/adjust`, input),
    onSuccess: () => invalidate(),
  })
}

export function useSetMinimumStock() {
  const invalidate = useInvalidateStock()
  return useMutation({
    mutationFn: ({ productId, minimumStock }: { productId: number; minimumStock: number }) =>
      http.put<InventoryItem>(`/inventory/${productId}/minimum-stock`, { minimumStock }),
    onSuccess: () => invalidate(),
  })
}

/** Sends the report to the receipt printer. A printer problem is an answer (`printed: false`), not an error. */
export const printReport = (report: ReportName) => http.post<PrintResult>(`/inventory/reports/${report}/print`)

export async function downloadReportPdf(report: ReportName): Promise<void> {
  saveDownload(await http.download(`/inventory/reports/${report}.pdf`), `${report}.pdf`)
}
