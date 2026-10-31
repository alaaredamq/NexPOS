// Users Index Page Script
(function() {
    'use strict';

    function urls() { return window.usersUrls || {}; }
    function strings() { return window.usersLocalizedStrings || {}; }

    function notifySuccess(msg) {
        if (typeof toastr !== 'undefined') { toastr.success(msg); } else { appAlert(msg, 'success'); }
    }
    function notifyError(msg) {
        if (typeof toastr !== 'undefined') { toastr.error(msg); } else { appAlert(msg, 'error'); }
    }
    function reloadUsers() {
        if (window.dataTable_usersTable) { window.dataTable_usersTable.ajax.reload(null, false); }
        if (typeof window.loadUserStats === 'function') { window.loadUserStats(); }
    }
    function reloadSessions() {
        if (window.dataTable_sessionsTable) { window.dataTable_sessionsTable.ajax.reload(null, false); }
    }
    function reloadRoles() {
        if (window.dataTable_rolesTable) { window.dataTable_rolesTable.ajax.reload(null, false); }
    }

    // Initialize the page
    function init() {
        var usersTable = window.dataTable_usersTable;
        var rolesTable = window.dataTable_rolesTable;

        // Add user form submission
        $('#addUserForm').on('submit', function(e) {
            e.preventDefault();

            const pin = $('#pin').val();

            if (!/^\d{4}$/.test(pin)) {
                notifyError('PIN must be exactly 4 digits');
                return;
            }

            const formData = {
                username: $('#username').val(),
                email: $('#email').val(),
                firstName: $('#firstName').val(),
                lastName: $('#lastName').val(),
                pin: pin,
                role: $('#userRole').val(),
                isActive: $('#isActive').is(':checked')
            };

            $.ajax({
                url: urls().createUrl || '',
                type: 'POST',
                contentType: 'application/json',
                data: JSON.stringify(formData),
                success: function(response) {
                    if (response.success) {
                        notifySuccess(strings().userCreatedSuccessfully || 'User created successfully');
                        $('#addUserModal').modal('hide');
                        $('#addUserForm')[0].reset();
                        reloadUsers();
                    } else {
                        notifyError(response.message || (strings().failedToCreateUser || 'Failed to create user'));
                    }
                },
                error: function() {
                    notifyError(strings().failedToCreateUser || 'Failed to create user');
                }
            });
        });

        // Edit user form submission
        $('#editUserForm').on('submit', function(e) {
            e.preventDefault();
            const id = $('#editUserId').val();

            const formData = {
                firstName: $('#editFirstName').val(),
                lastName: $('#editLastName').val(),
                email: $('#editEmail').val(),
                roleId: parseInt($('#editUserRole').val(), 10),
                isActive: $('#editIsActive').is(':checked')
            };

            $.ajax({
                url: (urls().updateUserUrl || '') + '/' + id,
                type: 'PUT',
                contentType: 'application/json',
                data: JSON.stringify(formData),
                success: function(response) {
                    if (response.success) {
                        notifySuccess(response.message || 'User updated successfully');
                        $('#editUserModal').modal('hide');
                        reloadUsers();
                    } else {
                        notifyError(response.message || 'Failed to update user');
                    }
                },
                error: function() { notifyError('Failed to update user'); }
            });
        });

        // Reset PIN form submission
        $('#resetPinForm').on('submit', function(e) {
            e.preventDefault();
            const id = $('#resetPinUserId').val();
            const newPin = $('#newPin').val();

            if (!/^\d{4}$/.test(newPin)) {
                notifyError('PIN must be exactly 4 digits');
                return;
            }

            $.ajax({
                url: (urls().resetPinUrl || '') + '/' + id,
                type: 'POST',
                contentType: 'application/json',
                data: JSON.stringify({ newPin: newPin }),
                success: function(response) {
                    if (response.success) {
                        notifySuccess(response.message || 'PIN reset successfully');
                        $('#resetPinModal').modal('hide');
                        $('#resetPinForm')[0].reset();
                    } else {
                        notifyError(response.message || 'Failed to reset PIN');
                    }
                },
                error: function() { notifyError('Failed to reset PIN'); }
            });
        });

        // Role editor: one modal for both creating and editing (hidden #roleId is empty when creating)
        $('#roleForm').on('submit', function(e) {
            e.preventDefault();

            const id = $('#roleId').val();
            const isEdit = id !== '';
            const name = $('#roleName').val().trim();

            const formData = {
                name: name,
                description: $('#roleDescription').val(),
                permissions: checkedPermissions()
            };

            $.ajax({
                url: isEdit ? (urls().updateRoleUrl || '') + '/' + id : (urls().createRoleUrl || ''),
                type: isEdit ? 'PUT' : 'POST',
                contentType: 'application/json',
                data: JSON.stringify(formData),
                success: function(response) {
                    if (response.success) {
                        notifySuccess(isEdit
                            ? (response.message || 'Role updated successfully')
                            : (strings().roleCreatedSuccessfully || 'Role created successfully'));
                        $('#roleModal').modal('hide');
                        syncRoleOptions(isEdit ? id : String(response.id), name);
                        reloadRoles();
                        if (typeof window.loadUserStats === 'function') { window.loadUserStats(); }
                    } else {
                        notifyError(response.message || (strings().failedToCreateRole || 'Failed to save role'));
                    }
                },
                error: function() {
                    notifyError(strings().failedToCreateRole || 'Failed to save role');
                }
            });
        });

        // Ticking a group ticks all of its permissions; the group box reflects how many are on.
        $('#roleModal').on('change', '.permission-group-toggle', function() {
            const group = $(this).data('group');
            const on = this.checked;
            $('.permission-checkbox').filter(function() { return $(this).data('group') === group; })
                .prop('checked', on);
            refreshRoleSelection();
        });
        $('#roleModal').on('change', '.permission-checkbox', refreshRoleSelection);

        // Load user statistics
        loadUserStats();
    }

    // ---- Role editor helpers ----
    function checkedPermissions() {
        return $('.permission-checkbox:checked').map(function() { return this.value; }).get();
    }

    // Keeps the "n / total selected" counter and each group's checkbox (checked / partly) in step with the ticks.
    function refreshRoleSelection() {
        $('#rolePermissionCount').text($('.permission-checkbox:checked').length);
        $('.permission-group-toggle').each(function() {
            const group = $(this).data('group');
            const boxes = $('.permission-checkbox').filter(function() { return $(this).data('group') === group; });
            const on = boxes.filter(':checked').length;
            this.checked = on === boxes.length;
            this.indeterminate = on > 0 && on < boxes.length;
        });
    }

    function setRoleFormLocked(locked) {
        $('#roleForm').find('input[type="text"], input[type="checkbox"]').prop('disabled', locked);
        $('#roleSaveButton').toggleClass('d-none', locked);
        $('#superAdminNote').toggleClass('d-none', !locked);
    }

    // The role pickers in the add/edit user modals are rendered by the server, so a new, renamed or removed
    // role has to be reflected there too or the page would need a reload before it could be assigned.
    function syncRoleOptions(id, name) {
        const addOption = $('#userRole option').filter(function() { return String($(this).data('id')) === String(id); });
        if (addOption.length) { addOption.val(name).text(name); }
        else { $('#userRole').append($('<option>').val(name).attr('data-id', id).text(name)); }

        const editOption = $('#editUserRole option').filter(function() { return String(this.value) === String(id); });
        if (editOption.length) { editOption.text(name); }
        else { $('#editUserRole').append($('<option>').val(id).text(name)); }
    }

    function removeRoleOption(id) {
        $('#userRole option').filter(function() { return String($(this).data('id')) === String(id); }).remove();
        $('#editUserRole option').filter(function() { return String(this.value) === String(id); }).remove();
    }

    window.openRoleModal = function() {
        $('#roleForm')[0].reset();
        $('#roleId').val('');
        $('.permission-checkbox').prop('checked', false);
        setRoleFormLocked(false);
        refreshRoleSelection();
        $('#roleModalTitle').text('Add New Role');
        $('#roleModal').modal('show');
    };

    window.editRole = function(id) {
        $.ajax({
            url: (urls().getRoleUrl || '') + '/' + id,
            type: 'GET',
            success: function(response) {
                if (!response || !response.success) {
                    notifyError((response && response.message) || 'Failed to load role');
                    return;
                }
                const r = response.role;
                $('#roleForm')[0].reset();
                $('#roleId').val(r.id);
                $('#roleName').val(r.name);
                $('#roleDescription').val(r.description || '');
                const granted = new Set(r.permissions);
                $('.permission-checkbox').each(function() { this.checked = granted.has(this.value); });
                setRoleFormLocked(!!r.isSuperAdmin);
                refreshRoleSelection();
                $('#roleModalTitle').text(r.isSuperAdmin ? 'View Role' : 'Edit Role');
                $('#roleModal').modal('show');
            },
            error: function() { notifyError('Failed to load role'); }
        });
    };

    window.deleteRole = function(id) {
        appConfirm('Delete this role? This cannot be undone.').then(function(ok) {
            if (!ok) return;
            $.ajax({
                url: (urls().deleteRoleUrl || '') + '/' + id,
                type: 'DELETE',
                success: function(response) {
                    if (response.success) {
                        notifySuccess(response.message || 'Role deleted');
                        removeRoleOption(id);
                        reloadRoles();
                        if (typeof window.loadUserStats === 'function') { window.loadUserStats(); }
                    } else {
                        notifyError(response.message || 'Failed to delete role');
                    }
                },
                error: function() { notifyError('Failed to delete role'); }
            });
        });
    };

    // Row action handlers (referenced from server-rendered onclick attributes)
    window.editUser = function(id) {
        $.ajax({
            url: (urls().getUserUrl || '') + '/' + id,
            type: 'GET',
            success: function(response) {
                if (!response || !response.success) {
                    notifyError((response && response.message) || 'Failed to load user');
                    return;
                }
                var u = response.user;
                $('#editUserId').val(u.id);
                $('#editUsername').val(u.username);
                $('#editEmail').val(u.email);
                $('#editFirstName').val(u.firstName || '');
                $('#editLastName').val(u.lastName || '');
                $('#editUserRole').val(u.roleId);
                $('#editIsActive').prop('checked', u.isActive);
                $('#editUserModal').modal('show');
            },
            error: function() { notifyError('Failed to load user'); }
        });
    };

    window.resetUserPin = function(id) {
        $('#resetPinForm')[0].reset();
        $('#resetPinUserId').val(id);
        $('#resetPinModal').modal('show');
    };

    window.toggleUserStatus = function(id) {
        $.ajax({
            url: (urls().toggleStatusUrl || '') + '/' + id,
            type: 'POST',
            success: function(response) {
                if (response.success) {
                    notifySuccess(response.message || 'User status updated');
                    reloadUsers();
                } else {
                    notifyError(response.message || 'Failed to update status');
                }
            },
            error: function() { notifyError('Failed to update status'); }
        });
    };

    window.deleteUser = function(id) {
        appConfirm(strings().deleteUserConfirm || 'Delete this user? This cannot be undone.').then(function(ok) {
            if (!ok) return;
            $.ajax({
                url: (urls().deleteUserUrl || '') + '/' + id,
                type: 'DELETE',
                success: function(response) {
                    if (response.success) {
                        notifySuccess(response.message || 'User deleted');
                        reloadUsers();
                    } else {
                        notifyError(response.message || 'Failed to delete user');
                    }
                },
                error: function() { notifyError('Failed to delete user'); }
            });
        });
    };

    window.terminateSession = function(id) {
        appConfirm(strings().terminateSessionConfirm || 'Terminate this session?', { title: window.i18n.t('common.confirm', 'Confirm'), confirmText: window.i18n.t('common.yes', 'Yes') }).then(function(ok) {
            if (!ok) return;
            $.ajax({
                url: (urls().terminateSessionUrl || '') + '/' + id,
                type: 'POST',
                success: function(response) {
                    if (response.success) {
                        notifySuccess(response.message || 'Session terminated');
                        reloadSessions();
                    } else {
                        notifyError(response.message || 'Failed to terminate session');
                    }
                },
                error: function() { notifyError('Failed to terminate session'); }
            });
        });
    };

    function loadUserStats() {
        $.ajax({
            url: urls().getStatsUrl || '',
            type: 'GET',
            success: function(stats) {
                if (stats) {
                    $('#totalUsers').text(stats.totalUsers);
                    $('#totalRoles').text(stats.totalRoles);
                    $('#activeUsers').text(stats.activeUsers);
                    $('#adminUsers').text(stats.adminUsers);
                }
            },
            error: function() {
                console.log('Failed to load user stats');
            }
        });
    }

    window.loadUserStats = loadUserStats;

    // Initialize when document is ready
    $(document).ready(function() {
        init();
    });

})();
