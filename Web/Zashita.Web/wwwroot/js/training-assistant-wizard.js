document.addEventListener('DOMContentLoaded', function () {
  var summaryGrid = document.getElementById('assistant-summary-grid');
  var selectedIndexInput = document.getElementById('assistant-selected-index');
  var searchForm = document.getElementById('assistant-step2-search');
  var searchTimer = null;

  if (summaryGrid && selectedIndexInput) {
    summaryGrid.addEventListener('click', function (event) {
      var row = event.target.closest('.training-assistant-summary-row');
      if (!row) {
        return;
      }

      summaryGrid.querySelectorAll('.training-assistant-summary-row.training-row-active').forEach(function (activeRow) {
        activeRow.classList.remove('training-row-active');
      });
      row.classList.add('training-row-active');
      selectedIndexInput.value = row.getAttribute('data-index') || '0';
    });
  }

  if (searchForm) {
    var searchInput = searchForm.querySelector('input[name="q"]');
    if (searchInput) {
      searchInput.addEventListener('input', function () {
        clearTimeout(searchTimer);
        searchTimer = setTimeout(function () {
          if (typeof searchForm.requestSubmit === 'function') {
            searchForm.requestSubmit();
          } else {
            searchForm.submit();
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

  document.querySelectorAll('.training-assistant-group-toggle').forEach(function (toggle) {
    toggle.addEventListener('click', function () {
      var groupRow = toggle.closest('.training-assistant-group-row');
      if (!groupRow) {
        return;
      }

      var groupId = groupRow.getAttribute('data-group-id');
      var expanded = toggle.getAttribute('aria-expanded') !== 'true';
      toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false');

      document.querySelectorAll('.training-assistant-employee-row[data-group-id="' + groupId + '"]').forEach(function (row) {
        row.classList.toggle('training-tree-hidden', !expanded);
      });
    });
  });
});
