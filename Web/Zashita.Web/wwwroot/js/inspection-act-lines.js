(function () {
  var table = document.getElementById('inspection-act-violations');
  var addBtn = document.getElementById('inspection-act-add-row');
  if (!table || !addBtn) {
    return;
  }

  var tbody = table.querySelector('tbody');

  function renumberRows() {
    var rows = tbody.querySelectorAll('.inspection-act-violation-row');
    rows.forEach(function (row, index) {
      var noCell = row.querySelector('.inspection-act-row-no');
      if (noCell) {
        noCell.textContent = String(index + 1);
      }
    });
  }

  function createRow() {
    var tr = document.createElement('tr');
    tr.className = 'inspection-act-violation-row';
    tr.innerHTML =
      '<td class="ot-act-col-no inspection-act-row-no"></td>' +
      '<td><textarea name="violationDescription" class="ppe-input inspection-act-textarea" rows="5"></textarea></td>' +
      '<td><textarea name="violationRegulations" class="ppe-input inspection-act-textarea" rows="5"></textarea></td>' +
      '<td><input name="violationLocation" class="ppe-input" /></td>' +
      '<td><input name="violationDeadline" type="datetime-local" class="ppe-input" /></td>' +
      '<td class="ppe-line-actions"><button type="button" class="suot-action" data-action="remove-violation" title="Удалить">✕</button></td>';
    return tr;
  }

  addBtn.addEventListener('click', function () {
    tbody.appendChild(createRow());
    renumberRows();
  });

  tbody.addEventListener('click', function (event) {
    var btn = event.target.closest('[data-action="remove-violation"]');
    if (!btn) {
      return;
    }

    var rows = tbody.querySelectorAll('.inspection-act-violation-row');
    if (rows.length <= 1) {
      var row = btn.closest('tr');
      if (row) {
        row.querySelectorAll('input, textarea').forEach(function (el) {
          el.value = '';
        });
      }
      return;
    }

    var targetRow = btn.closest('tr');
    if (targetRow) {
      targetRow.remove();
      renumberRows();
    }
  });

  renumberRows();
})();
