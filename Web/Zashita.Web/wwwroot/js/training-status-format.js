(function () {
  'use strict';

  var activeCell = null;
  var lastSelection = null;

  function parseStyles(raw) {
    try {
      var obj = raw ? JSON.parse(raw) : {};
      return obj && typeof obj === 'object' ? obj : {};
    } catch (e) {
      return {};
    }
  }

  function stylesToCss(map) {
    var parts = [];
    if (map['font-weight']) parts.push('font-weight: ' + map['font-weight'] + ' !important');
    if (map.color) parts.push('color: ' + map.color + ' !important');
    return parts.join('; ');
  }

  function cssToMap(css) {
    var map = {};
    String(css || '').split(';').forEach(function (chunk) {
      var i = chunk.indexOf(':');
      if (i <= 0) return;
      var prop = chunk.slice(0, i).trim().toLowerCase();
      var val = chunk.slice(i + 1).trim().replace(/\s*!important\s*$/i, '').trim();
      if (prop === 'font-weight' || prop === 'color') map[prop] = val;
    });
    return map;
  }

  function readFormatMap(el) {
    var fromAttr = cssToMap(el.getAttribute('style') || '');
    if (fromAttr.color || fromAttr['font-weight']) {
      return fromAttr;
    }
    var map = {};
    var weight = window.getComputedStyle(el).fontWeight;
    if (weight === '700' || weight === 'bold' || Number(weight) >= 600) {
      map['font-weight'] = '700';
    }
    if (el.style.color) {
      map.color = el.style.color;
    }
    return map;
  }

  function rowStylesInput(el) {
    var row = el.closest('tr');
    return row ? row.querySelector('.js-cell-styles') : null;
  }

  function persistCellStyle(el) {
    var key = el.getAttribute('data-format-key');
    var input = rowStylesInput(el);
    if (!key || !input) return;
    var all = parseStyles(input.value);
    var css = stylesToCss(cssToMap(el.getAttribute('style') || ''));
    if (css) {
      all[key] = css.replace(/\s*!important/gi, '');
    } else {
      delete all[key];
    }
    input.value = JSON.stringify(all);
  }

  function applyFormatMap(el, map) {
    var keepHeight = el.style.height;
    var keepMinHeight = el.style.minHeight;
    var css = stylesToCss(map);
    if (css) {
      el.setAttribute('style', css);
    } else {
      el.removeAttribute('style');
    }
    if (keepHeight) el.style.height = keepHeight;
    if (keepMinHeight) el.style.minHeight = keepMinHeight;
    if (map['font-weight']) {
      el.style.setProperty('font-weight', map['font-weight'], 'important');
    } else {
      el.style.removeProperty('font-weight');
    }
    if (map.color) {
      el.style.setProperty('color', map.color, 'important');
    } else {
      el.style.removeProperty('color');
    }
    persistCellStyle(el);
  }

  function isFormatCell(el) {
    return !!(el && el.classList && el.classList.contains('js-format-cell'));
  }

  function rememberSelection(el) {
    if (!el || (el.tagName !== 'TEXTAREA' && el.tagName !== 'INPUT')) {
      lastSelection = null;
      return;
    }
    try {
      lastSelection = {
        el: el,
        start: el.selectionStart,
        end: el.selectionEnd
      };
    } catch (_) {
      lastSelection = null;
    }
  }

  function restoreSelection() {
    if (!lastSelection || !lastSelection.el || !document.contains(lastSelection.el)) {
      return;
    }
    var el = lastSelection.el;
    try {
      el.focus({ preventScroll: true });
      if (typeof el.setSelectionRange === 'function'
          && lastSelection.start != null
          && lastSelection.end != null) {
        el.setSelectionRange(lastSelection.start, lastSelection.end);
      }
    } catch (_) { /* ignore */ }
  }

  function resolveTargetCell() {
    if (lastSelection && lastSelection.el && document.contains(lastSelection.el) && isFormatCell(lastSelection.el)) {
      return lastSelection.el;
    }
    if (activeCell && document.contains(activeCell) && isFormatCell(activeCell)) {
      return activeCell;
    }
    var focused = document.activeElement;
    if (focused && isFormatCell(focused)) {
      return focused;
    }
    if (focused && focused.closest) {
      var fromFocus = focused.closest('.js-format-cell');
      if (fromFocus) return fromFocus;
    }
    return document.querySelector('#training-status-grid .js-format-cell.is-format-active');
  }

  function markActive(cell) {
    if (!cell || !isFormatCell(cell)) return;
    activeCell = cell;
    document.querySelectorAll('.js-format-cell.is-format-active').forEach(function (n) {
      n.classList.remove('is-format-active');
    });
    cell.classList.add('is-format-active');
    rememberSelection(cell);
  }

  function applyToActive(mutator) {
    var cell = resolveTargetCell();
    if (!cell) {
      window.alert('Выделите текст или кликните в поле: номер протокола, программа, комментарий или объект — затем нажмите Ж / цвет.');
      return;
    }
    markActive(cell);
    var map = readFormatMap(cell);
    mutator(map);
    applyFormatMap(cell, map);
    // вернуть выделение, чтобы было видно, что действие сработало
    window.requestAnimationFrame(restoreSelection);
  }

  document.addEventListener('focusin', function (e) {
    var cell = e.target && e.target.closest ? e.target.closest('.js-format-cell') : null;
    if (cell) markActive(cell);
  });

  document.addEventListener('select', function (e) {
    if (e.target && isFormatCell(e.target)) {
      markActive(e.target);
      rememberSelection(e.target);
    }
  }, true);

  document.addEventListener('mouseup', function (e) {
    var cell = e.target && e.target.closest ? e.target.closest('.js-format-cell') : null;
    if (cell) {
      markActive(cell);
      rememberSelection(cell);
    }
  });

  document.addEventListener('keyup', function (e) {
    if (e.target && isFormatCell(e.target)) {
      rememberSelection(e.target);
    }
  });

  document.addEventListener('click', function (e) {
    var cell = e.target && e.target.closest ? e.target.closest('.js-format-cell') : null;
    if (cell) markActive(cell);
  });

  document.addEventListener('mousedown', function (e) {
    var btn = e.target && e.target.closest
      ? e.target.closest('#training-format-toolbar button, .training-format-toolbar button')
      : null;
    if (btn) {
      // сохранить выделение до потери фокуса и не снимать фокус с поля
      var focused = document.activeElement;
      if (focused && isFormatCell(focused)) {
        rememberSelection(focused);
        markActive(focused);
      }
      e.preventDefault();
    }
  }, true);

  document.addEventListener('click', function (e) {
    var btn = e.target && e.target.closest ? e.target.closest('button') : null;
    if (!btn || !btn.closest('#training-format-toolbar, .training-format-toolbar')) {
      return;
    }

    e.preventDefault();
    e.stopPropagation();

    if (btn.classList.contains('js-format-bold')) {
      applyToActive(function (map) {
        if (map['font-weight'] === '700' || map['font-weight'] === 'bold') {
          delete map['font-weight'];
        } else {
          map['font-weight'] = '700';
        }
      });
      return;
    }

    if (btn.classList.contains('js-format-color')) {
      var color = btn.getAttribute('data-color') || '';
      applyToActive(function (map) {
        if (map.color === color) delete map.color;
        else map.color = color;
      });
      return;
    }

    if (btn.classList.contains('js-format-reset')) {
      applyToActive(function (map) {
        Object.keys(map).forEach(function (k) { delete map[k]; });
      });
    }
  });

  var importInput = document.querySelector('#training-schedule-import .training-schedule-import-input');
  if (importInput) {
    importInput.addEventListener('change', function () {
      if (importInput.files && importInput.files.length > 0 && importInput.form) {
        importInput.form.submit();
      }
    });
  }
})();
