import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { http } from '@/api/http'
import { saveDownload } from '../products/api'

type Schemas = components['schemas']
export type Supplier = Schemas['SupplierDto']
export type SupplierList = Schemas['SupplierDtoPagedResult']
export type SupplierSummary = Schemas['SupplierSummaryDto']
export type SupplierOptions = Schemas['SupplierOptionsDto']
export type SupplierInput = Schemas['SupplierRequest']
export type DebtEntry = Schemas['SupplierDebtEntryDto']
export type DebtLedger = Schemas['SupplierDebtEntryDtoPagedResult']
export type DebtInput = Schemas['SupplierDebtRequest']
export type DebtResult = Schemas['SupplierDebtResultDto']

export type DebtKind = 'purchase' | 'payment' | 'adjustment'

export interface SupplierListParams {
  page: number
  pageSize: number
  sort: string
  dir: 'asc' | 'desc'
  q: string
}

const root = ['suppliers'] as const
export const supplierKeys = {
  all: root,
  list: (params: SupplierListParams) => [...root, 'list', params] as const,
  summary: [...root, 'summary'] as const,
  options: [...root, 'options'] as const,
  detail: (id: number) => [...root, 'detail', id] as const,
  ledger: (id: number, page: number, pageSize: number) => [...root, 'ledger', id, page, pageSize] as const,
}

export function useSuppliers(params: SupplierListParams) {
  return useQuery({
    queryKey: supplierKeys.list(params),
    queryFn: ({ signal }) => http.get<SupplierList>('/suppliers', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
  })
}

export function useSupplierSummary() {
  return useQuery({ queryKey: supplierKeys.summary, queryFn: ({ signal }) => http.get<SupplierSummary>('/suppliers/summary', { signal }) })
}

export function useSupplierOptions() {
  return useQuery({ queryKey: supplierKeys.options, queryFn: ({ signal }) => http.get<SupplierOptions>('/suppliers/options', { signal }), staleTime: 5 * 60_000 })
}

export function useSupplier(id: number) {
  return useQuery({ queryKey: supplierKeys.detail(id), queryFn: ({ signal }) => http.get<Supplier>(`/suppliers/${id}`, { signal }) })
}

export function useSupplierLedger(id: number, page: number, pageSize: number) {
  return useQuery({
    queryKey: supplierKeys.ledger(id, page, pageSize),
    queryFn: ({ signal }) => http.get<DebtLedger>(`/suppliers/${id}/debt-transactions`, { query: { page, pageSize }, signal }),
    placeholderData: keepPreviousData,
  })
}

/** Create (no `id`) or update (with `id`): every list, tile and details page of suppliers is refetched afterwards. */
export function useSaveSupplier() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, input }: { id?: number; input: SupplierInput }) =>
      id === undefined ? http.post<Supplier>('/suppliers', input) : http.put<Supplier>(`/suppliers/${id}`, input),
    onSuccess: () => client.invalidateQueries({ queryKey: root }),
  })
}

export function useDeleteSupplier() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => http.delete<void>(`/suppliers/${id}`),
    onSuccess: () => client.invalidateQueries({ queryKey: root }),
  })
}

/** A debt entry changes the balance, so lists, tiles, details and the ledger all refetch. Never optimistic: it is money. */
export function useAddDebt() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ supplierId, input }: { supplierId: number; input: DebtInput }) =>
      http.post<DebtResult>(`/suppliers/${supplierId}/debt-transactions`, input),
    onSuccess: () => client.invalidateQueries({ queryKey: root }),
  })
}

export async function downloadSuppliersCsv(): Promise<void> {
  saveDownload(await http.download('/suppliers/export'), 'suppliers.csv')
}


/** Which way a balance points: the shop owes the supplier, the supplier owes the shop, or nothing is owed. */
export const balanceSign = (balance: number) => (balance > 0 ? 'owe' : balance < 0 ? 'owed' : 'zero')
