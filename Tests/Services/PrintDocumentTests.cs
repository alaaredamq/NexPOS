using System.Text;
using Application.Services.Printing;
using Application.IServices.Sales;
using Core.Printing;
using Infrastructure.Repositories.PrinterTransport;
using Xunit;

namespace Tests.Services;

/// <summary>
/// One document must give the same content as a receipt-printer job and as a PDF, and a
/// long report must neither overflow the printer buffer nor produce an unopenable page.
/// </summary>
public class PrintDocumentTests
{
    private readonly EscPosBuilder _builder = new();

    private static PrintDocument Doc(params PrintLine[] lines) => new("Test Doc", lines);

    private static int CountPdfPages(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        // "/Type /Page" but not "/Type /Pages"
        return System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page(?!s)").Count;
    }

    [Fact]
    public void BuildPdf_ProducesAPdfFile()
    {
        var pdf = _builder.BuildPdf(Doc(PrintLine.Header("SHOP"), PrintLine.Row("Total:", "$5.00", bold: true)));

        Assert.True(pdf.Length > 200);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal(1, CountPdfPages(pdf));
    }

    [Fact]
    public void BuildPdf_HandlesArabicText()
    {
        var pdf = _builder.BuildPdf(Doc(PrintLine.Header("متجر النور"), PrintLine.Row("الإجمالي:", "10.00 ل.ل")));

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void BuildPdf_A4_PaginatesLongReports()
    {
        var lines = Enumerable.Range(1, 200).Select(i => PrintLine.Row($"Item {i}", "$1.00")).ToArray();

        var a4 = _builder.BuildPdf(Doc(lines), PdfPaper.A4);
        var receipt = _builder.BuildPdf(Doc(lines), PdfPaper.Receipt);

        Assert.True(CountPdfPages(a4) > 1, "200 rows can't fit on one A4 page");
        Assert.Equal(1, CountPdfPages(receipt));
    }

    [Fact]
    public void BuildPrintJob_PlainText_StaysTextAndEndsWithCut()
    {
        var job = _builder.BuildPrintJob(Doc(PrintLine.Row("Total:", "$5.00")));

        Assert.Contains("Total:", Encoding.GetEncoding(437).GetString(job));
        Assert.Equal(new byte[] { 0x1D, (byte)'V', 0x01 }, job[^3..]);
        // text mode never sends the raster-image command
        Assert.DoesNotContain("\x1Dv0", Encoding.GetEncoding(437).GetString(job));
    }

    [Fact]
    public void BuildPrintJob_Arabic_IsSentAsRasterImage()
    {
        var job = _builder.BuildPrintJob(Doc(PrintLine.Plain("منتج")));

        Assert.Contains("\x1Dv0", Encoding.Latin1.GetString(job));
        Assert.Equal(new byte[] { 0x1D, (byte)'V', 0x01 }, job[^3..]);
    }

    [Fact]
    public void BuildPrintJob_LongArabicReport_IsSplitIntoRasterBands()
    {
        var lines = Enumerable.Range(1, 400).Select(i => PrintLine.Plain($"منتج {i}")).ToArray();

        var text = Encoding.Latin1.GetString(_builder.BuildPrintJob(Doc(lines)));
        var commands = System.Text.RegularExpressions.Regex.Matches(text, "\x1Dv0").Count;

        Assert.True(commands > 1, "a report this tall must not go out as a single raster command");
    }

    [Fact]
    public void ZReport_DocumentCoversEverySection()
    {
        var report = new ZReport
        {
            SessionId = 7, CashierName = "Sam", OpenedAt = new DateTime(2026, 10, 1, 8, 0, 0), ClosedAt = new DateTime(2026, 10, 1, 17, 0, 0),
            TotalTransactions = 2, TotalSales = 30m, NetSales = 28m, TotalDiscounts = 2m, CashSales = 20m, CardSales = 8m,
            OpeningCash = 100m, ExpectedCash = 120m, ActualCash = 118m, CashDifference = -2m, CashDropCount = 0,
            Orders = { new ZReportOrder { OrderNumber = "ORD-1", OrderDate = new DateTime(2026, 10, 1, 9, 0, 0), TotalAmount = 10m, PaymentMethod = "Cash", CustomerName = "Walk-in" } },
        };

        var doc = ReportDocuments.ZReport(report);
        var text = string.Join("\n", doc.Lines.Select(l => $"{l.Text} {l.Right}"));

        Assert.Equal("Z-Report-Session-7", doc.FileName);
        Assert.Contains("SALES SUMMARY", text);
        Assert.Contains("CASH RECONCILIATION", text);
        Assert.Contains("TRANSACTIONS", text);
        Assert.Contains("SHORTAGE:", text);
    }

    [Fact]
    public void FileName_StripsCharactersWindowsRejects()
    {
        Assert.Equal("Receipt-A-B", new PrintDocument("Receipt-A/B", Array.Empty<PrintLine>()).FileName.Replace('-', '-'));
        Assert.DoesNotContain('/', new PrintDocument("a/b:c", Array.Empty<PrintLine>()).FileName);
    }
}

public class LabelPdfTests
{
    private static string Bars => string.Concat(Enumerable.Repeat("1101001110", 12));

    [Fact]
    public void Build_MakesOnePagePerCopy()
    {
        var label = StickerLabel.TryCreate("Cable", "2261013123452", "$4.00", Bars, 40, 25, 3, true)!;

        var pdf = new LabelPdfBuilder().Build(label);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        var pages = System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(pdf), @"/Type\s*/Page(?!s)").Count;
        Assert.Equal(3, pages);
    }

    [Fact]
    public void Build_HandlesArabicNamesAndNoPrice()
    {
        var label = StickerLabel.TryCreate("حليب كامل الدسم من المزرعة الطازجة", "2261013123452", "", Bars, 40, 25, 1, true)!;

        var pdf = new LabelPdfBuilder().Build(label);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Theory]
    [InlineData("", "10101")]            // no code
    [InlineData("123", "")]              // no bars
    [InlineData("123", "10x01")]         // not a bar pattern
    [InlineData("123", "0000")]          // nothing to draw
    public void TryCreate_RejectsUnusableInput(string code, string bars)
    {
        Assert.Null(StickerLabel.TryCreate("n", code, "", bars, 40, 25, 1, true));
    }

    [Fact]
    public void TryCreate_ClampsSizeAndCopies()
    {
        var label = StickerLabel.TryCreate("n", "123", "", "1010", 5000, -3, 100000, true)!;

        Assert.Equal(150, label.WidthMm);
        Assert.Equal(10, label.HeightMm);
        Assert.Equal(StickerLabel.MaxCopies, label.Copies);
    }
}
