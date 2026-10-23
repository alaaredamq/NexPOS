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
        if (IsDocumentPrinter(printerName))
        {
            // ESC/POS bytes mean nothing to a PDF/XPS driver - it would write a corrupt file.
            _logger.LogWarning("Printer '{Printer}' is a document printer and cannot print raw ESC/POS data (job '{Job}').", printerName, jobName);
            return false;
        }

        return RawPrint(printerName, data, jobName);
    }

    public bool SendString(string printerName, string content, string jobName)
    {
        if (IsDocumentPrinter(printerName))
            return GdiPrintText(printerName, StripEscPos(content), jobName);

        var bytes = Encoding.GetEncoding(437).GetBytes(content);
        return RawPrint(printerName, bytes, jobName);
    }

    /// <summary>
    /// "Print to PDF"-style virtual printers need normal Windows (GDI) print output;
    /// RAW data sent to them produces a file that cannot be opened.
    /// </summary>
    private static bool IsDocumentPrinter(string printerName)
    {
        var name = printerName.ToLowerInvariant();
        return name.Contains("pdf") || name.Contains("xps") || name.Contains("onenote")
            || name.Contains("fax") || name.Contains("document writer");
    }

    private static string StripEscPos(string content)
        => System.Text.RegularExpressions.Regex.Replace(content, @"\x1B@|\x1B[taEM!2-3dJ].|\x1D[V].", string.Empty);

    private bool GdiPrintText(string printerName, string text, string jobName)
    {
        try
        {
            var lines = text.Replace("\r", string.Empty).Split('\n');
            var index = 0;

            using var font = new System.Drawing.Font("Courier New", 10);
            using var doc = new System.Drawing.Printing.PrintDocument();
            doc.PrinterSettings.PrinterName = printerName;
            doc.DocumentName = jobName;
            doc.PrintPage += (_, e) =>
            {
                var g = e.Graphics!;
                var lineHeight = font.GetHeight(g);
                var y = (float)e.MarginBounds.Top;

                while (index < lines.Length && y + lineHeight <= e.MarginBounds.Bottom)
                {
                    g.DrawString(lines[index++], font, System.Drawing.Brushes.Black, e.MarginBounds.Left, y);
                    y += lineHeight;
                }

                e.HasMorePages = index < lines.Length;
            };
            doc.Print();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GDI print to '{Printer}' failed", printerName);
            return false;
        }
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