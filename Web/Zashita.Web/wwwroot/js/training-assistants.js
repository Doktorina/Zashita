document.addEventListener('DOMContentLoaded', function () {
  var list = document.getElementById('training-assistants-list');
  if (!list) {
    return;
  }

  list.querySelectorAll('.training-assistant-item').forEach(function (item) {
    var toggle = item.querySelector('.training-assistant-toggle');
    var body = item.querySelector('.training-assistant-body');
    if (!toggle || !body) {
      return;
    }

    toggle.addEventListener('click', function (event) {
      if (event.target.closest('a.training-assistant-link')) {
        return;
      }

      event.preventDefault();
      var expanded = item.classList.toggle('training-assistant-open');
      toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false');
      body.classList.toggle('training-tree-hidden', !expanded);
    });

    var link = item.querySelector('a.training-assistant-link');
    if (link) {
      link.addEventListener('click', function (event) {
        event.stopPropagation();
      });
    }
  });
});
