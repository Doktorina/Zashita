(function () {
  function initSectionNav(root) {
    var navItems = root.querySelectorAll('.med-catalog-edit-nav-item[data-section]');
    var panels = root.querySelectorAll('[data-section-panel]');
    navItems.forEach(function (btn) {
      btn.addEventListener('click', function () {
        var section = btn.getAttribute('data-section');
        navItems.forEach(function (item) {
          item.classList.toggle('med-catalog-edit-nav-active', item === btn);
        });
        panels.forEach(function (panel) {
          var active = panel.getAttribute('data-section-panel') === section;
          panel.classList.toggle('med-catalog-edit-section-active', active);
          panel.hidden = !active;
        });
      });
    });
  }

  function initTabNav(root) {
    var tabs = root.querySelectorAll('.sout-assignment-tab[data-tab]');
    var panels = root.querySelectorAll('[data-tab-panel]');
    tabs.forEach(function (tab) {
      tab.addEventListener('click', function () {
        var name = tab.getAttribute('data-tab');
        tabs.forEach(function (item) {
          item.classList.toggle('sout-assignment-tab-active', item === tab);
        });
        panels.forEach(function (panel) {
          var active = panel.getAttribute('data-tab-panel') === name;
          panel.classList.toggle('sout-assignment-tab-panel-active', active);
          panel.hidden = !active;
        });
      });
    });
  }

  function initLineTables(root) {
    var configs = [
      { btnId: 'med-factor-add-doctor-btn', bodyId: 'med-factor-doctors-body', field: 'doctor' },
      { btnId: 'med-factor-add-examination-btn', bodyId: 'med-factor-examinations-body', field: 'examination' },
      { btnId: 'med-factor-add-contra-btn', bodyId: 'med-factor-contra-body', field: 'contraindication' }
    ];

    configs.forEach(function (cfg) {
      var addBtn = root.querySelector('#' + cfg.btnId);
      var body = root.querySelector('#' + cfg.bodyId);
      if (!addBtn || !body) return;

      addBtn.addEventListener('click', function () {
        var row = document.createElement('tr');
        row.innerHTML = '<td><input name="' + cfg.field + '" value="" class="ppe-input" /></td>' +
          '<td><button type="button" class="btn btn-outline btn-sm med-remove-line-btn">Удалить</button></td>';
        body.appendChild(row);
      });

      body.addEventListener('click', function (event) {
        var target = event.target;
        if (!(target instanceof HTMLElement)) return;
        if (!target.classList.contains('med-remove-line-btn')) return;
        var row = target.closest('tr');
        if (row) row.remove();
      });
    });
  }

  window.initMedCatalogEdit = function (rootId) {
    var root = document.getElementById(rootId);
    if (!root) return;
    initSectionNav(root);
    initTabNav(root);
    initLineTables(root);
  };
})();
