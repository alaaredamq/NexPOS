using Application.DTOs.Products;
using Application.IServices.Base;

namespace Application.IServices.Products;

/// <summary>
/// Moves the product catalog in and out as an Excel workbook: a Products sheet, a Custom Prices sheet
/// (one row per extra price, so a product can have several) and dropdowns for category, supplier, unit
/// and active. Products are matched by barcode: a row whose barcode exists updates that product, any
/// other row creates a new one. A plain CSV of the product columns is still accepted on import.
/// </summary>
public interface IProductImportExportService : IBaseService
{
    const string FileContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Every product and its extra prices, in the same layout the import reads.</summary>
    Task<byte[]> ExportAsync();

    /// <summary>An empty workbook with the dropdowns filled from the current categories and suppliers, plus example rows.</summary>
    Task<byte[]> BuildTemplateAsync();

    /// <summary>
    /// Reads a workbook (or CSV). With <paramref name="apply"/> false nothing is saved: the result only says
    /// what an import would do. With true, every valid row is saved and invalid rows are skipped.
    /// A product listed on the Custom Prices sheet gets exactly those extra prices; others keep theirs.
    /// </summary>
    Task<ProductImportResult> ImportAsync(Stream file, bool apply);
}
