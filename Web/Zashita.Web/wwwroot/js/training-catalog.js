window.initTrainingCatalog = function (options) {
  var addRowBtn = document.getElementById(options.addRowBtnId);
  var addGroupBtn = options.addGroupBtnId ? document.getElementById(options.addGroupBtnId) : null;
  var rowTemplate = document.getElementById(options.rowTemplateId);
  var groupTemplate = options.groupTemplateId ? document.getElementById(options.groupTemplateId) : null;
  var bulkEditBtn = options.bulkEditBtnId ? document.getElementById(options.bulkEditBtnId) : null;
  var moveToGroupBtn = options.moveToGroupBtnId ? document.getElementById(options.moveToGroupBtnId) : null;
  var printMenu = options.printMenuId ? document.getElementById(options.printMenuId) : null;
  var printLinks = options.printLinkSelector ? document.querySelectorAll(options.printLinkSelector) : [];
  var selectedHint = options.selectedHintId ? document.getElementById(options.selectedHintId) : null;
  var selectedInput = options.selectedInputId ? document.getElementById(options.selectedInputId) : null;
  var printSelectForm = options.printSelectFormId ? document.getElementById(options.printSelectFormId) : null;
  var treeBody = options.treeBodyId ? document.getElementById(options.treeBodyId) : null;
  var selectClass = options.selectClass || 'training-row-select';
  var useParentFromCheckbox = options.parentFromCheckbox !== false;

  function createRowId() {
    if (window.crypto && typeof window.crypto.randomUUID === 'function') {
      return window.crypto.randomUUID().replace(/-/g, '');
    }

    return 'row-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2, 10);
  }

  function getRowId(row) {
    if (!row) {
      return '';
    }

    var idInput = row.querySelector('input[name="Id"]');
    return (row.getAttribute('data-id') || (idInput ? idInput.value : '') || '').trim();
  }

  function getParentId(row) {
    if (!row) {
      return '';
    }

    var parentInput = row.querySelector('input[name="ParentId"]');
    return (row.getAttribute('data-parent-id') || (parentInput ? parentInput.value : '') || '').trim();
  }

  function ensureRowId(row) {
    if (!row) {
      return '';
    }

    var idInput = row.querySelector('input[name="Id"]');
    var id = getRowId(row) || createRowId();
    row.setAttribute('data-id', id);
    if (idInput) {
      idInput.value = id;
    }
    return id;
  }

  function syncRowTreeAttributes(row) {
    if (!row) {
      return;
    }

    var id = getRowId(row);
    var parentId = getParentId(row);
    if (id) {
      row.setAttribute('data-id', id);
    }
    row.setAttribute('data-parent-id', parentId);
  }

  function getTreeRows() {
    if (!treeBody) {
      return [];
    }

    return Array.prototype.filter.call(
      treeBody.querySelectorAll('.training-tree-row'),
      function (row) { return !row.id || !row.id.endsWith('-template'); }
    );
  }

  function getChildRows(folderRow) {
    var folderId = getRowId(folderRow);
    if (!folderId) {
      return [];
    }

    return getTreeRows().filter(function (row) {
      return getParentId(row) === folderId;
    });
  }

  function getAllDescendantRows(row) {
    var result = [];
    getChildRows(row).forEach(function (child) {
      result.push(child);
      result = result.concat(getAllDescendantRows(child));
    });
    return result;
  }

  function collapseDescendants(row) {
    getChildRows(row).forEach(function (child) {
      child.classList.add('training-tree-hidden');
      child.classList.remove('training-tree-expanded');
      var toggle = child.querySelector('.training-tree-toggle');
      if (toggle) {
        toggle.setAttribute('aria-expanded', 'false');
      }
      child.classList.remove('training-tree-open');
      collapseDescendants(child);
    });
  }

  function setFolderExpanded(folderRow, expanded) {
    if (!folderRow) {
      return;
    }

    var toggle = folderRow.querySelector('.training-tree-toggle');
    folderRow.classList.toggle('training-tree-open', expanded);
    if (toggle) {
      toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false');
    }

    getChildRows(folderRow).forEach(function (child) {
      child.classList.toggle('training-tree-hidden', !expanded);
      if (!expanded) {
        collapseDescendants(child);
      }
    });
  }

  function toggleFolder(folderRow) {
    var expanded = folderRow.classList.contains('training-tree-open');
    setFolderExpanded(folderRow, !expanded);
  }

  function initTrainingTree() {
    if (!treeBody) {
      return;
    }

    // По умолчанию всё дерево раскрыто
    getTreeRows().forEach(function (row) {
      syncRowTreeAttributes(row);
      row.classList.remove('training-tree-hidden');
      if (row.classList.contains('training-tree-folder')) {
        row.classList.add('training-tree-open');
        var toggle = row.querySelector('.training-tree-toggle');
        if (toggle) {
          toggle.setAttribute('aria-expanded', 'true');
          var caret = toggle.querySelector('.training-tree-caret');
          if (caret) {
            caret.textContent = '▼';
          }
        }
      }
    });
  }

  function getSelectedParentContextFromCheckbox() {
    if (!treeBody) {
      return null;
    }

    var checked = treeBody.querySelector('.training-row-select:checked');
    if (!checked) {
      return null;
    }

    var row = checked.closest('.training-tree-row');
    if (!row) {
      return null;
    }

    if (row.classList.contains('training-tree-folder')) {
      var folderDepth = parseInt(row.getAttribute('data-depth') || '0', 10);
      return {
        parentRow: row,
        parentId: ensureRowId(row),
        depth: folderDepth + 1
      };
    }

    var parentId = getParentId(row);
    var itemDepth = parseInt(row.getAttribute('data-depth') || '0', 10);
    var parentRow = parentId
      ? treeBody.querySelector('.training-tree-row[data-id="' + parentId + '"]')
      : null;

    return {
      parentRow: parentRow,
      parentId: parentId,
      depth: parentId ? itemDepth : 0
    };
  }

  function getDefaultParentContext() {
    var defaultParentId = (options.defaultParentId || '').trim();
    if (!defaultParentId || !treeBody) {
      return null;
    }

    var parentRow = treeBody.querySelector('.training-tree-row[data-id="' + defaultParentId + '"]');
    if (!parentRow || parentRow.getAttribute('data-is-group') !== 'true') {
      return null;
    }

    return {
      parentRow: parentRow,
      parentId: defaultParentId,
      depth: parseInt(parentRow.getAttribute('data-depth') || '0', 10) + 1
    };
  }

  function getSelectedParentContext() {
    if (useParentFromCheckbox) {
      var fromCheckbox = getSelectedParentContextFromCheckbox();
      if (fromCheckbox) {
        return fromCheckbox;
      }
    }

    var fromDefault = getDefaultParentContext();
    if (fromDefault) {
      return fromDefault;
    }

    if (!treeBody) {
      return null;
    }

    var selectedFolder = treeBody.querySelector('.training-tree-folder.training-row-active');
    if (selectedFolder) {
      return {
        parentRow: selectedFolder,
        parentId: ensureRowId(selectedFolder),
        depth: parseInt(selectedFolder.getAttribute('data-depth') || '0', 10) + 1
      };
    }

    return null;
  }

  function insertRowInTree(row, template, context) {
    if (!template || !template.parentElement) {
      return;
    }

    if (context && context.parentRow) {
      // Новая строка — первой среди детей выбранной группы
      context.parentRow.insertAdjacentElement('afterend', row);
      return;
    }

    // В корне — в начало списка (перед первой видимой строкой)
    var firstVisible = null;
    var rows = template.parentElement.querySelectorAll('tr.training-tree-row');
    for (var i = 0; i < rows.length; i++) {
      if (rows[i] === template || rows[i].hidden) {
        continue;
      }
      firstVisible = rows[i];
      break;
    }
    if (firstVisible) {
      template.parentElement.insertBefore(row, firstVisible);
    } else {
      template.parentElement.insertBefore(row, template);
    }
  }

  function appendRow(template, isGroup) {
    if (!template || !template.parentElement || !treeBody) {
      return;
    }

    var row = template.cloneNode(true);
    row.id = '';
    row.hidden = false;
    row.querySelectorAll('[disabled]').forEach(function (el) {
      el.removeAttribute('disabled');
    });
    ensureRowId(row);

    var context = getSelectedParentContext();

    if (context && context.parentId) {
      row.setAttribute('data-parent-id', context.parentId);
      row.setAttribute('data-depth', String(context.depth));
      var parentInput = row.querySelector('input[name="ParentId"]');
      if (parentInput) {
        parentInput.value = context.parentId;
      }
      var treeName = row.querySelector('.training-tree-name');
      if (treeName) {
        treeName.style.setProperty('--tree-depth', String(context.depth));
      }
      if (context.parentRow) {
        setFolderExpanded(context.parentRow, true);
      }
      row.classList.remove('training-tree-hidden');
    } else {
      row.setAttribute('data-parent-id', '');
      row.setAttribute('data-depth', '0');
      var parentInputRoot = row.querySelector('input[name="ParentId"]');
      if (parentInputRoot) {
        parentInputRoot.value = '';
      }
      var treeNameRoot = row.querySelector('.training-tree-name');
      if (treeNameRoot) {
        treeNameRoot.style.setProperty('--tree-depth', '0');
      }
    }

    insertRowInTree(row, template, context);
    syncRowTreeAttributes(row);
    if (context && context.parentRow) {
      var department = context.parentRow.getAttribute('data-department') || '';
      var departmentInput = row.querySelector('.sout-department-input');
      if (departmentInput) {
        departmentInput.value = department;
      }
      row.setAttribute('data-department', department);
    }
    if (typeof options.onRowAppended === 'function') {
      options.onRowAppended(row);
    }
    var nameInput = row.querySelector('input[name="Name"]');
    if (nameInput) {
      nameInput.focus();
    }
  }

  function syncFormTreeFields() {
    if (!treeBody) {
      return;
    }

    getTreeRows().forEach(function (row) {
      syncRowTreeAttributes(row);
      var parentInput = row.querySelector('input[name="ParentId"]');
      if (parentInput) {
        parentInput.value = getParentId(row);
      }
    });
  }

  if (addRowBtn) {
    addRowBtn.addEventListener('click', function () {
      appendRow(rowTemplate, false);
    });
  }

  if (addGroupBtn && groupTemplate) {
    addGroupBtn.addEventListener('click', function () {
      appendRow(groupTemplate, true);
    });
  }

  if (treeBody) {
    var catalogForm = treeBody.closest('form');
    if (catalogForm) {
      catalogForm.addEventListener('submit', syncFormTreeFields);
    }
  }

  document.addEventListener('click', function (event) {
    var toggle = event.target.closest('.training-tree-toggle');
    if (toggle) {
      var folderRow = toggle.closest('.training-tree-folder');
      if (folderRow) {
        event.preventDefault();
        toggleFolder(folderRow);
      }
      return;
    }

    var folderIcon = event.target.closest('.training-tree-icon-folder');
    if (folderIcon) {
      var folderRowFromIcon = folderIcon.closest('.training-tree-folder');
      if (folderRowFromIcon && !event.target.closest('input, button[data-action], a')) {
        toggleFolder(folderRowFromIcon);
      }
      return;
    }

    var button = event.target.closest('button[data-action]');
    if (!button) {
      return;
    }

    var row = button.closest('tr');
    if (!row) {
      return;
    }

    var nameInput = row.querySelector('input[name="Name"]');
    var deleteFlag = row.querySelector('.training-delete-flag');
    var action = button.getAttribute('data-action');

    if (action === 'edit') {
      if (nameInput) {
        nameInput.focus();
        nameInput.select();
        nameInput.scrollIntoView({ block: 'center', behavior: 'smooth' });
      }
      return;
    }

    if (action === 'delete' && deleteFlag) {
      markRowTreeForDelete(row, !isRowMarkedForDelete(row));
    }
  });

  function isRowMarkedForDelete(row) {
    var deleteFlag = row ? row.querySelector('.training-delete-flag') : null;
    if (!deleteFlag) {
      return false;
    }

    if (deleteFlag.type === 'checkbox') {
      return deleteFlag.checked;
    }

    return deleteFlag.value === 'true';
  }

  function setRowMarkedForDelete(row, marked) {
    var deleteFlag = row ? row.querySelector('.training-delete-flag') : null;
    if (!deleteFlag) {
      return;
    }

    if (deleteFlag.type === 'checkbox') {
      deleteFlag.checked = marked;
    } else {
      deleteFlag.value = marked ? 'true' : 'false';
    }

    row.classList.toggle('training-row-delete', marked);
    row.classList.toggle('grid-row-selected', marked);
  }

  function markRowTreeForDelete(row, marked) {
    setRowMarkedForDelete(row, marked);
    if (row && row.classList.contains('training-tree-folder')) {
      getChildRows(row).forEach(function (child) {
        markRowTreeForDelete(child, marked);
      });
    }
  }

  function getSelectedRows() {
    return Array.prototype.filter.call(
      document.querySelectorAll('.' + selectClass),
      function (checkbox) { return checkbox.checked; }
    ).map(function (checkbox) { return checkbox.closest('tr'); }).filter(Boolean);
  }

  function getGroupRows() {
    return getTreeRows().filter(function (row) {
      return row.getAttribute('data-is-group') === 'true';
    });
  }

  function getRowDisplayName(row) {
    var nameInput = row ? row.querySelector('input[name="Name"]') : null;
    return nameInput ? (nameInput.value || '').trim() : '';
  }

  function isAncestorOf(possibleAncestor, row) {
    if (!possibleAncestor || !row) {
      return false;
    }

    var ancestorId = getRowId(possibleAncestor);
    if (!ancestorId) {
      return false;
    }

    var current = row;
    var guard = 0;
    while (current && guard < 50) {
      if (getRowId(current) === ancestorId) {
        return true;
      }

      var parentId = getParentId(current);
      if (!parentId) {
        return false;
      }

      current = getTreeRows().find(function (r) { return getRowId(r) === parentId; }) || null;
      guard += 1;
    }

    return false;
  }

  function setRowParent(row, parentRow) {
    var parentId = parentRow ? getRowId(parentRow) : '';
    var depth = parentRow ? (parseInt(parentRow.getAttribute('data-depth') || '0', 10) + 1) : 0;
    var departmentName = '';
    if (parentRow) {
      departmentName = getRowDisplayName(parentRow);
      if (!departmentName) {
        departmentName = (parentRow.getAttribute('data-department') || '').trim();
      }
    }

    row.setAttribute('data-parent-id', parentId);
    row.setAttribute('data-depth', String(depth));
    var parentInput = row.querySelector('input[name="ParentId"]');
    if (parentInput) {
      parentInput.value = parentId;
    }

    var deptInput = row.querySelector('.sout-department-input, input[name="Department"]');
    if (deptInput && row.getAttribute('data-is-group') !== 'true') {
      deptInput.value = departmentName;
      row.setAttribute('data-department', departmentName);
    }

    var treeName = row.querySelector('.training-tree-name');
    if (treeName) {
      treeName.style.setProperty('--tree-depth', String(depth));
    }
  }

  function relocateSubtree(row, parentRow) {
    var block = [row].concat(getAllDescendantRows(row));
    var anchor;
    if (parentRow) {
      var parentKids = getAllDescendantRows(parentRow).filter(function (child) {
        return block.indexOf(child) < 0;
      });
      anchor = parentKids.length > 0 ? parentKids[parentKids.length - 1] : parentRow;
      setFolderExpanded(parentRow, true);
    } else {
      var template = rowTemplate || groupTemplate;
      anchor = null;
      if (template) {
        block.forEach(function (item, index) {
          if (index === 0) {
            template.parentElement.insertBefore(item, template);
          } else {
            block[index - 1].insertAdjacentElement('afterend', item);
          }
        });
        return;
      }
    }

    block.forEach(function (item, index) {
      if (index === 0) {
        anchor.insertAdjacentElement('afterend', item);
      } else {
        block[index - 1].insertAdjacentElement('afterend', item);
      }
    });
  }

  function refreshSubtreeDepths(row) {
    var depth = parseInt(row.getAttribute('data-depth') || '0', 10);
    getChildRows(row).forEach(function (child) {
      var childDepth = depth + 1;
      child.setAttribute('data-depth', String(childDepth));
      var treeName = child.querySelector('.training-tree-name');
      if (treeName) {
        treeName.style.setProperty('--tree-depth', String(childDepth));
      }
      refreshSubtreeDepths(child);
    });
  }

  function moveSelectedRowsToGroup() {
    var selected = getSelectedRows().filter(function (row) {
      return row && (!row.id || !row.id.endsWith('-template'));
    });
    if (selected.length === 0) {
      window.alert('Выделите одну или несколько строк для переноса.');
      return;
    }

    // Не переносим потомков вместе с уже выбранным предком — они переедут автоматически.
    selected = selected.filter(function (row) {
      return !selected.some(function (other) {
        return other !== row && isAncestorOf(other, row);
      });
    });

    var groups = getGroupRows();
    var lines = ['0 — (без группы / корень)'];
    groups.forEach(function (group, index) {
      var indent = '';
      var depth = parseInt(group.getAttribute('data-depth') || '0', 10);
      for (var i = 0; i < depth; i++) {
        indent += '  ';
      }
      lines.push((index + 1) + ' — ' + indent + (getRowDisplayName(group) || 'Группа'));
    });

    var raw = window.prompt(
      'Переместить выделенные строки в группу.\nВведите номер:\n\n' + lines.join('\n')
    );
    if (raw === null) {
      return;
    }

    var choice = parseInt(String(raw).trim(), 10);
    if (!Number.isFinite(choice) || choice < 0 || choice > groups.length) {
      window.alert('Некорректный номер группы.');
      return;
    }

    var targetGroup = choice === 0 ? null : groups[choice - 1];
    var targetId = targetGroup ? getRowId(targetGroup) : '';

    for (var s = 0; s < selected.length; s++) {
      var row = selected[s];
      if (targetGroup && (getRowId(row) === targetId || isAncestorOf(row, targetGroup))) {
        window.alert('Нельзя переместить группу «' + getRowDisplayName(row) + '» внутрь самой себя.');
        return;
      }
    }

    selected.forEach(function (row) {
      setRowParent(row, targetGroup);
      relocateSubtree(row, targetGroup);
      refreshSubtreeDepths(row);
      syncRowTreeAttributes(row);
      getAllDescendantRows(row).forEach(syncRowTreeAttributes);
    });
  }

  if (moveToGroupBtn) {
    moveToGroupBtn.addEventListener('click', moveSelectedRowsToGroup);
  }

  if (bulkEditBtn) {
    bulkEditBtn.addEventListener('click', function () {
      var rows = getSelectedRows();
      if (rows.length === 0) {
        window.alert('Выделите одну или несколько строк в таблице.');
        return;
      }

      var firstName = rows[0].querySelector('input[name="Name"]');
      if (firstName) {
        firstName.focus();
        firstName.select();
        firstName.scrollIntoView({ block: 'center', behavior: 'smooth' });
      }
    });
  }

  function setSelectedRow(row) {
    if (!row) {
      return;
    }

    if (row.classList.contains('training-tree-folder')) {
      return;
    }

    document.querySelectorAll('.training-catalog-row.training-row-active').forEach(function (activeRow) {
      activeRow.classList.remove('training-row-active');
    });

    row.classList.add('training-row-active');
    var id = getRowId(row);
    var name = row.querySelector('input[name="Name"]')?.value || '';

    if (selectedInput) {
      selectedInput.value = id;
    }

    if (selectedHint) {
      selectedHint.textContent = name ? 'Для печати: ' + name : '';
    }

    printLinks.forEach(function (link) {
      if (!id) {
        link.classList.add('training-print-disabled');
        link.setAttribute('aria-disabled', 'true');
        link.tabIndex = -1;
        return;
      }

      var href = link.getAttribute('data-href-template');
      if (href) {
        link.href = href.replace('{id}', encodeURIComponent(id));
      }

      link.classList.remove('training-print-disabled');
      link.removeAttribute('aria-disabled');
      link.tabIndex = 0;
    });

    if (printMenu) {
      printMenu.classList.toggle('training-print-ready', Boolean(id));
    }

    if (selectedInput) {
      selectedInput.value = id || '';
    }

    if (window.history && typeof window.history.replaceState === 'function') {
      var url = new URL(window.location.href);
      if (id) {
        url.searchParams.set('printId', id);
      } else {
        url.searchParams.delete('printId');
      }
      window.history.replaceState({}, '', url);
    }
  }

  document.addEventListener('click', function (event) {
    var row = event.target.closest('.training-catalog-row');
    if (!row || event.target.closest('button, a, input, select, textarea, summary, details, .training-tree-toggle')) {
      return;
    }

    if (row.classList.contains('training-tree-folder')) {
      document.querySelectorAll('.training-catalog-row.training-row-active').forEach(function (activeRow) {
        activeRow.classList.remove('training-row-active');
      });
      row.classList.add('training-row-active');
      // Сворачивание только по стрелке/иконке папки — клик по строке только выделяет.
      return;
    }

    setSelectedRow(row);
  });

  initTrainingTree();
};
