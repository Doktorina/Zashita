document.addEventListener('DOMContentLoaded', function () {
  if (typeof window.initRisksDocList === 'function' && document.getElementById('risks-preparation-grid')) {
    initRisksDocList({
      gridId: 'risks-preparation-grid',
      printIdInputId: 'risks-preparation-print-id',
      filterFormId: 'risks-preparation-filter-form',
      selectedHintId: 'risks-preparation-selected-hint',
      printMenuId: 'risks-preparation-print-menu',
      editUrlPrefix: '/Risks/Preparation/Edit?id=',
      autoSelectFirst: true
    });
  }
});

window.initRisksDocList = function (options) {
  var grid = document.getElementById(options.gridId);
  var printIdInput = document.getElementById(options.printIdInputId);
  var selectForm = document.getElementById(options.filterFormId);
  var selectedHint = document.getElementById(options.selectedHintId);
  var printMenu = document.getElementById(options.printMenuId);
  var printLinks = document.querySelectorAll('.risks-print-link[data-href-template]');
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

  function setSelectedRow(row, reloadPage) {
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

    if (reloadPage && selectForm && printIdInput) {
      submitFilters(0);
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
      setSelectedRow(row, true);
    });

    grid.addEventListener('dblclick', function (event) {
      var row = event.target.closest('.sout-preparation-row');
      if (!row || !options.editUrlPrefix) {
        return;
      }
      var id = row.getAttribute('data-id');
      if (id) {
        window.location.href = options.editUrlPrefix + encodeURIComponent(id);
      }
    });

    var initial = grid.querySelector('.sout-preparation-row.training-row-active');
    if (!initial && options.autoSelectFirst) {
      initial = grid.querySelector('.sout-preparation-row');
    }
    if (initial) {
      setSelectedRow(initial, false);
    }
  }
};

window.initRisksPreparationPreview = function (options) {
  var saveBtn = document.getElementById(options.saveBtnId);
  var saveForm = document.getElementById(options.saveFormId);
  var overridesContainer = document.getElementById(options.overridesContainerId);
  var printArea = document.getElementById(options.printAreaId);

  function collectOverrides() {
    if (!overridesContainer || !printArea) {
      return;
    }

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
      valueInput.value = (cell.textContent || '').trim();
      overridesContainer.appendChild(valueInput);
    });
  }

  if (printArea) {
    printArea.addEventListener('click', function (e) {
      var cell = e.target.closest('[data-month-toggle="1"]');
      if (!cell || !printArea.contains(cell)) {
        return;
      }

      e.preventDefault();
      var text = (cell.textContent || '').trim();
      cell.textContent = text === '+' ? '' : '+';
      cell.focus();
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
};
