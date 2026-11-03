(function () {
  document.addEventListener('DOMContentLoaded', function () {
    initLineRows();
  });

  function initLineRows() {
    var addBtn = document.getElementById('ppe-acceptance-add-line-btn');
    var tbody = document.getElementById('ppe-acceptance-lines-tbody');
    if (!addBtn || !tbody) {
      return;
    }

    function reindex() {
      var rows = tbody.querySelectorAll('tr.ppe-line-row');
      rows.forEach(function (row, index) {
        row.querySelectorAll('[name]').forEach(function (input) {
          var name = input.getAttribute('name');
          if (!name) return;
          input.setAttribute('name', name.replace(/\[\d+\]/, '[' + index + ']'));
        });
      });
    }

    addBtn.addEventListener('click', function () {
      var rows = tbody.querySelectorAll('tr.ppe-line-row');
      var source = rows[rows.length - 1];
      var row = source.cloneNode(true);
      row.querySelectorAll('input').forEach(function (input) {
        if (input.name.indexOf('LineQuantity') >= 0) {
          input.value = '1';
        } else if (input.name.indexOf('LineUnit') >= 0) {
          input.value = 'шт';
        } else if (input.name.indexOf('LinePrice') >= 0) {
          input.value = '0';
        } else {
          input.value = '';
        }
      });
      tbody.appendChild(row);
      reindex();
    });

    tbody.addEventListener('click', function (event) {
      var button = event.target.closest('[data-action="remove-line"]');
      if (!button) return;
      var rows = tbody.querySelectorAll('tr.ppe-line-row');
      if (rows.length <= 1) return;
      button.closest('tr').remove();
      reindex();
    });
  }
})();

