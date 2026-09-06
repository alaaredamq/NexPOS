namespace Core.IRepositories.PrinterTransport;

public interface IPrinterTransport
{
    bool IsAvailable(string printerName);
    bool SendBytes(string printerName, byte[] data, string jobName);
    bool SendString(string printerName, string content, string jobName);

    /// <summary>
    /// Lists the printers installed on this machine, so a client can pick their receipt
    /// printer from a dropdown in Settings instead of typing the exact Windows printer name.
    /// </summary>
    List<string> GetAvailablePrinters();
}