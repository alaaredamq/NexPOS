using Microsoft.AspNetCore.Mvc;
using Web.Models.Shared;

namespace Web.Components.Shared;

public class ModalViewComponent : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(ModalModel model)
    {
        return View(model);
    }
}
