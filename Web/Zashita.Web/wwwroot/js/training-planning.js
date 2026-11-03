document.addEventListener('DOMContentLoaded', function () {
  var grid = document.getElementById('planning-grid');
  var printIdInput = document.getElementById('planning-print-id');
  var selectForm = document.getElementById('planning-select-form');
  var selectedHint = document.getElementById('planning-selected-hint');
  var printMenu = document.getElementById('planning-print-menu');
  var printLinks = document.querySelectorAll('.training-print-link[data-href-template]');
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
      grid.querySelectorAll('.training-planning-row.training-row-active').forEach(function (activeRow) {
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
      selectedHint.textContent = number ? 'Для печати: № ' + number : '';
    }

    printLinks.forEach(function (link) {
      var href = link.getAttribute('data-href-template');
      if (!id || !href) {
        link.classList.add('training-print-disabled');
        link.setAttribute('aria-disabled', 'true');
        link.tabIndex = -1;
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

    if (selectForm && printIdInput) {
      submitFilters(0);
    }
  }

  if (!grid) {
    return;
  }

  grid.addEventListener('click', function (event) {
    if (event.target.closest('a, button, form, input, label')) {
      return;
    }

    var row = event.target.closest('.training-planning-row');
    if (!row) {
      return;
    }

    if (event.detail > 1) {
      var editId = row.getAttribute('data-id');
      if (editId) {
        window.location.href = '/Training/Planning?editId=' + encodeURIComponent(editId);
      }
      return;
    }

    setSelectedRow(row);
  });
});
