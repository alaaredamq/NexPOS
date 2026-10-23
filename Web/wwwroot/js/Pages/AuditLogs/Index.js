// Audit Log viewer
// Everything shown here can contain text typed by users (product names, user names, ...), so values are
// only ever inserted as text (esc() / .text()), never as raw HTML.
(function () {
    'use strict';

    var S = window.auditStrings || {};
    var L = window.auditLabels || {};
    var urls = window.auditUrls || {};
    var locale = window.auditLang === 'ar' ? 'ar' : 'en';

    var table = null;
    var correlationId = '';   // set when drilling into one request
    var currentEntry = null;  // the entry open in the details dialog

    // ---- helpers -------------------------------------------------------------------------------

    function esc(value) {
        return $('<div>').text(value == null ? '' : String(value)).html();
    }

    function pad(n) { return n < 10 ? '0' + n : '' + n; }

    // Stored as UTC; shown in the computer's own time zone, unambiguous year-first format in both languages.
    function formatTime(iso) {
        if (!iso) return '';
        var d = new Date(iso);
        if (isNaN(d.getTime())) return esc(iso);
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) + ' ' +
            pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
    }

    function toUtc(localValue, addSeconds) {
        if (!localValue) return '';
        var d = new Date(localValue);
        if (isNaN(d.getTime())) return '';
        if (addSeconds) d = new Date(d.getTime() + addSeconds * 1000);
        return d.toISOString();
    }

    function toLocalInput(date) {
        return date.getFullYear() + '-' + pad(date.getMonth() + 1) + '-' + pad(date.getDate()) + 'T' +
            pad(date.getHours()) + ':' + pad(date.getMinutes());
    }

    var CATEGORY_ICON = {
        Data: 'fa-database', Request: 'fa-globe', Security: 'fa-user-shield',
        Business: 'fa-cash-register', System: 'fa-cog', Diagnostic: 'fa-bug'
    };
    var OUTCOME_ICON = { Success: 'fa-check-circle', Failure: 'fa-times-circle', Denied: 'fa-ban' };

    function categoryBadge(category) {
        var label = S['Cat_' + category] || category;
        return '<span class="audit-badge cat-' + esc(String(category).toLowerCase()) + '">' +
            '<i class="fas ' + (CATEGORY_ICON[category] || 'fa-circle') + '"></i>' + esc(label) + '</span>';
    }

    function outcomeBadge(outcome, severity) {
        var label = S['Out_' + outcome] || outcome;
        var html = '<span class="audit-outcome out-' + esc(String(outcome).toLowerCase()) + '">' +
            '<i class="fas ' + (OUTCOME_ICON[outcome] || 'fa-circle') + '"></i>' + esc(label) + '</span>';
        if (severity === 'Warning' || severity === 'Error' || severity === 'Critical') {
            html += ' <span class="audit-sev sev-' + esc(severity.toLowerCase()) + '">' + esc(S['Sev_' + severity] || severity) + '</span>';
        }
        return html;
    }

    // ---- filters --------------------------------------------------------------------------------

    function readFilters() {
        var filters = {
            search: $.trim($('#fSearch').val()),
            from: toUtc($('#fFrom').val(), 0),
            to: toUtc($('#fTo').val(), 60),   // "to 14:30" includes the whole of 14:30
            userId: $('#fUser').val(),
            category: $('#fCategory').val(),
            outcome: $('#fOutcome').val(),
            minSeverity: $('#fSeverity').val(),
            entityType: $('#fEntityType').val(),
            entityId: $.trim($('#fEntityId').val()),
            action: $('#fAction').val(),
            correlationId: correlationId
        };

        // A user without an id (a failed sign-in for an unknown name) can only be found by name.
        var selected = $('#fUser option:selected');
        if (filters.userId === '' && selected.val() === '' && selected.data('username')) {
            filters.username = selected.data('username');
        }

        Object.keys(filters).forEach(function (key) {
            if (filters[key] === '' || filters[key] == null) delete filters[key];
        });
        return filters;
    }

    function renderChips() {
        var $chips = $('#auditChips').empty();
        if (!correlationId) return;

        var $chip = $('<span class="audit-chip"></span>');
        $chip.append($('<i class="fas fa-link me-1"></i>'));
        $chip.append($('<span></span>').text((S.Chip_Request || 'Request') + ': ' + correlationId));
        $chip.append(
            $('<button type="button" class="btn-close btn-close-sm ms-2"></button>')
                .attr('aria-label', S.Chip_Remove || 'Remove')
                .on('click', function () { correlationId = ''; reload(); })
        );
        $chips.append($chip);
    }

    function reload() {
        renderChips();
        if (table) table.ajax.reload();
    }

    function resetFilters() {
        $('#auditFilterForm')[0].reset();
        $('#fUser, #fCategory, #fOutcome, #fSeverity, #fEntityType, #fAction').val('');
        correlationId = '';
        table.order([0, 'desc']);
        reload();
    }

    function applyRange(range) {
        var now = new Date();
        var from = null;
        if (range === 'hour') from = new Date(now.getTime() - 3600 * 1000);
        else if (range === 'today') from = new Date(now.getFullYear(), now.getMonth(), now.getDate());
        else if (range === '7d') from = new Date(now.getTime() - 7 * 86400 * 1000);
        else if (range === '30d') from = new Date(now.getTime() - 30 * 86400 * 1000);

        $('#fFrom').val(from ? toLocalInput(from) : '');
        $('#fTo').val('');
        reload();
    }

    // Pre-fill from the address (?correlationId=..&entityType=..&entityId=..&userId=..&search=..) so other
    // screens, or a support engineer, can link straight to a view of the log.
    function applyQueryString() {
        var q = new URLSearchParams(window.location.search);
        if (q.get('search')) $('#fSearch').val(q.get('search'));
        if (q.get('userId')) $('#fUser').val(q.get('userId'));
        if (q.get('category')) $('#fCategory').val(q.get('category'));
        if (q.get('entityType')) $('#fEntityType').val(q.get('entityType'));
        if (q.get('entityId')) $('#fEntityId').val(q.get('entityId'));
        if (q.get('correlationId')) correlationId = q.get('correlationId');
    }

    // ---- table ----------------------------------------------------------------------------------

    function initTable() {
        table = $('#auditTable').DataTable({
            serverSide: true,
            processing: true,
            searching: false,
            lengthMenu: [25, 50, 100, 200],
            pageLength: 50,
            order: [[0, 'desc']],
            columnDefs: [{ orderable: false, targets: [1, 2, 3, 4, 5, 6] }],
            language: {
                processing: S.Tbl_Processing,
                emptyTable: S.Tbl_Empty,
                zeroRecords: S.Tbl_Empty,
                info: S.Tbl_Info,
                infoEmpty: S.Tbl_InfoEmpty,
                infoFiltered: S.Tbl_InfoFiltered,
                lengthMenu: S.Tbl_LengthMenu,
                paginate: { first: S.Tbl_First, last: S.Tbl_Last, next: S.Tbl_Next, previous: S.Tbl_Previous }
            },
            ajax: function (request, callback) {
                var params = $.extend({}, readFilters(), {
                    draw: request.draw,
                    start: request.start,
                    length: request.length,
                    ascending: !!(request.order && request.order.length && request.order[0].dir === 'asc')
                });

                $.getJSON(urls.data, params)
                    .done(function (response) {
                        if (response.error) {
                            toastr.error(S.Msg_LoadFailed);
                            callback({ draw: request.draw, recordsTotal: 0, recordsFiltered: 0, data: [] });
                            return;
                        }
                        callback(response);
                    })
                    .fail(function () {
                        toastr.error(S.Msg_LoadFailed);
                        callback({ draw: request.draw, recordsTotal: 0, recordsFiltered: 0, data: [] });
                    });
            },
            columns: [
                // dir="ltr": in the Arabic (RTL) page the bidi algorithm would otherwise print "00:01:48 2026-10-24".
                { data: 'time', className: 'audit-time', render: function (v, type) { return type === 'display' ? '<span dir="ltr">' + formatTime(v) + '</span>' : v; } },
                {
                    data: 'user',
                    render: function (v, type, row) {
                        if (type !== 'display') return v;
                        if (!v) return '<span class="text-muted">—</span>';
                        var system = v === 'System';
                        return '<div class="audit-user"><i class="fas ' + (system ? 'fa-cog' : 'fa-user') + ' me-1 text-muted"></i>' +
                            esc(system ? (S.Msg_System || v) : v) + '</div>' +
                            (row.role ? '<div class="audit-role">' + esc(row.role) + '</div>' : '');
                    }
                },
                { data: 'category', render: function (v, type) { return type === 'display' ? categoryBadge(v) : v; } },
                { data: 'action', render: function (v, type) { return type === 'display' ? '<code class="audit-action">' + esc(v) + '</code>' : v; } },
                {
                    data: 'entityType',
                    render: function (v, type, row) {
                        if (type !== 'display') return v;
                        if (row.category === 'Request') return '';
                        var parts = '<span class="audit-entity">' + esc(v) + '</span>';
                        if (row.entityId) parts += ' <span class="text-muted">#' + esc(row.entityId) + '</span>';
                        if (row.label) parts += '<div class="audit-label">' + esc(row.label) + '</div>';
                        return parts;
                    }
                },
                {
                    data: 'description',
                    render: function (v, type) {
                        if (type !== 'display') return v;
                        return '<div class="audit-desc" dir="auto" title="' + esc(v) + '">' + esc(v) + '</div>';
                    }
                },
                { data: 'outcome', render: function (v, type, row) { return type === 'display' ? outcomeBadge(v, row.severity) : v; } }
            ],
            createdRow: function (row, data) {
                $(row).addClass('audit-row audit-sev-' + String(data.severity).toLowerCase())
                    .attr('data-id', data.id).attr('tabindex', 0);
            }
        });

        $('#auditTable tbody').on('click', 'tr.audit-row', function () { showDetails($(this).data('id')); });
        $('#auditTable tbody').on('keydown', 'tr.audit-row', function (e) {
            if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); showDetails($(this).data('id')); }
        });
    }

    // ---- details --------------------------------------------------------------------------------

    function fact(label, value) {
        if (value === null || value === undefined || value === '') return null;
        // Ids, paths, addresses and versions are technical text: keep them left-to-right even on the Arabic page.
        return $('<div class="row"></div>')
            .append($('<dt class="col-sm-3"></dt>').text(label))
            .append($('<dd class="col-sm-9"></dd>').append($('<span dir="ltr"></span>').text(value)));
    }

    function renderChanges(entry) {
        var $box = $('#dChanges').empty();
        var $heading = $box.prev('.audit-section');
        var changes = entry.changes || [];

        // "What changed" only means something for a change to a record. A request, a sign-in or an error has no
        // old/new values, so "no field-level changes were recorded" there is just noise: hide the section.
        var applicable = changes.length > 0 || entry.category === 'Data';
        $heading.toggle(applicable);
        $box.toggle(applicable);
        if (!applicable) return;

        if (changes.length === 0) {
            $box.append($('<p class="text-muted mb-0"></p>').text(L.noChanges || ''));
            return;
        }

        var hasBefore = changes.some(function (c) { return c.oldValue !== null && c.oldValue !== undefined; });
        var hasAfter = changes.some(function (c) { return c.newValue !== null && c.newValue !== undefined; });
        var isDiff = hasBefore && hasAfter;

        var $table = $('<table class="table table-sm audit-diff mb-0"></table>');
        var $head = $('<tr></tr>').append($('<th></th>').text(S.Detail_Field));
        if (isDiff) $head.append($('<th></th>').text(S.Detail_Before)).append($('<th></th>').text(S.Detail_After));
        else $head.append($('<th></th>').text(hasBefore ? S.Detail_Before : S.Detail_Value));
        $table.append($('<thead></thead>').append($head));

        var $body = $('<tbody></tbody>');
        changes.forEach(function (c) {
            var $row = $('<tr></tr>').append($('<td class="audit-field"></td>').text(c.field));
            if (isDiff) {
                $row.append($('<td class="audit-old"></td>').text(c.oldValue == null ? '∅' : c.oldValue));
                $row.append($('<td class="audit-new"></td>').text(c.newValue == null ? '∅' : c.newValue));
            } else {
                var v = hasBefore ? c.oldValue : c.newValue;
                $row.append($('<td class="audit-plain"></td>').text(v == null ? '∅' : v));
            }
            $body.append($row);
        });

        $box.append($('<div class="table-responsive"></div>').append($table.append($body)));
    }

    function renderEntry(entry) {
        currentEntry = entry;

        $('#dId').text('#' + entry.id);
        $('#dBadges').html(categoryBadge(entry.category) + ' <code class="audit-action">' + esc(entry.action) + '</code> ' +
            outcomeBadge(entry.outcome, entry.severity));
        // dir="auto": the text is whatever language the data was typed in, so let the browser pick its direction.
        $('#dDescription').attr('dir', 'auto').text(entry.description || '');

        $('#dWhen').empty().append($('<span dir="ltr"></span>').text(formatTime(entry.time)));
        $('#dWho').attr('dir', 'auto').text((entry.user === 'System' ? (S.Msg_System || 'System') : (entry.user || '—')) + (entry.role ? ' · ' + entry.role : ''));
        var record = entry.entityType || '';
        if (entry.entityId) record += ' #' + entry.entityId;
        if (entry.label) record += ' · ' + entry.label;
        $('#dRecord').attr('dir', 'auto').text(record || '—');
        // A request isn't about a record ("Http" is just the bucket it is filed under), so don't show a Record card -
        // and let the two cards that remain share the row instead of leaving a gap.
        var aboutARecord = entry.category !== 'Request';
        $('#dRecord').closest('[class*="col-md-"]').toggle(aboutARecord);
        $('#dWhen, #dWho').closest('[class*="col-md-"]')
            .removeClass('col-md-4 col-md-6').addClass(aboutARecord ? 'col-md-4' : 'col-md-6');

        renderChanges(entry);

        if (entry.additional) {
            $('#dExtra').text(entry.additional);
            $('#dExtraWrap').removeClass('d-none');
        } else {
            $('#dExtraWrap').addClass('d-none');
        }

        var $meta = $('#dMeta').empty();
        [
            fact(L.requestId, entry.correlationId),
            fact(L.method, entry.method),
            fact(L.path, entry.path),
            fact(L.status, entry.status),
            fact(L.duration, entry.durationMs == null ? null : entry.durationMs + ' ms'),
            fact(L.ip, entry.ip),
            fact(L.machine, entry.machine),
            fact(L.version, entry.version),
            fact(L.source, entry.source),
            fact(L.agent, entry.userAgent)
        ].forEach(function (row) { if (row) $meta.append(row); });

        // Drill-down buttons only make sense when there is something to drill into.
        $('#dShowRequest').toggle(!!entry.correlationId)
            .empty()
            .append('<i class="fas fa-link me-1"></i>')
            .append(document.createTextNode((S.Detail_ThisRequest || '').replace('{0}', entry.rowsInSameRequest)));
        $('#dShowRecord').toggle(!!entry.entityId && entry.category !== 'Request');
        $('#dShowUser').toggle(!!entry.user);
        $('#dCopyRequest').toggle(!!entry.correlationId);
    }

    function showDetails(id) {
        $.getJSON(urls.details + '/' + id)
            .done(function (entry) {
                if (!entry || !entry.success) { toastr.error(S.Msg_DetailFailed); return; }
                renderEntry(entry);
                bootstrap.Modal.getOrCreateInstance(document.getElementById('auditDetailModal')).show();
            })
            .fail(function () { toastr.error(S.Msg_DetailFailed); });
    }

    function closeDetails() {
        var modal = bootstrap.Modal.getInstance(document.getElementById('auditDetailModal'));
        if (modal) modal.hide();
    }

    // "Everything in this request": the whole story of one click, oldest first so it reads top to bottom.
    function drillRequest() {
        if (!currentEntry || !currentEntry.correlationId) return;
        $('#auditFilterForm')[0].reset();
        $('#fUser, #fCategory, #fOutcome, #fSeverity, #fEntityType, #fAction').val('');
        correlationId = currentEntry.correlationId;
        table.order([0, 'asc']);
        closeDetails();
        reload();
    }

    function drillRecord() {
        if (!currentEntry || !currentEntry.entityId) return;
        $('#auditFilterForm')[0].reset();
        $('#fUser, #fCategory, #fOutcome, #fSeverity, #fAction').val('');
        correlationId = '';
        $('#fEntityType').val(currentEntry.entityType);
        $('#fEntityId').val(currentEntry.entityId);
        table.order([0, 'desc']);
        closeDetails();
        reload();
    }

    function drillUser() {
        if (!currentEntry || !currentEntry.user) return;
        $('#auditFilterForm')[0].reset();
        $('#fCategory, #fOutcome, #fSeverity, #fEntityType, #fAction').val('');
        correlationId = '';
        if (currentEntry.userId && $('#fUser option[value="' + currentEntry.userId + '"]').length) {
            $('#fUser').val(String(currentEntry.userId));
        } else {
            $('#fUser').val('');
            $('#fSearch').val(currentEntry.user);
        }
        table.order([0, 'desc']);
        closeDetails();
        reload();
    }

    function copyRequestId() {
        if (!currentEntry || !currentEntry.correlationId) return;
        var text = currentEntry.correlationId;
        var done = function () { toastr.success(S.Detail_Copied || 'Copied'); };
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(done, function () { fallbackCopy(text); done(); });
        } else {
            fallbackCopy(text);
            done();
        }
    }

    function fallbackCopy(text) {
        var $tmp = $('<textarea></textarea>').val(text).css({ position: 'fixed', opacity: 0 }).appendTo('body');
        $tmp[0].select();
        try { document.execCommand('copy'); } catch (e) { /* nothing more to try */ }
        $tmp.remove();
    }

    // ---- wiring ---------------------------------------------------------------------------------

    $(function () {
        applyQueryString();
        initTable();
        renderChips();

        $('#auditApplyBtn').on('click', reload);
        // Enter in any filter box applies the filter (the form has no submit button, so the browser won't do it).
        $('#auditFilterForm').on('submit', function (e) { e.preventDefault(); })
            .on('keydown', 'input', function (e) { if (e.key === 'Enter') { e.preventDefault(); reload(); } });
        $('#auditResetBtn').on('click', resetFilters);
        $('#auditRefreshBtn').on('click', reload);
        $('.audit-ranges [data-range]').on('click', function () { applyRange($(this).data('range')); });

        $('#auditExportBtn').on('click', function () {
            var params = $.param($.extend({}, readFilters(), {
                ascending: !!(table.order().length && table.order()[0][1] === 'asc')
            }));
            window.location.href = urls.exportCsv + (params ? '?' + params : '');
        });

        $('#dShowRequest').on('click', drillRequest);
        $('#dShowRecord').on('click', drillRecord);
        $('#dShowUser').on('click', drillUser);
        $('#dCopyRequest').on('click', copyRequestId);
    });
})();
