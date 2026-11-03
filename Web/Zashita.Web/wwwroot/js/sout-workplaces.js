window.initSoutWorkplaces = function (options) {
  var filterForm = document.getElementById(options.filterFormId);
  var treeBody = options.treeBodyId ? document.getElementById(options.treeBodyId) : null;
  var departmentTree = options.departmentTreeId ? document.getElementById(options.departmentTreeId) : null;
  var initialDepartmentId = options.departmentId || '';

  function findDepartmentRow(departmentId) {
    if (!treeBody || !departmentId) {
      return null;
    }

    return treeBody.querySelector('.sout-workplace-row[data-is-group="true"][data-id="' + departmentId + '"]');
  }

  function setDepartmentActive(departmentId) {
    if (!departmentTree) {
      return;
    }

    departmentTree.querySelectorAll('.sout-department-link').forEach(function (link) {
      var id = link.getAttribute('data-department-id') || '';
      link.classList.toggle('sout-department-active', id === (departmentId || ''));
    });
  }

  function focusDepartment(departmentId, updateUrl, scrollToRow) {
    setDepartmentActive(departmentId || '');

    if (treeBody) {
      treeBody.querySelectorAll('.sout-workplace-row.sout-department-focus').forEach(function (row) {
        row.classList.remove('sout-department-focus');
      });
    }

    var row = findDepartmentRow(departmentId);
    if (row) {
      row.classList.add('sout-department-focus');
      // Не сворачиваем ветку при выборе — только раскрываем и прокручиваем.
      row.classList.add('training-tree-open');
      row.classList.remove('training-tree-hidden');
      var toggle = row.querySelector('.training-tree-toggle');
      if (toggle) {
        toggle.setAttribute('aria-expanded', 'true');
      }
      // При сохранении/перезагрузке не прыгаем к подразделению — только при клике в дереве.
      if (scrollToRow) {
        row.scrollIntoView({ block: 'center', behavior: 'smooth' });
      }
    }

    if (updateUrl && window.history && window.history.replaceState) {
      try {
        var url = new URL(window.location.href);
        if (departmentId) {
          url.searchParams.set('departmentId', departmentId);
        } else {
          url.searchParams.delete('departmentId');
        }
        window.history.replaceState({}, '', url.toString());
      } catch (e) {
        // ignore
      }
    }

    if (filterForm) {
      var hidden = filterForm.querySelector('input[name="departmentId"]');
      if (departmentId) {
        if (!hidden) {
          hidden = document.createElement('input');
          hidden.type = 'hidden';
          hidden.name = 'departmentId';
          filterForm.appendChild(hidden);
        }
        hidden.value = departmentId;
      } else if (hidden) {
        hidden.remove();
      }
    }
  }

  if (departmentTree) {
    departmentTree.addEventListener('click', function (event) {
      var link = event.target.closest('.sout-department-link');
      if (!link) {
        return;
      }

      event.preventDefault();
      focusDepartment(link.getAttribute('data-department-id') || '', true, true);
    });
  }

  if (initialDepartmentId) {
    focusDepartment(initialDepartmentId, false, false);
  } else {
    setDepartmentActive('');
  }

  function syncDepartmentFromGroup(row) {
    if (!row || row.getAttribute('data-is-group') !== 'true') {
      return;
    }

    var nameInput = row.querySelector('input[name="Name"]');
    var departmentInput = row.querySelector('.sout-department-input');
    if (!nameInput || !departmentInput) {
      return;
    }

    var department = nameInput.value.trim();
    departmentInput.value = department;
    row.setAttribute('data-department', department);
  }

  function bindUsedToggle(row) {
    var toggle = row.querySelector('.sout-is-used-toggle');
    var hidden = row.querySelector('.sout-is-used-input');
    if (!toggle || !hidden || toggle.dataset.bound === 'true') {
      return;
    }

    toggle.dataset.bound = 'true';
    toggle.addEventListener('change', function () {
      hidden.value = toggle.checked ? 'true' : 'false';
    });
  }

  function bindDeclarationToggle(row) {
    var toggle = row.querySelector('.sout-declaration-toggle');
    var hidden = row.querySelector('.sout-declaration-input');
    if (!toggle || !hidden || toggle.dataset.bound === 'true') {
      return;
    }

    toggle.dataset.bound = 'true';
    toggle.addEventListener('change', function () {
      hidden.value = toggle.checked ? 'true' : 'false';
    });
  }

  function bindGroupName(row) {
    var nameInput = row.querySelector('input[name="Name"]');
    if (!nameInput || nameInput.dataset.soutBound === 'true') {
      return;
    }

    nameInput.dataset.soutBound = 'true';
    nameInput.addEventListener('input', function () {
      syncDepartmentFromGroup(row);
    });
  }

  function bindProfessionSync(row) {
    if (!options.professionSync || !row || row.getAttribute('data-is-group') === 'true') {
      return;
    }

    var nameInput = row.querySelector('input[name="Name"]');
    var professionInput = row.querySelector('input[name="Profession"]');
    if (!nameInput || !professionInput || nameInput.dataset.professionSync === 'true') {
      return;
    }

    nameInput.dataset.professionSync = 'true';
    professionInput.dataset.professionSync = 'true';
    nameInput.addEventListener('input', function () {
      professionInput.value = nameInput.value;
    });
    professionInput.addEventListener('input', function () {
      nameInput.value = professionInput.value;
    });
  }

  function bindCardRow(row) {
    bindUsedToggle(row);
    bindDeclarationToggle(row);
    bindGroupName(row);
    bindProfessionSync(row);
    syncDepartmentFromGroup(row);
  }

  window.bindSoutCardRow = bindCardRow;

  document.querySelectorAll('.sout-workplace-row').forEach(function (row) {
    bindCardRow(row);
  });

  document.addEventListener('click', function (event) {
    var deleteBtn = event.target.closest('[data-action="delete"]');
    if (!deleteBtn) {
      return;
    }

    var row = deleteBtn.closest('.sout-workplace-row');
    if (row) {
      bindUsedToggle(row);
      bindGroupName(row);
    }
  });

  if (filterForm) {
    var usedCheckbox = filterForm.querySelector('input[name="usedOnly"]');
    if (usedCheckbox) {
      usedCheckbox.addEventListener('change', function () {
        if (typeof filterForm.requestSubmit === 'function') {
          filterForm.requestSubmit();
        } else {
          filterForm.submit();
        }
      });
    }

    var searchInput = filterForm.querySelector('input[name="q"]');
    if (searchInput) {
      document.addEventListener('keydown', function (event) {
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'f') {
          event.preventDefault();
          searchInput.focus();
          searchInput.select();
        }
      });
    }
  }
};
