namespace Web.Models.Shared;

public class FormModel
{
    public string Id { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Controller { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public string Class { get; set; } = string.Empty;
    public bool EnableClientValidation { get; set; } = true;
    public bool EnableUnobtrusiveValidation { get; set; } = true;
    public string ValidationSummary { get; set; } = "All"; // None, ModelOnly, All
    public bool ShowValidationSummary { get; set; } = true;
    public List<FormField> Fields { get; set; } = new();
    public List<FormButton> Buttons { get; set; } = new();
    public Dictionary<string, object> AdditionalAttributes { get; set; } = new();
    public string Language { get; set; } = "en";
    public bool Rtl { get; set; } = false;
}

public class FormField
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = "text"; // text, email, password, number, select, textarea, checkbox, radio
    public string Value { get; set; } = string.Empty;
    public string Placeholder { get; set; } = string.Empty;
    public string Class { get; set; } = "form-control";
    public string LabelClass { get; set; } = "form-label";
    public string ContainerClass { get; set; } = "mb-3";
    public bool Required { get; set; } = false;
    public bool ReadOnly { get; set; } = false;
    public bool Disabled { get; set; } = false;
    public string ValidationMessage { get; set; } = string.Empty;
    public List<Select2Item> Options { get; set; } = new(); // For select fields
    public int Rows { get; set; } = 3; // For textarea
    public int Cols { get; set; } = 50; // For textarea
    public string Min { get; set; } = string.Empty; // For number inputs
    public string Max { get; set; } = string.Empty; // For number inputs
    public string Step { get; set; } = string.Empty; // For number inputs
    public Dictionary<string, object> AdditionalAttributes { get; set; } = new();
}

public class FormButton
{
    public string Text { get; set; } = string.Empty;
    public string Type { get; set; } = "submit"; // submit, button, reset
    public string Class { get; set; } = "btn btn-primary";
    public string OnClick { get; set; } = string.Empty; // JavaScript function
    public string Icon { get; set; } = string.Empty;
    public bool Disabled { get; set; } = false;
    public Dictionary<string, object> AdditionalAttributes { get; set; } = new();
}
