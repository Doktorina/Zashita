using System.Text;
using Zashita.Core.Excel;

namespace Zashita.Web.Services;

public sealed class HtmlPrintService
{
  private readonly ExcelWorkbookStore _store;

  public HtmlPrintService(ExcelWorkbookStore store)
  {
    _store = store;
  }

  public string BuildDocument(string title, string? sheetName)
  {
    var sb = new StringBuilder();
    sb.Append("<!DOCTYPE html><html lang=\"ru\"><head><meta charset=\"utf-8\"/>");
    sb.Append("<title>").Append(System.Net.WebUtility.HtmlEncode(title)).Append("</title>");
    sb.Append(Zashita.Core.Brand.SiteIconHtml.HeadLinks);
    sb.Append("<link rel=\"stylesheet\" href=\"/css/print.css\"/>");
    sb.Append("</head><body class=\"print-doc\">");
    sb.Append("<header class=\"print-header\"><h1>").Append(System.Net.WebUtility.HtmlEncode(title)).Append("</h1>");

    if (_store.HasWorkbook)
    {
      sb.Append("<p class=\"print-meta\">Файл: ")
        .Append(System.Net.WebUtility.HtmlEncode(_store.FileName ?? "—"))
        .Append(" · ")
        .Append(_store.LoadedAt.ToLocalTime().ToString("g"))
        .Append("</p>");
    }

    sb.Append("</header><section class=\"print-body\">");

    if (!_store.HasWorkbook)
    {
      sb.Append("<p class=\"hint\">Подключите Excel на странице «Данные 1С», чтобы вывести таблицу для печати.</p>");
    }
    else if (string.IsNullOrWhiteSpace(sheetName))
    {
      var first = _store.SheetNames.FirstOrDefault();
      if (first is not null)
      {
        sb.Append(_store.BuildHtmlTable(first));
      }
    }
    else
    {
      sb.Append(_store.BuildHtmlTable(sheetName));
    }

    sb.Append("</section><script>window.addEventListener('load',function(){window.print();});</script>");
    sb.Append("</body></html>");
    return sb.ToString();
  }
}
