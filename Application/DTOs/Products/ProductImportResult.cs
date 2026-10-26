namespace Application.DTOs.Products;

/// <summary>One problem found in an import file; <see cref="Row"/> is the row number as a spreadsheet shows it (header = 1).</summary>
public record ProductImportIssue(int Row, string Message);

public class ProductImportResult
{
    /// <summary>False when the file was only checked ("preview") and nothing was saved.</summary>
    public bool Applied { get; set; }

    /// <summary>Set when the file as a whole is unusable (no header, wrong encoding, too big...).</summary>
    public string? FileError { get; set; }

    public int TotalRows { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }

    /// <summary>Categories named in the file that don't exist yet and are (or would be) created.</summary>
    public int NewCategories { get; set; }

    /// <summary>Rows that were skipped because of an error.</summary>
    public int Failed { get; set; }

    /// <summary>The first problems found, capped so a bad file can't produce a huge response.</summary>
    public List<ProductImportIssue> Issues { get; set; } = new();

    public bool Success => FileError == null;
}
