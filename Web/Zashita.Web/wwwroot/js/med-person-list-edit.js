window.initMedPersonListEdit = function () {
  var addBtn = document.getElementById('med-add-line-btn');
  var body = document.getElementById('med-person-lines-body') || document.getElementById('med-restriction-lines-body');
  var template = document.getElementById('med-person-line-template') || document.getElementById('med-restriction-line-template');
  var employees = Array.isArray(window.medPersonEmployees) ? window.medPersonEmployees : [];
  var standards = Array.isArray(window.medPersonStandards) ? window.medPersonStandards : [];
  var onceAYear = window.medPersonOnceAYear || '1 раз в год';

  function bindRemoveButtons() {
    document.querySelectorAll('.med-remove-line-btn').forEach(function (button) {
      if (button.dataset.bound === '1') {
        return;
      }
      button.dataset.bound = '1';
      button.addEventListener('click', function () {
        var row = button.closest('.med-person-line-row, .med-restriction-line-row');
        if (!row || !body) {
          return;
        }
        if (body.querySelectorAll('.med-person-line-row, .med-restriction-line-row').length <= 1) {
          row.querySelectorAll('input').forEach(function (input) {
            if (input.type !== 'hidden') {
              input.value = '';
            }
          });
          row.querySelectorAll('select').forEach(function (sel) {
            sel.selectedIndex = 0;
          });
          refreshAge(row);
          return;
        }
        row.remove();
      });
    });
  }

  function calcAge(iso) {
    if (!iso) return null;
    var parts = String(iso).split('-');
    if (parts.length < 3) return null;
    var y = parseInt(parts[0], 10);
    var m = parseInt(parts[1], 10) - 1;
    var d = parseInt(parts[2], 10);
    if (!y || isNaN(m) || isNaN(d)) return null;
    var birth = new Date(y, m, d);
    var today = new Date();
    var age = today.getFullYear() - birth.getFullYear();
    var md = today.getMonth() - birth.getMonth();
    if (md < 0 || (md === 0 && today.getDate() < birth.getDate())) age--;
    return age < 0 ? 0 : age;
  }

  function refreshAge(row) {
    if (!row) return;
    var birth = row.querySelector('.med-line-birth');
    var ageCell = row.querySelector('.med-line-age');
    var period = row.querySelector('.med-line-periodicity');
    var age = calcAge(birth && birth.value);
    if (ageCell) {
      ageCell.textContent = age == null ? '—' : String(age);
      ageCell.setAttribute('data-age', age == null ? '' : String(age));
    }
    if (period && age != null && age < 21) {
      period.value = onceAYear;
      period.dataset.lockedUnder21 = '1';
    } else if (period) {
      delete period.dataset.lockedUnder21;
    }
  }

  function findEmployee(name) {
    var key = String(name || '').trim().toLowerCase();
    if (!key) return null;
    return employees.find(function (e) {
      return String(e.name || '').trim().toLowerCase() === key;
    }) || null;
  }

  function findStandard(id) {
    return standards.find(function (s) { return s.id === id; }) || null;
  }

  function currentExamType() {
    var el = document.getElementById('med-person-exam-type');
    return el ? String(el.value || '').trim() : '';
  }

  function rulesForStandard(std) {
    if (!std || !Array.isArray(std.rules)) return [];
    var kind = currentExamType();
    var filtered = std.rules.filter(function (r) {
      if (!kind) return true;
      return !r.kind || String(r.kind).trim().toLowerCase() === kind.toLowerCase();
    });
    return filtered.length ? filtered : std.rules;
  }

  function setItemAndPeriod(row, item, period) {
    var itemSel = row.querySelector('.med-line-item');
    var periodSel = row.querySelector('.med-line-periodicity');
    if (itemSel && item) {
      var found = false;
      Array.prototype.forEach.call(itemSel.options, function (opt) {
        if (String(opt.value || '').replace(/\s+/g, '') === String(item).replace(/\s+/g, '')) {
          itemSel.value = opt.value;
          found = true;
        }
      });
      if (!found) {
        var opt = document.createElement('option');
        opt.value = item;
        opt.textContent = item + ' (из стандарта)';
        if (period) opt.setAttribute('data-periodicity', period);
        itemSel.appendChild(opt);
        itemSel.value = item;
      }
    }
    if (periodSel && period) {
      periodSel.value = period;
    }
    refreshAge(row);
  }

  function applyEmployee(row, emp) {
    if (!row || !emp) return;
    var birth = row.querySelector('.med-line-birth');
    var profession = row.querySelector('.med-line-profession');
    var workplace = row.querySelector('.med-line-workplace');
    if (birth && emp.birth) birth.value = emp.birth;
    if (profession && emp.profession) profession.value = emp.profession;
    if (workplace && emp.department) workplace.value = emp.department;
    refreshAge(row);

    // Автоподбор стандарта по профессии/отделу, если ещё не выбран.
    var stdSel = row.querySelector('.med-line-standard');
    if (stdSel && !stdSel.value && emp.profession) {
      var match = standards.find(function (s) {
        var sp = String(s.profession || '').trim().toLowerCase();
        var ep = String(emp.profession || '').trim().toLowerCase();
        if (!sp || !ep) return false;
        return ep.indexOf(sp) >= 0 || sp.indexOf(ep) >= 0;
      });
      if (match) {
        stdSel.value = match.id;
        applyStandard(row, match, true);
      }
    }
  }

  function cloneRowFrom(sourceRow) {
    if (!template || !body || !sourceRow) return null;
    var clone = template.content.cloneNode(true);
    var row = clone.querySelector('.med-person-line-row');
    if (!row) return null;

    var copy = function (sel) {
      var from = sourceRow.querySelector(sel);
      var to = row.querySelector(sel);
      if (from && to) to.value = from.value;
    };
    copy('.med-line-name');
    copy('.med-line-birth');
    copy('.med-line-profession');
    copy('.med-line-workplace');
    copy('.med-line-standard');

    sourceRow.after(row);
    bindRemoveButtons();
    refreshAge(row);
    return row;
  }

  function applyStandard(row, std, onlyFirst) {
    if (!row || !std) return;
    var rules = rulesForStandard(std).filter(function (r) { return r.item; });
    if (!rules.length) return;

    setItemAndPeriod(row, rules[0].item, rules[0].period);
    if (onlyFirst || rules.length === 1) return;

    var prev = row;
    for (var i = 1; i < rules.length; i++) {
      var extra = cloneRowFrom(prev);
      if (!extra) break;
      setItemAndPeriod(extra, rules[i].item, rules[i].period);
      prev = extra;
    }
  }

  if (addBtn && body && template) {
    addBtn.addEventListener('click', function () {
      var clone = template.content.cloneNode(true);
      body.appendChild(clone);
      bindRemoveButtons();
      var rows = body.querySelectorAll('.med-person-line-row');
      if (rows.length) refreshAge(rows[rows.length - 1]);
    });
  }

  document.body.addEventListener('change', function (e) {
    var target = e.target;
    if (!target) return;
    var row = target.closest('.med-person-line-row');
    if (!row) return;

    if (target.classList.contains('med-line-name')) {
      applyEmployee(row, findEmployee(target.value));
      return;
    }

    if (target.classList.contains('med-line-birth')) {
      refreshAge(row);
      return;
    }

    if (target.classList.contains('med-line-standard')) {
      applyStandard(row, findStandard(target.value), false);
      return;
    }

    if (target.classList.contains('med-line-item')) {
      var period = row.querySelector('.med-line-periodicity');
      var opt = target.options[target.selectedIndex];
      var p = opt && opt.getAttribute('data-periodicity');
      if (period && p && period.dataset.lockedUnder21 !== '1') {
        period.value = p;
      }
      refreshAge(row);
      return;
    }

    if (target.classList.contains('med-line-periodicity')) {
      refreshAge(row);
    }
  });

  document.body.addEventListener('blur', function (e) {
    var target = e.target;
    if (!target || !target.classList || !target.classList.contains('med-line-name')) return;
    var row = target.closest('.med-person-line-row');
    if (!row) return;
    applyEmployee(row, findEmployee(target.value));
  }, true);

  document.querySelectorAll('.med-person-line-row').forEach(refreshAge);
  bindRemoveButtons();
};

document.addEventListener('DOMContentLoaded', function () {
  if (typeof window.initMedPersonListEdit === 'function') {
    initMedPersonListEdit();
  }
});
