namespace Zashita.Web.Pages.Training;

/// <summary>
/// Встроенные стили заявки для Word (.doc): альбом A4, без внешней CSS
/// (Word не загружает /css/... из сохранённого файла).
/// </summary>
internal static class TrainingApplicationWordExportCss
{
    public const string LandscapeDoc =
        @"@page Section1 {
  size: 297mm 210mm;
  mso-page-orientation: landscape;
  margin: 12mm 14mm 12mm 14mm;
}
div.Section1 { page: Section1; }
body {
  margin: 0;
  padding: 0;
  background: #fff;
  color: #000;
  font-family: 'Times New Roman', Times, serif;
  font-size: 12pt;
  line-height: 1.35;
}
.training-app-doc { width: 100%; margin: 0; font-family: 'Times New Roman', Times, serif; font-size: 12pt; color: #000; line-height: 1.35; }
.training-app-object { text-align: right; font-size: 12pt; margin: 0 0 8pt; text-decoration: underline; }
.training-app-letterhead { text-align: center; margin: 0 0 12pt; }
.training-app-letterhead-org { font-weight: 700; font-size: 13pt; line-height: 1.3; padding: 2pt 0 3pt; }
.training-app-letterhead-details { font-size: 9.5pt; line-height: 1.35; padding: 4pt 0 6pt; white-space: pre-line; }
.training-app-letterhead-rule {
  border: none; border-top: 1px solid #000; height: 0; margin: 0; padding: 0;
  mso-border-top-alt: solid #000 .75pt;
}
.training-app-letterhead-rule-double {
  border-top: 3px double #000;
  mso-border-top-alt: double #000 2.25pt;
  margin-top: 2pt;
}
table.training-app-top { width: 100%; border-collapse: collapse; border: none; margin: 0 0 14pt; }
table.training-app-top td { border: none; padding: 0; vertical-align: top; font-family: 'Times New Roman', Times, serif; font-size: 12pt; }
td.training-app-date { width: 50%; text-align: left; }
td.training-app-to { width: 50%; text-align: right; line-height: 1.35; }
.training-app-title { text-align: center; font-weight: 700; font-size: 14pt; margin: 8pt 0 2pt; text-transform: uppercase; }
.training-app-subtitle { text-align: center; margin: 0 0 16pt; font-size: 12pt; }
.training-app-section { margin: 0 0 12pt; }
.training-app-label { font-weight: 700; margin-bottom: 3pt; }
table.training-app-section-program { width: 100%; border-collapse: collapse; border: none; margin: 0 0 12pt; }
table.training-app-section-program td { border: none; padding: 0; vertical-align: bottom; font-family: 'Times New Roman', Times, serif; font-size: 12pt; }
td.training-app-label-inline { font-weight: 700; white-space: nowrap; padding-right: 8pt; padding-bottom: 1pt; }
table.training-app-section-program td.training-app-program {
  border: none; border-bottom: 1px solid #000;
  mso-border-bottom-alt: solid #000 .75pt;
  padding: 0 2pt 1pt; width: 100%;
}
table.training-app-contact { width: 100%; border-collapse: collapse; border: none; margin-top: 3pt; }
table.training-app-contact td { border: none; padding: 3pt 0; vertical-align: bottom; font-family: 'Times New Roman', Times, serif; font-size: 12pt; }
table.training-app-contact td.k { width: 18%; white-space: nowrap; padding-right: 8pt; }
table.training-app-contact td.v {
  border: none; border-bottom: 1px solid #000;
  mso-border-bottom-alt: solid #000 .75pt;
  padding-left: 3pt; min-width: 180pt;
}
table.training-app-table {
  width: 100%; border-collapse: collapse; table-layout: fixed;
  margin-top: 6pt; font-size: 10.5pt;
  border: 1px solid #000;
}
table.training-app-table th, table.training-app-table td {
  border: 1px solid #000;
  mso-border-alt: solid #000 .75pt;
  padding: 3pt 2pt; vertical-align: middle;
  font-family: 'Times New Roman', Times, serif; font-size: 10pt;
  word-wrap: break-word;
}
table.training-app-table th { text-align: center; font-weight: 700; }
table.training-app-table td.c { text-align: center; }
.training-app-foot { margin-top: 22pt; line-height: 1.6; font-size: 12pt; }
table.training-app-foot-split { width: 100%; border-collapse: collapse; border: none; margin-top: 22pt; }
table.training-app-foot-split td { border: none; padding: 0; vertical-align: top; font-family: 'Times New Roman', Times, serif; font-size: 12pt; line-height: 1.6; }
td.training-app-foot-left { width: 58%; text-align: left; padding-right: 10pt; }
td.training-app-foot-right { width: 42%; text-align: right; vertical-align: bottom; }
.training-app-sign-row { margin-top: 8pt; }
.training-app-hint { text-align: center; font-size: 10pt; margin: 0 0 8pt; font-style: italic; }
.training-app-edit-hint, .ppe-no-print { display: none !important; }
";
}
