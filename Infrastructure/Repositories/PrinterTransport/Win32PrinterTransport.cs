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

            // Windows keeps an unplugged USB printer's queue installed, so the open above and the
            // spooling below both "succeed" and the receipt would just sit in the queue. Ask the
            // spooler whether the device is really there, and report a failure if it is not.
            if (IsOffline(hPrinter, out var reason))
            {
                _logger.LogWarning("Printer '{Printer}' is offline ({Reason}); job '{Job}' was not sent.", printerName, reason, jobName);
                Console.WriteLine($"[PRINTER TRANSPORT] ❌ Printer '{printerName}' is offline ({reason}) - nothing sent");
                return false;
            }

            var doc = new DOCINFOA { pDocName = jobName };

            Console.WriteLine($"[PRINTER TRANSPORT] Starting document...");
            var jobId = StartDocPrinter(hPrinter, 1, doc);
            if (jobId <= 0)
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

            // Spooling the job only means Windows accepted it. Follow it until the spooler has handed it
            // to the device; a job that stays stuck (printer unplugged, powered off) is cancelled so it
            // cannot come out later as a duplicate once the cashier retries.
            if (success && !WaitForJobToLeave(hPrinter, jobId, out var stuckReason))
            {
                _logger.LogWarning("Print job '{Job}' to '{Printer}' did not reach the device ({Reason}); cancelled.", jobName, printerName, stuckReason);
                Console.WriteLine($"[PRINTER TRANSPORT] ❌ Job did not reach the printer ({stuckReason}) - cancelled");
                SetJob(hPrinter, jobId, 0, IntPtr.Zero, JOB_CONTROL_DELETE);
                success = false;
            }

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

    /// <summary>
    /// True when the spooler says the device cannot take a job: switched to "Use Printer Offline",
    /// unplugged (offline / not available / port unavailable) or in an error state. If the status
    /// cannot be read, the printer is given the benefit of the doubt.
    /// </summary>
    private static bool IsOffline(IntPtr hPrinter, out string reason)
    {
        reason = string.Empty;

        GetPrinter(hPrinter, 2, IntPtr.Zero, 0, out var needed);
        if (needed <= 0) return false;

        var buffer = Marshal.AllocCoTaskMem(needed);
        try
        {
            if (!GetPrinter(hPrinter, 2, buffer, needed, out _)) return false;

            var info = Marshal.PtrToStructure<PRINTER_INFO_2>(buffer);
            if ((info.Attributes & PRINTER_ATTRIBUTE_WORK_OFFLINE) != 0) reason = "set to work offline";
            else if ((info.Status & PRINTER_STATUS_OFFLINE) != 0) reason = "status offline";
            else if ((info.Status & PRINTER_STATUS_NOT_AVAILABLE) != 0) reason = "status not available";
            else if ((info.Status & PRINTER_STATUS_PORT_UNAVAILABLE) != 0) reason = "port unavailable";
            else if ((info.Status & PRINTER_STATUS_ERROR) != 0) reason = "status error";

            return reason.Length > 0;
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    /// <summary>
    /// Waits for the spooler to finish with a job. A job that is gone from the queue (or marked
    /// printed/complete) went to the device; one that reports an error or is still queued after
    /// <see cref="JobWait"/> did not, which is what an unplugged printer looks like.
    /// </summary>
    private static bool WaitForJobToLeave(IntPtr hPrinter, int jobId, out string reason)
    {
        reason = "still queued";
        var deadline = DateTime.UtcNow + JobWait;

        while (true)
        {
            GetJob(hPrinter, jobId, 1, IntPtr.Zero, 0, out var needed);
            if (needed <= 0) return true; // no longer in the queue

            var buffer = Marshal.AllocCoTaskMem(needed);
            try
            {
                if (!GetJob(hPrinter, jobId, 1, buffer, needed, out _)) return true;

                var status = Marshal.PtrToStructure<JOB_INFO_1>(buffer).Status;
                if ((status & (JOB_STATUS_PRINTED | JOB_STATUS_COMPLETE)) != 0) return true;
                if ((status & JOB_STATUS_OFFLINE) != 0) { reason = "printer offline"; return false; }
                if ((status & JOB_STATUS_PAPEROUT) != 0) { reason = "paper out"; return false; }
                if ((status & JOB_STATUS_ERROR) != 0) { reason = "printer error"; return false; }
                if ((status & JOB_STATUS_USER_INTERVENTION) != 0) { reason = "printer needs attention"; return false; }
            }
            finally
            {
                Marshal.FreeCoTaskMem(buffer);
            }

            // The queue's own status can flip to offline a moment after the job was spooled: fail as soon as it does.
            if (IsOffline(hPrinter, out var printerReason)) { reason = printerReason; return false; }

            if (DateTime.UtcNow >= deadline) return false;
            Thread.Sleep(50);
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
    private static extern bool GetPrinter(IntPtr hPrinter, int level, IntPtr pPrinter, int cbBuf, out int pcbNeeded);

    private const uint PRINTER_ATTRIBUTE_WORK_OFFLINE = 0x00000400;
    private const uint PRINTER_STATUS_ERROR = 0x00000002;
    private const uint PRINTER_STATUS_OFFLINE = 0x00000080;
    private const uint PRINTER_STATUS_NOT_AVAILABLE = 0x00001000;
    private const uint PRINTER_STATUS_PORT_UNAVAILABLE = 0x00200000;

    [StructLayout(LayoutKind.Sequential)]
    private struct PRINTER_INFO_2
    {
        public IntPtr pServerName, pPrinterName, pShareName, pPortName, pDriverName, pComment, pLocation;
        public IntPtr pDevMode, pSepFile, pPrintProcessor, pDatatype, pParameters, pSecurityDescriptor;
        public uint Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, cJobs, AveragePPM;
    }

    /// <summary>Returns the spooler job id, or 0 when the document could not be started.</summary>
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern int StartDocPrinter(IntPtr hPrinter, int level, [In] DOCINFOA di);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool GetJob(IntPtr hPrinter, int jobId, int level, IntPtr pJob, int cbBuf, out int pcbNeeded);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool SetJob(IntPtr hPrinter, int jobId, int level, IntPtr pJob, int command);

    private const int JOB_CONTROL_DELETE = 5;
    private const uint JOB_STATUS_ERROR = 0x00000002;
    private const uint JOB_STATUS_OFFLINE = 0x00000020;
    private const uint JOB_STATUS_PAPEROUT = 0x00000040;
    private const uint JOB_STATUS_PRINTED = 0x00000080;
    private const uint JOB_STATUS_USER_INTERVENTION = 0x00000400;
    private const uint JOB_STATUS_COMPLETE = 0x00001000;

    /// <summary>How long a receipt may stay in the spooler before it counts as not delivered (a receipt normally leaves within a second).</summary>
    private static readonly TimeSpan JobWait = TimeSpan.FromSeconds(3);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOB_INFO_1
    {
        public uint JobId;
        public IntPtr pPrinterName, pMachineName, pUserName, pDocument, pDatatype, pStatus;
        public uint Status, Priority, Position, TotalPages, PagesPrinted;
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

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