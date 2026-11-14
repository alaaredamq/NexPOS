namespace Core.IRepositories.PrinterTransport;

public interface IPrinterTransport
{
    bool IsAvailable(string printerName);

    /// <param name="quickFail">
    /// True for the automatic print at the end of a sale: when this printer failed to take a job a moment ago it is only
    /// given a short chance to take this one, so a printer that is unplugged does not hold up every sale. An explicit
    /// reprint passes false and always gets the full wait.
    /// </param>
    bool SendBytes(string printerName, byte[] data, string jobName, bool quickFail = false);
    bool SendString(string printerName, string content, string jobName);

    /// <summary>
    /// Lists the printers installed on this machine, so a client can pick their receipt
    /// printer from a dropdown in Settings instead of typing the exact Windows printer name.
    /// </summary>
    List<string> GetAvailablePrinters();
}