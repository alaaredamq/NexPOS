/**
 * Price picker for a cart line.
 *
 * Opens next to the price button (a bottom sheet on phones) and lets the cashier either tap one of the
 * product's prices or type a custom one. A custom price can never be lower than the purchase price: the
 * lowest allowed figure comes from the server (GetPriceOptions), which also re-checks it when the sale is made.
 *
 * The picker never changes the cart itself. It hands the choice to onApply and POS updates the line.
 *
 *   const picker = new PricePicker({
 *       loadOptions: (item) => Promise<{ options: [{id, name, price}], minPrice, hasCost }>,
 *       getDisplay:  () => ({ currencyId, notation }),
 *       onApply:     (item, { price, currencyId, tierId, label, isCustom }) => void
 *   });
 *   picker.open(item, buttonElement);
 */
class PricePicker {
    constructor({ loadOptions, getDisplay, onApply }) {
        this.loadOptions = loadOptions;
        this.getDisplay = getDisplay;
        this.onApply = onApply;
        this.current = null;       // { item, anchor, data, state: 'loading' | 'ready' | 'error' }

        this.scrim = document.createElement('div');
        this.scrim.className = 'pp-scrim';
        this.scrim.hidden = true;

        this.root = document.createElement('div');
        this.root.className = 'price-picker';
        this.root.setAttribute('role', 'dialog');
        this.root.setAttribute('aria-modal', 'true');
        this.root.hidden = true;

        document.body.appendChild(this.scrim);
        document.body.appendChild(this.root);

        this.scrim.addEventListener('click', () => this.close());
        this.root.addEventListener('click', (e) => this.onClick(e));
        this.root.addEventListener('input', (e) => this.onInput(e));
        this.root.addEventListener('focusout', (e) => this.onFocusOut(e));
        this.root.addEventListener('keydown', (e) => this.onKeyDown(e));
        this.root.addEventListener('submit', (e) => { e.preventDefault(); this.submitCustom(); });
        window.addEventListener('resize', () => this.place());
    }

    // ---------- text ----------
    tr(key, fallback, params) {
        const i18n = window.i18n;
        if (i18n && i18n.isLoaded) return i18n.tf(key, params || {}, fallback);
        return Object.keys(params || {}).reduce((text, k) => text.split(`{${k}}`).join(params[k]), fallback);
    }

    esc(value) { return CartItem.escapeHtml(value); }
    money(value) { return CartItem.formatMoney(value); }
    isOpen() { return !!this.current; }
    isOpenFor(item) { return !!this.current && this.current.item === item; }

    /** Latin digits only: a cashier on the Arabic keyboard may type Arabic-Indic digits. */
    static normaliseDigits(raw) {
        return String(raw)
            .replace(/[٠-٩]/g, d => d.charCodeAt(0) - 0x0660)
            .replace(/[۰-۹]/g, d => d.charCodeAt(0) - 0x06F0)
            .replace(/٫/g, '.');
    }

    // ---------- open / close ----------
    async open(item, anchor) {
        if (this.current) this.close(true);
        this.current = { item, anchor, data: null, state: 'loading' };
        anchor.setAttribute('aria-expanded', 'true');

        this.render();
        this.root.hidden = false;
        this.scrim.hidden = false;
        this.place();
        requestAnimationFrame(() => { this.root.classList.add('in'); this.place(); });
        this.root.querySelector('.pp-close').focus({ preventScroll: true });

        const open = this.current;
        try {
            const data = await this.loadOptions(item);
            if (this.current !== open) return;          // closed or reopened while loading
            open.data = data;
            open.state = 'ready';
        } catch (err) {
            if (this.current !== open) return;
            console.error('Could not load prices', err);
            open.state = 'error';
        }
        this.render();
        this.place();
        const target = item.IsCustomPrice
            ? this.root.querySelector('#pp-custom-input')
            : (this.root.querySelector('.pp-tier[aria-pressed="true"]') || this.root.querySelector('.pp-tier') || this.root.querySelector('.pp-close'));
        if (target) {
            target.focus({ preventScroll: true });
            if (target.select) target.select();
        }
    }

    close(silent) {
        if (!this.current) return;
        const { anchor } = this.current;
        this.current = null;
        this.root.classList.remove('in');
        this.root.hidden = true;
        this.scrim.hidden = true;
        this.root.innerHTML = '';
        if (anchor && anchor.isConnected) {
            anchor.setAttribute('aria-expanded', 'false');
            if (!silent) anchor.focus();
        }
    }

    // ---------- render ----------
    render() {
        const { item, state } = this.current;
        const display = this.getDisplay();
        const kg = item.WeightOrVolume !== null && item.WeightOrVolume !== undefined;
        const unit = kg && typeof UnitConverter !== 'undefined' && UnitConverter.formatUnit
            ? UnitConverter.formatUnit(item.ProductUnit) : item.ProductUnit;

        let subtitle = this.tr('pos.priceChoose', 'Choose a price');
        if (kg) subtitle = this.tr('pos.priceChoosePerUnit', 'Choose a price per {unit}', { unit });
        else if (item.Quantity > 1) subtitle = this.tr('pos.priceChooseEach', 'Choose a price for each of {qty}', { qty: item.Quantity });

        let body = '';
        if (state === 'loading') {
            body = `<div class="pp-skeleton" aria-busy="true" aria-label="${this.esc(this.tr('pos.priceLoading', 'Loading prices'))}"><span></span><span></span><span></span></div>`;
        } else if (state === 'error') {
            body = `<div class="pp-error-state" role="alert"><p>${this.esc(this.tr('pos.priceLoadFailed', 'Prices could not be loaded.'))}</p>
                <button type="button" class="pp-retry">${this.esc(this.tr('pos.priceRetry', 'Try again'))}</button></div>`;
        } else {
            body = this.tiersHtml(display, kg, unit) + this.customHtml(display, kg, unit);
        }

        this.root.setAttribute('aria-label', item.Name);
        this.root.innerHTML = `
            <div class="pp-head">
                <div><h2 class="pp-title">${this.esc(item.Name)}</h2><p class="pp-sub">${this.esc(subtitle)}</p></div>
                <button type="button" class="pp-close" aria-label="${this.esc(this.tr('pos.priceClose', 'Close price picker'))}"><i class="fas fa-times" aria-hidden="true"></i></button>
            </div>${body}`;
    }

    tiersHtml(display, kg, unit) {
        const { item, data } = this.current;
        const options = data.options;
        const base = options[0];
        const defaultName = this.tr('pos.priceRetail', 'Retail');
        const items = options.map((opt, i) => {
            const name = i === 0 ? defaultName : opt.name;
            let sub;
            if (i === 0) sub = this.tr('pos.priceDefault', 'Default price');
            else {
                const pct = Math.round((opt.price / base.price - 1) * 100);
                // The sign stays attached to the number inside right-to-left text (LRI ... PDI isolates it).
                sub = this.tr('pos.priceVs', '{pct} vs {name}', { pct: `⁦${pct > 0 ? '+' : ''}${pct}%⁩`, name: defaultName });
            }
            const selected = !item.IsCustomPrice && (opt.id ?? null) === (item.PriceTierId ?? null);
            return `<li><button type="button" class="pp-tier" data-tier="${opt.id ?? ''}" aria-pressed="${selected}">
                <span class="pp-radio" aria-hidden="true"><i class="fas fa-check"></i></span>
                <span class="pp-tier-text"><span class="pp-tier-name">${this.esc(name)}</span><span class="pp-tier-sub">${this.esc(sub)}</span></span>
                <span class="pp-tier-price">${this.esc(display.notation)} ${this.money(opt.price)}${kg ? ` <small>/ ${this.esc(unit)}</small>` : ''}</span>
            </button></li>`;
        }).join('');
        return `<ul class="pp-tiers">${items}</ul>`;
    }

    customHtml(display, kg, unit) {
        const { item, data } = this.current;
        const help = data.hasCost
            ? this.tr('pos.priceMinHelp', 'Cannot be lower than {min}.', { min: `${display.notation} ${this.money(data.minPrice)}${kg ? ` / ${unit}` : ''}` })
            : this.tr('pos.priceAnyHelp', 'Any price above 0 is accepted.');
        const label = kg
            ? this.tr('pos.priceCustomLabelPerUnit', 'Custom price per {unit}', { unit })
            : this.tr('pos.priceCustomLabel', 'Custom price');
        return `
            <form class="pp-custom" id="pp-custom-form" novalidate>
                <div class="pp-custom-head">
                    <label for="pp-custom-input">${this.esc(label)}</label>
                    ${item.IsCustomPrice ? `<span class="pp-inuse">${this.esc(this.tr('pos.priceInUse', 'In use'))}</span>` : ''}
                </div>
                <div class="pp-row">
                    <div class="pp-field" id="pp-field">
                        <span class="pp-prefix">${this.esc(display.notation)}</span>
                        <input id="pp-custom-input" type="text" inputmode="decimal" autocomplete="off" dir="ltr"
                               placeholder="${this.esc(this.tr('pos.priceEnterAmount', 'Enter amount'))}"
                               value="${item.IsCustomPrice ? this.esc(this.money(item.ConvertedPrice)) : ''}"
                               aria-describedby="pp-help pp-err">
                    </div>
                    <button type="submit" class="pp-apply" id="pp-apply" disabled>${this.esc(this.tr('pos.priceUse', 'Use price'))}</button>
                </div>
                <p class="pp-help" id="pp-help">${this.esc(help)}</p>
                <p class="pp-err" id="pp-err" role="alert" hidden></p>
                <p class="pp-preview" id="pp-preview"></p>
            </form>`;
    }

    // ---------- placement ----------
    phone() { return window.matchMedia('(max-width: 559px)').matches; }

    place() {
        if (!this.current) return;
        const root = this.root;
        if (this.phone()) { root.style.left = root.style.top = root.style.width = ''; return; }
        const a = this.current.anchor.getBoundingClientRect();
        const width = Math.min(332, window.innerWidth - 32);
        const height = root.offsetHeight;
        const rtl = getComputedStyle(document.documentElement).direction === 'rtl';
        let left = rtl ? a.left : a.right - width;
        left = Math.max(16, Math.min(left, window.innerWidth - width - 16));
        let top = a.bottom + 8;
        if (top + height > window.innerHeight - 16) top = Math.max(16, a.top - height - 8);
        root.style.width = `${width}px`;
        root.style.left = `${left}px`;
        root.style.top = `${top}px`;
    }

    // ---------- custom price ----------
    validate(raw) {
        const { data } = this.current;
        const display = this.getDisplay();
        const text = PricePicker.normaliseDigits(raw).replace(/[,\s]/g, '');
        if (!text) return { ok: false, empty: true, msg: this.tr('pos.priceEnterToContinue', 'Enter a price to continue.') };
        if (!/^\d+(\.\d{1,2})?$/.test(text)) {
            return { ok: false, msg: this.tr('pos.priceNotANumber', 'Enter the price as a number, for example {example}.', { example: '52,000' }) };
        }
        const value = parseFloat(text);
        if (value <= 0) return { ok: false, msg: this.tr('pos.priceAboveZero', 'The price must be greater than 0.') };
        if (data.hasCost && value < data.minPrice) {
            return {
                ok: false,
                msg: this.tr('pos.priceBelowMin', '{amount} is below the minimum. Enter {min} or more.', {
                    amount: `${display.notation} ${this.money(value)}`,
                    min: `${display.notation} ${this.money(data.minPrice)}`
                })
            };
        }
        return { ok: true, value };
    }

    previewText(value) {
        const { item } = this.current;
        const display = this.getDisplay();
        if (item.WeightOrVolume !== null && item.WeightOrVolume !== undefined) {
            return this.tr('pos.priceNewTotal', 'New line total: {total}', { total: `${display.notation} ${this.money(value * item.WeightOrVolume)}` });
        }
        const total = `${display.notation} ${this.money(value * item.Quantity)}`;
        return item.Quantity === 1
            ? this.tr('pos.priceNewTotal', 'New line total: {total}', { total })
            : this.tr('pos.priceNewTotalQty', 'New line total: {qty} × {price} = {total}', { qty: item.Quantity, price: this.money(value), total });
    }

    showError(msg) {
        const err = this.root.querySelector('#pp-err');
        err.innerHTML = '<i class="fas fa-exclamation-circle" aria-hidden="true"></i><span></span>';
        err.querySelector('span').textContent = msg;
        err.hidden = false;
        this.root.querySelector('#pp-field').classList.add('invalid');
        this.root.querySelector('#pp-custom-input').setAttribute('aria-invalid', 'true');
    }

    clearError() {
        const err = this.root.querySelector('#pp-err');
        if (!err) return;
        err.hidden = true; err.innerHTML = '';
        this.root.querySelector('#pp-field').classList.remove('invalid');
        this.root.querySelector('#pp-custom-input').removeAttribute('aria-invalid');
    }

    submitCustom() {
        if (!this.current || this.current.state !== 'ready') return;
        const input = this.root.querySelector('#pp-custom-input');
        const result = this.validate(input.value);
        if (!result.ok) { this.showError(result.msg); input.focus(); return; }
        this.apply({ price: result.value, tierId: null, label: null, isCustom: true });
    }

    apply(choice) {
        const { item } = this.current;
        const display = this.getDisplay();
        this.close(true);
        this.onApply(item, { ...choice, currencyId: display.currencyId });
    }

    // ---------- events ----------
    onClick(e) {
        if (e.target.closest('.pp-close')) { this.close(); return; }
        if (e.target.closest('.pp-retry')) { this.retry(); return; }
        const tier = e.target.closest('.pp-tier');
        if (!tier || !this.current || this.current.state !== 'ready') return;
        const id = tier.dataset.tier === '' ? null : parseInt(tier.dataset.tier, 10);
        const option = this.current.data.options.find(o => (o.id ?? null) === id);
        this.apply({
            price: option.price,
            tierId: id,
            label: id === null ? null : option.name,
            isCustom: false
        });
    }

    async retry() {
        const open = this.current;
        if (!open) return;
        open.state = 'loading';
        this.render();
        try {
            open.data = await this.loadOptions(open.item);
            open.state = 'ready';
        } catch (err) {
            open.state = 'error';
        }
        if (this.current === open) { this.render(); this.place(); }
    }

    onInput(e) {
        if (e.target.id !== 'pp-custom-input' || !this.current) return;
        const result = this.validate(e.target.value);
        this.root.querySelector('#pp-apply').disabled = !result.ok;
        this.root.querySelector('#pp-preview').textContent = result.ok ? this.previewText(result.value) : '';
        if (result.ok) this.clearError();
        else if (!this.root.querySelector('#pp-err').hidden) this.showError(result.msg);
    }

    onFocusOut(e) {
        if (e.target.id !== 'pp-custom-input' || !this.current || this.current.state !== 'ready') return;
        const result = this.validate(e.target.value);
        if (result.ok) e.target.value = this.money(result.value);
        else if (!result.empty) this.showError(result.msg);
    }

    onKeyDown(e) {
        if (e.key === 'Escape') { e.stopPropagation(); this.close(); return; }
        // A form whose submit button is disabled ignores Enter, so Enter is handled here to show the error.
        if (e.key === 'Enter' && e.target.id === 'pp-custom-input') { e.preventDefault(); this.submitCustom(); return; }
        const tiers = Array.from(this.root.querySelectorAll('.pp-tier'));
        const at = tiers.indexOf(document.activeElement);
        if (at > -1 && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) {
            e.preventDefault();
            tiers[(at + (e.key === 'ArrowDown' ? 1 : tiers.length - 1)) % tiers.length].focus();
            return;
        }
        if (e.key === 'Tab') {
            const focusable = Array.from(this.root.querySelectorAll('button:not(:disabled), input'));
            if (!focusable.length) return;
            const first = focusable[0], last = focusable[focusable.length - 1];
            if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
            else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
        }
    }
}

window.PricePicker = PricePicker;
