// Backup Management JavaScript
// `var`, not `let`: lang-switch.js re-runs this file on a language swap to re-bind it to the
// fresh DOM, and a re-declared `let` at script scope is a SyntaxError.
var operationId = null;

$(document).ready(function() {
    loadBackups();
    loadStats();
    loadRestoreBackupList();
    loadSchedules();
});

function loadStats() {
    $.get(window.backupUrls.getBackupStats, function(response) {
        if (response.success && response.data) {
            const stats = response.data;
            $('#totalBackups').text(stats.totalBackups || 0);
            $('#successfulBackups').text(stats.successfulBackups || 0);
            $('#successfulBackupsLabel').text(stats.successfulBackups || 0);
            $('#totalSize').text(stats.totalBackupSizeFormatted || formatBytes(stats.totalBackupSize || 0));
            $('#scheduledCount').text(stats.scheduledBackups || 0);
            $('#scheduledCountMain').text(stats.scheduledBackups || 0);
            
            if (stats.nextScheduledBackup) {
                $('#nextScheduled').text(formatDate(stats.nextScheduledBackup));
            } else {
                $('#nextScheduled').text(window.backupLocalizedStrings.never || 'None');
            }
            
            if (stats.lastBackupDate) {
                $('#lastBackup').text(formatDate(stats.lastBackupDate));
            }
            
            if (stats.recentBackups && stats.recentBackups.length > 0) {
                const lastBackup = stats.recentBackups[0];
                $('#lastBackup').text(formatDate(lastBackup.createdAt));
                $('#lastBackupStatus').html(`<span class="badge ${lastBackup.status === 'Completed' ? 'bg-success' : 'bg-warning'}">${lastBackup.status}</span>`);
            }
        }
    }).fail(function() {
        console.error('Failed to load backup stats');
    });
}

function loadBackups() {
    $.get(window.backupUrls.getBackups, function(response) {
        const table = $('#backupsTable');
        
        if (response.data && response.data.length > 0) {
            let html = `
                <table class="table table-hover modern-table">
                    <thead>
                        <tr>
                            <th style="width: 40px;"><input type="checkbox" class="form-check-input" id="selectAllBackups" onchange="toggleSelectAll(this)"></th>
                            <th class="fw-bold">${window.backupLocalizedStrings.name}</th>
                            <th class="text-center">${window.backupLocalizedStrings.type}</th>
                            <th class="text-center">${window.backupLocalizedStrings.status}</th>
                            <th class="text-end">${window.backupLocalizedStrings.size}</th>
                            <th>${window.backupLocalizedStrings.created}</th>
                            <th class="text-center" style="width: 120px;">${window.backupLocalizedStrings.actions}</th>
                        </tr>
                    </thead>
                    <tbody>
            `;
            
            response.data.forEach(function(backup) {
                let typeBadgeClass = 'bg-info';
                if (backup.backupType === 'Full') typeBadgeClass = 'bg-primary';
                else if (backup.backupType === 'Incremental') typeBadgeClass = 'bg-warning';
                else if (backup.backupType === 'Differential') typeBadgeClass = 'bg-success';
                
                let statusBadge = '';
                if (backup.status === 'Completed') {
                    statusBadge = `<span class="badge bg-success"><i class="fas fa-check-circle me-1"></i>${backup.status}</span>`;
                } else if (backup.status === 'Failed') {
                    statusBadge = `<span class="badge bg-danger"><i class="fas fa-times-circle me-1"></i>${backup.status}</span>`;
                } else if (backup.status === 'InProgress') {
                    statusBadge = `<span class="badge bg-warning"><i class="fas fa-spinner fa-spin me-1"></i>${backup.status}</span>`;
                } else {
                    statusBadge = `<span class="badge bg-secondary">${backup.status}</span>`;
                }
                
                html += `
                    <tr>
                        <td><input type="checkbox" class="form-check-input backup-checkbox" data-id="${backup.id}"></td>
                        <td>
                            <div class="fw-bold">${backup.name}</div>
                            ${backup.description ? `<small class="text-muted">${backup.description}</small>` : ''}
                        </td>
                        <td class="text-center"><span class="badge ${typeBadgeClass}">${backup.backupType}</span></td>
                        <td class="text-center">${statusBadge}</td>
                        <td class="text-end"><strong>${formatBytes(backup.fileSizeBytes || backup.size || 0)}</strong></td>
                        <td><i class="fas fa-calendar text-muted me-1"></i>${formatDate(backup.createdAt)}</td>
                        <td class="text-center">
                            <div class="btn-group btn-group-sm">
                                <button class="btn btn-outline-primary" onclick="downloadBackup(${backup.id})" title="${window.backupLocalizedStrings.download}">
                                    <i class="fas fa-download"></i>
                                </button>
                                <button class="btn btn-outline-success" onclick="validateBackup(${backup.id})" title="${window.backupLocalizedStrings.validate}">
                                    <i class="fas fa-check"></i>
                                </button>
                                <button class="btn btn-outline-danger" onclick="deleteBackup(${backup.id})" title="${window.backupLocalizedStrings.delete}">
                                    <i class="fas fa-trash"></i>
                                </button>
                            </div>
                        </td>
                    </tr>
                `;
            });
            
            html += '</tbody></table>';
            table.html(html);
        } else {
            table.html(`<div class="text-center py-5">
                <i class="fas fa-database fa-3x text-muted mb-3"></i>
                <h5 class="text-muted">${window.backupLocalizedStrings.noBackupsFound}</h5>
                <p class="text-muted mb-3">Create your first backup to protect your data.</p>
                <button class="btn btn-primary" data-bs-toggle="modal" data-bs-target="#createBackupModal">
                    <i class="fas fa-plus me-2"></i>${window.backupLocalizedStrings.createBackup || 'Create Backup'}
                </button>
            </div>`);
        }
    });
}

function loadRestoreBackupList() {
    $.get(window.backupUrls.getBackups, function(response) {
        const select = $('#restoreBackupSelect');
        select.find('option:not(:first)').remove();
        
        if (response.data) {
            response.data.forEach(function(backup) {
                select.append(`<option value="${backup.id}">${backup.name} (${formatDate(backup.createdAt)})</option>`);
            });
        }
    });
}

function testConnections() {
    toastr.info(window.backupLocalizedStrings.testingConnections);
    $.get(window.backupUrls.testConnections, function(response) {
        if (response.success) {
            toastr.success(window.backupLocalizedStrings.allConnectionsSuccessful);
        } else {
            toastr.error(window.backupLocalizedStrings.connectionTestFailed + ': ' + response.message);
        }
    });
}

function refreshBackups() {
    toastr.info(window.backupLocalizedStrings.refreshingBackupList);
    loadBackups();
    loadStats();
}

function createBackup() {
    const backupType = $('#backupType').val();
    if (!backupType) {
        toastr.warning(window.backupLocalizedStrings.pleaseSelectBackupType);
        return;
    }

    const data = {
        name: $('#backupName').val(),
        type: backupType,
        description: $('#backupDescription').val(),
        includeData: $('#includeData').is(':checked'),
        includeSchema: $('#includeSchema').is(':checked'),
        encrypt: $('#encryptBackup').is(':checked'),
        compression: $('#compressionType').val()
    };

    $('#createBackupModal').modal('hide');
    showProgress(window.backupLocalizedStrings.startingBackupOperation);

    $.ajax({
        url: window.backupUrls.createBackup,
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(data),
        success: function(response) {
            // The backup runs synchronously on the server, so the response already carries the final result
            if (response.success) {
                hideProgress(true);
                toastr.success(response.message || window.backupLocalizedStrings.operationCompletedSuccessfully);
            } else {
                hideProgress(false, response.message);
                toastr.error(response.message || window.backupLocalizedStrings.failedToCreateBackup);
            }
        },
        error: function() {
            hideProgress(false);
            toastr.error(window.backupLocalizedStrings.failedToCreateBackup);
        }
    });
}

function showProgress(message) {
    $('#progressBar').css('width', '0%');
    $('#progressMessage').text(message);
    $('#progressCloseBtn').hide();
    $('#progressCancelBtn').show();
    $('#progressModal').modal('show');
}

function hideProgress(success, errorMessage) {
    $('#progressBar').css('width', '100%');
    $('#progressMessage').text(success
        ? window.backupLocalizedStrings.operationCompletedSuccessfully
        : window.backupLocalizedStrings.operationFailed + (errorMessage ? ': ' + errorMessage : ''));
    $('#progressCloseBtn').show();
    $('#progressCancelBtn').hide();
    loadBackups();
    loadStats();
}

function pollOperationStatus() {
    if (!operationId) return;
    
    $.get(window.backupUrls.getOperationStatus, { operationId: operationId }, function(response) {
        if (response.success) {
            $('#progressBar').css('width', (response.progress || 0) + '%');
            $('#progressMessage').text(response.message || '');

            if (response.isCompleted) {
                hideProgress(response.isSuccess, response.isSuccess ? null : response.message);
                operationId = null;
            } else {
                setTimeout(pollOperationStatus, 2000);
            }
        } else {
            hideProgress(false, response.message);
            operationId = null;
        }
    }).fail(function() {
        hideProgress(false);
        operationId = null;
    });
}

function cancelOperation() {
    if (operationId) {
        $.ajax({
            url: window.backupUrls.cancelOperation,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(operationId),
            success: function(response) {
                if (response.success) {
                    toastr.info(window.backupLocalizedStrings.operationCancelled);
                    hideProgress(false);
                    operationId = null;
                } else {
                    toastr.error(response.message || window.backupLocalizedStrings.failedToCancelOperation);
                }
            },
            error: function() {
                toastr.error(window.backupLocalizedStrings.failedToCancelOperation);
            }
        });
    }
}

function downloadBackup(id) {
    toastr.info('Preparing download...', '', { timeOut: 2000 });
    window.location.href = window.backupUrls.downloadBackup + '?id=' + id;
}

function validateBackup(id) {
    toastr.info('Validating backup...', '', { timeOut: 2000 });
    
    $.ajax({
        url: window.backupUrls.validateBackup,
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(id),
        success: function(response) {
            if (response.success && response.data && response.data.isValid) {
                toastr.success(window.backupLocalizedStrings.backupIsValid);
            } else if (response.success) {
                toastr.warning(window.backupLocalizedStrings.backupValidationFailed);
            } else {
                toastr.error(response.message || window.backupLocalizedStrings.failedToValidateBackup);
            }
        },
        error: function() {
            toastr.error(window.backupLocalizedStrings.failedToValidateBackup);
        }
    });
}

function deleteBackup(id) {
    Swal.fire({
        title: window.backupLocalizedStrings.deleteBackupTitle,
        text: window.backupLocalizedStrings.confirmDeleteBackup,
        icon: 'warning',
        showCancelButton: true,
        confirmButtonColor: '#d33',
        cancelButtonColor: '#6c757d',
        confirmButtonText: window.backupLocalizedStrings.yesDelete,
        cancelButtonText: window.backupLocalizedStrings.cancel,
        reverseButtons: true
    }).then(function(result) {
        if (result.isConfirmed) {
            $.ajax({
                url: window.backupUrls.deleteBackup,
                type: 'POST',
                contentType: 'application/json',
                data: JSON.stringify(id),
                success: function(response) {
                    if (response.success) {
                        Swal.fire({
                            title: window.backupLocalizedStrings.deleted,
                            text: window.backupLocalizedStrings.backupDeletedSuccessfully,
                            icon: 'success',
                            timer: 2000,
                            showConfirmButton: false
                        });
                        loadBackups();
                        loadStats();
                    } else {
                        Swal.fire({
                            title: window.backupLocalizedStrings.error,
                            text: response.message || window.backupLocalizedStrings.failedToDeleteBackup,
                            icon: 'error'
                        });
                    }
                },
                error: function() {
                    Swal.fire({
                        title: window.backupLocalizedStrings.error,
                        text: window.backupLocalizedStrings.failedToDeleteBackup,
                        icon: 'error'
                    });
                }
            });
        }
    });
}

function toggleSelectAll(checkbox) {
    $('.backup-checkbox').prop('checked', checkbox.checked);
}

function bulkDeleteBackups() {
    const selectedIds = [];
    $('.backup-checkbox:checked').each(function() {
        selectedIds.push($(this).data('id'));
    });
    
    if (selectedIds.length === 0) {
        toastr.warning(window.backupLocalizedStrings.pleaseSelectBackupsToDelete);
        return;
    }
    
    Swal.fire({
        title: window.backupLocalizedStrings.bulkDeleteTitle,
        text: (window.backupLocalizedStrings.confirmDeleteBackups || 'Are you sure you want to delete {0} backups?').replace('{0}', selectedIds.length),
        icon: 'warning',
        showCancelButton: true,
        confirmButtonColor: '#d33',
        cancelButtonColor: '#6c757d',
        confirmButtonText: window.backupLocalizedStrings.yesDelete,
        cancelButtonText: window.backupLocalizedStrings.cancel,
        reverseButtons: true
    }).then(function(result) {
        if (result.isConfirmed) {
            $.ajax({
                url: window.backupUrls.bulkDeleteBackups,
                type: 'POST',
                contentType: 'application/json',
                data: JSON.stringify(selectedIds),
                success: function(response) {
                    if (response.success) {
                        Swal.fire({
                            title: window.backupLocalizedStrings.deleted,
                            text: response.message,
                            icon: 'success',
                            timer: 2000,
                            showConfirmButton: false
                        });
                        loadBackups();
                        loadStats();
                    } else {
                        Swal.fire({
                            title: window.backupLocalizedStrings.error,
                            text: response.message,
                            icon: 'error'
                        });
                    }
                },
                error: function() {
                    Swal.fire({
                        title: window.backupLocalizedStrings.error,
                        text: window.backupLocalizedStrings.failedToDeleteBackups,
                        icon: 'error'
                    });
                }
            });
        }
    });
}

function validateAllBackups() {
    const selectedIds = [];
    $('.backup-checkbox:checked').each(function() {
        selectedIds.push($(this).data('id'));
    });
    
    if (selectedIds.length === 0) {
        toastr.warning(window.backupLocalizedStrings.pleaseSelectBackupsToValidate);
        return;
    }
    
    toastr.info('Validating backups...', '', { timeOut: 2000 });
    
    $.ajax({
        url: window.backupUrls.bulkValidateBackups,
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(selectedIds),
        success: function(response) {
            if (response.success) {
                toastr.success(response.message);
            } else {
                toastr.warning(response.message);
            }
        },
        error: function() {
            toastr.error(window.backupLocalizedStrings.failedToValidateBackups);
        }
    });
}

function startRestore() {
    const backupId = $('#restoreBackupSelect').val();
    if (!backupId) {
        toastr.warning(window.backupLocalizedStrings.pleaseSelectBackupToRestore);
        return;
    }

    const data = {
        backupId: backupId,
        restoreData: $('#restoreData').is(':checked'),
        restoreSchema: $('#restoreSchema').is(':checked'),
        createBackupBefore: $('#createBackupBeforeRestore').is(':checked'),
        description: $('#restoreDescription').val()
    };

    showProgress('Starting restore operation...');

    $.ajax({
        url: window.backupUrls.restoreBackup,
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(data),
        success: function(response) {
            if (response.success) {
                operationId = response.operationId;
                toastr.success(window.backupLocalizedStrings.restoreStartedSuccessfully);
                if (operationId) {
                    pollOperationStatus();
                } else {
                    hideProgress(true);
                }
            } else {
                hideProgress(false);
                toastr.error(response.message);
            }
        },
        error: function() {
            hideProgress(false);
            toastr.error(window.backupLocalizedStrings.failedToStartRestore);
        }
    });
}

function showEmergencyOptions() {
    toastr.info(window.backupLocalizedStrings.emergencyOptionsComingSoon);
}

// Schedule Functions
function showScheduleModal(scheduleId = null) {
    $('#scheduleForm')[0].reset();
    $('#scheduleId').val('0');
    $('#scheduleTime').val('02:00');
    $('#scheduleIsActive').prop('checked', true);
    $('#scheduleRetentionDays').val('');
    toggleFrequencyOptions();
    
    if (scheduleId) {
        $('#scheduleModalTitle').text(window.backupLocalizedStrings.editSchedule);
        loadScheduleForEdit(scheduleId);
    } else {
        $('#scheduleModalTitle').text(window.backupLocalizedStrings.createSchedule);
    }
    
    $('#scheduleModal').modal('show');
}

function loadScheduleForEdit(scheduleId) {
    $.get(window.backupUrls.getSchedules, function(response) {
        if (response.success && response.data) {
            const schedule = response.data.find(s => s.id === scheduleId);
            if (schedule) {
                $('#scheduleId').val(schedule.id);
                $('#scheduleName').val(schedule.name);
                $('#scheduleDescription').val(schedule.description);
                $('#scheduleBackupType').val(schedule.backupType);
                $('#scheduleFrequency').val(schedule.scheduleFrequency);
                
                if (schedule.scheduleTime) {
                    const timeParts = schedule.scheduleTime.split(':');
                    const hours = timeParts[0].padStart(2, '0');
                    const minutes = timeParts[1].padStart(2, '0');
                    $('#scheduleTime').val(`${hours}:${minutes}`);
                }
                
                if (schedule.scheduleDayOfWeek !== null) {
                    $('#scheduleDayOfWeek').val(schedule.scheduleDayOfWeek);
                }
                if (schedule.scheduleDayOfMonth !== null) {
                    $('#scheduleDayOfMonth').val(schedule.scheduleDayOfMonth);
                }
                $('#scheduleIsActive').prop('checked', schedule.isActive);
                
                // Set retention days
                if (schedule.retentionDays !== null && schedule.retentionDays > 0) {
                    $('#scheduleRetentionDays').val(schedule.retentionDays);
                } else {
                    $('#scheduleRetentionDays').val('');
                }
                
                toggleFrequencyOptions();
            }
        }
    });
}

function toggleFrequencyOptions() {
    const frequency = $('#scheduleFrequency').val();
    $('#dayOfWeekContainer').hide();
    $('#dayOfMonthContainer').hide();
    
    if (frequency === 'Weekly') {
        $('#dayOfWeekContainer').show();
    } else if (frequency === 'Monthly') {
        $('#dayOfMonthContainer').show();
    }
}

function loadSchedules() {
    $.get(window.backupUrls.getSchedules, function(response) {
        const table = $('#schedulesTable');
        
        if (response.success && response.data && response.data.length > 0) {
            let html = `
                <table class="table table-hover modern-table">
                    <thead>
                        <tr>
                            <th class="fw-bold">${window.backupLocalizedStrings.scheduleName}</th>
                            <th class="text-center">${window.backupLocalizedStrings.type}</th>
                            <th>${window.backupLocalizedStrings.frequency}</th>
                            <th>${window.backupLocalizedStrings.nextRun}</th>
                            <th>${window.backupLocalizedStrings.lastRun}</th>
                            <th class="text-center">${window.backupLocalizedStrings.status}</th>
                            <th class="text-center" style="width: 150px;">${window.backupLocalizedStrings.actions}</th>
                        </tr>
                    </thead>
                    <tbody>
            `;
            
            response.data.forEach(function(schedule) {
                const statusBadge = schedule.isActive 
                    ? `<span class="badge bg-success"><i class="fas fa-check-circle me-1"></i>${window.backupLocalizedStrings.active}</span>`
                    : `<span class="badge bg-secondary"><i class="fas fa-pause-circle me-1"></i>${window.backupLocalizedStrings.inactive}</span>`;
                
                const nextRun = schedule.nextRun ? formatDate(schedule.nextRun) : `<span class="text-muted">${window.backupLocalizedStrings.never}</span>`;
                const lastRun = schedule.lastRun ? formatDate(schedule.lastRun) : `<span class="text-muted">${window.backupLocalizedStrings.never}</span>`;
                
                const frequencyText = getFrequencyText(schedule);
                
                let typeBadgeClass = 'bg-info';
                if (schedule.backupType === 'Full') typeBadgeClass = 'bg-primary';
                else if (schedule.backupType === 'Incremental') typeBadgeClass = 'bg-warning';
                else if (schedule.backupType === 'Differential') typeBadgeClass = 'bg-success';
                
                html += `
                    <tr>
                        <td>
                            <div class="fw-bold">${schedule.name}</div>
                            ${schedule.description ? `<small class="text-muted">${schedule.description}</small>` : ''}
                        </td>
                        <td class="text-center"><span class="badge ${typeBadgeClass}">${schedule.backupType}</span></td>
                        <td><i class="fas fa-clock text-muted me-1"></i>${frequencyText}</td>
                        <td>${nextRun}</td>
                        <td>${lastRun}</td>
                        <td class="text-center">${statusBadge}</td>
                        <td class="text-center">
                            <div class="btn-group btn-group-sm">
                                <button class="btn btn-outline-info" onclick="runScheduleNow(${schedule.id})" title="${window.backupLocalizedStrings.runNow}">
                                    <i class="fas fa-rocket"></i>
                                </button>
                                <button class="btn btn-outline-primary" onclick="showScheduleModal(${schedule.id})" title="${window.backupLocalizedStrings.edit}">
                                    <i class="fas fa-edit"></i>
                                </button>
                                ${schedule.isActive 
                                    ? `<button class="btn btn-outline-warning" onclick="toggleScheduleStatus(${schedule.id}, false)" title="${window.backupLocalizedStrings.disable}">
                                        <i class="fas fa-pause"></i>
                                       </button>`
                                    : `<button class="btn btn-outline-success" onclick="toggleScheduleStatus(${schedule.id}, true)" title="${window.backupLocalizedStrings.enable}">
                                        <i class="fas fa-play"></i>
                                       </button>`
                                }
                                <button class="btn btn-outline-danger" onclick="deleteSchedule(${schedule.id})" title="${window.backupLocalizedStrings.delete}">
                                    <i class="fas fa-trash"></i>
                                </button>
                            </div>
                        </td>
                    </tr>
                `;
            });
            
            html += '</tbody></table>';
            table.html(html);
        } else {
            table.html(`<div class="text-center py-5">
                <i class="fas fa-calendar-plus fa-3x text-muted mb-3"></i>
                <h5 class="text-muted">${window.backupLocalizedStrings.noSchedulesFound}</h5>
                <p class="text-muted mb-3">Create one to automate your backups.</p>
                <button class="btn btn-primary" onclick="showScheduleModal()">
                    <i class="fas fa-plus me-2"></i>${window.backupLocalizedStrings.createSchedule}
                </button>
            </div>`);
        }
    }).fail(function() {
        $('#schedulesTable').html(`<div class="erp-alert erp-alert-danger text-center">
            <i class="fas fa-exclamation-triangle me-2"></i>
            Failed to load schedules
        </div>`);
    });
}

function getFrequencyText(schedule) {
    let text = '';
    const frequency = schedule.scheduleFrequency;
    
    let timeStr = '02:00';
    if (schedule.scheduleTime) {
        const timeParts = schedule.scheduleTime.split(':');
        timeStr = `${timeParts[0].padStart(2, '0')}:${timeParts[1].padStart(2, '0')}`;
    }
    
    switch (frequency?.toLowerCase()) {
        case 'daily':
            text = `${window.backupLocalizedStrings.daily} @ ${timeStr}`;
            break;
        case 'weekly':
            const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
            const dayName = days[schedule.scheduleDayOfWeek] || 'Sunday';
            text = `${window.backupLocalizedStrings.weekly} (${dayName}) @ ${timeStr}`;
            break;
        case 'monthly':
            const dayOfMonth = schedule.scheduleDayOfMonth || 1;
            text = `${window.backupLocalizedStrings.monthly} (Day ${dayOfMonth}) @ ${timeStr}`;
            break;
        default:
            text = frequency || 'Unknown';
    }
    
    return text;
}

function saveSchedule() {
    const scheduleName = $('#scheduleName').val().trim();
    if (!scheduleName) {
        toastr.warning(window.backupLocalizedStrings.pleaseEnterScheduleName);
        return;
    }

    const scheduleTime = $('#scheduleTime').val();
    const timeParts = scheduleTime.split(':');
    const timeSpan = `${timeParts[0]}:${timeParts[1]}:00`;

    const retentionDaysVal = $('#scheduleRetentionDays').val();
    const retentionDays = retentionDaysVal ? parseInt(retentionDaysVal) : null;

    const scheduleData = {
        id: parseInt($('#scheduleId').val()) || 0,
        name: scheduleName,
        description: $('#scheduleDescription').val(),
        backupType: $('#scheduleBackupType').val(),
        scheduleFrequency: $('#scheduleFrequency').val(),
        scheduleTime: timeSpan,
        scheduleDayOfWeek: $('#scheduleFrequency').val() === 'Weekly' ? parseInt($('#scheduleDayOfWeek').val()) : null,
        scheduleDayOfMonth: $('#scheduleFrequency').val() === 'Monthly' ? parseInt($('#scheduleDayOfMonth').val()) : null,
        isActive: $('#scheduleIsActive').is(':checked'),
        retentionDays: retentionDays,
        settings: {}
    };

    const isEdit = scheduleData.id > 0;
    const url = isEdit ? window.backupUrls.updateSchedule : window.backupUrls.createSchedule;
    const method = isEdit ? 'PUT' : 'POST';

    $.ajax({
        url: url,
        type: method,
        contentType: 'application/json',
        data: JSON.stringify(scheduleData),
        success: function(response) {
            if (response.success) {
                toastr.success(isEdit 
                    ? window.backupLocalizedStrings.scheduleUpdatedSuccessfully
                    : window.backupLocalizedStrings.scheduleCreatedSuccessfully);
                $('#scheduleModal').modal('hide');
                loadSchedules();
                loadStats();
            } else {
                toastr.error(response.message || window.backupLocalizedStrings.failedToSaveSchedule);
            }
        },
        error: function() {
            toastr.error(window.backupLocalizedStrings.failedToSaveSchedule);
        }
    });
}

function toggleScheduleStatus(scheduleId, enable) {
    const url = enable ? window.backupUrls.enableSchedule : window.backupUrls.disableSchedule;

    $.ajax({
        url: url,
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(scheduleId),
        success: function(response) {
            if (response.success) {
                toastr.success(enable 
                    ? window.backupLocalizedStrings.scheduleEnabledSuccessfully
                    : window.backupLocalizedStrings.scheduleDisabledSuccessfully);
                loadSchedules();
                loadStats();
            } else {
                toastr.error(response.message);
            }
        },
        error: function() {
            toastr.error('Failed to update schedule status');
        }
    });
}

function deleteSchedule(scheduleId) {
    Swal.fire({
        title: window.backupLocalizedStrings.deleteScheduleTitle,
        text: window.backupLocalizedStrings.confirmDeleteSchedule,
        icon: 'warning',
        showCancelButton: true,
        confirmButtonColor: '#d33',
        cancelButtonColor: '#6c757d',
        confirmButtonText: window.backupLocalizedStrings.yesDelete,
        cancelButtonText: window.backupLocalizedStrings.cancel,
        reverseButtons: true
    }).then(function(result) {
        if (result.isConfirmed) {
            $.ajax({
                url: window.backupUrls.deleteSchedule,
                type: 'DELETE',
                contentType: 'application/json',
                data: JSON.stringify(scheduleId),
                success: function(response) {
                    if (response.success) {
                        Swal.fire({
                            title: window.backupLocalizedStrings.deleted,
                            text: window.backupLocalizedStrings.scheduleDeletedSuccessfully,
                            icon: 'success',
                            timer: 2000,
                            showConfirmButton: false
                        });
                        loadSchedules();
                        loadStats();
                    } else {
                        Swal.fire({
                            title: window.backupLocalizedStrings.error,
                            text: response.message || window.backupLocalizedStrings.failedToDeleteSchedule,
                            icon: 'error'
                        });
                    }
                },
                error: function() {
                    Swal.fire({
                        title: window.backupLocalizedStrings.error,
                        text: window.backupLocalizedStrings.failedToDeleteSchedule,
                        icon: 'error'
                    });
                }
            });
        }
    });
}

function runScheduleNow(scheduleId) {
    toastr.info('Starting backup...', '', { timeOut: 2000 });
    
    $.ajax({
        url: window.backupUrls.runScheduleNow,
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(scheduleId),
        success: function(response) {
            if (response.success) {
                toastr.success(window.backupLocalizedStrings.backupStartedFromSchedule);
                loadSchedules();
                loadStats();
                loadBackups();
            } else {
                toastr.error(response.message || window.backupLocalizedStrings.failedToRunSchedule);
            }
        },
        error: function() {
            toastr.error(window.backupLocalizedStrings.failedToRunSchedule);
        }
    });
}

function generateReport() {
    toastr.info('Report generation coming soon');
}

function loadSystemInfo() {
    $.get(window.backupUrls.getSystemInfo, function(response) {
        if (response.success) {
            $('#systemInfoCard').html(`
                <table class="table table-sm">
                    <tr><td>Database Server</td><td>${response.data.databaseServer}</td></tr>
                    <tr><td>Database Name</td><td>${response.data.databaseName}</td></tr>
                    <tr><td>Backup Directory</td><td>${response.data.backupDirectory}</td></tr>
                    <tr><td>Free Space</td><td>${formatBytes(response.data.freeSpace)}</td></tr>
                </table>
            `);
        }
    });
}

function debugBackupDirectories() {
    $.get(window.backupUrls.debugDirectories, function(response) {
        console.log('Backup directories debug:', response);
        toastr.info('Check console for debug information');
    });
}

// Utility functions
function formatBytes(bytes) {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}

function formatDate(dateString) {
    return new Date(dateString).toLocaleString();
}
