using System.Text;
using Application.IServices.Settings;
using Application.Services.Sales;
using Core.IRepositories.PrinterTransport;
using Core.IRepositories.Sales;
using Core.Printing;
using Infrastructure.Repositories.PrinterTransport;
using Moq;
using Xunit;

namespace Tests.Services;

/// <summary>
/// A 2-in-1 printer (Xprinter XP-365B) is set to receipt or label mode on the printer and silently ignores the other language. With its
/// mode recorded, receipts and labels both go out in the language it is listening for.
/// </summary>
public class PrinterModeTests
{
    private static string Bars => string.Concat(Enumerable.Repeat("1101001110", 12));
    private static PrintDocument Receipt => new("Receipt-1", [PrintLine.Header("SHOP"), PrintLine.Row("Total:", "$5.00", bold: true)]);

    [Fact]
    public void A_receipt_for_a_printer_in_label_mode_is_one_TSPL_picture_on_continuous_paper()
    {
        var job = new EscPosBuilder().BuildLabelModeJob(Receipt);
        var head = Encoding.ASCII.GetString(job, 0, 200);

        Assert.StartsWith("SIZE 72 mm,", head);          // 576 dots across, the receipt width
        Assert.Contains("GAP 0 mm,0 mm\r\n", head);      // continuous: no label gaps
        Assert.Contains("BITMAP 0,0,72,", head);
        Assert.EndsWith("PRINT 1,1\r\n", Encoding.ASCII.GetString(job, job.Length - 11, 11));
        Assert.NotEqual(0x1B, job[0]);                    // no receipt-printer command for the printer to ignore
    }

    [Fact]
    public void Labels_for_a_printer_in_receipt_mode_are_ESC_POS_pictures_from_the_left_one_sticker_apart()
    {
        var label = StickerLabel.TryCreate("Cable", "2261013123452", "$4.00", Bars, 40, 25, 3, true)!;

        var job = new TsplLabelBuilder().BuildForReceiptMode(label);

        Assert.Equal(new byte[] { 0x1B, 0x40, 0x1B, (byte)'a', 0x00 }, job[..5]); // initialise, left edge (not centred on an 80 mm head)
        Assert.Equal(3, Count(job, [0x1D, (byte)'v', (byte)'0']));               // one picture per copy (each fits one band)
        Assert.Equal(3, Count(job, [0x1B, (byte)'J', 16]));                       // then exactly the 2 mm gap, so copies stay on their stickers
        Assert.DoesNotContain("SIZE", Encoding.ASCII.GetString(job));
        Assert.Equal(0, Count(job, [0x1D, (byte)'V']));                           // no cut in the middle of a sticker roll
    }

    [Fact]
    public void Modes_are_kept_per_printer_name_and_a_damaged_value_means_none()
    {
        var modes = PrinterModes.Parse(PrinterModes.Write(new Dictionary<string, PrinterMode> { ["Xprinter XP-365B (Copy 1)"] = PrinterMode.Label }));

        Assert.Equal(PrinterMode.Label, modes["xprinter xp-365b (copy 1)"]);
        Assert.Empty(PrinterModes.Parse("{not json"));
        Assert.Empty(PrinterModes.Parse("{\"P\":\"fax\"}"));
    }

    [Theory]
    [InlineData(null, (byte)0x1B)]             // not set: receipt-printer commands, as before
    [InlineData("receipt", (byte)0x1B)]
    [InlineData("label", (byte)'S')]           // "SIZE ...": label-printer commands
    public async Task Receipts_go_out_in_the_language_of_the_printers_mode(string? mode, byte first)
    {
        byte[]? sent = null;
        var transport = new Mock<IPrinterTransport>();
        transport.Setup(t => t.SendBytes(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Callback<string, byte[], string, bool>((_, bytes, _, _) => sent = bytes).Returns(true);
        var settings = new Mock<ISystemSettingsService>();
        settings.Setup(s => s.GetSettingValueAsync(PrinterModes.SettingKey, It.IsAny<string>()))
            .ReturnsAsync(mode is null ? "" : $"{{\"XP\":\"{mode}\"}}");
        var service = new ThermalPrinterService(transport.Object, new EscPosBuilder(), Mock.Of<Application.IServices.Settings.IStoreSettingsService>(),
            settings.Object, Mock.Of<IReceiptRepository>());

        Assert.True(await service.PrintDocumentAsync(Receipt, "XP"));

        Assert.NotNull(sent);
        Assert.Equal(first, sent![0]);
    }

    private static int Count(byte[] data, byte[] pattern)
    {
        var n = 0;
        for (var i = 0; i <= data.Length - pattern.Length; i++)
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern)) n++;
        return n;
    }
}
