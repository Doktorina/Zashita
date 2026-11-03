/**
 * Скачивание превью документов ОПР в Word (.doc) и PDF.
 *
 * Word: простой CSS + border=1 на таблицах (сложный CSS Word ломает сетку).
 * PDF: фиксируем лист A4 (210×297 / 297×210), сбрасываем preview max-width/flex,
 * иначе html2canvas снимает узкую «карточку» или обрезает таблицу.
 */
(function (global) {
  'use strict';

  var A4_PORTRAIT_PX = 794;  // ~210mm @ 96dpi
  var A4_LANDSCAPE_PX = 1123; // ~297mm @ 96dpi
  var A3_LANDSCAPE_PX = 1587; // ~420mm @ 96dpi
  var A3_LANDSCAPE_SIZE = '1190.55pt 841.89pt';
  var MSO_A3_LANDSCAPE_W = 23811;
  var MSO_A3_LANDSCAPE_H = 16838;
  // Область содержимого при полях 10 мм (как в образце карты).
  var A4_PORTRAIT_CONTENT_PX = 718;  // ~190mm
  var A4_LANDSCAPE_CONTENT_PX = 1047; // ~277mm
  // Карта ОПР: доли колонок как в образце (Меры управления — самая широкая).
  var CARD_COL_WIDTHS_PCT = ['6%', '16%', '12%', '10%', '8%', '8%', '32%', '8%'];
  var CARD_COL_WIDTHS_PT = ['47pt', '126pt', '94pt', '79pt', '63pt', '63pt', '251pt', '63pt'];
  // ═══ LOCKED: доли/поля Word — не менять без нужды (таблица уже влезает) ═══
  var INFO_COL_WIDTHS_PCT = [
    '3.2%', '5.5%', '5.2%', '4.0%',
    '8.5%', '6.5%',
    '2.8%',
    '2.4%', '2.4%', '2.4%', '2.4%',
    '16.5%',
    '2.8%',
    '2.4%', '2.4%', '2.4%', '2.4%', '2.4%',
    '3.0%', '4.0%', '4.4%'
  ];
  var INFO_COL_WIDTHS_PT = [
    '26pt', '44pt', '42pt', '32pt',
    '68pt', '52pt',
    '22pt',
    '19pt', '19pt', '19pt', '19pt',
    '132pt',
    '22pt',
    '19pt', '19pt', '19pt', '19pt', '19pt',
    '24pt', '32pt', '35pt'
  ];
  var INFO_TABLE_WIDTH_PT = '100%';
  var INFO_TABLE_WIDTH_ATTR = '100%';
  var INFO_OK_BG = '#ffffff';
  var INFO_HEAD_BG = '#f0f0f0';
  var INFO_PAGE_MARGIN = '14.2pt 14.2pt 14.2pt 14.2pt';
  var INFO_TAIL_MARGIN = '14.2pt 14.2pt 14.2pt 14.2pt';
  // Высота шапки: буквы видны, без растягивания на полстраницы.
  var INFO_HEADER_HEIGHT_PT = '155pt';
  var INFO_HEADER_HEIGHT_PX = '163';
  var INFO_HEADER2_HEIGHT_PT = '118pt';
  var INFO_HEADER2_HEIGHT_PX = '124';

  /**
   * Превью режет документ на .sout-a4-info + .sout-info-tail-sheet.
   * Для Word убираем обёртки листов — иначе min-height/пустые div дают лишние страницы.
   */
  /** Убирает жёлтые выделения (ok/hl/mark/inline background) с СОУТ-инфо. */
  function stripSoutInfoHighlights(root) {
    if (!root || !root.querySelectorAll) return;
    Array.prototype.forEach.call(
      root.querySelectorAll('.sout-info-ok, .sout-info-hl, mark, [bgcolor], [style*="background"]'),
      function (el) {
        var bgAttr = (el.getAttribute('bgcolor') || '').toLowerCase();
        var st = (el.getAttribute('style') || '').toLowerCase();
        var isOk = el.classList && (el.classList.contains('sout-info-ok') || el.classList.contains('sout-info-hl'));
        var isYellow = /fff8|ffc|ffff99|ffffcc|ffeb|ffe4|yellow|highlight/i.test(bgAttr + ' ' + st)
          || el.tagName === 'MARK';
        if (!isOk && !isYellow) return;
        el.removeAttribute('bgcolor');
        if (el.style) {
          el.style.background = '#fff';
          el.style.backgroundColor = '#fff';
          el.style.fontWeight = 'inherit';
        }
        if (el.tagName === 'MARK' && el.parentNode) {
          while (el.firstChild) el.parentNode.insertBefore(el.firstChild, el);
          el.parentNode.removeChild(el);
        }
      }
    );
  }

  function flattenSoutInfoCloneForWord(clone) {
    if (!clone || !clone.querySelector) return;

    var wrappers = clone.querySelectorAll(
      '.sout-a4-page, .sout-a4-info, .sout-a4-portrait, .sout-a4-landscape,' +
      ' .sout-info-tail-sheet, .sout-preview-doc-wrap, .sout-preview-stage'
    );
    // Снизу вверх, чтобы не потерять вложенность при unwrap.
    var list = Array.prototype.slice.call(wrappers).reverse();
    list.forEach(function (sheet) {
      if (sheet === clone || !sheet.parentNode) return;
      var parent = sheet.parentNode;
      while (sheet.firstChild) {
        parent.insertBefore(sheet.firstChild, sheet);
      }
      parent.removeChild(sheet);
    });

    Array.prototype.forEach.call(
      clone.querySelectorAll('.sout-info-doc, .sout-info-tail, div, table'),
      function (el) {
        el.style.minHeight = '0';
        el.style.height = 'auto';
        el.style.maxHeight = 'none';
        el.style.boxShadow = 'none';
        if (el.classList && (
          el.classList.contains('sout-info-doc')
          || el.classList.contains('sout-info-tail')
        )) {
          el.style.margin = '0';
          el.style.padding = '0';
          el.style.width = '100%';
        }
      }
    );
  }

  /* Только селекторы без «>» — Word их криво парсит. */
  var WORD_DOC_CSS =
    'body { margin: 0; padding: 0; background: #fff; color: #000;' +
    '  font-family: "Times New Roman", Times, serif; font-size: 12pt; line-height: 1.35; }' +
    'h1, h2 { font-family: "Times New Roman", Times, serif; font-size: 14pt; font-weight: 700;' +
    '  text-align: center; margin: 0 0 10pt; }' +
    'p { margin: 0 0 8pt; }' +
    'table { border-collapse: collapse; width: 100%; }' +
    'th, td { border: 1px solid #000; padding: 4px 6px; vertical-align: top;' +
    '  font-family: "Times New Roman", Times, serif; font-size: 11pt; }' +
    /* После demoteTableHeaders: зелёная шапка только у таблиц ОПР/СИЗ, не у СОУТ-форм */
    'tr.risks-export-head-row th {' +
    '  font-weight: 700; text-align: center; }' +
    '.risks-wp-table tr.risks-export-head-row th,' +
    ' .risks-print-table tr.risks-export-head-row th,' +
    ' .ppe-doc-table tr.risks-export-head-row th,' +
    ' .risks-card-table tr.risks-export-head-row th,' +
    ' .risks-wp-table thead th, .risks-print-table thead th,' +
    ' .ppe-doc-table thead th, .risks-card-table thead th {' +
    '  background: #2f6b3a; color: #fff; font-weight: 700; text-align: center; }' +
    'table.risks-word-layout, table.risks-word-layout td,' +
    ' table.risks-card-acq-table, table.risks-card-acq-table td,' +
    ' table.risks-word-title-field td,' +
    ' table.risks-word-approve td {' +
    '  border: none !important; padding: 2px 4px; }' +
    'table.risks-word-title-field { width: 100%; border: none; border-collapse: collapse; margin: 0 0 10pt; }' +
    'td.risks-word-field-line {' +
    '  border: none !important; border-bottom: 1px solid #000 !important;' +
    '  text-align: center; font-weight: 700; vertical-align: bottom; }' +
    'table.risks-card-acq-table { width: 100%; border-collapse: collapse; table-layout: fixed; margin: 0; border: none !important; }' +
    'table.risks-card-acq-table td {' +
    '  border: none !important; vertical-align: bottom; font-size: 10.5pt; padding: 3pt 6pt 3pt 0; }' +
    'td.risks-card-acq-label { width: 52%; }' +
    'td.risks-card-acq-field { width: 18%; text-align: center; }' +
    'td.risks-card-acq-field-name { width: 30%; text-align: center; }' +
    '.risks-card-acq-underscore, p.risks-word-underscore {' +
    '  border: none; border-bottom: 1px solid #000; margin: 0 0 2pt; padding: 0;' +
    '  line-height: 14pt; min-height: 14pt; height: 14pt; }' +
    '.risks-chair-hint { font-size: 9pt; text-align: center; margin: 0; }' +
    '.risks-method-title-block, .risks-method-title-main, .risks-method-title-sub,' +
    ' .risks-method-title-import, .risks-method-subtitle-import, .risks-print-head-title { text-align: center; }' +
    '.risks-method-title-main, .risks-method-title-import { font-size: 16pt; font-weight: 700; margin: 0 0 6pt; text-transform: uppercase; }' +
    '.risks-method-title-sub, .risks-method-subtitle-import { font-size: 12pt; font-weight: 700; margin: 0 0 10pt; }' +
    '.risks-method-org-uline, .risks-wp-org-center { text-align: center; font-weight: 700; font-size: 16pt; margin: 0 0 14pt; }' +
    '.risks-regulation-running-header { text-align: center; font-weight: 700; font-size: 12pt; margin: 0 0 12pt; }' +
    '.risks-method-approve p, .risks-word-approve p { text-align: center; margin: 0 0 4pt; }' +
    '.risks-method-approve-label { font-weight: 700; }' +
    '.risks-assessment-matrix .risk-zone-low, .risks-risk-class-table .risk-zone-low { background: #c5e0b3; mso-background: #c5e0b3; }' +
    '.risks-assessment-matrix .risk-zone-mid, .risks-risk-class-table .risk-zone-mid { background: #ffc000; mso-background: #ffc000; }' +
    '.risks-assessment-matrix .risk-zone-high, .risks-risk-class-table .risk-zone-high { background: #ff0000; mso-background: #ff0000; color: #000; }' +
    '.risks-assessment-matrix .ram-prob-label, .risks-assessment-matrix .ram-sev-label { background: #ffcc99; mso-background: #ffcc99; }' +
    '.risks-assessment-matrix .ram-head { background: #f8cbad; mso-background: #f8cbad; }' +
    '.risks-assessment-matrix td, .risks-assessment-matrix th { border: 1px solid #000; }' +
    '.risks-card-field-hint { font-size: 8.5pt; text-align: center; }' +
    '.risks-card-field-line {' +
    '  border: none; border-bottom: 1px solid #000; text-align: center; font-weight: 700;' +
    '  min-height: 14pt; line-height: 14pt; }' +
    /* Не повторять шапку таблицы на каждой странице Word */
    'thead { display: table-row-group; }' +
    '.sout-info-table thead { display: table-row-group; }' +
    '.sout-info-table { page-break-inside: auto; break-inside: auto; mso-table-layout:fixed; table-layout:fixed; width:100%; max-width:100%; }' +
    '.sout-info-table thead { page-break-inside: avoid; break-inside: avoid; page-break-after: avoid; }' +
    '.sout-info-table tbody tr { page-break-inside: auto; break-inside: auto; page-break-before: auto; page-break-after: auto; }' +
    '.sout-info-table col { mso-width-source: userset; }' +
    '.risks-wp-table-2col th:first-child, .risks-wp-table-2col td:first-child { width: 12%; text-align: center; }' +
    '.risks-wp-table-2col th:nth-child(2), .risks-wp-table-2col td:nth-child(2) { width: 88%; }' +
    '.risks-wp-table-measures th:nth-child(1), .risks-wp-table-measures td:nth-child(1) { width: 10%; text-align: center; }' +
    '.risks-wp-table-measures th:nth-child(2), .risks-wp-table-measures td:nth-child(2) { width: 42%; }' +
    '.risks-wp-table-measures th:nth-child(3), .risks-wp-table-measures td:nth-child(3) { width: 34%; }' +
    '.risks-wp-table-measures th:nth-child(4), .risks-wp-table-measures td:nth-child(4) { width: 14%; text-align: center; }' +
    /* Карта ОПР: колонка «Меры управления» шире (как в предпросмотре) */
    '.risks-card-table { table-layout: fixed; width: 100%; }' +
    '.risks-card-table th, .risks-card-table td { font-size: 8.5pt; padding: 2px 3px; }' +
    '.risks-card-table th:nth-child(1), .risks-card-table td:nth-child(1) { width: 6%; text-align: center; }' +
    '.risks-card-table th:nth-child(2), .risks-card-table td:nth-child(2) { width: 16%; }' +
    '.risks-card-table th:nth-child(3), .risks-card-table td:nth-child(3) { width: 12%; }' +
    '.risks-card-table th:nth-child(4), .risks-card-table td:nth-child(4) { width: 10%; }' +
    '.risks-card-table th:nth-child(5), .risks-card-table td:nth-child(5) { width: 8%; }' +
    '.risks-card-table th:nth-child(6), .risks-card-table td:nth-child(6) { width: 8%; text-align: center; }' +
    '.risks-card-table th:nth-child(7), .risks-card-table td:nth-child(7) { width: 32%; }' +
    '.risks-card-table th:nth-child(8), .risks-card-table td:nth-child(8) { width: 8%; }' +
    '.risks-wp-title, .risks-order-title, .risks-method-h, .risks-print-main-title {' +
    '  font-size: 14pt; font-weight: 700; text-align: center; margin: 0 0 10pt; }' +
    '.risks-wp-org-center, .risks-order-org, .risks-print-org-center { text-align: center; font-weight: 700; margin: 0 0 10pt; }' +
    '.risks-order-preamble { text-align: justify; }' +
    '.risks-order-command { text-align: center; font-weight: 700; }' +
    '.risks-order-letterhead { text-align: center; margin: 0 0 12pt; }' +
    '.risks-order-letterhead-name, .risks-order-letterhead-role {' +
    '  text-align: center; font-weight: 700; font-size: 12pt; margin: 0 0 4pt; }' +
    '.risks-order-letterhead-bank { text-align: center; font-size: 9pt; margin: 4pt 0; }' +
    '.risks-order-letterhead-rule, hr.risks-word-hr-double {' +
    '  border: none; border-top: 3px double #000; border-bottom: none;' +
    '  height: 0; margin: 2pt 0 8pt; padding: 0; }' +
    '.risks-order-letterhead-rule-double {' +
    '  border: none; border-top: 3px double #000; border-bottom: none;' +
    '  height: 0; margin: 2pt 0 10pt; padding: 0; }' +
    'hr.risks-word-hr-title {' +
    '  border: none; border-top: 1.5pt solid #000; border-bottom: none;' +
    '  height: 0; margin: 0 0 10pt; padding: 0; }' +
    'hr.risks-order-sign-rule, .risks-order-sign-rule {' +
    '  border: none !important; border-top: 1pt solid #000 !important; border-bottom: none !important;' +
    '  height: 0 !important; margin: 14pt 0 10pt !important; padding: 0 !important; width: 100% !important; }' +
    '.risks-order-date, .risks-order-city, .risks-order-number,' +
    ' .risks-order-subject, .risks-order-preamble, .risks-order-inline,' +
    ' .risks-order-sign-left, .risks-order-sign-right, .risks-order-filled {' +
    '  border: none; text-decoration: none; background: transparent; }' +
    /* Полки у даты/города/номера — не у темы, преамбулы, пунктов и подписи с линией сверху. */
    '.risks-order-sheet .risks-order-date, .risks-order-sheet .risks-order-city,' +
    ' .risks-order-sheet .risks-order-number, .risks-order-sheet .risks-order-inline {' +
    '  border: none; border-bottom: 0.75pt solid #000;' +
    '  text-decoration: none; padding-bottom: 1pt; background: transparent; }' +
    '.risks-order-sheet .risks-order-subject, .risks-order-sheet .risks-order-preamble,' +
    ' .risks-order-sheet .risks-order-points li, .risks-order-sheet .risks-order-sublist li,' +
    ' .risks-order-sheet .risks-order-command,' +
    ' .risks-order-sheet .risks-order-sign-left, .risks-order-sheet .risks-order-sign-right {' +
    '  border: none !important; border-bottom: none !important;' +
    '  background: transparent !important; text-decoration: none !important; }' +
    '.risks-order-title {' +
    '  border: none !important; border-bottom: none !important; text-decoration: none !important; }' +
    '.risks-order-meta, .risks-order-meta-2, .risks-order-meta-3 { margin: 10pt 0; }' +
    '.risks-order-date { text-align: left; }' +
    '.risks-order-city { text-align: right; }' +
    '.risks-order-subject { text-align: center; font-weight: 400; margin: 8pt 0; }' +
    '.risks-order-points-word { margin: 0 0 10pt; }' +
    '.risks-order-point, .risks-order-bullet {' +
    '  text-align: justify; border: none; background: transparent; }' +
    /* Старые ol/ul на случай, если flatten не сработал */
    '.risks-order-points { margin: 0 0 14pt 0; padding-left: 21pt; list-style-position: outside; }' +
    '.risks-order-points > li { margin: 8pt 0; text-align: justify; text-indent: 0; padding-left: 0; }' +
    '.risks-order-sublist { margin: 4pt 0 2pt; padding-left: 18pt; list-style-type: disc; list-style-position: outside; }' +
    '.risks-order-sublist > li { text-indent: 0; padding-left: 0; }' +
    '.risks-wp-org-center, .risks-print-org-center { text-align: center; font-weight: 700; margin: 0 0 10pt; }' +
    '.risks-wp-col-n { text-align: center; }' +
    '.risks-wp-empty { text-align: center; font-style: italic; }' +
    '.risks-chair-label { font-size: 11pt; }' +
    '.risks-chair-hint { font-size: 8.5pt; text-align: center; }' +
    '.risks-chair-line, .risks-chair-value { border-bottom: 1px solid #000; min-height: 14pt; text-align: center; }' +
    '.risks-print-edit-hint, .ppe-no-print { display: none; }' +
    /* ВАЖНО: не ставить mso-break-type:section-break на контент — Word
       расползает документ «по строке на страницу». Разрыв секции — только
       между WordSectionN при смене ориентации. */
    /* Разрыв страницы — только через явный .risks-word-page-break / .risks-page-break.
       НЕ ставить page-break-before на .sout-commission-appendix / блоки контента —
       вместе с маркером Word даёт пустые страницы и «строка на лист». */
    '.risks-page-break, .risks-word-page-break {' +
    '  page-break-before: always; height: 0; margin: 0; padding: 0; border: none; font-size: 1pt; line-height: 1pt; }' +
    '.sout-commission-appendix { page-break-before: auto; padding-top: 12pt; }' +
    '.sout-commission-appendix-ref { text-align: right; margin: 0 0 18pt; font-size: 11pt; line-height: 1.35; }' +
    '.sout-commission-appendix-title { text-align: center; font-size: 12pt; font-weight: 700; margin: 0 0 22pt; }' +
    /* Росписи: разрыв только через .risks-page-break → br (не дублировать page-break на листе) */
    '.risks-card-acq-sheet {' +
    '  page-break-before: auto; break-before: auto;' +
    '  page-break-after: auto; break-after: auto;' +
    '  min-height: 0 !important; height: auto !important; max-height: none !important;' +
    '  border: none !important; padding: 0 !important; margin: 0 !important; }' +
    '.risks-card-acq-spacer { height: 10pt; line-height: 10pt; font-size: 10pt; margin: 0 0 4pt; }' +
    /* «Карту составил» не отрывать от таблицы на пустой лист; небольшой отступ от сетки */
    '.risks-card-compiled {' +
    '  page-break-before: avoid; page-break-inside: avoid; page-break-after: avoid;' +
    '  margin-top: 12pt !important; padding-top: 6pt; }' +
    '.risks-card-page-foot, .risks-card-acq-foot {' +
    '  page-break-before: avoid; page-break-inside: avoid; page-break-after: avoid; }' +
    '.risks-card-acq-foot { margin-top: 18pt; font-size: 9pt; color: #000; }' +
    /* Сброс «листа» превью — иначе min-height:297mm растягивает Word */
    '.risks-a4-page, .risks-a4-portrait, .risks-a4-landscape, .risks-a4-sheet,' +
    ' .risks-order-sheet, .sout-commission-order-sheet, .sout-commission-appendix,' +
    ' .risks-card-sheet, .risks-workplaces-sheet, .risks-method-sheet, .risks-method-cover,' +
    ' .sout-a4-page, .sout-a4-portrait, .sout-a4-landscape, .sout-a4-info, .sout-info-doc {' +
    '  min-height: 0 !important; height: auto !important; max-height: none !important;' +
    '  width: 100% !important; margin: 0 !important; box-shadow: none !important; }' +
    'table tr, table td, table th { page-break-before: auto; page-break-after: auto; }' +
    '.sout-commission-schedule tr, .sout-commission-schedule td, .sout-commission-schedule th,' +
    ' .risks-wp-table tr, .risks-wp-table td {' +
    '  page-break-before: auto !important; page-break-after: auto !important; }' +
    '.sout-info-title { text-align: center; font-weight: 700; font-size: 12pt; margin: 0 0 6pt; color: #000; }' +
    '.sout-info-doc, .sout-info-table, .sout-info-table th, .sout-info-table td { color: #000; }' +
    '.sout-info-header { margin: 0 0 6pt; font-size: 9.5pt; line-height: 1.3; color: #000; }' +
    '.sout-info-meta-row { margin: 0 0 2pt; }' +
    '.sout-info-meta-label { font-size: 9.5pt; color: #000; }' +
    '.sout-info-meta-value { border-bottom: 1px solid #000; color: #000; padding: 0 3px; }' +
    '.sout-info-meta-word { width: 100%; border: none; border-collapse: collapse; margin: 0 0 4pt; }' +
    '.sout-info-meta-word td {' +
    '  border: none; color: #000; font-family: Arial, Tahoma, sans-serif; font-size: 9.5pt;' +
    '  vertical-align: bottom; padding: 0 3px 1px; }' +
    '.sout-info-meta-word td.sout-info-meta-shelf {' +
    '  border: none; border-bottom: 1px solid #000;' +
    '  mso-border-bottom-alt: solid windowtext .5pt; }' +
    '.sout-info-meta-word p { margin: 0; padding: 0; color: #000; font-size: 9.5pt; }' +
    /* Сетка: border на ячейках + border=1 в HTML (полки). */
    '.sout-info-table { width: 100%; border-collapse: collapse; border-spacing: 0;' +
    '  border: 1px solid #000; outline: none; table-layout: fixed; font-size: 8.5pt; }' +
    '.sout-info-table th, .sout-info-table td {' +
    '  border: 1px solid #000; padding: 2px 3px; vertical-align: middle; font-size: 8.5pt; color: #000;' +
    '  background-clip: padding-box; }' +
    '.sout-info-meta-shelf-tbl { width: 100%; border: none !important; border-collapse: collapse; }' +
    '.sout-info-meta-shelf-tbl td {' +
    '  border: none !important; border-bottom: 1px solid #000 !important;' +
    '  mso-border-bottom-alt: solid windowtext .5pt; padding: 0 3px 1px; }' +
    '.risks-word-layout td { border: none !important; }' +
    '.sout-info-meta-word td.sout-info-meta-shelf {' +
    '  border: none !important; border-bottom: 1px solid #000 !important;' +
    '  mso-border-bottom-alt: solid windowtext .5pt; }' +
    /* Полки значений шапки внутри layout-таблицы */
    '.sout-info-meta-row .risks-word-layout td .sout-info-meta-value,' +
    ' .sout-info-meta-value { border: none; }' +
    '.sout-info-table thead th,' +
    ' .sout-info-table tr.risks-export-head-row th {' +
    '  background: #f0f0f0; color: #000; font-weight: 400; text-align: center; }' +
    '.sout-info-ok, td.sout-info-ok, th.sout-info-ok { background: #fff; color: #000; }' +
    '.sout-info-c { text-align: center; color: #000; }' +
    '.sout-info-v { text-align: center; vertical-align: bottom; color: #000; background: #f0f0f0; }' +
    '.sout-info-v span, .sout-info-v p.sout-info-vert, .sout-info-v .sout-info-vert {' +
    '  display: block; color: #000 !important; font-size: 5.5pt; margin: 0; padding: 0; line-height: 0.88;' +
    '  font-family: Arial, Tahoma, sans-serif; text-align: center; mso-line-height-rule: exactly;' +
    '  writing-mode: horizontal-tb; transform: none; white-space: normal; background: transparent; }' +
    '.sout-info-h { width: 8.5%; text-align: center; vertical-align: middle; font-size: 6.5pt; }' +
    '.sout-info-equip { width: 16.5%; text-align: center; vertical-align: middle; font-size: 5.5pt; line-height: 1.1; }' +
    '.sout-info-ok, td.sout-info-ok, th.sout-info-ok, .sout-info-hl, mark { background: transparent !important; }' +
    '.sout-info-table { width: 100% !important; max-width: 100% !important; table-layout: fixed; mso-table-layout: fixed; }' +
    '.sout-info-table thead th { background: #f0f0f0 !important; color: #000 !important; }' +
    '.sout-info-col-numbers th { background: #fff !important; color: #000 !important; }' +
    '.sout-info-table--form thead tr:first-child th[rowspan],' +
    ' .sout-info-table--form tr.sout-info-head-row th[rowspan] { height: 150pt; vertical-align: bottom; }' +
    '.sout-info-table--form thead tr:first-child th[colspan],' +
    ' .sout-info-table--form tr.sout-info-head-row th[colspan] { height: auto; vertical-align: middle; }' +
    '.sout-info-table--form thead tr:nth-child(2) th,' +
    ' .sout-info-table--form tr.sout-info-head-row + tr th { height: 112pt; }' +
    '.sout-info-table tbody td { padding: 2px 3px; font-size: 8.5pt; }' +
    '.sout-info-table thead th { font-size: 7pt; }' +
    /* Хвост: крупнее, но одна альбомная страница (снизу был запас) */
    '.sout-info-rules { white-space: normal; border: none; padding: 0;' +
    '  margin: 0 0 6pt; font-size: 10.5pt; background: transparent; color: #000; line-height: 1.28; }' +
    '.sout-info-rules p { margin: 0 0 2pt; padding: 0; page-break-before: auto; page-break-after: auto; }' +
    '.sout-info-rules-lead { display: block; font-style: italic; text-decoration: underline;' +
    '  margin: 0 0 3pt; font-size: 10.5pt; line-height: 1.28; }' +
    /* Хвост: в Word уходит в книжную секцию; не форсировать page-break здесь */
    '.sout-info-tail { page-break-before: auto; page-break-inside: auto; margin: 0; padding: 0;' +
    '  font-size: 10.5pt; line-height: 1.2; }' +
    '.sout-info-hl { background: transparent; font-weight: inherit; }' +
    '.sout-info-summary, .sout-info-commission { width: 100%; border-collapse: collapse; border: none;' +
    '  font-size: 10.5pt; margin: 0 0 6pt; page-break-inside: auto; }' +
    '.sout-info-summary th, .sout-info-summary td, .sout-info-commission th, .sout-info-commission td {' +
    '  border: 1px solid #000; padding: 3px 5px; color: #000; font-size: 10.5pt; }' +
    '.sout-info-summary thead th, .sout-info-commission thead th,' +
    ' .sout-info-summary tr.risks-export-head-row th, .sout-info-commission tr.risks-export-head-row th {' +
    '  background: #e8e8e8 !important; color: #000 !important; text-align: center; font-weight: 400; font-size: 10pt; }' +
    '.sout-info-summary-lead, .sout-info-summary-title { margin: 4pt 0 2pt; font-size: 10.5pt; }' +
    '.sout-info-summary td.sout-info-c { font-size: 12pt; font-weight: 700; }' +
    '.sout-info-note { display: none; }' +
    '.sout-info-sign-role, .sout-info-sign-org { font-size: 10.5pt; }' +
    '.sout-info-sign-mp { display: block; font-size: 10pt; }' +
    '.sout-info-sign-shelf {' +
    '  border: none; border-bottom: 1px solid #000; margin: 6pt 0 1pt; padding: 0;' +
    '  min-height: 12pt; height: 12pt; line-height: 12pt; width: 85%; }' +
    '.sout-info-signs { margin-top: 6pt; page-break-inside: auto; }';

  /** Сброс preview-рамок: иначе PDF = узкая карточка по центру или обрезанная таблица. */
  var PDF_A4_CSS =
    'html, body { margin: 0 !important; padding: 0 !important; background: #fff !important; }' +
    /* Не обнулять padding у .sout-preview-doc-wrap — на нём поля PDF (fitCloneForPdf / mountSection). */
    '.sout-preview-doc-wrap, .sout-preview-shell { display: block !important;' +
    '  margin: 0 !important; justify-content: flex-start !important; align-items: stretch !important; }' +
    '.sout-preview-shell { padding: 0 !important; }' +
    '.sout-eq-doc, .med-print-sheet, .risks-a4-sheet, .risks-workplaces-sheet, .risks-order-sheet,' +
    ' .risks-schedule-sheet, .risks-card-sheet, .risks-a4-landscape, .risks-a4-portrait,' +
    ' .sout-a4-page, .sout-a4-portrait, .sout-a4-landscape, .sout-a4-info, .sout-info-doc,' +
    ' .sout-commission-order-sheet, .sout-commission-appendix {' +
    '  width: 100% !important; max-width: none !important; min-width: 0 !important;' +
    '  margin: 0 !important; padding: 0 !important; box-shadow: none !important;' +
    '  border: none !important; outline: none !important; background: #fff !important;' +
    '  min-height: 0 !important; height: auto !important; box-sizing: border-box !important; }' +
    '.sout-commission-appendix { padding-top: 12pt !important; page-break-before: always !important; }' +
    '.sout-commission-appendix-ref { text-align: right !important; margin: 0 0 18pt !important; }' +
    '.sout-commission-appendix-title { text-align: center !important; font-weight: 700 !important; margin: 0 0 22pt !important; }' +
    '.risks-card-acq-sheet { page-break-before: auto !important; break-before: auto !important; }' +
    '.risks-card-acq-spacer { height: 14pt !important; line-height: 14pt !important; margin: 0 0 6pt !important; }' +
    '.risks-print-order-body, .risks-order-sheet, .risks-preview-mode-order .risks-order-sheet {' +
    '  max-width: none !important; width: 100% !important; }' +
    'table { width: 100% !important; max-width: 100% !important; border-collapse: collapse !important;' +
    '  table-layout: fixed !important; }' +
    'th, td { word-wrap: break-word !important; overflow-wrap: break-word !important; }' +
    '.risks-wp-table, .risks-print-table, .ppe-doc-table {' +
    '  width: 100% !important; border-collapse: collapse !important; table-layout: fixed !important; }' +
    '.risks-wp-table-2col th:first-child, .risks-wp-table-2col td:first-child {' +
    '  width: 12% !important; text-align: center !important; }' +
    '.risks-wp-table-2col th:nth-child(2), .risks-wp-table-2col td:nth-child(2) {' +
    '  width: 88% !important; text-align: left !important; }' +
    '.risks-wp-table-measures th:nth-child(1), .risks-wp-table-measures td:nth-child(1) {' +
    '  width: 10% !important; text-align: center !important; }' +
    '.risks-wp-table-measures th:nth-child(2), .risks-wp-table-measures td:nth-child(2) {' +
    '  width: 42% !important; text-align: left !important; }' +
    '.risks-wp-table-measures th:nth-child(3), .risks-wp-table-measures td:nth-child(3) {' +
    '  width: 34% !important; text-align: left !important; }' +
    '.risks-wp-table-measures th:nth-child(4), .risks-wp-table-measures td:nth-child(4) {' +
    '  width: 14% !important; text-align: center !important; }' +
    '.risks-wp-table-measures th, .risks-wp-table-measures td {' +
    '  font-size: 9pt !important; padding: 2px 4px !important; }' +
    '.risks-card-table { table-layout: fixed !important; width: 100% !important; }' +
    '.risks-card-table th, .risks-card-table td {' +
    '  font-size: 8pt !important; padding: 2px 3px !important;' +
    '  word-wrap: break-word !important; overflow-wrap: break-word !important; }' +
    '.risks-card-table th:nth-child(1), .risks-card-table td:nth-child(1) { width: 6% !important; text-align: center !important; }' +
    '.risks-card-table th:nth-child(2), .risks-card-table td:nth-child(2) { width: 16% !important; }' +
    '.risks-card-table th:nth-child(3), .risks-card-table td:nth-child(3) { width: 12% !important; }' +
    '.risks-card-table th:nth-child(4), .risks-card-table td:nth-child(4) { width: 10% !important; }' +
    '.risks-card-table th:nth-child(5), .risks-card-table td:nth-child(5) { width: 8% !important; }' +
    '.risks-card-table th:nth-child(6), .risks-card-table td:nth-child(6) { width: 8% !important; text-align: center !important; }' +
    '.risks-card-table th:nth-child(7), .risks-card-table td:nth-child(7) { width: 32% !important; }' +
    '.risks-card-table th:nth-child(8), .risks-card-table td:nth-child(8) { width: 8% !important; }' +
    /* PDF: без внешней рамки таблицы — иначе на разрыве страницы торчат «хвосты» слева/справа */
    '.risks-card-table, .risks-print-table, .ppe-doc-table, .risks-wp-table {' +
    '  border-collapse: collapse !important; border-spacing: 0 !important;' +
    '  border: none !important; outline: none !important; }' +
    '.risks-card-table td, .risks-card-table th,' +
    ' .risks-print-table td, .risks-print-table th,' +
    ' .ppe-doc-table td, .ppe-doc-table th {' +
    '  border: 1px solid #000 !important; background-clip: padding-box !important; }' +
    '.risks-card-acq-table, .risks-card-acq-table td, .risks-card-acq-sheet {' +
    '  border: none !important; box-shadow: none !important; outline: none !important; }' +
    '.risks-wp-table th, .risks-wp-table td, .risks-print-table th, .risks-print-table td,' +
    ' .ppe-doc-table th, .ppe-doc-table td {' +
    '  border: 1px solid #000 !important; padding: 3px 5px !important;' +
    '  font-family: "Times New Roman", Times, serif !important; font-size: 10pt !important; }' +
    '.risks-wp-table thead th, .risks-print-table thead th,' +
    ' .risks-wp-table tr.risks-export-head-row th, .risks-print-table tr.risks-export-head-row th,' +
    ' .ppe-doc-table tr.risks-export-head-row th, .risks-card-table tr.risks-export-head-row th {' +
    '  background: #2f6b3a !important; color: #fff !important; text-align: center !important; }' +
    'thead, .risks-print-table thead, .risks-card-table thead, .ppe-doc-table thead,' +
    ' .risks-wp-table thead, .sout-info-table thead { display: table-row-group !important; }' +
    '.sout-info-table thead tr, .sout-info-table tbody tr {' +
    '  page-break-inside: avoid !important; break-inside: avoid !important; }' +
    '.risks-wp-title, .risks-order-title, .risks-print-main-title {' +
    '  text-align: center !important; font-size: 13pt !important; font-weight: 700 !important;' +
    '  margin: 0 0 8pt !important; }' +
    '.risks-wp-org-center { text-align: center !important; font-weight: 700 !important; margin: 0 0 8pt !important; }' +
    '.risks-order-letterhead { text-align: center !important; margin: 0 0 10pt !important; }' +
    '.risks-order-letterhead-name { font-weight: 700 !important; font-size: 12pt !important; }' +
    '.risks-order-letterhead-bank { font-size: 9pt !important; }' +
    '.risks-order-letterhead-rule, .risks-order-letterhead-rule-double {' +
    '  border: none !important; border-top: 3px double #000 !important;' +
    '  height: 0 !important; margin: 2pt 0 8pt !important; }' +
    /* Стили приказа (.risks-order-*) — в WORD_DOC_CSS; здесь только PDF-оболочка. */
    '.risks-print-edit-hint, .ppe-no-print, .sout-preview-toolbar { display: none !important; }' +
    '.risks-page-break { page-break-before: always !important; break-before: page !important;' +
    '  height: 0 !important; margin: 0 !important; padding: 0 !important; border: none !important; }' +
    '.sout-info-table { width: 100% !important; table-layout: fixed !important; font-size: 8.5pt !important; color: #000 !important;' +
    '  border: none !important; outline: none !important; border-collapse: collapse !important; border-spacing: 0 !important; }' +
    '.sout-info-table th, .sout-info-table td {' +
    '  border: 1px solid #000 !important; padding: 2px 3px !important; color: #000 !important;' +
    '  font-size: 8.5pt !important; vertical-align: middle !important; background-clip: padding-box !important; }' +
    '.sout-info-table tbody td { font-size: 8.5pt !important; }' +
    '.sout-info-table--form thead th { font-size: 6.5pt !important; line-height: 1.05 !important; color: #000 !important; }' +
    '.sout-info-table--form thead tr:first-child th[rowspan] {' +
    '  height: 200px !important; max-height: 200px !important; overflow: visible !important; vertical-align: bottom !important; }' +
    /* PDF: столбик букв (<br>), без writing-mode */
    '.sout-info-v { color: #000 !important; text-align: center !important; vertical-align: bottom !important;' +
    '  overflow: visible !important; background: #f0f0f0 !important; }' +
    '.sout-info-v span, .sout-info-v p.sout-info-vert, .sout-info-v .sout-info-vert {' +
    '  display: block !important; margin: 0 auto !important; padding: 0 !important;' +
    '  writing-mode: horizontal-tb !important; transform: none !important;' +
    '  font-size: 5.5pt !important; line-height: 0.9 !important; color: #000 !important;' +
    '  text-align: center !important; max-height: none !important; overflow: visible !important;' +
    '  white-space: normal !important; background: transparent !important; }' +
    '.sout-info-table--form thead tr:nth-child(2) th {' +
    '  height: 155px !important; max-height: 155px !important; background: #f0f0f0 !important; color: #000 !important; }' +
    '.sout-info-table--form thead tr:nth-child(2) .sout-info-v span,' +
    ' .sout-info-table--form thead tr:nth-child(2) .sout-info-v .sout-info-vert {' +
    '  font-size: 5pt !important; line-height: 0.9 !important; }' +
    '.sout-info-ok, .sout-info-hl, mark { background: transparent !important; }' +
    '.sout-info-table thead th { background: #f0f0f0 !important; color: #000 !important; }' +
    '.sout-info-col-numbers th { background: #fff !important; color: #000 !important; }' +
    /* ФИО / СНИЛС / оборудование — горизонтально */
    '.sout-info-h, .sout-info-equip {' +
    '  writing-mode: horizontal-tb !important; transform: none !important;' +
    '  white-space: normal !important; vertical-align: middle !important; text-align: center !important;' +
    '  font-size: 7pt !important; line-height: 1.15 !important; }' +
    '.sout-info-h { width: 9% !important; }' +
    '.sout-info-equip, .sout-info-table--21 thead th.sout-info-equip,' +
    ' .sout-info-table--21 td:nth-child(12) { width: 16% !important; }' +
    '.sout-info-ok, .sout-info-table td.sout-info-ok, .sout-info-table thead th.sout-info-ok {' +
    '  background: #fff !important; color: #000 !important; }' +
    /* Хвост СОУТ-инфо: крупный текст, одна альбомная страница */
    /* PDF: не avoid на строки — иначе белые разрывы между кусками таблицы. */
    '.sout-info-table, .sout-info-table tbody, .sout-info-table tbody tr,' +
    ' .sout-info-table tbody td, .sout-info-table tbody th {' +
    '  page-break-inside: auto !important; break-inside: auto !important;' +
    '  page-break-before: auto !important; page-break-after: auto !important; }' +
    '.sout-info-rules {' +
    '  white-space: normal !important; border: none !important;' +
    '  padding: 0 !important; margin: 0 0 6pt !important;' +
    '  font-size: 10pt !important; background: transparent !important; color: #000 !important;' +
    '  line-height: 1.22 !important; font-family: "Times New Roman", Times, serif !important; }' +
    '.sout-info-rules-lead {' +
    '  display: block !important; font-style: italic !important; text-decoration: underline !important;' +
    '  margin: 0 0 3pt !important; font-size: 10pt !important; line-height: 1.22 !important;' +
    '  font-family: "Times New Roman", Times, serif !important; background: transparent !important; }' +
    '.sout-info-note { display: none !important; }' +
    '.sout-info-tail {' +
    '  page-break-before: always !important; break-before: page !important;' +
    '  page-break-inside: auto !important; break-inside: auto !important;' +
    '  margin: 0 !important; padding: 0 !important; font-size: 10pt !important; line-height: 1.22 !important; }' +
    '.sout-info-summary, .sout-info-commission {' +
    '  width: 100% !important; border-collapse: collapse !important; border: none !important;' +
    '  font-size: 10pt !important; margin: 0 0 8pt !important; table-layout: fixed !important;' +
    '  page-break-inside: avoid !important; break-inside: avoid !important; }' +
    '.sout-info-summary th, .sout-info-summary td, .sout-info-commission th, .sout-info-commission td {' +
    '  border: 1px solid #000 !important; padding: 4px 6px !important; color: #000 !important;' +
    '  font-size: 10pt !important; background: #fff !important; }' +
    '.sout-info-summary thead th, .sout-info-commission thead th,' +
    ' .sout-info-summary tr.risks-export-head-row th, .sout-info-commission tr.risks-export-head-row th {' +
    '  background: #e8e8e8 !important; color: #000 !important; font-weight: 400 !important;' +
    '  text-align: center !important; font-size: 9.5pt !important; }' +
    '.sout-info-summary-lead, .sout-info-summary-title {' +
    '  margin: 6pt 0 3pt !important; font-size: 10pt !important; font-weight: 400 !important; color: #000 !important;' +
    '  font-family: "Times New Roman", Times, serif !important; background: transparent !important; }' +
    '.sout-info-summary-lead { text-decoration: underline !important; }' +
    '.sout-info-summary td.sout-info-c { font-size: 12pt !important; font-weight: 700 !important; }' +
    '.sout-info-hl, .sout-info-ok, mark, .sout-info-rules mark, .sout-info-rules span[style*="background"] {' +
    '  background: #fff !important; background-color: #fff !important; font-weight: inherit !important; }' +
    '.sout-info-signs { width: 100% !important; margin-top: 10pt !important; page-break-inside: avoid !important; }' +
    '.sout-info-sign-role, .sout-info-sign-org { font-size: 11pt !important; }' +
    '.sout-info-sign-mp { display: block !important; font-size: 10pt !important; }' +
    '.sout-info-meta-line { display: flex !important; gap: 8px !important; margin: 0 0 3px !important; }' +
    '.sout-info-meta-label { min-width: 220px !important; flex: 0 0 220px !important; color: #000 !important; }' +
    '.sout-info-meta-value { flex: 1 !important; border-bottom: 1px solid #000 !important; color: #000 !important; }' +
    /* Методика: не форсировать avoid на все p — html2pdf из‑за этого клеит пустые страницы.
       Разрыв контролируем через pagebreak.avoid в downloadPdf. */
    '.risks-method-h, .risks-method-table-caption, .risks-regulation-running-header,' +
    ' table.risks-assessment-matrix, table.risks-risk-class-table, table.risks-method-doc-table {' +
    '  page-break-inside: avoid !important; break-inside: avoid !important; }' +
    '.risks-method-h, .risks-method-table-caption {' +
    '  page-break-after: avoid !important; break-after: avoid !important; }';

  /** Высота полезной области A4 (px @96dpi) при заданных полях в мм. */
  function a4ContentHeightPx(landscape, marginMm) {
    var pageMm = landscape ? 210 : 297;
    var m = marginMm || { top: 15, bottom: 15 };
    var usableMm = pageMm - (m.top || 0) - (m.bottom || 0);
    return Math.max(200, Math.round(usableMm * (96 / 25.4)));
  }

  /**
   * Нарезает длинный поток (методика) на страницы по блокам:
   * абзац/заголовок/таблица целиком переносятся на следующую страницу,
   * сверху и снизу остаётся поле (не «в край»).
   */
  function paginateFlowBlocksForPdf(sec, pageContentHpx) {
    var body = sec.querySelector('.risks-regulation-body')
      || sec.querySelector('.risks-method-sheet')
      || sec;
    if (!body || !body.children || body.children.length < 3) return [sec];

    var blocks = Array.prototype.slice.call(body.children);
    if (blocks.length < 3) return [sec];

    function blockHeight(el) {
      var h = el.offsetHeight || 0;
      if (!h) {
        var r = el.getBoundingClientRect();
        h = r.height || 0;
      }
      // Учесть нижний margin (offsetHeight его не включает).
      try {
        var cs = window.getComputedStyle(el);
        h += (parseFloat(cs.marginTop) || 0) + (parseFloat(cs.marginBottom) || 0);
      } catch (e) { /* ignore */ }
      return Math.max(h, 8);
    }

    function keepWithNext(el) {
      if (!el) return false;
      var cls = el.className || '';
      return /risks-method-h|risks-method-table-caption|risks-regulation-running-header/i.test(cls)
        || /^H[1-4]$/i.test(el.tagName || '');
    }

    var heights = blocks.map(blockHeight);
    var pages = [];
    var start = 0;
    var used = 0;
    var i;
    for (i = 0; i < blocks.length; i++) {
      var h = heights[i];
      if (keepWithNext(blocks[i]) && i + 1 < blocks.length) {
        h += heights[i + 1];
      }
      if (used > 0 && used + h > pageContentHpx) {
        pages.push({ from: start, to: i });
        start = i;
        used = 0;
        h = heights[i];
        if (keepWithNext(blocks[i]) && i + 1 < blocks.length) {
          h += heights[i + 1];
        }
      }
      used += heights[i];
    }
    pages.push({ from: start, to: blocks.length });

    if (pages.length <= 1) return [sec];

    var sheetClass = 'risks-method-sheet risks-regulation-sheet';
    var sheet = sec.querySelector('.risks-method-sheet');
    if (sheet && sheet.className) sheetClass = sheet.className;
    else if (/\brisks-method-sheet\b/.test(sec.className || '')) sheetClass = sec.className;

    var result = [];
    pages.forEach(function (range) {
      var wrap = document.createElement('div');
      wrap.className = 'risks-pdf-flow-page';
      var inner = document.createElement('div');
      inner.className = sheetClass;
      var bodyWrap = document.createElement('div');
      bodyWrap.className = 'risks-regulation-body';
      for (var j = range.from; j < range.to; j++) {
        bodyWrap.appendChild(blocks[j]);
      }
      inner.appendChild(bodyWrap);
      wrap.appendChild(inner);
      result.push(wrap);
    });
    return result;
  }

  function sanitizeFileName(name) {
    var base = String(name || 'document')
      .replace(/[<>:"/\\|?*\u0000-\u001f]+/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();
    if (!base) base = 'document';
    if (base.length > 120) base = base.slice(0, 120).trim();
    return base;
  }

  /**
   * HTML→Word: убрать то, из‑за чего MS Word показывает «файл повреждён / восстановить».
   */
  function sanitizeWordHtml(html) {
    return String(html || '')
      .replace(/\sdata-[a-z0-9_-]+(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+))?/gi, '')
      .replace(/\scontenteditable(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+))?/gi, '')
      .replace(/\sspellcheck(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+))?/gi, '')
      .replace(/<br\s*\/?>/gi, '<br>')
      .replace(/\sclass="\s*"/gi, '')
      .replace(/\sid="\s*"/gi, '')
      .replace(/<!--[\s\S]*?-->/g, '');
  }

  function resolveSource(selector) {
    if (!selector) {
      return document.querySelector('.sout-preview-doc-wrap .risks-a4-sheet')
        || document.querySelector('.sout-preview-doc-wrap .sout-eq-doc')
        || document.querySelector('.sout-preview-doc-wrap')
        || document.getElementById('sout-preview-print-area')
        || document.getElementById('sout-templates-print-area')
        || document.getElementById('risks-workplace-preview-print-area')
        || document.getElementById('risks-orders-preview-print-area')
        || document.getElementById('risks-preview-print-area')
        || document.getElementById('risks-print-selection-area')
        || document.getElementById('risks-org-doc-print-area');
    }
    if (typeof selector !== 'string') return selector;
    return document.querySelector(selector);
  }

  function kidsToLayoutTable(el, opts) {
    opts = opts || {};
    var kids = Array.prototype.slice.call(el.children);
    if (kids.length === 0) return;
    var table = document.createElement('table');
    table.setAttribute('border', '0');
    table.setAttribute('cellspacing', '0');
    table.setAttribute('cellpadding', '2');
    table.setAttribute('width', '100%');
    table.className = 'risks-word-layout';
    table.style.cssText = 'width:100%;border:none;border-collapse:collapse;margin:0 0 8pt;';
    var tr = document.createElement('tr');
    kids.forEach(function (kid, i) {
      var td = document.createElement('td');
      td.setAttribute('valign', opts.vAlign || 'top');
      td.style.cssText = 'border:none;padding:2px 4px;vertical-align:' + (opts.vAlign || 'top') + ';';
      if (opts.aligns && opts.aligns[i]) {
        td.setAttribute('align', opts.aligns[i]);
        td.style.textAlign = opts.aligns[i];
      }
      if (opts.widths && opts.widths[i]) {
        td.setAttribute('width', opts.widths[i]);
        td.style.width = opts.widths[i];
      }
      td.appendChild(kid);
      tr.appendChild(td);
    });
    table.appendChild(tr);
    while (el.firstChild) el.removeChild(el.firstChild);
    el.appendChild(table);
    el.style.display = 'block';
  }

  /**
   * Полка (подчёркивание) для Word: border-bottom на ячейке layout-таблицы
   * или 1×1 таблица с mso-border — CSS на span Word часто игнорирует.
   */
  function wrapSoutInfoShelf(el, opts) {
    opts = opts || {};
    if (!el) return;
    if (el.getAttribute && el.getAttribute('data-shelf') === '1') return;
    if (el.querySelector && el.querySelector('.sout-info-meta-shelf-tbl')) return;

    var raw = (el.innerHTML || '').trim();
    if (!raw || raw === '&nbsp;' || raw === '\u00a0') raw = '&nbsp;';
    var h = opts.heightPt || 12;

    // Если значение уже в layout-ячейке — полка = нижняя граница этой ячейки (Word так стабильнее).
    var parentTd = el.parentNode;
    while (parentTd && parentTd.tagName !== 'TD' && parentTd.tagName !== 'TH') {
      parentTd = parentTd.parentNode;
    }
    if (
      parentTd
      && parentTd.closest
      && parentTd.closest('table.risks-word-layout')
      && !parentTd.closest('table.sout-info-table')
    ) {
      if (!(el.textContent || '').replace(/\u00a0/g, ' ').trim()) {
        el.innerHTML = '&nbsp;';
      }
      parentTd.setAttribute('valign', 'bottom');
      parentTd.style.setProperty('border', 'none', 'important');
      parentTd.style.setProperty('border-bottom', '1px solid #000', 'important');
      parentTd.style.setProperty('mso-border-bottom-alt', 'solid windowtext .5pt');
      parentTd.style.padding = '0 3px 1px';
      parentTd.style.verticalAlign = 'bottom';
      el.style.border = 'none';
      el.style.borderBottom = 'none';
      el.style.textDecoration = 'none';
      el.setAttribute('data-shelf', '1');
      return;
    }

    var tbl = document.createElement('table');
    tbl.className = 'sout-info-meta-shelf-tbl risks-word-layout';
    tbl.setAttribute('border', '0');
    tbl.setAttribute('cellspacing', '0');
    tbl.setAttribute('cellpadding', '0');
    tbl.setAttribute('width', '100%');
    tbl.style.cssText = 'width:100%;border:none;border-collapse:collapse;margin:0;';
    var tr = document.createElement('tr');
    var td = document.createElement('td');
    td.setAttribute('width', '100%');
    td.setAttribute('valign', 'bottom');
    td.style.cssText =
      'border:none !important;border-bottom:1px solid #000 !important;' +
      'mso-border-bottom-alt:solid windowtext .5pt;' +
      'padding:0 3px 1px;font-size:9.5pt;font-family:Arial,Tahoma,sans-serif;color:#000;' +
      'line-height:' + h + 'pt;mso-line-height-rule:exactly;min-height:' + h + 'pt;';
    td.innerHTML = raw;
    tr.appendChild(td);
    tbl.appendChild(tr);
    while (el.firstChild) el.removeChild(el.firstChild);
    el.appendChild(tbl);
    el.style.border = 'none';
    el.style.textDecoration = 'none';
    el.setAttribute('data-shelf', '1');
  }

  /**
   * Перед Word/PDF: зафиксировать правки из input внутри contenteditable
   * (ФИО из справочника), иначе в .doc уходят пустые ячейки.
   */
  function flushLiveEditors(root) {
    if (!root || !root.querySelectorAll) return;
    try {
      if (document.activeElement && typeof document.activeElement.blur === 'function') {
        document.activeElement.blur();
      }
    } catch (e) { /* ignore */ }

    Array.prototype.forEach.call(
      root.querySelectorAll('[data-override-key] input, [contenteditable] input, td input, th input'),
      function (inp) {
        if (!inp || !inp.parentNode) return;
        var type = String(inp.type || 'text').toLowerCase();
        if (type === 'hidden' || type === 'checkbox' || type === 'radio' || type === 'file' || type === 'button' || type === 'submit') {
          return;
        }
        var parent = inp.parentNode;
        var val = inp.value != null ? String(inp.value) : '';
        parent.textContent = val;
      }
    );
  }

  function flattenCloneInputs(root) {
    if (!root || !root.querySelectorAll) return;
    Array.prototype.forEach.call(root.querySelectorAll('input, textarea'), function (inp) {
      if (!inp || !inp.parentNode) return;
      var type = String(inp.type || 'text').toLowerCase();
      if (type === 'hidden' || type === 'checkbox' || type === 'radio' || type === 'file' || type === 'button' || type === 'submit') {
        if (inp.parentNode) inp.parentNode.removeChild(inp);
        return;
      }
      var parent = inp.parentNode;
      var val = inp.value != null ? String(inp.value) : '';
      parent.textContent = val;
    });
  }

  /** Word/PDF: сетка только с border=1 и inline-стилями. */
  /** Word: фон ячеек только через bgcolor + inline — CSS background часто теряется. */
  var RISK_ZONE_COLORS = {
    'risk-zone-low': '#c5e0b3',
    'risk-zone-mid': '#ffc000',
    'risk-zone-high': '#ff0000'
  };
  var RISK_MATRIX_LABEL_BG = '#ffcc99';
  var RISK_MATRIX_HEAD_BG = '#f8cbad';

  function paintRiskMatrixColorsForWord(root) {
    if (!root || !root.querySelectorAll) return;

    function applyBg(cell, bg) {
      if (!cell || !bg) return;
      cell.setAttribute('bgcolor', bg);
      cell.style.setProperty('background', bg, 'important');
      cell.style.setProperty('background-color', bg, 'important');
      cell.style.color = '#000';
    }

    Array.prototype.forEach.call(
      root.querySelectorAll('table.risks-assessment-matrix, table.risks-risk-class-table'),
      function (table) {
        table.setAttribute('border', '1');
        table.setAttribute('cellspacing', '0');
        table.setAttribute('cellpadding', '3');
        table.setAttribute('width', '100%');
        table.style.cssText =
          'width:100%;border-collapse:collapse;border:1px solid #000;margin:8pt 0;table-layout:fixed;';
      }
    );

    Array.prototype.forEach.call(
      root.querySelectorAll(
        'table.risks-assessment-matrix td, table.risks-assessment-matrix th,' +
        ' table.risks-risk-class-table td, table.risks-risk-class-table th'
      ),
      function (cell) {
        var cls = cell.className || '';
        var isScore = /\bram-score\b/.test(cls);
        var isSevLabel = /\bram-sev-label\b/.test(cls);
        var align = isSevLabel ? 'left' : 'center';
        var zone = null;
        if (/\brisk-zone-low\b/.test(cls)) zone = 'risk-zone-low';
        else if (/\brisk-zone-mid\b/.test(cls)) zone = 'risk-zone-mid';
        else if (/\brisk-zone-high\b/.test(cls)) zone = 'risk-zone-high';

        var bg = '#FFFFFF';
        if (zone) bg = RISK_ZONE_COLORS[zone];
        else if (/\bram-prob-label\b|\bram-sev-label\b/.test(cls)) bg = RISK_MATRIX_LABEL_BG;
        else if (/\bram-head\b/.test(cls) && !/\bram-corner\b/.test(cls)) bg = RISK_MATRIX_HEAD_BG;
        else if (cell.tagName === 'TH') bg = '#E8E8E8';

        cell.setAttribute('valign', 'middle');
        cell.setAttribute('align', align);
        cell.setAttribute('bgcolor', bg);
        cell.style.cssText =
          'border:1px solid #000;padding:3px 4px;vertical-align:middle;text-align:' + align + ';' +
          'font-family:"Times New Roman",Times,serif;' +
          'font-size:' + (isScore || /\bram-weight\b|\bram-sev-weight/.test(cls) ? '12pt' : '9pt') + ';' +
          'font-weight:' + (isScore || zone || /\bram-head\b|\bram-weight/.test(cls) ? '700' : '400') + ';' +
          'background:' + bg + ';background-color:' + bg + ';color:#000;';
      }
    );
  }

  function hardenDataTables(root, forWord) {
    Array.prototype.forEach.call(root.querySelectorAll('table'), function (table) {
      if (table.getAttribute('border') === '0' && table.classList && table.classList.contains('risks-word-layout')) {
        return;
      }
      if (table.classList && (
        table.classList.contains('risks-word-layout')
        || table.classList.contains('risks-card-acq-table')
        || table.classList.contains('risks-word-title-field')
        || table.classList.contains('risks-assessment-matrix')
        || table.classList.contains('risks-risk-class-table')
      )) {
        return;
      }

      var isInfo = table.className && /sout-info-table/i.test(table.className);
      // Сводная / комиссия — серые шапки как в образце, НЕ зелёные ОПР.
      var isInfoTail = table.className && /sout-info-summary|sout-info-commission/i.test(table.className);
      var isData = !table.className
        || /risks-wp-table|risks-print-table|risks-print-months|risks-method-doc|sout-eq-table|med-print-table|sout-info-table|sout-info-summary|sout-info-commission|sout-commission-schedule|ppe-doc-table|risks-card-meta-table|risks-card-table/i.test(table.className)
        || table.querySelector('th') != null;

      if (!isData) return;

      var is2col = table.classList && table.classList.contains('risks-wp-table-2col');
      var isMeasures = table.classList && table.classList.contains('risks-wp-table-measures');
      var isCard = table.classList && table.classList.contains('risks-card-table');
      var colWidths = null;
      if (is2col) colWidths = ['12%', '88%'];
      if (isMeasures) colWidths = ['10%', '42%', '34%', '14%'];
      // Как в образце: «Меры управления» — самая широкая; Word лучше понимает pt.
      if (isCard) colWidths = forWord ? CARD_COL_WIDTHS_PT : CARD_COL_WIDTHS_PCT;
      if (isInfo) colWidths = forWord ? INFO_COL_WIDTHS_PT : INFO_COL_WIDTHS_PCT;
      if (isInfoTail && /sout-info-summary/i.test(table.className)) colWidths = ['42%', '58%'];
      if (isInfoTail && /sout-info-commission/i.test(table.className)) colWidths = ['36%', '28%', '36%'];

      table.setAttribute('border', isInfo || isInfoTail ? (forWord ? '1' : '0') : '1');
      table.setAttribute('cellspacing', '0');
      table.setAttribute('cellpadding', isInfo || isInfoTail ? '2' : (isCard ? '3' : '4'));
      table.setAttribute('width', '100%');
      // Инфо: Word — border=1 (полки сетки); PDF/экран — только ячейки.
      table.style.cssText =
        'width:100%;max-width:100%;border-collapse:collapse;border-spacing:0;' +
        (isInfo || isInfoTail
          ? (forWord ? 'border:1px solid #000;' : 'border:none;outline:none;')
          : 'border:1px solid #000;') +
        (isInfoTail ? 'margin:4pt 0 8pt;' : 'margin:8pt 0;') +
        'table-layout:fixed;';

      if (colWidths && colWidths.length) {
        var colgroup = table.querySelector('colgroup');
        if (!colgroup) {
          colgroup = document.createElement('colgroup');
          table.insertBefore(colgroup, table.firstChild);
        }
        while (colgroup.firstChild) colgroup.removeChild(colgroup.firstChild);
        colWidths.forEach(function (w) {
          var col = document.createElement('col');
          col.style.width = w;
          col.setAttribute('width', w);
          colgroup.appendChild(col);
        });
      }

      Array.prototype.forEach.call(table.querySelectorAll('th, td'), function (cell) {
        var isTh = cell.tagName === 'TH';
        var cellIndex = cell.cellIndex;
        var align = cell.classList && cell.classList.contains('risks-wp-col-n')
          ? 'center'
          : (cell.classList && cell.classList.contains('sout-info-c') ? 'center' : (isTh ? 'center' : 'left'));
        if (is2col && cellIndex === 0) align = 'center';
        if (is2col && cellIndex === 1 && !isTh) align = 'left';
        if (isMeasures && (cellIndex === 0 || cellIndex === 3)) align = 'center';
        if (isMeasures && (cellIndex === 1 || cellIndex === 2) && !isTh) align = 'left';
        if (isCard && (cellIndex === 0 || cellIndex === 5) && !isTh) align = 'center';

        var fontSize = isInfo
          ? '8.5pt'
          : (isInfoTail ? '12pt' : (isCard ? '8.5pt' : (isMeasures ? '9.5pt' : '11pt')));
        var pad = isInfo
          ? '2px 3px'
          : (isInfoTail ? '4px 6px' : (isCard ? '2px 3px' : (isMeasures ? '3px 4px' : '4px 6px')));

        cell.setAttribute('valign', 'top');
        cell.setAttribute('align', align);
        // СОУТ-инфо: ширины только через colgroup. cellIndex ломается на rowspan/colspan
        // (2-я строка шапки получает ширины чужих колонок → Word «чинит» и едет сетка).
        if (!isInfo && colWidths && colWidths[cellIndex]) {
          cell.style.width = colWidths[cellIndex];
          cell.setAttribute('width', colWidths[cellIndex]);
        }
        if (isInfo) {
          // Вертикальные заголовки трогает prepareSoutInfoForWord — здесь только рамка/фон.
          var isOk = cell.classList && cell.classList.contains('sout-info-ok');
          var isNumRow = !!(cell.parentElement && cell.parentElement.classList
            && cell.parentElement.classList.contains('sout-info-col-numbers'));
          if (cell.classList && cell.classList.contains('sout-info-v')) {
            var bgV = isOk ? INFO_OK_BG : INFO_HEAD_BG;
            if (isTh) {
              cell.setAttribute('bgcolor', bgV);
              cell.style.background = bgV;
            } else if (isOk) {
              cell.setAttribute('bgcolor', INFO_OK_BG);
              cell.style.background = INFO_OK_BG;
            }
            cell.setAttribute('valign', 'middle');
            cell.setAttribute('align', 'center');
            // Граница ячейки обязательна: без неё Word рисует «хвосты» от внешней рамки.
            cell.style.border = '1px solid #000';
            cell.removeAttribute('width');
            return;
          }
          var bg = isOk ? INFO_OK_BG : (isTh ? (isNumRow ? '#fff' : INFO_HEAD_BG) : '#fff');
          if (isTh || isOk) cell.setAttribute('bgcolor', bg);
          cell.removeAttribute('width');
          cell.style.cssText =
            'border:1px solid #000;padding:2px;vertical-align:middle;text-align:' + align + ';' +
            'font-family:Arial,Tahoma,sans-serif;font-size:8.5pt;word-wrap:break-word;color:#000;' +
            'background-clip:padding-box;' +
            (isTh || isOk ? 'background:' + bg + ';' : '');
          return;
        }
        if (isInfoTail) {
          var tailBg = isTh ? '#e8e8e8' : '#fff';
          var isNum = !!(cell.classList && cell.classList.contains('sout-info-c') && !isTh);
          var cellFs = isNum ? '14pt' : (isTh ? '10.5pt' : fontSize);
          if (isTh) cell.setAttribute('bgcolor', tailBg);
          cell.setAttribute('valign', 'middle');
          cell.style.cssText =
            'border:1px solid #000;padding:' + pad + ';vertical-align:middle;text-align:' +
            (isTh ? 'center' : align) + ';' +
            'font-family:"Times New Roman",Times,serif;font-size:' + cellFs + ';' +
            'font-weight:' + (isNum ? '700' : '400') + ';' +
            'background:' + tailBg + ';color:#000;word-wrap:break-word;' +
            (colWidths && colWidths[cellIndex] ? 'width:' + colWidths[cellIndex] + ';' : '');
          return;
        }
        if (isTh) {
          cell.setAttribute('bgcolor', '#2f6b3a');
          cell.style.cssText =
            'border:1px solid #000;padding:' + pad + ';vertical-align:middle;text-align:center;' +
            'font-family:"Times New Roman",Times,serif;font-size:' + fontSize + ';font-weight:700;' +
            'background:#2f6b3a;color:#fff;word-wrap:break-word;' +
            (colWidths && colWidths[cellIndex] ? 'width:' + colWidths[cellIndex] + ';' : '');
        } else {
          cell.style.cssText =
            'border:1px solid #000;padding:' + pad + ';vertical-align:top;text-align:' + align + ';' +
            'font-family:"Times New Roman",Times,serif;font-size:' + fontSize + ';word-wrap:break-word;' +
            (colWidths && colWidths[cellIndex] ? 'width:' + colWidths[cellIndex] + ';' : '');
        }
      });
    });
  }

  function makeWordUnderscore() {
    var p = document.createElement('p');
    p.className = 'risks-word-underscore';
    p.setAttribute(
      'style',
      'border:none;border-bottom:1px solid #000;margin:0 0 2pt;padding:0;line-height:14pt;min-height:14pt;height:14pt;'
    );
    p.innerHTML = '&nbsp;';
    return p;
  }

  /** Лист ознакомления + титульный лист — Word не понимает flex/grid и пустые div-линии. */
  function prepareWordSpecialLayouts(clone) {
    Array.prototype.forEach.call(clone.querySelectorAll('.risks-card-acq-underscore, .risks-chair-line'), function (el) {
      if (!el.parentNode) return;
      el.parentNode.insertBefore(makeWordUnderscore(), el);
      el.parentNode.removeChild(el);
    });

    Array.prototype.forEach.call(clone.querySelectorAll('table.risks-card-acq-table'), function (table) {
      table.setAttribute('border', '0');
      table.setAttribute('cellspacing', '0');
      table.setAttribute('cellpadding', '0');
      table.setAttribute('width', '100%');
      table.style.cssText =
        'width:100%;border:none;border-collapse:collapse;table-layout:fixed;margin:0;';
      Array.prototype.forEach.call(table.querySelectorAll('td'), function (td) {
        var isLabel = td.classList && td.classList.contains('risks-card-acq-label');
        var isName = td.classList && td.classList.contains('risks-card-acq-field-name');
        var w = isLabel ? '52%' : (isName ? '30%' : '18%');
        var align = isLabel ? 'left' : 'center';
        td.setAttribute('width', w);
        td.setAttribute('valign', 'bottom');
        td.setAttribute('align', align);
        td.style.cssText =
          'border:none !important;width:' + w + ';vertical-align:bottom;text-align:' + align + ';' +
          'padding:3pt 6pt 2pt 0;font-family:"Times New Roman",Times,serif;font-size:10.5pt;';
      });
    });

    Array.prototype.forEach.call(clone.querySelectorAll('.risks-method-approve'), function (el) {
      // Word не понимает margin-left:auto — правый блок через таблицу 52% | 48%
      var table = document.createElement('table');
      table.className = 'risks-word-layout risks-word-approve';
      table.setAttribute('border', '0');
      table.setAttribute('cellspacing', '0');
      table.setAttribute('cellpadding', '0');
      table.setAttribute('width', '100%');
      table.style.cssText =
        'width:100%;border:none;border-collapse:collapse;margin:0 0 36pt;';

      var tr = document.createElement('tr');
      var tdLeft = document.createElement('td');
      tdLeft.setAttribute('width', '52%');
      tdLeft.style.cssText = 'border:none !important;width:52%;padding:0;font-size:1pt;';
      tdLeft.innerHTML = '&nbsp;';

      var tdRight = document.createElement('td');
      tdRight.setAttribute('width', '48%');
      tdRight.setAttribute('align', 'center');
      tdRight.setAttribute('valign', 'top');
      tdRight.style.cssText =
        'border:none !important;width:48%;text-align:center;vertical-align:top;padding:0;';

      while (el.firstChild) tdRight.appendChild(el.firstChild);
      Array.prototype.forEach.call(tdRight.querySelectorAll('p'), function (p) {
        p.style.cssText =
          'text-align:center;margin:0 0 4pt;font-family:"Times New Roman",Times,serif;font-size:11pt;';
      });
      var label = tdRight.querySelector('.risks-method-approve-label');
      if (label) label.style.fontWeight = '700';

      tr.appendChild(tdLeft);
      tr.appendChild(tdRight);
      table.appendChild(tr);

      if (el.parentNode) {
        el.parentNode.insertBefore(table, el);
        el.parentNode.removeChild(el);
      }
    });

    Array.prototype.forEach.call(clone.querySelectorAll('.risks-method-title-block'), function (el) {
      el.style.cssText = 'text-align:center;margin:48pt 0 28pt;width:100%;';
      Array.prototype.forEach.call(el.querySelectorAll('p'), function (p) {
        p.style.textAlign = 'center';
      });
      var main = el.querySelector('.risks-method-title-main');
      if (main) {
        main.style.cssText =
          'text-align:center;font-size:18pt;font-weight:700;letter-spacing:0.08em;margin:0 0 8pt;';
      }
      var sub = el.querySelector('.risks-method-title-sub');
      if (sub) {
        sub.style.cssText =
          'text-align:center;font-size:12pt;font-weight:700;margin:0;';
      }
    });

    Array.prototype.forEach.call(clone.querySelectorAll('.risks-card-field'), function (el) {
      var label = el.querySelector('.risks-card-field-label');
      var line = el.querySelector('.risks-card-field-line');
      var hint = el.querySelector('.risks-card-field-hint');
      if (!label || !line) return;

      var table = document.createElement('table');
      table.className = 'risks-word-title-field';
      table.setAttribute('border', '0');
      table.setAttribute('cellspacing', '0');
      table.setAttribute('cellpadding', '2');
      table.setAttribute('width', '100%');
      table.style.cssText = 'width:100%;border:none;border-collapse:collapse;margin:0 0 14pt;';

      var tr1 = document.createElement('tr');
      var tdLabel = document.createElement('td');
      tdLabel.setAttribute('width', '28%');
      tdLabel.setAttribute('valign', 'bottom');
      tdLabel.style.cssText =
        'border:none !important;width:28%;vertical-align:bottom;padding:2pt 6pt 2pt 0;' +
        'font-family:"Times New Roman",Times,serif;font-size:11pt;';
      tdLabel.appendChild(label);

      var tdLine = document.createElement('td');
      tdLine.className = 'risks-word-field-line';
      tdLine.setAttribute('width', '72%');
      tdLine.setAttribute('valign', 'bottom');
      tdLine.setAttribute('align', 'center');
      tdLine.style.cssText =
        'border:none !important;border-bottom:1px solid #000 !important;width:72%;' +
        'vertical-align:bottom;text-align:center;font-weight:700;padding:2pt 0 1pt;' +
        'font-family:"Times New Roman",Times,serif;font-size:11pt;';
      while (line.firstChild) tdLine.appendChild(line.firstChild);
      if (!(tdLine.textContent || '').trim()) tdLine.innerHTML = '&nbsp;';

      tr1.appendChild(tdLabel);
      tr1.appendChild(tdLine);
      table.appendChild(tr1);

      if (hint) {
        var tr2 = document.createElement('tr');
        var tdHintPad = document.createElement('td');
        tdHintPad.style.cssText = 'border:none !important;padding:0;';
        tdHintPad.innerHTML = '&nbsp;';
        var tdHint = document.createElement('td');
        tdHint.setAttribute('align', 'center');
        tdHint.style.cssText =
          'border:none !important;text-align:center;font-size:8.5pt;padding:1pt 0 8pt;color:#333;' +
          'font-family:"Times New Roman",Times,serif;';
        tdHint.appendChild(hint);
        tr2.appendChild(tdHintPad);
        tr2.appendChild(tdHint);
        table.appendChild(tr2);
      }

      while (el.firstChild) el.removeChild(el.firstChild);
      el.appendChild(table);
      el.style.display = 'block';
      el.style.margin = '0 0 10pt';
    });

    Array.prototype.forEach.call(clone.querySelectorAll('.risks-card-compiled'), function (el) {
      var n = el.children.length;
      if (n >= 2) {
        var widths = [];
        var aligns = [];
        var i;
        for (i = 0; i < n; i++) {
          widths.push((100 / n).toFixed(1) + '%');
          aligns.push(i === 0 ? 'left' : 'center');
        }
        kidsToLayoutTable(el, { aligns: aligns, widths: widths, vAlign: 'bottom' });
      }
      // Word часто игнорирует margin у div — задаём отступ от таблицы на обёртке и на layout-таблице.
      el.style.marginTop = '12pt';
      el.style.paddingTop = '6pt';
      var layout = el.querySelector('table.risks-word-layout');
      if (layout) {
        layout.style.marginTop = '12pt';
      }
    });
  }

  function escapeHtml(s) {
    return String(s)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  /**
   * PDF/html2canvas: вертикальные заголовки уже столбиком через <br> (.sout-info-vert).
   * Не возвращаем writing-mode — он снова обрежет текст в узких колонках.
   */
  function flattenVerticalTextForPdf(root) {
    Array.prototype.forEach.call(
      root.querySelectorAll('.sout-info-v span, .sout-info-v p.sout-info-vert, .sout-info-v .sout-info-vert'),
      function (el) {
        el.style.cssText =
          'display:block;margin:0 auto;padding:0;text-align:center;color:#000;' +
          'font-size:5.5pt;line-height:0.9;writing-mode:horizontal-tb;transform:none;' +
          'white-space:normal;overflow:visible;background:transparent;' +
          'font-family:Arial,Tahoma,sans-serif;';
        var host = el.parentElement;
        if (host && host.style) {
          host.style.textAlign = 'center';
          host.style.verticalAlign = 'bottom';
          host.style.background = INFO_HEAD_BG;
          host.style.overflow = 'visible';
          host.setAttribute('bgcolor', INFO_HEAD_BG);
          if (host.classList) {
            host.classList.remove('sout-info-ok', 'sout-info-hl');
          }
        }
      }
    );

    Array.prototype.forEach.call(
      root.querySelectorAll('.sout-info-h, .sout-info-equip'),
      function (th) {
        th.style.setProperty('writing-mode', 'horizontal-tb', 'important');
        th.style.setProperty('transform', 'none', 'important');
        th.style.setProperty('white-space', 'normal', 'important');
        th.style.setProperty('vertical-align', 'middle', 'important');
        th.style.setProperty('text-align', 'center', 'important');
        th.style.setProperty('font-size', '6.5pt', 'important');
        th.style.setProperty('background', INFO_HEAD_BG, 'important');
        if (th.classList.contains('sout-info-equip')) {
          th.style.setProperty('width', '16%', 'important');
        } else {
          th.style.setProperty('width', '9%', 'important');
        }
      }
    );

    Array.prototype.forEach.call(root.querySelectorAll('.sout-info-table'), function (table) {
      var colgroup = table.querySelector('colgroup');
      if (!colgroup) {
        colgroup = document.createElement('colgroup');
        table.insertBefore(colgroup, table.firstChild);
      }
      while (colgroup.firstChild) colgroup.removeChild(colgroup.firstChild);
      INFO_COL_WIDTHS_PCT.forEach(function (w) {
        var col = document.createElement('col');
        col.style.width = w;
        col.setAttribute('width', w);
        colgroup.appendChild(col);
      });
      Array.prototype.forEach.call(table.querySelectorAll('.sout-info-ok'), function (cell) {
        cell.style.setProperty('background', INFO_OK_BG, 'important');
        cell.setAttribute('bgcolor', INFO_OK_BG);
      });
    });
  }

  /**
   * Word: вертикальные заголовки — буквы через <br> (HTML→Word так стабильнее layout-flow).
   * ФИО / СНИЛС / оборудование — горизонтально, как в печати и образце.
   * Жёлтые колонки 1 / 4 / 21 сохраняем. Поля/ширины — как в образцах E-Stroy / Дубровский.
   */
  /** Явные ширины ячеек по colgroup — Word иначе по-разному считает таблицы без шапки. */
  function applySoutInfoCellWidths(table) {
    if (!table) return;
    var widths = INFO_COL_WIDTHS_PT;
    function ptNum(w) {
      return parseFloat(String(w || '0').replace(/pt/i, '')) || 0;
    }
    Array.prototype.forEach.call(table.querySelectorAll('tr'), function (tr) {
      var colIdx = 0;
      Array.prototype.forEach.call(tr.children, function (cell) {
        if (!cell || (cell.tagName !== 'TD' && cell.tagName !== 'TH')) return;
        var span = Math.max(1, Number(cell.getAttribute('colspan') || 1));
        var sum = 0;
        for (var i = 0; i < span; i++) {
          sum += ptNum(widths[colIdx + i]);
        }
        if (sum > 0) {
          cell.style.width = sum + 'pt';
          cell.setAttribute('width', String(sum));
        }
        cell.style.border = '1px solid #000';
        colIdx += span;
      });
    });
  }

  /**
   * Шапка СОУТ-инфо для Word: title + строки мета как простые таблицы с текстом в td.
   * Flex/span + вложенные «полки» Word часто глотает — остаются только линии без подписей.
   */
  /**
   * Шапка СОУТ-инфо для Word: flex/span Word игнорирует → остаются пустые линии.
   * Собираем простые таблицы: подпись | значение (с нижней границей).
   * Раскладка как на превью:
   *   1) договор | адрес
   *   2) организация (на всю ширину)
   *   3) контакт | e-mail | телефон
   * Затем заголовок «Информация…», затем таблица.
   */
  function prepareSoutInfoHeaderForWord(clone) {
    if (!clone || !clone.querySelector) return;

    function metaText(el) {
      return (el && (el.textContent || '') || '').replace(/\u00a0/g, ' ').replace(/\s+/g, ' ').trim();
    }

    function makeMetaTable(pairs) {
      var table = document.createElement('table');
      table.className = 'risks-word-layout sout-info-meta-word';
      table.setAttribute('border', '0');
      table.setAttribute('cellspacing', '0');
      table.setAttribute('cellpadding', '0');
      table.setAttribute('width', '100%');
      table.style.cssText =
        'width:100%;border:none;border-collapse:collapse;margin:0 0 4pt;table-layout:fixed;';

      var tr = document.createElement('tr');
      var n = pairs.length;
      pairs.forEach(function (pair, idx) {
        var labelW;
        var valueW;
        if (n === 1) {
          labelW = pair.labelWidth || '22%';
          valueW = pair.valueWidth || '78%';
        } else if (n === 2) {
          labelW = pair.labelWidth || '14%';
          valueW = pair.valueWidth || '36%';
        } else {
          labelW = pair.labelWidth || '11%';
          valueW = pair.valueWidth || '22%';
        }

        var tdL = document.createElement('td');
        tdL.setAttribute('width', labelW);
        tdL.setAttribute('valign', 'bottom');
        tdL.setAttribute('align', 'left');
        tdL.style.cssText =
          'border:none;width:' + labelW + ';vertical-align:bottom;text-align:left;' +
          'padding:0 4pt 1pt 0;color:#000;font-family:Arial,Tahoma,sans-serif;font-size:9.5pt;';
        var pL = document.createElement('p');
        pL.setAttribute(
          'style',
          'margin:0;padding:0;color:#000;font-family:Arial,Tahoma,sans-serif;font-size:9.5pt;' +
          'text-align:left;line-height:1.25;'
        );
        pL.textContent = pair.label || '';
        tdL.appendChild(pL);

        var tdV = document.createElement('td');
        tdV.setAttribute('width', valueW);
        tdV.setAttribute('valign', 'bottom');
        tdV.setAttribute('align', 'left');
        tdV.className = 'sout-info-meta-shelf';
        tdV.style.cssText =
          'border:none;border-bottom:1px solid #000;' +
          'mso-border-bottom-alt:solid windowtext .5pt;' +
          'width:' + valueW + ';vertical-align:bottom;text-align:left;' +
          'padding:0 3px 1px;color:#000;font-family:Arial,Tahoma,sans-serif;font-size:9.5pt;';
        var pV = document.createElement('p');
        pV.setAttribute(
          'style',
          'margin:0;padding:0;color:#000;font-family:Arial,Tahoma,sans-serif;font-size:9.5pt;' +
          'text-align:left;line-height:1.25;'
        );
        var val = pair.value || '';
        if (!val) pV.innerHTML = '&nbsp;';
        else pV.textContent = val;
        tdV.appendChild(pV);

        tr.appendChild(tdL);
        tr.appendChild(tdV);
      });
      table.appendChild(tr);
      return table;
    }

    Array.prototype.forEach.call(clone.querySelectorAll('.sout-info-header'), function (header) {
      // Собрать все label/value из текущих строк (порядок DOM).
      var items = [];
      Array.prototype.forEach.call(
        header.querySelectorAll('.sout-info-meta-label, .sout-info-meta-value'),
        function (el) {
          var isLabel = el.classList && el.classList.contains('sout-info-meta-label');
          var isValue = el.classList && el.classList.contains('sout-info-meta-value');
          if (!isLabel && !isValue) return;
          items.push({
            kind: isLabel ? 'label' : 'value',
            text: metaText(el),
            wide: !!(el.classList && el.classList.contains('sout-info-meta-value--wide'))
          });
        }
      );

      // Склеить пары label+value
      var pairs = [];
      for (var i = 0; i < items.length; i++) {
        if (items[i].kind !== 'label') continue;
        var label = items[i].text;
        var value = '';
        var wide = false;
        if (i + 1 < items.length && items[i + 1].kind === 'value') {
          value = items[i + 1].text;
          wide = items[i + 1].wide;
          i++;
        }
        pairs.push({ label: label, value: value, wide: wide });
      }

      // Нормализуем подписи (без лишнего двоеточия в конце — добавим при выводе если нет)
      function withColon(s) {
        var t = String(s || '').trim();
        if (!t) return t;
        return /[:：]\s*$/.test(t) ? t : (t + ':');
      }

      // Раскладка по образцу превью / Дубровского
      var byKey = {};
      pairs.forEach(function (p) {
        var key = (p.label || '').toLowerCase();
        if (/договор/.test(key)) byKey.contract = p;
        else if (/наименован|организац/.test(key)) byKey.org = p;
        else if (/адрес/.test(key)) byKey.address = p;
        else if (/контакт|ответствен/.test(key)) byKey.contact = p;
        else if (/e-?mail|почт/.test(key)) byKey.email = p;
        else if (/телефон/.test(key) || /^тел\b/.test(key)) byKey.phone = p;
        else {
          // запас: по порядку
          if (!byKey._extra) byKey._extra = [];
          byKey._extra.push(p);
        }
      });

      // Очистить старую flex-разметку
      while (header.firstChild) header.removeChild(header.firstChild);
      header.setAttribute(
        'style',
        'margin:0 0 8pt;padding:0;font-size:9.5pt;line-height:1.35;color:#000;width:100%;'
      );

      // Строка 1: договор | адрес
      var row1 = [];
      if (byKey.contract) {
        row1.push({
          label: withColon(byKey.contract.label || 'Номер и дата договора'),
          value: byKey.contract.value,
          labelWidth: '18%',
          valueWidth: '30%'
        });
      }
      if (byKey.address) {
        row1.push({
          label: withColon(byKey.address.label || 'Адрес проведения замеров'),
          value: byKey.address.value,
          labelWidth: '18%',
          valueWidth: '34%'
        });
      }
      if (row1.length) header.appendChild(makeMetaTable(row1));

      // Строка 2: организация на всю ширину
      if (byKey.org) {
        header.appendChild(makeMetaTable([{
          label: withColon(byKey.org.label || 'Наименование организации*'),
          value: byKey.org.value,
          labelWidth: '22%',
          valueWidth: '78%'
        }]));
      }

      // Строка 3: контакт | e-mail | телефон
      var row3 = [];
      if (byKey.contact) {
        row3.push({
          label: withColon(byKey.contact.label || 'Контактное (ответственное) лицо'),
          value: byKey.contact.value,
          labelWidth: '16%',
          valueWidth: '18%'
        });
      }
      if (byKey.email) {
        row3.push({
          label: withColon(byKey.email.label || 'E-mail'),
          value: byKey.email.value,
          labelWidth: '8%',
          valueWidth: '22%'
        });
      }
      if (byKey.phone) {
        row3.push({
          label: withColon(byKey.phone.label || 'телефон'),
          value: byKey.phone.value,
          labelWidth: '8%',
          valueWidth: '28%'
        });
      }
      if (row3.length) header.appendChild(makeMetaTable(row3));

      // На случай нестандартных полей — дописать остальное
      if (byKey._extra && byKey._extra.length) {
        byKey._extra.forEach(function (p) {
          header.appendChild(makeMetaTable([{
            label: withColon(p.label),
            value: p.value,
            labelWidth: '22%',
            valueWidth: '78%'
          }]));
        });
      }

      // Если пар не распознали — вывести все подряд попарно
      if (!byKey.contract && !byKey.org && !byKey.address && !byKey.contact && pairs.length) {
        while (header.firstChild) header.removeChild(header.firstChild);
        for (var j = 0; j < pairs.length; j += 2) {
          var chunk = pairs.slice(j, j + 2).map(function (p) {
            return {
              label: withColon(p.label),
              value: p.value,
              labelWidth: pairs.length === 1 ? '22%' : '14%',
              valueWidth: pairs.length === 1 ? '78%' : '36%'
            };
          });
          header.appendChild(makeMetaTable(chunk));
        }
      }

      // Заголовок документа — сразу под шапкой (как на превью)
      var parent = header.parentNode;
      if (parent) {
        var titleEl = null;
        Array.prototype.forEach.call(parent.children, function (ch) {
          if (ch.classList && ch.classList.contains('sout-info-title')) titleEl = ch;
        });
        if (!titleEl && parent.querySelector) {
          titleEl = parent.querySelector('.sout-info-title');
        }
        if (titleEl && titleEl.parentNode === parent) {
          // Порядок: header → title → table
          parent.insertBefore(header, parent.firstChild);
          if (header.nextSibling !== titleEl) {
            parent.insertBefore(titleEl, header.nextSibling);
          }
        }
      }
    });

    Array.prototype.forEach.call(clone.querySelectorAll('.sout-info-title'), function (el) {
      if (!el || !el.parentNode) return;
      var text = (el.textContent || '').replace(/\s+/g, ' ').trim()
        || 'Информация, необходимая для проведения СОУТ';
      var p = document.createElement('p');
      p.className = 'sout-info-title';
      p.setAttribute(
        'style',
        'text-align:center;font-weight:700;font-size:12pt;margin:6pt 0 8pt;padding:0;' +
        'color:#000;font-family:"Times New Roman",Times,serif;'
      );
      p.textContent = text;
      el.parentNode.insertBefore(p, el);
      el.parentNode.removeChild(el);
    });
  }

  function prepareSoutInfoForWord(root) {
    if (!root.querySelector('.sout-info-table')) return;

    /** Вертикаль в Word — поворот текста (1в1 с образцом), без столбика букв. */
    function makeRotatedSpan(text, fontSize) {
      var fs = fontSize || '5.5pt';
      var span = document.createElement('span');
      span.className = 'sout-info-vert';
      span.textContent = String(text || '').trim();
      span.setAttribute(
        'style',
        'display:inline-block;writing-mode:tb-rl;mso-rotate:90;' +
        'white-space:nowrap;line-height:1;color:#000000;font-size:' + fs + ';' +
        'font-family:Arial,Tahoma,sans-serif;font-weight:400;margin:0 auto;padding:0;'
      );
      return span;
    }

    function fontForHeader(text, isRowspan) {
      var n = String(text || '').length;
      if (n > 50) return isRowspan ? '4.5pt' : '4pt';
      if (n > 32) return isRowspan ? '5pt' : '4.5pt';
      if (n > 18) return isRowspan ? '5.5pt' : '5pt';
      return isRowspan ? '6pt' : '5.5pt';
    }

    function makeVerticalCell(th, text) {
      var bg = INFO_HEAD_BG;
      var isRowspan = th.getAttribute('rowspan') === '2' || Number(th.getAttribute('rowspan') || 1) > 1;
      while (th.firstChild) th.removeChild(th.firstChild);
      th.appendChild(makeRotatedSpan(text, fontForHeader(text, isRowspan)));
      th.classList.remove('sout-info-ok', 'sout-info-hl');
      th.classList.add('sout-info-v');
      th.setAttribute('align', 'center');
      th.setAttribute('valign', 'bottom');
      th.setAttribute('bgcolor', bg);
      th.style.cssText =
        'color:#000000;text-align:center;vertical-align:bottom;padding:1px 0;' +
        'font-size:5.5pt;white-space:normal;background:' + bg + ';' +
        'border:1px solid #000;background-clip:padding-box;';
      if (isRowspan) {
        th.style.height = INFO_HEADER_HEIGHT_PT;
        th.setAttribute('height', INFO_HEADER_HEIGHT_PX);
      } else {
        th.style.height = INFO_HEADER2_HEIGHT_PT;
        th.setAttribute('height', INFO_HEADER2_HEIGHT_PX);
      }
    }

    function makeHorizontalCell(th, widthPct, widthPt) {
      var bg = INFO_HEAD_BG;
      th.classList.remove('sout-info-ok', 'sout-info-hl');
      th.setAttribute('align', 'center');
      th.setAttribute('valign', 'middle');
      th.setAttribute('bgcolor', bg);
      th.style.cssText =
        'color:#000000;text-align:center;vertical-align:middle;padding:1px 2px;' +
        'font-size:6.5pt;width:' + widthPct + ';white-space:normal;' +
        'font-family:Arial,Tahoma,sans-serif;background:' + bg + ';' +
        'border:1px solid #000;background-clip:padding-box;';
      th.style.width = widthPct;
      th.setAttribute('width', widthPt);
      Array.prototype.forEach.call(th.querySelectorAll('br'), function (br) {
        br.setAttribute('style', 'mso-data-placement:same-cell;');
      });
    }

    Array.prototype.forEach.call(root.querySelectorAll('.sout-info-v'), function (th) {
      var existing = th.querySelector('.sout-info-vert');
      var style = existing ? (existing.getAttribute('style') || '') : '';
      if (existing && !existing.querySelector('br') && /mso-rotate|tb-rl|vertical-rl/i.test(style)) return;
      var span = th.querySelector('span, p.sout-info-vert, .sout-info-vert');
      var text = (span ? span.textContent : th.textContent || '').replace(/\s+/g, ' ').trim();
      if (!text) return;
      makeVerticalCell(th, text);
    });

    Array.prototype.forEach.call(root.querySelectorAll('.sout-info-h'), function (th) {
      var isSnils = /снилс/i.test((th.textContent || '').replace(/\s+/g, ' '));
      if (isSnils) makeHorizontalCell(th, '6.5%', '52');
      else makeHorizontalCell(th, '8.5%', '68');
    });
    Array.prototype.forEach.call(root.querySelectorAll('.sout-info-equip'), function (th) {
      makeHorizontalCell(th, '16.5%', '132');
      th.style.fontSize = '5.5pt';
      th.style.lineHeight = '1.1';
      th.style.wordWrap = 'break-word';
      th.style.overflowWrap = 'break-word';
      th.style.whiteSpace = 'normal';
    });

    Array.prototype.forEach.call(root.querySelectorAll('.sout-info-table'), function (table) {
      table.style.fontSize = '7.5pt';
      table.style.tableLayout = 'fixed';
      table.style.width = '100%';
      table.style.maxWidth = '100%';
      table.style.borderCollapse = 'collapse';
      table.style.borderSpacing = '0';
      table.style.msoTableLayout = 'fixed';
      table.setAttribute('border', '1');
      table.setAttribute('cellspacing', '0');
      table.setAttribute('cellpadding', '1');
      table.setAttribute('width', '100%');
      table.setAttribute('style',
        'width:100%;max-width:100%;' +
        'border-collapse:collapse;table-layout:fixed;mso-table-layout:fixed;border:1px solid #000;');

      var colgroup = table.querySelector('colgroup');
      if (!colgroup) {
        colgroup = document.createElement('colgroup');
        table.insertBefore(colgroup, table.firstChild);
      }
      while (colgroup.firstChild) colgroup.removeChild(colgroup.firstChild);
      INFO_COL_WIDTHS_PCT.forEach(function (w, idx) {
        var col = document.createElement('col');
        col.style.width = w;
        col.setAttribute('width', String(INFO_COL_WIDTHS_PT[idx] || '').replace('pt', ''));
        colgroup.appendChild(col);
      });

      Array.prototype.forEach.call(table.querySelectorAll('th, td'), function (cell) {
        cell.style.color = '#000000';
        cell.style.border = '1px solid #000';
        cell.style.padding = '1px 2px';
        if (cell.classList && cell.classList.contains('sout-info-ok')) {
          cell.classList.remove('sout-info-ok');
          cell.style.background = '#fff';
          cell.setAttribute('bgcolor', '#FFFFFF');
        }
        if (cell.classList && (cell.classList.contains('sout-info-h') || cell.classList.contains('sout-info-equip'))) {
          cell.style.fontSize = '6.5pt';
        } else if (!(cell.classList && cell.classList.contains('sout-info-v'))) {
          cell.style.fontSize = '7.5pt';
        }
      });

      applySoutInfoCellWidths(table);

      Array.prototype.forEach.call(
        table.querySelectorAll(
          'tr.sout-info-head-row th[rowspan], thead tr:first-child th[rowspan],' +
          ' tr.sout-info-head-row th.sout-info-v, thead tr:first-child th.sout-info-v'
        ),
        function (th) {
          if (th.getAttribute('rowspan') === '2' || Number(th.getAttribute('rowspan') || 1) > 1) {
            th.style.height = INFO_HEADER_HEIGHT_PT;
            th.setAttribute('height', INFO_HEADER_HEIGHT_PX);
          } else if (th.classList && th.classList.contains('sout-info-v')) {
            th.style.height = INFO_HEADER2_HEIGHT_PT;
            th.setAttribute('height', INFO_HEADER2_HEIGHT_PX);
          }
        }
      );

      Array.prototype.forEach.call(table.querySelectorAll('tr.sout-info-col-numbers th'), function (th) {
        th.style.background = '#fff';
        th.setAttribute('bgcolor', '#FFFFFF');
        th.style.fontWeight = '700';
        th.style.height = '1.2em';
        th.style.padding = '1px 2px';
        th.style.color = '#000';
      });
    });

    Array.prototype.forEach.call(root.querySelectorAll('.sout-info-rules'), function (el) {
      el.style.cssText =
        'white-space:normal;border:none;padding:0;margin:0 0 6pt;' +
        'font-size:10pt;background:transparent;color:#000;line-height:1.22;' +
        'font-family:Arial,Tahoma,sans-serif;page-break-inside:auto;';
    });
    Array.prototype.forEach.call(root.querySelectorAll('.sout-info-rules-lead'), function (el) {
      el.style.cssText =
        'display:block;font-style:italic;text-decoration:underline;margin:0 0 3pt;' +
        'font-size:10pt;line-height:1.22;color:#000;page-break-after:auto;' +
        'font-family:Arial,Tahoma,sans-serif;';
    });
    Array.prototype.forEach.call(root.querySelectorAll('.sout-info-footnotes'), function (el) {
      el.style.cssText =
        'margin:6pt 0 0;padding:0;font-size:9pt;line-height:1.25;' +
        'font-family:Arial,Tahoma,sans-serif;color:#000;';
    });
    Array.prototype.forEach.call(root.querySelectorAll('.sout-info-footnote-req'), function (el) {
      el.style.cssText = 'margin:0 0 2pt;font-style:italic;font-weight:700;font-size:9pt;';
    });
    Array.prototype.forEach.call(root.querySelectorAll('.sout-info-footnote-law'), function (el) {
      el.style.cssText =
        'margin:0;font-style:italic;text-decoration:underline;font-size:9pt;';
    });
    Array.prototype.forEach.call(root.querySelectorAll('.sout-info-tail'), function (el) {
      el.style.fontSize = '10pt';
      el.style.lineHeight = '1.22';
      el.style.pageBreakBefore = 'auto';
      el.style.pageBreakInside = 'auto';
      el.style.minHeight = '0';
      el.style.height = 'auto';
      el.style.fontFamily = 'Arial,Tahoma,sans-serif';
    });
  }
  function packSoutInfoTailForWord(root) {
    var tail = root.querySelector('.sout-info-tail');
    if (!tail) return;

    tail.style.cssText =
      'page-break-before:auto;page-break-inside:auto;page-break-after:auto;' +
      'min-height:0;height:auto;margin:0;padding:0;font-size:10pt;line-height:1.22;' +
      'font-family:Arial,Tahoma,sans-serif;color:#000;';

    // Правила: <br> → короткие <p> (компактно, как образец).
    Array.prototype.forEach.call(root.querySelectorAll('.sout-info-rules'), function (el) {
      var raw = el.innerHTML || '';
      if (!/<br\s*\/?>/i.test(raw) && !el.querySelector('p')) {
        el.style.cssText =
          'margin:0 0 4pt;padding:0;font-size:10pt;line-height:1.22;' +
          'font-family:Arial,Tahoma,sans-serif;color:#000;';
        return;
      }
      var parts = raw.split(/<br\s*\/?>/i);
      if (parts.length <= 1 && el.querySelector('p')) {
        Array.prototype.forEach.call(el.querySelectorAll('p'), function (p) {
          p.style.cssText =
            'margin:0 0 1.5pt;padding:0;font-size:10pt;line-height:1.22;' +
            'font-family:Arial,Tahoma,sans-serif;color:#000;' +
            'page-break-before:auto;page-break-after:auto;';
        });
        return;
      }
      el.innerHTML = '';
      parts.forEach(function (part, idx) {
        var chunk = String(part || '').replace(/^\s+|\s+$/g, '');
        if (!chunk) return;
        var p = document.createElement('p');
        var isLead = idx === 0 && /sout-info-rules-lead/i.test(chunk);
        p.setAttribute(
          'style',
          'margin:0 0 ' + (isLead ? '3pt' : '1.5pt') + ';padding:0;' +
          'page-break-before:auto;page-break-after:auto;page-break-inside:auto;' +
          'font-size:10pt;line-height:1.22;' +
          'font-family:Arial,Tahoma,sans-serif;color:#000;' +
          (isLead ? 'font-style:italic;text-decoration:underline;' : '')
        );
        p.innerHTML = chunk;
        el.appendChild(p);
      });
      el.style.cssText =
        'margin:0 0 4pt;padding:0;border:none;background:transparent;' +
        'font-family:Arial,Tahoma,sans-serif;';
    });

    Array.prototype.forEach.call(
      tail.querySelectorAll('.sout-info-summary-lead, .sout-info-summary-title'),
      function (el) {
        el.style.cssText =
          'margin:4pt 0 2pt;padding:0;font-size:10pt;line-height:1.22;' +
          'font-family:Arial,Tahoma,sans-serif;color:#000;';
      }
    );

    Array.prototype.forEach.call(
      tail.querySelectorAll('.sout-info-summary, .sout-info-commission'),
      function (table) {
        table.style.cssText =
          'width:100%;border-collapse:collapse;border:none;margin:0 0 6pt;' +
          'font-size:10pt;table-layout:fixed;page-break-inside:avoid;' +
          'font-family:Arial,Tahoma,sans-serif;';
        table.setAttribute('border', '0');
        table.setAttribute('cellspacing', '0');
        table.setAttribute('cellpadding', '3');
        Array.prototype.forEach.call(table.querySelectorAll('th, td'), function (cell) {
          var isTh = cell.tagName === 'TH';
          var isNum = !!(cell.classList && cell.classList.contains('sout-info-c'));
          var zebra = isTh ? '#e8e8e8' : '#fff';
          cell.setAttribute('bgcolor', zebra);
          cell.style.cssText =
            'border:1px solid #000;padding:3px 5px;vertical-align:middle;' +
            'font-family:Arial,Tahoma,sans-serif;font-size:' +
            (isNum ? '12pt' : (isTh ? '9.5pt' : '10pt')) + ';' +
            'font-weight:' + (isNum ? '700' : '400') + ';' +
            'text-align:' + (isTh || isNum || /sout-info-commission/i.test(table.className) ? 'center' : 'left') + ';' +
            'background:' + zebra + ';color:#000;';
        });
      }
    );

    Array.prototype.forEach.call(tail.querySelectorAll('.sout-info-signs'), function (el) {
      el.style.cssText = 'width:100%;margin:6pt 0 0;padding:0;page-break-inside:auto;';
      Array.prototype.forEach.call(el.querySelectorAll('table.risks-word-layout'), function (t) {
        t.style.cssText =
          'width:100%;border:none;border-collapse:collapse;margin:0;page-break-inside:auto;';
        Array.prototype.forEach.call(t.querySelectorAll('td'), function (td) {
          td.style.padding = '1px 4px';
          td.style.border = 'none';
          td.style.verticalAlign = 'top';
          td.style.pageBreakInside = 'auto';
        });
      });
    });
    Array.prototype.forEach.call(tail.querySelectorAll('.sout-info-sign-role'), function (el) {
      el.style.cssText =
        'font-weight:700;margin:0 0 1pt;font-size:10pt;' +
        'font-family:Arial,Tahoma,sans-serif;';
    });
    Array.prototype.forEach.call(tail.querySelectorAll('.sout-info-sign-org'), function (el) {
      el.style.cssText =
        'margin:0 0 1pt;font-size:10pt;min-height:1em;' +
        'font-family:Arial,Tahoma,sans-serif;';
    });
    Array.prototype.forEach.call(tail.querySelectorAll('.sout-info-sign-shelf'), function (el) {
      wrapSoutInfoShelf(el, { heightPt: 14 });
    });
    Array.prototype.forEach.call(tail.querySelectorAll('.sout-info-sign-mp'), function (el) {
      el.style.cssText = 'display:block;font-size:9pt;margin:0;';
    });

    // Сводная/комиссия не рвать; остальное можно.
    Array.prototype.forEach.call(
      tail.querySelectorAll('.sout-info-summary, .sout-info-commission, .sout-info-signs'),
      function (el) {
        el.style.pageBreakInside = 'avoid';
      }
    );
  }

  /**
   * PDF: режет таблицу СОУТ-инфо целыми строками.
   * Сноски — только после ВСЕХ кусков таблицы (не в середине!).
   * Продолжения без шапки.
   */
  function paginateSoutInfoTableForPdf(root, pageContentHpx) {
    var table = root.querySelector('table.sout-info-table');
    if (!table || !root.contains(table)) return;
    var thead = table.querySelector('thead');
    var tbody = table.querySelector('tbody');
    if (!tbody) return;

    var dataRows = Array.prototype.slice.call(tbody.children).filter(function (el) {
      return el && el.tagName === 'TR';
    });
    if (!dataRows.length) return;

    var titleEl = root.querySelector('.sout-info-title');
    var metaEl = root.querySelector('.sout-info-header');
    var footnotes = root.querySelector('.sout-info-footnotes');
    var usedAbove = 0;
    if (titleEl) usedAbove += titleEl.offsetHeight || 0;
    if (metaEl) usedAbove += metaEl.offsetHeight || 0;
    var theadH = thead ? (thead.offsetHeight || 110) : 0;
    // Запас снизу: html2pdf часто «съедает» ~15–20% высоты.
    var pageH = Math.max(200, Math.floor((pageContentHpx || 700) * 0.82));
    var firstAvail = pageH - usedAbove - theadH - 24;
    var nextAvail = pageH - 24;
    if (firstAvail < 50) firstAvail = 50;
    if (nextAvail < 70) nextAvail = 70;
    // Жёсткий потолок строк — даже если высоты занижены.
    var FIRST_MAX = 6;
    var NEXT_MAX = 9;

    function rowH(row) {
      var h = row.offsetHeight || 0;
      if (!h) {
        try { h = row.getBoundingClientRect().height || 0; } catch (e) { /* ignore */ }
      }
      return Math.max(h, 20);
    }

    var chunks = [];
    var cur = [];
    var curH = 0;
    var avail = firstAvail;
    var maxRows = FIRST_MAX;
    dataRows.forEach(function (row) {
      var h = rowH(row);
      var overflow = cur.length && (curH + h > avail || cur.length >= maxRows);
      if (overflow) {
        chunks.push(cur);
        cur = [];
        curH = 0;
        avail = nextAvail;
        maxRows = NEXT_MAX;
      }
      cur.push(row);
      curH += h;
    });
    if (cur.length) chunks.push(cur);

    // Одна страница — только перенести сноски в конец блока таблицы.
    var parent = table.parentNode;
    if (!parent) return;

    if (chunks.length > 1) {
      while (tbody.firstChild) tbody.removeChild(tbody.firstChild);
      chunks[0].forEach(function (r) { tbody.appendChild(r); });

      var colgroup = table.querySelector('colgroup');
      var lastTable = table;

      for (var c = 1; c < chunks.length; c++) {
        var br = document.createElement('div');
        br.className = 'risks-page-break risks-word-page-break';
        br.setAttribute(
          'style',
          'page-break-before:always;break-before:page;display:block;height:0;margin:0;padding:0;border:none;'
        );

        var newTable = table.cloneNode(false);
        Array.prototype.forEach.call(table.attributes || [], function (attr) {
          if (attr.name === 'id') return;
          try { newTable.setAttribute(attr.name, attr.value); } catch (eAttr) { /* ignore */ }
        });
        newTable.style.cssText = table.style.cssText || '';
        newTable.setAttribute('border', '0');
        newTable.style.border = 'none';
        newTable.style.borderCollapse = 'collapse';
        newTable.style.width = '100%';
        newTable.style.tableLayout = 'fixed';
        newTable.style.pageBreakBefore = 'always';
        newTable.style.breakBefore = 'page';
        if (colgroup) newTable.appendChild(colgroup.cloneNode(true));
        var newBody = document.createElement('tbody');
        chunks[c].forEach(function (r) { newBody.appendChild(r); });
        newTable.appendChild(newBody);

        parent.insertBefore(br, lastTable.nextSibling);
        parent.insertBefore(newTable, br.nextSibling);
        applySoutInfoCellWidths(newTable);
        lastTable = newTable;
      }
      applySoutInfoCellWidths(table);
    }

    // Сноски — строго после последней таблицы с данными (не между кусками!).
    if (footnotes) {
      var tables = parent.querySelectorAll('table.sout-info-table');
      var last = tables.length ? tables[tables.length - 1] : table;
      if (last && last.parentNode) {
        if (last.nextSibling) {
          last.parentNode.insertBefore(footnotes, last.nextSibling);
        } else {
          last.parentNode.appendChild(footnotes);
        }
      }
    }
  }

  /**
   * Word: одна большая HTML-таблица часто не режется по страницам («одним полотном»).
   * Режем tbody на куски с page-break; шапка только на 1-й таблице.
   */
  function paginateSoutInfoTable(root) {
    var table = root.querySelector('table.sout-info-table');
    if (!table) return;
    var tbody = table.querySelector('tbody');
    if (!tbody) return;

    var rows = Array.prototype.slice.call(tbody.children).filter(function (el) {
      return el && el.tagName === 'TR' && !(el.querySelector && el.querySelector('th'));
    });
    // Если demote ещё не был — обычные data rows; head в thead.
    if (!rows.length) {
      rows = Array.prototype.slice.call(tbody.children).filter(function (el) {
        return el && el.tagName === 'TR';
      });
    }

    var FIRST = 8;
    var NEXT = 9;
    if (rows.length <= FIRST) return;

    var chunks = [];
    chunks.push(rows.slice(0, FIRST));
    for (var i = FIRST; i < rows.length; i += NEXT) {
      chunks.push(rows.slice(i, i + NEXT));
    }

    // Оставляем в первой таблице head-строки (th) + первый chunk data.
    var headKeep = Array.prototype.slice.call(tbody.children).filter(function (el) {
      return el && el.tagName === 'TR' && el.querySelector && el.querySelector('th');
    });
    while (tbody.firstChild) tbody.removeChild(tbody.firstChild);
    headKeep.forEach(function (r) { tbody.appendChild(r); });
    chunks[0].forEach(function (r) { tbody.appendChild(r); });

    var parent = table.parentNode;
    if (!parent) return;
    var insertBefore = table.nextSibling;
    var colgroup = table.querySelector('colgroup');

    for (var c = 1; c < chunks.length; c++) {
      var br = document.createElement('br');
      br.className = 'risks-word-page-break';
      br.setAttribute('clear', 'all');
      br.setAttribute(
        'style',
        'mso-special-character:line-break;page-break-before:always'
      );

      var newTable = table.cloneNode(false);
      Array.prototype.forEach.call(table.attributes || [], function (attr) {
        if (attr.name === 'id') return;
        try { newTable.setAttribute(attr.name, attr.value); } catch (e) { /* ignore */ }
      });
      newTable.style.cssText = table.style.cssText || '';
      newTable.setAttribute('border', '0');
      newTable.style.border = 'none';
      newTable.style.outline = 'none';
      newTable.style.borderCollapse = 'collapse';
      newTable.style.borderSpacing = '0';
      if (colgroup) newTable.appendChild(colgroup.cloneNode(true));
      var newBody = document.createElement('tbody');
      chunks[c].forEach(function (r) { newBody.appendChild(r); });
      newTable.appendChild(newBody);

      parent.insertBefore(br, insertBefore);
      parent.insertBefore(newTable, insertBefore);
    }

    // Блок примечаний / сводной / комиссии — строго с новой страницы.
    var tail = root.querySelector('.sout-info-tail');
    if (tail && tail.parentNode) {
      var already = tail.previousSibling;
      var hasBreak = false;
      while (already) {
        if (already.nodeType === 1) {
          if (
            (already.classList && (already.classList.contains('risks-word-page-break') || already.classList.contains('risks-page-break')))
            || (already.tagName === 'BR' && /page-break/i.test(already.getAttribute('style') || ''))
          ) {
            hasBreak = true;
          }
          break;
        }
        already = already.previousSibling;
      }
      if (!hasBreak) {
        var tailBr = document.createElement('br');
        tailBr.className = 'risks-word-page-break';
        tailBr.setAttribute('clear', 'all');
        tailBr.setAttribute(
          'style',
          'mso-special-character:line-break;page-break-before:always'
        );
        tail.parentNode.insertBefore(tailBr, tail);
      }
      tail.style.pageBreakInside = 'auto';
      tail.style.breakInside = 'auto';
      tail.style.pageBreakBefore = 'auto';
    }
  }

  /**
   * Шапка таблицы один раз: thead → первые строки tbody.
   * Иначе Word повторяет <thead> на каждой странице (CSS display часто игнорируется).
   * СОУТ-инфо тоже переносим (rowspan/colspan в tbody работают) — иначе «лишние шапки».
   */
  function demoteTableHeaders(root) {
    Array.prototype.forEach.call(root.querySelectorAll('table'), function (table) {
      var isSoutInfo = !!(table.classList && /sout-info-table|sout-info-summary|sout-info-commission/i.test(table.className));
      var thead = table.querySelector('thead');
      if (!thead) return;
      // Прочие таблицы со сложной шапкой не трогаем; СОУТ-инфо — обязательно.
      if (!isSoutInfo && thead.querySelector('[rowspan], [colspan]')) return;
      // thead.rows надёжнее, чем :scope (иначе шапка может удалиться целиком).
      var rows = Array.prototype.slice.call(thead.rows || []);
      if (!rows.length) {
        rows = Array.prototype.slice.call(thead.querySelectorAll('tr'));
      }
      if (!rows.length) {
        if (thead.parentNode) thead.parentNode.removeChild(thead);
        return;
      }
      var tbody = table.querySelector('tbody');
      if (!tbody) {
        tbody = document.createElement('tbody');
        table.appendChild(tbody);
      }
      for (var i = rows.length - 1; i >= 0; i--) {
        rows[i].classList.add(isSoutInfo ? 'sout-info-head-row' : 'risks-export-head-row');
        tbody.insertBefore(rows[i], tbody.firstChild);
      }
      if (thead.parentNode) thead.parentNode.removeChild(thead);
    });
  }

  /**
   * Пункты приказа → абзацы для Word.
   * Висячий отступ: номер/маркер слева, перенос текста ровно под первой строкой (без лишнего сдвига).
   */
  function flattenOrderListsForWord(sheet) {
    var font =
      'font-family:"Times New Roman",Times,serif;font-size:12pt;border:none;background:transparent;';

    function makePointPara(prefix, html, level) {
      var hang = level <= 0 ? 18 : 14;
      var left = level <= 0 ? 21 : 21 + level * 18;
      var p = document.createElement('p');
      p.className = level <= 0 ? 'risks-order-point' : 'risks-order-bullet';
      p.style.cssText =
        'margin:' + (level <= 0 ? '8pt 0' : '3pt 0') + ';' +
        'margin-left:' + left + 'pt;text-indent:-' + hang + 'pt;' +
        'text-align:justify;' + font;
      p.innerHTML = prefix + (html || '');
      return p;
    }

    function appendSublist(ul, parent, level) {
      if (!ul) return;
      Array.prototype.forEach.call(ul.children, function (li) {
        if (!li || li.tagName !== 'LI') return;
        var leadHtml = '';
        var nested = [];
        Array.prototype.forEach.call(li.childNodes, function (node) {
          if (node.nodeType === 1 && (node.tagName === 'UL' || node.tagName === 'OL')) {
            nested.push(node);
            return;
          }
          if (node.nodeType === 1) leadHtml += node.outerHTML || '';
          else if (node.nodeType === 3) leadHtml += node.textContent || '';
        });
        parent.appendChild(makePointPara('•\u00a0', leadHtml.replace(/^\s+/, ''), level));
        nested.forEach(function (childUl) {
          appendSublist(childUl, parent, level + 1);
        });
      });
    }

    Array.prototype.forEach.call(sheet.querySelectorAll('ol.risks-order-points'), function (ol) {
      var wrap = document.createElement('div');
      wrap.className = 'risks-order-points-word';
      wrap.style.cssText = 'margin:0 0 10pt;padding:0;';
      var index = 0;
      Array.prototype.forEach.call(ol.children, function (li) {
        if (!li || li.tagName !== 'LI') return;
        index += 1;
        var leadHtml = '';
        var nested = [];
        Array.prototype.forEach.call(li.childNodes, function (node) {
          if (node.nodeType === 1 && (node.tagName === 'UL' || node.tagName === 'OL')) {
            nested.push(node);
            return;
          }
          if (node.nodeType === 1) leadHtml += node.outerHTML || '';
          else if (node.nodeType === 3) leadHtml += node.textContent || '';
        });
        wrap.appendChild(makePointPara(index + '.\u00a0', leadHtml.replace(/^\s+/, ''), 0));
        nested.forEach(function (ul) {
          appendSublist(ul, wrap, 1);
        });
      });
      if (ol.parentNode) {
        ol.parentNode.insertBefore(wrap, ol);
        ol.parentNode.removeChild(ol);
      }
    });
  }

  /** Приказ ОПР/СОУТ — Word ломает flex/div; собираем шапку, дату/город и подпись таблицами. */
  function prepareOrderForWord(clone) {
    var sheets = clone.querySelectorAll('.risks-order-sheet');
    if (!sheets.length) return;

    function forceParagraph(el, styleCss) {
      if (!el || !el.parentNode) return null;
      if (el.tagName === 'P') {
        el.style.cssText = styleCss;
        return el;
      }
      var p = document.createElement('p');
      p.className = el.className || '';
      p.style.cssText = styleCss;
      while (el.firstChild) p.appendChild(el.firstChild);
      el.parentNode.insertBefore(p, el);
      el.parentNode.removeChild(el);
      return p;
    }

    function makeLayoutTable(cells, marginCss) {
      var table = document.createElement('table');
      table.className = 'risks-word-layout';
      table.setAttribute('border', '0');
      table.setAttribute('cellspacing', '0');
      table.setAttribute('cellpadding', '0');
      table.setAttribute('width', '100%');
      table.style.cssText =
        'width:100%;border:none;border-collapse:collapse;margin:' + (marginCss || '10pt 0') + ';';
      var tr = document.createElement('tr');
      cells.forEach(function (cell) {
        var td = document.createElement('td');
        td.setAttribute('width', cell.width);
        td.setAttribute('align', cell.align);
        td.setAttribute('valign', cell.shelf ? 'bottom' : 'top');
        var tdCss =
          'border:none !important;width:' + cell.width + ';text-align:' + cell.align + ';' +
          'vertical-align:' + (cell.shelf ? 'bottom' : 'top') + ';' +
          'padding:0;font-family:"Times New Roman",Times,serif;font-size:12pt;';
        if (cell.shelfFull) {
          tdCss +=
            'border-bottom:0.75pt solid #000 !important;' +
            'mso-border-bottom-alt:solid windowtext .5pt;padding-bottom:1pt;';
        }
        td.style.cssText = tdCss;
        var p = document.createElement('p');
        var pCss =
          'margin:0;text-align:' + cell.align + ';' +
          'font-family:"Times New Roman",Times,serif;font-size:12pt;';
        if (cell.shelf && !cell.shelfFull) {
          // Полка только под текстом (дата / город)
          pCss +=
            'display:inline-block;border:none;border-bottom:0.75pt solid #000;' +
            'mso-border-bottom-alt:solid windowtext .5pt;padding:0 2pt 1pt;';
        }
        p.style.cssText = pCss;
        if (cell.html != null) p.innerHTML = cell.html;
        else p.textContent = cell.text || '';
        td.appendChild(p);
        tr.appendChild(td);
      });
      table.appendChild(tr);
      return table;
    }

    Array.prototype.forEach.call(sheets, function (sheet) {
      sheet.style.cssText =
        'width:100%;max-width:100%;margin:0;padding:0;background:#fff;color:#000;' +
        'font-family:"Times New Roman",Times,serif;font-size:12pt;line-height:1.4;';

      // Шапка: обе линии двойные (как на образце)
      Array.prototype.forEach.call(sheet.querySelectorAll('.risks-order-letterhead-rule'), function (el) {
        var hr = document.createElement('hr');
        hr.className = 'risks-word-hr risks-word-hr-double';
        hr.setAttribute(
          'style',
          'border:none;border-top:3px double #000;border-bottom:none;height:0;margin:2pt 0 10pt;padding:0;'
        );
        if (el.parentNode) {
          el.parentNode.insertBefore(hr, el);
          el.parentNode.removeChild(el);
        }
      });

      Array.prototype.forEach.call(sheet.querySelectorAll('.risks-order-letterhead'), function (el) {
        el.style.cssText = 'text-align:center;margin:0 0 14pt;width:100%;';
        Array.prototype.slice.call(el.children).forEach(function (child) {
          if (child.tagName === 'HR' || (child.classList && child.classList.contains('risks-word-hr'))) {
            return;
          }
          var isBank = child.classList && child.classList.contains('risks-order-letterhead-bank');
          forceParagraph(
            child,
            isBank
              ? 'text-align:center;font-size:9pt;font-weight:400;margin:4pt 0;line-height:1.35;font-family:"Times New Roman",Times,serif;border:none;'
              : 'text-align:center;font-size:12pt;font-weight:700;margin:0 0 2pt;font-family:"Times New Roman",Times,serif;border:none;'
          );
        });
      });

      // Заголовок ПРИКАЗ + толстая линия на всю ширину
      Array.prototype.forEach.call(sheet.querySelectorAll('.risks-order-title'), function (el) {
        var p = forceParagraph(
          el,
          'text-align:center;font-size:16pt;font-weight:700;margin:12pt 0 4pt;letter-spacing:0.04em;' +
          'border:none;font-family:"Times New Roman",Times,serif;'
        );
        if (p && p.parentNode) {
          var hr = document.createElement('hr');
          hr.className = 'risks-word-hr risks-word-hr-title';
          hr.setAttribute(
            'style',
            'border:none;border-top:1.5pt solid #000;border-bottom:none;height:0;margin:0 0 10pt;padding:0;'
          );
          if (p.nextSibling) p.parentNode.insertBefore(hr, p.nextSibling);
          else p.parentNode.appendChild(hr);
        }
      });

      Array.prototype.forEach.call(
        sheet.querySelectorAll('.risks-order-meta-3, .risks-order-meta-2, .risks-order-meta'),
        function (el) {
          var kids = Array.prototype.slice.call(el.children);
          if (!kids.length) return;
          var n = kids.length;
          var cells = kids.map(function (kid, i) {
            var align = i === 0 ? 'left' : (i === n - 1 ? 'right' : 'center');
            var width = n === 2 ? '50%' : (n === 3 ? (i === 1 ? '34%' : '33%') : ((100 / n).toFixed(1) + '%'));
            var isShelf =
              (kid.classList && (
                kid.classList.contains('risks-order-date') ||
                kid.classList.contains('risks-order-city') ||
                kid.classList.contains('risks-order-number')
              )) || true;
            return { width: width, align: align, html: kid.innerHTML || '', shelf: isShelf };
          });
          var table = makeLayoutTable(cells, '10pt 0 14pt');
          if (el.parentNode) {
            el.parentNode.insertBefore(table, el);
            el.parentNode.removeChild(el);
          }
        }
      );

      Array.prototype.forEach.call(sheet.querySelectorAll('.risks-order-subject'), function (el) {
        forceParagraph(
          el,
          'text-align:center;font-size:12pt;font-weight:400;margin:0 0 10pt;width:100%;' +
          'border:none;background:transparent;font-family:"Times New Roman",Times,serif;'
        );
      });

      Array.prototype.forEach.call(sheet.querySelectorAll('.risks-order-preamble'), function (el) {
        var inLi = !!(el.closest && el.closest('li'));
        if (inLi) {
          el.style.cssText =
            'text-align:justify;margin:0;width:100%;border:none;background:transparent;' +
            'font-family:"Times New Roman",Times,serif;font-size:12pt;';
          return;
        }
        forceParagraph(
          el,
          'text-align:justify;margin:0 0 10pt;width:100%;border:none;background:transparent;' +
          'font-family:"Times New Roman",Times,serif;font-size:12pt;'
        );
      });

      Array.prototype.forEach.call(sheet.querySelectorAll('.risks-order-inline'), function (el) {
        el.style.cssText =
          'display:inline;border:none;border-bottom:0.75pt solid #000;' +
          'mso-border-bottom-alt:solid windowtext .5pt;padding:0 2pt 1pt;' +
          'font-family:"Times New Roman",Times,serif;font-size:12pt;';
      });

      Array.prototype.forEach.call(sheet.querySelectorAll('.risks-order-command'), function (el) {
        forceParagraph(
          el,
          'text-align:center;font-weight:700;margin:12pt 0;font-size:12pt;border:none;' +
          'text-transform:uppercase;background:transparent;font-family:"Times New Roman",Times,serif;'
        );
      });

      // div внутри li (старые бланки) — разворачиваем до конвертации списков
      Array.prototype.forEach.call(sheet.querySelectorAll('li > div'), function (div) {
        var parent = div.parentNode;
        if (!parent) return;
        while (div.firstChild) parent.insertBefore(div.firstChild, div);
        parent.removeChild(div);
      });

      // Word криво ставит отступ переноса у <ol>/<ul> (особенно с вложенными списками).
      // Пункты → обычные абзацы: «1. …» / «• …» с висячим отступом без лишнего сдвига.
      flattenOrderListsForWord(sheet);

      // Линия перед подписью (тонкая сплошная — не double от letterhead)
      Array.prototype.forEach.call(sheet.querySelectorAll('.risks-order-sign-rule'), function (el) {
        var hr = document.createElement('hr');
        hr.className = 'risks-order-sign-rule';
        hr.setAttribute(
          'style',
          'border:none;border-top:1pt solid #000;border-bottom:none;height:0;margin:14pt 0 10pt;padding:0;width:100%;'
        );
        if (el.parentNode) {
          el.parentNode.insertBefore(hr, el);
          el.parentNode.removeChild(el);
        }
      });

      Array.prototype.forEach.call(
        sheet.querySelectorAll('.risks-order-sign-row2'),
        function (el) {
          var kids = Array.prototype.slice.call(el.children);
          if (!kids.length) return;
          var prev = el.previousElementSibling;
          var hasRuleBefore =
            !!prev &&
            (prev.tagName === 'HR' ||
              /risks-order-sign-rule/i.test(prev.className || ''));
          var cells;
          if (kids.length === 1) {
            cells = [{
              width: '100%',
              align: 'right',
              html: kids[0].innerHTML || kids[0].textContent || '',
              shelf: !hasRuleBefore,
              shelfFull: !hasRuleBefore
            }];
          } else {
            // С линией сверху — без полок под текстом (1в1 с образцом).
            cells = kids.map(function (kid, i) {
              return {
                width: (100 / kids.length).toFixed(0) + '%',
                align: i === 0 ? 'left' : 'right',
                html: kid.innerHTML || kid.textContent || '',
                shelf: !hasRuleBefore,
                shelfFull: false
              };
            });
          }
          var table = makeLayoutTable(cells, hasRuleBefore ? '0' : '28pt 0 0');
          if (el.parentNode) {
            el.parentNode.insertBefore(table, el);
            el.parentNode.removeChild(el);
          }
        }
      );

      Array.prototype.forEach.call(sheet.querySelectorAll('.sout-commission-appendix-ref'), function (el) {
        forceParagraph(
          el,
          'text-align:right;margin:0 0 18pt;font-size:11pt;line-height:1.35;font-family:"Times New Roman",Times,serif;'
        );
      });
      Array.prototype.forEach.call(sheet.querySelectorAll('.sout-commission-appendix-title'), function (el) {
        forceParagraph(
          el,
          'text-align:center;font-size:12pt;font-weight:700;margin:0 0 22pt;font-family:"Times New Roman",Times,serif;'
        );
      });
    });
  }

  function prepareClone(src, forWord) {
    flushLiveEditors(src);
    var clone = src.cloneNode(true);
    flattenCloneInputs(clone);
    Array.prototype.forEach.call(clone.querySelectorAll('[contenteditable]'), function (el) {
      el.removeAttribute('contenteditable');
    });
    Array.prototype.forEach.call(
      clone.querySelectorAll(
        '.ppe-no-print, .risks-print-edit-hint, .sout-preview-toolbar, .risks-preview-toolbar, script, style, button, .btn, datalist'
      ),
      function (el) {
        if (el.parentNode) el.parentNode.removeChild(el);
      }
    );
    Array.prototype.forEach.call(clone.querySelectorAll('p, div, span'), function (el) {
      var t = (el.textContent || '').replace(/\s+/g, ' ').trim();
      if (/^Правка:\s*клик/i.test(t) && el.parentNode) {
        el.parentNode.removeChild(el);
      }
    });

    // Word: убрать <thead>, иначе шапка повторяется на каждой странице.
    // PDF: thead оставляем — режем таблицу вручную в downloadSoutInfoPdf.
    if (forWord) {
      demoteTableHeaders(clone);
    } else {
      Array.prototype.forEach.call(clone.querySelectorAll('.sout-info-table thead'), function (thead) {
        thead.style.display = 'table-row-group';
      });
    }

    if (forWord) {
      prepareWordSpecialLayouts(clone);
      prepareOrderForWord(clone);
      Array.prototype.forEach.call(
        clone.querySelectorAll('.risks-wp-sign-row, .risks-wp-sign-hints, .risks-chair-fields'),
        function (el) {
          var n = el.children.length || 3;
          var widths = [];
          var aligns = [];
          var i;
          for (i = 0; i < n; i++) {
            widths.push((100 / n).toFixed(1) + '%');
            aligns.push('center');
          }
          kidsToLayoutTable(el, { aligns: aligns, widths: widths, vAlign: 'bottom' });
        }
      );
      Array.prototype.forEach.call(clone.querySelectorAll('.risks-chair-footer'), function (el) {
        if (el.children.length >= 2) {
          kidsToLayoutTable(el, { aligns: ['left', 'left'], widths: ['30%', '70%'], vAlign: 'bottom' });
        }
      });
      Array.prototype.forEach.call(clone.querySelectorAll('.risks-print-approve'), function (el) {
        kidsToLayoutTable(el, { aligns: ['right'], widths: ['100%'] });
      });
      // Шапка СОУТ-инфо — отдельная сборка (иначе Word оставляет только линии без текста).
      prepareSoutInfoHeaderForWord(clone);
      Array.prototype.forEach.call(clone.querySelectorAll('.sout-info-signs'), function (el) {
        if (el.children.length >= 2) {
          kidsToLayoutTable(el, { aligns: ['left', 'left'], widths: ['50%', '50%'] });
        }
      });
      // Линия подписи — та же полка.
      Array.prototype.forEach.call(clone.querySelectorAll('.sout-info-sign-shelf'), function (el) {
        wrapSoutInfoShelf(el, { heightPt: 14 });
      });
      Array.prototype.forEach.call(clone.querySelectorAll('.sout-info-sign-org'), function (el) {
        // org уже над полкой; если пусто — nbsp
        if (!(el.textContent || '').replace(/\u00a0/g, ' ').trim()) {
          el.innerHTML = '&nbsp;';
        }
      });

      Array.prototype.forEach.call(clone.querySelectorAll('.risks-page-break'), function (el) {
        // Жёсткий разрыв страницы, который Word реально понимает в HTML-.doc
        var marker = document.createElement('br');
        marker.className = 'risks-word-page-break';
        marker.setAttribute('clear', 'all');
        marker.setAttribute(
          'style',
          'mso-special-character:line-break;page-break-before:always'
        );
        if (el.parentNode) {
          el.parentNode.insertBefore(marker, el);
          el.parentNode.removeChild(el);
        }
      });

      // Приложение / ознакомление — min-height сброс; разрыв страницы только через
      // .risks-page-break → br выше (не дублировать br перед acq — пустая страница в Word).
      Array.prototype.forEach.call(
        clone.querySelectorAll('.sout-commission-appendix, .risks-card-acq-sheet'),
        function (el) {
          el.style.minHeight = '0';
          el.style.height = 'auto';
          el.style.pageBreakBefore = 'auto';
          el.style.removeProperty('mso-break-type');
        }
      );

      // Сброс min-height превью-листов (297mm / 180mm) — Word иначе пустые страницы
      Array.prototype.forEach.call(
        clone.querySelectorAll(
          '.risks-a4-page, .risks-a4-portrait, .risks-a4-landscape, .risks-a4-sheet,' +
          ' .risks-order-sheet, .sout-commission-order-sheet, .sout-commission-appendix,' +
          ' .risks-card-sheet, .risks-method-sheet, .risks-method-cover, .risks-workplaces-sheet,' +
          ' .sout-a4-page, .sout-a4-portrait, .sout-a4-landscape, .sout-a4-info'
        ),
        function (el) {
          el.style.setProperty('min-height', '0', 'important');
          el.style.setProperty('height', 'auto', 'important');
          el.style.setProperty('max-height', 'none', 'important');
          el.style.setProperty('padding', '0', 'important');
          el.style.setProperty('box-shadow', 'none', 'important');
          el.style.setProperty('margin', '0', 'important');
        }
      );

      hardenDataTables(clone, true);
      // После harden: вернуть цвета матрицы / классификации (Word нужен bgcolor).
      paintRiskMatrixColorsForWord(clone);
      if (clone.querySelector('.sout-info-doc, .sout-info-table')) {
        flattenSoutInfoCloneForWord(clone);
        prepareSoutInfoForWord(clone);
        // НЕ режем таблицу на куски — Word тогда пересчитывает ширины колонок
        // и «ломает» сетку/полки на продолжении. Одна таблица = ровная сетка.
        packSoutInfoTailForWord(clone);
      }
    }

    // Шапка таблиц — один раз (в т.ч. СОУТ-инфо).
    Array.prototype.forEach.call(clone.querySelectorAll('thead'), function (thead) {
      thead.style.display = 'table-row-group';
    });

    clone.style.boxShadow = 'none';
    clone.style.margin = '0';
    clone.style.maxWidth = 'none';
    clone.style.minHeight = '0';
    clone.style.height = 'auto';
    clone.style.background = '#fff';
    clone.style.color = '#000';
    return clone;
  }

  function isLandscapeSource(src, opts) {
    opts = opts || {};
    if (opts.landscape) return true;
    if (!src) return false;
    var cls = String(src.className || '');
    // СОУТ-инфо: таблица альбомная (шаблон Дубровского); ориентацию задаёт opts/обёртка.
    if (src.querySelector && src.querySelector('.sout-info-doc, .sout-info-table')) {
      return true;
    }
    if (/\bsout-a4-landscape\b|\brisks-a4-landscape\b|\brisks-export-landscape\b/.test(cls)) {
      return true;
    }
    if (!src.querySelector) return false;
    if (src.querySelector('.sout-a4-landscape, .risks-a4-landscape')) {
      return true;
    }
    // Широкая карта идентификации / оценки рисков
    if (src.querySelector('.risks-card-table, .risks-card-sheet:not(.risks-card-acq-sheet)')) {
      return true;
    }
    return false;
  }

  function wordPageCss(name, landscape, isInfoDoc, headerId) {
    var pageSize = landscape ? '841.9pt 595.3pt' : '595.3pt 841.9pt';
    // Образцы СОУТ-инфо: верх/низ 0.5 см (14.2pt), лево/право 2 см (56.7pt).
    // Методика (образец): верх 1см / низ 1.25см / лево 2.25см / право 1.75см ≈ 28.4/35.4/63.8/49.6 pt
    var margin = landscape
      ? (isInfoDoc ? INFO_PAGE_MARGIN : '28.35pt')
      : (headerId ? '42.5pt 49.6pt 35.4pt 63.8pt' : '56.7pt');
    var orient = landscape ? 'landscape' : 'portrait';
    var headerLine = headerId
      ? ('    mso-header: ' + headerId + ';\n' +
         '    mso-header-margin: 14.2pt;\n')
      : ('    mso-header-margin: 36pt;\n');
    return (
      '  @page ' + name + ' {\n' +
      '    size: ' + pageSize + ';\n' +
      '    mso-page-orientation: ' + orient + ';\n' +
      '    margin: ' + margin + ';\n' +
      headerLine +
      '    mso-footer-margin: 36pt;\n' +
      '    mso-paper-source: 0;\n' +
      '  }\n' +
      '  div.' + name + ' { page: ' + name + '; }\n'
    );
  }

  /** Колонтитул Методики: текст из .risks-regulation-running-header → mso-header Word. */
  function extractRegulationWordHeader(clone) {
    var el = clone && clone.querySelector
      ? clone.querySelector('.risks-regulation-running-header')
      : null;
    if (!el) return null;
    var text = (el.textContent || '').replace(/\s+/g, ' ').trim();
    if (!text) return null;
    if (el.parentNode) el.parentNode.removeChild(el);
    return text;
  }

  /** Явные поля/ориентация A3 landscape — Word часто игнорирует @page из CSS. */
  function buildMsoSoutInfoSectPrHtml() {
    return (
      '<!--[if gte mso 9]><xml>\n' +
      '<w:sectPr xmlns:w="urn:schemas-microsoft-com:office:word">\n' +
      '  <w:pgSz w:w="' + MSO_A3_LANDSCAPE_W + '" w:h="' + MSO_A3_LANDSCAPE_H + '" w:orient="landscape"/>\n' +
      '  <w:pgMar w:top="284" w:right="284" w:bottom="284" w:left="284"' +
      ' w:header="0" w:footer="0" w:gutter="0"/>\n' +
      '</w:sectPr>\n' +
      '</xml><![endif]-->\n'
    );
  }

  function buildMsoHeaderHtml(headerText) {
    if (!headerText) return '';
    var esc = String(headerText)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
    return (
      '<div style="mso-element:header" id="h1">\n' +
      '  <p class="MsoHeader" align="center" style="text-align:center;font-family:\'Times New Roman\',Times,serif;' +
      'font-size:12pt;font-weight:700;margin:0">' + esc + '</p>\n' +
      '</div>\n'
    );
  }

  function downloadWord(opts) {
    opts = opts || {};
    var src = resolveSource(opts.sourceSelector);
    if (!src) {
      window.alert('Не найден документ для сохранения.');
      return;
    }

    var clone = prepareClone(src, true);
    var isInfoDoc = !!(clone.querySelector && clone.querySelector('.sout-info-doc, .sout-info-table'));
    var isRegulationDoc = !!(clone.querySelector && clone.querySelector(
      '.risks-regulation-body, .risks-regulation-sheet, .risks-regulation-running-header'
    ));
    var regulationHeaderText = isRegulationDoc ? extractRegulationWordHeader(clone) : null;
    var landscape = isLandscapeSource(src, opts)
      || !!(clone.querySelector && clone.querySelector(
        '.risks-a4-landscape, .risks-card-table, .risks-card-sheet:not(.risks-card-acq-sheet)'
      ));
    // СОУТ-инфо: таблица альбом (как шаблон), хвост — книжный.
    if (isInfoDoc) landscape = true;

    var pageCss = wordPageCss('WordSection1', landscape, isInfoDoc, regulationHeaderText ? 'h1' : null);
    if (landscape && !isInfoDoc) {
      pageCss =
        '  @page {\n' +
        '    size: 841.9pt 595.3pt;\n' +
        '    mso-page-orientation: landscape;\n' +
        '    margin: 28.35pt;\n' +
        '  }\n' +
        pageCss;
    }

    var bodyHtml;
    var infoTailOverrideCss = '';

    if (isInfoDoc) {
      // Секция 1 — альбом (таблица); секция 2 — книжный (примечания).
      flattenSoutInfoCloneForWord(clone);
      // Повторно: после flatten/demote шапка должна стать столбиком букв + 100% ширины.
      prepareSoutInfoForWord(clone);

      Array.prototype.forEach.call(clone.querySelectorAll('.risks-page-break'), function (el) {
        var marker = document.createElement('br');
        marker.className = 'risks-word-page-break';
        marker.setAttribute('clear', 'all');
        marker.setAttribute(
          'style',
          'mso-special-character:line-break;page-break-before:always'
        );
        if (el.parentNode) {
          el.parentNode.insertBefore(marker, el);
          el.parentNode.removeChild(el);
        }
      });

      var tail = clone.querySelector('.sout-info-tail');
      var mainHtml;
      var tailHtml = '';
      if (tail && tail.parentNode) {
        var prev = tail.previousSibling;
        while (prev && prev.nodeType === 3 && !/\S/.test(prev.textContent || '')) {
          var gone = prev;
          prev = prev.previousSibling;
          if (gone.parentNode) gone.parentNode.removeChild(gone);
        }
        if (
          prev &&
          prev.nodeType === 1 &&
          (
            (prev.classList && (prev.classList.contains('risks-word-page-break') || prev.classList.contains('risks-page-break')))
            || (prev.tagName === 'BR' && /page-break/i.test(prev.getAttribute('style') || ''))
          )
        ) {
          if (prev.parentNode) prev.parentNode.removeChild(prev);
        }

        packSoutInfoTailForWord(clone);

        stripSoutInfoHighlights(clone);

        tailHtml = sanitizeWordHtml(tail.outerHTML)
          .replace(/min-height\s*:\s*[^;]+;?/gi, 'min-height:0;')
          .replace(/height\s*:\s*\d+mm;?/gi, 'height:auto;')
          .replace(/mso-break-type\s*:\s*section-break;?/gi, '')
          .replace(/page-break-before\s*:\s*always;?/gi, 'page-break-before:auto;');
        tail.parentNode.removeChild(tail);
      }

      stripSoutInfoHighlights(clone);

      mainHtml = sanitizeWordHtml(String(clone.innerHTML || ''))
        .replace(/min-height\s*:\s*[^;]+;?/gi, 'min-height:0;')
        .replace(/height\s*:\s*\d+mm;?/gi, 'height:auto;')
        .replace(/mso-break-type\s*:\s*section-break;?/gi, '')
        .replace(/\sclass="sout-a4-[^"]*"/gi, '')
        .replace(/\sclass="sout-info-tail-sheet"/gi, '');

      // Обе секции — альбом A3; шапка таблицы только в первой таблице.
      // Поля 5мм явно со всех сторон + mso-paper-source.
      pageCss =
        '  @page WordSection1 {\n' +
        '    size: ' + A3_LANDSCAPE_SIZE + ';\n' +
        '    mso-page-orientation: landscape;\n' +
        '    margin: ' + INFO_PAGE_MARGIN + ';\n' +
        '    mso-header-margin: 0pt;\n' +
        '    mso-footer-margin: 0pt;\n' +
        '    mso-paper-source: 0;\n' +
        '  }\n' +
        '  div.WordSection1 { page: WordSection1; }\n';
      if (tailHtml) {
        pageCss +=
          '  @page WordSection2 {\n' +
          '    size: ' + A3_LANDSCAPE_SIZE + ';\n' +
          '    mso-page-orientation: landscape;\n' +
          '    margin: ' + INFO_TAIL_MARGIN + ';\n' +
          '    mso-header-margin: 0pt;\n' +
          '    mso-footer-margin: 0pt;\n' +
          '    mso-paper-source: 0;\n' +
          '  }\n' +
          '  div.WordSection2 { page: WordSection2; }\n';
      }
      infoTailOverrideCss =
        '  table.sout-info-table { width:100% !important; max-width:100% !important;' +
        '    table-layout:fixed !important; mso-table-layout-alt:fixed !important; margin:0 !important; }\n' +
        '  .sout-info-doc { width:100% !important; max-width:none !important; }\n' +
        '  body, .WordSection1, .WordSection2 { background:#ffffff !important; color:#000000 !important; }\n' +
        '  .sout-info-v span, .sout-info-v .sout-info-vert {' +
        '    color:#000000 !important; background:transparent !important; white-space:nowrap !important;' +
        '    writing-mode:tb-rl !important; mso-rotate:90 !important; transform:none !important;' +
        '    display:inline-block !important; line-height:1 !important;' +
        '    font-family:Arial,Tahoma,sans-serif !important; }\n' +
        '  .sout-info-footnotes { margin:6pt 0 0 !important; font-size:9pt !important;' +
        '    font-family:Arial,Tahoma,sans-serif !important; }\n' +
        '  .sout-info-footnote-req { margin:0 0 2pt !important; font-style:italic !important; font-weight:700 !important; }\n' +
        '  .sout-info-footnote-law { margin:0 !important; font-style:italic !important; text-decoration:underline !important; }\n' +
        '  .sout-info-tail {' +
        '    page-break-before: auto !important; page-break-inside: auto !important;' +
        '    margin: 0 !important; padding: 0 !important;' +
        '    font-size: 10pt !important; line-height: 1.22 !important;' +
        '    font-family: "Times New Roman", Times, serif !important;' +
        '    min-height: 0 !important; height: auto !important; }\n' +
        '  .sout-info-summary thead th, .sout-info-commission thead th,' +
        '   .sout-info-summary tr.risks-export-head-row th,' +
        '   .sout-info-commission tr.risks-export-head-row th,' +
        '   .sout-info-summary tr.sout-info-head-row th,' +
        '   .sout-info-commission tr.sout-info-head-row th {' +
        '    background: #e8e8e8 !important; color: #000 !important; font-weight: 400 !important;' +
        '    text-align: center !important; font-size: 9.5pt !important; }\n' +
        '  .sout-info-summary, .sout-info-commission {' +
        '    width: 100% !important; border: none !important; font-size: 10pt !important;' +
        '    margin: 0 0 6pt !important; page-break-inside: avoid !important;' +
        '    font-family: Arial, Tahoma, sans-serif !important; }\n' +
        '  .sout-info-summary th, .sout-info-summary td,' +
        '   .sout-info-commission th, .sout-info-commission td {' +
        '    border: 1px solid #000 !important; padding: 3px 5px !important;' +
        '    font-size: 10pt !important; color: #000 !important; }\n' +
        '  .sout-info-rules {' +
        '    border: none !important; background: transparent !important; padding: 0 !important;' +
        '    margin: 0 0 4pt !important; font-size: 10pt !important; line-height: 1.22 !important;' +
        '    page-break-inside: auto !important; font-family: "Times New Roman", Times, serif !important; }\n' +
        '  .sout-info-rules p {' +
        '    margin: 0 0 1.5pt !important; padding: 0 !important;' +
        '    page-break-before: auto !important; page-break-after: auto !important; }\n' +
        '  .sout-info-rules-lead {' +
        '    display: block !important; font-style: italic !important; text-decoration: underline !important;' +
        '    margin: 0 0 3pt !important; font-size: 10pt !important; line-height: 1.22 !important;' +
        '    font-family: "Times New Roman", Times, serif !important; background: transparent !important; }\n' +
        '  .sout-info-note { display: none !important; }\n' +
        '  .sout-info-ok, .sout-info-hl, mark { background: #fff !important; font-weight: inherit !important; }\n' +
        '  .sout-info-meta-shelf-tbl { width: 100% !important; border: none !important; border-collapse: collapse !important; }\n' +
        '  .sout-info-meta-shelf-tbl td {' +
        '    border: none !important; border-bottom: 1px solid #000 !important;' +
        '    padding: 0 3px 1px !important; font-size: 9.5pt !important; }\n' +
        '  .sout-info-summary-lead, .sout-info-summary-title {' +
        '    font-size: 10pt !important; margin: 3pt 0 2pt !important;' +
        '    font-family: "Times New Roman", Times, serif !important; }\n' +
        '  .sout-info-summary-lead { text-decoration: underline !important; }\n' +
        '  .sout-info-summary td.sout-info-c {' +
        '    font-size: 12pt !important; font-weight: 700 !important; }\n' +
        '  .sout-info-signs {' +
        '    width: 100% !important; margin-top: 10pt !important; page-break-inside: avoid !important; }\n' +
        '  .sout-info-sign-role, .sout-info-sign-org { font-size: 10pt !important; }\n' +
        '  .sout-info-sign-mp { display: block !important; font-size: 9pt !important; }\n' +
        '  .sout-info-sign-shelf {' +
        '    border: none !important; border-bottom: 1px solid #000 !important;' +
        '    margin: 6pt 0 1pt !important; min-height: 12pt !important; height: 12pt !important;' +
        '    line-height: 12pt !important; width: 85% !important; }\n' +
        '  .sout-info-tail table, .sout-info-tail p, .sout-info-tail div {' +
        '    page-break-before: auto !important;' +
        '    page-break-after: auto !important; }\n';

      bodyHtml = '<div class="WordSection1">\n' + mainHtml + '\n' + buildMsoSoutInfoSectPrHtml() + '</div>\n';
      if (tailHtml) {
        bodyHtml +=
          '<br clear="all" style="page-break-before:always;mso-break-type:section-break">\n' +
          '<div class="WordSection2">\n' + tailHtml + '\n' + buildMsoSoutInfoSectPrHtml() + '</div>\n';
      }
    } else {
      // Word: НЕ делаем section-break landscape→portrait для листа ознакомления —
      // MS Word часто применяет портрет ко всему документу и обрезает широкую таблицу.
      var sectionHtmls = splitHtmlByWordPageBreaks(clone).filter(function (part) {
        var tmp = document.createElement('div');
        tmp.innerHTML = part.html || '';
        var text = (tmp.textContent || '').replace(/\u00a0/g, ' ').replace(/\s+/g, ' ').trim();
        return text.length > 0;
      });
      if (!sectionHtmls.length) {
        sectionHtmls = [{ html: clone.innerHTML }];
      }

      function cleanWordChunk(html) {
        return String(html || '')
          .replace(/mso-break-type\s*:\s*section-break;?/gi, '')
          .replace(/page-break-before\s*:\s*always;?/gi, '')
          .replace(/break-before\s*:\s*page;?/gi, '')
          .replace(/page-break-after\s*:\s*always;?/gi, '')
          .replace(/min-height\s*:\s*[^;]+;?/gi, 'min-height:0;')
          .replace(/height\s*:\s*\d+mm;?/gi, 'height:auto;');
      }

      var joined = sectionHtmls.map(function (part, idx) {
        var chunk = cleanWordChunk(part.html);
        if (idx === 0) return chunk;
        return (
          '<br clear="all" style="mso-special-character:line-break;page-break-before:always" />\n' +
          chunk
        );
      }).join('\n');

      joined = joined
        .replace(/(<br[^>]*page-break-before[^>]*\/?>\s*)+$/gi, '')
        .replace(/(<p[^>]*>\s*&nbsp;\s*<\/p>\s*)+$/gi, '');

      bodyHtml = '<div class="WordSection1">\n' + joined + '\n</div>\n';
    }

    var html =
      '<html xmlns:o="urn:schemas-microsoft-com:office:office"\n' +
      '      xmlns:w="urn:schemas-microsoft-com:office:word"\n' +
      '      xmlns="http://www.w3.org/TR/REC-html40">\n' +
      '<head>\n' +
      '  <meta http-equiv="Content-Type" content="text/html; charset=utf-8" />\n' +
      '  <!--[if gte mso 9]><xml>\n' +
      '    <w:WordDocument>\n' +
      '      <w:View>Print</w:View>\n' +
      '      <w:Zoom>100</w:Zoom>\n' +
      '      <w:DoNotOptimizeForBrowser/>\n' +
      (landscape
        ? '      <w:PrintOptimized/>\n'
        : '') +
      (isInfoDoc
        ? '      <w:DisplayHorizontalDrawingGridEvery>0</w:DisplayHorizontalDrawingGridEvery>\n' +
          '      <w:DisplayVerticalDrawingGridEvery>0</w:DisplayVerticalDrawingGridEvery>\n'
        : '') +
      '    </w:WordDocument>\n' +
      '  </xml><![endif]-->\n' +
      '  <style type="text/css">\n' +
      pageCss +
      WORD_DOC_CSS + '\n' +
      infoTailOverrideCss +
      '  </style>\n' +
      '</head>\n' +
      '<body lang="RU"' + (landscape ? ' style="tab-interval:35.4pt"' : '') + '>\n' +
      buildMsoHeaderHtml(regulationHeaderText) +
      // СОУТ-инфо: main/tail уже sanitize; повторный проход не трогает section-break br.
      (isInfoDoc ? bodyHtml : sanitizeWordHtml(bodyHtml)) +
      '</body>\n</html>';

    var blob = new Blob(['\ufeff', html], { type: 'application/msword' });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = sanitizeFileName(opts.fileName || document.title) +
      (isInfoDoc ? '_v' + String(Date.now()).slice(-6) : '') + '.doc';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(function () { URL.revokeObjectURL(url); }, 1500);
  }

  /**
   * Режет HTML по .risks-page-break / .risks-word-page-break на части для Word-секций.
   * Лист ознакомления и приложение СОУТ уходят на отдельные страницы.
   */
  function splitHtmlByWordPageBreaks(root) {
    // br.risks-word-page-break → div.risks-page-break (удобнее резать)
    Array.prototype.forEach.call(
      root.querySelectorAll('.risks-word-page-break'),
      function (el) {
        var marker = document.createElement('div');
        marker.className = 'risks-page-break';
        if (el.parentNode) {
          el.parentNode.insertBefore(marker, el);
          el.parentNode.removeChild(el);
        }
      }
    );

    liftPageBreaksToRoot(root);

    var sections = splitCloneByPageBreaks(root);
    var parts = sections.map(function (sec) {
      return { html: (sec.innerHTML || '').trim() };
    }).filter(function (p) {
      return p.html.length > 0;
    });
    return parts.length ? parts : [{ html: root.innerHTML }];
  }

  function liftPageBreaksToRoot(root) {
    var guard = 0;
    while (guard++ < 50) {
      var brk = null;
      var all = root.querySelectorAll('.risks-page-break');
      for (var i = 0; i < all.length; i++) {
        if (all[i].parentNode && all[i].parentNode !== root) {
          brk = all[i];
          break;
        }
      }
      if (!brk) return;

      var parent = brk.parentNode;
      // Переносим parent и всё после разрыва на уровень корня
      var after = [];
      var n = brk.nextSibling;
      while (n) {
        after.push(n);
        n = n.nextSibling;
      }
      after.forEach(function (node) {
        parent.removeChild(node);
      });
      parent.removeChild(brk);

      var insertBefore = parent.nextSibling;
      var host = parent.parentNode || root;
      if (host === root) {
        root.insertBefore(brk, insertBefore);
        after.forEach(function (node) {
          root.insertBefore(node, insertBefore);
        });
      } else {
        // parent ещё вложен — выносим разрыв и хвост сразу после parent
        host.insertBefore(brk, parent.nextSibling);
        var ref = brk.nextSibling;
        after.forEach(function (node) {
          host.insertBefore(node, ref);
        });
      }
    }
  }

  function fitCloneForPdf(clone, landscape) {
    var isInfo = !!clone.querySelector('.sout-info-doc, .sout-info-table');
    var isCard = !!clone.querySelector('.risks-card-table, .risks-card-sheet');
    var wPx = landscape ? A4_LANDSCAPE_PX : A4_PORTRAIT_PX;
    // Карты: поля как @page при печати — padding на клоне (слева/справа на всех нарезанных страницах).
    var pad = isInfo ? '0' : (isCard ? (landscape ? '10mm' : '12mm') : (landscape ? '12mm 14mm' : '14mm 16mm'));
    clone.style.cssText =
      'box-shadow:none !important;border:none !important;outline:none !important;' +
      'margin:0 auto !important;padding:' + pad + ' !important;box-sizing:border-box !important;' +
      'width:' + wPx + 'px !important;max-width:' + wPx + 'px !important;min-width:' + wPx + 'px !important;' +
      'min-height:0 !important;height:auto !important;background:#fff !important;color:#000 !important;' +
      'overflow:hidden !important;display:block !important;';

    Array.prototype.forEach.call(
      clone.querySelectorAll(
        '.sout-eq-doc, .med-print-sheet, .risks-a4-sheet, .risks-workplaces-sheet, .risks-order-sheet,' +
        ' .sout-commission-order-sheet, .sout-commission-appendix, .sout-info-doc,' +
        ' .sout-a4-page, .sout-a4-portrait, .sout-a4-landscape, .sout-a4-info,' +
        ' .risks-a4-page, .risks-a4-portrait, .risks-a4-landscape, .risks-print-order-body,' +
        ' .risks-schedule-sheet, .risks-card-sheet'
      ),
      function (el) {
        el.style.setProperty('width', '100%', 'important');
        el.style.setProperty('max-width', 'none', 'important');
        el.style.setProperty('min-width', '0', 'important');
        el.style.setProperty('min-height', '0', 'important');
        el.style.setProperty('height', 'auto', 'important');
        el.style.setProperty('margin', '0', 'important');
        // Не обнулять padding у самого «листа» — отступ от края даёт clone; у вложенных — 0
        if (el !== clone) {
          el.style.setProperty('padding', '0', 'important');
        }
        el.style.setProperty('box-shadow', 'none', 'important');
        el.style.setProperty('border', 'none', 'important');
        el.style.setProperty('outline', 'none', 'important');
        el.style.setProperty('box-sizing', 'border-box', 'important');
        el.style.setProperty('background', '#fff', 'important');
        el.style.setProperty('color', '#000', 'important');
        el.style.setProperty('display', 'block', 'important');
      }
    );

    // «Приложение 1» — отступ от верха/линии страницы
    Array.prototype.forEach.call(clone.querySelectorAll('.sout-commission-appendix'), function (el) {
      el.style.setProperty('padding-top', '12pt', 'important');
    });

    // Приказ: flex дата/город и подпись → таблицы (иначе PDF иногда сжимает в столбик)
    Array.prototype.forEach.call(
      clone.querySelectorAll('.risks-order-meta-2, .risks-order-meta-3, .risks-order-sign-row2'),
      function (el) {
        var kids = Array.prototype.slice.call(el.children);
        if (kids.length < 2) return;
        var n = kids.length;
        var widths = [];
        var aligns = [];
        var i;
        for (i = 0; i < n; i++) {
          widths.push(n === 2 ? '50%' : (i === 1 && n === 3 ? '34%' : '33%'));
          aligns.push(i === 0 ? 'left' : (i === n - 1 ? 'right' : 'center'));
        }
        kidsToLayoutTable(el, { aligns: aligns, widths: widths, vAlign: 'top' });
      }
    );

    hardenDataTables(clone, false);
    if (isInfo) {
      flattenVerticalTextForPdf(clone);
    }

    // Шапка таблиц — один раз (в т.ч. СОУТ-инфо).
    Array.prototype.forEach.call(clone.querySelectorAll('thead'), function (thead) {
      thead.style.setProperty('display', 'table-row-group', 'important');
    });

    Array.prototype.forEach.call(clone.querySelectorAll('.risks-page-break'), function (el) {
      el.style.cssText =
        'page-break-before:always;break-before:page;height:0;margin:0;padding:0;border:none;display:block;';
    });
    return { widthPx: wPx, isInfo: isInfo, isCard: isCard };
  }

  function splitCloneByPageBreaks(clone) {
    var kids = Array.prototype.slice.call(clone.childNodes);
    var sections = [];
    var bucket = [];

    function flush() {
      if (!bucket.length) return;
      var sec = document.createElement('div');
      sec.className = clone.className || '';
      sec.style.cssText = clone.style.cssText || '';
      bucket.forEach(function (n) { sec.appendChild(n); });
      sections.push(sec);
      bucket = [];
    }

    kids.forEach(function (node) {
      if (node.nodeType === 1 && node.classList && node.classList.contains('risks-page-break')) {
        flush();
        return;
      }
      bucket.push(node);
    });
    flush();
    return sections.length ? sections : [clone];
  }

  function makePdfHost(pageW) {
    var host = document.createElement('div');
    host.setAttribute('aria-hidden', 'true');
    // Без opacity:0 и без transform — иначе html2canvas даёт пустые белые страницы.
    host.style.cssText =
      'position:fixed;left:0;top:0;width:' + pageW + 'px;background:#fff;color:#000;' +
      'z-index:2147483000;opacity:1;pointer-events:none;overflow:visible;' +
      'box-shadow:none;border:none;';
    var styleEl = document.createElement('style');
    // PDF рисуем из того же HTML/стилей, что и Word.
    styleEl.textContent = WORD_DOC_CSS + '\n' + PDF_A4_CSS;
    host.appendChild(styleEl);
    var cover = document.createElement('div');
    cover.style.cssText =
      'position:fixed;left:0;top:0;right:0;bottom:0;z-index:2147483001;' +
      'background:rgba(255,255,255,.92);display:flex;align-items:center;justify-content:center;' +
      'font:14px/1.4 "Segoe UI",Tahoma,sans-serif;color:#222;pointer-events:none;';
    cover.textContent = 'Формирование PDF…';
    host._cover = cover;
    return host;
  }

  /**
   * PDF «Информация для СОУТ» = тот же HTML, что Word (forWord=true).
   * Без отдельной нарезки таблицы — она ломала строки и ставила сноски в середину.
   */
  function downloadSoutInfoPdf(opts, src) {
    var fileName = sanitizeFileName(opts.fileName || document.title) + '.pdf';
    // Как Word: одна таблица, полки, ширины колонок.
    var clone = prepareClone(src, true);
    flattenSoutInfoCloneForWord(clone);
    stripSoutInfoHighlights(clone);

    var tailEl = clone.querySelector('.sout-info-tail');
    var tailClone = null;
    if (tailEl && tailEl.parentNode) {
      packSoutInfoTailForWord(clone);
      tailClone = tailEl.cloneNode(true);
      tailClone.style.pageBreakBefore = 'auto';
      tailClone.style.breakBefore = 'auto';
      if (tailEl.parentNode) tailEl.parentNode.removeChild(tailEl);
    }

    Array.prototype.forEach.call(clone.querySelectorAll('.sout-info-tail-sheet'), function (el) {
      if (!(el.textContent || '').replace(/\s+/g, '').length && el.parentNode) {
        el.parentNode.removeChild(el);
      }
    });

    var landW = A4_LANDSCAPE_PX;
    clone.style.setProperty('width', landW + 'px', 'important');
    clone.style.setProperty('max-width', landW + 'px', 'important');
    clone.style.setProperty('min-width', landW + 'px', 'important');
    clone.style.setProperty('padding', '0', 'important');
    clone.style.setProperty('overflow', 'visible', 'important');

    Array.prototype.forEach.call(clone.querySelectorAll('.sout-info-table'), function (table) {
      table.style.setProperty('width', '100%', 'important');
      table.style.setProperty('table-layout', 'fixed', 'important');
      table.style.setProperty('border-collapse', 'collapse', 'important');
    });

    var host = document.createElement('div');
    host.setAttribute('aria-hidden', 'true');
    host.style.cssText =
      'position:fixed;left:0;top:0;width:' + landW + 'px;background:#fff;color:#000;' +
      'z-index:2147483000;opacity:1;pointer-events:none;overflow:visible;' +
      'box-shadow:none;border:none;';
    var styleEl = document.createElement('style');
    styleEl.textContent =
      WORD_DOC_CSS + '\n' + PDF_A4_CSS + '\n' +
      '.sout-info-table tbody tr { page-break-inside: avoid !important; break-inside: avoid !important; }\n' +
      '.sout-info-footnotes { page-break-inside: avoid !important; }\n';
    host.appendChild(styleEl);
    host.appendChild(clone);

    var cover = document.createElement('div');
    cover.style.cssText =
      'position:fixed;left:0;top:0;right:0;bottom:0;z-index:2147483001;' +
      'background:rgba(255,255,255,.92);display:flex;align-items:center;justify-content:center;' +
      'font:14px/1.4 "Segoe UI",Tahoma,sans-serif;color:#222;pointer-events:none;';
    cover.textContent = 'Формирование PDF…';
    document.body.appendChild(host);
    document.body.appendChild(cover);

    var cleanup = function () {
      try {
        if (cover.parentNode) cover.parentNode.removeChild(cover);
      } catch (e1) { /* ignore */ }
      try {
        if (host.parentNode) host.parentNode.removeChild(host);
      } catch (e2) { /* ignore */ }
    };

    var canvasOpts = function (w, h, scale) {
      return {
        scale: scale,
        useCORS: true,
        allowTaint: true,
        logging: false,
        backgroundColor: '#ffffff',
        scrollX: 0,
        scrollY: 0,
        x: 0,
        y: 0,
        width: w,
        windowWidth: w,
        windowHeight: Math.max(h, 800)
      };
    };

    setTimeout(function () {
      try {
        var w = landW;
        var h = Math.ceil(clone.scrollHeight || clone.offsetHeight) || 800;

        var worker = html2pdf()
          .set({
            margin: [5, 20, 5, 20],
            filename: fileName,
            image: { type: 'jpeg', quality: 0.92 },
            html2canvas: canvasOpts(w, h, 1.6),
            jsPDF: { unit: 'mm', format: 'a4', orientation: 'landscape' },
            pagebreak: {
              mode: ['css', 'legacy'],
              avoid: ['tr', '.sout-info-footnotes', '.sout-info-title', '.sout-info-header']
            }
          })
          .from(clone)
          .toPdf();

        if (tailClone) {
          var tailWrap = document.createElement('div');
          tailWrap.className = 'sout-info-doc sout-info-tail-page';
          tailWrap.style.cssText =
            'width:' + landW + 'px;max-width:' + landW + 'px;margin:0;padding:0;' +
            'background:#fff;color:#000;font-family:"Times New Roman",Times,serif;font-size:10pt;line-height:1.22;' +
            'box-sizing:border-box;';
          tailWrap.appendChild(tailClone);

          worker = worker.get('pdf').then(function (pdf) {
            pdf.addPage('a4', 'landscape');
            while (host.firstChild) host.removeChild(host.firstChild);
            host.style.width = landW + 'px';
            var st2 = document.createElement('style');
            st2.textContent = WORD_DOC_CSS + '\n' + PDF_A4_CSS;
            host.appendChild(st2);
            host.appendChild(tailWrap);
          }).set({
            margin: [5, 20, 5, 20],
            image: { type: 'jpeg', quality: 0.92 },
            html2canvas: canvasOpts(landW, 900, 1.6),
            jsPDF: { unit: 'mm', format: 'a4', orientation: 'landscape' },
            pagebreak: { mode: ['avoid-all'] }
          }).from(tailWrap).toContainer().toCanvas().toImg().toPdf();
        }

        worker.save().then(cleanup, function (err) {
          console.error(err);
          cleanup();
          window.alert('Не удалось сформировать PDF.');
        });
      } catch (errSync) {
        console.error(errSync);
        cleanup();
        window.alert('Не удалось сформировать PDF.');
      }
    }, 120);
  }

  /**
   * PDF методики / положения: отдельный путь.
   * Общий downloadPdf (prepareClone forWord + windowHeight 2400 / avoid) давал пустые страницы.
   */
  function downloadMethodRegulationPdf(opts, src) {
    var fileName = sanitizeFileName(opts.fileName || document.title) + '.pdf';
    // forWord=false: Word-harden и layout-таблицы ломают html2canvas.
    var clone = prepareClone(src, false);

    Array.prototype.forEach.call(
      clone.querySelectorAll(
        '.risks-method-sheet, .risks-method-cover, .risks-regulation-sheet, .risks-regulation-body,' +
        ' .risks-a4-sheet, .risks-a4-page, .risks-a4-portrait'
      ),
      function (el) {
        el.style.setProperty('min-height', '0', 'important');
        el.style.setProperty('height', 'auto', 'important');
        el.style.setProperty('max-height', 'none', 'important');
        el.style.setProperty('box-shadow', 'none', 'important');
        el.style.setProperty('margin', '0', 'important');
        el.style.setProperty('padding', '0', 'important');
        el.style.setProperty('width', '100%', 'important');
        el.style.setProperty('max-width', '100%', 'important');
        el.style.setProperty('background', '#fff', 'important');
        el.style.setProperty('color', '#000', 'important');
      }
    );

    // Цвета матрицы — через bgcolor (html2canvas надёжнее CSS).
    paintRiskMatrixColorsForWord(clone);

    var pageW = A4_PORTRAIT_PX;
    clone.style.cssText =
      'box-shadow:none;border:none;margin:0;padding:0;box-sizing:border-box;' +
      'width:' + pageW + 'px;max-width:' + pageW + 'px;min-width:' + pageW + 'px;' +
      'min-height:0;height:auto;background:#fff;color:#000;overflow:visible;display:block;' +
      'font-family:"Times New Roman",Times,serif;font-size:11pt;line-height:1.35;';

    var host = document.createElement('div');
    host.setAttribute('aria-hidden', 'true');
    host.style.cssText =
      'position:fixed;left:0;top:0;width:' + pageW + 'px;background:#fff;color:#000;' +
      'z-index:2147483000;opacity:1;pointer-events:none;overflow:visible;box-shadow:none;border:none;';
    var styleEl = document.createElement('style');
    styleEl.textContent =
      WORD_DOC_CSS + '\n' + PDF_A4_CSS + '\n' +
      '.risks-method-sheet,.risks-regulation-body,.risks-regulation-sheet,.risks-a4-sheet{' +
      'min-height:0!important;height:auto!important;max-height:none!important;' +
      'box-shadow:none!important;background:#fff!important;color:#000!important;}' +
      '.risks-regulation-body p,.risks-method-sheet p{color:#000!important;font-size:11pt!important;' +
      'font-family:"Times New Roman",Times,serif!important;}' +
      '.risks-method-h{color:#000!important;font-size:12pt!important;font-weight:700!important;}';
    host.appendChild(styleEl);
    host.appendChild(clone);

    var cover = document.createElement('div');
    cover.style.cssText =
      'position:fixed;left:0;top:0;right:0;bottom:0;z-index:2147483001;' +
      'background:rgba(255,255,255,.92);display:flex;align-items:center;justify-content:center;' +
      'font:14px/1.4 "Segoe UI",Tahoma,sans-serif;color:#222;pointer-events:none;';
    cover.textContent = 'Формирование PDF…';
    document.body.appendChild(host);
    document.body.appendChild(cover);

    var cleanup = function () {
      try {
        if (cover.parentNode) cover.parentNode.removeChild(cover);
      } catch (e1) { /* ignore */ }
      try {
        if (host.parentNode) host.parentNode.removeChild(host);
      } catch (e2) { /* ignore */ }
    };

    setTimeout(function () {
      try {
        void host.offsetHeight;
        var h = Math.ceil(clone.scrollHeight || clone.offsetHeight || 0);
        if (h < 200) {
          // Повторная попытка после layout
          h = Math.ceil(clone.getBoundingClientRect().height || 0);
        }
        if (h < 200) {
          cleanup();
          window.alert('Не удалось сформировать PDF: пустой документ. Обновите страницу (Ctrl+F5) и повторите.');
          return;
        }
        // Не раздувать windowHeight — лишняя высота → белые страницы.
        h = Math.min(h + 32, 30000);

        html2pdf()
          .set({
            margin: [15, 15, 15, 15],
            filename: fileName,
            image: { type: 'jpeg', quality: 0.94 },
            html2canvas: {
              scale: 1.6,
              useCORS: true,
              allowTaint: true,
              logging: false,
              backgroundColor: '#ffffff',
              scrollX: 0,
              scrollY: 0,
              x: 0,
              y: 0,
              width: pageW,
              windowWidth: pageW,
              windowHeight: h
            },
            jsPDF: {
              unit: 'mm',
              format: 'a4',
              orientation: 'portrait'
            },
            pagebreak: {
              mode: ['css', 'legacy'],
              avoid: [
                'table', 'img', 'tr',
                '.risks-method-h',
                '.risks-method-table-caption',
                '.risks-assessment-matrix',
                '.risks-risk-class-table'
              ]
            }
          })
          .from(clone)
          .save()
          .then(cleanup, function (err) {
            console.error(err);
            cleanup();
            window.alert('Не удалось сформировать PDF. Откройте предпросмотр и сохраните через печать браузера.');
          });
      } catch (errSync) {
        console.error(errSync);
        cleanup();
        window.alert('Не удалось сформировать PDF.');
      }
    }, 200);
  }

  /**
   * PDF карт/прочих бланков ≈ Word (prepareClone forWord + те же разрывы).
   * СОУТ-инфо — отдельно (downloadSoutInfoPdf).
   * Методика / положение — downloadMethodRegulationPdf.
   */
  function downloadPdf(opts) {
    opts = opts || {};
    var src = resolveSource(opts.sourceSelector);
    if (!src) {
      window.alert('Не найден документ для сохранения.');
      return;
    }
    if (typeof html2pdf !== 'function') {
      window.print();
      return;
    }

    try {
      window.scrollTo(0, 0);
    } catch (e) { /* ignore */ }

    // Информация для СОУТ: Word-подготовка ломает колонки в html2canvas.
    if (src.querySelector && src.querySelector('.sout-info-doc, .sout-info-table')) {
      downloadSoutInfoPdf(opts, src);
      return;
    }

    // Методика / положение ОПР.
    if (src.querySelector && src.querySelector(
      '.risks-method-sheet, .risks-regulation-body, .risks-regulation-sheet, .risks-assessment-matrix'
    )) {
      downloadMethodRegulationPdf(opts, src);
      return;
    }

    var fileName = sanitizeFileName(opts.fileName || document.title) + '.pdf';

    // Как в Word: те же таблицы, ширины колонок, разрывы.
    var clone = prepareClone(src, true);
    var isInfoDoc = false;
    var landscape = isLandscapeSource(src, opts)
      || !!(clone.querySelector && clone.querySelector(
        '.risks-a4-landscape, .risks-card-table, .risks-card-sheet:not(.risks-card-acq-sheet)'
      ));
    var isCard = !!(clone.querySelector && clone.querySelector('.risks-card-table, .risks-card-sheet'));

    liftPageBreaksToRoot(clone);

    var pageW = landscape ? A4_LANDSCAPE_PX : A4_PORTRAIT_PX;
    var host = makePdfHost(pageW);
    document.body.appendChild(host);
    if (host._cover) document.body.appendChild(host._cover);

    var cleanup = function () {
      try {
        if (host._cover && host._cover.parentNode) host._cover.parentNode.removeChild(host._cover);
      } catch (e1) { /* ignore */ }
      try {
        if (host.parentNode) host.parentNode.removeChild(host);
      } catch (e2) { /* ignore */ }
    };

    var canvasOpts = function (w, h, scale) {
      return {
        scale: scale,
        useCORS: true,
        allowTaint: true,
        logging: false,
        backgroundColor: '#ffffff',
        scrollX: 0,
        scrollY: 0,
        x: 0,
        y: 0,
        width: w,
        windowWidth: w,
        windowHeight: h
      };
    };

    var mountSection = function (sec, widthPx, pad) {
      sec.style.cssText =
        'box-shadow:none !important;border:none !important;outline:none !important;' +
        'margin:0 !important;padding:' + pad + ' !important;box-sizing:border-box !important;' +
        'width:' + widthPx + 'px !important;max-width:' + widthPx + 'px !important;min-width:0 !important;' +
        'min-height:0 !important;height:auto !important;background:#fff !important;color:#000 !important;' +
        'overflow:visible !important;display:block !important;' +
        'font-family:"Times New Roman",Times,serif !important;';

      Array.prototype.forEach.call(
        sec.querySelectorAll(
          '.risks-card-acq-sheet, .risks-page-break, .risks-card-sheet, .risks-a4-landscape,' +
          ' .risks-a4-sheet, .risks-workplaces-sheet, .WordSection1'
        ),
        function (el) {
          el.style.setProperty('page-break-before', 'auto', 'important');
          el.style.setProperty('page-break-after', 'auto', 'important');
          el.style.setProperty('break-before', 'auto', 'important');
          el.style.setProperty('break-after', 'auto', 'important');
          el.style.setProperty('min-height', '0', 'important');
          el.style.setProperty('height', 'auto', 'important');
          el.style.setProperty('width', '100%', 'important');
          el.style.setProperty('max-width', '100%', 'important');
          el.style.setProperty('padding', '0', 'important');
          el.style.setProperty('margin', '0', 'important');
          el.style.setProperty('border', 'none', 'important');
          el.style.setProperty('box-shadow', 'none', 'important');
        }
      );

      // Внешняя рамка table + collapse → на разрыве страницы «хвосты» по бокам.
      Array.prototype.forEach.call(
        sec.querySelectorAll(
          'table.risks-card-table, table.risks-print-table, table.ppe-doc-table, table.sout-info-table'
        ),
        function (t) {
          t.setAttribute('border', '0');
          t.style.setProperty('border', 'none', 'important');
          t.style.setProperty('outline', 'none', 'important');
          t.style.setProperty('border-collapse', 'collapse', 'important');
          t.style.setProperty('border-spacing', '0', 'important');
        }
      );
      Array.prototype.forEach.call(
        sec.querySelectorAll(
          'table.risks-card-table th, table.risks-card-table td,' +
          ' table.risks-print-table th, table.risks-print-table td,' +
          ' table.ppe-doc-table th, table.ppe-doc-table td,' +
          ' table.sout-info-table th, table.sout-info-table td'
        ),
        function (cell) {
          cell.style.setProperty('border', '1px solid #000', 'important');
        }
      );

      host.style.width = widthPx + 'px';
      while (host.firstChild) host.removeChild(host.firstChild);
      var st = document.createElement('style');
      st.textContent = WORD_DOC_CSS + '\n' + PDF_A4_CSS;
      host.appendChild(st);
      host.appendChild(sec);
    };

    setTimeout(function () {
      try {
        var sections = splitCloneByPageBreaks(clone).filter(function (sec) {
          var text = (sec.textContent || '').replace(/\s+/g, ' ').trim();
          return text.length > 20;
        });
        if (!sections.length) {
          sections = [clone];
        }

        var scale = sections.length > 8 ? 1.35 : 1.6;
        var first = sections[0];
        var firstAcq = !!(first.querySelector && first.querySelector('.risks-card-acq-sheet'));
        var firstLand = !!(landscape || isCard || firstAcq);
        var firstW = firstLand ? A4_LANDSCAPE_PX : A4_PORTRAIT_PX;
        var firstOrient = firstLand ? 'landscape' : 'portrait';
        var flowMargin = isCard ? 0 : [12, 12, 12, 12];
        var flowAvoid = [
          'p', 'li', 'tr', 'img', 'h1', 'h2', 'h3', 'h4',
          '.risks-order-points', '.risks-card-compiled', '.risks-card-page-foot', '.risks-card-acq-foot'
        ];

        var optBase = {
          margin: flowMargin,
          filename: fileName,
          image: { type: 'jpeg', quality: 0.92 },
          html2canvas: canvasOpts(firstW, 900, scale),
          jsPDF: {
            unit: 'mm',
            format: 'a4',
            orientation: firstOrient
          },
          pagebreak: {
            mode: ['css', 'legacy'],
            avoid: flowAvoid
          }
        };

        var worker = null;
        sections.forEach(function (sec, idx) {
          var isAcq = !!(sec.querySelector && sec.querySelector('.risks-card-acq-sheet'));
          var secLand = !!(landscape || isCard || isAcq);
          var widthPx = secLand ? A4_LANDSCAPE_PX : A4_PORTRAIT_PX;
          var orient = secLand ? 'landscape' : 'portrait';
          var pad = (isCard || isAcq) ? (isAcq ? '8mm' : '10mm') : '0';
          var secMargin = isAcq ? 0 : flowMargin;

          mountSection(sec, widthPx, pad);
          void host.offsetHeight;
          var contentH = Math.ceil(sec.scrollHeight || sec.offsetHeight || 800);
          contentH = Math.min(Math.max(contentH + 24, 400), 20000);

          var pageOpts = {
            margin: secMargin,
            html2canvas: canvasOpts(widthPx, contentH, scale),
            pagebreak: isAcq
              ? { mode: ['avoid-all'] }
              : {
                  mode: ['css', 'legacy'],
                  avoid: flowAvoid
                },
            jsPDF: {
              unit: 'mm',
              format: 'a4',
              orientation: orient
            }
          };

          if (idx === 0) {
            worker = html2pdf().set(Object.assign({}, optBase, pageOpts)).from(sec).toPdf();
          } else {
            worker = worker.get('pdf').then(function (pdf) {
              pdf.addPage('a4', orient);
              mountSection(sec, widthPx, pad);
              void host.offsetHeight;
            }).set(pageOpts).from(sec).toContainer().toCanvas().toImg().toPdf();
          }
        });

        if (!worker) {
          cleanup();
          window.print();
          return;
        }

        worker.save().then(cleanup, function (err) {
          console.error(err);
          cleanup();
          window.alert('Не удалось сформировать PDF. Откройте предпросмотр и сохраните оттуда.');
        });
      } catch (errSync) {
        console.error(errSync);
        cleanup();
        window.alert('Не удалось сформировать PDF. Откройте предпросмотр и сохраните оттуда.');
      }
    }, 120);
  }

  function bind(opts) {
    opts = opts || {};
    var wordBtn = document.getElementById(opts.wordBtnId || 'risks-doc-word-btn');
    var pdfBtn = document.getElementById(opts.pdfBtnId || 'risks-doc-pdf-btn');
    if (wordBtn) {
      wordBtn.addEventListener('click', function () {
        downloadWord(opts);
      });
    }
    if (pdfBtn) {
      pdfBtn.addEventListener('click', function () {
        downloadPdf(opts);
      });
    }
    maybeAutoExport(opts);
  }

  function getExportKindFromQuery() {
    try {
      var params = new URLSearchParams(window.location.search || '');
      var exp = String(params.get('export') || '').toLowerCase();
      if (exp === 'doc') exp = 'word';
      if (exp === 'word' || exp === 'pdf') return exp;
    } catch (e) { /* ignore */ }
    return '';
  }

  function maybeAutoExport(opts) {
    var kind = getExportKindFromQuery();
    if (!kind) return;

    try {
      document.documentElement.classList.add('risks-auto-export');
      var style = document.createElement('style');
      style.textContent =
        'html.risks-auto-export .sout-preview-toolbar,' +
        'html.risks-auto-export .risks-preview-toolbar,' +
        'html.risks-auto-export .ppe-no-print { display:none !important; }' +
        'html.risks-auto-export body { background:#fff !important; }';
      document.head.appendChild(style);
    } catch (e) { /* ignore */ }

    var delay = kind === 'pdf' ? 200 : 80;
    setTimeout(function () {
      if (kind === 'pdf') downloadPdf(opts);
      else downloadWord(opts);
    }, delay);
  }

  /**
   * Скачать Word/PDF по URL превью, не уходя со страницы (скрытый iframe).
   * На превью должен быть bind() — сработает ?export=word|pdf.
   */
  function exportFromUrl(url, format) {
    if (!url) return;
    format = String(format || '').toLowerCase();
    if (format === 'doc') format = 'word';
    if (format !== 'word' && format !== 'pdf') return;

    var target = String(url);
    try {
      var u = new URL(target, window.location.origin);
      u.searchParams.set('export', format);
      target = u.pathname + u.search + u.hash;
    } catch (e) {
      var sep = target.indexOf('?') >= 0 ? '&' : '?';
      target = target + sep + 'export=' + encodeURIComponent(format);
    }

    var iframe = document.createElement('iframe');
    iframe.setAttribute('aria-hidden', 'true');
    iframe.setAttribute('title', 'Экспорт документа');
    iframe.style.cssText =
      'position:fixed;left:-12000px;top:0;width:1200px;height:1400px;border:0;' +
      'opacity:0;pointer-events:none;overflow:auto;background:#fff;';
    iframe.src = target;
    document.body.appendChild(iframe);
    setTimeout(function () {
      try {
        if (iframe.parentNode) iframe.parentNode.removeChild(iframe);
      } catch (err) { /* ignore */ }
    }, 120000);
  }

  function bindQuickExportButtons(root) {
    var scope = root || document;
    Array.prototype.forEach.call(scope.querySelectorAll('[data-risks-export]'), function (btn) {
      if (btn.getAttribute('data-risks-export-bound') === '1') return;
      btn.setAttribute('data-risks-export-bound', '1');
      btn.addEventListener('click', function (event) {
        event.preventDefault();
        var format = btn.getAttribute('data-risks-export');
        var url = btn.getAttribute('data-risks-export-url');
        if (!url && btn.tagName === 'A') url = btn.getAttribute('href');
        if (!url) return;
        exportFromUrl(url, format);
      });
    });
  }

  if (typeof document !== 'undefined') {
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', function () {
        bindQuickExportButtons(document);
      });
    } else {
      bindQuickExportButtons(document);
    }
  }

  global.zashitaRisksDocExport = {
    downloadWord: downloadWord,
    downloadPdf: downloadPdf,
    bind: bind,
    exportFromUrl: exportFromUrl,
    bindQuickExportButtons: bindQuickExportButtons
  };
})(window);
