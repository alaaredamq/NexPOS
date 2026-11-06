using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cloud.Services;

public static class PageFlash
{
    /// <summary>
    /// A one-line confirmation shown once, as a toast, on the page the handler redirects to. Same words as the button that was
    /// pressed ("Extended by 12 months"), so the action keeps its name through the whole flow.
    /// </summary>
    public static void Flash(this PageModel page, string message) => page.TempData["Flash"] = message;
}
