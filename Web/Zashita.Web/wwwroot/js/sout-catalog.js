window.initSoutCatalog = function (options) {
  var editBase = options.editBase || ('/Sout/Catalog/Edit?kind=' + encodeURIComponent(options.kind || 'equipment'));

  function withQuery(url, key, value) {
    if (!value) {
      return url;
    }
    var sep = url.indexOf('?') >= 0 ? '&' : '?';
    return url + sep + encodeURIComponent(key) + '=' + encodeURIComponent(value);
  }

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
      var folderId = row.getAttribute('data-id') || row.querySelector('input[name="Id"]')?.value || '';
      return { parentId: folderId };
    }

    var parentId = row.getAttribute('data-parent-id') || row.querySelector('input[name="ParentId"]')?.value || '';
    return { parentId: parentId };
  }

  function openEdit(isGroup) {
    var context = getParentContext();
    var url = isGroup ? withQuery(editBase, 'isGroup', 'true') : editBase;
    url = withQuery(url, 'parentId', context.parentId);
    window.location.href = url;
  }

  var addRowBtn = document.getElementById(options.addRowBtnId);
  if (addRowBtn) {
    addRowBtn.addEventListener('click', function () {
      openEdit(false);
    });
  }

  var addGroupBtn = document.getElementById(options.addGroupBtnId);
  if (addGroupBtn) {
    addGroupBtn.addEventListener('click', function () {
      openEdit(true);
    });
  }

  initTrainingCatalog({
    addRowBtnId: null,
    addGroupBtnId: null,
    rowTemplateId: options.rowTemplateId,
    groupTemplateId: options.groupTemplateId,
    bulkEditBtnId: options.bulkEditBtnId,
    treeBodyId: options.treeBodyId,
    parentFromCheckbox: true
  });

  var bulkEditBtn = document.getElementById(options.bulkEditBtnId);
  if (bulkEditBtn) {
    bulkEditBtn.addEventListener('click', function () {
      var treeBody = document.getElementById(options.treeBodyId);
      if (!treeBody) {
        return;
      }

      var checked = treeBody.querySelector('.training-row-select:checked');
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

      window.location.href = withQuery(editBase, 'id', id);
    });
  }

  var treeBody = document.getElementById(options.treeBodyId);
  if (treeBody) {
    treeBody.addEventListener('dblclick', function (event) {
      if (event.target.closest('button, a, input, select, textarea, label')) {
        return;
      }

      var row = event.target.closest('.training-tree-row');
      if (!row || row.hidden || row.classList.contains('sout-catalog-row-new')) {
        return;
      }

      var id = row.getAttribute('data-id') || row.querySelector('input[name="Id"]')?.value;
      if (!id) {
        return;
      }

      window.location.href = withQuery(editBase, 'id', id);
    });
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

window.initCatalogMultiSelects = function (root) {
  (root || document).querySelectorAll('.catalog-multiselect').forEach(function (box) {
    if (box.dataset.ready === '1') {
      return;
    }
    box.dataset.ready = '1';

    var valueEl = box.querySelector('.catalog-multiselect-value');
    var placeholder = valueEl?.getAttribute('data-placeholder') || 'Выберите';
    var search = box.querySelector('.catalog-multiselect-search');
    var clearBtn = box.querySelector('.catalog-multiselect-clear');
    var menu = box.querySelector('.catalog-multiselect-menu');

    function optionSummary(option) {
      var summary = option.getAttribute('data-summary');
      if (summary) {
        return summary.trim();
      }
      return (option.querySelector('span')?.textContent || '').trim();
    }

    function syncSummary() {
      var checked = Array.prototype.slice.call(box.querySelectorAll('.catalog-multiselect-option input:checked'));
      if (!valueEl) {
        return;
      }
      if (checked.length === 0) {
        valueEl.textContent = placeholder;
        valueEl.title = '';
        return;
      }
      if (checked.length === 1) {
        valueEl.textContent = optionSummary(checked[0].closest('label')) || placeholder;
        valueEl.title = valueEl.textContent;
        return;
      }
      var labels = checked
        .map(function (item) {
          return optionSummary(item.closest('label'));
        })
        .filter(function (text) { return text.length > 0; });
      valueEl.textContent = labels.join(',\n');
      valueEl.title = labels.join(', ');
    }

    function clearSelection() {
      box.querySelectorAll('.catalog-multiselect-option input:checked').forEach(function (input) {
        input.checked = false;
      });
      syncSummary();
    }

    box.addEventListener('change', function (event) {
      if (event.target && event.target.matches('input[type="checkbox"]')) {
        syncSummary();
      }
    });

    box.addEventListener('toggle', function () {
      if (!box.open) {
        return;
      }
      document.querySelectorAll('details.catalog-multiselect[open]').forEach(function (other) {
        if (other !== box) {
          other.open = false;
        }
      });
    });

    if (clearBtn) {
      clearBtn.addEventListener('click', function (event) {
        event.preventDefault();
        event.stopPropagation();
        clearSelection();
      });
    }

    if (menu) {
      menu.addEventListener('click', function (event) {
        event.stopPropagation();
      });
    }

    if (search) {
      search.addEventListener('click', function (event) {
        event.stopPropagation();
      });
      search.addEventListener('input', function () {
        var query = (search.value || '').trim().toLowerCase();
        box.querySelectorAll('.catalog-multiselect-option').forEach(function (option) {
          var label = option.getAttribute('data-label') || '';
          option.hidden = query.length > 0 && label.indexOf(query) === -1;
        });
      });
    }
  });

  if (!window.__catalogMultiSelectOutsideBound) {
    window.__catalogMultiSelectOutsideBound = true;
    document.addEventListener('pointerdown', function (event) {
      var target = event.target;
      if (!(target instanceof Element)) {
        return;
      }
      document.querySelectorAll('details.catalog-multiselect[open]').forEach(function (box) {
        if (!box.contains(target)) {
          box.open = false;
        }
      });
    });
  }
};

