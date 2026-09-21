// Receipts Index Page Script
(function() {
    'use strict';

    let currentReceiptId = null;
    let receiptsTable;
    let recentReceiptsTable;
    let voidReceiptsTable;

    // Initialize the page
    function init() {
        var urls = window.receiptsUrls || {};
        var localizedStrings = window.receiptsLocalizedStrings || {};

        // Get table references from DataTableComponent
        receiptsTable = window.dataTable_receiptsTable;
        recentReceiptsTable = window.dataTable_recentReceiptsTable;
        voidReceiptsTable = window.dataTable_voidReceiptsTable;

        // Set default dates
        const today = new Date().toISOString().split('T')[0];
        const lastWeek = new Date(Date.now() - 7 * 24 * 60 * 60 * 1000).toISOString().split('T')[0];
        $('#startDate').val(lastWeek);
        $('#endDate').val(today);

        // Load initial data
        loadReceiptStats();
        loadAnalytics();
    }

    function loadReceiptStats() {
        var urls = window.receiptsUrls || {};
        
        $.ajax({
            url: urls.getStatsUrl || '',
            type: 'GET',
            success: function(stats) {
                if (stats) {
                    $('#totalSales').text('$' + stats.totalSales.toLocaleString());
                    $('#totalReceipts').text(stats.totalReceipts);
                    $('#voidReceipts').text(stats.voidReceipts);
                    $('#averageReceipt').text('$' + stats.averageReceiptValue.toLocaleString());
                }
            },
            error: function() {
                console.log('Failed to load receipt stats');
            }
        });
    }

    function loadAnalytics() {
        var urls = window.receiptsUrls || {};
        
        $.ajax({
            url: urls.getStatsUrl || '',
            type: 'GET',
            success: function(stats) {
                if (typeof Chart === 'undefined') {
                    console.warn('Chart.js is not loaded');
                    return;
                }

                // Payment Methods Chart
                const paymentCtx = document.getElementById('paymentMethodsChart');
                if (paymentCtx) {
                    new Chart(paymentCtx.getContext('2d'), {
                        type: 'doughnut',
                        data: {
                            labels: Object.keys(stats.paymentMethodBreakdown || {}),
                            datasets: [{
                                data: Object.values(stats.paymentMethodBreakdown || {}),
                                backgroundColor: ['#0aa2a2', '#20c997', '#17a2b8', '#ffc107', '#dc3545']
                            }]
                        },
                        options: {
                            responsive: true,
                            maintainAspectRatio: false
                        }
                    });
                }

                // Daily Sales Chart
                const salesCtx = document.getElementById('dailySalesChart');
                if (salesCtx) {
                    new Chart(salesCtx.getContext('2d'), {
                        type: 'line',
                        data: {
                            labels: Object.keys(stats.dailySales || {}),
                            datasets: [{
                                label: 'Daily Sales',
                                data: Object.values(stats.dailySales || {}),
                                borderColor: '#0aa2a2',
                                backgroundColor: 'rgba(10, 162, 162, 0.1)',
                                tension: 0.4,
                                fill: true
                            }]
                        },
                        options: {
                            responsive: true,
                            maintainAspectRatio: false,
                            scales: {
                                y: {
                                    beginAtZero: true,
                                    ticks: {
                                        callback: function(value) {
                                            return '$' + value.toLocaleString();
                                        }
                                    }
                                }
                            }
                        }
                    });
                }
            },
            error: function() {
                console.log('Failed to load analytics');
            }
        });
    }

    window.searchReceipts = function() {
        var localizedStrings = window.receiptsLocalizedStrings || {};
        const searchTerm = $('#searchTerm').val();
        if (!searchTerm.trim()) {
            if (typeof toastr !== 'undefined') {
                toastr.warning(localizedStrings.pleaseEnterASearchTerm || 'Please enter a search term');
            }
            return;
        }

        // Use DataTable search functionality
        if (receiptsTable) {
            receiptsTable.search(searchTerm).draw();
        }
        $('#searchModal').modal('hide');
    };

    window.filterByDateRange = function() {
        var urls = window.receiptsUrls || {};
        var localizedStrings = window.receiptsLocalizedStrings || {};
        
        const startDate = $('#startDate').val();
        const endDate = $('#endDate').val();
        
        if (!startDate || !endDate) {
            if (typeof toastr !== 'undefined') {
                toastr.warning(localizedStrings.pleaseSelectBothStartAndEndDates || 'Please select both start and end dates');
            }
            return;
        }

        // Reload table with date range parameters
        if (receiptsTable) {
            receiptsTable.ajax.url((urls.getReceiptsUrl || '') + '?startDate=' + startDate + '&endDate=' + endDate).load();
        }
        $('#dateRangeModal').modal('hide');
    };

    window.generateActionButtons = function(receiptId, isVoid) {
        let buttons = `
            <div class="action-buttons">
                <button class="btn btn-sm btn-outline-primary" onclick="viewReceipt(${receiptId})" title="View Details">
                    <i class="fas fa-eye"></i>
                </button>
                <button class="btn btn-sm btn-outline-success" onclick="downloadPdf(${receiptId})" title="Download PDF">
                    <i class="fas fa-download"></i>
                </button>
        `;
        
        if (!isVoid) {
            buttons += `
                <button class="btn btn-sm btn-outline-danger" onclick="voidReceipt(${receiptId})" title="Void Receipt">
                    <i class="fas fa-ban"></i>
                </button>
            `;
        }
        
        buttons += `</div>`;
        return buttons;
    };

    window.viewReceipt = function(receiptId) {
        var urls = window.receiptsUrls || {};
        window.location.href = (urls.detailsUrl || '') + '/' + receiptId;
    };

    window.downloadPdf = function(receiptId) {
        var urls = window.receiptsUrls || {};
        window.open((urls.downloadPdfUrl || '') + '/' + receiptId, '_blank');
    };

    window.voidReceipt = function(receiptId) {
        currentReceiptId = receiptId;
        $('#voidReceiptModal').modal('show');
    };

    window.confirmVoidReceipt = function() {
        var urls = window.receiptsUrls || {};
        var localizedStrings = window.receiptsLocalizedStrings || {};
        
        const reason = $('#voidReason').val();
        if (!reason.trim()) {
            if (typeof toastr !== 'undefined') {
                toastr.warning(localizedStrings.pleaseEnterAReasonForVoiding || 'Please enter a reason for voiding');
            }
            return;
        }

        $.ajax({
            url: (urls.voidReceiptUrl || '') + '/' + currentReceiptId,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ reason: reason }),
            success: function(response) {
                if (response.success) {
                    if (typeof toastr !== 'undefined') {
                        toastr.success(localizedStrings.receiptVoidedSuccessfully || 'Receipt voided successfully');
                    }
                    $('#voidReceiptModal').modal('hide');
                    $('#voidReason').val('');
                    // Reload tables
                    loadReceiptStats();
                    if (receiptsTable) {
                        receiptsTable.ajax.reload();
                    }
                    if (recentReceiptsTable) {
                        recentReceiptsTable.ajax.reload();
                    }
                    if (voidReceiptsTable) {
                        voidReceiptsTable.ajax.reload();
                    }
                } else {
                    if (typeof toastr !== 'undefined') {
                        toastr.error(response.message);
                    }
                }
            },
            error: function() {
                if (typeof toastr !== 'undefined') {
                    toastr.error(localizedStrings.failedToVoidReceipt || 'Failed to void receipt');
                }
            }
        });
    };

    window.exportReceipts = function() {
        // Implementation for exporting receipts
        if (typeof toastr !== 'undefined') {
            toastr.info(window.receiptsLocalizedStrings?.exportFunctionalityWillBeImplemented || 'Export functionality will be implemented');
        }
    };

    // Initialize when document is ready
    $(document).ready(function() {
        init();
    });

})();







