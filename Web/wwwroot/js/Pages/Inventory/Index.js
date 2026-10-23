// Inventory Index Page Script
(function() {
    'use strict';

    // Initialize when document is ready
    $(document).ready(function() {
        var urls = window.inventoryUrls || {};
        var localizedStrings = window.inventoryLocalizedStrings || {};

        // Refresh button handler
        $('#refreshTable').on('click', function() {
            var table = $('#inventoryTable').DataTable();
            if (table) {
                table.ajax.reload();
                if (typeof toastr !== 'undefined') {
                    toastr.success(localizedStrings.inventoryDataRefreshedSuccessfully || 'Inventory data refreshed successfully!');
                }
            }
        });

        // Export button handler
        $('#exportTable').on('click', function() {
            if (typeof toastr !== 'undefined') {
                toastr.info(localizedStrings.exportFunctionalityWillBeImplementedSoon || 'Export functionality will be implemented soon.');
            }
        });

        // Configure toastr options
        if (typeof toastr !== 'undefined') {
            toastr.options.timeOut = 3000;
            toastr.options.extendedTimeOut = 1000;
            toastr.options.progressBar = true;
            toastr.options.closeButton = true;
        }
    });

    // Global function for setting minimum stock
    window.setMinimumStock = function(productId) {
        var urls = window.inventoryUrls || {};
        var localizedStrings = window.inventoryLocalizedStrings || {};
        
        if (typeof Swal !== 'undefined') {
            Swal.fire({
                title: localizedStrings.setMinimumStock || 'Set Minimum Stock',
                input: 'number',
                inputLabel: localizedStrings.minimumStockLevel || 'Minimum Stock Level',
                inputPlaceholder: localizedStrings.enterMinimumStockLevel || 'Enter minimum stock level',
                showCancelButton: true,
                confirmButtonText: localizedStrings.set || 'Set',
                cancelButtonText: localizedStrings.cancel || 'Cancel',
                customClass: {
                    confirmButton: 'btn btn-primary',
                    cancelButton: 'btn btn-secondary'
                },
                inputValidator: function(value) {
                    if (!value || value < 0) {
                        return localizedStrings.pleaseEnterAValidMinimumStockLevel || 'Please enter a valid minimum stock level';
                    }
                }
            }).then(function(result) {
                if (result.isConfirmed) {
                    $.ajax({
                        url: urls.setMinimumStockUrl || '',
                        type: 'POST',
                        data: {
                            productId: productId,
                            minimumStock: result.value
                        },
                        success: function(response) {
                            if (response.success) {
                                if (typeof toastr !== 'undefined') {
                                    toastr.success(localizedStrings.minimumStockLevelUpdatedSuccessfully || 'Minimum stock level updated successfully!');
                                }
                                var table = $('#inventoryTable').DataTable();
                                if (table) {
                                    table.ajax.reload(null, false);
                                }
                            } else {
                                if (typeof toastr !== 'undefined') {
                                    toastr.error(response.message || (localizedStrings.failedToUpdateMinimumStockLevel || 'Failed to update minimum stock level'));
                                }
                            }
                        },
                        error: function() {
                            if (typeof toastr !== 'undefined') {
                                toastr.error(localizedStrings.anErrorOccurredWhileUpdatingMinimumStockLevel || 'An error occurred while updating minimum stock level');
                            }
                        }
                    });
                }
            });
        } else {
            console.error('SweetAlert2 is required for this feature');
        }
    };

    // Global function for viewing stock history
    window.viewStockHistory = function(productId) {
        var localizedStrings = window.inventoryLocalizedStrings || {};
        if (typeof toastr !== 'undefined') {
            toastr.info(localizedStrings.stockHistoryFeatureWillBeImplementedSoon || 'Stock history feature will be implemented soon.');
        }
    };

})();
