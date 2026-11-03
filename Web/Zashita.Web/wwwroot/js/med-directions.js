(function () {
  var catalog = Array.isArray(window.medDirectionCatalog) ? window.medDirectionCatalog : [];
  var employees = Array.isArray(window.medDirectionEmployees) ? window.medDirectionEmployees : [];
  var standards = Array.isArray(window.medDirectionStandards) ? window.medDirectionStandards : [];
  var input = document.getElementById('med-dir-combo-input');
  var list = document.getElementById('med-dir-combo-list');
  var toggle = document.getElementById('med-dir-combo-toggle');
  var combo = document.getElementById('med-dir-combo');
  var activeIndex = -1;

  function normalizeItem(value) {
    return String(value || '')
      .trim()
      .replace(/^п\.?\s*/i, '')
      .replace(/\s+/g, '');
  }

  function selectionKey(f) {
    if (f && f.id) return String(f.id);
    var item = normalizeItem(f && f.item);
    var name = String((f && f.name) || '').trim().toLowerCase();
    return (item || '—') + '|' + name;
  }

  function selectedKeys() {
    var set = {};
    document.querySelectorAll('#med-dir-selected-body tr').forEach(function (row) {
      var item = normalizeItem(row.getAttribute('data-item'));
      var name = String(row.getAttribute('data-name') || '').trim().toLowerCase();
      set[(item || '—') + '|' + name] = true;
      if (item) set[item] = true;
    });
    return set;
  }

  function refreshEmpty() {
    var body = document.getElementById('med-dir-selected-body');
    var empty = document.getElementById('med-dir-empty');
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

  function isSelected(f) {
    var selected = selectedKeys();
    var key = selectionKey(f);
    if (selected[key]) return true;
    var item = normalizeItem(f.item);
    if (item && selected[item]) return true;
    return false;
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

  function escapeHtml(text) {
    return String(text)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  function renderList() {
    if (!list) return;
    var items = filteredCatalog();
    list.innerHTML = '';
    if (!items.length) {
      var empty = document.createElement('li');
      empty.className = 'med-std-combo-empty';
      empty.textContent = catalog.length
        ? 'Ничего не найдено или всё уже добавлено'
        : 'Перечень факторов пуст — заполните «Перечень факторов и видов работ»';
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
    var tpl = document.getElementById('med-dir-row-template');
    var body = document.getElementById('med-dir-selected-body');
    if (!tpl || !body) return;

    var item = f.item || '—';
    var name = f.name || '';
    var itemValue = item === '—' ? '' : normalizeItem(item);

    var row = tpl.content.firstElementChild.cloneNode(true);
    row.setAttribute('data-item', itemValue || '—');
    row.setAttribute('data-name', name);
    row.querySelector('input[name="FactorName"]').value = name;
    row.querySelector('input[name="FactorCode988"]').value = '';
    row.querySelector('input[name="FactorCode29n"]').value = itemValue;
    row.querySelector('input[name="FactorLevel"]').value = '';
    row.querySelector('input[name="FactorClass"]').value = '';
    row.querySelector('.med-dir-item-label').textContent = item;
    row.querySelector('.med-dir-name-label').textContent = name;
    body.appendChild(row);
    refreshEmpty();
  }

  function clearFactors() {
    var body = document.getElementById('med-dir-selected-body');
    if (!body) return;
    body.innerHTML = '';
    refreshEmpty();
  }

  function currentExamType() {
    var el = document.querySelector('[name="ExamType"]');
    return el ? String(el.value || '').trim() : '';
  }

  function normalizeText(value) {
    return String(value || '')
      .trim()
      .toLowerCase()
      .replace(/ё/g, 'е')
      .replace(/\s+/g, ' ');
  }

  function professionTokens(value) {
    return String(value || '')
      .split(/[;,|]/)
      .map(function (x) { return normalizeText(x); })
      .filter(Boolean);
  }

  function matchesProfession(standardProfession, employeeProfession) {
    var emp = normalizeText(employeeProfession);
    if (!emp) return false;
    var tokens = professionTokens(standardProfession);
    if (!tokens.length) return true;
    return tokens.some(function (t) {
      return emp === t || emp.indexOf(t) >= 0 || t.indexOf(emp) >= 0;
    });
  }

  function findStandardForProfession(profession, department) {
    var dept = normalizeText(department);
    var scored = standards
      .filter(function (s) { return matchesProfession(s.profession, profession); })
      .map(function (s) {
        var score = 0;
        if (String(s.profession || '').trim()) score += 2;
        var sd = normalizeText(s.department);
        if (!sd) score += 0;
        else if (!dept || sd === dept || dept.indexOf(sd) >= 0 || sd.indexOf(dept) >= 0) score += 1;
        else score -= 5;
        if (String(s.name || '').trim()) score += 1;
        if (Array.isArray(s.rules) && s.rules.length) score += 1;
        return { s: s, score: score };
      })
      .filter(function (x) { return x.score >= 0; })
      .sort(function (a, b) { return b.score - a.score; });
    return scored.length ? scored[0].s : null;
  }

  function setStandardStatus(text, isError) {
    var hint = document.getElementById('med-dir-standard-hint');
    var status = document.getElementById('med-dir-factor-status');
    if (!status) {
      status = document.createElement('p');
      status.id = 'med-dir-factor-status';
      status.className = 'hint';
      var wrap = document.getElementById('med-dir-selected-wrap');
      if (wrap && wrap.parentNode) {
        wrap.parentNode.insertBefore(status, wrap);
      } else if (hint && hint.parentNode) {
        hint.parentNode.insertBefore(status, hint.nextSibling);
      }
    }
    if (!status) return;
    status.className = isError ? 'form-error' : 'hint';
    status.textContent = text || '';
  }

  function applyStandardFactors(profession, department, replace) {
    var prof = String(profession || '').trim();
    if (!prof) {
      setStandardStatus('Укажите профессию — факторы подтянутся из стандарта.', false);
      return false;
    }

    if (!standards.length) {
      setStandardStatus('Нет сохранённых стандартов. Создайте стандарт в разделе «Стандарты медосмотров».', true);
      return false;
    }

    var std = findStandardForProfession(prof, department);
    if (!std) {
      setStandardStatus('Для профессии «' + prof + '» стандарт не найден. Проверьте профессию в стандарте.', true);
      return false;
    }

    if (!Array.isArray(std.rules) || !std.rules.length) {
      setStandardStatus('Стандарт «' + (std.name || prof) + '» найден, но в нём нет факторов.', true);
      return false;
    }

    var kind = currentExamType();
    var rules = std.rules.filter(function (r) {
      if (!kind) return true;
      var rk = normalizeText(r.kind);
      if (!rk) return true;
      var kindNorm = normalizeText(kind);
      if (kindNorm.indexOf('предварител') >= 0) {
        return rk.indexOf('периодическ') >= 0 || rk.indexOf('предварител') >= 0;
      }
      return rk === kindNorm;
    });
    if (!rules.length) rules = std.rules;

    if (replace) clearFactors();
    rules.forEach(function (r) {
      addFactor({ item: r.item, name: r.name });
    });
    setStandardStatus(
      'Подставлено факторов: ' + rules.length + ' из стандарта «' + (std.name || 'без названия') + '». Лишние можно убрать.',
      false
    );
    return true;
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
            name: active.dataset.name
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
    var removeBtn = e.target.closest('.med-dir-remove');
    if (removeBtn) {
      var row = removeBtn.closest('tr');
      if (row) row.remove();
      refreshEmpty();
      return;
    }

    if (combo && !combo.contains(e.target)) {
      setOpen(false);
    }

    var employeeCombo = document.getElementById('med-dir-employee-combo');
    if (employeeCombo && !employeeCombo.contains(e.target)) {
      setEmployeeOpen(false);
    }
  });

  refreshEmpty();

  // --- Выпадающий список сотрудников (без datalist — совместимость с Яндекс.Браузером) ---
  var empInput = document.getElementById('med-dir-employee-input');
  var empList = document.getElementById('med-dir-employee-list');
  var empToggle = document.getElementById('med-dir-employee-toggle');
  var empCombo = document.getElementById('med-dir-employee-combo');
  var empActiveIndex = -1;

  function filteredEmployees() {
    var q = (empInput?.value || '').trim().toLowerCase();
    return employees.filter(function (e) {
      var name = String(e.name || '').trim();
      if (!name) return false;
      if (!q) return true;
      return name.toLowerCase().indexOf(q) >= 0;
    }).slice(0, 200);
  }

  function setEmployeeOpen(open) {
    if (!empList || !empInput) return;
    empList.hidden = !open;
    empInput.setAttribute('aria-expanded', open ? 'true' : 'false');
    if (open) {
      renderEmployeeList();
    } else {
      empActiveIndex = -1;
    }
  }

  function renderEmployeeList() {
    if (!empList) return;
    var items = filteredEmployees();
    empList.innerHTML = '';
    if (!items.length) {
      var empty = document.createElement('li');
      empty.className = 'med-std-combo-empty';
      empty.textContent = employees.length
        ? 'Ничего не найдено'
        : 'Штат пуст — введите данные вручную';
      empList.appendChild(empty);
      empActiveIndex = -1;
      return;
    }

    items.forEach(function (emp, idx) {
      var li = document.createElement('li');
      li.className = 'med-std-combo-item' + (idx === empActiveIndex ? ' is-active' : '');
      li.setAttribute('role', 'option');
      li.dataset.name = emp.name || '';
      var meta = [emp.department, emp.profession].filter(Boolean).join(' · ');
      li.innerHTML =
        '<div class="med-std-combo-item-main">' + escapeHtml(emp.name || '') + '</div>' +
        (meta ? '<div class="med-std-combo-item-meta">' + escapeHtml(meta) + '</div>' : '');
      li.addEventListener('mousedown', function (e) {
        e.preventDefault();
        pickEmployee(emp);
      });
      empList.appendChild(li);
    });
  }

  function pickEmployee(emp) {
    if (!emp || !empInput) return;
    empInput.value = emp.name || '';
    applyEmployee(emp);
    setEmployeeOpen(false);
  }

  function moveEmployeeActive(delta) {
    var items = empList ? empList.querySelectorAll('.med-std-combo-item') : [];
    if (!items.length) return;
    empActiveIndex = (empActiveIndex + delta + items.length) % items.length;
    items.forEach(function (el, i) {
      el.classList.toggle('is-active', i === empActiveIndex);
      if (i === empActiveIndex) el.scrollIntoView({ block: 'nearest' });
    });
  }

  if (empInput) {
    empInput.addEventListener('focus', function () { setEmployeeOpen(true); });
    empInput.addEventListener('input', function () {
      empActiveIndex = 0;
      setEmployeeOpen(true);
      var exact = findByName(empInput.value);
      if (exact) applyEmployee(exact);
    });
    empInput.addEventListener('change', function () { applyEmployee(findByName(empInput.value)); });
    empInput.addEventListener('blur', function () {
      window.setTimeout(function () {
        if (empCombo && !empCombo.contains(document.activeElement)) {
          setEmployeeOpen(false);
        }
        applyEmployee(findByName(empInput.value));
      }, 120);
    });
    empInput.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        if (empList && empList.hidden) setEmployeeOpen(true);
        else moveEmployeeActive(1);
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        moveEmployeeActive(-1);
      } else if (e.key === 'Enter') {
        var active = empList && !empList.hidden ? empList.querySelector('.med-std-combo-item.is-active') : null;
        if (active) {
          e.preventDefault();
          var picked = findByName(active.dataset.name);
          if (picked) pickEmployee(picked);
        }
      } else if (e.key === 'Escape') {
        setEmployeeOpen(false);
      }
    });
  }

  if (empToggle) {
    empToggle.addEventListener('click', function () {
      setEmployeeOpen(empList ? empList.hidden : true);
      if (empInput) empInput.focus();
    });
  }

  // --- Автозаполнение из штата + факторы из стандарта ---
  var professionInput = document.querySelector('input[name="ProfessionOrWorkType"]');
  var hireInput = document.querySelector('input[name="HireProfession"]');
  var hireField = document.getElementById('med-dir-hire-profession-field');
  var hireHint = document.getElementById('med-dir-hire-hint');
  var departmentInput = document.querySelector('input[name="Department"]');

  function isPreliminaryExamType() {
    return /предвар/i.test(currentExamType());
  }

  function syncHireProfessionField() {
    var preliminary = isPreliminaryExamType();
    if (hireInput) {
      hireInput.readOnly = !preliminary;
      if (!preliminary) {
        hireInput.value = '';
      } else if (!String(hireInput.value || '').trim() && professionInput && professionInput.value) {
        hireInput.value = professionInput.value;
      }
    }
    if (hireField) {
      hireField.style.opacity = preliminary ? '' : '0.55';
    }
    if (hireHint) {
      hireHint.style.display = preliminary ? 'none' : '';
    }
  }

  function formatSnils(value) {
    var digits = String(value || '').replace(/\D/g, '');
    if (digits.length !== 11) return String(value || '').trim();
    return digits.slice(0, 3) + '-' + digits.slice(3, 6) + '-' + digits.slice(6, 9) + ' ' + digits.slice(9, 11);
  }

  function setField(name, value) {
    var el = document.querySelector('[name="' + name + '"]');
    if (el) el.value = value || '';
  }

  function applyEmployee(emp) {
    if (!emp) return;
    setField('LastName', emp.last);
    setField('FirstName', emp.first);
    setField('MiddleName', emp.middle);
    setField('Gender', emp.gender);
    setField('BirthDate', emp.birth);
    setField('Snils', formatSnils(emp.snils));
    setField('Department', emp.department);
    // Адрес из штата — только если есть; пустой не затирает уже введённый.
    if (emp.address) {
      setField('HomeAddress', emp.address);
    }
    // Заменяем профессию, а не накапливаем.
    if (emp.profession) {
      setField('ProfessionOrWorkType', emp.profession);
      if (isPreliminaryExamType()) {
        setField('HireProfession', emp.profession);
      } else {
        setField('HireProfession', '');
      }
      applyStandardFactors(emp.profession, emp.department, true);
    }
    syncHireProfessionField();
  }

  function findByName(name) {
    var key = String(name || '').trim().toLowerCase();
    if (!key) return null;
    return employees.find(function (e) {
      return String(e.name || '').trim().toLowerCase() === key;
    }) || null;
  }

  var lastAppliedProfession = '';
  var professionTimer = null;

  function resolveProfessionValue() {
    var fromMain = professionInput ? String(professionInput.value || '').trim() : '';
    if (fromMain) return fromMain;
    return hireInput ? String(hireInput.value || '').trim() : '';
  }

  function applyProfessionFactors(force) {
    var prof = resolveProfessionValue();
    if (!prof) {
      lastAppliedProfession = '';
      setStandardStatus('Укажите профессию — факторы подтянутся из стандарта.', false);
      return;
    }

    var key = normalizeText(prof) + '|' + normalizeText(departmentInput ? departmentInput.value : '') + '|' + normalizeText(currentExamType());
    if (!force && key === lastAppliedProfession) return;
    lastAppliedProfession = key;

    if (hireInput && isPreliminaryExamType() && !String(hireInput.value || '').trim() && professionInput) {
      hireInput.value = professionInput.value;
    }

    applyStandardFactors(prof, departmentInput ? departmentInput.value : '', true);
  }

  function scheduleProfessionApply() {
    if (professionTimer) clearTimeout(professionTimer);
    professionTimer = setTimeout(function () {
      applyProfessionFactors(false);
    }, 250);
  }

  if (professionInput) {
    professionInput.addEventListener('change', function () { applyProfessionFactors(true); });
    professionInput.addEventListener('blur', function () { applyProfessionFactors(true); });
    professionInput.addEventListener('input', scheduleProfessionApply);
  }

  if (hireInput) {
    hireInput.addEventListener('change', function () { applyProfessionFactors(true); });
    hireInput.addEventListener('blur', function () { applyProfessionFactors(true); });
  }

  if (departmentInput) {
    departmentInput.addEventListener('change', function () { applyProfessionFactors(true); });
    departmentInput.addEventListener('blur', function () { applyProfessionFactors(true); });
  }

  var examTypeSelect = document.querySelector('[name="ExamType"]');
  if (examTypeSelect) {
    examTypeSelect.addEventListener('change', function () {
      syncHireProfessionField();
      applyProfessionFactors(true);
    });
  }

  syncHireProfessionField();

  (function bootstrapFactorsFromStandard() {
    var body = document.getElementById('med-dir-selected-body');
    // Сервер уже подставил факторы (планирование — только пункты из таблицы;
    // отчёт/ссылка — из стандарта). Не затираем их полным стандартом при открытии.
    if (body && body.children.length > 0) {
      lastAppliedProfession =
        normalizeText(resolveProfessionValue()) + '|' +
        normalizeText(departmentInput ? departmentInput.value : '') + '|' +
        normalizeText(currentExamType());
      setStandardStatus('Факторы уже заполнены. Можно добавить ещё или убрать лишние.', false);
      return;
    }

    if (empInput && empInput.value) {
      applyEmployee(findByName(empInput.value));
    }

    if (body && body.children.length > 0) {
      setStandardStatus('Факторы уже заполнены. Можно добавить ещё или убрать лишние.', false);
      return;
    }
    applyProfessionFactors(true);
  })();

  var snilsInput = document.querySelector('input[name="Snils"]');
  if (snilsInput) {
    snilsInput.addEventListener('blur', function () {
      snilsInput.value = formatSnils(snilsInput.value);
    });
    if (snilsInput.value) {
      snilsInput.value = formatSnils(snilsInput.value);
    }
  }
})();
