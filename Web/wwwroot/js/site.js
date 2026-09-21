/**
 * Main Application JavaScript - NexPos
 * Handles common functionality across all pages
 */

// One-time wiring (document-level delegated handlers, global config).
$(document).ready(function() {
    initializeEventHandlers();
    initializeCharts();
    initializeToastr();
    initPage();
});

// Per-page init — also runs after a no-reload shell swap (language switch).
function initPage() {
    initializeComponents();
    initializeDataTables();
    handleTempDataMessages();
}

document.addEventListener('shell:loaded', function() {
    initPage();
});

/**
 * Initialize common components
 */
function initializeComponents() {
    // Initialize tooltips
    var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
    var tooltipList = tooltipTriggerList.map(function (tooltipTriggerEl) {
        return new bootstrap.Tooltip(tooltipTriggerEl);
    });

    // Initialize popovers
    var popoverTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="popover"]'));
    var popoverList = popoverTriggerList.map(function (popoverTriggerEl) {
        return new bootstrap.Popover(popoverTriggerEl);
    });

    // Add fade-in animation to cards
    $('.card').addClass('fade-in');
}

/**
 * Initialize event handlers
 */
function initializeEventHandlers() {
    // Language switching is handled by lang-switch.js (no-reload shell swap,
    // with a full-navigation fallback built in).

    // Confirm delete actions
    $('.btn-delete').on('click', function(e) {
        e.preventDefault();
        var url = $(this).attr('href') || $(this).data('url');
        var message = $(this).data('confirm') || window.i18n.t('alerts.deleteMessage', 'Are you sure you want to delete this item?');
        
        Swal.fire({
            title: window.i18n.t('alerts.confirmDelete', 'Confirm Delete'),
            text: message,
            icon: 'warning',
            showCancelButton: true,
            confirmButtonColor: '#d33',
            cancelButtonColor: '#3085d6',
            confirmButtonText: window.i18n.t('alerts.yesDeleteIt', 'Yes, delete it!')
        }).then((result) => {
            if (result.isConfirmed) {
                if (url) {
                    window.location.href = url;
                } else {
                    $(this).closest('form').submit();
                }
            }
        });
    });

    // Form validation feedback
    $('form').on('submit', function() {
        var $form = $(this);
        var $submitBtn = $form.find('button[type="submit"]');
        
        if ($form[0].checkValidity()) {
            $submitBtn.prop('disabled', true);
            $submitBtn.html('<span class="spinner"></span> Processing...');
        }
    });

    // Auto-hide erp-alerts (custom alerts that should fade out)
    // Note: We use .erp-alert class to distinguish from Bootstrap's .alert class
    // which is used by plugins and should not auto-hide

    // Toggle sidebar on mobile
    $('#sidebarToggle').on('click', function() {
        $('#sidebar').toggleClass('show');
    });

    // Close sidebar when clicking outside on mobile
    $(document).on('click', function(e) {
        if ($(window).width() <= 768) {
            if (!$(e.target).closest('#sidebar, #sidebarToggle').length) {
                $('#sidebar').removeClass('show');
            }
        }
    });
}

/**
 * Initialize DataTables with common settings
 */
function initializeDataTables() {
    if ($.fn.DataTable) {
        $('.data-table').DataTable({
            retrieve: true,
            responsive: true,
            pageLength: 25,
            lengthMenu: [[10, 25, 50, 100, -1], [10, 25, 50, 100, window.i18n.t('datatable.all', 'All')]],
            language: {
                search: window.i18n.t('datatable.search', 'Search:'),
                lengthMenu: window.i18n.t('datatable.lengthMenu', 'Show _MENU_ entries'),
                info: window.i18n.t('datatable.info', 'Showing _START_ to _END_ of _TOTAL_ entries'),
                infoEmpty: window.i18n.t('datatable.infoEmpty', 'Showing 0 to 0 of 0 entries'),
                infoFiltered: window.i18n.t('datatable.infoFiltered', '(filtered from _MAX_ total entries)'),
                paginate: {
                    first: window.i18n.t('datatable.paginate.first', 'First'),
                    last: window.i18n.t('datatable.paginate.last', 'Last'),
                    next: window.i18n.t('datatable.paginate.next', 'Next'),
                    previous: window.i18n.t('datatable.paginate.previous', 'Previous')
                }
            },
            dom: '<"row"<"col-sm-12 col-md-6"l><"col-sm-12 col-md-6"f>>' +
                 '<"row"<"col-sm-12"tr>>' +
                 '<"row"<"col-sm-12 col-md-5"i><"col-sm-12 col-md-7"p>>',
            initComplete: function() {
                // Add custom styling
                $(this).closest('.dataTables_wrapper').addClass('fade-in');
            }
        });
    }
}

/**
 * Initialize charts with common settings
 */
function initializeCharts() {
    // Chart.js default configuration
    if (typeof Chart !== 'undefined') {
        Chart.defaults.font.family = "'Inter', 'Segoe UI', system-ui, sans-serif";
        Chart.defaults.color = '#6c757d';
        Chart.defaults.plugins.legend.labels.usePointStyle = true;
        Chart.defaults.plugins.legend.labels.padding = 20;
        
        // Default colors for charts
        Chart.defaults.elements.bar.backgroundColor = 'rgba(10, 162, 162, 0.8)';
        Chart.defaults.elements.bar.borderColor = '#0aa2a2';
        Chart.defaults.elements.bar.borderWidth = 1;
        
        Chart.defaults.elements.line.borderColor = '#0aa2a2';
        Chart.defaults.elements.line.backgroundColor = 'rgba(10, 162, 162, 0.1)';
        Chart.defaults.elements.line.borderWidth = 3;
        Chart.defaults.elements.line.tension = 0.4;
    }
}

/**
 * Initialize Toastr with custom settings
 */
function initializeToastr() {
    if (typeof toastr !== 'undefined') {
        toastr.options = {
            "closeButton": true,
            "debug": false,
            "newestOnTop": true,
            "progressBar": true,
            "positionClass": "toast-top-right",
            "preventDuplicates": false,
            "onclick": null,
            "showDuration": "300",
            "hideDuration": "1000",
            "timeOut": "5000",
            "extendedTimeOut": "1000",
            "showEasing": "swing",
            "hideEasing": "linear",
            "showMethod": "fadeIn",
            "hideMethod": "fadeOut"
        };
    }
}

/**
 * Handle TempData messages from server
 */
function handleTempDataMessages() {
    // Check for success messages
    if (window.successMessage) {
        toastr.success(window.successMessage);
        // Clear the message to prevent showing again on page refresh
        window.successMessage = null;
    }
    
    // Check for error messages
    if (window.errorMessage) {
        toastr.error(window.errorMessage);
        window.errorMessage = null;
    }
    
    // Check for info messages
    if (window.infoMessage) {
        toastr.info(window.infoMessage);
        window.infoMessage = null;
    }
    
    // Check for warning messages
    if (window.warningMessage) {
        toastr.warning(window.warningMessage);
        window.warningMessage = null;
    }
}

/**
 * Utility Functions
 */

// Show loading state
function showLoading(element) {
    $(element).addClass('loading');
    $(element).find('button').prop('disabled', true);
}

// Hide loading state
function hideLoading(element) {
    $(element).removeClass('loading');
    $(element).find('button').prop('disabled', false);
}

// Show success message
function showSuccess(message) {
    if (typeof toastr !== 'undefined') {
        toastr.success(message);
    } else {
        Swal.fire({
            icon: 'success',
            title: window.i18n.t('alerts.successMessage', 'Success!'),
            text: message,
            timer: 3000,
            showConfirmButton: false
        });
    }
}

// Show error message
function showError(message) {
    if (typeof toastr !== 'undefined') {
        toastr.error(message);
    } else {
        Swal.fire({
            icon: 'error',
            title: window.i18n.t('alerts.errorMessage', 'Error!'),
            text: message,
            confirmButtonColor: '#dc3545'
        });
    }
}

// Show info message
function showInfo(message) {
    if (typeof toastr !== 'undefined') {
        toastr.info(message);
    } else {
        Swal.fire({
            icon: 'info',
            title: window.i18n.t('alerts.infoMessage', 'Information'),
            text: message,
            timer: 3000,
            showConfirmButton: false
        });
    }
}

// Show warning message
function showWarning(message) {
    if (typeof toastr !== 'undefined') {
        toastr.warning(message);
    } else {
        Swal.fire({
            icon: 'warning',
            title: window.i18n.t('alerts.warningMessage', 'Warning!'),
            text: message,
            timer: 3000,
            showConfirmButton: false
        });
    }
}

// Format currency
function formatCurrency(amount, currency = 'USD') {
    return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency: currency
    }).format(amount);
}

// Format date
function formatDate(date, format = 'short') {
    return new Intl.DateTimeFormat('en-US', {
        dateStyle: format
    }).format(new Date(date));
}

// Format number
function formatNumber(number, decimals = 2) {
    return new Intl.NumberFormat('en-US', {
        minimumFractionDigits: decimals,
        maximumFractionDigits: decimals
    }).format(number);
}

// Debounce function
function debounce(func, wait, immediate) {
    var timeout;
    return function() {
        var context = this, args = arguments;
        var later = function() {
            timeout = null;
            if (!immediate) func.apply(context, args);
        };
        var callNow = immediate && !timeout;
        clearTimeout(timeout);
        timeout = setTimeout(later, wait);
        if (callNow) func.apply(context, args);
    };
}

// Throttle function
function throttle(func, limit) {
    var inThrottle;
    return function() {
        var args = arguments;
        var context = this;
        if (!inThrottle) {
            func.apply(context, args);
            inThrottle = true;
            setTimeout(() => inThrottle = false, limit);
        }
    };
}

/**
 * API Helper Functions
 */

// Make AJAX request with loading state
function apiRequest(url, options = {}) {
    const defaults = {
        method: 'GET',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': $('input[name="__RequestVerificationToken"]').val()
        }
    };
    
    const config = Object.assign({}, defaults, options);
    
    return fetch(url, config)
        .then(response => {
            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }
            return response.json();
        })
        .catch(error => {
            console.error('API Request failed:', error);
            showError(window.i18n.t('common.error', 'An error occurred while processing your request.'));
            throw error;
        });
}

// GET request
function apiGet(url) {
    return apiRequest(url, { method: 'GET' });
}

// POST request
function apiPost(url, data) {
    return apiRequest(url, {
        method: 'POST',
        body: JSON.stringify(data)
    });
}

// PUT request
function apiPut(url, data) {
    return apiRequest(url, {
        method: 'PUT',
        body: JSON.stringify(data)
    });
}

// DELETE request
function apiDelete(url) {
    return apiRequest(url, { method: 'DELETE' });
}

/**
 * Form Helper Functions
 */

// Serialize form to object
function serializeForm(form) {
    const formData = new FormData(form);
    const data = {};
    
    for (let [key, value] of formData.entries()) {
        data[key] = value;
    }
    
    return data;
}

// Clear form
function clearForm(form) {
    $(form)[0].reset();
    $(form).find('.form-control').removeClass('is-valid is-invalid');
    $(form).find('.invalid-feedback').remove();
}

// Validate form
function validateForm(form) {
    let isValid = true;
    const $form = $(form);
    
    // Clear previous validation
    $form.find('.form-control').removeClass('is-valid is-invalid');
    $form.find('.invalid-feedback').remove();
    
    // Check required fields
    $form.find('[required]').each(function() {
        const $field = $(this);
        const value = $field.val().trim();
        
        if (!value) {
            $field.addClass('is-invalid');
            $field.after(`<div class="invalid-feedback">${window.i18n.t('validation.required', 'This field is required.')}</div>`);
            isValid = false;
        } else {
            $field.addClass('is-valid');
        }
    });
    
    // Check email fields
    $form.find('input[type="email"]').each(function() {
        const $field = $(this);
        const value = $field.val().trim();
        const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
        
        if (value && !emailRegex.test(value)) {
            $field.addClass('is-invalid');
            $field.after(`<div class="invalid-feedback">${window.i18n.t('validation.invalidEmail', 'Please enter a valid email address.')}</div>`);
            isValid = false;
        }
    });
    
    return isValid;
}

/**
 * Global error handler
 */
window.addEventListener('error', function(e) {
    console.error('Global error:', e.error);
    // You can add error reporting here
});

/**
 * Export functions for global use
 */
window.NexPos = {
    showLoading,
    hideLoading,
    showSuccess,
    showError,
    showInfo,
    showWarning,
    formatCurrency,
    formatDate,
    formatNumber,
    debounce,
    throttle,
    apiRequest,
    apiGet,
    apiPost,
    apiPut,
    apiDelete,
    serializeForm,
    clearForm,
    validateForm
};

// Backward-compatible alias
window.ERP = window.NexPos;