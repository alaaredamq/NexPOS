function ErpDatatable (id, options) {
    const variables = {
        tableId: id,
        dataTable: null
    };
    
    const reloadData = function() {
        variables.dataTable.ajax.reload();
    };

    const getDatatable = function () {
        return variables.dataTable;
    }

    const search = function (value) {
        variables.dataTable.search(value).draw();
    };

    const initialize = function() {
        variables.dataTable = $('#' + variables.tableId).DataTable(options);
    };

    initialize();
    
    return {
        reloadData: reloadData,
        getDatatable: getDatatable,
        search: search
    };
}