(function () {
  function initTabs(root) {
    var tabs = root.querySelectorAll('.sout-catalog-edit-tabs .sout-assignment-tab[data-tab]');
    var panels = root.querySelectorAll('[data-tab-panel]');

    tabs.forEach(function (tab) {
      tab.addEventListener('click', function () {
        var name = tab.getAttribute('data-tab');
        tabs.forEach(function (item) {
          item.classList.toggle('sout-assignment-tab-active', item === tab);
        });
        panels.forEach(function (panel) {
          panel.classList.toggle('sout-assignment-tab-panel-active', panel.getAttribute('data-tab-panel') === name);
        });
      });
    });
  }

  function initSections(root) {
    var navItems = root.querySelectorAll('.sout-catalog-edit-nav-item[data-section]');
    var sections = root.querySelectorAll('[data-section-panel]');

    navItems.forEach(function (item) {
      item.addEventListener('click', function () {
        var name = item.getAttribute('data-section');
        navItems.forEach(function (nav) {
          nav.classList.toggle('sout-catalog-edit-nav-active', nav === item);
        });
        sections.forEach(function (section) {
          section.classList.toggle('sout-catalog-edit-section-active', section.getAttribute('data-section-panel') === name);
        });
      });
    });
  }

  document.addEventListener('DOMContentLoaded', function () {
    var root = document.getElementById('sout-catalog-edit');
    if (!root) {
      return;
    }

    initTabs(root);
    initSections(root);
  });
})();
