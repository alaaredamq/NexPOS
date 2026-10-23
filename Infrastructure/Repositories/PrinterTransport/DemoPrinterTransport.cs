using Core.IRepositories.PrinterTransport;

namespace Infrastructure.Repositories.PrinterTransport;

/// <summary>
/// Stand-in printer for demos and recordings (enabled with NEXPOS_DEMO=1). Reports a
/// receipt printer as available and swallows every job, so running the app for a demo can
/// never send output to a real printer attached to the machine.
/// </summary>
public class DemoPrinterTransport : IPrinterTransport
{
    private const string DemoPrinterName = "Demo Receipt Printer";

    public bool IsAvailable(string printerName) => true;
    public bool SendBytes(string printerName, byte[] data, string jobName) => true;
    public bool SendString(string printerName, string content, string jobName) => true;
    public List<string> GetAvailablePrinters() => new() { DemoPrinterName };
}
