using Core.Printing;

namespace Core.IRepositories.PrinterTransport;

public interface ITsplLabelBuilder
{
    /// <summary>
    /// The sticker as TSPL commands (what TSC-compatible label printers such as Xprinter speak), sent to the printer as raw
    /// data. The label size is part of the commands, so the printer cuts to it whatever paper size its Windows driver has.
    /// </summary>
    byte[] Build(StickerLabel label);
}
