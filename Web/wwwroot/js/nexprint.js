/**
 * Receipt-printer printing for the whole app. There is no browser print dialog anywhere:
 * a document either goes straight to the receipt printer chosen in Settings, or is downloaded
 * as a PDF - both are produced on the server from the same content.
 *
 *   <button data-nexprint-url="/Orders/PrintReceipt/5">Print</button>   POSTs, toasts the result
 *   <a href="/Orders/ReceiptPdf/5" download>PDF</a>                      plain download
 *
 * When the printer can't be reached the toast offers the PDF link instead, so a failed print
 * is never silent and never opens a stray window.
 */
(function () {
    'use strict';

    var inFlight = {};

    function tr(key, fallback) {
        return window.i18n && window.i18n.isLoaded ? window.i18n.t(key, fallback) : fallback;
    }

    function escapeHtml(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    /** Only same-site paths are ever put into a link. */
    function isLocalPath(url) {
        return typeof url === 'string' && url.charAt(0) === '/' && url.charAt(1) !== '/';
    }

    function toast(type, message, title, options) {
        if (window.toastr) {
            window.toastr[type](message, title, options);
        } else {
            console[type === 'error' ? 'error' : 'log'](message);
        }
    }

    function showFailure(res) {
        var message = escapeHtml(tr('print.failed', 'Could not print. Check that the receipt printer is connected and selected in Settings.'));
        if (res && isLocalPath(res.pdfUrl)) {
            message += '<br><a href="' + escapeHtml(res.pdfUrl) + '" download class="fw-bold text-white text-decoration-underline">' +
                escapeHtml(tr('print.downloadInstead', 'Download PDF instead')) + '</a>';
        }
        toast('error', message, tr('print.title', 'Printing'), { timeOut: 12000, extendedTimeOut: 6000, closeButton: true });
    }

    /**
     * Sends the print job to the server. Resolves true when the printer accepted it. A second
     * click on the same button while the first job is still going is ignored, so a slow printer
     * can't produce duplicate slips.
     */
    function print(url) {
        if (!isLocalPath(url) || inFlight[url]) return Promise.resolve(false);
        inFlight[url] = true;

        return fetch(url, { method: 'POST', credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (response) { return response.json(); })
            .then(function (res) {
                if (res && res.success) {
                    toast('success', tr('print.sent', 'Sent to the receipt printer'), tr('print.title', 'Printing'), { timeOut: 2500 });
                    return true;
                }
                showFailure(res);
                return false;
            })
            .catch(function () {
                showFailure(null);
                return false;
            })
            .then(function (ok) {
                delete inFlight[url];
                return ok;
            });
    }

    /**
     * Downloads a PDF without opening a window (window.open would leave an empty app window
     * behind in the desktop app).
     */
    function download(url) {
        if (!isLocalPath(url)) return;
        var link = document.createElement('a');
        link.href = url;
        link.download = '';
        link.style.display = 'none';
        document.body.appendChild(link);
        link.click();
        link.remove();
    }

    // Delegated, so it also works for buttons that DataTables renders later.
    document.addEventListener('click', function (e) {
        var trigger = e.target.closest && e.target.closest('[data-nexprint-url]');
        if (!trigger) return;
        e.preventDefault();
        print(trigger.getAttribute('data-nexprint-url'));
    });

    window.NexPrint = { print: print, download: download };
})();
