using Application.DTOs.Settings;
using Application.IServices.Settings;
using Core.Entities.Configuration;
using Core.IRepositories.Configuration;
using System.Text.Json;

namespace Application.Services.Settings
{
    public class SystemSettingsService : ISystemSettingsService
    {
        private readonly ISystemSettingsRepository _repository;

        public SystemSettingsService(ISystemSettingsRepository repository)
        {
            _repository = repository;
        }

        public async Task<IEnumerable<SystemSettings>> GetAllAsync()
        {
            return await _repository.GetAllAsync();
        }

        public async Task<SystemSettings> GetByIdAsync(int id)
        {
            return await _repository.GetByIdAsync(id);
        }

        public async Task<SystemSettings> GetByKeyAsync(string key)
        {
            return await _repository.GetByKeyAsync(key);
        }

        public async Task<SystemSettings> CreateAsync(SystemSettings entity)
        {
            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;
            return await _repository.SaveAsync(entity);
        }

        public async Task<SystemSettings> UpdateAsync(SystemSettings entity)
        {
            entity.UpdatedAt = DateTime.UtcNow;
            return await _repository.SaveAsync(entity);
        }

        public async Task DeleteAsync(int id)
        {
            await _repository.DeleteAsync(id);
        }

        public async Task DeleteByKeyAsync(string key)
        {
            await _repository.DeleteByKeyAsync(key);
        }

        public async Task UpdateMultipleAsync(IEnumerable<SystemSettings> settings)
        {
            await _repository.UpdateMultipleAsync(settings);
        }

        public async Task<BulkSettingUpdateRequest> UpdateBulkAsync(BulkSettingUpdateRequest request)
        {
            foreach (var settingUpdate in request.Settings)
            {
                var existing = await _repository.GetByKeyAsync(settingUpdate.SettingKey);
                if (existing.SettingKey == settingUpdate.SettingKey && !existing.IsEditable)
                {
                    continue;
                }

                await SetSettingValueAsync(settingUpdate.SettingKey, settingUpdate.SettingValue);
            }
            return request;
        }

        public async Task<IEnumerable<SettingCategoryDTO>> GetSettingsByCategoryAsync()
        {
            await InitializeDefaultSettingsAsync();
            var allSettings = await _repository.GetAllAsync();
            var categories = allSettings
                .GroupBy(s => s.Category)
                .Select(g => new SettingCategoryDTO
                {
                    CategoryName = g.Key,
                    DisplayName = GetCategoryDisplayName(g.Key),
                    Icon = GetCategoryIcon(g.Key),
                    Description = GetCategoryDescription(g.Key),
                    Settings = g.Select(s => new SystemSettingsDTO
                    {
                        Id = s.Id,
                        SettingKey = s.SettingKey,
                        SettingValue = s.SettingValue,
                        SettingType = s.SettingType,
                        Category = s.Category,
                        Description = s.Description,
                        IsEditable = s.IsEditable,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt ?? DateTime.UtcNow
                    }).ToList()
                })
                .OrderBy(c => c.CategoryName)
                .ToList();

            return categories;
        }

        public async Task<IEnumerable<SystemSettings>> GetSettingsByCategoryAsync(string category)
        {
            return await _repository.GetByCategoryAsync(category);
        }

        public async Task<AppSettingsDTO> GetAppSettingsAsync()
        {
            await InitializeDefaultSettingsAsync();
            var settings = await _repository.GetAllAsync();
            var appSettings = new AppSettingsDTO();

            // Map settings to AppSettingsDTO
            foreach (var setting in settings)
            {
                SetPropertyValue(appSettings, setting.SettingKey, setting.SettingValue, setting.SettingType);
            }

            return appSettings;
        }

        public async Task<AppSettingsDTO> UpdateAppSettingsAsync(AppSettingsDTO appSettings)
        {
            var properties = typeof(AppSettingsDTO).GetProperties();
            
            foreach (var property in properties)
            {
                var value = property.GetValue(appSettings);
                var stringValue = value is bool boolValue
                    ? boolValue.ToString().ToLowerInvariant()
                    : value?.ToString() ?? "";

                await SetSettingValueAsync(property.Name, stringValue);
            }

            return await GetAppSettingsAsync();
        }

        public async Task<string> GetSettingValueAsync(string key, string defaultValue = "")
        {
            var setting = await _repository.GetByKeyAsync(key);
            return setting.SettingKey == key ? setting.SettingValue : defaultValue;
        }

        public async Task<T> GetSettingValueAsync<T>(string key, T defaultValue = default!)
        {
            var stringValue = await GetSettingValueAsync(key, "");
            
            if (string.IsNullOrEmpty(stringValue))
                return defaultValue;

            try
            {
                return (T)Convert.ChangeType(stringValue, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        public async Task SetSettingValueAsync(string key, string value)
        {
            var existing = await _repository.GetByKeyAsync(key);
            
            if (existing.SettingKey == key)
            {
                existing.SettingValue = value;
                existing.UpdatedAt = DateTime.UtcNow;
                await _repository.SaveAsync(existing);
            }
            else
            {
                var newSetting = new SystemSettings
                {
                    SettingKey = key,
                    SettingValue = value,
                    SettingType = "string",
                    Category = "General",
                    IsEditable = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _repository.SaveAsync(newSetting);
            }
        }

        public async Task SetSettingValueAsync<T>(string key, T value)
        {
            var stringValue = value?.ToString() ?? "";
            var settingType = GetPropertyType(typeof(T));
            
            var existing = await _repository.GetByKeyAsync(key);
            
            if (existing.SettingKey == key)
            {
                existing.SettingValue = stringValue;
                existing.SettingType = settingType;
                existing.UpdatedAt = DateTime.UtcNow;
                await _repository.SaveAsync(existing);
            }
            else
            {
                var newSetting = new SystemSettings
                {
                    SettingKey = key,
                    SettingValue = stringValue,
                    SettingType = settingType,
                    Category = "General",
                    IsEditable = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _repository.SaveAsync(newSetting);
            }
        }

        public async Task InitializeDefaultSettingsAsync()
        {
            var defaultSettings = GetDefaultSettings();
            var existingKeys = (await _repository.GetAllAsync())
                .Select(s => s.SettingKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var setting in defaultSettings)
            {
                if (!existingKeys.Contains(setting.SettingKey))
                {
                    await _repository.SaveAsync(setting);
                }
            }
        }

        public async Task<bool> IsFirstRunAsync()
        {
            var setting = await _repository.GetByKeyAsync("System.FirstRun");
            return setting.SettingKey != "System.FirstRun" || setting.SettingValue == "true";
        }

        public async Task ResetToDefaultsAsync()
        {
            var defaultSettings = GetDefaultSettings();
            await _repository.UpdateMultipleAsync(defaultSettings);
        }

        public async Task<bool> ValidateSettingAsync(string key, string value)
        {
            var setting = await _repository.GetByKeyAsync(key);
            if (setting.SettingKey != key) return true; // New setting

            try
            {
                switch (setting.SettingType.ToLower())
                {
                    case "int":
                        int.Parse(value);
                        break;
                    case "bool":
                        bool.Parse(value);
                        break;
                    case "decimal":
                        decimal.Parse(value);
                        break;
                    case "json":
                        JsonSerializer.Deserialize<object>(value);
                        break;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<string>> ValidateSettingsAsync(Dictionary<string, string> settings)
        {
            var errors = new List<string>();
            
            foreach (var kvp in settings)
            {
                var isValid = await ValidateSettingAsync(kvp.Key, kvp.Value);
                if (!isValid)
                {
                    errors.Add($"Invalid value for setting '{kvp.Key}'");
                }
            }
            
            return errors;
        }

        public async Task<string> ExportSettingsAsync()
        {
            var settings = await _repository.GetAllAsync();
            var exportData = settings.Select(s => new
            {
                s.SettingKey,
                s.SettingValue,
                s.SettingType,
                s.Category,
                s.Description,
                s.IsEditable
            });
            
            return JsonSerializer.Serialize(exportData, new JsonSerializerOptions { WriteIndented = true });
        }

        public async Task<bool> ImportSettingsAsync(string jsonData)
        {
            try
            {
                var importData = JsonSerializer.Deserialize<List<SystemSettings>>(jsonData);
                if (importData != null)
                {
                    await _repository.UpdateMultipleAsync(importData);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private List<SystemSettings> GetDefaultSettings()
        {
            return new List<SystemSettings>
            {
                // General Settings
                new() { SettingKey = "CompanyName", SettingValue = "NexPos", SettingType = "string", Category = "General", Description = "Company Name", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "CompanyAddress", SettingValue = "", SettingType = "string", Category = "General", Description = "Company Address", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "CompanyPhone", SettingValue = "", SettingType = "string", Category = "General", Description = "Company Phone", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "CompanyEmail", SettingValue = "", SettingType = "string", Category = "General", Description = "Company Email", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "DefaultLanguage", SettingValue = "en", SettingType = "string", Category = "General", Description = "Default Language", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "DefaultCurrency", SettingValue = "USD", SettingType = "string", Category = "General", Description = "Default Currency", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "TimeZone", SettingValue = "UTC", SettingType = "string", Category = "General", Description = "System Time Zone", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },

                // POS Settings
                new() { SettingKey = "EnableBarcodeScanner", SettingValue = "true", SettingType = "bool", Category = "POS", Description = "Enable Barcode Scanner", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "EnableReceiptPrinting", SettingValue = "true", SettingType = "bool", Category = "POS", Description = "Enable Receipt Printing", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "AutoPrintReceipt", SettingValue = "false", SettingType = "bool", Category = "POS", Description = "Auto Print Receipt", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "ShowProductImages", SettingValue = "true", SettingType = "bool", Category = "POS", Description = "Show Product Images", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "ProductsPerPage", SettingValue = "30", SettingType = "int", Category = "POS", Description = "Products Per Page", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "EnableDiscounts", SettingValue = "true", SettingType = "bool", Category = "POS", Description = "Enable Discounts", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "MaxDiscountPercentage", SettingValue = "50", SettingType = "decimal", Category = "POS", Description = "Max Discount Percentage", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "ReceiptPrinterName", SettingValue = "", SettingType = "string", Category = "POS", Description = "Receipt Printer", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "RequireCustomerForSale", SettingValue = "false", SettingType = "bool", Category = "POS", Description = "Require Customer for Sale", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },

                // Inventory Settings
                new() { SettingKey = "EnableLowStockAlerts", SettingValue = "true", SettingType = "bool", Category = "Inventory", Description = "Enable Low Stock Alerts", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "LowStockThreshold", SettingValue = "10", SettingType = "int", Category = "Inventory", Description = "Low Stock Threshold", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "EnableNegativeStock", SettingValue = "false", SettingType = "bool", Category = "Inventory", Description = "Enable Negative Stock", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "AutoReorderEnabled", SettingValue = "false", SettingType = "bool", Category = "Inventory", Description = "Enable Auto Reorder", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "ReorderPoint", SettingValue = "5", SettingType = "int", Category = "Inventory", Description = "Reorder Point", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "ReorderQuantity", SettingValue = "50", SettingType = "int", Category = "Inventory", Description = "Reorder Quantity", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },

                // Security Settings
                new() { SettingKey = "SessionTimeout", SettingValue = "30", SettingType = "int", Category = "Security", Description = "Session Timeout (minutes)", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "MaxLoginAttempts", SettingValue = "5", SettingType = "int", Category = "Security", Description = "Max Login Attempts", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "PasswordMinLength", SettingValue = "8", SettingType = "int", Category = "Security", Description = "Password Minimum Length", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "RequireStrongPassword", SettingValue = "true", SettingType = "bool", Category = "Security", Description = "Require Strong Password", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "EnableTwoFactorAuth", SettingValue = "false", SettingType = "bool", Category = "Security", Description = "Enable Two-Factor Auth", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "EnableAuditLogging", SettingValue = "true", SettingType = "bool", Category = "Security", Description = "Enable Audit Logging", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },

                // Theme Settings
                new() { SettingKey = "PrimaryColor", SettingValue = "#008080", SettingType = "string", Category = "Theme", Description = "Primary Color", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "SecondaryColor", SettingValue = "#006666", SettingType = "string", Category = "Theme", Description = "Secondary Color", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "Theme", SettingValue = "light", SettingType = "string", Category = "Theme", Description = "Theme", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "EnableAnimations", SettingValue = "true", SettingType = "bool", Category = "Theme", Description = "Enable Animations", IsEditable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },

                // System Settings
                new() { SettingKey = "System.FirstRun", SettingValue = "false", SettingType = "bool", Category = "System", Description = "First Run Flag", IsEditable = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { SettingKey = "System.Version", SettingValue = "1.0.0", SettingType = "string", Category = "System", Description = "System Version", IsEditable = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            };
        }

        private string GetCategoryDisplayName(string category)
        {
            return category switch
            {
                "General" => "General Settings",
                "POS" => "POS Configuration",
                "Inventory" => "Inventory Management",
                "Security" => "Security & Access",
                "Theme" => "Appearance & Theme",
                "System" => "System Information",
                _ => category
            };
        }

        private string GetCategoryIcon(string category)
        {
            return category switch
            {
                "General" => "fas fa-cog",
                "POS" => "fas fa-cash-register",
                "Inventory" => "fas fa-boxes",
                "Security" => "fas fa-shield-alt",
                "Theme" => "fas fa-palette",
                "System" => "fas fa-info-circle",
                _ => "fas fa-cog"
            };
        }

        private string GetCategoryDescription(string category)
        {
            return category switch
            {
                "General" => "Basic system configuration and company information",
                "POS" => "Point of Sale system settings and preferences",
                "Inventory" => "Inventory management and stock control settings",
                "Security" => "Security policies and access control settings",
                "Theme" => "User interface appearance and customization",
                "System" => "System information and technical settings",
                _ => "System configuration settings"
            };
        }

        private void SetPropertyValue(AppSettingsDTO appSettings, string propertyName, string value, string settingType)
        {
            try
            {
                var property = typeof(AppSettingsDTO).GetProperty(propertyName);
                if (property != null && property.CanWrite)
                {
                    object convertedValue = settingType.ToLower() switch
                    {
                        "int" => int.Parse(value),
                        "bool" => bool.Parse(value),
                        "decimal" => decimal.Parse(value),
                        _ => value
                    };
                    property.SetValue(appSettings, convertedValue);
                }
            }
            catch
            {
                // Ignore conversion errors
            }
        }

        private string GetPropertyType(Type propertyType)
        {
            return propertyType switch
            {
                Type t when t == typeof(int) => "int",
                Type t when t == typeof(bool) => "bool",
                Type t when t == typeof(decimal) => "decimal",
                _ => "string"
            };
        }
    }
}
