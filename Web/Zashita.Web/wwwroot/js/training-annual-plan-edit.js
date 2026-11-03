(function () {
  function syncRowMonths(row) {
    var hidden = row.querySelector('.training-plan-months-hidden');
    if (!hidden) {
      return;
    }

    var months = Array.prototype.slice.call(row.querySelectorAll('.training-plan-month-cb:checked'))
      .map(function (cb) { return cb.getAttribute('data-month'); })
      .filter(Boolean)
      .sort(function (a, b) { return Number(a) - Number(b); });
    hidden.value = months.join(',');
  }

  function syncAllMonths(tbody) {
    tbody.querySelectorAll('tr.training-plan-edit-row').forEach(syncRowMonths);
  }

  function reindex(tbody) {
    var prefix = tbody.getAttribute('data-prefix') || 'General';
    tbody.querySelectorAll('tr.training-plan-edit-row').forEach(function (row, index) {
      row.querySelectorAll('[name]').forEach(function (input) {
        var name = input.getAttribute('name');
        if (!name) {
          return;
        }
        input.setAttribute('name', name.replace(/\[\d+\]/, '[' + index + ']'));
      });
      syncRowMonths(row);
    });
  }

  function initTable(tbody, addButtonId) {
    if (!tbody) {
      return;
    }

    tbody.addEventListener('change', function (event) {
      if (event.target.classList.contains('training-plan-month-cb')) {
        syncRowMonths(event.target.closest('tr'));
      }
    });

    tbody.addEventListener('click', function (event) {
      var button = event.target.closest('[data-action="remove-plan-row"]');
      if (!button) {
        return;
      }
      var rows = tbody.querySelectorAll('tr.training-plan-edit-row');
      if (rows.length <= 1) {
        return;
      }
      button.closest('tr').remove();
      reindex(tbody);
    });

    var addBtn = document.getElementById(addButtonId);
    if (!addBtn) {
      return;
    }

    addBtn.addEventListener('click', function () {
      var rows = tbody.querySelectorAll('tr.training-plan-edit-row');
      var source = rows[rows.length - 1];
      var row = source.cloneNode(true);
      row.querySelectorAll('input.ppe-input').forEach(function (input) {
        if (input.name.indexOf('HeadCount') >= 0) {
          input.value = '1';
        } else {
          input.value = '';
        }
      });
      row.querySelectorAll('.training-plan-month-cb').forEach(function (cb) {
        cb.checked = false;
      });
      var hidden = row.querySelector('.training-plan-months-hidden');
      if (hidden) {
        hidden.value = '';
      }
      tbody.appendChild(row);
      reindex(tbody);
    });
  }

  document.addEventListener('DOMContentLoaded', function () {
    initTable(document.getElementById('general-rows-tbody'), 'general-add-row-btn');
    initTable(document.getElementById('profession-rows-tbody'), 'profession-add-row-btn');

    document.querySelectorAll('.training-plan-edit-form').forEach(function (form) {
      form.addEventListener('submit', function () {
        form.querySelectorAll('tbody[data-prefix]').forEach(syncAllMonths);
      });
    });
  });
})();
