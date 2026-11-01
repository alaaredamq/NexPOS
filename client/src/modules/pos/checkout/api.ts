import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { http } from '@/api/http'
import type { components } from '@/api/schema'
import { useRefreshSession } from '@/core/auth/useSession'
import { rememberCustomer } from './customerNames'

type Schemas = components['schemas']
export type PosPaymentMethod = Schemas['PosPaymentMethodDto']
export type PosCustomer = Schemas['PosCustomerDto']
export type PosCustomerPage = Schemas['PosCustomerPageDto']
export type PosCheckoutRequest = Schemas['PosCheckoutRequest']
export type PosSale = Schemas['PosSaleDto']
export type PosSaleSummary = Schemas['PosSaleSummaryDto']
export type PosQuickCustomerRequest = Schemas['PosQuickCustomerRequest']

export const checkoutKeys = {
  paymentMethods: ['pos', 'paymentMethods'] as const,
  recentSales: ['pos', 'recentSales'] as const,
}

/** The ways to pay at the till (the system "Customer Debt" method is not among them: that is `addToDebt`). */
export function usePaymentMethods() {
  return useQuery({
    queryKey: checkoutKeys.paymentMethods,
    queryFn: ({ signal }) => http.get<PosPaymentMethod[]>('/pos/payment-methods', { signal }),
    staleTime: 60_000,
  })
}

/** The first page of customers matching `q` (name). The picker narrows by typing; the server returns at most 50 per page. */
export async function searchCustomers(q: string, pageSize = 30): Promise<PosCustomer[]> {
  const page = await http.get<PosCustomerPage>('/pos/customers', { query: { q, page: 1, pageSize } })
  for (const customer of page.items) rememberCustomer(customer.id, customer.name)
  return page.items
}

export function useQuickCreateCustomer() {
  return useMutation({
    mutationFn: (request: PosQuickCustomerRequest) => http.post<PosCustomer>('/pos/customers', request),
    onSuccess: (customer) => rememberCustomer(customer.id, customer.name),
  })
}

/** The sale. Resolves with the server's document; rejects with `ApiError` (`code` says why: see P1.md). Not a mutation hook: the dialog owns the retry flow. */
export function postCheckout(request: PosCheckoutRequest): Promise<PosSale> {
  return http.post<PosSale>('/pos/sales/checkout', request)
}

/** The signed-in cashier's latest sales (needs Orders.View). Always fresh when the dialog opens. */
export function useRecentSales(enabled = true) {
  return useQuery({
    queryKey: checkoutKeys.recentSales,
    queryFn: ({ signal }) => http.get<PosSaleSummary[]>('/pos/sales/recent', { query: { count: 10 }, signal }),
    enabled,
    staleTime: 0,
    gcTime: 0,
  })
}

/** Everything a finished sale changes on screen: stock on the tiles, the drawer chip, the customer's balance, the sales lists. */
export function useAfterSale() {
  const client = useQueryClient()
  const refreshSession = useRefreshSession()
  return () => {
    void client.invalidateQueries({ queryKey: ['pos', 'catalog'] })
    void client.invalidateQueries({ queryKey: ['pos', 'heldCarts'] })
    void client.invalidateQueries({ queryKey: ['customers'] })
    void client.invalidateQueries({ queryKey: ['orders'] })
    void client.invalidateQueries({ queryKey: ['receipts'] })
    void client.invalidateQueries({ queryKey: ['products'] })
    void refreshSession()
  }
}
