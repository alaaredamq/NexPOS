import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { beforeAll, beforeEach, describe, expect, it } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { queryClient, queryKeys } from '@/api/queryClient'
import { makeSession } from '@/test/sessionFixture'
import type { PosProduct } from './api'
import { ProductTile } from './ProductTile'

const soldOut = {
  id: 1,
  name: 'Cola',
  barcode: '0001',
  categoryName: 'Drinks',
  currencyNotation: '$',
  sellingPrice: 2,
  stockQuantity: 0,
  minimumStock: 5,
  unit: null,
  unitKind: 'count',
  isPack: false,
  packSize: null,
} as unknown as PosProduct

function showTile(plan: 'Basic' | 'Pro') {
  const session = makeSession()
  queryClient.setQueryData(queryKeys.session, { ...session, license: { ...session.license, plan } })
  render(
    <QueryClientProvider client={queryClient}>
      <ProductTile product={soldOut} onPress={() => {}} />
    </QueryClientProvider>,
  )
}

beforeAll(async () => {
  await loadNamespaces('pos', 'common')
})

beforeEach(async () => {
  queryClient.clear()
  await setLanguage('en')
})

describe('product key and the plan', () => {
  it('shows the stock (here: sold out) on Pro', () => {
    showTile('Pro')
    expect(screen.getByRole('button', { name: /Cola/ })).toHaveTextContent(/out of stock/i)
  })

  it('is name and price only on Basic, which tracks no stock', () => {
    showTile('Basic')
    const key = screen.getByRole('button', { name: /Cola/ })
    expect(key).not.toHaveTextContent(/out of stock/i)
    expect(key).toHaveTextContent('2.00')
    expect(key).toHaveAttribute('data-stock', 'ok')
  })
})
