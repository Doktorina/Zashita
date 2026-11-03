namespace Zashita.Core.Brand;

/// <summary>Ссылки на иконку сайта для HTML без _Layout (печать / предпросмотр).</summary>
public static class SiteIconHtml
{
    public const string HeadLinks =
        """<link rel="icon" type="image/svg+xml" href="/brand/site-icon.svg"/>"""
        + """<link rel="icon" type="image/png" sizes="32x32" href="/brand/site-icon-32.png"/>"""
        + """<link rel="icon" type="image/png" sizes="16x16" href="/brand/site-icon-16.png"/>"""
        + """<link rel="shortcut icon" href="/favicon.ico"/>""";
}
