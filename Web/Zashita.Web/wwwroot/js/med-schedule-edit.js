window.initMedScheduleEdit = function () {
  var addBtn = document.getElementById('med-add-line-btn');
  var body = document.getElementById('med-schedule-lines-body');
  var template = document.getElementById('med-person-line-template');
  var employees = Array.isArray(window.medScheduleEmployees) ? window.medScheduleEmployees : [];

  function bindRemoveButtons() {
    if (!body) return;
    body.querySelectorAll('.med-remove-line-btn').forEach(function (button) {
      if (button.dataset.bound === '1') return;
      button.dataset.bound = '1';
      button.addEventListener('click', function () {
        var row = button.closest('.med-person-line-row');
        if (!row || !body) return;
        if (body.querySelectorAll('.med-person-line-row').length <= 1) {
          row.querySelectorAll('input').forEach(function (input) {
            if (input.type !== 'hidden') input.value = '';
          });
          row.querySelectorAll('select').forEach(function (sel) {
            sel.selectedIndex = 0;
          });
          return;
        }
        row.remove();
      });
    });
  }

  function normalizeName(value) {
    return String(value || '')
      .trim()
      .toLowerCase()
      .replace(/\s+/g, ' ');
  }

  function findEmployee(name) {
    var key = normalizeName(name);
    if (!key) return null;

    var exact = employees.find(function (e) {
      return normalizeName(e.name) === key;
    });
    if (exact) return exact;

    return employees.find(function (e) {
      var n = normalizeName(e.name);
      return n.indexOf(key) >= 0 || key.indexOf(n) >= 0;
    }) || null;
  }

  function setSelectValue(select, value) {
    if (!select || !value) return;
    var found = false;
    Array.prototype.forEach.call(select.options || [], function (opt) {
      if (String(opt.value).toLowerCase() === String(value).toLowerCase()) {
        select.value = opt.value;
        found = true;
      }
    });
    if (!found) {
      var opt = document.createElement('option');
      opt.value = value;
      opt.textContent = value;
      select.appendChild(opt);
      select.value = value;
    }
  }

  function applyEmployee(row, emp, force) {
    if (!row || !emp) return;
    var dept = row.querySelector('[name="lineDepartment"]');
    var profession = row.querySelector('[name="profession"]');
    var birth = row.querySelector('[name="birthDate"]');
    var gender = row.querySelector('[name="gender"]');
    var item = row.querySelector('[name="itemNumber"]');
    var period = row.querySelector('[name="periodicity"]');

    if (dept && emp.department && (force || !String(dept.value || '').trim())) {
      dept.value = emp.department;
    }
    if (profession && emp.profession && (force || !String(profession.value || '').trim())) {
      profession.value = emp.profession;
    }
    if (birth && emp.birth && (force || !String(birth.value || '').trim())) {
      birth.value = emp.birth;
    }
    if (gender && emp.gender && (force || !String(gender.value || '').trim())) {
      setSelectValue(gender, emp.gender);
    }
    if (item && emp.itemNumber) {
      item.value = emp.itemNumber;
    }
    if (period && emp.periodicity) {
      setSelectValue(period, emp.periodicity);
    }
  }

  function onNameChange(target, force) {
    var row = target.closest('.med-person-line-row');
    if (!row) return;
    var emp = findEmployee(target.value);
    if (emp) {
      applyEmployee(row, emp, !!force);
    }
  }

  function bindNameInput(input) {
    if (!input || input.dataset.bound === '1') return;
    input.dataset.bound = '1';
    input.addEventListener('change', function () { onNameChange(input, true); });
    input.addEventListener('input', function () { onNameChange(input, false); });
    input.addEventListener('blur', function () { onNameChange(input, true); });
  }

  if (addBtn && body && template) {
    addBtn.addEventListener('click', function () {
      var clone = template.content.cloneNode(true);
      body.appendChild(clone);
      bindRemoveButtons();
      var row = body.lastElementChild;
      if (row) {
        var nameInput = row.querySelector('[name="employeeName"]');
        bindNameInput(nameInput);
      }
    });
  }

  if (body) {
    body.querySelectorAll('[name="employeeName"]').forEach(function (input) {
      bindNameInput(input);
      if (String(input.value || '').trim()) {
        onNameChange(input, false);
      }
    });
  }

  bindRemoveButtons();
};

document.addEventListener('DOMContentLoaded', function () {
  if (typeof window.initMedScheduleEdit === 'function') {
    initMedScheduleEdit();
  }
});
