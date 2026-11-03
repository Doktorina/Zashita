document.addEventListener('DOMContentLoaded', function () {
  if (typeof window.initSoutPreparationList === 'function') {
    initSoutPreparationList();
  }
});

window.initSoutPreparationList = function () {
  var grid = document.getElementById('sout-preparation-grid');
  var printIdInput = document.getElementById('sout-preparation-print-id');
  var selectForm = document.getElementById('sout-preparation-filter-form');
  var selectedHint = document.getElementById('sout-preparation-selected-hint');
  var printMenu = document.getElementById('sout-preparation-print-menu');
  var printLinks = document.querySelectorAll('.sout-print-link[data-href-template]');
  var filterTimer = null;

  function submitFilters(delayMs) {
    if (!selectForm) {
      return;
    }

    clearTimeout(filterTimer);
    filterTimer = setTimeout(function () {
      if (typeof selectForm.requestSubmit === 'function') {
        selectForm.requestSubmit();
      } else {
        selectForm.submit();
      }
    }, typeof delayMs === 'number' ? delayMs : 350);
  }

  if (selectForm) {
    selectForm.querySelectorAll('input[name], select[name]').forEach(function (field) {
      if (field.readOnly || field.type === 'hidden') {
        return;
      }

      field.addEventListener('input', function () {
        submitFilters(400);
      });
      field.addEventListener('change', function () {
        submitFilters(150);
      });
    });
  }

  function setSelectedRow(row) {
    if (!row) {
      return;
    }

    if (grid) {
      grid.querySelectorAll('.sout-preparation-row.training-row-active').forEach(function (activeRow) {
        activeRow.classList.remove('training-row-active');
      });
    }
    row.classList.add('training-row-active');

    var id = row.getAttribute('data-id') || '';
    var number = row.getAttribute('data-number') || '';

    if (printIdInput) {
      printIdInput.value = id;
    }

    if (selectedHint) {
      selectedHint.textContent = number ? 'Для печати: ' + number : '';
    }

    var printHint = document.getElementById('sout-preparation-print-hint');
    if (printHint) {
      printHint.style.display = id ? 'none' : '';
    }

    printLinks.forEach(function (link) {
      var href = link.getAttribute('data-href-template');
      if (!id || !href) {
        link.classList.add('training-print-disabled');
        link.setAttribute('aria-disabled', 'true');
        link.tabIndex = -1;
        link.href = '#';
        return;
      }

      link.href = href.replace('{id}', encodeURIComponent(id));
      link.classList.remove('training-print-disabled');
      link.removeAttribute('aria-disabled');
      link.tabIndex = 0;
    });

    if (printMenu) {
      printMenu.classList.toggle('training-print-ready', Boolean(id));
    }
  }

  if (grid) {
    grid.addEventListener('click', function (event) {
      if (event.target.closest('a')) {
        return;
      }
      var row = event.target.closest('.sout-preparation-row');
      if (!row) {
        return;
      }
      setSelectedRow(row);
    });

    var initial = grid.querySelector('.sout-preparation-row.training-row-active')
      || grid.querySelector('.sout-preparation-row');
    if (initial) {
      setSelectedRow(initial);
    }
  }

  document.addEventListener('keydown', function (event) {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'f') {
      var search = selectForm && selectForm.querySelector('input[name="q"]');
      if (!search) {
        return;
      }
      event.preventDefault();
      search.focus();
      search.select();
    }
  });
};

window.initSoutPreparationPreview = function (options) {
  var saveBtn = document.getElementById(options.saveBtnId);
  var saveForm = document.getElementById(options.saveFormId);
  var overridesContainer = document.getElementById(options.overridesContainerId);
  var printArea = document.getElementById(options.printAreaId);

  function collectOverrides() {
    if (!overridesContainer || !printArea) {
      return;
    }

    // Зафиксировать открытый input ФИО до чтения ячеек.
    try {
      if (document.activeElement && typeof document.activeElement.blur === 'function') {
        document.activeElement.blur();
      }
    } catch (e) { /* ignore */ }
    printArea.querySelectorAll('[data-override-key] input, [contenteditable] input').forEach(function (inp) {
      if (!inp || !inp.parentNode) return;
      var type = String(inp.type || 'text').toLowerCase();
      if (type === 'hidden' || type === 'checkbox' || type === 'radio') return;
      inp.parentNode.textContent = inp.value != null ? String(inp.value) : '';
    });

    overridesContainer.innerHTML = '';
    printArea.querySelectorAll('[data-override-key]').forEach(function (cell) {
      var key = cell.getAttribute('data-override-key');
      if (!key) {
        return;
      }

      var keyInput = document.createElement('input');
      keyInput.type = 'hidden';
      keyInput.name = 'overrideKey';
      keyInput.value = key;
      overridesContainer.appendChild(keyInput);

      var valueInput = document.createElement('input');
      valueInput.type = 'hidden';
      valueInput.name = 'overrideValue';
      var nested = cell.querySelector('input, textarea');
      valueInput.value = nested
        ? String(nested.value || '').trim()
        : (cell.textContent || '').trim();
      overridesContainer.appendChild(valueInput);
    });
  }

  if (saveBtn && saveForm) {
    saveBtn.addEventListener('click', function () {
      collectOverrides();
      if (typeof saveForm.requestSubmit === 'function') {
        saveForm.requestSubmit();
      } else {
        saveForm.submit();
      }
    });
  }

  if (printArea) {
    printArea.querySelectorAll('[contenteditable="true"]').forEach(function (cell) {
      cell.addEventListener('keydown', function (event) {
        if (event.key === 'Enter' && cell.tagName !== 'DIV') {
          event.preventDefault();
        }
      });
    });
    // Хвост на отдельный альбомный лист; таблицу не режем заранее
    // (преждевременная нарезка давала «дыры» в превью/PDF).
    splitSoutInfoTailToPortraitSheet(printArea);
    packSoutInfoTailSheet(printArea);
  }
};

/**
 * Режет основную таблицу СОУТ-инфо по реальной высоте альбомного листа.
 * На каждом продолжении — копия thead (как при печати/в Word).
 */
function paginateSoutInfoTableInPreview(root) {
  if (!root || !root.querySelector) return;
  if (root.getAttribute('data-sout-info-paginated') === '1') return;

  var sheet = root.querySelector('.sout-a4-info, .sout-a4-landscape, .sout-a4-page');
  var table = root.querySelector('table.sout-info-table');
  if (!sheet || !table) return;
  var thead = table.querySelector('thead');
  var tbody = table.querySelector('tbody');
  if (!thead || !tbody) return;

  var rows = Array.prototype.slice.call(tbody.querySelectorAll(':scope > tr'));
  if (rows.length <= 1) {
    root.setAttribute('data-sout-info-paginated', '1');
    return;
  }

  // Доступная высота под строки: лист − шапка документа − thead − сноски.
  var sheetH = sheet.clientHeight || sheet.offsetHeight || 0;
  if (sheetH < 200) sheetH = Math.round(210 * 96 / 25.4); // ~210mm
  var padTop = 0;
  var padBottom = 0;
  try {
    var cs = window.getComputedStyle(sheet);
    padTop = parseFloat(cs.paddingTop) || 0;
    padBottom = parseFloat(cs.paddingBottom) || 0;
  } catch (ePad) { /* ignore */ }

  var headerBlock = 0;
  Array.prototype.forEach.call(sheet.children, function (child) {
    if (child === table || (child.classList && (
      child.classList.contains('sout-info-tail')
      || child.classList.contains('sout-info-footnotes')
      || child.classList.contains('sout-info-doc')
    ))) return;
    // Элементы до таблицы внутри .sout-info-doc
  });

  var doc = sheet.querySelector('.sout-info-doc') || sheet;
  var titleEl = doc.querySelector('.sout-info-title');
  var metaEl = doc.querySelector('.sout-info-header');
  var footnotes = doc.querySelector('.sout-info-footnotes');
  var usedAbove = 0;
  if (titleEl) usedAbove += titleEl.offsetHeight || 0;
  if (metaEl) usedAbove += metaEl.offsetHeight || 0;
  var theadH = thead.offsetHeight || 96;
  var footH = footnotes ? (footnotes.offsetHeight || 28) : 28;
  var avail = sheetH - padTop - padBottom - usedAbove - theadH - footH - 8;
  if (avail < 80) avail = 120;

  var chunks = [];
  var cur = [];
  var curH = 0;
  rows.forEach(function (row) {
    var h = row.offsetHeight || 28;
    if (cur.length && curH + h > avail) {
      chunks.push(cur);
      cur = [];
      curH = 0;
    }
    cur.push(row);
    curH += h;
  });
  if (cur.length) chunks.push(cur);

  if (chunks.length <= 1) {
    root.setAttribute('data-sout-info-paginated', '1');
    return;
  }

  // Первая страница — первые строки; продолжения — новые альбомные листы.
  while (tbody.firstChild) tbody.removeChild(tbody.firstChild);
  chunks[0].forEach(function (r) { tbody.appendChild(r); });

  var host = sheet.parentNode || root;
  var insertBefore = sheet.nextSibling;
  var colgroup = table.querySelector('colgroup');
  var footnotesNode = footnotes;

  for (var c = 1; c < chunks.length; c++) {
    var page = document.createElement('div');
    page.className = sheet.className;
    page.setAttribute('data-sout-info-cont', String(c + 1));

    var wrap = document.createElement('div');
    wrap.className = 'sout-info-doc';

    var newTable = table.cloneNode(false);
    Array.prototype.forEach.call(table.attributes || [], function (attr) {
      if (attr.name === 'id') return;
      try { newTable.setAttribute(attr.name, attr.value); } catch (eA) { /* ignore */ }
    });
    if (colgroup) newTable.appendChild(colgroup.cloneNode(true));
    // Без thead на продолжении — шапка только на 1-й странице.
    var newBody = document.createElement('tbody');
    chunks[c].forEach(function (r) { newBody.appendChild(r); });
    newTable.appendChild(newBody);
    wrap.appendChild(newTable);

    // Сноски только на последнем альбомном листе с таблицей.
    if (c === chunks.length - 1 && footnotesNode) {
      wrap.appendChild(footnotesNode);
    }
    page.appendChild(wrap);
    host.insertBefore(page, insertBefore);
  }

  // Если сноски остались на первом листе, а страниц > 1 — убрать с первого.
  if (chunks.length > 1 && footnotesNode && footnotesNode.parentNode) {
    var lastPage = host.querySelector('[data-sout-info-cont="' + chunks.length + '"] .sout-info-doc');
    if (lastPage && footnotesNode.parentNode !== lastPage) {
      lastPage.appendChild(footnotesNode);
    }
  }

  root.setAttribute('data-sout-info-paginated', '1');
}

/**
 * Таблица СОУТ-инфо — альбом A4; хвост — тоже альбом A4 (отдельный лист).
 */
function splitSoutInfoTailToPortraitSheet(root) {
  if (!root || !root.querySelector) return;
  var tail = root.querySelector('.sout-info-tail');
  if (!tail || !tail.parentNode) return;
  // Уже вынесен
  if (tail.closest && tail.closest('.sout-info-tail-sheet')) return;

  var landscapeSheet = tail.closest('.sout-a4-page, .sout-a4-info') || root;
  var host = landscapeSheet.parentNode || root;
  if (!host) return;

  var nextSheet = document.createElement('div');
  nextSheet.className = 'sout-a4-page sout-a4-landscape sout-a4-info sout-a3-info sout-info-tail-sheet';
  nextSheet.setAttribute('data-sout-info-tail-sheet', '1');
  // Переносим хвост целиком
  nextSheet.appendChild(tail);
  // После последнего альбомного листа
  var lastLandscape = host.querySelector('.sout-a4-info:last-of-type, [data-sout-info-cont]:last-of-type') || landscapeSheet;
  if (lastLandscape && lastLandscape.parentNode === host) {
    if (lastLandscape.nextSibling) {
      host.insertBefore(nextSheet, lastLandscape.nextSibling);
    } else {
      host.appendChild(nextSheet);
    }
  } else if (landscapeSheet.nextSibling) {
    host.insertBefore(nextSheet, landscapeSheet.nextSibling);
  } else {
    host.appendChild(nextSheet);
  }
}

/**
 * Уплотняет хвост под один альбомный A4 (как образец).
 */
function packSoutInfoTailSheet(root) {
  var sheet = root && root.querySelector && root.querySelector('.sout-info-tail-sheet');
  var tail = sheet && sheet.querySelector('.sout-info-tail');
  if (!tail) return;

  // Базовые размеры как в образце
  tail.style.fontSize = '10pt';
  tail.style.lineHeight = '1.22';
  Array.prototype.forEach.call(tail.querySelectorAll('.sout-info-rules, .sout-info-rules-lead'), function (el) {
    el.style.fontSize = '10pt';
    el.style.lineHeight = '1.22';
  });
  Array.prototype.forEach.call(tail.querySelectorAll('.sout-info-summary, .sout-info-commission'), function (t) {
    t.style.fontSize = '10pt';
    Array.prototype.forEach.call(t.querySelectorAll('th, td'), function (cell) {
      if (cell.classList && cell.classList.contains('sout-info-c')) {
        cell.style.fontSize = '12pt';
        cell.style.fontWeight = '700';
      } else {
        cell.style.fontSize = '10pt';
      }
      cell.style.padding = '3px 5px';
    });
  });

  // Если не влезает — чуть сжать (до 8.5pt)
  var maxH = sheet.clientHeight || Math.round(210 * 96 / 25.4);
  try {
    var cs = window.getComputedStyle(sheet);
    maxH -= (parseFloat(cs.paddingTop) || 0) + (parseFloat(cs.paddingBottom) || 0);
  } catch (e) { /* ignore */ }

  var sizes = [10, 9.5, 9, 8.5];
  for (var i = 0; i < sizes.length; i++) {
    if (tail.scrollHeight <= maxH + 2) break;
    var pt = sizes[i] + 'pt';
    tail.style.fontSize = pt;
    Array.prototype.forEach.call(tail.querySelectorAll('.sout-info-rules, .sout-info-rules-lead, .sout-info-summary-lead'), function (el) {
      el.style.fontSize = pt;
    });
    Array.prototype.forEach.call(tail.querySelectorAll('.sout-info-summary th, .sout-info-summary td, .sout-info-commission th, .sout-info-commission td'), function (cell) {
      if (!(cell.classList && cell.classList.contains('sout-info-c'))) {
        cell.style.fontSize = pt;
      }
    });
  }
}

window.initSoutInfoEmployeeAutofill = function (options) {
  options = options || {};
  var lookupNode = document.getElementById('sout-info-employee-lookup');
  var printArea = document.getElementById(options.printAreaId || 'sout-preview-print-area')
    || document.getElementById('sout-templates-print-area');
  if (!lookupNode || !printArea) {
    return;
  }

  var employees = [];
  try {
    employees = JSON.parse(lookupNode.textContent || '[]') || [];
  } catch (e) {
    employees = [];
  }

  if (!employees.length) {
    return;
  }

  function normalizeName(value) {
    return String(value || '')
      .toLowerCase()
      .replace(/\./g, ' ')
      .replace(/\s+/g, ' ')
      .trim();
  }

  function extractInitials(parts) {
    return parts
      .filter(Boolean)
      .map(function (p) { return p.charAt(0); })
      .join('');
  }

  function findEmployee(name) {
    var needle = normalizeName(name);
    if (!needle) {
      return null;
    }

    var exact = employees.find(function (item) {
      return normalizeName(item.name) === needle;
    });
    if (exact) {
      return exact;
    }

    var needleParts = needle.split(' ').filter(Boolean);
    var surname = needleParts[0] || '';
    var candidates = employees.filter(function (item) {
      var parts = normalizeName(item.name).split(' ').filter(Boolean);
      return parts.length > 0 && parts[0] === surname;
    });
    if (candidates.length === 1) {
      return candidates[0];
    }
    if (candidates.length === 0) {
      return employees.find(function (item) {
        var n = normalizeName(item.name);
        return n.indexOf(needle) >= 0 || needle.indexOf(n) >= 0;
      }) || null;
    }

    var initials = extractInitials(needleParts.slice(1));
    if (!initials) {
      return candidates[0] || null;
    }

    return candidates.find(function (item) {
      var parts = normalizeName(item.name).split(' ').filter(Boolean);
      var empInitials = extractInitials(parts.slice(1));
      return empInitials.indexOf(initials) === 0 || initials.indexOf(empInitials) === 0;
    }) || null;
  }

  function setTarget(row, field, value) {
    if (!value) {
      return;
    }

    var cell = printArea.querySelector('[data-autofill-target="' + field + '"][data-row="' + row + '"]');
    if (!cell) {
      var key = 'r' + row + ':' + field;
      cell = printArea.querySelector('[data-override-key="' + key + '"]');
    }
    if (cell) {
      cell.textContent = value;
    }
  }

  function setCommPosition(commRow, value) {
    if (!value || !commRow) {
      return;
    }
    var cell = printArea.querySelector('[data-autofill-target="commPosition"][data-comm-row="' + commRow + '"]');
    if (!cell) {
      cell = printArea.querySelector('[data-override-key="infoComm' + commRow + 'Position"]');
    }
    if (cell && !(cell.textContent || '').trim()) {
      cell.textContent = value;
    } else if (cell && value) {
      cell.textContent = value;
    }
  }

  function wireFioCell(cell, onMatch) {
    function applyFromText(text) {
      var match = findEmployee(text);
      if (!match) {
        return;
      }
      cell.textContent = match.name || text;
      onMatch(match);
    }

    cell.addEventListener('focus', function () {
      if (cell.querySelector('input')) {
        return;
      }

      var input = document.createElement('input');
      input.type = 'text';
      input.setAttribute('list', 'sout-info-employees-dl');
      input.value = (cell.textContent || '').trim();
      input.style.width = '100%';
      input.style.border = 'none';
      input.style.outline = 'none';
      input.style.font = 'inherit';
      input.style.background = 'transparent';
      cell.textContent = '';
      cell.appendChild(input);
      input.focus();
      input.select();

      function commit() {
        var value = (input.value || '').trim();
        cell.textContent = value;
        applyFromText(value);
      }

      input.addEventListener('blur', commit);
      input.addEventListener('change', commit);
      input.addEventListener('keydown', function (event) {
        if (event.key === 'Enter') {
          event.preventDefault();
          input.blur();
        }
      });
    });

    cell.addEventListener('blur', function () {
      if (!cell.querySelector('input')) {
        applyFromText(cell.textContent);
      }
    });
  }

  printArea.querySelectorAll('[data-autofill="fio"]').forEach(function (cell) {
    wireFioCell(cell, function (match) {
      var row = cell.getAttribute('data-row');
      if (!row) {
        return;
      }
      setTarget(row, 'profession', match.profession);
      setTarget(row, 'department', match.department);
      setTarget(row, 'snils', match.snils);
      setTarget(row, 'okCode', match.okCode);
    });
    cell.setAttribute('title', 'Введите ФИО из списка сотрудников — подставятся должность, подразделение, СНИЛС и код ОК');
  });

  printArea.querySelectorAll('[data-autofill="fio-comm"]').forEach(function (cell) {
    wireFioCell(cell, function (match) {
      setCommPosition(cell.getAttribute('data-comm-row'), match.profession);
    });
    cell.setAttribute('title', 'Введите ФИО из списка — подставится должность');

    // Уже заполненное ФИО без должности — подтянуть при открытии предпечати
    var existing = (cell.textContent || '').trim();
    if (existing) {
      var match = findEmployee(existing);
      var posCell = printArea.querySelector(
        '[data-autofill-target="commPosition"][data-comm-row="' + (cell.getAttribute('data-comm-row') || '') + '"]'
      );
      if (match && match.profession && posCell && !(posCell.textContent || '').trim()) {
        posCell.textContent = match.profession;
      }
    }
  });
};
