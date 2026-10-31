/**
 * Ctrl/Cmd+K jump-to-page. The list itself is rendered by _CommandPalette.cshtml from NavCatalog (only pages the
 * signed-in user's role grants), so this file just filters it and navigates. Everything is delegated from
 * `document` and the palette is looked up on use, because lang-switch.js replaces the whole shell - markup
 * included - on a language switch.
 */
(function () {
    var restoreFocusTo = null;

    function palette() { return document.getElementById('commandPalette'); }
    function isOpen() { var p = palette(); return !!p && !p.hidden; }
    function input(p) { return p.querySelector('.cmdk-input'); }
    function items(p) { return Array.prototype.slice.call(p.querySelectorAll('.cmdk-item')); }
    function visibleItems(p) { return items(p).filter(function (el) { return !el.hidden; }); }

    function setActive(p, el) {
        items(p).forEach(function (i) {
            var on = i === el;
            i.classList.toggle('is-active', on);
            i.setAttribute('aria-selected', on ? 'true' : 'false');
        });
        var field = input(p);
        if (el) {
            if (!el.id) el.id = 'cmdk-opt-' + items(p).indexOf(el);
            field.setAttribute('aria-activedescendant', el.id);
            el.scrollIntoView({ block: 'nearest' });
        } else {
            field.removeAttribute('aria-activedescendant');
        }
    }

    // Substring match on the label (and the controller name), prefix matches first - enough for a
    // list this size, and it never surprises the way a fuzzy score can. Empty query restores the rail order.
    function filter(p) {
        var q = input(p).value.trim().toLowerCase();
        var list = p.querySelector('.cmdk-results');
        var ranked = items(p).map(function (el, index) {
            if (el.dataset.index === undefined) el.dataset.index = index;
            var label = el.querySelector('.cmdk-label').textContent.trim().toLowerCase();
            var hay = label + ' ' + (el.dataset.keywords || '').toLowerCase();
            var hit = !q || hay.indexOf(q) !== -1;
            el.hidden = !hit;
            return { el: el, rank: q && label.indexOf(q) === 0 ? 0 : 1, order: Number(el.dataset.index) };
        });
        ranked.sort(function (a, b) { return a.rank - b.rank || a.order - b.order; });
        ranked.forEach(function (r) { list.appendChild(r.el); });

        var shown = visibleItems(p);
        p.querySelector('.cmdk-empty').hidden = shown.length > 0;
        list.hidden = shown.length === 0;
        setActive(p, shown[0] || null);
    }

    function open() {
        var p = palette();
        if (!p) return;
        restoreFocusTo = document.activeElement;
        p.hidden = false;
        document.body.classList.add('cmdk-open');
        input(p).value = '';
        filter(p);
        input(p).focus();
    }

    function close() {
        var p = palette();
        if (!p || p.hidden) return;
        p.hidden = true;
        document.body.classList.remove('cmdk-open');
        if (restoreFocusTo && document.contains(restoreFocusTo) && restoreFocusTo.focus) restoreFocusTo.focus();
        restoreFocusTo = null;
    }

    function move(p, step) {
        var shown = visibleItems(p);
        if (!shown.length) return;
        var at = shown.findIndex(function (el) { return el.classList.contains('is-active'); });
        setActive(p, shown[(at + step + shown.length) % shown.length]);
    }

    document.addEventListener('keydown', function (e) {
        if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && e.key.toLowerCase() === 'k') {
            if (!palette()) return; // pages without the shell (noSidebar) have no palette
            e.preventDefault();
            if (isOpen()) close(); else open();
            return;
        }
        if (!isOpen()) return;

        var p = palette();
        if (e.key === 'Escape') {
            e.preventDefault();
            close();
        } else if (e.key === 'ArrowDown') {
            e.preventDefault();
            move(p, 1);
        } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            move(p, -1);
        } else if (e.key === 'Enter') {
            var active = p.querySelector('.cmdk-item.is-active:not([hidden])');
            if (active) {
                e.preventDefault();
                close();
                window.location.assign(active.href);
            }
        } else if (e.key === 'Tab') {
            e.preventDefault(); // one field and a list driven by arrows: keep focus on the field
        }
    });

    document.addEventListener('input', function (e) {
        if (e.target.classList && e.target.classList.contains('cmdk-input')) filter(palette());
    });

    document.addEventListener('mousemove', function (e) {
        var item = e.target.closest && e.target.closest('.cmdk-item');
        if (item && !item.classList.contains('is-active')) setActive(palette(), item);
    });

    document.addEventListener('click', function (e) {
        if (e.target.closest('[data-cmdk-open]')) {
            e.preventDefault();
            open();
        } else if (e.target.closest('.cmdk-item')) {
            close(); // the link itself navigates; closing covers jumping to the page you are already on
        } else if (isOpen() && e.target.classList.contains('cmdk')) {
            close(); // backdrop
        }
    });

    function syncKeyHint() {
        var mac = /Mac|iPhone|iPad/.test(navigator.platform || '');
        document.querySelectorAll('[data-cmdk-key]').forEach(function (k) { k.textContent = mac ? '⌘K' : 'Ctrl K'; });
    }

    document.addEventListener('shell:loaded', function () {
        document.body.classList.remove('cmdk-open');
        syncKeyHint();
    });
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', syncKeyHint);
    else syncKeyHint();
})();
