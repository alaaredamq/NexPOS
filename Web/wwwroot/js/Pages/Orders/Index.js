// Orders Index Page Script
(function() {
    'use strict';

    // Initialize the page
    function init() {
        var urls = window.ordersUrls || {};
        var localizedStrings = window.ordersLocalizedStrings || {};

        loadOrderStats();
        
        // Get table reference from DataTableComponent
        var table = window.dataTable_ordersTable;
        
        // Refresh button
        $('#refreshOrdersBtn').on('click', function() {
            refreshOrdersTable();
        });
    }

    function loadOrderStats() {
        var urls = window.ordersUrls || {};
        
        $.ajax({
            url: urls.getStatsUrl || '',
            type: 'GET',
            success: function(response) {
                if (response.success) {
                    var stats = response.data;
                    $('#totalOrders').text(stats.totalOrders.toLocaleString());
                    $('#deliveredOrders').text(stats.deliveredOrders.toLocaleString());
                    $('#pendingOrders').text(stats.pendingOrders.toLocaleString());
                    $('#totalRevenue').text('$' + stats.totalRevenue.toLocaleString('en-US', {minimumFractionDigits: 2, maximumFractionDigits: 2}));
                } else {
                    console.error('Failed to load order stats:', response.message);
                    // Set fallback values
                    $('#totalOrders').text('0');
                    $('#deliveredOrders').text('0');
                    $('#pendingOrders').text('0');
                    $('#totalRevenue').text('$0.00');
                }
            },
            error: function(xhr, status, error) {
                console.error('Error loading order stats:', error);
                // Set fallback values
                $('#totalOrders').text('0');
                $('#deliveredOrders').text('0');
                $('#pendingOrders').text('0');
                $('#totalRevenue').text('$0.00');
            }
        });
    }

    window.refreshOrdersTable = function() {
        if (window.dataTable_ordersTable) {
            window.dataTable_ordersTable.ajax.reload();
        }
        loadOrderStats();
    };

    window.filterByStatus = function(status) {
        // Add status filter to DataTable
        if (window.dataTable_ordersTable) {
            window.dataTable_ordersTable.column(4).search(status).draw();
        }
    };

    window.clearFilters = function() {
        if (window.dataTable_ordersTable) {
            window.dataTable_ordersTable.search('').columns().search('').draw();
        }
    };

    window.viewOrder = function(orderId) {
        var urls = window.ordersUrls || {};
        window.location.href = (urls.detailsUrl || '') + '/' + orderId;
    };

    window.editOrderStatus = function(orderId) {
        $('#orderId').val(orderId);
        $('#updateStatusModal').modal('show');
    };

    window.saveStatusUpdate = function() {
        var urls = window.ordersUrls || {};
        var localizedStrings = window.ordersLocalizedStrings || {};
        
        var formData = {
            OrderId: $('#orderId').val(),
            Status: $('#status').val()
        };

        $.ajax({
            url: urls.editStatusUrl || '',
            type: 'POST',
            data: formData,
            headers: {
                'RequestVerificationToken': $('input[name="__RequestVerificationToken"]').val()
            },
            success: function(response) {
                $('#updateStatusModal').modal('hide');
                refreshOrdersTable();
                showSuccessMessage(localizedStrings.orderStatusUpdatedSuccessfully || 'Order status updated successfully');
            },
            error: function(xhr, status, error) {
                showErrorMessage((localizedStrings.errorUpdatingOrderStatus || 'Error updating order status') + ': ' + error);
            }
        });
    };

    // Reprint: straight to the receipt printer, no dialog or preview window.
    window.printReceipt = function(orderId) {
        var urls = window.ordersUrls || {};
        NexPrint.print((urls.printReceiptUrl || '') + '/' + orderId);
    };

    window.exportOrders = function() {
        var urls = window.ordersUrls || {};
        var localizedStrings = window.ordersLocalizedStrings || {};
        
        // Show loading state
        var exportBtn = $('button[onclick="exportOrders()"]');
        var originalHtml = exportBtn.html();
        exportBtn.prop('disabled', true).html('<i class="fas fa-spinner fa-spin"></i> ' + (localizedStrings.exporting || 'Exporting') + '...');
        
        // Create a form to submit the export request
        var form = $('<form>', {
            'method': 'POST',
            'action': urls.exportUrl || ''
        });
        
        // Add anti-forgery token
        var token = $('input[name="__RequestVerificationToken"]').val();
        if (token) {
            form.append($('<input>', {
                'type': 'hidden',
                'name': '__RequestVerificationToken',
                'value': token
            }));
        }
        
        // Submit the form
        $('body').append(form);
        form.submit();
        form.remove();
        
        // Reset button after a short delay
        setTimeout(function() {
            exportBtn.prop('disabled', false).html(originalHtml);
        }, 2000);
    };

    function showSuccessMessage(message) {
        // Implement toast notification
        if (typeof toastr !== 'undefined') {
            toastr.success(message);
        } else {
            console.log('Success:', message);
        }
    }

    function showErrorMessage(message) {
        // Implement toast notification
        if (typeof toastr !== 'undefined') {
            toastr.error(message);
        } else {
            console.log('Error:', message);
        }
    }

    function showInfoMessage(message) {
        // Implement toast notification
        if (typeof toastr !== 'undefined') {
            toastr.info(message);
        } else {
            console.log('Info:', message);
        }
    }

    window.showSuccessMessage = showSuccessMessage;
    window.showErrorMessage = showErrorMessage;
    window.showInfoMessage = showInfoMessage;

    // Initialize when document is ready
    $(document).ready(function() {
        init();
    });

})();







