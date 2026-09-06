using Microsoft.AspNetCore.Mvc;
using Web.Models.Shared;

namespace Web.Components.Shared;

public class FormViewComponent : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(FormModel model)
    {
        return View(model);
    }
}
