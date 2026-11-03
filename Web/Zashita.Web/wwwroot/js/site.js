(function () {
  const storageKey = 'zashita-theme';

  function applyTheme(theme) {
    document.documentElement.dataset.theme = theme;
    document.documentElement.style.colorScheme = theme;
    if (document.body) {
      document.body.dataset.theme = theme;
    }
    document.querySelectorAll('[data-theme-set]').forEach(function (btn) {
      btn.classList.toggle('active', btn.getAttribute('data-theme-set') === theme);
    });
    try {
      localStorage.setItem(storageKey, theme);
    } catch (_) {}
  }

  function initTheme() {
    var theme = 'light';
    try {
      theme = localStorage.getItem(storageKey) || 'light';
    } catch (_) {}
    applyTheme(theme);
  }

  function escapeHtml(value) {
    return String(value)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  function readHit(hit, key) {
    return hit[key] || hit[key.charAt(0).toUpperCase() + key.slice(1)] || '';
  }

  function initGlobalSearch() {
    var input = document.getElementById('global-search-input');
    var dropdown = document.getElementById('global-search-results');
    if (!input || !dropdown) {
      return;
    }

    var timer = null;
    var activeIndex = -1;

    function positionDropdown() {
      if (dropdown.hidden) return;
      var rect = input.getBoundingClientRect();
      var width = Math.max(rect.width, 320);
      var maxW = Math.min(480, window.innerWidth - 16);
      width = Math.min(width, maxW);
      var left = Math.min(rect.left, window.innerWidth - width - 8);
      left = Math.max(8, left);
      dropdown.style.top = Math.round(rect.bottom + 6) + 'px';
      dropdown.style.left = Math.round(left) + 'px';
      dropdown.style.right = 'auto';
      dropdown.style.width = Math.round(width) + 'px';
      dropdown.style.maxWidth = maxW + 'px';
    }

    function hideDropdown() {
      dropdown.hidden = true;
      dropdown.innerHTML = '';
      activeIndex = -1;
    }

    function renderHits(hits) {
      if (!hits.length) {
        hideDropdown();
        return;
      }

      dropdown.innerHTML = hits.map(function (hit, index) {
        var title = readHit(hit, 'title');
        var hint = readHit(hit, 'hint');
        var url = readHit(hit, 'url');
        var kind = readHit(hit, 'kind');
        return '<a class="search-hit" href="' + escapeHtml(url) + '" data-index="' + index + '">' +
          '<span class="search-hit-title">' + escapeHtml(title) + '</span>' +
          '<span class="search-hit-hint">' + escapeHtml(hint) + (kind ? ' · ' + escapeHtml(kind) : '') + '</span>' +
          '</a>';
      }).join('');
      dropdown.hidden = false;
      positionDropdown();
    }

    function fetchHits() {
      var q = input.value.trim();
      if (!q.length) {
        hideDropdown();
        return;
      }

      fetch('/api/search?q=' + encodeURIComponent(q), { credentials: 'same-origin' })
        .then(function (response) {
          if (!response.ok) {
            hideDropdown();
            return null;
          }
          return response.json();
        })
        .then(function (hits) {
          if (hits) {
            renderHits(hits);
          }
        })
        .catch(function () { hideDropdown(); });
    }

    input.addEventListener('input', function () {
      clearTimeout(timer);
      timer = setTimeout(fetchHits, 150);
    });

    input.addEventListener('focus', function () {
      if (input.value.trim().length) {
        fetchHits();
      }
    });

    window.addEventListener('resize', positionDropdown);
    window.addEventListener('scroll', positionDropdown, true);

    input.addEventListener('keydown', function (event) {
      var links = dropdown.querySelectorAll('.search-hit');
      if (event.key === 'ArrowDown') {
        event.preventDefault();
        activeIndex = Math.min(activeIndex + 1, links.length - 1);
      } else if (event.key === 'ArrowUp') {
        event.preventDefault();
        activeIndex = Math.max(activeIndex - 1, 0);
      } else if (event.key === 'Enter' && activeIndex >= 0 && links[activeIndex]) {
        event.preventDefault();
        window.location.href = links[activeIndex].getAttribute('href');
        return;
      } else if (event.key === 'Escape') {
        hideDropdown();
        return;
      } else {
        return;
      }

      links.forEach(function (link, index) {
        link.classList.toggle('active', index === activeIndex);
      });
      if (links[activeIndex]) {
        links[activeIndex].scrollIntoView({ block: 'nearest' });
      }
    });

    document.addEventListener('click', function (event) {
      if (!dropdown.contains(event.target) && event.target !== input) {
        hideDropdown();
      }
    });
  }

  function closeAllSelects(except) {
    document.querySelectorAll('.ui-select.open').forEach(function (select) {
      if (select !== except) {
        select.classList.remove('open');
      }
    });
  }

  function initCustomSelects() {
    document.querySelectorAll('.ui-select').forEach(function (select) {
      var trigger = select.querySelector('.ui-select-trigger');
      var valueEl = select.querySelector('.ui-select-value');
      var targetId = select.getAttribute('data-target');
      var shouldSubmit = select.getAttribute('data-submit') === 'true';
      var target = targetId ? document.getElementById(targetId) : null;
      if (!trigger || !valueEl) {
        return;
      }

      trigger.addEventListener('click', function (event) {
        event.stopPropagation();
        var isOpen = select.classList.contains('open');
        closeAllSelects();
        select.classList.toggle('open', !isOpen);
      });

      select.querySelectorAll('.ui-select-option').forEach(function (option) {
        option.addEventListener('click', function () {
          var value = option.getAttribute('data-value') || '';
          valueEl.textContent = (option.textContent || '').trim() || value;
          if (target) {
            target.value = value;
          }
          select.querySelectorAll('.ui-select-option').forEach(function (item) {
            item.classList.toggle('active', item === option);
          });
          select.classList.remove('open');
          if (shouldSubmit) {
            var form = select.closest('form');
            if (form) {
              form.submit();
            }
          }
        });
      });
    });

    document.addEventListener('click', function () {
      closeAllSelects();
    });
  }

  function initSegmentedControls() {
    document.querySelectorAll('.ui-segmented').forEach(function (group) {
      // Навигационные переключатели (ссылки) — не трогаем: иначе только
      // меняется active-класс, а перехода по href не происходит визуально
      // (пользователь думает, что категория сменилась).
      if (group.getAttribute('data-nav') === 'true') {
        return;
      }

      var targetId = group.getAttribute('data-target');
      var shouldSubmit = group.getAttribute('data-submit') === 'true';
      var target = targetId ? document.getElementById(targetId) : null;

      group.querySelectorAll('.ui-segmented-btn').forEach(function (button) {
        button.addEventListener('click', function (event) {
          if (button.tagName === 'A' && button.getAttribute('href')) {
            return;
          }

          var value = button.getAttribute('data-value') || '';
          if (target) {
            target.value = value;
          }
          group.querySelectorAll('.ui-segmented-btn').forEach(function (item) {
            item.classList.toggle('active', item === button);
          });
          if (shouldSubmit) {
            var form = group.closest('form');
            if (form) {
              event.preventDefault();
              form.submit();
            }
          }
        });
      });
    });
  }

  function initMotion() {
    var shell = document.querySelector('.app-shell');
    if (shell) {
      shell.classList.add('app-ready');
    }

    var body = document.querySelector('.window-body');
    if (body) {
      body.classList.add('page-enter');
      // Убрать transform после анимации — иначе sticky-шапки таблиц не работают
      window.setTimeout(function () {
        body.classList.remove('page-enter');
      }, 450);
    }
  }

  var rowCheckSelector = '.training-row-select, .grid-row-check, input[name="DeleteRow"]';

  function syncGridRowMark(checkbox) {
    if (!checkbox) {
      return;
    }

    var row = checkbox.closest('tr');
    if (!row) {
      return;
    }

    var on = !!checkbox.checked;
    row.classList.toggle('grid-row-selected', on);

    // Отметка к удалению в каталогах (скрытый флаг) — тоже зелёная подсветка.
    var isDeleteMark = checkbox.classList.contains('training-delete-flag');
    if (isDeleteMark) {
      row.classList.toggle('training-row-delete', on);
    }
  }

  function initGridRowSelection() {
    document.querySelectorAll(rowCheckSelector).forEach(syncGridRowMark);

    document.addEventListener('change', function (event) {
      var checkbox = event.target.closest(rowCheckSelector);
      if (!checkbox) {
        return;
      }

      syncGridRowMark(checkbox);
    });

    document.addEventListener('click', function (event) {
      if (event.target.closest('#planning-needs-select-all, thead .grid-select-col')) {
        return;
      }

      var cell = event.target.closest('td.training-select-col, td.grid-select-col');
      if (cell && !event.target.closest('input, button, a, label')) {
        var cellCheck = cell.querySelector(rowCheckSelector);
        if (cellCheck) {
          cellCheck.checked = !cellCheck.checked;
          cellCheck.dispatchEvent(new Event('change', { bubbles: true }));
          return;
        }
      }

      var row = event.target.closest('table.grid-selectable tbody tr');
      if (!row || event.target.closest('input, select, textarea, button, a, label')) {
        return;
      }

      var rowCheck = row.querySelector('.grid-row-check, input[name="DeleteRow"]');
      if (!rowCheck) {
        return;
      }

      rowCheck.checked = !rowCheck.checked;
      rowCheck.dispatchEvent(new Event('change', { bubbles: true }));
    });
  }

  function initReportScrollChrome() {
    var windowBody = document.querySelector('.window-body');
    var docRoot = document.scrollingElement || document.documentElement;

    function getScrollTop() {
      var bodyTop = windowBody ? windowBody.scrollTop : 0;
      var docTop = docRoot ? docRoot.scrollTop : 0;
      var winTop = window.pageYOffset || 0;
      return Math.max(bodyTop, docTop, winTop);
    }

    function scrollToTopSmooth() {
      if (windowBody && windowBody.scrollHeight > windowBody.clientHeight + 1) {
        windowBody.scrollTo({ top: 0, behavior: 'smooth' });
      }

      if (docRoot && docRoot.scrollTop > 0) {
        docRoot.scrollTo({ top: 0, behavior: 'smooth' });
      }

      if (window.pageYOffset > 0) {
        window.scrollTo({ top: 0, behavior: 'smooth' });
      }
    }

    function markSaveBar(bar) {
      if (!bar) {
        return;
      }

      if (bar.querySelector('button[type="submit"], button[form]')) {
        bar.classList.add('report-save-bar', 'training-status-save-bar');
      }
    }

    function bindSaveBarOffset(shell, saveBar) {
      if (!shell || !saveBar) {
        return;
      }

      function updateOffset() {
        var h = Math.ceil(saveBar.getBoundingClientRect().height);
        if (!h || h < 1) {
          h = 52;
        }
        var value = h + 'px';
        shell.style.setProperty('--training-save-bar-height', value);
        document.documentElement.style.setProperty('--training-save-bar-height', value);
        var body = document.querySelector('.window-body');
        if (body) {
          body.style.setProperty('--training-save-bar-height', value);
        }
      }

      var offsetScheduled = false;
      function scheduleOffset() {
        if (offsetScheduled) {
          return;
        }
        offsetScheduled = true;
        window.requestAnimationFrame(function () {
          offsetScheduled = false;
          updateOffset();
        });
      }

      updateOffset();
      window.addEventListener('resize', scheduleOffset);
      if (typeof ResizeObserver !== 'undefined') {
        new ResizeObserver(scheduleOffset).observe(saveBar);
      }
      window.setTimeout(updateOffset, 0);
      window.setTimeout(updateOffset, 300);
    }

    function bindFreezeViewport(shell) {
      if (!shell || !shell.classList.contains('training-status-wrap')) {
        return;
      }

      var lastTop = null;
      function updateHeight() {
        var rect = shell.getBoundingClientRect();
        var top = Math.max(Math.round(rect.top), 40);
        // Не дёргать layout на каждый пиксель скролла
        if (lastTop !== null && Math.abs(top - lastTop) < 24) {
          return;
        }
        lastTop = top;
        var h = Math.floor(window.innerHeight - top - 8);
        var minH = shell.classList.contains('training-planning-table-shell')
          ? Math.max(560, Math.floor(window.innerHeight * 0.62))
          : 360;
        if (h < minH) {
          h = minH;
        }
        shell.style.maxHeight = h + 'px';
        if (shell.classList.contains('training-planning-table-shell')) {
          shell.style.minHeight = Math.min(h, Math.floor(window.innerHeight * 0.7)) + 'px';
        }
      }

      var scheduled = false;
      function scheduleUpdate() {
        if (scheduled) {
          return;
        }
        scheduled = true;
        window.requestAnimationFrame(function () {
          scheduled = false;
          updateHeight();
        });
      }

      updateHeight();
      window.addEventListener('resize', scheduleUpdate);
      // Скролл окна — редко, с порогом; иначе layout thrash на большой таблице
      var body = document.querySelector('.window-body');
      if (body) {
        var scrollTimer = null;
        body.addEventListener('scroll', function () {
          if (scrollTimer) {
            return;
          }
          scrollTimer = window.setTimeout(function () {
            scrollTimer = null;
            scheduleUpdate();
          }, 120);
        }, { passive: true });
      }
      window.setTimeout(updateHeight, 0);
      window.setTimeout(updateHeight, 400);
    }

    function prepareFrozenShell(shell) {
      if (!shell) {
        return;
      }

      shell.classList.add('report-frozen-shell');
      shell.querySelectorAll('table.data-grid').forEach(function (table) {
        table.classList.add('report-sticky-head-grid', 'training-sticky-head-grid');
      });

      shell.querySelectorAll(':scope > .training-report-toolbar, :scope > .ppe-toolbar, :scope > .ppe-actions').forEach(markSaveBar);
      shell.querySelectorAll('.training-status-save-bar, .training-mapping-save-bar').forEach(function (bar) {
        bar.classList.add('report-save-bar');
      });

      var saveBar = shell.querySelector('.report-save-bar, .training-status-save-bar, .training-mapping-save-bar');
      if (saveBar) {
        bindSaveBarOffset(shell, saveBar);
      } else {
        shell.style.setProperty('--training-save-bar-height', '0px');
      }
      bindFreezeViewport(shell);
    }

    function shouldFreezeTable(table) {
      if (!table || !table.tBodies.length) {
        return false;
      }

      return table.tBodies[0].rows.length >= 4;
    }

    document.querySelectorAll('.training-status-wrap, form.training-status-wrap').forEach(prepareFrozenShell);
    document.querySelectorAll('.training-mapping-panel').forEach(prepareFrozenShell);

    // Планирование: wrap уже training-status-wrap — не замораживать внешнюю form по window-scroll
    document.querySelectorAll('.training-needs-wrap').forEach(function (wrap) {
      if (wrap.closest('.training-status-wrap, .report-frozen-shell')) {
        return;
      }

      var host = wrap.parentElement;
      if (!host || host.classList.contains('report-frozen-shell')) {
        return;
      }

      var table = wrap.querySelector('table.data-grid');
      if (!shouldFreezeTable(table)) {
        return;
      }

      // Без внутреннего скролл-контейнера sticky top:52px ломает шапку — оставляем top:0
      table.classList.add('report-sticky-head-grid', 'training-sticky-head-grid');
    });

    document.querySelectorAll('.training-report-panel, .panel:has(.training-report-filters)').forEach(function (panel) {
      if (panel.querySelector('.report-frozen-shell, .training-status-wrap, form.training-status-wrap')) {
        return;
      }

      var table = panel.querySelector('table.data-grid');
      if (!table) {
        return;
      }

      var saveButton = panel.querySelector('.ppe-toolbar button[type="submit"], .training-report-toolbar button[type="submit"]');
      if (!saveButton) {
        return;
      }

      var saveBar = saveButton.closest('.ppe-toolbar, .training-report-toolbar');
      if (!saveBar) {
        return;
      }

      var rowCount = table.tBodies.length > 0 ? table.tBodies[0].rows.length : 0;
      if (rowCount < 4) {
        return;
      }

      // Только если таблица уже в training-status-wrap — иначе не вешаем offset 52px на window
      var shell = table.closest('.training-status-wrap');
      if (!shell) {
        table.classList.add('report-sticky-head-grid', 'training-sticky-head-grid');
        return;
      }

      panel.classList.add('report-frozen-shell');
      table.classList.add('report-sticky-head-grid', 'training-sticky-head-grid');
      saveBar.classList.add('report-save-bar', 'training-status-save-bar');
      bindSaveBarOffset(panel, saveBar);
    });

    var scrollTopBtn = document.getElementById('report-scroll-top')
      || document.getElementById('training-scroll-top');
    if (!scrollTopBtn) {
      return;
    }

    // Кнопка «Наверх» — внутри окна таблицы (не поверх всего экрана).
    var tableShell = document.querySelector(
      '.training-report-panel .training-status-wrap.training-status-scroll,' +
      ' .training-report-panel > .training-status-wrap,' +
      ' .training-status-wrap.training-status-scroll'
    ) || document.querySelector('.training-status-wrap');
    var scrollRoot = windowBody;
    var dockedInTable = false;

    function ensureTableFrame(shell) {
      if (!shell || !shell.parentNode) {
        return null;
      }
      if (shell.parentElement && shell.parentElement.classList.contains('training-status-frame')) {
        return shell.parentElement;
      }
      var frame = document.createElement('div');
      frame.className = 'training-status-frame';
      shell.parentNode.insertBefore(frame, shell);
      frame.appendChild(shell);
      return frame;
    }

    if (tableShell) {
      var tableFrame = ensureTableFrame(tableShell);
      if (tableFrame) {
        tableFrame.appendChild(scrollTopBtn);
        scrollTopBtn.classList.add('is-in-table-frame');
        scrollRoot = tableShell;
        dockedInTable = true;
      }
    }

    function getActiveScrollTop() {
      if (dockedInTable && scrollRoot) {
        return scrollRoot.scrollTop || 0;
      }
      return getScrollTop();
    }

    function scrollActiveToTop() {
      if (dockedInTable && scrollRoot) {
        scrollRoot.scrollTo({ top: 0, behavior: 'smooth' });
        return;
      }
      scrollToTopSmooth();
    }

    function toggleScrollTop() {
      if (scrollTopScheduled) {
        return;
      }
      scrollTopScheduled = true;
      window.requestAnimationFrame(function () {
        scrollTopScheduled = false;
        if (getActiveScrollTop() > 160) {
          scrollTopBtn.removeAttribute('hidden');
        } else {
          scrollTopBtn.setAttribute('hidden', '');
        }
      });
    }

    var scrollTopScheduled = false;
    if (scrollRoot) {
      scrollRoot.addEventListener('scroll', toggleScrollTop, { passive: true });
    }
    if (!dockedInTable) {
      if (windowBody) {
        windowBody.addEventListener('scroll', toggleScrollTop, { passive: true });
      }
      window.addEventListener('scroll', toggleScrollTop, { passive: true });
    }
    toggleScrollTop();
    scrollTopBtn.addEventListener('click', scrollActiveToTop);
  }

  function initDismissibleAlerts() {
    var prefix = 'zashita-alert-dismiss:';

    function storageKey(el) {
      var key = el.getAttribute('data-dismiss-key') || '';
      var version = el.getAttribute('data-dismiss-version') || '';
      if (!key) {
        return '';
      }
      return prefix + key + ':' + version;
    }

    document.querySelectorAll('.home-alert-dismissible[data-dismiss-key]').forEach(function (el) {
      var key = storageKey(el);
      if (!key) {
        return;
      }
      try {
        if (localStorage.getItem(key) === '1') {
          el.hidden = true;
          el.classList.add('is-dismissed');
        }
      } catch (_) {}
    });

    document.addEventListener('click', function (event) {
      var btn = event.target.closest('[data-dismiss-alert]');
      if (!btn) {
        return;
      }
      var alert = btn.closest('.home-alert-dismissible');
      if (!alert) {
        return;
      }
      var key = storageKey(alert);
      if (key) {
        try {
          localStorage.setItem(key, '1');
        } catch (_) {}
      }
      alert.hidden = true;
      alert.classList.add('is-dismissed');
    });
  }

  function boot() {
    initMotion();
    initGlobalSearch();
    initCustomSelects();
    initSegmentedControls();
    initGridRowSelection();
    initReportScrollChrome();
    initDismissibleAlerts();
    document.querySelectorAll('[data-theme-set]').forEach(function (btn) {
      btn.addEventListener('click', function () {
        applyTheme(btn.getAttribute('data-theme-set') || 'light');
      });
    });
  }

  initTheme();
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }
})();
