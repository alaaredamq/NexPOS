using Application.DTOs.Products;
using Application.IServices.Base;

namespace Application.IServices.Products;

/// <summary>
/// Moves the product catalog in and out as CSV (UTF-8 with BOM, so Excel opens Arabic text correctly).
/// Products are matched by barcode: a row whose barcode exists updates that product, any other row
/// creates a new one.
/// </summary>
public interface IProductImportExportService : IBaseService
{
    /// <summary>Every product, in the same column layout the import reads.</summary>
    Task<byte[]> ExportAsync();

    /// <summary>The column headers plus one example row.</summary>
    byte[] BuildTemplate();

    /// <summary>
    /// Reads a CSV file. With <paramref name="apply"/> false nothing is saved: the result only says
    /// what an import would do. With true, every valid row is saved in one transaction and invalid rows are skipped.
    /// </summary>
    Task<ProductImportResult> ImportAsync(Stream csv, bool apply);
}
