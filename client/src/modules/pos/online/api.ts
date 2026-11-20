import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { http } from '@/api/http'
import type { PosProduct } from '../catalog/api'

/** One dish of an online order, as the cloud priced it. `posProductId` is this shop's product (null for a dish made in the menu editor). */
export interface OnlineOrderLine {
  name: string
  quantity: number
  unitPrice: number
  currency: string
  posProductId: number | null
  total: string
}

/** An order a guest sent from the shop's online menu to this till. */
export interface OnlineOrder {
  id: number
  number: number
  status: 'new' | 'accepted' | 'ready' | 'done' | 'rejected'
  branch: string | null
  orderType: string | null
  name: string
  phone: string | null
  place: string | null
  note: string | null
  createdAtUtc: string
  lines: OnlineOrderLine[]
  total: string
}

export interface OnlineOrders {
  /** This PC is connected to Coduo POS Online (otherwise orders cannot reach it). */
  connected: boolean
  /** The cloud answered just now. */
  reachable: boolean
  orders: OnlineOrder[]
}

export type OnlineOrderMove = 'accepted' | 'ready' | 'done' | 'rejected'

const key = ['pos', 'online-orders'] as const

/** The open orders, asked again every 15 seconds while the till is open (a new one shows up without anyone looking). */
export function useOnlineOrders() {
  return useQuery({
    queryKey: key,
    queryFn: ({ signal }) => http.get<OnlineOrders>('/online-orders', { signal }),
    refetchInterval: 15_000,
    refetchIntervalInBackground: true,
    staleTime: 5_000,
  })
}

export function useMoveOnlineOrder() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, status }: { id: number; status: OnlineOrderMove }) => http.put<void>(`/online-orders/${id}/status`, { status }),
    onSettled: () => queryClient.invalidateQueries({ queryKey: key }),
  })
}

/** The POS product behind an order line, priced and stocked as the till sees it now. */
export function fetchPosProduct(id: number): Promise<PosProduct> {
  return http.get<PosProduct>(`/pos/products/${id}`)
}

/** A till customer whose phone is the number an online guest gave. */
export interface MatchedCustomer {
  id: number
  name: string
  phoneNumber?: string | null
  balance: number
}

/** The till's customers with this phone number, however either is written ("+961 70 123 456" finds "70123456"). */
export function findCustomersByPhone(phone: string): Promise<MatchedCustomer[]> {
  return http.get<MatchedCustomer[]>('/pos/customers/by-phone', { query: { phone } })
}
