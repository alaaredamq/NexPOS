/**
 * The unsaved order is a list of row ids the user arranged. The server's list can change underneath it (someone adds or
 * removes a product, or this very page does), so the arranged list is reconciled against the server's ids every render:
 * ids that disappeared drop out, new ids join at the end. Pure, so it is unit-tested.
 */
export function reconcileOrder(arranged: readonly number[] | null, serverIds: readonly number[]): number[] {
  if (arranged === null) return [...serverIds]
  const known = new Set(serverIds)
  const kept = arranged.filter((id) => known.has(id))
  const seen = new Set(kept)
  return [...kept, ...serverIds.filter((id) => !seen.has(id))]
}

export function sameOrder(a: readonly number[], b: readonly number[]): boolean {
  return a.length === b.length && a.every((id, index) => id === b[index])
}

/** `arrayMove` without the dependency, for the up/down buttons. Out-of-range moves return the list unchanged. */
export function moveItem<T>(list: readonly T[], from: number, to: number): T[] {
  if (from < 0 || from >= list.length || to < 0 || to >= list.length || from === to) return [...list]
  const next = [...list]
  const [moved] = next.splice(from, 1)
  next.splice(to, 0, moved as T)
  return next
}
