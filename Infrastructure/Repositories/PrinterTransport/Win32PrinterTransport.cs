using System.Runtime.InteropServices;
using System.Text;
using Core.IRepositories.PrinterTransport;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Repositories.PrinterTransport;

public class Win32PrinterTransport : IPrinterTransport
{
    private readonly ILogger<Win32PrinterTransport> _logger;

    public Win32PrinterTransport(ILogger<Win32PrinterTransport> logger)
    {
        _logger = logger;
    }

    public bool IsAvailable(string printerName)
    {
        try
        {
            Console.WriteLine($"[PRINTER TRANSPORT] Checking if printer '{printerName}' is available...");
            
            var result = OpenPrinter(printerName, out var hPrinter, IntPtr.Zero);
            if (result)
            {
                ClosePrinter(hPrinter);
                Console.WriteLine($"[PRINTER TRANSPORT] ✅ Printer '{printerName}' is available");
                return true;
            }
            else
            {
                var error = Marshal.GetLastWin32Error();
                Console.WriteLine($"[PRINTER TRANSPORT] ❌ Printer '{printerName}' not available. Win32 Error: {error}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PRINTER TRANSPORT] ❌ Exception checking printer '{printerName}': {ex.Message}");
            return false;
        }
    }

    public bool SendBytes(string printerName, byte[] data, string jobName)
    {
        return RawPrint(printerName, data, jobName);
    }

    public bool SendString(string printerName, string content, string jobName)
    {
        var bytes = Encoding.GetEncoding(437).GetBytes(content);
        return RawPrint(printerName, bytes, jobName);
    }

    public List<string> GetAvailablePrinters()
    {
        try
        {
            return System.Drawing.Printing.PrinterSettings.InstalledPrinters
                .Cast<string>()
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate installed printers");
            return new List<string>();
        }
    }

    private bool RawPrint(string printerName, byte[] data, string jobName)
    {
        IntPtr hPrinter = IntPtr.Zero;
        IntPtr pBytes = IntPtr.Zero;

        try
        {
            Console.WriteLine($"[PRINTER TRANSPORT] Attempting to print {data.Length} bytes to '{printerName}'");
            Console.WriteLine($"[PRINTER TRANSPORT] Job name: '{jobName}'");
            
            pBytes = Marshal.AllocCoTaskMem(data.Length);
            Marshal.Copy(data, 0, pBytes, data.Length);

            Console.WriteLine($"[PRINTER TRANSPORT] Opening printer '{printerName}'...");
            if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                Console.WriteLine($"[PRINTER TRANSPORT] ❌ Failed to open printer '{printerName}'. Win32 Error: {error}");
                
                // List available printers for debugging
                ListAvailablePrinters();
                return false;
            }

            Console.WriteLine($"[PRINTER TRANSPORT] ✅ Printer opened successfully");

            var doc = new DOCINFOA { pDocName = jobName };

            Console.WriteLine($"[PRINTER TRANSPORT] Starting document...");
            if (!StartDocPrinter(hPrinter, 1, doc))
            {
                var error = Marshal.GetLastWin32Error();
                Console.WriteLine($"[PRINTER TRANSPORT] ❌ Failed to start document. Win32 Error: {error}");
                return false;
            }

            Console.WriteLine($"[PRINTER TRANSPORT] Starting page...");
            if (!StartPagePrinter(hPrinter))
            {
                var error = Marshal.GetLastWin32Error();
                Console.WriteLine($"[PRINTER TRANSPORT] ❌ Failed to start page. Win32 Error: {error}");
                return false;
            }

            Console.WriteLine($"[PRINTER TRANSPORT] Writing {data.Length} bytes...");
            bool ok = WritePrinter(hPrinter, pBytes, data.Length, out int written);

            Console.WriteLine($"[PRINTER TRANSPORT] Write result: {ok}, Bytes written: {written}/{data.Length}");

            EndPagePrinter(hPrinter);
            EndDocPrinter(hPrinter);

            var success = ok && written == data.Length;
            Console.WriteLine($"[PRINTER TRANSPORT] Print job result: {(success ? "✅ SUCCESS" : "❌ FAILED")}");
            
            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PRINTER TRANSPORT] ❌ Exception during print: {ex.Message}");
            Console.WriteLine($"[PRINTER TRANSPORT] Stack trace: {ex.StackTrace}");
            return false;
        }
        finally
        {
            if (pBytes != IntPtr.Zero) Marshal.FreeCoTaskMem(pBytes);
            if (hPrinter != IntPtr.Zero) ClosePrinter(hPrinter);
        }
    }

    private void ListAvailablePrinters()
    {
        Console.WriteLine($"[PRINTER TRANSPORT] Available printers:");
        foreach (var printerName in GetAvailablePrinters())
        {
            Console.WriteLine($"[PRINTER TRANSPORT] - {printerName}");
        }
    }

    #region Win32

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv")]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DOCINFOA di);

    [DllImport("winspool.drv")]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv")]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv")]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv")]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    [StructLayout(LayoutKind.Sequential)]
    private class DOCINFOA
    {
        public string pDocName;
        public string pOutputFile;
        public string pDataType = "RAW";
    }

    #endregion
}