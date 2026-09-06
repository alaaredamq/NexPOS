namespace Web.Models.Shared;

public class Select2Model
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Class { get; set; } = "form-select";
    public string Placeholder { get; set; } = string.Empty;
    public bool Multiple { get; set; } = false;
    public bool AllowClear { get; set; } = true;
    public bool Required { get; set; } = false;
    public bool Disabled { get; set; } = false;
    public string DataSource { get; set; } = string.Empty; // API endpoint
    public string ValueField { get; set; } = "id";
    public string TextField { get; set; } = "text";
    public string SearchField { get; set; } = "text";
    public int MinimumInputLength { get; set; } = 0;
    public int Delay { get; set; } = 250;
    public string Language { get; set; } = "en";
    public object? Value { get; set; }
    public List<Select2Item> Items { get; set; } = new();
    public Dictionary<string, object> AdditionalAttributes { get; set; } = new();
}

public class Select2Item
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool Selected { get; set; } = false;
    public bool Disabled { get; set; } = false;
    public Dictionary<string, object> AdditionalData { get; set; } = new();
}
