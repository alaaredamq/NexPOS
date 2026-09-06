using Microsoft.AspNetCore.Mvc;
using Web.Models.Shared;

namespace Web.Components.Shared;

public class DataTableViewComponent : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(DataTableModel model)
    {
        return View(model);
    }
}
