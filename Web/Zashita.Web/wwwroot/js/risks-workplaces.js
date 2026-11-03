window.initRisksWorkplaces = function () {
  var grid = document.getElementById('risks-workplaces-body');
  var printIdInput = document.getElementById('risks-workplaces-print-id');
  var filterForm = document.getElementById('risks-workplaces-filter-form');
  var selectedHint = document.getElementById('risks-workplaces-selected-hint');
  var printLinks = document.querySelectorAll('.risks-print-link[data-href-template]');
  var isAssessmentView = !!(filterForm && filterForm.querySelector('input[name="view"][value="assessment"]'));

  function applyRisksTrafficClass(row, traffic) {
    row.classList.remove('traffic-green', 'traffic-red', 'traffic-blue', 'traffic-yellow');
    if (traffic === 'green' || traffic === 'red' || traffic === 'blue') {
      row.classList.add('traffic-' + traffic);
    }
  }

  function syncRisksTrafficMark(select) {
    var row = select.closest('tr');
    if (!row) return;
    var mark = (select.value || '').trim().toLowerCase();
    var traffic = mark || (row.getAttribute('data-auto-traffic') || '');
    applyRisksTrafficClass(row, traffic);
  }

  function setSelectedRow(row, reloadPage) {
    if (!row || row.getAttribute('data-is-group') === 'true') {
      return;
    }

    if (grid) {
      grid.querySelectorAll('.risks-workplace-row.training-row-active').forEach(function (activeRow) {
        activeRow.classList.remove('training-row-active');
      });
    }
    row.classList.add('training-row-active');

    var id = row.getAttribute('data-id') || '';
    var name = row.getAttribute('data-name') || '';

    if (printIdInput) {
      printIdInput.value = id;
    }

    if (selectedHint) {
      selectedHint.textContent = name ? 'Для печати: ' + name : '';
    }

    printLinks.forEach(function (link) {
      var href = link.getAttribute('data-href-template');
      if (!id || !href) {
        link.classList.add('training-print-disabled');
        link.setAttribute('aria-disabled', 'true');
        link.tabIndex = -1;
        link.href = '#';
        return;
      }

      link.href = href.replace('{id}', encodeURIComponent(id));
      link.classList.remove('training-print-disabled');
      link.removeAttribute('aria-disabled');
      link.tabIndex = 0;
    });

    var quickWord = document.getElementById('risks-wp-quick-word');
    var assessmentLink = document.querySelector('#risks-workplaces-print-menu a[data-href-template*="mode=assessment"]');
    if (assessmentLink && id) {
      var aHref = (assessmentLink.getAttribute('data-href-template') || '').replace('{id}', encodeURIComponent(id));
      if (quickWord) {
        quickWord.disabled = false;
        quickWord.setAttribute('data-risks-export-url', aHref);
      }
    } else if (quickWord) {
      quickWord.disabled = true;
    }

    if (reloadPage && filterForm && !isAssessmentView) {
      if (typeof filterForm.requestSubmit === 'function') {
        filterForm.requestSubmit();
      } else {
        filterForm.submit();
      }
    }
  }

  if (!grid) {
    return;
  }

  if (isAssessmentView) {
    grid.querySelectorAll('.risks-traffic-mark').forEach(function (select) {
      select.addEventListener('change', function () { syncRisksTrafficMark(select); });
    });

    grid.addEventListener('change', function (e) {
      var select = e.target.closest('.risks-traffic-mark');
      if (select) syncRisksTrafficMark(select);
    });
  }

  grid.addEventListener('click', function (event) {
    if (event.target.closest('input, button, a, label, select, .training-tree-toggle')) {
      return;
    }

    var row = event.target.closest('.risks-workplace-row');
    if (!row) {
      return;
    }

    if (isAssessmentView && row.getAttribute('data-is-group') !== 'true') {
      var href = row.getAttribute('data-assessment-href');
      if (href) {
        window.location.href = href;
        return;
      }
    }

    setSelectedRow(row, true);
  });

  var initial = grid.querySelector('.risks-workplace-row.training-row-active');
  if (!initial) {
    initial = grid.querySelector('.risks-workplace-row[data-is-group="false"]');
  }
  if (initial) {
    setSelectedRow(initial, false);
  }
};
