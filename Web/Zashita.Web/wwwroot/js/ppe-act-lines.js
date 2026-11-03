(function () {
  document.addEventListener('DOMContentLoaded', function () {
    initLineRows();
    initCommissionRows();
  });

  function initLineRows() {
    var addBtn = document.getElementById('ppe-add-line-btn');
    var tbody = document.getElementById('ppe-lines-tbody');
    if (!addBtn || !tbody) {
      return;
    }

    function defaultDate() {
      var dateInput = document.querySelector('input[name="Date"]');
      if (dateInput && dateInput.value) {
        return dateInput.value;
      }
      return new Date().toISOString().slice(0, 10);
    }

    function defaultConclusion() {
      var conclusion = document.querySelector('input[name="SuitabilityDecision"]');
      return conclusion ? conclusion.value : '';
    }

    function reindex() {
      var rows = tbody.querySelectorAll('tr.ppe-line-row');
      rows.forEach(function (row, index) {
        row.querySelectorAll('[name]').forEach(function (input) {
          var name = input.getAttribute('name');
          if (!name) {
            return;
          }
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
        } else if (input.name.indexOf('LineIssueDate') >= 0) {
          input.value = defaultDate();
        } else if (input.name.indexOf('LineWearPercent') >= 0) {
          input.value = '0';
        } else if (input.name.indexOf('LineConclusion') >= 0) {
          input.value = defaultConclusion();
        } else {
          input.value = '';
        }
      });
      tbody.appendChild(row);
      reindex();
    });

    tbody.addEventListener('click', function (event) {
      var button = event.target.closest('[data-action="remove-line"]');
      if (!button) {
        return;
      }
      var rows = tbody.querySelectorAll('tr.ppe-line-row');
      if (rows.length <= 1) {
        return;
      }
      button.closest('tr').remove();
      reindex();
    });
  }

  function initCommissionRows() {
    var addBtn = document.getElementById('ppe-add-commission-btn');
    var tbody = document.getElementById('ppe-commission-tbody');
    if (!addBtn || !tbody) {
      return;
    }

    function reindex() {
      var rows = tbody.querySelectorAll('tr.ppe-commission-row');
      rows.forEach(function (row, index) {
        row.querySelectorAll('[name]').forEach(function (input) {
          var name = input.getAttribute('name');
          if (!name) {
            return;
          }
          input.setAttribute('name', name.replace(/\[\d+\]/, '[' + index + ']'));
        });
      });
    }

    addBtn.addEventListener('click', function () {
      var rows = tbody.querySelectorAll('tr.ppe-commission-row');
      var source = rows[rows.length - 1];
      var row = source.cloneNode(true);
      row.querySelectorAll('input').forEach(function (input) {
        input.value = '';
      });
      tbody.appendChild(row);
      reindex();
    });

    tbody.addEventListener('click', function (event) {
      var button = event.target.closest('[data-action="remove-commission"]');
      if (!button) {
        return;
      }
      var rows = tbody.querySelectorAll('tr.ppe-commission-row');
      if (rows.length <= 1) {
        return;
      }
      button.closest('tr').remove();
      reindex();
    });
  }
})();
