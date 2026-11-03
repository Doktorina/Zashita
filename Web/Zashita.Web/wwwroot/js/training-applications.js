document.addEventListener('DOMContentLoaded', function () {
  var filterForm = document.getElementById('application-filter-form');
  var grid = document.getElementById('application-grid');
  var printIdInput = document.getElementById('application-print-id');
  var selectedHint = document.getElementById('application-selected-hint');
  var printLinks = document.querySelectorAll('.training-print-link[data-href-template]');
  var filterTimer = null;

  function submitFilters(delayMs) {
    if (!filterForm) {
      return;
    }

    clearTimeout(filterTimer);
    filterTimer = setTimeout(function () {
      if (typeof filterForm.requestSubmit === 'function') {
        filterForm.requestSubmit();
      } else {
        filterForm.submit();
      }
    }, typeof delayMs === 'number' ? delayMs : 350);
  }

  if (filterForm) {
    filterForm.querySelectorAll('.js-auto-filter').forEach(function (field) {
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

    var downloadName = row.getAttribute('data-download-name') || '';
    printLinks.forEach(function (link) {
      var href = link.getAttribute('data-href-template');
      if (!id || !href) {
        link.href = '#';
        link.classList.add('training-print-disabled');
        link.setAttribute('aria-disabled', 'true');
        link.tabIndex = -1;
        if (link.getAttribute('data-download-role') === 'save') {
          link.removeAttribute('download');
        }
        return;
      }

      link.href = href.replace('{id}', encodeURIComponent(id));
      link.classList.remove('training-print-disabled');
      link.removeAttribute('aria-disabled');
      link.tabIndex = 0;
      if (link.getAttribute('data-download-role') === 'save') {
        if (downloadName) {
          link.setAttribute('download', downloadName);
        } else {
          link.removeAttribute('download');
        }
      }
    });

    if (filterForm && printIdInput) {
      submitFilters(0);
    }
  }

  if (grid) {
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
          window.location.href = '/Training/Applications?editId=' + encodeURIComponent(editId) + '#application-form-panel';
        }
        return;
      }

      setSelectedRow(row);
    });
  }

  var body = document.getElementById('application-participants-body');
  var addBtn = document.getElementById('application-add-participant');
  var agreementBody = document.getElementById('application-agreement-body');
  var agreementAddBtn = document.getElementById('application-add-agreement');
  var employeeSearch = document.getElementById('application-employee-search');
  var employeeAddBtn = document.getElementById('application-employee-add');
  var employeeDataNode = document.getElementById('application-employee-data');
  var kindInput = document.getElementById('application-training-kind');
  var employees = [];
  if (employeeDataNode) {
    try {
      employees = JSON.parse(employeeDataNode.textContent || '[]') || [];
    } catch (e) {
      employees = [];
    }
  }

  function currentDirection() {
    return (kindInput && kindInput.value ? kindInput.value : '').trim() || 'Охрана труда';
  }

  function findEmployeeByName(name) {
    var needle = (name || '').trim().toLowerCase();
    if (!needle) {
      return null;
    }
    return employees.find(function (item) {
      return (item.name || '').trim().toLowerCase() === needle;
    }) || employees.find(function (item) {
      return (item.name || '').toLowerCase().indexOf(needle) >= 0;
    }) || null;
  }

  function isParticipantRowEmpty(row) {
    if (!row) {
      return false;
    }
    var name = (row.querySelector('[name="participantFullName"]') || {}).value || '';
    var position = (row.querySelector('[name="participantPosition"]') || {}).value || '';
    var birth = (row.querySelector('[name="participantBirthDate"]') || {}).value || '';
    var employment = (row.querySelector('[name="participantEmploymentDate"]') || {}).value || '';
    var snils = (row.querySelector('[name="participantSnils"]') || {}).value || '';
    return !(name.trim() || position.trim() || birth.trim() || employment.trim() || snils.trim());
  }

  function findFirstEmptyParticipantRow() {
    if (!body) {
      return null;
    }
    var rows = body.querySelectorAll('.application-participant-row');
    for (var i = 0; i < rows.length; i++) {
      if (isParticipantRowEmpty(rows[i])) {
        return rows[i];
      }
    }
    return null;
  }

  function fillParticipantRow(row, data) {
    if (!row) {
      return;
    }
    var direction = (data && data.direction) || currentDirection();
    var directionInput = row.querySelector('[name="participantTrainingDirection"]');
    if (directionInput) {
      directionInput.value = direction;
    }
    if (!data) {
      return;
    }
    row.querySelector('[name="participantFullName"]').value = data.name || '';
    row.querySelector('[name="participantPosition"]').value = data.profession || '';
    row.querySelector('[name="participantSnils"]').value = data.snils || '';
    row.querySelector('[name="participantBirthDate"]').value = data.birth || '';
    row.querySelector('[name="participantEmploymentDate"]').value = data.employment || '';
  }

  function addEmployeeFromSearch() {
    if (!employeeSearch) {
      return;
    }
    var match = findEmployeeByName(employeeSearch.value);
    if (!match) {
      window.alert('Сотрудник не найден. Выберите ФИО из списка подсказок.');
      return;
    }
    var payload = {
      name: match.name,
      profession: match.profession,
      snils: match.snils,
      birth: match.birth,
      employment: match.employment,
      direction: currentDirection()
    };
    var emptyRow = findFirstEmptyParticipantRow();
    if (emptyRow) {
      fillParticipantRow(emptyRow, payload);
    } else {
      appendRow(payload);
    }
    var dept = document.querySelector('#application-form-panel [name="department"]');
    if (dept && match.department) {
      dept.value = match.department;
    }
    employeeSearch.value = '';
    employeeSearch.focus();
  }

  function removeRow(button) {
    var rows = body ? body.querySelectorAll('.application-participant-row') : [];
    if (!body || rows.length <= 1) {
      return;
    }
    var row = button.closest('.application-participant-row');
    if (row) {
      row.remove();
    }
  }

  function wireRow(row) {
    if (!row) {
      return;
    }
    var btn = row.querySelector('.application-remove-participant');
    if (btn) {
      btn.addEventListener('click', function () { removeRow(btn); });
    }
  }

  function removeAgreementRow(button) {
    var rows = agreementBody ? agreementBody.querySelectorAll('.application-agreement-row') : [];
    if (!agreementBody || rows.length <= 1) {
      var only = button && button.closest('.application-agreement-row');
      if (only) {
        only.querySelectorAll('input').forEach(function (input) { input.value = ''; });
      }
      return;
    }
    var row = button.closest('.application-agreement-row');
    if (row) {
      row.remove();
    }
  }

  function wireAgreementRow(row) {
    if (!row) {
      return;
    }
    var btn = row.querySelector('.application-remove-agreement');
    if (btn) {
      btn.addEventListener('click', function () { removeAgreementRow(btn); });
    }
  }

  function appendAgreementRow(data) {
    if (!agreementBody) {
      return null;
    }
    var row = document.createElement('tr');
    row.className = 'application-agreement-row';
    row.innerHTML =
      '<td><input name="agreementFullName" class="ppe-input" /></td>' +
      '<td><input name="agreementPosition" class="ppe-input" /></td>' +
      '<td><button type="button" class="btn btn-outline btn-sm application-remove-agreement">Удалить</button></td>';
    agreementBody.appendChild(row);
    wireAgreementRow(row);
    if (data) {
      row.querySelector('[name="agreementFullName"]').value = data.name || '';
      row.querySelector('[name="agreementPosition"]').value = data.position || '';
    }
    return row;
  }

  function appendRow(data) {
    if (!body) {
      return null;
    }
    var row = document.createElement('tr');
    row.className = 'application-participant-row';
    row.innerHTML =
      '<td><input name="participantFullName" class="ppe-input" /></td>' +
      '<td><input name="participantPosition" class="ppe-input" /></td>' +
      '<td><input type="date" name="participantBirthDate" class="ppe-input" /></td>' +
      '<td><input type="date" name="participantEmploymentDate" class="ppe-input" /></td>' +
      '<td><input name="participantSnils" class="ppe-input" placeholder="000-000-000 00" /></td>' +
      '<td>' +
      '<input type="hidden" name="participantTrainingDirection" />' +
      '<button type="button" class="btn btn-outline btn-sm application-remove-participant">Удалить</button>' +
      '</td>';
    body.appendChild(row);
    wireRow(row);
    fillParticipantRow(row, data || { direction: currentDirection() });
    return row;
  }

  if (body) {
    body.querySelectorAll('.application-participant-row').forEach(wireRow);
  }

  if (agreementBody) {
    agreementBody.querySelectorAll('.application-agreement-row').forEach(wireAgreementRow);
  }

  if (addBtn) {
    addBtn.addEventListener('click', function () {
      appendRow({ direction: currentDirection() });
    });
  }

  if (agreementAddBtn) {
    agreementAddBtn.addEventListener('click', function () {
      appendAgreementRow({});
    });
  }

  if (employeeAddBtn) {
    employeeAddBtn.addEventListener('click', addEmployeeFromSearch);
  }
  if (employeeSearch) {
    employeeSearch.addEventListener('keydown', function (event) {
      if (event.key === 'Enter') {
        event.preventDefault();
        addEmployeeFromSearch();
      }
    });
  }

  if (kindInput) {
    kindInput.addEventListener('change', function () {
      var direction = currentDirection();
      var contentInput = document.getElementById('application-content');
      if (contentInput && direction) {
        contentInput.value = 'Планирование: ' + direction;
      }
      if (!body) {
        return;
      }
      body.querySelectorAll('[name="participantTrainingDirection"]').forEach(function (input) {
        if (!input.value || !input.value.trim()) {
          input.value = direction;
        }
      });
    });
  }

  if (window.location.hash === '#application-form-panel') {
    var panel = document.getElementById('application-form-panel');
    if (panel) {
      panel.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  }
});
