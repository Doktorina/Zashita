(function () {
  var catalog = Array.isArray(window.medStandardCatalog) ? window.medStandardCatalog : [];
  var input = document.getElementById('med-std-combo-input');
  var list = document.getElementById('med-std-combo-list');
  var toggle = document.getElementById('med-std-combo-toggle');
  var combo = document.getElementById('med-std-combo');
  var activeIndex = -1;

  function normalizeItem(value) {
    return String(value || '')
      .trim()
      .replace(/^п\.?\s*/i, '')
      .replace(/\s+/g, '');
  }

  function selectedKeys() {
    var set = {};
    document.querySelectorAll('#med-std-selected-body tr').forEach(function (row) {
      var id = row.getAttribute('data-id');
      if (id) set[id] = true;
      var item = normalizeItem(row.getAttribute('data-item'));
      var name = String(row.getAttribute('data-name') || '').trim().toLowerCase();
      var kindSelect = row.querySelector('select[name="RuleExamKind"]');
      var kind = kindSelect ? String(kindSelect.value || '').trim().toLowerCase() : '';
      // Один и тот же пункт можно добавить в разные виды осмотра (но не дважды в один вид).
      set[(item || '—') + '|' + name + '|' + kind] = true;
      set[(item || '—') + '|' + kind] = true;
    });
    return set;
  }

  function selectionKey(f, kind) {
    var item = normalizeItem(f && f.item);
    var name = String((f && f.name) || '').trim().toLowerCase();
    var k = String(kind || defaultExamKind() || '').trim().toLowerCase();
    return (item || '—') + '|' + name + '|' + k;
  }

  function isSelected(f) {
    var selected = selectedKeys();
    var kind = defaultExamKind();
    var key = selectionKey(f, kind);
    if (selected[key]) return true;
    var item = normalizeItem(f.item);
    var kindKey = (item || '—') + '|' + String(kind || '').trim().toLowerCase();
    if (item && selected[kindKey]) return true;
    return false;
  }

  function defaultExamKind() {
    var periodic = document.querySelector('input[name="IsPeriodic"]');
    var preShift = document.querySelector('input[name="IsPreShift"]');
    var extraordinary = document.querySelector('input[name="IsExtraordinary"]');
    var psych = document.querySelector('input[name="HasPsychiatric"]');
    // Периодический — основной вид; психиатрию не ставим по умолчанию
    // (иначе те же пункты уезжают в «Психиатрическое» и задваиваются в отчёте).
    if (periodic && periodic.checked) return 'Периодический';
    if (preShift && preShift.checked) return 'Предсменный / Предрейсовый';
    if (extraordinary && extraordinary.checked) return 'Внеочередной';
    if (psych && psych.checked) return 'Психиатрическое освидетельствование';
    return 'Периодический';
  }

  function refreshEmpty() {
    var body = document.getElementById('med-std-selected-body');
    var empty = document.getElementById('med-std-empty');
    if (!body || !empty) return;
    empty.style.display = body.children.length ? 'none' : '';
  }

  function setOpen(open) {
    if (!list || !input) return;
    list.hidden = !open;
    input.setAttribute('aria-expanded', open ? 'true' : 'false');
    if (open) {
      renderList();
    } else {
      activeIndex = -1;
    }
  }

  function filteredCatalog() {
    var q = (input?.value || '').trim().toLowerCase();
    return catalog.filter(function (f) {
      if (isSelected(f)) return false;
      if (!q) return true;
      var hay = [f.item || '', f.name || '', f.group || '', f.period || ''].join(' ').toLowerCase();
      return hay.indexOf(q) >= 0;
    }).slice(0, 200);
  }

  function renderList() {
    if (!list) return;
    var items = filteredCatalog();
    list.innerHTML = '';
    if (!items.length) {
      var empty = document.createElement('li');
      empty.className = 'med-std-combo-empty';
      empty.textContent = catalog.length ? 'Ничего не найдено или всё уже добавлено' : 'Перечень факторов пуст — заполните «Перечень факторов и видов работ»';
      list.appendChild(empty);
      activeIndex = -1;
      return;
    }

    items.forEach(function (f, idx) {
      var li = document.createElement('li');
      li.className = 'med-std-combo-item' + (idx === activeIndex ? ' is-active' : '');
      li.setAttribute('role', 'option');
      li.dataset.id = f.id || '';
      li.dataset.item = f.item || '';
      li.dataset.name = f.name || '';
      li.dataset.period = f.period || '';
      li.innerHTML =
        '<div class="med-std-combo-item-main"><b>' + escapeHtml(f.item || '—') + '</b> — ' + escapeHtml(f.name || '') + '</div>' +
        '<div class="med-std-combo-item-meta">' + escapeHtml(f.group || '—') + ' · ' + escapeHtml(f.period || '') + '</div>';
      li.addEventListener('mousedown', function (e) {
        e.preventDefault();
        pick(f);
      });
      list.appendChild(li);
    });
  }

  function escapeHtml(text) {
    return String(text)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  function pick(f) {
    addFactor(f);
    if (input) {
      input.value = '';
      input.focus();
    }
    setOpen(true);
  }

  function addFactor(f) {
    if (!f || isSelected(f)) return;

    var tpl = document.getElementById('med-std-row-template');
    var body = document.getElementById('med-std-selected-body');
    if (!tpl || !body) return;

    var item = f.item || '—';
    var name = f.name || '';
    var period = f.period || '';
    var key = selectionKey(f);
    var itemValue = item === '—' ? '' : normalizeItem(item);

    var row = tpl.content.firstElementChild.cloneNode(true);
    row.setAttribute('data-id', f.id || key);
    row.setAttribute('data-item', itemValue || '—');
    row.setAttribute('data-name', name);
    row.querySelector('input[name="RuleItemNumber"]').value = itemValue;
    row.querySelector('input[name="RuleFactorName"]').value = name;
    row.querySelector('.med-std-item-label').textContent = item;
    row.querySelector('.med-std-name-label').textContent = name;

    var periodSelect = row.querySelector('select[name="RulePeriodicity"]');
    if (periodSelect && period) {
      for (var i = 0; i < periodSelect.options.length; i++) {
        if (periodSelect.options[i].value === period) {
          periodSelect.selectedIndex = i;
          break;
        }
      }
    }

    var kindSelect = row.querySelector('select[name="RuleExamKind"]');
    if (kindSelect) {
      var kind = defaultExamKind();
      for (var j = 0; j < kindSelect.options.length; j++) {
        if (kindSelect.options[j].value === kind) {
          kindSelect.selectedIndex = j;
          break;
        }
      }
    }

    body.appendChild(row);
    refreshEmpty();
  }

  function moveActive(delta) {
    var items = list ? list.querySelectorAll('.med-std-combo-item') : [];
    if (!items.length) return;
    activeIndex = (activeIndex + delta + items.length) % items.length;
    items.forEach(function (el, i) {
      el.classList.toggle('is-active', i === activeIndex);
      if (i === activeIndex) el.scrollIntoView({ block: 'nearest' });
    });
  }

  if (input) {
    input.addEventListener('focus', function () { setOpen(true); });
    input.addEventListener('input', function () {
      activeIndex = 0;
      setOpen(true);
    });
    input.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        if (list && list.hidden) setOpen(true);
        else moveActive(1);
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        moveActive(-1);
      } else if (e.key === 'Enter') {
        var active = list && !list.hidden ? list.querySelector('.med-std-combo-item.is-active') : null;
        if (active) {
          e.preventDefault();
          pick({
            id: active.dataset.id,
            item: active.dataset.item,
            name: active.dataset.name,
            period: active.dataset.period
          });
        }
      } else if (e.key === 'Escape') {
        setOpen(false);
      }
    });
  }

  if (toggle) {
    toggle.addEventListener('click', function () {
      setOpen(list ? list.hidden : true);
      if (input) input.focus();
    });
  }

  document.addEventListener('click', function (e) {
    var removeBtn = e.target.closest('.med-std-remove');
    if (removeBtn) {
      var row = removeBtn.closest('tr');
      if (row) row.remove();
      refreshEmpty();
      return;
    }

    if (combo && !combo.contains(e.target)) {
      setOpen(false);
    }
  });

  refreshEmpty();

  // --- Профессии (чипы как в стандартах обучения) ---
  var profChips = document.getElementById('med-std-prof-chips');
  var profAddBtn = document.getElementById('med-std-add-prof');
  var profPickerWrap = document.getElementById('med-std-prof-picker-wrap');
  var profPicker = document.getElementById('med-std-prof-picker');
  var deptInput = document.getElementById('med-std-department');
  var deptMap = window.medStandardProfessionDepartments || {};
  var professionOptions = Array.isArray(window.medStandardProfessionOptions)
    ? window.medStandardProfessionOptions
    : [];

  function canonicalProfession(value) {
    var v = String(value || '').trim();
    if (!v) return v;
    var lower = v.toLowerCase();
    for (var i = 0; i < professionOptions.length; i++) {
      var opt = String(professionOptions[i] || '').trim();
      if (opt.toLowerCase() === lower) {
        return opt;
      }
    }
    return v;
  }

  function fillDepartmentFromProfession(profession) {
    if (!deptInput || !profession) return;
    var key = String(profession).trim();
    var dept = deptMap[key];
    if (!dept) {
      var lower = key.toLowerCase();
      for (var k in deptMap) {
        if (Object.prototype.hasOwnProperty.call(deptMap, k) && String(k).toLowerCase() === lower) {
          dept = deptMap[k];
          break;
        }
      }
    }
    if (dept && (!deptInput.value || deptInput.value.trim() === '')) {
      deptInput.value = dept;
    } else if (dept) {
      deptInput.value = dept;
    }
  }

  function hasProfessionChip(value) {
    if (!profChips) return false;
    var lower = String(value || '').trim().toLowerCase();
    var chips = profChips.querySelectorAll('.qual-chip');
    for (var i = 0; i < chips.length; i++) {
      var existing = String(chips[i].getAttribute('data-value') || '').trim().toLowerCase();
      if (existing === lower) return true;
    }
    return false;
  }

  function addProfessionChip(value) {
    value = canonicalProfession(String(value || '').trim());
    if (!value || !profChips) return;
    if (hasProfessionChip(value)) return;

    var chip = document.createElement('span');
    chip.className = 'qual-chip';
    chip.setAttribute('data-value', value);
    chip.innerHTML =
      '<span></span>' +
      '<button type="button" class="qual-chip-remove" aria-label="Удалить">×</button>' +
      '<input type="hidden" name="SelectedProfessionsForm" value="" />';
    chip.querySelector('span').textContent = value;
    chip.querySelector('input').value = value;
    profChips.appendChild(chip);

    if (profPicker) {
      Array.prototype.forEach.call(profPicker.options, function (opt) {
        if (String(opt.value || '').trim().toLowerCase() === value.toLowerCase()) {
          opt.remove();
        }
      });
      profPicker.value = '';
    }
    if (profPickerWrap) profPickerWrap.hidden = true;
    fillDepartmentFromProfession(value);
    if (profBox) {
      profBox.classList.remove('is-collapsed');
      profBox.classList.add('is-expanded');
    }
    syncProfessionChipsCollapse();
  }

  if (profAddBtn && profPicker) {
    profAddBtn.addEventListener('click', function () {
      if (profPickerWrap) profPickerWrap.hidden = !profPickerWrap.hidden;
      if (profPickerWrap && !profPickerWrap.hidden) profPicker.focus();
    });
    profPicker.addEventListener('change', function () {
      if (profPicker.value) addProfessionChip(profPicker.value);
    });
  }

  var profManual = document.getElementById('med-std-prof-manual');
  if (profManual) {
    profManual.addEventListener('keydown', function (e) {
      if (e.key !== 'Enter') return;
      e.preventDefault();
      addProfessionChip(profManual.value);
      profManual.value = '';
    });
  }

  var stdForm = document.getElementById('med-standard-form') ||
    (profChips && profChips.closest('form'));
  if (stdForm && profManual) {
    stdForm.addEventListener('submit', function () {
      if (String(profManual.value || '').trim()) {
        addProfessionChip(profManual.value);
        profManual.value = '';
      }
    });
  }

  var chipsToggle = document.getElementById('med-std-chips-toggle');
  var profBox = document.getElementById('med-std-prof-box');
  var chipsPreview = 2;

  function syncProfessionChipsCollapse() {
    if (!profChips || !chipsToggle || !profBox) return;
    var count = profChips.querySelectorAll('.qual-chip').length;
    if (count <= chipsPreview) {
      profBox.classList.remove('is-collapsed');
      profBox.classList.remove('is-expanded');
      chipsToggle.hidden = true;
      return;
    }
    if (!profBox.classList.contains('is-expanded')) {
      profBox.classList.add('is-collapsed');
    }
    chipsToggle.hidden = false;
    var hidden = Math.max(0, count - chipsPreview);
    if (profBox.classList.contains('is-collapsed')) {
      chipsToggle.textContent = 'ещё ' + hidden;
    } else {
      chipsToggle.textContent = 'свернуть';
    }
  }

  if (profChips) {
    profChips.addEventListener('click', function (event) {
      var button = event.target.closest('.qual-chip-remove');
      if (!button) return;
      var chip = button.closest('.qual-chip');
      if (!chip) return;
      var value = chip.getAttribute('data-value') || '';
      chip.remove();
      if (profPicker && value) {
        var option = document.createElement('option');
        option.value = value;
        option.textContent = value;
        profPicker.appendChild(option);
      }
      syncProfessionChipsCollapse();
    });
  }

  if (chipsToggle && profBox) {
    chipsToggle.addEventListener('click', function () {
      if (profBox.classList.contains('is-collapsed')) {
        profBox.classList.remove('is-collapsed');
        profBox.classList.add('is-expanded');
        chipsToggle.textContent = 'свернуть';
      } else {
        profBox.classList.add('is-collapsed');
        profBox.classList.remove('is-expanded');
        syncProfessionChipsCollapse();
      }
    });
  }

  syncProfessionChipsCollapse();

  document.querySelectorAll('.med-std-prof-toggle').forEach(function (btn) {
    btn.addEventListener('click', function (event) {
      event.preventDefault();
      event.stopPropagation();
      var wrap = btn.closest('.med-std-prof-collapse');
      if (!wrap) return;
      var shortEl = wrap.querySelector('.med-std-prof-short');
      var fullEl = wrap.querySelector('.med-std-prof-full');
      var expanded = wrap.getAttribute('data-expanded') === '1';
      if (expanded) {
        wrap.setAttribute('data-expanded', '0');
        if (shortEl) shortEl.hidden = false;
        if (fullEl) fullEl.hidden = true;
        btn.textContent = btn.getAttribute('data-more') || 'ещё';
      } else {
        wrap.setAttribute('data-expanded', '1');
        if (shortEl) shortEl.hidden = true;
        if (fullEl) fullEl.hidden = false;
        btn.textContent = btn.getAttribute('data-less') || 'свернуть';
      }
    });
  });
})();
