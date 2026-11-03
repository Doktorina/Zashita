(function () {
  const chartsEl = document.getElementById('ppe-size-charts');
  if (!chartsEl) return;

  const charts = JSON.parse(chartsEl.textContent || '{}');
  const initial = window.ppePreviewInitial || {};

  const sexEl = document.getElementById('ppe-sex');
  const heightEl = document.getElementById('ppe-height');
  const clothesEl = document.getElementById('ppe-clothes');
  const shoeEl = document.getElementById('ppe-shoe');
  const headEl = document.getElementById('ppe-head');
  const gloveEl = document.getElementById('ppe-glove');
  const gasEl = document.getElementById('ppe-gas');

  function isFemale() {
    return (sexEl.value || '').toLowerCase().includes('жен');
  }

  function chart() {
    return isFemale() ? charts.female : charts.male;
  }

  function fillSelect(select, items, selected) {
    select.innerHTML = '';
    (items || []).forEach(function (value) {
      const opt = document.createElement('option');
      opt.value = value;
      opt.textContent = value;
      if (selected && selected === value) opt.selected = true;
      select.appendChild(opt);
    });
  }

  function applyRow(row, keepManual) {
    if (!row) return;
    if (!keepManual || !clothesEl.value) clothesEl.value = row.Clothes || row.clothes || '';
    if (!keepManual || !shoeEl.value) shoeEl.value = row.Shoe || row.shoe || '';
    if (!keepManual || !headEl.value) headEl.value = row.Headwear || row.headwear || '';
    if (!keepManual || !gloveEl.value) gloveEl.value = row.Glove || row.glove || '';
    const gas = row.GasMask || row.gasMask || '';
    if (gas && gas !== '-') gasEl.value = gas;
  }

  function applyHeight(keepManual) {
    const c = chart();
    const row = (c.ByHeight || c.byHeight || {})[heightEl.value];
    applyRow(row, keepManual);
  }

  function rebuildLists() {
    const c = chart();
    fillSelect(heightEl, c.Heights || c.heights, initial.height);
    fillSelect(clothesEl, c.ClothesFull || c.clothesFull, initial.clothes);
    fillSelect(shoeEl, c.Shoes || c.shoes, initial.shoe);
    fillSelect(headEl, c.Headwear || c.headwear, initial.head);
    fillSelect(gloveEl, c.Gloves || c.gloves, initial.glove);

    if (!heightEl.value && heightEl.options.length > 0) {
      heightEl.selectedIndex = Math.min(1, heightEl.options.length - 1);
    }

    applyHeight(false);
    initial.height = '';
    initial.clothes = '';
    initial.shoe = '';
    initial.head = '';
    initial.glove = '';
  }

  sexEl.addEventListener('change', rebuildLists);
  heightEl.addEventListener('change', function () { applyHeight(false); });

  if (initial.sex) sexEl.value = initial.sex.includes('жен') ? 'жен.' : 'муж.';
  rebuildLists();
})();
