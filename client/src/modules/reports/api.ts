import { keepPreviousData, useQuery } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { http } from '@/api/http'

type Schemas = components['schemas']
export type ReportsSummary = Schemas['ReportsSummaryDto']
export type SalesReport = Schemas['SalesReportDto']
export type SalesReportDay = Schemas['SalesReportDayDto']
export type SalesReportProduct = Schemas['SalesReportProductDto']
export type SalesReportPayment = Schemas['SalesReportPaymentDto']

export interface Range {
  /** Local days, `yyyy-mm-dd`. */
  from: string
  to: string
}

export const reportKeys = {
  summary: ['reports', 'summary'] as const,
  sales: (range: Range) => ['reports', 'sales', range] as const,
}

export function useReportsSummary() {
  return useQuery({ queryKey: reportKeys.summary, queryFn: ({ signal }) => http.get<ReportsSummary>('/reports/summary', { signal }) })
}

export function useSalesReport(range: Range) {
  return useQuery({
    queryKey: reportKeys.sales(range),
    queryFn: ({ signal }) => http.get<SalesReport>('/reports/sales', { query: { ...range }, signal }),
    enabled: range.from !== '' && range.to !== '',
    placeholderData: keepPreviousData,
  })
}

export const reportsApi = {
  csv: (range: Range) => http.download('/reports/sales.csv', { query: { ...range } }),
  pdf: (range: Range) => http.download('/reports/sales.pdf', { query: { ...range } }),
}
