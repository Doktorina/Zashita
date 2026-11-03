using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Zashita.Web.Services;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireLoginAttribute : Attribute, IPageFilter
{
    public void OnPageHandlerExecuted(PageHandlerExecutedContext context)
    {
    }

    public void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        var http = context.HttpContext;
        var isAuthenticated = http.Session.GetString(AuthConstants.SessionKey) == "1"
            || http.Request.Cookies[AuthConstants.CookieName] == "1";

        if (isAuthenticated)
        {
            return;
        }

        var returnUrl = (http.Request.Path + http.Request.QueryString).ToString();
        context.Result = new RedirectToPageResult("/Login", new { returnUrl });
    }

    public void OnPageHandlerSelected(PageHandlerSelectedContext context)
    {
    }
}
