using Core.Entities.Configuration;
using Core.Entities.Sales;

namespace Core.IRepositories.PrinterTransport;

public interface IEscPosBuilder
{
    /// <summary>
    /// Builds a ready-to-send ESC/POS print job for the receipt. Uses fast plain-text mode
    /// (codepage 437) when the content is all Latin/numeric; automatically switches to
    /// rendering the receipt as an image (via SkiaSharp, with proper Arabic shaping/RTL) when
    /// any of the text - store name, address, footer, item names - contains Arabic, since
    /// codepage 437 has no Arabic code points at all.
    /// </summary>
    byte[] BuildReceipt(Receipt receipt, StoreSettings? storeSettings = null);
}
