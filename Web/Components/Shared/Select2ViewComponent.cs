using Microsoft.AspNetCore.Mvc;
using Web.Models.Shared;

namespace Web.Components.Shared;

public class Select2ViewComponent : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(Select2Model model)
    {
        return View(model);
    }
}
