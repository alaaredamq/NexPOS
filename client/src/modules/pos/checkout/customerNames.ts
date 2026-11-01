/**
 * Names of the customers this cashier has seen on the till, by id. The cart store keeps a customer as an id only, and a resumed held cart
 * (or a customer picked before the dialog was reopened) must still show who it is without another round trip.
 */
const names = new Map<number, string>()

export function rememberCustomer(id: number | null | undefined, name: string | null | undefined): void {
  if (id && name) names.set(id, name)
}

export function customerName(id: number | null): string | null {
  return id ? (names.get(id) ?? null) : null
}
