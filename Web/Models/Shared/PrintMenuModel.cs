namespace Web.Models.Shared;

/// <summary>
/// The two ways to get a document out of the app: onto the receipt printer or as a PDF file.
/// Rendered by <c>Shared/_PrintMenu</c>.
/// </summary>
public record PrintMenuModel(string PrintUrl, string PdfUrl, string ButtonClass = "btn btn-outline-primary", string? Label = null);
