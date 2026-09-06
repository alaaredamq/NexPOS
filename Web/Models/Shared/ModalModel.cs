namespace Web.Models.Shared;

public class ModalModel
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Size { get; set; } = "modal-lg"; // modal-sm, modal-lg, modal-xl
    public bool Centered { get; set; } = true;
    public bool Scrollable { get; set; } = true;
    public bool Backdrop { get; set; } = true;
    public bool Keyboard { get; set; } = true;
    public bool Static { get; set; } = false;
    public string Class { get; set; } = string.Empty;
    public List<ModalButton> FooterButtons { get; set; } = new();
    public bool ShowCloseButton { get; set; } = true;
    public string CloseButtonText { get; set; } = "Close";
    public string CloseButtonClass { get; set; } = "btn-secondary";
    public Dictionary<string, object> AdditionalAttributes { get; set; } = new();
}

public class ModalButton
{
    public string Text { get; set; } = string.Empty;
    public string Class { get; set; } = "btn-primary";
    public string Type { get; set; } = "button";
    public string OnClick { get; set; } = string.Empty; // JavaScript function
    public bool Dismiss { get; set; } = false;
    public string Icon { get; set; } = string.Empty;
    public Dictionary<string, object> AdditionalAttributes { get; set; } = new();
}
