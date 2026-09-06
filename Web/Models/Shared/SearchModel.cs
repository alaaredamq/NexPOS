namespace Web.Models.Shared;

public class SearchModel
{
    public string Id { get; set; } = string.Empty;
    public string Placeholder { get; set; } = "Search...";
    public string Class { get; set; } = "form-control";
    public string ContainerClass { get; set; } = "mb-3";
    public string ButtonClass { get; set; } = "btn btn-outline-secondary";
    public string IconClass { get; set; } = "fas fa-search";
    public bool ShowButton { get; set; } = true;
    public bool ShowIcon { get; set; } = true;
    public int Delay { get; set; } = 300; // milliseconds
    public int MinimumLength { get; set; } = 2;
    public string OnSearch { get; set; } = string.Empty; // JavaScript function
    public string OnClear { get; set; } = string.Empty; // JavaScript function
    public bool AutoSearch { get; set; } = true;
    public string Language { get; set; } = "en";
    public Dictionary<string, object> AdditionalAttributes { get; set; } = new();
}
