/**
 * Barcode sticker printing: a "Print label" dialog (printer + size + number of copies)
 * and the printing itself. Needs JsBarcode loaded first - include Shared/_StickerLabelScripts.
 *
 *   StickerLabel.prompt({ name: 'Cable', code: '2261013123452', price: '$4.00' });
 *
 * Inside the Coduo POS desktop app the sticker prints silently, straight to the sticker printer
 * chosen on this computer (in App Settings or the dialog itself; the OS default if none), and
 * the copy count is handed to the printer.
 * In a plain browser a web page can't talk to a specific printer, so the labels (one page per
 * copy) are downloaded as a PDF instead - there is no browser print dialog.
 */
(function () {
    'use strict';

    /** Upper bound on one print job - a mistyped quantity shouldn't tie up the label printer. */
    var MAX_COPIES = 100;
    var PRESETS = [1, 5, 10, 20];
    var MIN_WIDTH_MM = 15, MAX_WIDTH_MM = 150;
    var MIN_HEIGHT_MM = 10, MAX_HEIGHT_MM = 150;
    var DEFAULT_SETTINGS = { widthMm: 40, heightMm: 25 };
    var PRINTER_STORAGE_KEY = 'nexpos-sticker-printer';
    var SHOW_PRICE_STORAGE_KEY = 'nexpos-sticker-show-price';

    function isDesktop() {
        return !!(window.nexpos && window.nexpos.isElectron);
    }

    /**
     * The sticker printer chosen on this computer - a per-machine hardware choice, so it lives in
     * localStorage rather than the shared settings table (several tills can share one database).
     * '' means the OS default printer.
     */
    function getPrinterName() {
        try { return localStorage.getItem(PRINTER_STORAGE_KEY) || ''; } catch (e) { return ''; }
    }

    function setPrinterName(name) {
        try {
            if (name) localStorage.setItem(PRINTER_STORAGE_KEY, name);
            else localStorage.removeItem(PRINTER_STORAGE_KEY);
        } catch (e) { /* storage blocked - falls back to the default printer */ }
    }

    /** Whether the price is printed on the label - remembered per computer; on unless switched off. */
    function getShowPrice() {
        try { return localStorage.getItem(SHOW_PRICE_STORAGE_KEY) !== '0'; } catch (e) { return true; }
    }

    function setShowPrice(show) {
        try { localStorage.setItem(SHOW_PRICE_STORAGE_KEY, show ? '1' : '0'); } catch (e) { /* storage blocked - not remembered */ }
    }

    /** Printers installed on this computer, as the desktop app sees them; [] in a plain browser. */
    function listPrinters() {
        return isDesktop() && window.nexpos.listPrinters
            ? window.nexpos.listPrinters().catch(function () { return []; })
            : Promise.resolve([]);
    }

    /** Fills a <select> with this machine's printers and selects the remembered one. */
    function fillPrinterSelect($select, defaultText) {
        return listPrinters().then(function (printers) {
            var current = getPrinterName();
            $select.empty().append($('<option>', { value: '', text: defaultText }));
            printers.forEach(function (p) {
                $select.append($('<option>', { value: p.name, text: (p.displayName || p.name) + (p.isDefault ? ' (' + defaultText + ')' : '') }));
            });
            // Keep a remembered printer selectable even if it isn't connected right now.
            if (current && !printers.some(function (p) { return p.name === current; })) {
                $select.append($('<option>', { value: current, text: current + ' (' + tr('notDetected', 'not detected') + ')' }));
            }
            $select.val(current);
        });
    }

    var settingsUrl = document.currentScript && document.currentScript.getAttribute('data-settings-url');
    var pdfUrl = document.currentScript && document.currentScript.getAttribute('data-pdf-url');
    var settingsPromise = null;

    function loadSettings() {
        if (!settingsPromise) {
            settingsPromise = (settingsUrl ? $.getJSON(settingsUrl) : $.Deferred().reject())
                .then(function (res) {
                    return {
                        widthMm: clamp(res && res.widthMm, MIN_WIDTH_MM, MAX_WIDTH_MM, DEFAULT_SETTINGS.widthMm),
                        heightMm: clamp(res && res.heightMm, MIN_HEIGHT_MM, MAX_HEIGHT_MM, DEFAULT_SETTINGS.heightMm)
                    };
                }, function () {
                    // Don't cache a failure - try again next time the dialog opens.
                    settingsPromise = null;
                    return $.Deferred().resolve($.extend({}, DEFAULT_SETTINGS));
                });
        }
        return settingsPromise;
    }

    function clamp(value, min, max, fallback) {
        var n = Math.floor(Number(value));
        if (!isFinite(n) || n <= 0) return fallback;
        return Math.min(max, Math.max(min, n));
    }

    function clampCopies(copies) {
        return clamp(copies, 1, MAX_COPIES, 1);
    }

    function tr(key, fallback, params) {
        var text = fallback;
        if (window.i18n && window.i18n.isLoaded) {
            text = window.i18n.t('products.printLabel.' + key, fallback);
        }
        Object.keys(params || {}).forEach(function (p) {
            text = text.replace(new RegExp('\\{' + p + '\\}', 'g'), params[p]);
        });
        return text;
    }

    function escapeHtml(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    /** "$" + 1234.5 -> "$1,234.50", matching CurrencyHelper.FormatCurrency on the server. */
    function formatPrice(amount, notation) {
        var n = Number(amount) || 0;
        return (notation == null ? '$' : notation) +
            n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    /**
     * The barcode as modules ('1' = bar, '0' = space) for the server to draw into a PDF.
     * Throws if the code can't be encoded, like buildBarcodeSvg.
     */
    function buildBarcodeBars(code) {
        var encoded = {};
        JsBarcode(encoded, code, { format: 'CODE128', displayValue: false });
        return encoded.encodings.map(function (e) { return e.data; }).join('');
    }

    /** Throws if the code can't be encoded (e.g. non-ASCII characters). */
    function buildBarcodeSvg(code) {
        var svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        JsBarcode(svg, code, {
            format: 'CODE128',
            width: 1.4,
            height: 34,
            margin: 0,
            displayValue: false
        });
        // Stretch to whatever box the label size gives it; bars scale uniformly so it still scans.
        svg.setAttribute('preserveAspectRatio', 'none');
        return svg.outerHTML;
    }

    /**
     * One page per sticker (name / barcode / code / price). Sizes are the 40mm x 25mm layout
     * scaled to the chosen label, so small and large stock keep the same proportions.
     */
    function buildStickerHtml(data, widthMm, heightMm, copies, showPrice) {
        var s = Math.min(widthMm / 40, heightMm / 25);
        var mm = function (v) { return (v * s).toFixed(2) + 'mm'; };

        var sticker =
            '  <div class="sticker">\n' +
            '    <div class="name">' + escapeHtml(data.name) + '</div>\n' +
            '    <div class="bc">' + buildBarcodeSvg(data.code) + '</div>\n' +
            '    <div class="sku">' + escapeHtml(data.code) + '</div>\n' +
            (data.price && showPrice ? '    <div class="price">' + escapeHtml(data.price) + '</div>\n' : '') +
            '  </div>';
        var stickers = [];
        for (var i = 0; i < clampCopies(copies); i++) stickers.push(sticker);

        return '<!doctype html>\n<html>\n<head>\n<meta charset="utf-8" />\n<style>\n' +
            '  @page { size: ' + widthMm + 'mm ' + heightMm + 'mm; margin: 0; }\n' +
            '  html, body { margin: 0; padding: 0; }\n' +
            '  body { width: ' + widthMm + 'mm; font-family: Arial, Helvetica, sans-serif; color: #000; background: #fff; }\n' +
            '  .sticker {\n' +
            '    width: ' + widthMm + 'mm; height: ' + heightMm + 'mm; box-sizing: border-box; overflow: hidden;\n' +
            '    break-after: page; page-break-after: always;\n' +
            '    padding: ' + mm(1.4) + ' ' + mm(1.6) + ';\n' +
            '    display: flex; flex-direction: column; align-items: center; justify-content: center;\n' +
            '    gap: ' + mm(0.6) + '; text-align: center;\n' +
            '  }\n' +
            '  .name { font-size: ' + mm(2.7) + '; font-weight: 700; line-height: 1.1; max-width: 100%;\n' +
            '    white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }\n' +
            '  .bc { width: ' + (widthMm * 0.85).toFixed(2) + 'mm; height: ' + (heightMm * 0.28).toFixed(2) + 'mm; margin: ' + mm(0.3) + ' 0; }\n' +
            '  .bc svg { width: 100%; height: 100%; display: block; }\n' +
            '  .sku { font-size: ' + mm(1.7) + '; letter-spacing: 0.06em; color: #2a2a2a; font-family: Consolas, monospace; }\n' +
            '  .price { font-size: ' + mm(4) + '; font-weight: 800; margin-top: ' + mm(0.2) + '; }\n' +
            '  /* The last label must not push out a trailing blank page. */\n' +
            '  .sticker:last-child { break-after: auto; page-break-after: auto; }\n' +
            '</style>\n</head>\n<body>\n' + stickers.join('\n') + '\n</body>\n</html>';
    }

    /** Sends the job. Resolves once handed off; rejects with a message on failure. */
    function print(data, options) {
        var copies = clampCopies(options.copies);
        var widthMm = clamp(options.widthMm, MIN_WIDTH_MM, MAX_WIDTH_MM, DEFAULT_SETTINGS.widthMm);
        var heightMm = clamp(options.heightMm, MIN_HEIGHT_MM, MAX_HEIGHT_MM, DEFAULT_SETTINGS.heightMm);
        var showPrice = options.showPrice === undefined ? getShowPrice() : !!options.showPrice;
        var bridge = window.nexpos;

        if (bridge && bridge.isElectron && bridge.printSticker) {
            return bridge.printSticker({
                html: buildStickerHtml(data, widthMm, heightMm, 1, showPrice),
                printerName: (options.printerName === undefined ? getPrinterName() : options.printerName) || undefined,
                copies: copies,
                widthMm: widthMm,
                heightMm: heightMm
            }).then(function (res) {
                if (!res || !res.success) throw new Error((res && res.failureReason) || 'unknown error');
            });
        }

        return downloadPdf(data, { copies: copies, widthMm: widthMm, heightMm: heightMm, showPrice: showPrice });
    }

    /** Plain browser: asks the server for the labels as a PDF and saves it. */
    function downloadPdf(data, job) {
        if (!pdfUrl) return Promise.reject(new Error('label PDF is not available'));

        return fetch(pdfUrl, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                name: data.name,
                code: data.code,
                price: data.price,
                bars: buildBarcodeBars(data.code),
                widthMm: job.widthMm,
                heightMm: job.heightMm,
                copies: job.copies,
                showPrice: job.showPrice
            })
        }).then(function (response) {
            if (!response.ok) throw new Error('HTTP ' + response.status);
            return response.blob();
        }).then(function (blob) {
            var url = URL.createObjectURL(blob);
            var link = document.createElement('a');
            link.href = url;
            link.download = 'labels-' + data.code + '.pdf';
            link.style.display = 'none';
            document.body.appendChild(link);
            link.click();
            link.remove();
            window.setTimeout(function () { URL.revokeObjectURL(url); }, 10000);
        });
    }

    // ---- Dialog ---------------------------------------------------------------------------

    var $modal = null;
    var modal = null;
    var current = null; // { data, settings }

    function ensureModal() {
        if ($modal) return;

        var presets = PRESETS.map(function (n) {
            return '<button type="button" class="btn btn-sm btn-outline-secondary sl-preset" data-copies="' + n + '">' + n + '</button>';
        }).join('');

        $modal = $(
            '<div class="modal fade" id="stickerLabelModal" tabindex="-1" aria-hidden="true">' +
            '  <div class="modal-dialog modal-dialog-centered" style="max-width:440px">' +
            '    <div class="modal-content">' +
            '      <div class="modal-header">' +
            '        <h5 class="modal-title"><i class="fas fa-barcode me-2"></i><span class="sl-title"></span></h5>' +
            '        <button type="button" class="btn-close" data-bs-dismiss="modal"></button>' +
            '      </div>' +
            '      <div class="modal-body">' +
            '        <div class="p-2 mb-3 rounded border bg-light">' +
            '          <div class="fw-bold text-truncate sl-name"></div>' +
            '          <div class="small text-muted font-monospace sl-meta"></div>' +
            '        </div>' +
            '        <div class="mb-3 sl-printer-row">' +
            '          <label class="form-label fw-semibold sl-printer-label"></label>' +
            '          <select class="form-select form-select-sm sl-printer"></select>' +
            '        </div>' +
            '        <div class="form-check form-switch mb-3 sl-price-row">' +
            '          <input type="checkbox" class="form-check-input sl-show-price" id="slShowPrice" />' +
            '          <label class="form-check-label" for="slShowPrice"></label>' +
            '        </div>' +
            '        <label class="form-label fw-semibold sl-size-label"></label>' +
            '        <div class="d-flex align-items-center gap-2 mb-3">' +
            '          <div class="input-group input-group-sm"><span class="input-group-text sl-width-label"></span>' +
            '            <input type="number" class="form-control sl-width" min="' + MIN_WIDTH_MM + '" max="' + MAX_WIDTH_MM + '" step="1" /></div>' +
            '          <span class="text-muted">&times;</span>' +
            '          <div class="input-group input-group-sm"><span class="input-group-text sl-height-label"></span>' +
            '            <input type="number" class="form-control sl-height" min="' + MIN_HEIGHT_MM + '" max="' + MAX_HEIGHT_MM + '" step="1" /></div>' +
            '        </div>' +
            '        <label class="form-label fw-semibold sl-copies-label"></label>' +
            '        <div class="input-group mb-2" style="max-width:200px">' +
            '          <button type="button" class="btn btn-outline-secondary sl-dec">&minus;</button>' +
            '          <input type="text" inputmode="numeric" class="form-control text-center fw-bold sl-copies" />' +
            '          <button type="button" class="btn btn-outline-secondary sl-inc">+</button>' +
            '        </div>' +
            '        <div class="d-flex gap-2 mb-1">' + presets + '</div>' +
            '        <div class="form-text sl-hint"></div>' +
            '      </div>' +
            '      <div class="modal-footer">' +
            '        <button type="button" class="btn btn-outline-secondary sl-cancel" data-bs-dismiss="modal"></button>' +
            '        <button type="button" class="btn btn-primary sl-print"><i class="fas fa-print me-1"></i><span></span></button>' +
            '      </div>' +
            '    </div>' +
            '  </div>' +
            '</div>'
        ).appendTo('body');

        modal = new bootstrap.Modal($modal[0]);

        // Kept as text so the field can be emptied while typing; parsed on print.
        $modal.on('input', '.sl-copies', function () {
            this.value = this.value.replace(/[^0-9]/g, '').slice(0, 3);
            refreshCopies();
        });
        $modal.on('blur', '.sl-copies', function () { setCopies(getCopies()); });
        $modal.on('keydown', '.sl-copies, .sl-width, .sl-height', function (e) {
            if (e.key === 'Enter') { e.preventDefault(); submit(); }
        });
        $modal.on('focus', '.sl-copies', function () { this.select(); });
        $modal.on('click', '.sl-dec', function () { setCopies(getCopies() - 1); });
        $modal.on('click', '.sl-inc', function () { setCopies(getCopies() + 1); });
        $modal.on('click', '.sl-preset', function () { setCopies($(this).data('copies')); });
        $modal.on('click', '.sl-print', submit);
        $modal.on('change', '.sl-printer', function () { setPrinterName($(this).val()); });
        $modal.on('change', '.sl-show-price', function () { setShowPrice(this.checked); });
        $modal.on('shown.bs.modal', function () { $modal.find('.sl-copies').trigger('focus'); });
    }

    function getCopies() { return clampCopies($modal.find('.sl-copies').val()); }

    function setCopies(n) {
        $modal.find('.sl-copies').val(clampCopies(n));
        refreshCopies();
    }

    function refreshCopies() {
        var copies = getCopies();
        $modal.find('.sl-dec').prop('disabled', copies <= 1);
        $modal.find('.sl-inc').prop('disabled', copies >= MAX_COPIES);
        $modal.find('.sl-preset').each(function () {
            var active = Number($(this).data('copies')) === copies;
            $(this).toggleClass('btn-secondary', active).toggleClass('btn-outline-secondary', !active);
        });
        // A plain browser can't reach a label printer, so there the button saves a PDF.
        $modal.find('.sl-print span').text(isDesktop()
            ? tr('print', 'Print {count} label(s)', { count: copies })
            : tr('downloadPdf', 'Download {count} label(s) as PDF', { count: copies }));
        $modal.find('.sl-print i').toggleClass('fa-print', isDesktop()).toggleClass('fa-file-pdf', !isDesktop());
    }

    function getSize() {
        return {
            widthMm: clamp($modal.find('.sl-width').val(), MIN_WIDTH_MM, MAX_WIDTH_MM, current.settings.widthMm),
            heightMm: clamp($modal.find('.sl-height').val(), MIN_HEIGHT_MM, MAX_HEIGHT_MM, current.settings.heightMm)
        };
    }

    function submit() {
        var size = getSize();
        var copies = getCopies();
        var data = current.data;
        modal.hide();
        print(data, {
            copies: copies,
            widthMm: size.widthMm,
            heightMm: size.heightMm,
            printerName: getPrinterName(),
            showPrice: $modal.find('.sl-show-price').prop('checked')
        }).catch(function (err) {
            console.error('Sticker print failed:', err);
            if (window.toastr) toastr.error(tr('failed', 'Sticker print failed') + ': ' + err.message);
        });
    }

    /** Opens the dialog for one product. `data` = { name, code, price (already formatted) }. */
    function prompt(data) {
        var code = $.trim((data && data.code) || '');
        if (!code) {
            if (window.toastr) toastr.warning(tr('missingBarcode', 'Enter or generate a barcode first.'));
            return;
        }
        data = { name: $.trim(data.name || ''), code: code, price: data.price || '' };
        try {
            buildBarcodeSvg(code);
        } catch (e) {
            if (window.toastr) toastr.error(String(e && e.message || e));
            return;
        }

        ensureModal();
        loadSettings().then(function (settings) {
            current = { data: data, settings: settings };

            $modal.find('.sl-title').text(tr('title', 'Print label'));
            $modal.find('.sl-printer-label').text(tr('printer', 'Sticker printer'));
            // Only the desktop app can send to a chosen printer; a browser always shows its own dialog.
            $modal.find('.sl-printer-row').toggle(isDesktop());
            if (isDesktop()) fillPrinterSelect($modal.find('.sl-printer'), tr('systemDefault', 'System default'));
            // Nothing to show or hide when the product has no price to print.
            $modal.find('.sl-price-row').toggle(!!data.price);
            $modal.find('.sl-price-row label').text(tr('showPrice', 'Show price on label'));
            $modal.find('.sl-show-price').prop('checked', getShowPrice());
            $modal.find('.sl-size-label').text(tr('size', 'Sticker size (mm)'));
            $modal.find('.sl-width-label').text(tr('width', 'Width'));
            $modal.find('.sl-height-label').text(tr('height', 'Height'));
            $modal.find('.sl-copies-label').text(tr('copies', 'Number of labels'));
            $modal.find('.sl-dec').attr('aria-label', tr('decrease', 'One less label'));
            $modal.find('.sl-inc').attr('aria-label', tr('increase', 'One more label'));
            $modal.find('.sl-hint').text(tr('hint', 'Up to {max} labels per job.', { max: MAX_COPIES }));
            $modal.find('.sl-cancel').text(window.i18n && window.i18n.isLoaded ? window.i18n.t('common.cancel', 'Cancel') : 'Cancel');

            $modal.find('.sl-name').text(data.name || ' ');
            $modal.find('.sl-meta').text(data.code + (data.price ? ' · ' + data.price : ''));
            $modal.find('.sl-width').val(settings.widthMm);
            $modal.find('.sl-height').val(settings.heightMm);
            setCopies(1);
            modal.show();
        });
    }

    window.StickerLabel = {
        MAX_COPIES: MAX_COPIES,
        prompt: prompt,
        print: print,
        formatPrice: formatPrice,
        isDesktop: isDesktop,
        getPrinterName: getPrinterName,
        setPrinterName: setPrinterName,
        fillPrinterSelect: fillPrinterSelect
    };
})();
