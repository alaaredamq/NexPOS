import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { http } from '@/api/http'

type Schemas = components['schemas']
export type Currency = Schemas['CurrencyDto']
export type CurrencyList = Schemas['CurrencyDtoPagedResult']
export type CurrencyInput = Schemas['CurrencyRequest']
export type Conversion = Schemas['CurrencyConversionDto']
export type ExchangeRate = Schemas['ExchangeRateDto']
export type ExchangeRateList = Schemas['ExchangeRateDtoPagedResult']
export type ExchangeRateInput = Schemas['ExchangeRateRequest']
export type TaxRate = Schemas['TaxRateDto']
export type TaxRateList = Schemas['TaxRateDtoPagedResult']
export type TaxRateInput = Schemas['TaxRateRequest']
export type TaxCalculation = Schemas['TaxCalculationDto']
export type Discount = Schemas['DiscountDto']
export type DiscountList = Schemas['DiscountDtoPagedResult']
export type DiscountInput = Schemas['DiscountRequest']
export type PaymentMethod = Schemas['PaymentMethodItemDto']
export type PaymentMethodList = Schemas['PaymentMethodItemDtoPagedResult']
export type PaymentMethodInput = Schemas['PaymentMethodRequest']
export type CodeList = Schemas['CodeListDto']
export type CodeListPage = Schemas['CodeListDtoPagedResult']
export type CodeListDetails = Schemas['CodeListDetailsDto']
export type CodeListInput = Schemas['CodeListRequest']
export type CodeListValue = Schemas['CodeListValueDto']
export type CodeListValueInput = Schemas['CodeListValueRequest']
export type Option = Schemas['MoneyOptionDto']

export interface ListParams {
  page: number
  pageSize: number
  sort: string
  dir: 'asc' | 'desc'
  q: string
}

const root = ['money'] as const
export const moneyKeys = {
  all: root,
  currencies: (params: ListParams) => [...root, 'currencies', params] as const,
  rates: (params: ListParams) => [...root, 'rates', params] as const,
  rateCount: [...root, 'rates', 'count'] as const,
  taxes: (params: ListParams) => [...root, 'taxes', params] as const,
  discounts: (params: ListParams) => [...root, 'discounts', params] as const,
  methods: (params: ListParams) => [...root, 'methods', params] as const,
  lists: (params: ListParams) => [...root, 'lists', params] as const,
  list: (id: number) => [...root, 'lists', 'list', id] as const,
  suppliers: [...root, 'suppliers'] as const,
  categories: [...root, 'categories'] as const,
  options: [...root, 'options'] as const,
}

/** Largest page the API hands out; the pickers of the forms use it (a store has a handful of currencies). */
export const ALL: ListParams = { page: 1, pageSize: 200, sort: '', dir: 'asc', q: '' }

function useList<T>(key: readonly unknown[], path: string, params: ListParams, extra: Record<string, string | number | undefined> = {}) {
  return useQuery({
    queryKey: key,
    queryFn: ({ signal }) => http.get<T>(path, { query: { ...params, sort: params.sort || undefined, ...extra }, signal }),
    placeholderData: keepPreviousData,
  })
}

export const useCurrencies = (params: ListParams) => useList<CurrencyList>(moneyKeys.currencies(params), '/currencies', params)

/** Every currency, for the pickers (rates, converter). Shares the cache with the currencies list when it asks for the same page. */
export function useCurrencyOptions() {
  return useQuery({
    queryKey: moneyKeys.options,
    queryFn: ({ signal }) => http.get<CurrencyList>('/currencies', { query: { pageSize: 200 }, signal }),
  })
}

export const useExchangeRates = (params: ListParams) => useList<ExchangeRateList>(moneyKeys.rates(params), '/exchange-rates', params)

export function useExchangeRateCount() {
  return useQuery({
    queryKey: moneyKeys.rateCount,
    queryFn: ({ signal }) => http.get<ExchangeRateList>('/exchange-rates', { query: { pageSize: 1 }, signal }),
    select: (page) => page.total,
  })
}

export function useRateSuppliers() {
  return useQuery({ queryKey: moneyKeys.suppliers, queryFn: ({ signal }) => http.get<Option[]>('/exchange-rates/suppliers', { signal }) })
}

export const useTaxes = (params: ListParams) => useList<TaxRateList>(moneyKeys.taxes(params), '/taxes', params)

export function useTaxCategories() {
  return useQuery({ queryKey: moneyKeys.categories, queryFn: ({ signal }) => http.get<Option[]>('/taxes/categories', { signal }) })
}

export const useDiscounts = (params: ListParams) => useList<DiscountList>(moneyKeys.discounts(params), '/discounts', params)

export const usePaymentMethods = (params: ListParams) => useList<PaymentMethodList>(moneyKeys.methods(params), '/payment-methods', params)

export const useCodeLists = (params: ListParams) => useList<CodeListPage>(moneyKeys.lists(params), '/code-lists', params)

/** One list with its values; fetched fresh whenever the values dialog opens. */
export function useCodeList(id: number | null) {
  return useQuery({
    queryKey: moneyKeys.list(id ?? 0),
    queryFn: ({ signal }) => http.get<CodeListDetails>(`/code-lists/${id}`, { signal }),
    enabled: id !== null,
    gcTime: 0,
  })
}

/**
 * Any change here can alter what the till shows (rates, discounts, payment methods) and what the other pickers offer, so every list of the module is
 * refetched and so is the till's cached lookups (`['pos', ...]`, owned by modules/pos).
 */
function useInvalidate() {
  const client = useQueryClient()
  return () => Promise.all([client.invalidateQueries({ queryKey: moneyKeys.all }), client.invalidateQueries({ queryKey: ['pos'] })])
}

function useSave<Input, Row>(path: string) {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: ({ id, input }: { id?: number; input: Input }) =>
      id === undefined ? http.post<Row>(path, input) : http.put<Row>(`${path}/${id}`, input),
    onSuccess: invalidate,
  })
}

function useRemove(path: string) {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (id: number) => http.delete<void>(`${path}/${id}`), onSuccess: invalidate })
}

export const useSaveCurrency = () => useSave<CurrencyInput, Currency>('/currencies')
export const useDeleteCurrency = () => useRemove('/currencies')

export function useSetDefaultCurrency() {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (id: number) => http.put<Currency>(`/currencies/${id}/default`, {}), onSuccess: invalidate })
}

export function useConvert() {
  return useMutation({
    mutationFn: (input: { amount: number; fromCurrencyId: number; toCurrencyId: number }) => http.post<Conversion>('/currencies/convert', input),
  })
}

export const useSaveExchangeRate = () => useSave<ExchangeRateInput, ExchangeRate>('/exchange-rates')
export const useDeleteExchangeRate = () => useRemove('/exchange-rates')

export const useSaveTax = () => useSave<TaxRateInput, TaxRate>('/taxes')
export const useDeleteTax = () => useRemove('/taxes')

export function useSetDefaultTax() {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (id: number) => http.post<TaxRate>(`/taxes/${id}/default`, {}), onSuccess: invalidate })
}

export function useDeactivateTax() {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (id: number) => http.post<TaxRate>(`/taxes/${id}/deactivate`, {}), onSuccess: invalidate })
}

export function useCalculateTax() {
  return useMutation({
    mutationFn: (input: { baseAmount: number; taxId?: number | null; taxType?: number | null }) => http.post<TaxCalculation>('/taxes/calculate', input),
  })
}

export const useSaveDiscount = () => useSave<DiscountInput, Discount>('/discounts')
export const useDeleteDiscount = () => useRemove('/discounts')

export const useSavePaymentMethod = () => useSave<PaymentMethodInput, PaymentMethod>('/payment-methods')
export const useDeletePaymentMethod = () => useRemove('/payment-methods')

export const useSaveCodeList = () => useSave<CodeListInput, CodeList>('/code-lists')
export const useDeleteCodeList = () => useRemove('/code-lists')

export function useSaveCodeListValue(listId: number) {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: ({ id, input }: { id?: number; input: CodeListValueInput }) =>
      id === undefined
        ? http.post<CodeListValue>(`/code-lists/${listId}/values`, input)
        : http.put<CodeListValue>(`/code-lists/${listId}/values/${id}`, input),
    onSuccess: invalidate,
  })
}

export function useDeleteCodeListValue(listId: number) {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (id: number) => http.delete<void>(`/code-lists/${listId}/values/${id}`), onSuccess: invalidate })
}
