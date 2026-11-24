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

// ---- table filter: <input data-filter="#table"> hides rows whose data-text does not contain what was typed ----
document.querySelectorAll('[data-filter]').forEach((input) => {
  const table = document.querySelector(input.dataset.filter)
  if (!table) return
  const rows = [...table.querySelectorAll('[data-row]')]
  const none = table.parentElement.querySelector('[data-none]')
  input.addEventListener('input', () => {
    const q = input.value.trim().toLowerCase()
    let shown = 0
    for (const row of rows) {
      row.hidden = !!q && !row.dataset.text.includes(q)
      if (!row.hidden) shown++
    }
    if (none) none.hidden = shown !== 0
  })
})

// ---- payment form: "where it came from" only matters for income that is not from a client ----
document.querySelectorAll('[data-payer-toggle]').forEach((select) => {
  const field = document.querySelector(select.dataset.payerToggle)
  if (!field) return
  const sync = () => { field.hidden = select.value !== '' }
  select.addEventListener('change', sync)
  sync()
})

// ---- chart tooltip: data-tip="Title|line|line" on hover or keyboard focus ----
{
  let tip = null
  const show = (el) => {
    const [title, ...lines] = el.dataset.tip.split('|')
    tip ??= Object.assign(document.createElement('div'), { className: 'tip', role: 'tooltip' })
    tip.replaceChildren(Object.assign(document.createElement('b'), { textContent: title }),
      ...lines.map((l) => Object.assign(document.createElement('div'), { textContent: l })))
    document.body.append(tip)
    const r = el.getBoundingClientRect()
    const w = tip.offsetWidth
    tip.style.left = Math.max(8, Math.min(window.innerWidth - w - 8, r.left + r.width / 2 - w / 2)) + 'px'
    tip.style.top = Math.max(8, r.top - tip.offsetHeight - 8) + 'px'
  }
  const hide = () => tip?.remove()
  document.querySelectorAll('[data-tip]').forEach((el) => {
    el.addEventListener('mouseenter', () => show(el))
    el.addEventListener('focus', () => show(el))
    el.addEventListener('mouseleave', hide)
    el.addEventListener('blur', hide)
  })
  window.addEventListener('scroll', hide, { passive: true })
}

// ---- new client: Online months only matter for a POS + Online client ----
document.querySelectorAll('[data-type-toggle]').forEach((radio, _, all) => {
  const months = document.querySelector('[data-online-only]')
  if (!months) return
  const sync = () => { months.hidden = [...all].some((r) => r.checked && r.value === 'pos') }
  radio.addEventListener('change', sync)
  sync()
})

// ---- issue a key: Basic is always 1 PC ----
{
  const seats = document.querySelector('[data-seats]')
  const plans = [...document.querySelectorAll('[data-plan]')]
  if (seats && plans.length) {
    const sync = () => {
      const basic = plans.some((p) => p.checked && p.value === 'basic')
      if (basic) seats.value = '1'
      seats.readOnly = basic
    }
    plans.forEach((p) => p.addEventListener('change', sync))
    sync()
  }
}
