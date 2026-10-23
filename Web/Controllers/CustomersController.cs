using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Application.IServices.Users;
using Application.IServices.Reports;
using Application.IServices.Base;
using Application.DTOs.Customers;
using Microsoft.AspNetCore.Mvc.Localization;
using Web.Attributes;
using Web.Helpers;
using Core.Entities.Users;
using static Web.Helpers.CurrencyHelper;

namespace Web.Controllers;

[Authorize]
[RequirePermission(PermissionHelper.Permissions.Customers_View)]
public class CustomersController : BaseController
{
    private readonly ICustomerService _customerService;
    private readonly ICustomerDebtService _customerDebtService;
    private readonly IViewLocalizer _localizer;
    private readonly IExportService _exportService;
    private readonly ICurrencyService _currencyService;

    public CustomersController(ICustomerService customerService, ICustomerDebtService customerDebtService, IViewLocalizer localizer, IExportService exportService, ICurrencyService currencyService)
    {
        _customerService = customerService;
        _customerDebtService = customerDebtService;
        _localizer = localizer;
        _exportService = exportService;
        _currencyService = currencyService;
    }

    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Customers";
        ViewData["Subtitle"] = "Manage customer database";
        
        // Get customers for display
        var customers = await _customerService.GetAllAsync();
        ViewBag.Customers = customers;
        
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetCustomers(int start = 0, int length = 10, string searchValue = "", string sortColumn = "FirstName", string sortDirection = "asc")
    {
        try
        {
            var customers = await _customerService.GetAllWithOrdersAsync();
            
            // Apply search filter
            if (!string.IsNullOrEmpty(searchValue))
            {
                customers = customers.Where(c => 
                    c.FirstName.Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    c.LastName.Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    c.Email.Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    (c.PhoneNumber ?? "").Contains(searchValue, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            // Apply sorting
            var sortedCustomers = sortDirection.ToLower() == "desc" 
                ? customers.OrderByDescending(c => GetCustomerPropertyValue(c, sortColumn))
                : customers.OrderBy(c => GetCustomerPropertyValue(c, sortColumn));

            // Apply pagination
            var pagedCustomers = sortedCustomers.Skip(start).Take(length);

            return Json(new
            {
                draw = Request.Query["draw"].FirstOrDefault(),
                recordsTotal = customers.Count,
                recordsFiltered = customers.Count,
                data = pagedCustomers.Select(c => new
                {
                    id = c.Id,
                    firstName = c.FirstName,
                    lastName = c.LastName,
                    email = c.Email,
                    phoneNumber = c.PhoneNumber,
                    address = c.Address,
                    ordersCount = c.Orders?.Count ?? 0,
                    totalSpent = c.Orders?.Sum(o => o.TotalAmount) ?? 0,
                    isActive = c.IsActive, // Return boolean instead of string
                    actions = GetActionButtons(c.Id)
                })
            });
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    private object GetCustomerPropertyValue(Customer customer, string propertyName)
    {
        return propertyName switch
        {
            "FirstName" => customer.FirstName,
            "LastName" => customer.LastName,
            "Email" => customer.Email,
            "PhoneNumber" => customer.PhoneNumber ?? "",
            _ => customer.FirstName
        };
    }

    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Customers_Create)]
    public async Task<IActionResult> Create()
    {
        ViewData["Title"] = "Create Customer";
        ViewData["Subtitle"] = "Add a new customer to the database";
        
        var model = new CreateCustomerRequest();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionHelper.Permissions.Customers_Create)]
    public async Task<IActionResult> Create(CreateCustomerRequest model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var customer = new Customer(model.FirstName, model.LastName, model.Email, model.PhoneNumber, model.Address)
            {
                IsActive = model.IsActive
            };

            await _customerService.CreateAsync(customer);
            TempData["SuccessMessage"] = "Customer created successfully!";
            return RedirectToAction("Index");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"An error occurred: {ex.Message}");
        }

        return View(model);
    }

    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Customers_Edit)]
    public async Task<IActionResult> Edit(int id)
    {
        try
        {
            var customer = await _customerService.GetByIdAsync(id);
            if (customer == null)
            {
                TempData["ErrorMessage"] = "Customer not found.";
                return RedirectToAction("Index");
            }

            ViewData["Title"] = "Edit Customer";
            ViewData["Subtitle"] = $"Edit {customer.FirstName} {customer.LastName}";
            
            var model = new UpdateCustomerRequest
            {
                Id = customer.Id,
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                PhoneNumber = customer.PhoneNumber,
                Address = customer.Address,
                DateOfBirth = null, // Customer entity doesn't have DateOfBirth
                IsActive = customer.IsActive
            };

            return View(model);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"An error occurred: {ex.Message}";
            return RedirectToAction("Index");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionHelper.Permissions.Customers_Edit)]
    public async Task<IActionResult> Edit(UpdateCustomerRequest model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var customer = await _customerService.GetByIdAsync(model.Id);
            if (customer == null)
            {
                ModelState.AddModelError(string.Empty, "Customer not found.");
                return View(model);
            }

            // Update the customer properties
            customer.FirstName = model.FirstName;
            customer.LastName = model.LastName;
            customer.Email = model.Email;
            customer.PhoneNumber = model.PhoneNumber;
            customer.Address = model.Address;
            customer.IsActive = model.IsActive;

            await _customerService.UpdateAsync(model.Id, customer);
            TempData["SuccessMessage"] = "Customer updated successfully!";
            return RedirectToAction("Index");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"An error occurred: {ex.Message}");
        }

        return View(model);
    }

    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Customers_Delete)]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _customerService.DeleteAsync(id);
            return Json(new { success = true, message = "Customer deleted successfully!" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"An error occurred: {ex.Message}" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        try
        {
            var customer = await _customerService.GetByIdAsync(id);
            if (customer == null)
            {
                TempData["ErrorMessage"] = "Customer not found.";
                return RedirectToAction("Index");
            }

            ViewData["Title"] = "Customer Details";
            ViewData["Subtitle"] = $"{customer.FirstName} {customer.LastName}";
            
            // Get default currency for debt display
            var defaultCurrency = await _currencyService.GetDefaultAsync();
            ViewBag.DefaultCurrencyNotation = defaultCurrency?.Notation ?? "$";
            ViewBag.DebtTransactions = await _customerDebtService.GetTransactionsAsync(id);

            return View(customer);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"An error occurred: {ex.Message}";
            return RedirectToAction("Index");
        }
    }

    /// <summary>
    /// Records a manual debt adjustment (editing what the customer owes, or what the business
    /// owes the customer) from the customer's Details page.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionHelper.Permissions.Customers_Edit)]
    public async Task<IActionResult> AddDebtTransaction(AddCustomerDebtTransactionRequest model)
    {
        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
            return Json(new { success = false, message = firstError ?? "Invalid request." });
        }

        try
        {
            var signedAmount = model.CustomerOwesMore ? model.Amount : -model.Amount;
            await _customerDebtService.AddManualTransactionAsync(model.CustomerId, GetCurrentUserId(), signedAmount, model.Note);
            return Json(new { success = true, message = "Debt updated successfully!" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"An error occurred: {ex.Message}" });
        }
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Unable to determine the current user from the authentication claims.");
        }
        return userId;
    }

    private string GetActionButtons(int customerId)
    {
        return $@"
            <div class='btn-group' role='group'>
                <button type='button' class='btn btn-sm btn-outline-primary' onclick='viewCustomer({customerId})' title='View Details'>
                    <i class='fas fa-eye'></i>
                </button>
                <button type='button' class='btn btn-sm btn-outline-teal' onclick='editCustomer({customerId})' title='Edit'>
                    <i class='fas fa-edit'></i>
                </button>
                <button type='button' class='btn btn-sm btn-outline-warning' onclick='addCustomerDebt({customerId})' title='Add Debt Transaction'>
                    <i class='fas fa-hand-holding-usd'></i>
                </button>
                <button type='button' class='btn btn-sm btn-outline-danger' onclick='deleteCustomer({customerId})' title='Delete'>
                    <i class='fas fa-trash'></i>
                </button>
            </div>";
    }

    /// <summary>
    /// Export customers to CSV
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ExportCustomers()
    {
        try
        {
            var csvData = await _exportService.ExportCustomersToCsvAsync();
            var fileName = $"customers_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            
            return File(csvData, "text/csv", fileName);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Export failed: {ex.Message}" });
        }
    }
}