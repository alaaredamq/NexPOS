import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { http } from '@/api/http'

type Schemas = components['schemas']
export type Wallet = Schemas['WalletDto']
export type WalletBalance = Schemas['WalletBalanceDto']
export type WalletRow = Schemas['WalletTransactionDto']
export type WalletRowsPage = Schemas['WalletTransactionDtoPagedResult']
export type MovementInput = Schemas['WalletMovementRequest']

/** The kinds of movement a person records by hand. Sale and SaleReversal only come from checkout and the void. */
export const MANUAL_TYPES = ['Deposit', 'Withdrawal', 'SupplierPayment', 'Fee', 'Adjustment'] as const
export type ManualType = (typeof MANUAL_TYPES)[number]
export const ALL_TYPES = ['Sale', 'SaleReversal', ...MANUAL_TYPES] as const

export interface RowsParams {
  page: number
  pageSize: number
  /** `yyyy-mm-dd` local days, both included; `''` is open-ended. */
  from: string
  to: string
  /** `''`, `checked` or `unchecked`. */
  status: string
  type: string
  q: string
}

// 'wallets' is in LIVE_ROOTS: a Whish sale on another till refreshes the balance here.
const root = ['wallets'] as const
export const walletKeys = {
  all: root,
  list: [...root, 'list'] as const,
  rows: (id: number, params: RowsParams) => [...root, 'rows', id, params] as const,
}

export function useWallets() {
  return useQuery({ queryKey: walletKeys.list, queryFn: ({ signal }) => http.get<Wallet[]>('/wallets', { signal }) })
}

export function useWalletRows(id: number | null, params: RowsParams) {
  return useQuery({
    queryKey: walletKeys.rows(id ?? 0, params),
    enabled: id !== null,
    queryFn: ({ signal }) =>
      http.get<WalletRowsPage>(`/wallets/${id}/transactions`, {
        query: {
          page: params.page,
          pageSize: params.pageSize,
          from: params.from || undefined,
          to: params.to || undefined,
          status: params.status || undefined,
          type: params.type || undefined,
          q: params.q || undefined,
        },
        signal,
      }),
    placeholderData: keepPreviousData,
  })
}

function useWalletMutation<TVars, TResult = unknown>(fn: (vars: TVars) => Promise<TResult>) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: walletKeys.all })
      // Turning Whish on or off changes what the till's payment dialog offers.
      void client.invalidateQueries({ queryKey: ['pos'] })
    },
  })
}

export const useSetUpWhish = () =>
  useWalletMutation((input: { accountNumber: string | null }) => http.post<Wallet>('/wallets/whish', input))

export const useUpdateWallet = () =>
  useWalletMutation(({ id, ...input }: { id: number; name: string; accountNumber: string | null; isActive: boolean }) =>
    http.put<Wallet>(`/wallets/${id}`, input),
  )

export const useSetOnline = () =>
  useWalletMutation(({ id, ...input }: { id: number; mode: string; channel: string | null; secret: string | null; websiteUrl: string | null }) =>
    http.put<Wallet>(`/wallets/${id}/online`, input),
  )

export const useAddMovement = () =>
  useWalletMutation(({ id, ...input }: MovementInput & { id: number }) => http.post<WalletRow>(`/wallets/${id}/transactions`, input))

export const useSetChecked = () =>
  useWalletMutation(({ id, ids, checked }: { id: number; ids: number[]; checked: boolean }) =>
    http.post<{ changed: number }>(`/wallets/${id}/transactions/check`, { ids, checked }),
  )
