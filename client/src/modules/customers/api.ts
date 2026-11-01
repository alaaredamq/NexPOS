import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { http, type Download } from '@/api/http'

type Schemas = components['schemas']
export type Customer = Schemas['CustomerDto']
export type CustomerList = Schemas['CustomerDtoPagedResult']
export type CustomerSummary = Schemas['CustomerSummaryDto']
export type CustomerDetails = Schemas['CustomerDetailsDto']
export type CustomerOrder = Schemas['CustomerOrderDto']
export type CustomerInput = Schemas['CustomerRequest']
export type DebtTransaction = Schemas['CustomerDebtTransactionDto']
export type DebtTransactionList = Schemas['CustomerDebtTransactionDtoPagedResult']
export type DebtInput = Schemas['CustomerDebtRequest']
export type DebtResult = Schemas['CustomerDebtResultDto']

export interface CustomerListParams {
  page: number
  pageSize: number
  sort: string
  dir: 'asc' | 'desc'
  q: string
}

export const LEDGER_PAGE_SIZE = 10

const root = ['customers'] as const
export const customerKeys = {
  all: root,
  list: (params: CustomerListParams) => [...root, 'list', params] as const,
  summary: [...root, 'summary'] as const,
  detail: (id: number) => [...root, 'detail', id] as const,
  ledger: (id: number, page: number) => [...root, 'ledger', id, page] as const,
}

export function useCustomers(params: CustomerListParams) {
  return useQuery({
    queryKey: customerKeys.list(params),
    queryFn: ({ signal }) => http.get<CustomerList>('/customers', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
  })
}

/** The tiles, and the default currency's notation (the unit of every balance). */
export function useCustomerSummary(enabled = true) {
  return useQuery({
    queryKey: customerKeys.summary,
    queryFn: ({ signal }) => http.get<CustomerSummary>('/customers/summary', { signal }),
    enabled,
  })
}

export function useCustomer(id: number) {
  return useQuery({ queryKey: customerKeys.detail(id), queryFn: ({ signal }) => http.get<CustomerDetails>(`/customers/${id}`, { signal }) })
}

export function useDebtTransactions(id: number, page: number) {
  return useQuery({
    queryKey: customerKeys.ledger(id, page),
    queryFn: ({ signal }) => http.get<DebtTransactionList>(`/customers/${id}/debt-transactions`, { query: { page, pageSize: LEDGER_PAGE_SIZE }, signal }),
    placeholderData: keepPreviousData,
  })
}

/** Create (no `id`) or update (with `id`): lists, tiles and details of customers are refetched afterwards. */
export function useSaveCustomer() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, input }: { id?: number; input: CustomerInput }) =>
      id === undefined ? http.post<Customer>('/customers', input) : http.put<Customer>(`/customers/${id}`, input),
    onSuccess: () => client.invalidateQueries({ queryKey: customerKeys.all }),
  })
}

export function useDeleteCustomer() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => http.delete<void>(`/customers/${id}`),
    onSuccess: () => client.invalidateQueries({ queryKey: customerKeys.all }),
  })
}

/** Records a manual debt entry. Everything that shows a balance is refetched: the list, the tiles, the details and the ledger. */
export function useAddDebtTransaction() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ customerId, input }: { customerId: number; input: DebtInput }) =>
      http.post<DebtResult>(`/customers/${customerId}/debt-transactions`, input),
    onSuccess: () => client.invalidateQueries({ queryKey: customerKeys.all }),
  })
}

export const customersApi = {
  export: (): Promise<Download> => http.download('/customers/export'),
}
