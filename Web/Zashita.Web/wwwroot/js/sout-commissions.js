window.initSoutCommissions = function (options) {
  var editBase = options.editBase || '/Sout/Commissions/Edit';
  var previewBase = options.previewBase || '/Sout/Commissions/Preview';
  var selectBase = options.selectBase || '/Sout/Commissions/SelectCommission';

  function getParentContext() {
    var treeBody = document.getElementById(options.treeBodyId);
    if (!treeBody) {
      return { parentId: '' };
    }

    var checked = treeBody.querySelector('.training-row-select:checked');
    if (!checked) {
      return { parentId: '' };
    }

    var row = checked.closest('.training-tree-row');
    if (!row) {
      return { parentId: '' };
    }

    if (row.classList.contains('training-tree-folder')) {
      return { parentId: row.getAttribute('data-id') || row.querySelector('input[name="Id"]')?.value || '' };
    }

    return { parentId: row.getAttribute('data-parent-id') || row.querySelector('input[name="ParentId"]')?.value || '' };
  }

  function openEdit(isGroup) {
    var context = getParentContext();
    var url = editBase + (isGroup ? '?isGroup=true' : '');
    if (context.parentId) {
      url += (url.indexOf('?') >= 0 ? '&' : '?') + 'parentId=' + encodeURIComponent(context.parentId);
    }
    window.location.href = url;
  }

  var addRowBtn = document.getElementById(options.addRowBtnId);
  if (addRowBtn) {
    addRowBtn.addEventListener('click', function () { openEdit(false); });
  }

  var addGroupBtn = document.getElementById(options.addGroupBtnId);
  if (addGroupBtn) {
    addGroupBtn.addEventListener('click', function () { openEdit(true); });
  }

  initTrainingCatalog({
    addRowBtnId: null,
    addGroupBtnId: null,
    bulkEditBtnId: options.bulkEditBtnId,
    treeBodyId: options.treeBodyId,
    parentFromCheckbox: true
  });

  var bulkEditBtn = document.getElementById(options.bulkEditBtnId);
  if (bulkEditBtn) {
    bulkEditBtn.addEventListener('click', function () {
      var treeBody = document.getElementById(options.treeBodyId);
      var checked = treeBody?.querySelector('.training-row-select:checked');
      if (!checked) {
        window.alert('Выделите строку для редактирования.');
        return;
      }
      var row = checked.closest('.training-tree-row');
      var id = row?.getAttribute('data-id') || row?.querySelector('input[name="Id"]')?.value;
      if (!id) {
        window.alert('Выделите сохранённую строку.');
        return;
      }
      var isGroup = row?.getAttribute('data-is-group') === 'true';
      if (isGroup) {
        // Группа / папка — открываем форму новой комиссии внутри неё (со всеми полями).
        window.location.href = editBase + '?parentId=' + encodeURIComponent(id);
        return;
      }
      window.location.href = editBase + '?id=' + encodeURIComponent(id);
    });
  }

  var printBtn = options.printBtnId ? document.getElementById(options.printBtnId) : null;
  function updatePrintHref() {
    if (!printBtn) {
      return;
    }
    var treeBody = document.getElementById(options.treeBodyId);
    var checked = treeBody?.querySelector('.training-row-select:checked');
    var row = checked?.closest('.training-tree-row');
    var isGroup = row?.getAttribute('data-is-group') === 'true';
    var id = row?.getAttribute('data-id') || row?.querySelector('input[name="Id"]')?.value;
    if (id && !isGroup) {
      printBtn.href = previewBase + '?id=' + encodeURIComponent(id);
      printBtn.classList.remove('btn-outline');
      printBtn.classList.add('btn-primary');
    } else {
      printBtn.href = selectBase;
    }
  }

  if (printBtn) {
    updatePrintHref();
    var treeBody = document.getElementById(options.treeBodyId);
    if (treeBody) {
      treeBody.addEventListener('change', function (e) {
        if (e.target && e.target.classList && e.target.classList.contains('training-row-select')) {
          updatePrintHref();
        }
      });
    }
  }

  var filterForm = document.getElementById(options.filterFormId);
  if (filterForm) {
    var searchInput = filterForm.querySelector('input[name="q"]');
    var searchTimer = null;
    if (searchInput) {
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
    }
  }
};
