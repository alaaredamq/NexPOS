// Products Index Page Script
(function() {
    'use strict';

    // Initialize the page
    function init() {
        var urls = window.productsUrls || {};
        var localizedStrings = window.productsLocalizedStrings || {};

        // DEBUG: Test API endpoint directly
        console.log('=== Products Page Debug ===');
        console.log('jQuery loaded:', typeof $ !== 'undefined');
        console.log('DataTables loaded:', typeof $.fn.DataTable !== 'undefined');
        console.log('Toastr loaded:', typeof toastr !== 'undefined');
        
        // Test direct API call
        $.ajax({
            url: urls.getProductsUrl || '',
            type: 'GET',
            data: { draw: 1, start: 0, length: 10, searchValue: '' },
            success: function(data) {
                console.log('✅ API Test Success:', data);
                console.log('   Records Total:', data.recordsTotal);
                console.log('   Records Filtered:', data.recordsFiltered);
                console.log('   Data Count:', data.data ? data.data.length : 0);
                if (data.data && data.data.length > 0) {
                    console.log('   First Record:', data.data[0]);
                }
            },
            error: function(xhr, status, error) {
                console.error('❌ API Test Failed:', {
                    status: xhr.status,
                    statusText: xhr.statusText,
                    error: error,
                    response: xhr.responseText
                });
            }
        });
        
        // Wait for table to initialize, then update stats
        setTimeout(function() {
            console.log('DataTable reference:', window.dataTable_productsTable);
            // Update stats after DataTable is loaded
            if (typeof updateProductStats === 'function') {
                updateProductStats();
            }
        }, 1500);
        
        // Get table reference from DataTableComponent
        var table = window.dataTable_productsTable;

        // Refresh table button
        $('#refreshTable').on('click', function() {
            if (table) {
                table.ajax.reload();
                // Update stats after refresh
                setTimeout(function() {
                    if (typeof updateProductStats === 'function') {
                        updateProductStats();
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

    // Product action functions
    window.viewProduct = function(id) {
        var urls = window.productsUrls || {};
        window.location.href = (urls.detailsUrl || '') + '?id=' + id;
    };

    window.editProduct = function(id) {
        var urls = window.productsUrls || {};
        window.location.href = (urls.editUrl || '') + '?id=' + id;
    };

    window.deleteProduct = function(id) {
        var localizedStrings = window.productsLocalizedStrings || {};
        var urls = window.productsUrls || {};
        
        console.log('deleteProduct called with id:', id, 'URL:', urls.deleteUrl);
        
        Swal.fire({
            title: localizedStrings.deleteTitle || 'Delete Product',
            text: localizedStrings.deleteConfirm || 'Are you sure you want to delete this product?',
            icon: 'warning',
            showCancelButton: true,
            confirmButtonColor: '#d33',
            cancelButtonColor: '#6c757d',
            confirmButtonText: localizedStrings.yes || 'Yes, delete it!',
            cancelButtonText: localizedStrings.cancel || 'Cancel',
            reverseButtons: true
        }).then(function(result) {
            if (result.isConfirmed) {
                var deleteUrl = urls.deleteUrl || '';
                console.log('Delete confirmed, making AJAX call to:', deleteUrl, 'with id:', id);
                $.ajax({
                    url: deleteUrl,
                    type: 'POST',
                    data: { id: id },
                    success: function(response) {
                        console.log('Delete response:', response);
                        if (response.success) {
                            if (window.dataTable_productsTable) {
                                window.dataTable_productsTable.ajax.reload();
                            }
                            Swal.fire({
                                title: localizedStrings.deleted || 'Deleted!',
                                text: localizedStrings.productDeleted || 'Product has been deleted.',
                                icon: 'success',
                                timer: 2000,
                                showConfirmButton: false
                            });
                            // Also update stats
                            if (typeof updateProductStats === 'function') {
                                setTimeout(updateProductStats, 500);
                            }
                        } else {
                            Swal.fire({
                                title: localizedStrings.error || 'Error',
                                text: response.message || localizedStrings.deleteError || 'Failed to delete product',
                                icon: 'error'
                            });
                        }
                    },
                    error: function(xhr, status, error) {
                        console.error('Delete error:', { 
                            status: xhr.status, 
                            statusText: xhr.statusText, 
                            responseText: xhr.responseText,
                            error: error 
                        });
                        var errorMessage = localizedStrings.deleteError || 'An error occurred while deleting the product';
                        if (xhr.status === 403) {
                            errorMessage = 'You do not have permission to delete products.';
                        } else if (xhr.status === 404) {
                            errorMessage = 'Product not found.';
                        } else if (xhr.responseJSON && xhr.responseJSON.message) {
                            errorMessage = xhr.responseJSON.message;
                        }
                        Swal.fire({
                            title: localizedStrings.error || 'Error',
                            text: errorMessage,
                            icon: 'error'
                        });
                    }
                });
            }
        });
    };

    window.updateProductStats = function() {
        var getProductsUrl = window.productsUrls?.getProductsUrl || '/Products/GetProducts';
        
        console.log('Updating product stats from:', getProductsUrl);
        
        // Make API call to get all products for statistics (request large page size to get all data)
        $.ajax({
            url: getProductsUrl,
            type: 'GET',
            data: { draw: 1, start: 0, length: 100000, searchValue: '' },
            success: function(response) {
                console.log('Product stats response:', response);
                
                if (response && response.data && Array.isArray(response.data)) {
                    var all = response.data;
                    var totalProducts = response.recordsTotal || 0;
                    
                    // Calculate statistics from all returned data
                    var activeProducts = all.filter(function(p) {
                        // Products returns isActive as "Active" or "Inactive" string
                        return p.isActive === "Active" || p.isActive === true || p.isActive === "active";
                    }).length;
                    
                    var outOfStockProducts = all.filter(function(p) {
                        var qty = parseInt(p.stockQuantity) || 0;
                        return qty === 0;
                    }).length;
                    
                    var lowStockProducts = all.filter(function(p) {
                        var qty = parseInt(p.stockQuantity) || 0;
                        return qty > 0 && qty <= 5;
                    }).length;
                    
                    // If we got all data (not paginated), use actual counts, otherwise use recordsTotal
                    if (totalProducts <= all.length) {
                        // We have all data, calculate exactly
                        activeProducts = all.filter(function(p) {
                            return p.isActive === "Active" || p.isActive === true || p.isActive === "active";
                        }).length;
                        outOfStockProducts = all.filter(function(p) {
                            var qty = parseInt(p.stockQuantity) || 0;
                            return qty === 0;
                        }).length;
                        lowStockProducts = all.filter(function(p) {
                            var qty = parseInt(p.stockQuantity) || 0;
                            return qty > 0 && qty <= 5;
                        }).length;
                    } else {
                        // Estimate based on sample ratio
                        var ratio = totalProducts / all.length;
                        activeProducts = Math.round(activeProducts * ratio);
                        outOfStockProducts = Math.round(outOfStockProducts * ratio);
                        lowStockProducts = Math.round(lowStockProducts * ratio);
                    }
                    
                    console.log('Product stats calculated:', {
                        total: totalProducts,
                        active: activeProducts,
                        lowStock: lowStockProducts,
                        outOfStock: outOfStockProducts
                    });
                    
                    $('#totalProducts').text(totalProducts || 0);
                    $('#activeProducts').text(activeProducts || 0);
                    $('#lowStockProducts').text(lowStockProducts || 0);
                    $('#outOfStockProducts').text(outOfStockProducts || 0);
                } else {
                    console.warn('Invalid response format:', response);
                    // Set to 0 if response is invalid
                    $('#totalProducts').text(0);
                    $('#activeProducts').text(0);
                    $('#lowStockProducts').text(0);
                    $('#outOfStockProducts').text(0);
                }
            },
            error: function(xhr, status, error) {
                console.error('Error fetching product stats:', {
                    status: xhr.status,
                    statusText: xhr.statusText,
                    error: error,
                    responseText: xhr.responseText
                });
                // Set to 0 if API fails
                $('#totalProducts').text(0);
                $('#activeProducts').text(0);
                $('#lowStockProducts').text(0);
                $('#outOfStockProducts').text(0);
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







