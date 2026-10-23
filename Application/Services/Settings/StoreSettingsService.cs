using Application.DTOs.Settings;
using Application.IServices.Settings;
using Core.Entities.Configuration;
using Core.IRepositories.Configuration;

namespace Application.Services.Settings;

/// <summary>
/// Service for managing store settings.
/// Store settings contain information displayed on receipts like store name, address, etc.
/// </summary>
public class StoreSettingsService : IStoreSettingsService
{
    private readonly IStoreSettingsRepository _repository;

    public StoreSettingsService(IStoreSettingsRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Gets the current store settings
    /// </summary>
    public async Task<StoreSettingsDTO> GetStoreSettingsAsync()
    {
        var settings = await _repository.GetStoreSettingsAsync();
        return MapToDTO(settings);
    }

    /// <summary>
    /// Gets the raw store settings entity
    /// </summary>
    public async Task<StoreSettings> GetStoreSettingsEntityAsync()
    {
        return await _repository.GetStoreSettingsAsync();
    }

    /// <summary>
    /// Updates the store settings
    /// </summary>
    public async Task<StoreSettingsDTO> UpdateStoreSettingsAsync(UpdateStoreSettingsRequest request, int? userId = null)
    {
        var settings = await _repository.GetStoreSettingsAsync();
        
        // Update properties from request
        settings.StoreName = request.StoreName;
        settings.AddressLine1 = request.AddressLine1;
        settings.AddressLine2 = request.AddressLine2;
        settings.Phone = request.Phone;
        settings.TaxId = request.TaxId;
        settings.Website = request.Website;
        settings.ReceiptFooterMessage = request.ReceiptFooterMessage;
        settings.FooterLine1 = request.FooterLine1;
        settings.FooterLine2 = request.FooterLine2;
        settings.FooterLine3 = request.FooterLine3;
        // Only the fields printed on receipts are editable. The remaining columns (email, currency
        // symbol, language, logo, return policy, tax/barcode toggles, social, registration number)
        // aren't used anywhere, so a save leaves them untouched.
        settings.LastModifiedByUserId = userId;

        var updated = await _repository.UpdateStoreSettingsAsync(settings);
        return MapToDTO(updated);
    }

    /// <summary>
    /// Resets store settings to defaults
    /// </summary>
    public async Task<StoreSettingsDTO> ResetToDefaultsAsync()
    {
        var settings = await _repository.GetStoreSettingsAsync();
        var defaults = StoreSettings.CreateDefault();
        
        // Copy default values but keep the ID
        settings.StoreName = defaults.StoreName;
        settings.AddressLine1 = defaults.AddressLine1;
        settings.AddressLine2 = defaults.AddressLine2;
        settings.Phone = defaults.Phone;
        settings.TaxId = defaults.TaxId;
        settings.Email = defaults.Email;
        settings.Website = defaults.Website;
        settings.ReceiptFooterMessage = defaults.ReceiptFooterMessage;
        settings.FooterLine1 = defaults.FooterLine1;
        settings.FooterLine2 = defaults.FooterLine2;
        settings.FooterLine3 = defaults.FooterLine3;
        settings.CurrencySymbol = defaults.CurrencySymbol;
        settings.DefaultLanguage = defaults.DefaultLanguage;
        settings.PrintLogo = defaults.PrintLogo;
        settings.LogoPath = defaults.LogoPath;
        settings.ReturnPolicyDays = defaults.ReturnPolicyDays;
        settings.ShowTaxBreakdown = defaults.ShowTaxBreakdown;
        settings.PrintBarcode = defaults.PrintBarcode;
        settings.SocialMedia = defaults.SocialMedia;
        settings.BusinessRegistrationNumber = defaults.BusinessRegistrationNumber;
        
        var updated = await _repository.UpdateStoreSettingsAsync(settings);
        return MapToDTO(updated);
    }

    /// <summary>
    /// Validates store settings
    /// </summary>
    public Task<List<string>> ValidateSettingsAsync(UpdateStoreSettingsRequest request)
    {
        var errors = new List<string>();
        
        if (string.IsNullOrWhiteSpace(request.StoreName))
        {
            errors.Add("Store name is required");
        }
        else if (request.StoreName.Length > 200)
        {
            errors.Add("Store name must be 200 characters or less");
        }
        
        if (request.AddressLine1?.Length > 200)
        {
            errors.Add("Address line 1 must be 200 characters or less");
        }
        
        if (request.AddressLine2?.Length > 200)
        {
            errors.Add("Address line 2 must be 200 characters or less");
        }
        
        if (request.Phone?.Length > 50)
        {
            errors.Add("Phone must be 50 characters or less");
        }
        
        if (request.TaxId?.Length > 50)
        {
            errors.Add("Tax ID must be 50 characters or less");
        }
        
        if (request.Email?.Length > 200)
        {
            errors.Add("Email must be 200 characters or less");
        }
        
        if (!string.IsNullOrEmpty(request.Email) && !IsValidEmail(request.Email))
        {
            errors.Add("Invalid email format");
        }
        
        if (request.Website?.Length > 200)
        {
            errors.Add("Website must be 200 characters or less");
        }
        
        if (request.ReturnPolicyDays < 0)
        {
            errors.Add("Return policy days cannot be negative");
        }
        
        return Task.FromResult(errors);
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    private static StoreSettingsDTO MapToDTO(StoreSettings settings)
    {
        return new StoreSettingsDTO
        {
            Id = settings.Id,
            StoreName = settings.StoreName,
            AddressLine1 = settings.AddressLine1,
            AddressLine2 = settings.AddressLine2,
            Phone = settings.Phone,
            TaxId = settings.TaxId,
            Email = settings.Email,
            Website = settings.Website,
            ReceiptFooterMessage = settings.ReceiptFooterMessage,
            FooterLine1 = settings.FooterLine1,
            FooterLine2 = settings.FooterLine2,
            FooterLine3 = settings.FooterLine3,
            CurrencySymbol = settings.CurrencySymbol,
            DefaultLanguage = settings.DefaultLanguage,
            PrintLogo = settings.PrintLogo,
            LogoPath = settings.LogoPath,
            ReturnPolicyDays = settings.ReturnPolicyDays,
            ShowTaxBreakdown = settings.ShowTaxBreakdown,
            PrintBarcode = settings.PrintBarcode,
            SocialMedia = settings.SocialMedia,
            BusinessRegistrationNumber = settings.BusinessRegistrationNumber
        };
    }
}
