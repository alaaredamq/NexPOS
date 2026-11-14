// Coduo POS Cloud portal: copy buttons, confirm dialog, toast, and the client list filter. No dependencies.

// ---- copy: <button data-copy="#selector"> copies that element's text ----
document.addEventListener('click', async (event) => {
  const button = event.target.closest('[data-copy]')
  if (!button) return
  const source = document.querySelector(button.dataset.copy)
  if (!source) return
  const text = source.textContent.trim()
  try {
    await navigator.clipboard.writeText(text)
  } catch {
    const range = document.createRange()
    range.selectNodeContents(source)
    const selection = window.getSelection()
    selection.removeAllRanges()
    selection.addRange(range)
    document.execCommand('copy')
  }
  const label = button.querySelector('.label') ?? button
  const original = label.textContent
  label.textContent = 'Copied'
  setTimeout(() => { label.textContent = original }, 1600)
})

// ---- confirm: <form data-confirm="Disconnect this branch?" data-confirm-title="..." data-confirm-yes="Disconnect"> ----
const dialog = document.getElementById('confirm')
if (dialog) {
  let pending = null
  document.addEventListener('submit', (event) => {
    const form = event.target
    if (!(form instanceof HTMLFormElement) || !form.dataset.confirm || form.dataset.confirmed) return
    event.preventDefault()
    pending = { form, submitter: event.submitter }
    dialog.querySelector('#confirm-title').textContent = form.dataset.confirmTitle || 'Are you sure?'
    dialog.querySelector('#confirm-text').textContent = form.dataset.confirm
    dialog.querySelector('[data-yes]').textContent = form.dataset.confirmYes || 'Confirm'
    dialog.showModal()
  })
  dialog.querySelector('[data-no]').addEventListener('click', () => { pending = null; dialog.close() })
  dialog.querySelector('[data-yes]').addEventListener('click', () => {
    const { form, submitter } = pending ?? {}
    dialog.close()
    if (!form) return
    form.dataset.confirmed = '1'
    form.requestSubmit(submitter)
  })
}

// ---- toast: disappears by itself ----
document.querySelectorAll('[data-flash]').forEach((toast) => setTimeout(() => toast.remove(), 6000))

// ---- client list: search and filter chips (the server already sent every row) ----
const list = document.querySelector('[data-list]')
if (list) {
  const rows = [...list.querySelectorAll('[data-row]')]
  const search = document.querySelector('[data-search]')
  const chips = [...document.querySelectorAll('[data-chip]')]
  const none = document.querySelector('[data-none]')
  let filter = 'all'

  const apply = () => {
    const q = (search?.value ?? '').trim().toLowerCase()
    let shown = 0
    for (const row of rows) {
      const okFilter = filter === 'all' || row.dataset.tags.split(' ').includes(filter)
      const okSearch = !q || row.dataset.name.includes(q)
      row.hidden = !(okFilter && okSearch)
      if (!row.hidden) shown++
    }
    if (none) none.hidden = shown !== 0
  }
  search?.addEventListener('input', apply)
  for (const chip of chips) {
    chip.addEventListener('click', () => {
      filter = chip.dataset.chip
      chips.forEach((c) => c.setAttribute('aria-pressed', String(c === chip)))
      apply()
    })
  }
}
