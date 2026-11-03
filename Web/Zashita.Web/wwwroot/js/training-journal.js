(function () {
  function requiresReason(type) {
    var value = (type || '').toLowerCase();
    return value.indexOf('внеплан') >= 0 || value.indexOf('целев') >= 0;
  }

  function syncReasonHint() {
    var select = document.getElementById('journal-briefing-type');
    var hint = document.getElementById('journal-reason-hint');
    if (!select || !hint) {
      return;
    }

    hint.classList.toggle('is-hidden', !requiresReason(select.value));
  }

  function syncRowReason(row) {
    if (!row) {
      return;
    }

    var typeSelect = row.querySelector('.journal-row-briefing-type');
    var reasonInput = row.querySelector('.journal-row-briefing-reason');
    if (!typeSelect || !reasonInput) {
      return;
    }

    var needed = requiresReason(typeSelect.value);
    reasonInput.readOnly = !needed;
    if (!needed) {
      if (!reasonInput.value || reasonInput.value.trim() === '') {
        reasonInput.value = '—';
      }
    } else if (reasonInput.value.trim() === '—') {
      reasonInput.value = '';
    }

    var cell = reasonInput.closest('td');
    if (cell) {
      cell.classList.toggle('journal-col-muted', !needed);
    }
  }

  document.addEventListener('DOMContentLoaded', function () {
    syncReasonHint();
    var typeSelect = document.getElementById('journal-briefing-type');
    if (typeSelect) {
      typeSelect.addEventListener('change', syncReasonHint);
    }

    var grid = document.getElementById('journal-edit-grid');
    if (!grid) {
      return;
    }

    grid.querySelectorAll('tbody tr').forEach(syncRowReason);
    grid.addEventListener('change', function (e) {
      var target = e.target;
      if (!target || !target.classList || !target.classList.contains('journal-row-briefing-type')) {
        return;
      }
      syncRowReason(target.closest('tr'));
    });
  });
})();
