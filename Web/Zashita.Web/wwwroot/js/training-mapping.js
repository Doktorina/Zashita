window.initTrainingMapping = function (options) {
  var body = document.getElementById(options.bodyId);
  if (!body) {
    return;
  }

  var rowTemplate = document.getElementById(options.rowTemplateId);
  var addRowBtn = options.addRowBtnId ? document.getElementById(options.addRowBtnId) : null;

  function getRows() {
    return Array.prototype.filter.call(
      body.querySelectorAll('.training-mapping-row'),
      function (row) { return !row.id || !row.id.endsWith('-template'); }
    );
  }

  function bindRow(row) {
    var deleteBtn = row.querySelector('.training-mapping-delete');
    if (deleteBtn && deleteBtn.dataset.bound !== 'true') {
      deleteBtn.dataset.bound = 'true';
      deleteBtn.addEventListener('click', function () {
        var deleteFlag = row.querySelector('.training-delete-flag');
        if (deleteFlag) {
          deleteFlag.checked = true;
        }
        row.classList.add('training-row-delete');
        row.classList.add('grid-row-selected');
        row.style.display = 'none';
      });
    }
  }

  function appendRow() {
    if (!rowTemplate) {
      return null;
    }

    var row = rowTemplate.cloneNode(true);
    row.removeAttribute('id');
    row.removeAttribute('hidden');
    row.setAttribute('data-id', 'new-' + Date.now());
    body.appendChild(row);
    bindRow(row);

    var sourceInput = row.querySelector('input[name="SourceProgram"]');
    if (sourceInput) {
      sourceInput.focus();
    }
    return row;
  }

  getRows().forEach(bindRow);

  if (addRowBtn) {
    addRowBtn.addEventListener('click', function () {
      appendRow();
    });
  }
};
