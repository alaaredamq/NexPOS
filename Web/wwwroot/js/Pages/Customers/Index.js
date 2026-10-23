// Customers Index Page Script
(function() {
    'use strict';

    // Initialize the page
    function init() {
        // Get localized strings from data attribute or global variable
        var localizedStrings = window.customersLocalizedStrings || {};
        var urls = window.customersUrls || {};

        // Wait for DataTable to initialize, then update stats
        setTimeout(function() {
            if (typeof updateCustomerStats === 'function') {
                updateCustomerStats();
            }
        }, 1500);

        // Get table reference from DataTableComponent
        var table = window.dataTable_customersTable;

        // Refresh table button
        $('#refreshTable').on('click', function() {
            if (table) {
                table.ajax.reload();
                // Update stats after refresh
                setTimeout(function() {
                    if (typeof updateCustomerStats === 'function') {
                        updateCustomerStats();
                    }
                }, 500);
            }
        });

        // Export button
        $('#exportTable').on('click', function() {
            // Show loading state
            var $btn = $(this);
            var originalHtml = $btn.html();
            $btn.prop('disabled', true).html('<i class="fas fa-spinner fa-spin"></i>');
            
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
                $btn.prop('disabled', false).html(originalHtml);
            }, 2000);
        });

        // Note: Auto-hide removed - using toastr for notifications instead
    }

    // Customer action functions
    window.viewCustomer = function(id) {
        var urls = window.customersUrls || {};
        window.location.href = (urls.detailsUrl || '') + '?id=' + id;
    };

    window.editCustomer = function(id) {
        var urls = window.customersUrls || {};
        window.location.href = (urls.editUrl || '') + '?id=' + id;
    };

    window.deleteCustomer = function(id) {
        var localizedStrings = window.customersLocalizedStrings || {};
        var urls = window.customersUrls || {};
        
        appConfirm(localizedStrings.deleteConfirm || 'Are you sure?').then(function(ok) {
            if (!ok) return;
            $.ajax({
                url: urls.deleteUrl || '',
                type: 'POST',
                data: { id: id },
                success: function(response) {
                    if (response.success) {
                        if (window.dataTable_customersTable) {
                            window.dataTable_customersTable.ajax.reload();
                        }
                        showAlert('success', response.message);
                    } else {
                        showAlert('danger', response.message);
                    }
                },
                error: function() {
                    showAlert('danger', localizedStrings.deleteError || 'An error occurred');
                }
            });
        });
    };

    window.addCustomerDebt = function(id) {
        var urls = window.customersUrls || {};
        window.openDebtTransactionDialog(id, urls.addDebtTransactionUrl || '', function () {
            if (window.dataTable_customersTable) {
                window.dataTable_customersTable.ajax.reload(null, false);
            }
        });
    };

    window.updateCustomerStats = function() {
        var getCustomersUrl = window.customersUrls?.getCustomersUrl || '/Customers/GetCustomers';
        
        // Make API call to get all customers for statistics
        $.ajax({
            url: getCustomersUrl,
            type: 'GET',
            data: { start: 0, length: 10000, searchValue: '' },
            success: function(response) {
                if (response && response.data) {
                    var all = response.data;
                    var totalCustomers = response.recordsTotal || all.length;
                    var activeCustomers = all.filter(function(c) {
                        // Handle both boolean and string values
                        return c.isActive === true || c.isActive === "Active" || c.isActive === "active";
                    }).length;
                    // Company customers - not available in current data model, set to 0
                    var companyCustomers = 0;
                    var totalRevenue = all.reduce(function(sum, c) {
                        var spent = parseFloat(c.totalSpent) || 0;
                        // Remove currency symbols if present if it's a string
                        if (typeof spent === 'string') {
                            var cleanAmount = spent.replace(/[^0-9.-]/g, '');
                            spent = parseFloat(cleanAmount) || 0;
                        }
                        return sum + spent;
                    }, 0);
                    
                    $('#totalCustomers').text(totalCustomers || 0);
                    $('#activeCustomers').text(activeCustomers || 0);
                    $('#companyCustomers').text(companyCustomers || 0);
                    $('#totalRevenue').text('$' + (totalRevenue || 0).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }));
                }
            },
            error: function(xhr, status, error) {
                console.error('Error fetching customer stats:', error);
                // Set to 0 if API fails
                $('#totalCustomers').text(0);
                $('#activeCustomers').text(0);
                $('#companyCustomers').text(0);
                $('#totalRevenue').text('$0.00');
            }
        });
    };

    function showAlert(type, message) {
        var alertHtml = `
            <div class="erp-alert erp-alert-${type} alert-dismissible fade show" role="alert">
                <i class="fas fa-${type === 'success' ? 'check-circle' : 'exclamation-circle'} me-2"></i>
                ${message}
                <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
            </div>
        `;
        $('.products-header').after(alertHtml);
    }

    window.showAlert = showAlert;

    // Initialize when document is ready
    $(document).ready(function() {
        init();
    });

})();


