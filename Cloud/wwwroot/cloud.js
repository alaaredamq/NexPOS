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

// ---- toast: slides away by itself ----
document.querySelectorAll('[data-flash]').forEach((toast) => setTimeout(() => {
  toast.classList.add('out')
  setTimeout(() => toast.remove(), 450)
}, 6000))

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
// New client: months are for POS + Online and the online menu only. The menu always needs some (no "None yet"), and is sold by the
// year, so picking it selects 12 months unless months were already picked by hand.
{
  const types = [...document.querySelectorAll('[data-type-toggle]')]
  const months = document.querySelector('[data-online-only]')
  if (types.length && months) {
    let picked = false
    months.querySelectorAll('input').forEach((m) => m.addEventListener('change', () => { picked = true }))
    const sync = () => {
      const type = (types.find((r) => r.checked) || {}).value
      months.hidden = type === 'pos'
      const none = months.querySelector('[data-none-yet]')
      if (none) none.hidden = type === 'menu'
      const zero = none && none.querySelector('input')
      if (type === 'menu' && (!picked || (zero && zero.checked))) {
        const twelve = months.querySelector('input[value="12"]')
        if (twelve) twelve.checked = true
      }
    }
    types.forEach((r) => r.addEventListener('change', sync))
    sync()
  }
}

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

// ---- pickers: every <select> becomes a normal-looking box with a search (like select2), and every <input list>
//      keeps its typing but shows our own list instead of the browser's datalist popup. Add data-native to opt out. ----
{
  const fold = (s) => s.toLocaleLowerCase().normalize('NFKD').replace(/[̀-ͯ]/g, '')
  const CHEVRON = '<svg viewBox="0 0 16 16" width="16" height="16" aria-hidden="true"><path d="M4 6l4 4 4-4" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>'
  let current = null // the open picker: { root, close }
  document.addEventListener('pointerdown', (e) => { if (current && !current.root.contains(e.target)) current.close() })
  let seq = 0

  // A dropdown list under `root`. items(): [{ value, text, sub, disabled }]. pick(item) is called on a choice.
  const makePanel = (root, { items, pick, selected, withSearch, query, owner }) => {
    const panel = document.createElement('div')
    panel.className = 'pick-panel'
    const list = document.createElement('ul')
    list.className = 'pick-list'
    list.setAttribute('role', 'listbox')
    list.id = 'pick-list-' + (++seq)
    let search = null
    if (withSearch) {
      search = document.createElement('input')
      search.className = 'pick-search'
      search.placeholder = 'Search...'
      search.autocomplete = 'off'
      search.setAttribute('aria-label', 'Search')
      search.setAttribute('aria-controls', list.id)
      panel.append(search)
    }
    panel.append(list)
    let shown = []
    let active = -1
    const setActive = (i) => {
      const lis = list.querySelectorAll('li[role=option]')
      lis.forEach((li, n) => li.classList.toggle('active', n === i))
      active = i
      lis[i]?.scrollIntoView({ block: 'nearest' })
      const target = search ?? owner
      if (target) target.setAttribute('aria-activedescendant', lis[i]?.id ?? '')
    }
    const render = (q) => {
      const f = fold(q ?? '')
      shown = items().filter((it) => !f || fold(it.text + ' ' + (it.sub ?? '') + ' ' + it.value).includes(f))
      list.replaceChildren(...shown.map((it, n) => {
        const li = document.createElement('li')
        li.id = list.id + '-' + n
        li.setAttribute('role', 'option')
        li.setAttribute('aria-selected', String(it.value === selected()))
        if (it.disabled) li.setAttribute('aria-disabled', 'true')
        li.append(Object.assign(document.createElement('span'), { textContent: it.text || ' ' }))
        if (it.sub) li.append(Object.assign(document.createElement('small'), { textContent: it.sub }))
        li.addEventListener('pointerdown', (e) => e.preventDefault()) // keep focus where it is
        li.addEventListener('click', () => { if (!it.disabled) pick(it) })
        li.addEventListener('pointermove', () => { if (active !== n) setActive(n) })
        return li
      }))
      if (!shown.length) {
        list.append(Object.assign(document.createElement('li'), { className: 'none', textContent: 'No matches' }))
        active = -1
        return
      }
      const sel = shown.findIndex((it) => it.value === selected())
      setActive(f ? 0 : Math.max(sel, 0))
    }
    const close = () => {
      if (!panel.isConnected) return
      panel.remove()
      root.classList.remove('open')
      if (current?.root === root) current = null
      root.dispatchEvent(new Event('pick:close'))
    }
    const open = () => {
      if (current && current.root !== root) current.close()
      root.append(panel)
      root.classList.add('open')
      const r = root.getBoundingClientRect()
      panel.classList.toggle('up', window.innerHeight - r.bottom < 300 && r.top > 300)
      render(query ? query() : '')
      if (search) { search.value = ''; search.focus() }
      current = { root, close }
      root.dispatchEvent(new Event('pick:open'))
    }
    // Arrow keys, Enter, Escape. Returns true when the key was used.
    const key = (e) => {
      if (!panel.isConnected) return false
      if (e.key === 'ArrowDown') { if (shown.length) setActive(Math.min(active + 1, shown.length - 1)); return true }
      if (e.key === 'ArrowUp') { if (shown.length) setActive(Math.max(active - 1, 0)); return true }
      if (e.key === 'Enter') {
        const it = shown[active]
        if (it && !it.disabled) { pick(it); return true }
        close()
        return false
      }
      if (e.key === 'Escape') { close(); return true }
      if (e.key === 'Tab') close()
      return false
    }
    search?.addEventListener('input', () => render(search.value))
    search?.addEventListener('keydown', (e) => { if (key(e)) e.preventDefault() })
    return { open, close, key, render, isOpen: () => panel.isConnected, list }
  }

  // <select>: a button shows the chosen option; the real select stays (hidden) so the form, its change events and validation still work.
  const enhanceSelect = (select) => {
    const root = document.createElement('span')
    root.className = 'pick'
    if (select.style.width === 'auto') root.classList.add('inline')
    const box = document.createElement('button')
    box.type = 'button'
    box.className = 'pick-box'
    if (select.getAttribute('style')) box.setAttribute('style', select.getAttribute('style'))
    box.setAttribute('aria-haspopup', 'listbox')
    box.setAttribute('aria-expanded', 'false')
    const label = select.id ? document.querySelector('label[for="' + CSS.escape(select.id) + '"]') : null
    const name = select.getAttribute('aria-label') || label?.textContent.trim()
    const text = document.createElement('span')
    text.className = 'pick-text'
    box.append(text)
    box.insertAdjacentHTML('beforeend', '<span class="pick-chev">' + CHEVRON + '</span>')
    select.before(root)
    root.append(box, select)
    select.classList.add('pick-native')
    select.tabIndex = -1
    const items = () => [...select.options].map((o) => ({ value: o.value, text: o.textContent.trim(), disabled: o.disabled }))
    const sync = () => {
      text.textContent = select.selectedOptions[0]?.textContent.trim() || ' '
      box.disabled = select.disabled
      box.setAttribute('aria-label', (name ? name + ': ' : '') + text.textContent)
      root.classList.remove('is-invalid')
    }
    const panel = makePanel(root, {
      items, selected: () => select.value, withSearch: select.options.length > 5,
      pick: (it) => {
        panel.close()
        box.focus()
        if (select.value === it.value) return
        select.value = it.value
        select.dispatchEvent(new Event('input', { bubbles: true }))
        select.dispatchEvent(new Event('change', { bubbles: true }))
      },
    })
    box.setAttribute('aria-controls', panel.list.id)
    root.addEventListener('pick:open', () => box.setAttribute('aria-expanded', 'true'))
    root.addEventListener('pick:close', () => box.setAttribute('aria-expanded', 'false'))
    box.addEventListener('click', () => (panel.isOpen() ? panel.close() : panel.open()))
    box.addEventListener('keydown', (e) => {
      if (panel.key(e)) { e.preventDefault(); return }
      if (['ArrowDown', 'ArrowUp', ' ', 'Enter'].includes(e.key)) { e.preventDefault(); panel.open() }
    })
    root.addEventListener('keydown', (e) => { if (e.key === 'Escape' && e.target !== box) box.focus() })
    select.addEventListener('focus', () => box.focus()) // a click on the <label> focuses the select
    select.addEventListener('change', sync)
    select.addEventListener('invalid', () => root.classList.add('is-invalid'))
    select.form?.addEventListener('reset', () => setTimeout(sync))
    new MutationObserver(sync).observe(select, { attributes: true, attributeFilter: ['disabled'] })
    sync()
  }

  // <input list>: stays a text box you can type into; our list filters as you type. Picking fills the box.
  const enhanceCombo = (input) => {
    const datalist = document.getElementById(input.getAttribute('list'))
    if (!datalist) return
    const options = [...datalist.options].map((o) => {
      const t = o.textContent.trim()
      return { value: o.value, text: o.value, sub: t && t !== o.value ? t : '' }
    })
    input.removeAttribute('list')
    input.autocomplete = 'off'
    input.setAttribute('role', 'combobox')
    input.setAttribute('aria-autocomplete', 'list')
    input.setAttribute('aria-expanded', 'false')
    const root = document.createElement('span')
    root.className = 'pick pick-combo'
    input.before(root)
    const chev = document.createElement('button')
    chev.type = 'button'
    chev.tabIndex = -1
    chev.className = 'pick-chev-btn'
    chev.setAttribute('aria-label', 'Show choices')
    chev.innerHTML = CHEVRON
    root.append(input, chev)
    let typed = false // show every choice until the person types
    const panel = makePanel(root, {
      items: () => options, selected: () => input.value.trim(), withSearch: false, owner: input,
      query: () => (typed ? input.value : ''),
      pick: (it) => {
        input.value = it.value
        input.dispatchEvent(new Event('input', { bubbles: true }))
        input.dispatchEvent(new Event('change', { bubbles: true }))
        panel.close()
        input.focus()
      },
    })
    input.setAttribute('aria-controls', panel.list.id)
    root.addEventListener('pick:open', () => input.setAttribute('aria-expanded', 'true'))
    root.addEventListener('pick:close', () => { input.setAttribute('aria-expanded', 'false'); typed = false })
    chev.addEventListener('pointerdown', (e) => e.preventDefault())
    chev.addEventListener('click', () => {
      if (panel.isOpen()) { panel.close(); return }
      input.focus()
      typed = false
      panel.open()
    })
    input.addEventListener('click', () => { if (!panel.isOpen()) { typed = false; panel.open() } })
    input.addEventListener('input', (e) => {
      if (!e.isTrusted) return
      typed = true
      if (panel.isOpen()) panel.render(input.value)
      else panel.open()
    })
    input.addEventListener('keydown', (e) => {
      if (panel.key(e)) { e.preventDefault(); return }
      if ((e.key === 'ArrowDown' || e.key === 'ArrowUp') && !panel.isOpen()) { e.preventDefault(); typed = false; panel.open() }
    })
    input.addEventListener('blur', () => setTimeout(() => { if (!root.contains(document.activeElement)) panel.close() }))
  }

  document.querySelectorAll('select:not([multiple]):not([size]):not([data-native])').forEach(enhanceSelect)
  document.querySelectorAll('input[list]:not([data-native])').forEach(enhanceCombo)
}
