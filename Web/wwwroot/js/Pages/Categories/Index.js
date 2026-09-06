// Categories Index Page Script
(function() {
    'use strict';

    // Initialize the page
    function init() {
        // Get localized strings from data attribute or global variable
        var localizedStrings = window.categoriesLocalizedStrings || {};
        var urls = window.categoriesUrls || {};

        // Wait for DataTable to initialize, then update stats
        setTimeout(function() {
            if (typeof updateCategoryStats === 'function') {
                updateCategoryStats();
            }
        }, 1500);

        // Get table reference from DataTableComponent
        var table = window.dataTable_categoriesTable;

        // Refresh table button
        $('#refreshTable').on('click', function() {
            if (table) {
                table.ajax.reload();
                // Update stats after refresh
                setTimeout(function() {
                    if (typeof updateCategoryStats === 'function') {
                        updateCategoryStats();
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

    // Category action functions
    window.viewCategory = function(id) {
        var urls = window.categoriesUrls || {};
        window.location.href = (urls.detailsUrl || '') + '?id=' + id;
    };

    window.editCategory = function(id) {
        var urls = window.categoriesUrls || {};
        window.location.href = (urls.editUrl || '') + '?id=' + id;
    };

    window.deleteCategory = function(id) {
        var localizedStrings = window.categoriesLocalizedStrings || {};
        var urls = window.categoriesUrls || {};
        
        if (confirm(localizedStrings.deleteConfirm || 'Are you sure?')) {
            $.ajax({
                url: urls.deleteUrl || '',
                type: 'POST',
                data: { id: id },
                success: function(response) {
                    if (response.success) {
                        if (window.dataTable_categoriesTable) {
                            window.dataTable_categoriesTable.ajax.reload();
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
        }
    };

    window.updateCategoryStats = function() {
        var getCategoriesUrl = window.categoriesUrls?.getCategoriesUrl || '/Categories/GetCategories';
        
        // Make API call to get all categories for statistics
        $.ajax({
            url: getCategoriesUrl,
            type: 'GET',
            data: { start: 0, length: 10000, searchValue: '' },
            success: function(response) {
                if (response && response.data) {
                    var all = response.data;
                    var totalCategories = response.recordsTotal || all.length;
                    var activeCategories = all.filter(function(c) {
                        return c.isActive === true || c.isActive === "Active" || c.isActive === "active";
                    }).length;
                    var inactiveCategories = totalCategories - activeCategories;
                    var totalProducts = all.reduce(function(sum, c) {
                        return sum + (parseInt(c.productsCount) || 0);
                    }, 0);
                    
                    $('#totalCategories').text(totalCategories || 0);
                    $('#activeCategories').text(activeCategories || 0);
                    $('#inactiveCategories').text(inactiveCategories || 0);
                    $('#totalProducts').text(totalProducts || 0);
                }
            },
            error: function(xhr, status, error) {
                console.error('Error fetching category stats:', error);
                // Set to 0 if API fails
                $('#totalCategories').text(0);
                $('#activeCategories').text(0);
                $('#inactiveCategories').text(0);
                $('#totalProducts').text(0);
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


