namespace Web.Models.Shared;

public class DataTableModel
{
    public string Id { get; set; } = string.Empty;
    public string Class { get; set; } = "table table-striped table-bordered";
    public string DataSource { get; set; } = string.Empty; // API endpoint
    public List<DataTableColumn> Columns { get; set; } = new();
    public bool ServerSide { get; set; } = true;
    public bool Processing { get; set; } = true;
    public bool Ordering { get; set; } = true;
    public bool Searching { get; set; } = true;
    public bool Paging { get; set; } = true;
    public int PageLength { get; set; } = 10;
    public List<int> PageLengthMenu { get; set; } = new() { 10, 25, 50, 100 };
    public string Language { get; set; } = "en";
    public string Dom { get; set; } = "<'row'<'col-sm-12 col-md-6'l><'col-sm-12 col-md-6'f>>" +
                                      "<'row'<'col-sm-12'tr>>" +
                                      "<'row'<'col-sm-12 col-md-5'i><'col-sm-12 col-md-7'p>>";
    public Dictionary<string, object> AdditionalOptions { get; set; } = new();
    public List<DataTableButton> Buttons { get; set; } = new();
    public bool Responsive { get; set; } = true;
    public bool ScrollX { get; set; } = false;
    public string ScrollY { get; set; } = string.Empty;
}

public class DataTableColumn
{
    public string Data { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public bool Orderable { get; set; } = true;
    public bool Searchable { get; set; } = true;
    public bool Visible { get; set; } = true;
    public int Width { get; set; } = 0;
    public string Render { get; set; } = string.Empty; // JavaScript function
    public Dictionary<string, object> AdditionalAttributes { get; set; } = new();
}

public class DataTableButton
{
    public string Text { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // JavaScript function
    public string ClassName { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public Dictionary<string, object> AdditionalAttributes { get; set; } = new();
}
