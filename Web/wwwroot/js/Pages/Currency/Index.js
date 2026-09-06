// Currency Index Page Script
(function() {
    'use strict';

    // Initialize the page
    function init() {
        var urls = window.currencyUrls || {};
        var localizedStrings = window.currencyLocalizedStrings || {};

        // Initialize DataTables
        if ($('#currenciesTable').length) {
            $('#currenciesTable').DataTable({
                responsive: true,
                pageLength: 10,
                order: [[0, 'asc']],
                columnDefs: [
                    { orderable: false, targets: -1 }
                ]
            });
        }

        if ($('#ratesTable').length) {
            $('#ratesTable').DataTable({
                responsive: true,
                pageLength: 10,
                order: [[4, 'desc']],
                columnDefs: [
                    { orderable: false, targets: -1 }
                ]
            });
        }

        // Set today's date as default for valid from
        $('#validFrom').val(new Date().toISOString().split('T')[0]);

        // Add currency form submission
        $('#addCurrencyForm').on('submit', function(e) {
            e.preventDefault();
            
            const formData = {
                code: $('#currencyCode').val(),
                name: $('#currencyName').val(),
                symbol: $('#currencySymbol').val(),
                isDefault: $('#isDefault').is(':checked')
            };

            $.ajax({
                url: urls.createUrl || '',
                type: 'POST',
                contentType: 'application/json',
                data: JSON.stringify(formData),
                success: function(response) {
                    if (response.success) {
                        if (typeof toastr !== 'undefined') {
                            toastr.success(localizedStrings.currencyAddedSuccessfully || 'Currency added successfully');
                        }
                        $('#addCurrencyModal').modal('hide');
                        $('#addCurrencyForm')[0].reset();
                        // Reload table
                        if ($('#currenciesTable').length) {
                            $('#currenciesTable').DataTable().ajax.reload();
                        }
                    } else {
                        if (typeof toastr !== 'undefined') {
                            toastr.error(response.message || (localizedStrings.failedToAddCurrency || 'Failed to add currency'));
                        }
                    }
                },
                error: function() {
                    if (typeof toastr !== 'undefined') {
                        toastr.error(localizedStrings.failedToAddCurrency || 'Failed to add currency');
                    }
                }
            });
        });

        // Add exchange rate form submission
        $('#addExchangeRateForm').on('submit', function(e) {
            e.preventDefault();
            
            const formData = {
                fromCurrencyCode: $('#fromCurrency').val(),
                toCurrencyCode: $('#toCurrency').val(),
                rate: parseFloat($('#exchangeRate').val()),
                supplierId: $('#supplierId').val() || null,
                validFrom: $('#validFrom').val(),
                validTo: $('#validTo').val() || null
            };

            $.ajax({
                url: urls.createRateUrl || '',
                type: 'POST',
                contentType: 'application/json',
                data: JSON.stringify(formData),
                success: function(response) {
                    if (response.success) {
                        if (typeof toastr !== 'undefined') {
                            toastr.success(localizedStrings.exchangeRateAddedSuccessfully || 'Exchange rate added successfully');
                        }
                        $('#addExchangeRateModal').modal('hide');
                        $('#addExchangeRateForm')[0].reset();
                        $('#validFrom').val(new Date().toISOString().split('T')[0]);
                        // Reload table
                        if ($('#ratesTable').length) {
                            $('#ratesTable').DataTable().ajax.reload();
                        }
                    } else {
                        if (typeof toastr !== 'undefined') {
                            toastr.error(response.message || (localizedStrings.failedToAddExchangeRate || 'Failed to add exchange rate'));
                        }
                    }
                },
                error: function() {
                    if (typeof toastr !== 'undefined') {
                        toastr.error(localizedStrings.failedToAddExchangeRate || 'Failed to add exchange rate');
                    }
                }
            });
        });

        // Settings form submissions
        $('#defaultCurrencyForm').on('submit', function(e) {
            e.preventDefault();
            // Implementation for saving default currency settings
            if (typeof toastr !== 'undefined') {
                toastr.success(localizedStrings.defaultCurrencySettingsSaved || 'Default currency settings saved');
            }
        });

        $('#autoUpdateForm').on('submit', function(e) {
            e.preventDefault();
            // Implementation for saving auto-update settings
            if (typeof toastr !== 'undefined') {
                toastr.success(localizedStrings.autoUpdateSettingsSaved || 'Auto-update settings saved');
            }
        });

        // Load current settings
        loadCurrencySettings();
    }

    function loadCurrencySettings() {
        var urls = window.currencyUrls || {};
        
        // Load current currency settings from server
        $.ajax({
            url: urls.getSettingsUrl || '',
            type: 'GET',
            success: function(settings) {
                if (settings) {
                    $('#defaultSystemCurrency').val(settings.defaultCurrency);
                    $('#autoUpdateRates').prop('checked', settings.autoUpdate);
                    $('#updateFrequency').val(settings.updateFrequency);
                    
                    // Update stats display
                    $('#defaultCurrency').text(settings.defaultCurrency);
                    $('#currentRate').text(settings.currentRate);
                    $('#totalCurrencies').text(settings.totalCurrencies);
                    $('#lastUpdate').text(settings.lastUpdate);
                }
            },
            error: function() {
                console.log('Failed to load currency settings');
            }
        });
    }

    window.loadCurrencySettings = loadCurrencySettings;

    // Initialize when document is ready
    $(document).ready(function() {
        init();
    });

})();







