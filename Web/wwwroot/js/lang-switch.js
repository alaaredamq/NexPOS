/**
 * lang-switch.js — switches UI language without a full-page reload.
 *
 * Clicking a `.language-switcher-link` fetches the target URL as a lightweight
 * shell fragment (X-Partial: shell), swaps the sidebar + top bar + page content,
 * flips RTL, updates history, and re-runs page init — keeping the JS context,
 * CSS and libraries (jQuery/DataTables/Chart.js/...) alive.
 *
 * Opt out per page with `<body data-no-partial-lang>` or a `.content-wrapper`
 * carrying `data-no-partial-lang` — those fall back to a normal navigation.
 */
(function () {
    'use strict';

    var LAYOUT_CONTAINER = 'body > .container-fluid';

    function partialDisabled() {
        return !!(document.body.hasAttribute('data-no-partial-lang') ||
                  document.querySelector('.content-wrapper[data-no-partial-lang]'));
    }

    function fullNavigate(url) {
        window.location.assign(url);
    }

    // Per-page controllers live under wwwroot/js/pages/… (either casing). Libraries
    // (/lib/…) and shared scripts (/js/site.js, /js/localization.js) do not.
    function isPageController(resolvedSrc) {
        return /\/js\/pages\//i.test(resolvedSrc);
    }

    function reExecuteScripts(container) {
        if (!container) return;
        container.querySelectorAll('script').forEach(function (old) {
            var code = old.textContent || '';
            var src = old.getAttribute('src');
            if (src) {
                var already = Array.prototype.some.call(
                    document.scripts,
                    function (s) { return s.src === old.src; });
                if (already) {
                    // Libraries load once. A per-page controller, though, was bound to
                    // the DOM we just replaced — drop the stale tag and re-run it so it
                    // re-initialises against the new markup.
                    if (!isPageController(old.src)) return;
                    Array.prototype.forEach.call(document.scripts, function (s) {
                        if (s.src === old.src) s.remove();
                    });
                }
                var s = document.createElement('script');
                s.src = src;
                s.setAttribute('data-shell-src', src);
                document.body.appendChild(s);
                return;
            }
            if (!code.trim()) return;
            try {
                // Wrap in a function scope so top-level const/let/function from
                // `@section Scripts` don't collide when a page is re-entered.
                (new Function(code + '\n//# sourceURL=shell-inline.js'))();
            } catch (err) {
                console.error('lang-switch: page script failed, falling back to reload', err);
                throw err;
            }
        });
    }

    function applyFragment(html, url) {
        var doc = new DOMParser().parseFromString(html, 'text/html');

        var newContainer = doc.querySelector('.container-fluid');
        var liveContainer = document.querySelector(LAYOUT_CONTAINER);
        if (!newContainer || !liveContainer) {
            throw new Error('lang-switch: shell markers missing');
        }

        var meta = doc.getElementById('shell-meta');
        var dir = (meta && meta.getAttribute('data-dir')) || 'ltr';
        var lang = (meta && meta.getAttribute('data-lang')) || 'en';
        var title = (meta && meta.getAttribute('data-title')) || document.title;

        // Swap sidebar + top bar + content in one shot.
        liveContainer.replaceWith(newContainer);

        // Document-level direction / language.
        document.documentElement.setAttribute('lang', lang);
        document.documentElement.setAttribute('dir', dir);
        document.title = title;

        var rtlLink = document.getElementById('rtl-css');
        if (rtlLink) rtlLink.media = (dir === 'rtl') ? 'all' : 'not all';

        // Refresh translation tables for JS-side i18n.
        var trBlock = doc.getElementById('shell-translations');
        if (trBlock) reExecuteScripts(trBlock);
        window.__currentLanguage = lang;

        if (window.i18n) {
            window.i18n.currentLanguage = lang;
            window.i18n.isLoaded = false;
            window.i18n.load();
        }

        // Styles first (charts/select2 css for the page), then page scripts.
        reExecuteScripts(doc.getElementById('shell-styles'));
        reExecuteScripts(doc.getElementById('shell-scripts'));

        history.pushState({ shell: true }, title, url);

        // Let site.js and any page hooks re-initialise.
        document.dispatchEvent(new CustomEvent('shell:loaded', { detail: { lang: lang, url: url } }));
        window.dispatchEvent(new Event('localizationReady'));
    }

    function switchTo(url) {
        document.body.classList.add('shell-loading');
        fetch(url, {
            headers: { 'X-Partial': 'shell', 'X-Requested-With': 'fetch' },
            credentials: 'same-origin'
        })
            .then(function (r) {
                if (!r.ok) throw new Error('HTTP ' + r.status);
                return r.text();
            })
            .then(function (html) { applyFragment(html, url); })
            .catch(function (err) {
                console.error('lang-switch: partial swap failed, reloading', err);
                fullNavigate(url);
            })
            .finally(function () {
                document.body.classList.remove('shell-loading');
            });
    }

    document.addEventListener('click', function (e) {
        var link = e.target.closest('a.language-switcher-link');
        if (!link) return;
        var url = link.getAttribute('href');
        if (!url) return;

        e.preventDefault();
        e.stopPropagation();

        if (partialDisabled()) {
            fullNavigate(url);
            return;
        }
        switchTo(url);
    });

    window.addEventListener('popstate', function (e) {
        if (e.state && e.state.shell && !partialDisabled()) {
            switchTo(window.location.href);
        }
    });
})();
