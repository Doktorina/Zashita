(function () {
  'use strict';

  var root = document.getElementById('med-contingent-hint-root');
  var hint = root ? root.querySelector('.js-contingent-hint') : null;
  var textInput = root ? root.querySelector('.js-contingent-hint-text') : null;
  var styleInput = root ? root.querySelector('.js-contingent-hint-style') : null;
  var activeCell = null;
  var savedRange = null;

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

  function stylesToCss(map) {
    var parts = [];
    if (map['font-weight']) parts.push('font-weight: ' + map['font-weight']);
    if (map.color) parts.push('color: ' + map.color);
    return parts.join('; ');
  }

  function isFormatCell(el) {
    return !!(el && el.classList && el.classList.contains('js-format-cell'));
  }

  function selectionInsideHint() {
    if (!hint) return null;
    var sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) return null;
    var range = sel.getRangeAt(0);
    if (!hint.contains(range.commonAncestorContainer)) return null;
    return range;
  }

  function rememberHintSelection() {
    var range = selectionInsideHint();
    if (range) savedRange = range.cloneRange();
  }

  function restoreHintSelection() {
    if (!savedRange || !hint) return false;
    var sel = window.getSelection();
    if (!sel) return false;
    sel.removeAllRanges();
    sel.addRange(savedRange);
    return true;
  }

  function syncHintFields() {
    if (!hint || !textInput) return;
    textInput.value = hint.innerHTML || '';
    if (styleInput) styleInput.value = '';
    var plain = String(hint.innerText || '').replace(/\u00a0/g, ' ').trim();
    hint.setAttribute('data-empty', plain.length ? '0' : '1');
  }

  function rowStylesInput(el) {
    var row = el.closest('tr');
    if (!row) return null;
    var group = row.getAttribute('data-group');
    if (group == null) return null;
    var first = document.querySelector(
      '#med-contingent-tbody tr.med-contingent-data-row[data-group="' + group + '"]'
    );
    return first ? first.querySelector('.js-cell-styles') : null;
  }

  function persistCellStyle(el) {
    var key = el.getAttribute('data-format-key');
    var input = rowStylesInput(el);
    if (!key || !input) return;
    var all = {};
    try {
      all = input.value ? JSON.parse(input.value) : {};
    } catch (e) {
      all = {};
    }
    if (!all || typeof all !== 'object') all = {};
    var css = stylesToCss(cssToMap(el.getAttribute('style') || ''));
    if (css) all[key] = css;
    else delete all[key];
    input.value = JSON.stringify(all);
  }

  function applyCellMap(el, map) {
    var keepH = el.style.height;
    var keepMin = el.style.minHeight;
    var css = stylesToCss(map);
    if (css) el.setAttribute('style', css);
    else el.removeAttribute('style');
    if (keepH) el.style.height = keepH;
    if (keepMin) el.style.minHeight = keepMin;
    if (map['font-weight']) el.style.setProperty('font-weight', map['font-weight'], 'important');
    else el.style.removeProperty('font-weight');
    if (map.color) el.style.setProperty('color', map.color, 'important');
    else el.style.removeProperty('color');
    persistCellStyle(el);
  }

  function markActiveCell(el) {
    document.querySelectorAll('.js-format-cell.is-format-active').forEach(function (n) {
      n.classList.remove('is-format-active');
    });
    if (el) {
      el.classList.add('is-format-active');
      activeCell = el;
    }
  }

  function applyHintCommand(command, value) {
    if (!hint) return false;
    hint.focus();
    restoreHintSelection();
    var range = selectionInsideHint();
    if (!range || range.collapsed) return false;
    try {
      document.execCommand('styleWithCSS', false, true);
    } catch (e) { /* ignore */ }
    if (command === 'bold') document.execCommand('bold', false, null);
    else if (command === 'foreColor' && value) document.execCommand('foreColor', false, value);
    else if (command === 'removeFormat') {
      document.execCommand('removeFormat', false, null);
      document.execCommand('foreColor', false, 'inherit');
    }
    rememberHintSelection();
    syncHintFields();
    return true;
  }

  function resolveCell() {
    if (activeCell && document.body.contains(activeCell)) return activeCell;
    var focused = document.activeElement;
    return isFormatCell(focused) ? focused : null;
  }

  function applyToCell(mutator) {
    var cell = resolveCell();
    if (!cell) return false;
    markActiveCell(cell);
    var map = cssToMap(cell.getAttribute('style') || '');
    mutator(map);
    applyCellMap(cell, map);
    return true;
  }

  document.addEventListener('focusin', function (e) {
    if (isFormatCell(e.target)) markActiveCell(e.target);
  });

  document.addEventListener('mouseup', function (e) {
    if (isFormatCell(e.target)) markActiveCell(e.target);
    if (hint && hint.contains(e.target)) rememberHintSelection();
  });

  document.addEventListener('keyup', function (e) {
    if (hint && hint.contains(e.target)) rememberHintSelection();
  });

  if (hint) {
    hint.addEventListener('input', syncHintFields);
    hint.addEventListener('blur', function () {
      rememberHintSelection();
      syncHintFields();
    });
  }

  document.addEventListener('mousedown', function (e) {
    var btn = e.target && e.target.closest
      ? e.target.closest('.med-contingent-format-toolbar button')
      : null;
    if (!btn) return;
    var focused = document.activeElement;
    if (isFormatCell(focused)) markActiveCell(focused);
    if (hint && focused === hint) rememberHintSelection();
    e.preventDefault();
  }, true);

  document.addEventListener('click', function (e) {
    var btn = e.target && e.target.closest
      ? e.target.closest('.js-format-bold, .js-format-color, .js-format-reset')
      : null;
    if (!btn || !btn.closest('.med-contingent-format-toolbar')) return;
    e.preventDefault();
    e.stopPropagation();

    // Ячейка таблицы в приоритете, если по ней кликнули / она в фокусе.
    var cell = resolveCell();
    if (cell) {
      if (btn.classList.contains('js-format-bold')) {
        applyToCell(function (map) {
          if (map['font-weight'] === '700' || map['font-weight'] === 'bold') delete map['font-weight'];
          else map['font-weight'] = '700';
        });
        return;
      }
      if (btn.classList.contains('js-format-color')) {
        var color = btn.getAttribute('data-color') || '';
        applyToCell(function (map) {
          if (color && map.color === color) delete map.color;
          else if (color) map.color = color;
        });
        return;
      }
      if (btn.classList.contains('js-format-reset')) {
        applyCellMap(cell, {});
        return;
      }
    }

    var hintRange = selectionInsideHint() || savedRange;
    var useHint = !!(hint && hintRange && !hintRange.collapsed);
    if (useHint) {
      if (btn.classList.contains('js-format-bold')) {
        applyHintCommand('bold');
        return;
      }
      if (btn.classList.contains('js-format-color')) {
        applyHintCommand('foreColor', btn.getAttribute('data-color') || '#b91c1c');
        return;
      }
      if (btn.classList.contains('js-format-reset')) {
        applyHintCommand('removeFormat');
        return;
      }
    }

    window.alert('Кликните в ячейку (факторы / 988н / 29н) или выделите текст над таблицей — затем Ж или цвет.');
  });

  if (hint && textInput) {
    var form = hint.closest('form');
    if (form) form.addEventListener('submit', syncHintFields);
    syncHintFields();
  }

  document.querySelectorAll('#med-contingent-tbody .js-format-cell[style]').forEach(function (el) {
    var map = cssToMap(el.getAttribute('style') || '');
    if (map['font-weight'] || map.color) applyCellMap(el, map);
  });
})();
