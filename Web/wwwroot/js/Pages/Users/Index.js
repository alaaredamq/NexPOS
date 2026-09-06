// Users Index Page Script
(function() {
    'use strict';

    function urls() { return window.usersUrls || {}; }
    function strings() { return window.usersLocalizedStrings || {}; }

    function notifySuccess(msg) {
        if (typeof toastr !== 'undefined') { toastr.success(msg); } else { alert(msg); }
    }
    function notifyError(msg) {
        if (typeof toastr !== 'undefined') { toastr.error(msg); } else { alert(msg); }
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

            const password = $('#password').val();
            const confirmPassword = $('#confirmPassword').val();
            const pin = $('#pin').val();

            if (password !== confirmPassword) {
                notifyError(strings().passwordsDoNotMatch || 'Passwords do not match');
                return;
            }
            if (!/^\d{4,6}$/.test(pin)) {
                notifyError('PIN must be 4-6 digits');
                return;
            }

            const formData = {
                username: $('#username').val(),
                email: $('#email').val(),
                firstName: $('#firstName').val(),
                lastName: $('#lastName').val(),
                password: password,
                confirmPassword: confirmPassword,
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

            if (!/^\d{4,6}$/.test(newPin)) {
                notifyError('PIN must be 4-6 digits');
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

        // Add role form submission
        $('#addRoleForm').on('submit', function(e) {
            e.preventDefault();

            const permissions = [];
            $('.permissions-selection input[type="checkbox"]:checked').each(function() {
                permissions.push($(this).attr('id').replace('perm_', '').replace('_', '.'));
            });

            const formData = {
                name: $('#roleName').val(),
                description: $('#roleDescription').val(),
                permissions: permissions
            };

            $.ajax({
                url: urls().createRoleUrl || '',
                type: 'POST',
                contentType: 'application/json',
                data: JSON.stringify(formData),
                success: function(response) {
                    if (response.success) {
                        notifySuccess(strings().roleCreatedSuccessfully || 'Role created successfully');
                        $('#addRoleModal').modal('hide');
                        $('#addRoleForm')[0].reset();
                        reloadRoles();
                    } else {
                        notifyError(response.message || (strings().failedToCreateRole || 'Failed to create role'));
                    }
                },
                error: function() {
                    notifyError(strings().failedToCreateRole || 'Failed to create role');
                }
            });
        });

        // Load user statistics
        loadUserStats();
    }

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
        if (!confirm(strings().deleteUserConfirm || 'Delete this user? This cannot be undone.')) {
            return;
        }
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
    };

    window.terminateSession = function(id) {
        if (!confirm(strings().terminateSessionConfirm || 'Terminate this session?')) {
            return;
        }
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
