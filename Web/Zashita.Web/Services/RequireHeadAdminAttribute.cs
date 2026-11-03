using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Zashita.Web.Services;

/// <summary>Только для главного администратора (личный кабинет).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireHeadAdminAttribute : Attribute, IPageFilter
{
    public void OnPageHandlerExecuted(PageHandlerExecutedContext context)
    {
    }

    public void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        var http = context.HttpContext;
        if (!AuthHelper.IsAuthenticated(http))
        {
            var returnUrl = (http.Request.Path + http.Request.QueryString).ToString();
            context.Result = new RedirectToPageResult("/Login", new { returnUrl });
            return;
        }

        if (!AuthHelper.IsHeadAdmin(http))
        {
            context.Result = new RedirectToPageResult("/Index");
        }
    }

    public void OnPageHandlerSelected(PageHandlerSelectedContext context)
    {
    }
}
