using Core.Printing;

namespace Core.IRepositories.PrinterTransport;

public interface ILabelPdfBuilder
{
    /// <summary>The labels as a PDF: one page per copy, sized to the sticker.</summary>
    byte[] Build(StickerLabel label);
}
