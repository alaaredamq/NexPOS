// Dashboard Index Page Script
(function() {
    'use strict';

    var salesChart;
    var productsChart;
    var categoryDataLoaded = false;
    var chartsInitialized = false;

    // Initialize the page
    function init() {
        // Prevent multiple initializations
        if (chartsInitialized) {
            return;
        }
        
        chartsInitialized = true;
        var urls = window.dashboardUrls || {};
        var localizedStrings = window.dashboardLocalizedStrings || {};

        // Update time display
        function updateTime() {
            const now = new Date();
            const locale = document.documentElement.lang || 'en';
            const timeString = now.toLocaleTimeString(locale, { 
                hour: '2-digit', 
                minute: '2-digit',
                hour12: true 
            });
            const dateString = now.toLocaleDateString(locale, { 
                weekday: 'long', 
                year: 'numeric', 
                month: 'long', 
                day: 'numeric' 
            });
            
            $('#currentTime').text(timeString);
            $('#currentDate').text(dateString);
        }
        
        // Update time immediately and then every second
        updateTime();
        setInterval(updateTime, 1000);

        // Chart period controls
        $('.chart-controls button').on('click', function() {
            $('.chart-controls button').removeClass('active');
            $(this).addClass('active');
            var period = $(this).data('period');
            updateSalesChart(period);
        });

        // Sales Chart with enhanced styling
        if (document.getElementById('salesChart')) {
            const salesCtx = document.getElementById('salesChart').getContext('2d');
            
            salesChart = new Chart(salesCtx, {
                type: 'line',
                data: {
                    labels: [],
                    datasets: [{
                        label: localizedStrings.salesRevenue || 'Sales Revenue',
                        data: [],
                        borderColor: '#0aa2a2',
                        backgroundColor: 'rgba(10, 162, 162, 0.1)',
                        borderWidth: 3,
                        tension: 0.4,
                        fill: true,
                        pointBackgroundColor: '#0aa2a2',
                        pointBorderColor: '#ffffff',
                        pointBorderWidth: 2,
                        pointRadius: 6,
                        pointHoverRadius: 8
                    }, {
                        label: localizedStrings.profit || 'Profit',
                        data: [],
                        borderColor: '#20c997',
                        backgroundColor: 'rgba(32, 201, 151, 0.1)',
                        borderWidth: 3,
                        tension: 0.4,
                        fill: false,
                        pointBackgroundColor: '#20c997',
                        pointBorderColor: '#ffffff',
                        pointBorderWidth: 2,
                        pointRadius: 6,
                        pointHoverRadius: 8
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: {
                            display: true,
                            position: 'top',
                            labels: {
                                usePointStyle: true,
                                padding: 20,
                                color: '#6c757d'
                            }
                        },
                        tooltip: {
                            backgroundColor: 'rgba(0, 0, 0, 0.8)',
                            titleColor: '#ffffff',
                            bodyColor: '#ffffff',
                            borderColor: '#0aa2a2',
                            borderWidth: 1,
                            cornerRadius: 8,
                            displayColors: false
                        }
                    },
                    scales: {
                        x: {
                            grid: {
                                display: false
                            },
                            ticks: {
                                color: '#6c757d'
                            }
                        },
                        y: {
                            beginAtZero: true,
                            grid: {
                                color: 'rgba(0, 0, 0, 0.05)'
                            },
                            ticks: {
                                color: '#6c757d',
                                callback: function(value) {
                                    return '$' + value.toLocaleString();
                                }
                            }
                        }
                    },
                    elements: {
                        point: {
                            hoverBackgroundColor: '#0aa2a2'
                        }
                    }
                }
            });
            
            // Load initial sales data
            updateSalesChart('month');
        }

        // Products Chart with enhanced styling
        if (document.getElementById('productsChart')) {
            // Destroy existing chart instance if it exists
            if (productsChart) {
                try {
                    productsChart.destroy();
                } catch (e) {
                    // Ignore errors
                }
                productsChart = null;
            }
            
            const productsCanvas = document.getElementById('productsChart');
            const productsCtx = productsCanvas.getContext('2d');
            
            productsChart = new Chart(productsCtx, {
                type: 'doughnut',
                data: {
                    labels: [],
                    datasets: [{
                        data: [],
                        backgroundColor: [],
                        borderWidth: 0,
                        hoverOffset: 10
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    cutout: '60%',
                    plugins: {
                        legend: {
                            display: false // We're using custom legend
                        },
                        tooltip: {
                            backgroundColor: 'rgba(0, 0, 0, 0.8)',
                            titleColor: '#ffffff',
                            bodyColor: '#ffffff',
                            borderColor: '#0aa2a2',
                            borderWidth: 1,
                            cornerRadius: 8,
                            displayColors: false,
                            callbacks: {
                                label: function(context) {
                                    return context.label + ': ' + context.parsed.toFixed(1) + '%';
                                }
                            }
                        }
                    }
                }
            });
            
            // Load category analytics data
            loadCategoryAnalytics();
        }

        // Add animation to stats cards
        $('.stats-card').each(function(index) {
            $(this).css('animation-delay', (index * 0.1) + 's');
            $(this).addClass('fade-in-up');
        });

        // Quick action button hover effects
        $('.quick-action-btn').hover(
            function() {
                $(this).addClass('shadow-lg').css('transform', 'translateY(-2px)');
            },
            function() {
                $(this).removeClass('shadow-lg').css('transform', 'translateY(0)');
            }
        );
    }

    // Function to update sales chart with new data
    window.updateSalesChart = function(period) {
        var urls = window.dashboardUrls || {};
        var localizedStrings = window.dashboardLocalizedStrings || {};
        
        $.ajax({
            url: urls.getSalesAnalyticsUrl || '',
            type: 'GET',
            data: { period: period },
            success: function(data) {
                if (data && data.labels && data.salesData && salesChart) {
                    salesChart.data.labels = data.labels;
                    salesChart.data.datasets[0].data = data.salesData;
                    if (data.profitData) {
                        salesChart.data.datasets[1].data = data.profitData;
                    }
                    salesChart.update('active');
                }
            },
            error: function() {
                if (typeof toastr !== 'undefined') {
                    toastr.error(localizedStrings.failedToLoadChartData || 'Failed to load chart data');
                }
            }
        });
    };
    
    // Function to load category analytics data
    function loadCategoryAnalytics() {
        // Prevent multiple simultaneous calls
        if (categoryDataLoaded) {
            return;
        }
        
        var urls = window.dashboardUrls || {};
        var localizedStrings = window.dashboardLocalizedStrings || {};
        
        if (!productsChart) {
            return;
        }
        
        categoryDataLoaded = true;
        
        $.ajax({
            url: urls.getCategoryAnalyticsUrl || '',
            type: 'GET',
            success: function(data) {
                if (data && data.labels && data.values && data.colors && productsChart) {
                    // Set new data (copy arrays to avoid reference issues)
                    productsChart.data.labels = Array.isArray(data.labels) ? data.labels.slice() : [];
                    productsChart.data.datasets[0].data = Array.isArray(data.values) ? data.values.slice() : [];
                    productsChart.data.datasets[0].backgroundColor = Array.isArray(data.colors) ? data.colors.slice() : [];
                    
                    // Use 'none' animation mode to prevent height accumulation
                    productsChart.update('none');
                    
                    // Update the legend
                    updateCategoryLegend(data);
                }
            },
            error: function() {
                console.error('Failed to load category analytics');
                categoryDataLoaded = false; // Reset on error to allow retry
            }
        });
    }
    
    // Function to update category legend
    function updateCategoryLegend(data) {
        var legendHtml = '';
        if (data.categories && data.categories.length > 0) {
            data.categories.forEach(function(category) {
                legendHtml += '<div class="legend-item">' +
                    '<span class="legend-color" style="background-color: ' + category.color + ';"></span>' +
                    '<span class="legend-label">' + category.name + '</span>' +
                    '<span class="legend-value">' + category.percentage.toFixed(1) + '%</span>' +
                    '</div>';
            });
            $('.chart-legend').html(legendHtml);
        }
    }

    // Initialize when document is ready
    $(document).ready(function() {
        init();
    });

})();
