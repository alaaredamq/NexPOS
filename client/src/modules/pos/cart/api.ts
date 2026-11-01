import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { http } from '@/api/http'
import { queryClient } from '@/api/queryClient'
import type { components } from '@/api/schema'
import type { PosCartInput, PosCurrency, PosPricedCart, PosProduct } from '../store/cartStore'

type Schemas = components['schemas']
export type PosDiscount = Schemas['PosDiscountDto']
export type PosPriceOptions = Schemas['PosPriceOptionsDto']
export type PosPriceOption = Schemas['PosPriceOptionDto']
export type HeldCart = Schemas['HeldCartDto']
export type ResumedCart = Schemas['ResumedCartDto']
export type ResumedLine = Schemas['ResumedLineDto']
export type HoldCartRequest = Schemas['PosHoldCartRequest']

export const posKeys = {
  currencies: ['pos', 'currencies'] as const,
  discounts: ['pos', 'discounts'] as const,
  product: (id: number) => ['pos', 'product', id] as const,
  priceOptions: (productId: number, currencyId: number) => ['pos', 'priceOptions', productId, currencyId] as const,
  price: (cartKey: string) => ['pos', 'price', cartKey] as const,
  heldCarts: ['pos', 'heldCarts'] as const,
}

/** Active currencies with their rates. Nearly static, so one fetch serves the whole shift. */
export function useCurrencies() {
  return useQuery({
    queryKey: posKeys.currencies,
    queryFn: ({ signal }) => http.get<PosCurrency[]>('/pos/currencies', { signal }),
    staleTime: 5 * 60_000,
  })
}

export function fetchCurrencies(): Promise<PosCurrency[]> {
  return queryClient.fetchQuery({
    queryKey: posKeys.currencies,
    queryFn: ({ signal }) => http.get<PosCurrency[]>('/pos/currencies', { signal }),
    staleTime: 5 * 60_000,
  })
}

/** The shop's stored discounts (the "preset" choice of the discount dialog). */
export function useDiscounts(enabled = true) {
  return useQuery({
    queryKey: posKeys.discounts,
    queryFn: ({ signal }) => http.get<PosDiscount[]>('/pos/discounts', { signal }),
    staleTime: 60_000,
    enabled,
  })
}

export function fetchProduct(id: number): Promise<PosProduct> {
  return queryClient.fetchQuery({
    queryKey: posKeys.product(id),
    queryFn: ({ signal }) => http.get<PosProduct>(`/pos/products/${id}`, { signal }),
    staleTime: 30_000,
  })
}

/** The prices a cashier can choose for a product, converted into the cart currency. Fetched when the picker opens, always fresh. */
export function usePriceOptions(productId: number, currencyId: number) {
  return useQuery({
    queryKey: posKeys.priceOptions(productId, currencyId),
    queryFn: ({ signal }) => http.get<PosPriceOptions>(`/pos/products/${productId}/prices`, { query: { currencyId }, signal }),
    staleTime: 0,
    gcTime: 0,
  })
}

/** The server's price of a cart. A POST because the cart is the body; it writes nothing. */
export function usePricedCart(input: PosCartInput | null, key: string) {
  return useQuery({
    queryKey: posKeys.price(key),
    queryFn: ({ signal }) => http.post<PosPricedCart>('/pos/price', input, { signal }),
    enabled: input !== null,
    placeholderData: keepPreviousData,
    // Prices change when someone edits a product or a rate: re-ask when the cashier comes back to the window.
    staleTime: 30_000,
    gcTime: 60_000,
  })
}

export function useHeldCarts(enabled = true) {
  return useQuery({
    queryKey: posKeys.heldCarts,
    queryFn: ({ signal }) => http.get<HeldCart[]>('/carts', { signal }),
    enabled,
    staleTime: 0,
    gcTime: 0,
  })
}

export function useHoldCart() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (request: HoldCartRequest) => http.post<HeldCart>('/carts', request),
    onSuccess: () => client.invalidateQueries({ queryKey: posKeys.heldCarts }),
  })
}

export function useDeleteHeldCart() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => http.delete<void>(`/carts/${id}`),
    onSuccess: () => client.invalidateQueries({ queryKey: posKeys.heldCarts }),
  })
}

export function useDeleteAllHeldCarts() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => http.delete<Schemas['HeldCartsDeletedDto']>('/carts'),
    onSuccess: () => client.invalidateQueries({ queryKey: posKeys.heldCarts }),
  })
}

export function fetchHeldCart(id: number): Promise<ResumedCart> {
  return http.get<ResumedCart>(`/carts/${id}`)
}
