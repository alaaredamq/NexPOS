/**
 * Top-bar notifications: unread badge, bell dropdown and pop-up toasts.
 *
 * Same behaviour as BoussiCell's notification store / bell / toast container, except the items come from the
 * server (GET /Notifications/GetFeed) instead of a client store, because ERP alerts are raised server-side
 * (after a sale, etc.) and are shared by every signed-in user.
 *
 *  - A new unread item pops a toast (6s, click to open, X to dismiss) once; toasts are deduped on the item key
 *    and remembered in localStorage so a refresh or a second tab does not repeat them.
 *  - Clicking an item (bell or toast) marks it read on the server and opens the thing it is about.
 */
(function () {
    'use strict';

    var POLL_MS = 30000;
    var TOAST_MS = 6000;
    var TOAST_EXIT_MS = 220;
    var MAX_TOASTS_PER_POLL = 3;
    var MAX_SEEN_KEYS = 200;
    var BELL_ITEMS = 5;
    var SEEN_STORAGE_KEY = 'nexpos-notification-toasts-seen';

    var bell = document.getElementById('notificationBell');
    if (!bell) return; // no top bar on this page (noSidebar layouts)

    var badge = document.getElementById('managementNotificationBadge');
    var list = document.getElementById('notificationList');
    var markAllBtn = document.getElementById('notificationMarkAllRead');
    var toastStack = null;
    var panelOpen = false;
    var items = [];

    function t(path, params, fallback) {
        var value = window.__translations;
        path.split('.').forEach(function (key) {
            value = value && typeof value === 'object' ? value[key] : undefined;
        });
        if (typeof value !== 'string') return fallback;
        var text = fill(value, params);
        // An alert without the product facts (e.g. one created by hand) keeps its stored text instead of showing "{productName}".
        return /\{\w+\}/.test(text) ? fallback : text;
    }

    function fill(text, params) {
        return text.replace(/\{(\w+)\}/g, function (match, key) {
            return params && params[key] !== undefined ? params[key] : match;
        });
    }

    // Stock alerts are localized from their params (like BoussiCell's kind + params); anything else shows the stored text.
    function titleOf(item) {
        return t('notifications.kinds.' + item.kind + '.title', item.params, item.title);
    }

    function messageOf(item) {
        return t('notifications.kinds.' + item.kind + '.message', item.params, item.message);
    }

    function relativeTime(createdAt) {
        // The server sends UTC without a zone suffix; without "Z" the browser would read it as local time.
        var iso = /(Z|[+-]\d\d:?\d\d)$/.test(createdAt) ? createdAt : createdAt + 'Z';
        var diffSec = Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 1000));
        if (diffSec < 60) return t('notifications.time.justNow', null, 'Just now');
        var diffMin = Math.floor(diffSec / 60);
        if (diffMin < 60) return t('notifications.time.minutesAgo', { count: diffMin }, diffMin + ' min ago');
        var diffHour = Math.floor(diffMin / 60);
        if (diffHour < 24) return t('notifications.time.hoursAgo', { count: diffHour }, diffHour + ' h ago');
        var diffDay = Math.floor(diffHour / 24);
        return t('notifications.time.daysAgo', { count: diffDay }, diffDay + ' d ago');
    }

    function el(tag, className, text) {
        var node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    }

    // ----- seen-toast memory (BoussiCell: seenDedupeKeys) ---------------------------------------------------

    function readSeen() {
        try {
            var raw = localStorage.getItem(SEEN_STORAGE_KEY);
            return raw ? JSON.parse(raw) : null;
        } catch (e) {
            return null;
        }
    }

    function writeSeen(keys) {
        try {
            localStorage.setItem(SEEN_STORAGE_KEY, JSON.stringify(keys.slice(-MAX_SEEN_KEYS)));
        } catch (e) {
            // Private mode / quota: toasts may repeat after a refresh, nothing else breaks.
        }
    }

    // ----- server calls ----------------------------------------------------------------------------------

    function post(url, data) {
        return $.post(url, data);
    }

    function markRead(item) {
        item.isRead = true;
        return post('/Notifications/MarkFeedItemRead', { source: item.source, id: item.id });
    }

    function open(item) {
        var go = function () { window.location.href = item.actionUrl; };
        if (item.isRead) return go();
        markRead(item).always(go);
    }

    // ----- bell ---------------------------------------------------------------------------------------

    function renderBadge(unreadCount) {
        if (!badge) return;
        if (unreadCount > 0) {
            badge.textContent = unreadCount > 9 ? '9+' : String(unreadCount);
            badge.hidden = false;
        } else {
            badge.hidden = true;
        }
        if (markAllBtn) markAllBtn.hidden = unreadCount === 0;
    }

    function renderList() {
        list.textContent = '';
        if (items.length === 0) {
            list.appendChild(el('div', 'notif-empty', t('notifications.empty', null, 'You\'re all caught up')));
            return;
        }

        items.slice(0, BELL_ITEMS).forEach(function (item) {
            var row = el('button', 'notif-item');
            row.type = 'button';
            row.dataset.unread = String(!item.isRead);
            row.dataset.kind = item.kind;

            var body = el('span', 'notif-item-body');
            body.appendChild(el('span', 'notif-item-title', titleOf(item)));
            body.appendChild(el('span', 'notif-item-message', messageOf(item)));
            body.appendChild(el('span', 'notif-item-time', relativeTime(item.createdAt)));

            row.appendChild(el('span', 'notif-dot'));
            row.appendChild(body);
            row.addEventListener('click', function () { open(item); });
            list.appendChild(row);
        });
    }

    // ----- toasts ------------------------------------------------------------------------------------

    function dismissToast(node) {
        if (!node || node.dataset.leaving) return;
        node.dataset.leaving = 'true';
        setTimeout(function () { node.remove(); }, TOAST_EXIT_MS);
    }

    function showToast(item) {
        if (!toastStack) {
            toastStack = el('div', 'notif-toast-stack');
            toastStack.setAttribute('aria-live', 'polite');
            document.body.appendChild(toastStack);
        }

        var toast = el('div', 'notif-toast');
        toast.dataset.kind = item.kind;

        var body = el('button', 'notif-toast-body');
        body.type = 'button';
        body.appendChild(el('span', 'notif-item-title', titleOf(item)));
        body.appendChild(el('span', 'notif-item-message', messageOf(item)));
        body.addEventListener('click', function () {
            dismissToast(toast);
            open(item);
        });

        var close = el('button', 'notif-toast-close', '✕');
        close.type = 'button';
        close.setAttribute('aria-label', t('notifications.dismiss', null, 'Dismiss'));
        close.addEventListener('click', function () { dismissToast(toast); });

        toast.appendChild(el('span', 'notif-dot'));
        toast.appendChild(body);
        toast.appendChild(close);
        toastStack.appendChild(toast);
        setTimeout(function () { dismissToast(toast); }, TOAST_MS);
    }

    function dismissAllToasts() {
        if (toastStack) toastStack.querySelectorAll('.notif-toast').forEach(dismissToast);
    }

    function raiseToasts() {
        var seen = readSeen() || [];
        var fresh = items.filter(function (item) { return !item.isRead && seen.indexOf(item.key) === -1; });
        if (fresh.length === 0) return;

        // The toasts sit over the dropdown's header, so while it is open the list itself is the notification.
        if (!panelOpen) {
            // Oldest first so the newest ends up at the bottom of the stack; cap the burst (first visit can have many).
            fresh.slice(0, MAX_TOASTS_PER_POLL).reverse().forEach(showToast);
        }
        writeSeen(seen.concat(fresh.map(function (item) { return item.key; })));
    }

    // ----- polling -------------------------------------------------------------------------------------

    function refresh() {
        return $.get('/Notifications/GetFeed', { take: BELL_ITEMS }).done(function (response) {
            if (!response || !response.success) return;
            items = response.data.items || [];
            renderBadge(response.data.unreadCount);
            renderList();
            raiseToasts();
        });
        // A failed poll (offline, session expired) is silent on purpose: the next tick tries again.
    }

    function markAllRead() {
        post('/Notifications/MarkAllRead').done(function () {
            items.forEach(function (item) { item.isRead = true; });
            renderBadge(0);
            renderList();
        });
    }

    function start() {
        if (markAllBtn) {
            markAllBtn.textContent = t('notifications.markAllRead', null, 'Mark all read');
            markAllBtn.addEventListener('click', markAllRead);
        }
        bell.addEventListener('show.bs.dropdown', function () {
            panelOpen = true;
            dismissAllToasts();
        });
        bell.addEventListener('hidden.bs.dropdown', function () { panelOpen = false; });
        renderList();
        refresh();
        setInterval(refresh, POLL_MS);
        document.addEventListener('visibilitychange', function () {
            if (document.visibilityState === 'visible') refresh();
        });
    }

    window.NexNotifications = { refresh: refresh };

    if (window.jQuery) {
        $(start);
    }
})();
