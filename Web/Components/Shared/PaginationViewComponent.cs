using Microsoft.AspNetCore.Mvc;
using Web.Models.Shared;

namespace Web.Components.Shared;

public class PaginationViewComponent : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(PaginationModel model)
    {
        return View(model);
    }
}

public class PaginationModel
{
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalItems { get; set; } = 0;
    public int PageSize { get; set; } = 10;
    public int MaxVisiblePages { get; set; } = 5;
    public string Class { get; set; } = "pagination";
    public string Size { get; set; } = string.Empty; // sm, lg
    public bool ShowFirstLast { get; set; } = true;
    public bool ShowPreviousNext { get; set; } = true;
    public bool ShowInfo { get; set; } = true;
    public bool ShowPageSizeSelector { get; set; } = false;
    public List<int> PageSizeOptions { get; set; } = new() { 10, 25, 50, 100 };
    public string OnPageChange { get; set; } = string.Empty; // JavaScript function
    public string OnPageSizeChange { get; set; } = string.Empty; // JavaScript function
    public string Language { get; set; } = "en";
    public Dictionary<string, object> AdditionalAttributes { get; set; } = new();
}

public class PaginationInfo
{
    public int StartItem { get; set; }
    public int EndItem { get; set; }
    public int TotalItems { get; set; }
    public string Text { get; set; } = string.Empty;
}
