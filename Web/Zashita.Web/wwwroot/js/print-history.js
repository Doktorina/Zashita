window.zashitaRecordPrint = function (opts) {
  opts = opts || {};
  var historyTitle = opts.title || document.title || 'Документ';
  var payload = {
    category: opts.category || 'other',
    title: historyTitle,
    organization: opts.organization || '',
    detail: opts.detail || '',
    summary: opts.summary || 'Печать',
    openUrl: opts.openUrl || (location.pathname + location.search)
  };

  // Печать сразу из клика — иначе браузер блокирует диалог (setTimeout теряет user gesture).
  if (opts.thenPrint !== false) {
    var prevTitle = document.title;
    document.title = '\u00a0';
    var restored = false;
    var restore = function () {
      if (restored) {
        return;
      }
      restored = true;
      document.title = prevTitle;
      window.removeEventListener('afterprint', restore);
    };
    window.addEventListener('afterprint', restore);
    try {
      window.print();
    } catch (_) {
      /* ignore */
    }
    setTimeout(restore, 1500);
  }

  if (typeof opts.onDone === 'function') {
    try {
      opts.onDone();
    } catch (_) {
      /* ignore */
    }
  }

  try {
    fetch('/api/print-history', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
      body: JSON.stringify(payload)
    }).catch(function () { /* ignore */ });
  } catch (_) {
    /* ignore */
  }
};
