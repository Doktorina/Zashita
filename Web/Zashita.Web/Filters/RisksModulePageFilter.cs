using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Zashita.Web.Filters;

/// <summary>
/// На всех страницах /Risks* выставляет ActiveModule=риски,
/// чтобы в тулбаре всегда был переключатель объекта.
/// </summary>
public sealed class RisksModulePageFilter : IPageFilter
{
    public void OnPageHandlerSelected(PageHandlerSelectedContext context)
    {
    }

    public void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        if (context.HandlerInstance is not PageModel page)
        {
            return;
        }

        var path = context.HttpContext.Request.Path;
        if (!path.StartsWithSegments("/Risks"))
        {
            return;
        }

        page.ViewData["ActiveModule"] = "риски";
        page.ViewData["IsModulePage"] = true;
    }

    public void OnPageHandlerExecuted(PageHandlerExecutedContext context)
    {
    }
}
