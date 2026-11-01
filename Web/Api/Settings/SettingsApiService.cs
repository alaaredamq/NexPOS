using Application.DTOs.Settings;
using Application.IServices.Sales;
using Application.IServices.Settings;
using Microsoft.Extensions.Localization;
using Web.Api.Settings.Contracts;
using Web.Helpers;
using Web.Services;

namespace Web.Api.Settings;

public interface ISettingsApiService
{
    Task<StoreSettingsDto> GetStoreAsync();

    Task<StoreSettingsDto> SaveStoreAsync(StoreSettingsRequest request, int? userId);

    Task<StoreSettingsDto> ResetStoreAsync();

    Task<PrintingSettingsDto> GetPrintingAsync();

    Task<PrintingSettingsDto> SavePrintingAsync(PrintingSettingsRequest request);

    IReadOnlyList<string> InstalledPrinters();

    Task<PrinterTestResultDto> TestPrinterAsync(string printerName);

    BrandingSettingsDto GetBranding();

    /// <summary>Throws <see cref="SettingsRuleException"/> for a name that is too long, a file of a type that is not allowed, or one that is too big.</summary>
    BrandingSettingsDto SaveBranding(BrandingForm form);
}

/// <summary>A field of the request breaks a rule the DTO attributes cannot judge; <see cref="Field"/> is the request property.</summary>
public sealed class SettingsRuleException : Exception
{
    public string Field { get; }

    public SettingsRuleException(string field, string message) : base(message) => Field = field;
}

/// <summary>
/// Maps the store, system-settings, printer and branding services to the Settings API's DTOs. The rules stay where they were: a store save only
/// touches the receipt fields, a printing save only the three settings the app reads, branding is plain files in the app-data folder.
/// </summary>
public sealed class SettingsApiService : ISettingsApiService
{
    private readonly IStoreSettingsService _store;
    private readonly ISystemSettingsService _system;
    private readonly IThermalPrinterService _printers;
    private readonly IBrandingService _branding;
    private readonly IStringLocalizer<SharedResource> _text;

    public SettingsApiService(
        IStoreSettingsService store,
        ISystemSettingsService system,
        IThermalPrinterService printers,
        IBrandingService branding,
        IStringLocalizer<SharedResource> text)
    {
        _store = store;
        _system = system;
        _printers = printers;
        _branding = branding;
        _text = text;
    }

    // ---- store & receipts ----------------------------------------------------------------------------------------

    public async Task<StoreSettingsDto> GetStoreAsync() => ToDto(await _store.GetStoreSettingsAsync());

    public async Task<StoreSettingsDto> SaveStoreAsync(StoreSettingsRequest request, int? userId)
    {
        var storeName = request.StoreName.Trim();
        if (storeName.Length == 0) throw new SettingsRuleException(nameof(StoreSettingsRequest.StoreName), _text["Set_StoreNameRequired"]);

        // The other columns of UpdateStoreSettingsRequest are ignored by the service: it copies the receipt fields only.
        var saved = await _store.UpdateStoreSettingsAsync(new UpdateStoreSettingsRequest
        {
            StoreName = storeName,
            AddressLine1 = Clean(request.AddressLine1),
            AddressLine2 = Clean(request.AddressLine2),
            Phone = Clean(request.Phone),
            TaxId = Clean(request.TaxId),
            Website = Clean(request.Website),
            ReceiptFooterMessage = Clean(request.ReceiptFooterMessage),
            FooterLine1 = Clean(request.FooterLine1),
            FooterLine2 = Clean(request.FooterLine2),
            FooterLine3 = Clean(request.FooterLine3),
        }, userId);
        return ToDto(saved);
    }

    public async Task<StoreSettingsDto> ResetStoreAsync() => ToDto(await _store.ResetToDefaultsAsync());

    // ---- printers & labels ---------------------------------------------------------------------------------------

    public async Task<PrintingSettingsDto> GetPrintingAsync() => ToDto(await _system.GetAppSettingsAsync());

    public async Task<PrintingSettingsDto> SavePrintingAsync(PrintingSettingsRequest request)
    {
        // UpdateAppSettingsAsync writes only ReceiptPrinterName and the two sticker sizes; start from what is stored so nothing else is touched.
        var current = await _system.GetAppSettingsAsync();
        current.ReceiptPrinterName = request.ReceiptPrinterName?.Trim() ?? string.Empty;
        current.StickerWidthMm = request.StickerWidthMm;
        current.StickerHeightMm = request.StickerHeightMm;
        return ToDto(await _system.UpdateAppSettingsAsync(current));
    }

    public IReadOnlyList<string> InstalledPrinters() => _printers.GetAvailablePrinters();

    public async Task<PrinterTestResultDto> TestPrinterAsync(string printerName)
    {
        printerName = printerName.Trim();
        bool printed;
        try
        {
            printed = await _printers.TestPrintAsync(printerName);
        }
        catch (Exception)
        {
            // A driver or spooler failure reads the same to the user as an unreachable printer.
            printed = false;
        }

        return new PrinterTestResultDto
        {
            Printed = printed,
            Message = printed ? _text["Set_TestSent"] : _text["Set_TestUnreachable", printerName],
        };
    }

    // ---- branding --------------------------------------------------------------------------------------------------

    public BrandingSettingsDto GetBranding() => ToDto(_branding);

    public BrandingSettingsDto SaveBranding(BrandingForm form)
    {
        var name = form.AppName?.Trim();
        if (name is { Length: > BrandingService.MaxNameLength })
            throw new SettingsRuleException(nameof(BrandingForm.AppName), _text["Set_NameTooLong", BrandingService.MaxNameLength]);

        // Check both files before anything is written, so a bad favicon does not leave a half-saved brand.
        CheckImage(form.Logo, nameof(BrandingForm.Logo), BrandingService.LogoExtensions);
        CheckImage(form.Favicon, nameof(BrandingForm.Favicon), BrandingService.FaviconExtensions);

        _branding.SetAppName(name);

        if (form.RemoveLogo) _branding.RemoveLogo();
        else if (form.Logo is { Length: > 0 } logo)
        {
            using var stream = logo.OpenReadStream();
            _branding.SaveLogo(stream, Path.GetExtension(logo.FileName));
        }

        if (form.RemoveFavicon) _branding.RemoveFavicon();
        else if (form.Favicon is { Length: > 0 } favicon)
        {
            using var stream = favicon.OpenReadStream();
            _branding.SaveFavicon(stream, Path.GetExtension(favicon.FileName));
        }

        return ToDto(_branding);
    }

    private void CheckImage(IFormFile? file, string field, string[] allowed)
    {
        if (file is not { Length: > 0 }) return;

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowed.Contains(extension))
            throw new SettingsRuleException(field, _text["Set_FileTypeNotAllowed", extension, string.Join(", ", allowed)]);
        if (file.Length > BrandingService.MaxFileBytes)
            throw new SettingsRuleException(field, _text["Set_FileTooLarge", BrandingService.MaxFileBytes / 1024 / 1024]);
    }

    // ---- mapping ---------------------------------------------------------------------------------------------------

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static StoreSettingsDto ToDto(StoreSettingsDTO s) => new()
    {
        StoreName = s.StoreName,
        AddressLine1 = s.AddressLine1,
        AddressLine2 = s.AddressLine2,
        Phone = s.Phone,
        TaxId = s.TaxId,
        Website = s.Website,
        ReceiptFooterMessage = s.ReceiptFooterMessage,
        FooterLine1 = s.FooterLine1,
        FooterLine2 = s.FooterLine2,
        FooterLine3 = s.FooterLine3,
    };

    private static PrintingSettingsDto ToDto(AppSettingsDTO s) => new()
    {
        ReceiptPrinterName = s.ReceiptPrinterName,
        StickerWidthMm = s.StickerWidthMm,
        StickerHeightMm = s.StickerHeightMm,
    };

    private static BrandingSettingsDto ToDto(IBrandingService b)
    {
        var version = Uri.EscapeDataString(b.Version);
        return new BrandingSettingsDto
        {
            AppName = b.HasCustomName ? b.AppName : string.Empty,
            DefaultAppName = b.DefaultAppName,
            HasCustomLogo = b.HasCustomLogo,
            HasCustomFavicon = b.HasCustomFavicon,
            LogoUrl = $"/branding/logo?v={version}",
            FaviconUrl = $"/branding/favicon?v={version}",
            MaxNameLength = BrandingService.MaxNameLength,
            MaxFileBytes = BrandingService.MaxFileBytes,
            LogoExtensions = BrandingService.LogoExtensions,
            FaviconExtensions = BrandingService.FaviconExtensions,
        };
    }
}
