window.initSoutAssignmentsList = function (options) {
  var filterForm = document.getElementById(options.filterFormId);
  if (!filterForm) {
    return;
  }

  var searchInput = filterForm.querySelector('input[name="q"]');
  if (!searchInput) {
    return;
  }

  var searchTimer = null;
  searchInput.addEventListener('input', function () {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(function () {
      if (typeof filterForm.requestSubmit === 'function') {
        filterForm.requestSubmit();
      } else {
        filterForm.submit();
      }
    }, 350);
  });

  document.addEventListener('keydown', function (event) {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'f') {
      event.preventDefault();
      searchInput.focus();
      searchInput.select();
    }
  });
};

window.initSoutAssignmentEditor = function (options) {
  var form = document.getElementById(options.formId);
  var linesBody = document.getElementById(options.linesBodyId);
  var lineTemplate = document.getElementById(options.lineTemplateId);
  var addLineBtn = document.getElementById(options.addLineBtnId);
  var fillWorkplacesBtn = document.getElementById(options.fillWorkplacesBtnId);
  var fillDemoBtn = document.getElementById(options.fillDemoBtnId);
  var moveUpBtn = document.getElementById(options.moveUpBtnId);
  var moveDownBtn = document.getElementById(options.moveDownBtnId);
  var lineSearch = document.getElementById(options.lineSearchId);
  var workplaces = Array.isArray(window.soutAssignmentWorkplaces) ? window.soutAssignmentWorkplaces : [];

  if (!form || !linesBody || !lineTemplate) {
    return;
  }

  function getVisibleLines() {
    return Array.prototype.filter.call(
      linesBody.querySelectorAll('.sout-assignment-line-row'),
      function (row) { return !row.id || row.id !== options.lineTemplateId; }
    ).filter(function (row) { return !row.hasAttribute('hidden') && row.style.display !== 'none'; });
  }

  function renumberLines() {
    getVisibleLines().forEach(function (row, index) {
      var cell = row.querySelector('.sout-line-number');
      if (cell) {
        cell.textContent = String(index + 1);
      }
    });
  }

  function findWorkplaceByName(name) {
    var value = (name || '').trim().toLowerCase();
    if (!value) {
      return null;
    }

    return workplaces.find(function (item) {
      return (item.name || '').trim().toLowerCase() === value;
    }) || null;
  }

  function syncWorkplaceFields(row) {
    var nameInput = row.querySelector('.sout-workplace-name-input');
    var idInput = row.querySelector('.sout-workplace-id-input');
    var numberInput = row.querySelector('.sout-workplace-number-input');
    if (!nameInput || !idInput || !numberInput) {
      return;
    }

    var workplace = findWorkplaceByName(nameInput.value);
    if (workplace) {
      idInput.value = workplace.id || '';
      numberInput.value = workplace.number || '';
      return;
    }

    idInput.value = '';
    numberInput.value = '';
  }

  function bindLineRow(row) {
    var deleteBtn = row.querySelector('.sout-line-delete');
    if (deleteBtn && deleteBtn.dataset.bound !== 'true') {
      deleteBtn.dataset.bound = 'true';
      deleteBtn.addEventListener('click', function () {
        var deleteFlag = row.querySelector('.training-delete-flag');
        if (deleteFlag) {
          deleteFlag.checked = true;
        }
        row.style.display = 'none';
        renumberLines();
      });
    }

    var workplaceInput = row.querySelector('.sout-workplace-name-input');
    if (workplaceInput && workplaceInput.dataset.bound !== 'true') {
      workplaceInput.dataset.bound = 'true';
      workplaceInput.addEventListener('change', function () {
        syncWorkplaceFields(row);
      });
      workplaceInput.addEventListener('input', function () {
        syncWorkplaceFields(row);
      });
    }

    row.addEventListener('click', function (event) {
      if (event.target.closest('input, button, select, textarea, label')) {
        return;
      }

      getVisibleLines().forEach(function (line) {
        line.classList.toggle('training-row-active', line === row);
      });
    });
  }

  function getSelectedLine() {
    return linesBody.querySelector('.sout-assignment-line-row.training-row-active:not([hidden])');
  }

  function addLine() {
    var row = lineTemplate.cloneNode(true);
    row.removeAttribute('id');
    row.removeAttribute('hidden');
    row.style.display = '';
    row.setAttribute('data-line-id', 'line-' + Date.now());
    linesBody.appendChild(row);
    bindLineRow(row);
    renumberLines();
    var employeeInput = row.querySelector('.sout-employee-input');
    if (employeeInput) {
      employeeInput.focus();
    }
  }

  function setRowWorkplace(row, workplace) {
    if (!row || !workplace) {
      return;
    }
    var nameInput = row.querySelector('.sout-workplace-name-input');
    if (nameInput) {
      nameInput.value = workplace.name || '';
    }
    syncWorkplaceFields(row);
  }

  function setRowEmployee(row, employee) {
    var employeeInput = row.querySelector('.sout-employee-input');
    if (employeeInput) {
      employeeInput.value = employee || '';
    }
  }

  function setRowAssignedFrom(row, value) {
    var fromInput = row.querySelector('input[name="assignedFrom"]');
    if (fromInput && value) {
      fromInput.value = value;
    }
  }

  function getDocDateForLine() {
    var input = form.querySelector('input[name="date"]');
    if (!input || !input.value) {
      return '';
    }
    // yyyy-MM-ddTHH:mm:ss -> yyyy-MM-dd
    return String(input.value).slice(0, 10);
  }

  function getDepartment() {
    var input = form.querySelector('input[name="department"]');
    return input ? (input.value || '').trim() : '';
  }

  function normalizeText(value) {
    return String(value || '').trim().toLowerCase();
  }

  function uniqueById(items) {
    var seen = {};
    return items.filter(function (x) {
      var id = x && x.id ? String(x.id) : '';
      if (!id) {
        return false;
      }
      if (seen[id]) {
        return false;
      }
      seen[id] = true;
      return true;
    });
  }

  function fillFromWorkplaces() {
    var dept = getDepartment();
    if (!dept) {
      alert('Сначала выберите подразделение в шапке документа.');
      return;
    }

    var deptLower = normalizeText(dept);
    var matches = workplaces.filter(function (w) {
      return normalizeText(w.department) === deptLower;
    });
    matches = uniqueById(matches);
    if (matches.length === 0) {
      alert('Не найдено рабочих мест для выбранного подразделения.');
      return;
    }

    if (!confirm('Заполнить табличную часть рабочими местами выбранного подразделения?')) {
      return;
    }

    // clear existing visible lines
    getVisibleLines().forEach(function (row) {
      var deleteFlag = row.querySelector('.training-delete-flag');
      if (deleteFlag) {
        deleteFlag.checked = true;
      }
      row.style.display = 'none';
    });

    var fromDate = getDocDateForLine();
    matches.forEach(function (w, idx) {
      addLine();
      var row = getVisibleLines()[getVisibleLines().length - 1];
      setRowWorkplace(row, w);
      setRowEmployee(row, '');
      setRowAssignedFrom(row, fromDate);
    });
    renumberLines();
  }

  function findWorkplaceByProfessionPart(part) {
    var needle = normalizeText(part);
    if (!needle) {
      return null;
    }
    return workplaces.find(function (w) {
      return normalizeText(w.profession).indexOf(needle) >= 0 || normalizeText(w.name).indexOf(needle) >= 0;
    }) || null;
  }

  function fillDemo() {
    // Демо-ФИО отключены — сотрудники только из штата 1С.
  }

  function moveSelectedLine(direction) {
    var selected = getSelectedLine();
    if (!selected) {
      return;
    }

    var lines = getVisibleLines();
    var index = lines.indexOf(selected);
    if (index < 0) {
      return;
    }

    var targetIndex = direction < 0 ? index - 1 : index + 1;
    if (targetIndex < 0 || targetIndex >= lines.length) {
      return;
    }

    var target = lines[targetIndex];
    if (direction < 0) {
      linesBody.insertBefore(selected, target);
    } else {
      linesBody.insertBefore(target, selected);
    }

    renumberLines();
  }

  getVisibleLines().forEach(bindLineRow);
  renumberLines();

  if (addLineBtn) {
    addLineBtn.addEventListener('click', addLine);
  }

  if (fillWorkplacesBtn) {
    fillWorkplacesBtn.addEventListener('click', fillFromWorkplaces);
  }

  if (fillDemoBtn) {
    fillDemoBtn.addEventListener('click', fillDemo);
  }

  if (moveUpBtn) {
    moveUpBtn.addEventListener('click', function () {
      moveSelectedLine(-1);
    });
  }

  if (moveDownBtn) {
    moveDownBtn.addEventListener('click', function () {
      moveSelectedLine(1);
    });
  }

  if (lineSearch) {
    lineSearch.addEventListener('input', function () {
      var query = lineSearch.value.trim().toLowerCase();
      getVisibleLines().forEach(function (row) {
        var text = row.textContent.toLowerCase();
        row.classList.toggle('sout-assignment-line-hidden', query && text.indexOf(query) === -1);
      });
    });

    document.addEventListener('keydown', function (event) {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'f') {
        event.preventDefault();
        lineSearch.focus();
        lineSearch.select();
      }
    });
  }

  document.querySelectorAll('.sout-assignment-tab').forEach(function (tab) {
    tab.addEventListener('click', function () {
      var tabName = tab.getAttribute('data-tab');
      document.querySelectorAll('.sout-assignment-tab').forEach(function (button) {
        button.classList.toggle('sout-assignment-tab-active', button === tab);
      });
      document.querySelectorAll('.sout-assignment-tab-panel').forEach(function (panel) {
        var isActive = panel.getAttribute('data-tab-panel') === tabName;
        panel.classList.toggle('sout-assignment-tab-panel-active', isActive);
        panel.hidden = !isActive;
      });
    });
  });
};
