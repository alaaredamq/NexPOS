// Suppliers Index Page Script
(function() {
    'use strict';

    // Initialize the page
    function init() {
        // Get localized strings from data attribute or global variable
        var localizedStrings = window.suppliersLocalizedStrings || {};
        var urls = window.suppliersUrls || {};

        // Get table reference from DataTableComponent
        var table = window.dataTable_suppliersTable;

        // Refresh table button
        $('#refreshTable').on('click', function() {
            if (table) {
                table.ajax.reload();
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
    }

    // Supplier action functions
    window.viewSupplier = function(id) {
        var urls = window.suppliersUrls || {};
        var detailsUrl = urls.detailsUrl || '/Suppliers/Details';
        window.location.href = detailsUrl + '?id=' + id;
    };

    window.editSupplier = function(id) {
        var urls = window.suppliersUrls || {};
        var editUrl = urls.editUrl || '/Suppliers/Edit';
        window.location.href = editUrl + '?id=' + id;
    };

    window.deleteSupplier = function(id) {
        var urls = window.suppliersUrls || {};
        
        // Use i18n for confirmation message
        var confirmMessage = window.i18n && window.i18n.t ? 
            window.i18n.t('common.deleteConfirm', 'Are you sure you want to delete this supplier?') : 
            'Are you sure you want to delete this supplier?';
        
        if (confirm(confirmMessage)) {
            $.ajax({
                url: urls.deleteUrl || '',
                type: 'POST',
                data: { id: id },
                success: function(response) {
                    if (response.success) {
                        if (window.dataTable_suppliersTable) {
                            window.dataTable_suppliersTable.ajax.reload();
                        }
                        var successMsg = window.i18n && window.i18n.t ? 
                            window.i18n.t('common.success', 'Success') + ': ' + response.message : 
                            response.message;
                        showAlert('success', successMsg);
                    } else {
                        showAlert('danger', response.message);
                    }
                },
                error: function() {
                    var errorMsg = window.i18n && window.i18n.t ? 
                        window.i18n.t('common.error', 'Error') + ': ' + window.i18n.t('common.errorOccurred', 'An error occurred') : 
                        'An error occurred while deleting the supplier.';
                    showAlert('danger', errorMsg);
                }
            });
        }
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

    window.updateSupplierStats = function() {
        var urls = window.suppliersUrls || {};
        var getSuppliersUrl = urls.getSuppliersUrl || '/Suppliers/GetSuppliers';
        
        // Fetch all suppliers to calculate stats
        $.ajax({
            url: getSuppliersUrl,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({
                start: 0,
                length: 10000 // Get all suppliers
            }),
            success: function(response) {
                if (response && response.data) {
                    var suppliers = response.data;
                    var totalSuppliers = response.recordsTotal || suppliers.length;
                    var activeSuppliers = suppliers.filter(function(s) { return s.isActive === true; }).length;
                    var inactiveSuppliers = suppliers.filter(function(s) { return s.isActive === false; }).length;
                    
                    $('#totalSuppliers').text(totalSuppliers);
                    $('#activeSuppliers').text(activeSuppliers);
                    $('#inactiveSuppliers').text(inactiveSuppliers);
                    $('#totalContracts').text(activeSuppliers); // Active contracts = Active suppliers
                }
            },
            error: function(xhr, status, error) {
                console.error('Error fetching supplier stats:', error);
                // Set to 0 on error instead of random numbers
                $('#totalSuppliers').text('0');
                $('#activeSuppliers').text('0');
                $('#inactiveSuppliers').text('0');
                $('#totalContracts').text('0');
            }
        });
    };

    // Initialize when document is ready
    $(document).ready(function() {
        init();
    });

})();


