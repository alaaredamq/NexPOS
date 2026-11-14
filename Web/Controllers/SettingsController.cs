using Application.DTOs.Settings;
using Application.IServices.Sales;
using Application.IServices.Settings;
using Core.Entities.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Attributes;
using Web.Helpers;

namespace Web.Controllers
{
    [Authorize]
    [RequirePermission(PermissionHelper.Permissions.Settings_Manage)]
    public class SettingsController : BaseController
    {
        private readonly ISystemSettingsService _settingsService;
        private readonly IStoreSettingsService _storeSettingsService;
        private readonly IThermalPrinterService _thermalPrinterService;
        private readonly Web.Services.IBrandingService _branding;

        public SettingsController(
            ISystemSettingsService settingsService,
            IStoreSettingsService storeSettingsService,
            IThermalPrinterService thermalPrinterService,
            Web.Services.IBrandingService branding)
        {
            _settingsService = settingsService;
            _storeSettingsService = storeSettingsService;
            _thermalPrinterService = thermalPrinterService;
            _branding = branding;
        }

        /// <summary>
        /// Lists printers installed on this machine, for the receipt printer dropdown.
        /// </summary>
        [HttpGet]
        public IActionResult GetAvailablePrinters()
        {
            try
            {
                var printers = _thermalPrinterService.GetAvailablePrinters();
                return Json(new { success = true, data = printers });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Prints a short test slip to the chosen printer so the client can confirm it
        /// works before saving it as the receipt printer.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> TestPrinter([FromForm] string printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName))
            {
                return Json(new { success = false, message = "Select a printer first" });
            }

            try
            {
                var printed = await _thermalPrinterService.TestPrintAsync(printerName);
                return Json(new
                {
                    success = printed,
                    message = printed
                        ? "Test print sent successfully"
                        : $"Could not print to '{printerName}'. Check that it's connected and powered on."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Landing page: links to the settings pages that actually change app behaviour.
        public IActionResult Index() => View();

        [HttpGet]
        public IActionResult Branding() => View();

        /// <summary>
        /// White-label branding: app name, logo and favicon. Blank / "remove" falls back to the Coduo POS defaults.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Branding(string? appName, IFormFile? logo, IFormFile? favicon, bool removeLogo, bool removeFavicon)
        {
            try
            {
                _branding.SetAppName(appName);

                if (removeLogo) _branding.RemoveLogo();
                else if (logo is { Length: > 0 })
                {
                    using var stream = logo.OpenReadStream();
                    _branding.SaveLogo(stream, Path.GetExtension(logo.FileName));
                }

                if (removeFavicon) _branding.RemoveFavicon();
                else if (favicon is { Length: > 0 })
                {
                    using var stream = favicon.OpenReadStream();
                    _branding.SaveFavicon(stream, Path.GetExtension(favicon.FileName));
                }

                TempData["SuccessMessage"] = "Branding saved";
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            return RedirectToAction(nameof(Branding));
        }

        [HttpGet]
        public async Task<IActionResult> GetByCategory(string category)
        {
            try
            {
                var settings = await _settingsService.GetSettingsByCategoryAsync(category);
                return Json(new { success = true, data = settings });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetSetting(string key)
        {
            try
            {
                var setting = await _settingsService.GetByKeyAsync(key);
                return Json(new { success = true, data = setting });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateSetting([FromBody] SettingUpdateRequest request)
        {
            try
            {
                var setting = await _settingsService.GetByKeyAsync(request.SettingKey);
                
                if (setting.SettingKey == request.SettingKey)
                {
                    setting.SettingValue = request.SettingValue;
                    await _settingsService.UpdateAsync(setting);
                }
                else
                {
                    var newSetting = new SystemSettings
                    {
                        SettingKey = request.SettingKey,
                        SettingValue = request.SettingValue,
                        SettingType = "string",
                        Category = "General",
                        IsEditable = true
                    };
                    await _settingsService.CreateAsync(newSetting);
                }

                return Json(new { success = true, message = "Setting updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateBulk([FromBody] BulkSettingUpdateRequest request)
        {
            try
            {
                await _settingsService.UpdateBulkAsync(request);
                return Json(new { success = true, message = "Settings updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> AppSettings()
        {
            try
            {
                var appSettings = await _settingsService.GetAppSettingsAsync();
                return View(appSettings);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error loading app settings: {ex.Message}";
                return View(new AppSettingsDTO());
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateAppSettings([FromForm] AppSettingsDTO appSettings)
        {
            try
            {
                await _settingsService.UpdateAppSettingsAsync(appSettings);
                TempData["SuccessMessage"] = "Application settings updated successfully";
                return RedirectToAction(nameof(AppSettings));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error updating settings: {ex.Message}";
                return View(nameof(AppSettings), appSettings);
            }
        }

        [HttpPost]
        public async Task<IActionResult> ResetToDefaults()
        {
            try
            {
                await _settingsService.ResetToDefaultsAsync();
                return Json(new { success = true, message = "Settings reset to defaults successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Export()
        {
            try
            {
                var settingsJson = await _settingsService.ExportSettingsAsync();
                var fileName = $"system_settings_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                
                return File(System.Text.Encoding.UTF8.GetBytes(settingsJson), "application/json", fileName);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error exporting settings: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        public async Task<IActionResult> Import(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return Json(new { success = false, message = "No file uploaded" });
                }

                using var reader = new StreamReader(file.OpenReadStream());
                var jsonData = await reader.ReadToEndAsync();
                
                var success = await _settingsService.ImportSettingsAsync(jsonData);
                
                if (success)
                {
                    return Json(new { success = true, message = "Settings imported successfully" });
                }
                else
                {
                    return Json(new { success = false, message = "Invalid settings file format" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ValidateSetting(string key, string value)
        {
            try
            {
                var isValid = await _settingsService.ValidateSettingAsync(key, value);
                return Json(new { success = true, isValid = isValid });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetSettingValue(string key, string defaultValue = "")
        {
            try
            {
                var value = await _settingsService.GetSettingValueAsync(key, defaultValue);
                return Json(new { success = true, value = value });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> InitializeDefaults()
        {
            try
            {
                await _settingsService.InitializeDefaultSettingsAsync();
                return Json(new { success = true, message = "Default settings initialized successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        #region Store Settings

        /// <summary>
        /// Store Settings page for managing receipt information
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> StoreSettings()
        {
            try
            {
                var settings = await _storeSettingsService.GetStoreSettingsAsync();
                return View(settings);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error loading store settings: {ex.Message}";
                return View(new StoreSettingsDTO());
            }
        }

        /// <summary>
        /// Get store settings as JSON
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetStoreSettings()
        {
            try
            {
                var settings = await _storeSettingsService.GetStoreSettingsAsync();
                return Json(new { success = true, data = settings });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Update store settings
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> UpdateStoreSettings([FromBody] UpdateStoreSettingsRequest request)
        {
            try
            {
                // Validate
                var errors = await _storeSettingsService.ValidateSettingsAsync(request);
                if (errors.Any())
                {
                    return Json(new { success = false, message = string.Join(", ", errors) });
                }

                // Get current user ID if available
                int? userId = null;
                if (User.Identity?.IsAuthenticated == true)
                {
                    var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
                    if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var parsedUserId))
                    {
                        userId = parsedUserId;
                    }
                }

                var settings = await _storeSettingsService.UpdateStoreSettingsAsync(request, userId);
                return Json(new { success = true, data = settings, message = "Store settings updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Reset store settings to defaults
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ResetStoreSettings()
        {
            try
            {
                var settings = await _storeSettingsService.ResetToDefaultsAsync();
                return Json(new { success = true, data = settings, message = "Store settings reset to defaults" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        #endregion
    }
}