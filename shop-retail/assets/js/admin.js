/* Admin dashboard. Reads the catalogue (catalog.js), demo history (seed.js) and the orders placed in the
   storefront on this browser (common.js), and writes admin changes back to the same storage. */
(function () {
  var X = window.Shop, S = X.S, SEED = window.SEED || { orders: [], customers: [], sessions: [] };
  var $ = X.$, $$ = X.$$, esc = X.esc, vnd = X.vnd, R = X.root;
  var params = new URLSearchParams(location.search);
  var DAY = 864e5;
  var TODAY = new Date(); TODAY.setHours(0, 0, 0, 0);
  var STAFF = S.staff && S.staff.length ? S.staff : [{ name: 'Quản lý cửa hàng', role: 'Chủ cửa hàng' }, { name: 'Nhân viên bán hàng', role: 'Bán hàng' }, { name: 'Nhân viên kho', role: 'Kho' }];

  /* ---------------------------------------------------------------- staff login guard */
  var loginForm = $('[data-staff-login]');
  if (loginForm) {
    var sel = $('[data-staff-select]');
    STAFF.forEach(function (s, i) { var o = document.createElement('option'); o.value = i; o.textContent = s.name + ' (' + s.role + ')'; sel.appendChild(o); });
    loginForm.addEventListener('submit', function (e) {
      e.preventDefault();
      if (loginForm.password.value !== 'demo') { $('[data-login-error]').hidden = false; return; }
      X.set('adm:staff', STAFF[+sel.value]); location.href = 'index.html';
    });
    return;
  }
  var staff = X.get('adm:staff', null);
  if (!staff) { location.replace('login.html'); return; }

  /* ---------------------------------------------------------------- data */
  // seed.js v2 holds compact arrays (see build.py seed_data); decode them once.
  var seedCustomers = (SEED.customers || []).map(function (c) {
    return Array.isArray(c) ? { id: c[0], name: c[1], phone: c[2], email: c[3], city: (SEED.cities || [])[c[4]] || '', joined: c[5] } : c;
  });
  var custById = {};
  seedCustomers.forEach(function (c) { custById[c.id] = c; });
  var payName = {}; (S.payments || []).forEach(function (p) { payName[p.id] = p.name; });
  var shipName = {}; (S.shipping || []).forEach(function (s) { shipName[s.id] = s.name; });
  var FLOW = ['pending', 'confirmed', 'packing', 'shipping', 'delivered'];
  var NEXT = { pending: ['confirmed', 'Xác nhận đơn'], confirmed: ['packing', 'Bắt đầu đóng gói'], packing: ['shipping', 'Giao cho vận chuyển'], shipping: ['delivered', 'Xác nhận đã giao'] };
  var SEED_DAYS = SEED.days || 0;

  function seedOrder(r) {
    // [num, d, minute, customer, [[sku, qty, price]], discount, fee, status, payment, paid, shipping, channel]
    var c = seedCustomers[r[3]] || { id: '', name: 'Khách lẻ', phone: '', email: '', city: '' };
    var status = SEED.statuses[r[7]], pay = SEED.pays[r[8]], ship = SEED.ships[r[10]], channel = SEED.channels[r[11]];
    var created = new Date(TODAY.getTime() + r[1] * DAY + r[2] * 6e4);
    var steps = status === 'cancelled' ? ['pending', 'cancelled'] : status === 'returned' ? FLOW.concat(['returned']) : FLOW.slice(0, FLOW.indexOf(status) + 1);
    var items = r[4].map(function (it) {
      var sku = SEED.skus[it[0]], ref = X.bySku[sku] || { p: {}, v: {} };
      return { id: ref.p.id, sku: sku, name: ref.p.name || sku, variant: ref.v.label || '', price: it[2], qty: it[1], image: ref.p.image || '' };
    });
    var sub = items.reduce(function (a, it) { return a + it.price * it.qty; }, 0);
    return {
      code: 'DH' + r[0], created: created.toISOString(), k: r[1], hour: Math.floor(r[2] / 60), status: status, paid: !!r[9], channel: channel, source: 'seed',
      customer: { id: c.id, title: '', name: c.name, phone: c.phone, email: c.email },
      receive: 'ship', address: { province: c.city, ward: '', street: '' }, store: null,
      shipping: { id: ship, name: shipName[ship] || ship }, payment: { id: pay, name: payName[pay] || pay },
      items: items, subtotal: sub, discount: r[5], shipping_fee: r[6], total: sub - r[5] + r[6], voucher: r[12] >= 0 ? (SEED.promos || [])[r[12]] : null,
      history: steps.map(function (st, i) { return { status: st, at: new Date(created.getTime() + i * 5.5 * 36e5).toISOString(), note: i === 0 ? 'Đặt qua ' + channel : '', by: i ? 'Hệ thống' : '' }; }),
      notes: []
    };
  }
  var seedCache = null, allCache = null;
  document.addEventListener('shop:change', function () { allCache = null; });
  function allOrders() {
    if (allCache) return allCache;
    seedCache = seedCache || (SEED.orders || []).map(seedOrder);
    var ov = X.get('adm:orders', {});
    var seeded = seedCache.map(function (o) {
      var v = ov[o.code];
      if (!v) return o;
      var c = Object.assign({}, o);
      c.status = v.status || o.status; if (v.paid != null) c.paid = v.paid;
      c.history = o.history.concat(v.history || []); c.notes = v.notes || []; c.serials = v.serials || {};
      return c;
    });
    var local = X.orders().map(function (o) {
      o.source = 'web'; o.notes = o.notes || []; o.serials = o.serials || {};
      o.k = dayIndex(o.created); o.hour = new Date(o.created).getHours();
      return o;
    });
    allCache = local.concat(seeded).sort(function (a, b) { return b.created < a.created ? -1 : 1; });
    return allCache;
  }
  function changeOrder(o, fn) {
    allCache = null;
    if (o.source === 'web') { X.updateOrder(o.code, fn); return; }
    var ov = X.get('adm:orders', {}), v = ov[o.code] || { history: [], notes: [], serials: {} };
    var tmp = { status: v.status || o.status, paid: v.paid != null ? v.paid : o.paid, history: v.history, notes: v.notes, serials: v.serials || {} };
    fn(tmp); ov[o.code] = tmp; X.set('adm:orders', ov);
  }
  function setStatus(o, st, note) {
    changeOrder(o, function (x) {
      x.status = st;
      x.history = x.history || [];
      x.history.push({ status: st, at: new Date().toISOString(), note: note || '', by: staff.name });
      if (st === 'delivered' && (o.payment.id === 'cod')) x.paid = true;
    });
  }
  function live(o) { return o.status !== 'cancelled' && o.status !== 'returned'; }
  function net(o) { return o.subtotal - o.discount; }
  function dayIndex(iso) { var d = new Date(iso); d.setHours(0, 0, 0, 0); return Math.round((d - TODAY) / DAY); }
  function inRange(o, from, to) { var k = o.k != null ? o.k : dayIndex(o.created); return k >= from && k <= to; }
  // daily traffic: [day, sessions, added to cart, reached checkout]
  function trafficFor(from, to) {
    var t = { sessions: 0, carts: 0, checkouts: 0, days: 0 };
    (SEED.traffic || []).forEach(function (r) { if (r[0] >= from && r[0] <= to) { t.sessions += r[1]; t.carts += r[2]; t.checkouts += r[3]; t.days++; } });
    return t;
  }
  function sessionsFor(from, to) { return trafficFor(from, to).sessions; }

  /* ---------------------------------------------------------------- formatting */
  function compact(n) {
    if (Math.abs(n) >= 1e9) return (n / 1e9).toFixed(n >= 1e10 ? 0 : 1).replace('.', ',') + ' tỷ';
    if (Math.abs(n) >= 1e6) return (n / 1e6).toFixed(n >= 1e8 ? 0 : 1).replace('.', ',') + ' tr';
    if (Math.abs(n) >= 1e3) return Math.round(n / 1e3) + 'k';
    return String(Math.round(n));
  }
  function num(n) { return new Intl.NumberFormat('vi-VN').format(Math.round(n)); }
  function pct(n, d) { return d ? (n * 100 / d) : 0; }
  function delta(cur, prev) { if (!prev) return null; return (cur - prev) * 100 / prev; }
  function badge(st) { return '<span class="badge ' + (X.STATUS_CLASS[st] || '') + '">' + esc(X.STATUS[st] || st) + '</span>'; }
  function payBadge(o) { return o.status === 'cancelled' ? '<span class="badge">Không thu</span>' : o.paid ? '<span class="badge badge-ok">Đã thanh toán</span>' : '<span class="badge badge-warn">Chưa thanh toán</span>'; }
  function ddmm(k) { var d = new Date(TODAY.getTime() + k * DAY); return ('0' + d.getDate()).slice(-2) + '/' + ('0' + (d.getMonth() + 1)).slice(-2); }
  function csv(name, rows) {
    var text = '﻿' + rows.map(function (r) { return r.map(function (c) { c = String(c == null ? '' : c); return /[",\n]/.test(c) ? '"' + c.replace(/"/g, '""') + '"' : c; }).join(','); }).join('\n');
    var a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([text], { type: 'text/csv;charset=utf-8' })); a.download = name + '.csv'; a.click();
    X.toast(X.icon('check') + 'Đã xuất ' + (rows.length - 1) + ' dòng ra ' + name + '.csv');
  }

  /* ---------------------------------------------------------------- charts (inline SVG) */
  var C1 = '#2a78d6', CPREV = '#9aa1ab', GRID = '#e6e8ec', INK2 = '#5b6470';
  var tip = $('[data-tip]');
  function showTip(e, html) {
    tip.innerHTML = html; tip.hidden = false;
    var x = (e.clientX || 0) + 14, y = (e.clientY || 0) + 14, w = tip.offsetWidth, h = tip.offsetHeight;
    if (x + w > innerWidth - 8) x = (e.clientX || 0) - w - 14;
    if (y + h > innerHeight - 8) y = (e.clientY || 0) - h - 14;
    tip.style.left = x + 'px'; tip.style.top = y + 'px';
  }
  function hideTip() { tip.hidden = true; }
  function niceMax(v) {
    if (v <= 0) return 1;
    var p = Math.pow(10, Math.floor(Math.log10(v))), m = v / p;
    return (m <= 1 ? 1 : m <= 2 ? 2 : m <= 2.5 ? 2.5 : m <= 5 ? 5 : 10) * p;
  }
  function svgEl(w, h) { return '<svg viewBox="0 0 ' + w + ' ' + h + '" width="100%" height="' + h + '" preserveAspectRatio="none" role="img">'; }

  function lineChart(el, labels, series, fmt, axisFmt) {
    axisFmt = axisFmt || compact;
    var W = Math.max(320, el.clientWidth || 640), H = 260, L = 56, Rr = 12, T = 12, B = 28;
    var max = niceMax(Math.max.apply(null, series.reduce(function (a, s) { return a.concat(s.values); }, [0])));
    var n = labels.length, iw = W - L - Rr, ih = H - T - B;
    function X_(i) { return L + (n < 2 ? iw / 2 : i * iw / (n - 1)); }
    function Y_(v) { return T + ih - v * ih / max; }
    var h = '<svg viewBox="0 0 ' + W + ' ' + H + '" width="100%" height="' + H + '" role="img" aria-label="' + esc(series[0].name) + '">';
    for (var g = 0; g <= 4; g++) {
      var v = max * g / 4, y = Y_(v);
      h += '<line x1="' + L + '" x2="' + (W - Rr) + '" y1="' + y + '" y2="' + y + '" stroke="' + GRID + '" stroke-width="1"/>';
      h += '<text x="' + (L - 8) + '" y="' + (y + 4) + '" text-anchor="end" font-size="11" fill="' + INK2 + '">' + axisFmt(v) + '</text>';
    }
    var step = Math.ceil(n / 8);
    labels.forEach(function (lb, i) { if ((i % step === 0 && i < n - 1 - step / 2) || i === n - 1) h += '<text x="' + X_(i) + '" y="' + (H - 8) + '" text-anchor="middle" font-size="11" fill="' + INK2 + '">' + esc(lb) + '</text>'; });
    series.slice().reverse().forEach(function (s) {
      var pts = s.values.map(function (v, i) { return X_(i) + ',' + Y_(v); }).join(' ');
      if (!s.muted) h += '<polygon points="' + X_(0) + ',' + Y_(0) + ' ' + pts + ' ' + X_(n - 1) + ',' + Y_(0) + '" fill="' + s.color + '" fill-opacity=".1"/>';
      h += '<polyline points="' + pts + '" fill="none" stroke="' + s.color + '" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"' + (s.muted ? ' stroke-dasharray="5 4"' : '') + '/>';
    });
    var last = series[0].values[n - 1];
    h += '<circle cx="' + X_(n - 1) + '" cy="' + Y_(last) + '" r="4" fill="' + series[0].color + '" stroke="#fff" stroke-width="2"/>';
    h += '<line class="xhair" x1="0" x2="0" y1="' + T + '" y2="' + (T + ih) + '" stroke="#1b1f24" stroke-width="1" opacity="0"/>';
    h += '<rect x="' + L + '" y="' + T + '" width="' + iw + '" height="' + ih + '" fill="transparent" class="hit" tabindex="0"/></svg>';
    el.innerHTML = h;
    var svg = $('svg', el), hit = $('.hit', el), xh = $('.xhair', el);
    function at(i) {
      xh.setAttribute('x1', X_(i)); xh.setAttribute('x2', X_(i)); xh.setAttribute('opacity', '.35');
      return '<b class="tip-title">' + esc(labels[i]) + '</b>' + series.map(function (s) { return '<span class="tip-row"><i style="background:' + s.color + '"></i><b>' + fmt(s.values[i]) + '</b><small>' + esc(s.name) + '</small></span>'; }).join('');
    }
    hit.addEventListener('pointermove', function (e) {
      var r = svg.getBoundingClientRect(), x = (e.clientX - r.left) * W / r.width;
      var i = Math.max(0, Math.min(n - 1, Math.round((x - L) / (iw / Math.max(1, n - 1)))));
      showTip(e, at(i));
    });
    hit.addEventListener('pointerleave', function () { hideTip(); xh.setAttribute('opacity', 0); });
    hit.addEventListener('focus', function () { var r = hit.getBoundingClientRect(); showTip({ clientX: r.right - 40, clientY: r.top + 20 }, at(n - 1)); });
    hit.addEventListener('blur', function () { hideTip(); xh.setAttribute('opacity', 0); });
  }

  function columnChart(el, labels, values, fmt, name) {
    var W = Math.max(320, el.clientWidth || 640), H = 220, L = 44, Rr = 8, T = 10, B = 26;
    var max = niceMax(Math.max.apply(null, values.concat([1]))), n = values.length, iw = W - L - Rr, ih = H - T - B;
    var band = iw / n, bw = Math.max(2, Math.min(24, band - 2));
    var h = '<svg viewBox="0 0 ' + W + ' ' + H + '" width="100%" height="' + H + '" role="img" aria-label="' + esc(name) + '">';
    for (var g = 0; g <= 4; g++) { var y = T + ih - ih * g / 4; h += '<line x1="' + L + '" x2="' + (W - Rr) + '" y1="' + y + '" y2="' + y + '" stroke="' + GRID + '"/><text x="' + (L - 8) + '" y="' + (y + 4) + '" text-anchor="end" font-size="11" fill="' + INK2 + '">' + compact(max * g / 4) + '</text>'; }
    var step = Math.ceil(n / 8);
    values.forEach(function (v, i) {
      var x = L + i * band + (band - bw) / 2, bh = v * ih / max, y = T + ih - bh, r = Math.min(4, bw / 2, bh);
      var d = 'M' + x + ',' + (T + ih) + 'V' + (y + r) + 'Q' + x + ',' + y + ' ' + (x + r) + ',' + y + 'H' + (x + bw - r) + 'Q' + (x + bw) + ',' + y + ' ' + (x + bw) + ',' + (y + r) + 'V' + (T + ih) + 'Z';
      h += '<path class="bar" d="' + d + '" fill="' + C1 + '" data-i="' + i + '"/>';
      h += '<rect class="bar-hit" x="' + (L + i * band) + '" y="' + T + '" width="' + band + '" height="' + ih + '" fill="transparent" data-i="' + i + '"/>';
      if ((i % step === 0 && i < n - 1 - step / 2) || i === n - 1) h += '<text x="' + (x + bw / 2) + '" y="' + (H - 8) + '" text-anchor="middle" font-size="11" fill="' + INK2 + '">' + esc(labels[i]) + '</text>';
    });
    el.innerHTML = h + '</svg>';
    $$('.bar-hit', el).forEach(function (r) {
      r.addEventListener('pointermove', function (e) { var i = +r.dataset.i; $$('.bar', el).forEach(function (b) { b.setAttribute('fill-opacity', +b.dataset.i === i ? 1 : .55); }); showTip(e, '<b class="tip-title">' + esc(labels[i]) + '</b><span class="tip-row"><i style="background:' + C1 + '"></i><b>' + fmt(values[i]) + '</b><small>' + esc(name) + '</small></span>'); });
      r.addEventListener('pointerleave', function () { hideTip(); $$('.bar', el).forEach(function (b) { b.setAttribute('fill-opacity', 1); }); });
    });
  }

  function barList(el, rows, fmt, opts) {
    opts = opts || {};
    if (!rows.length) { el.innerHTML = '<p class="muted">Chưa có dữ liệu trong khoảng này.</p>'; return; }
    var max = Math.max.apply(null, rows.map(function (r) { return r.value; }).concat([1])), total = rows.reduce(function (a, r) { return a + r.value; }, 0);
    el.innerHTML = '<ul class="hbars">' + rows.map(function (r) {
      return '<li tabindex="0" data-tipv="' + esc(r.label) + '|' + esc(fmt(r.value)) + '|' + Math.round(pct(r.value, total)) + '"><span class="hb-label" title="' + esc(r.label) + '">' + (r.dot ? '<i class="sdot" style="background:' + r.dot + '"></i>' : '') + esc(r.label) + '</span>' +
        '<span class="hb-track"><span class="hb-bar" style="width:' + Math.max(1, r.value * 100 / max) + '%"></span></span><span class="hb-val">' + fmt(r.value) + (opts.share ? '<small>' + Math.round(pct(r.value, total)) + '%</small>' : '') + '</span></li>';
    }).join('') + '</ul>';
    $$('[data-tipv]', el).forEach(function (li) {
      var p = li.dataset.tipv.split('|');
      li.addEventListener('pointermove', function (e) { showTip(e, '<b class="tip-title">' + esc(p[0]) + '</b><span class="tip-row"><b>' + esc(p[1]) + '</b><small>' + p[2] + '% tổng</small></span>'); });
      li.addEventListener('pointerleave', hideTip);
    });
  }
  function spark(values) {
    var W = 96, H = 28, max = Math.max.apply(null, values.concat([1])), n = values.length;
    var pts = values.map(function (v, i) { return (i * W / Math.max(1, n - 1)).toFixed(1) + ',' + (H - 2 - v * (H - 4) / max).toFixed(1); }).join(' ');
    return '<svg class="spark" viewBox="0 0 ' + W + ' ' + H + '" preserveAspectRatio="none" aria-hidden="true"><polyline points="' + pts + '" fill="none" stroke="' + C1 + '" stroke-width="1.5" stroke-linejoin="round" vector-effect="non-scaling-stroke"/></svg>';
  }
  function tableView(el, head, rows) {
    el.innerHTML = '<div class="table-wrap"><table class="tbl"><thead><tr>' + head.map(function (h, i) { return '<th' + (i ? ' class="num"' : '') + '>' + esc(h) + '</th>'; }).join('') + '</tr></thead><tbody>' +
      rows.map(function (r) { return '<tr>' + r.map(function (c, i) { return '<td' + (i ? ' class="num"' : '') + '>' + esc(c) + '</td>'; }).join('') + '</tr>'; }).join('') + '</tbody></table></div>';
  }
  $$('[data-table-toggle]').forEach(function (b) {
    b.addEventListener('click', function () {
      var t = $('[data-chart-table="' + b.dataset.tableToggle + '"]'), c = $('[data-chart="' + b.dataset.tableToggle + '"]');
      t.hidden = !t.hidden; c.hidden = !t.hidden; b.textContent = t.hidden ? 'Xem dạng bảng' : 'Xem biểu đồ';
    });
  });

  /* ---------------------------------------------------------------- shell: nav, notifications */
  function shell() {
    $$('[data-staff-name]').forEach(function (e) { e.textContent = staff.name; });
    $$('[data-staff-role]').forEach(function (e) { e.textContent = staff.role; });
    $$('[data-staff-initial]').forEach(function (e) { e.textContent = staff.name.trim().split(' ').slice(-1)[0][0]; });
    $$('[data-staff-logout]').forEach(function (b) { b.addEventListener('click', function () { X.set('adm:staff', null); location.href = 'login.html'; }); });
    var side = $('[data-side]'), scrim = $('[data-side-close]');
    $$('[data-side-open]').forEach(function (b) { b.addEventListener('click', function () { side.classList.add('is-open'); scrim.hidden = false; }); });
    scrim.addEventListener('click', function () { side.classList.remove('is-open'); scrim.hidden = true; });
    var orders = allOrders(), pending = orders.filter(function (o) { return o.status === 'pending'; });
    var low = lowStock();
    var nb = $('[data-nav-badge=orders]'); if (nb && pending.length) { nb.hidden = false; nb.textContent = pending.length; nb.classList.add('nav-badge-hot'); nb.title = pending.length + ' đơn chờ xác nhận'; }
    var sb = $('[data-nav-badge=stock]'); if (sb && low.length) { sb.hidden = false; sb.textContent = low.length; sb.title = low.length + ' phiên bản sắp hết hoặc hết hàng'; }
    var subCount = {
      pending: pending.length,
      confirmed: orders.filter(function (o) { return o.status === 'confirmed' || o.status === 'packing'; }).length,
      shipping: orders.filter(function (o) { return o.status === 'shipping'; }).length,
      returned: orders.filter(function (o) { return o.status === 'returned'; }).length
    };
    $$('[data-sub-count]').forEach(function (el) { var n = subCount[el.dataset.subCount]; el.textContent = n ? num(n) : ''; });
    var curStatus = page === 'orders' ? (params.get('status') || '') : '';
    $$('[data-sub]').forEach(function (a) { if (a.dataset.sub === curStatus) a.setAttribute('aria-current', 'true'); });
    // collapse to an icon rail (desktop), remembered per browser
    var root = document.documentElement, col = $('[data-side-collapse]');
    function setMini(v) {
      root.classList.toggle('side-mini', v);
      try { localStorage.setItem('adm-collapsed', v ? '1' : '0'); } catch (e) { /* ignore */ }
      if (col) { col.setAttribute('aria-pressed', v ? 'true' : 'false'); col.title = v ? 'Mở rộng menu' : 'Thu gọn menu'; }
    }
    if (col) { col.addEventListener('click', function () { setMini(!root.classList.contains('side-mini')); }); setMini(root.classList.contains('side-mini')); }
    palette(orders);
    // tables become stacked cards on phones: every cell gets its column name
    function cardify(table) {
      var heads = $$('thead th', table).map(function (h) { return h.textContent.trim(); });
      if (!heads.length) return;
      $$('tbody tr', table).forEach(function (tr) {
        var first = true;
        $$('td', tr).forEach(function (td, i) {
          if (td.hasAttribute('data-label')) return;
          td.setAttribute('data-label', heads[i] || '');
          if (td.classList.contains('ck')) return;
          if (first) { td.classList.add('td-main'); first = false; }
          var w = document.createElement('span'); w.className = 'td-v';
          while (td.firstChild) w.appendChild(td.firstChild);
          td.appendChild(w);
        });
      });
    }
    var pend = false;
    new MutationObserver(function () {
      if (pend) return; pend = true;
      requestAnimationFrame(function () { pend = false; $$('table.tbl').forEach(cardify); });
    }).observe(document.body, { childList: true, subtree: true });
    $$('table.tbl').forEach(cardify);
    // filters fold behind one button on phones
    $$('.filters-row').forEach(function (row) {
      var sels = $$('select', row); if (sels.length < 2) return;
      var b = document.createElement('button');
      b.type = 'button'; b.className = 'btn btn-sm filters-toggle'; b.setAttribute('aria-expanded', 'false');
      function label() { var n = sels.filter(function (s) { return s.value; }).length; b.innerHTML = X.icon('down', 'i-xs') + 'Bộ lọc' + (n ? ' (' + n + ')' : ''); }
      label();
      b.addEventListener('click', function () { var on = row.classList.toggle('is-open'); b.setAttribute('aria-expanded', on ? 'true' : 'false'); });
      sels.forEach(function (s) { s.addEventListener('change', label); });
      row.insertBefore(b, sels[0].closest('label') || sels[0]);
    });
    // keyboard shortcuts: ? lists them
    var KEYS = [['Ctrl K', 'Tìm nhanh đơn, sản phẩm, khách hàng'], ['/', 'Tìm trong danh sách đang xem'], ['j, k', 'Chọn đơn kế tiếp, đơn trước trong danh sách'],
      ['Enter', 'Mở xem nhanh đơn đang chọn'], ['x', 'Đánh dấu đơn đang chọn'], ['Esc', 'Đóng xem nhanh hoặc hộp thoại'], ['[ , ]', 'Sang đơn mới hơn, cũ hơn trong trang chi tiết'], ['?', 'Mở bảng phím tắt này']];
    document.addEventListener('keydown', function (e) {
      if (e.key !== '?' || typing(e) || $('dialog[open]')) return;
      var d = document.createElement('dialog'); d.className = 'modal keys';
      d.innerHTML = '<div class="modal-head"><h2>Phím tắt</h2><button class="icon-btn" type="button" aria-label="Đóng">' + X.icon('x', 'i-sm') + '</button></div><dl>' +
        KEYS.map(function (k) { return '<div><dt>' + k[0].split(', ').map(function (x) { return '<kbd>' + esc(x) + '</kbd>'; }).join(' ') + '</dt><dd>' + esc(k[1]) + '</dd></div>'; }).join('') + '</dl>';
      document.body.appendChild(d);
      $('button', d).addEventListener('click', function () { d.close(); });
      d.addEventListener('close', function () { d.remove(); });
      d.showModal();
    });
    // notifications: new web orders, low stock, reviews and questions waiting
    var read = X.get('adm:notif_read', 0), items = [];
    orders.filter(function (o) { return o.source === 'web'; }).slice(0, 6).forEach(function (o) { items.push({ at: o.created, html: '<b>Đơn mới ' + esc(o.code) + '</b><span>' + esc(o.customer.name) + ', ' + vnd(o.total) + '</span>', href: 'order.html?code=' + o.code }); });
    low.filter(function (x) { return x.stock === 0; }).slice(0, 3).forEach(function (x) { items.push({ at: new Date(TODAY).toISOString(), html: '<b>Hết hàng</b><span>' + esc(x.name) + ' (' + esc(x.label) + ')</span>', href: 'inventory.html?tab=out' }); });
    X.get('reviews', []).filter(function (r) { return r.status === 'pending'; }).forEach(function (r) { items.push({ at: new Date(r.at).toISOString(), html: '<b>Đánh giá chờ duyệt</b><span>' + esc(r.name) + ', ' + r.stars + ' sao: ' + esc((X.byId[r.product] || {}).name || '') + '</span>', href: 'products.html' }); });
    X.get('questions', []).forEach(function (q) { items.push({ at: new Date(q.at).toISOString(), html: '<b>Câu hỏi mới</b><span>' + esc(q.q) + '</span>', href: 'products.html' }); });
    items.sort(function (a, b) { return b.at < a.at ? -1 : 1; });
    var unread = items.filter(function (i) { return new Date(i.at).getTime() > read; }).length;
    var dot = $('[data-notif-dot]'); dot.hidden = !unread;
    function ago(iso) { var m = Math.round((Date.now() - new Date(iso)) / 6e4); return m < 60 ? Math.max(1, m) + ' phút trước' : m < 1440 ? Math.round(m / 60) + ' giờ trước' : Math.round(m / 1440) + ' ngày trước'; }
    $('[data-notif-list]').innerHTML = items.slice(0, 12).map(function (i) { return '<li class="' + (new Date(i.at).getTime() > read ? 'unread' : '') + '"><a href="' + i.href + '">' + i.html + '<small>' + ago(i.at) + '</small></a></li>'; }).join('') || '<li class="muted">Không có thông báo mới.</li>';
    var tg = $('[data-notif-toggle]'), panel = $('[data-notif-panel]');
    tg.addEventListener('click', function (e) { e.stopPropagation(); panel.hidden = !panel.hidden; tg.setAttribute('aria-expanded', panel.hidden ? 'false' : 'true'); });
    document.addEventListener('click', function (e) { if (!panel.hidden && !panel.contains(e.target)) { panel.hidden = true; tg.setAttribute('aria-expanded', 'false'); } });
    $('[data-notif-read]').addEventListener('click', function () { X.set('adm:notif_read', Date.now()); dot.hidden = true; $$('.unread', panel).forEach(function (li) { li.classList.remove('unread'); }); });
    $$('[data-export]').forEach(function (b) { b.addEventListener('click', function () { exporters[b.dataset.export](); }); });
  }

  /* ---------------------------------------------------------------- quick search (Ctrl K) */
  var PAGES = [['index.html', 'Tổng quan', 'dashboard'], ['orders.html', 'Đơn hàng', 'clipboard'], ['orders.html?status=pending', 'Đơn chờ xác nhận', 'clipboard'],
    ['products.html', 'Sản phẩm', 'package'], ['inventory.html', 'Tồn kho', 'package'], ['inventory.html?tab=low', 'Hàng sắp hết', 'alert'], ['customers.html', 'Khách hàng', 'home'],
    ['promotions.html', 'Khuyến mãi', 'store'], ['reports.html', 'Báo cáo', 'search'], ['settings.html', 'Cài đặt', 'store'], [R + 'index.html', 'Xem cửa hàng', 'store']];
  function palette(orders) {
    var dlg = $('[data-palette]'); if (!dlg) return;
    var input = $('[data-palette-q]', dlg), list = $('[data-palette-list]', dlg), sel = 0, people = null;
    function row(href, main, sub, img, ic) {
      return '<li><a href="' + href + '" role="option">' + (img ? '<img src="' + R + img + '" alt="">' : '<span class="pl-ic">' + X.icon(ic || 'search', 'i-sm') + '</span>') + '<span>' + esc(main) + '</span>' + (sub ? '<small>' + esc(sub) + '</small>' : '') + '</a></li>';
    }
    function render() {
      var q = X.norm(input.value.trim()), h = '';
      var pages = PAGES.filter(function (p) { return !q || X.norm(p[1]).indexOf(q) >= 0; });
      if (q) {
        var os = orders.filter(function (o) { return X.norm(o.code + ' ' + o.customer.name + ' ' + o.customer.phone).indexOf(q) >= 0; }).slice(0, 6);
        var ps = S.products.filter(function (p) { return X.norm(p.name + ' ' + p.variants.map(function (v) { return v.sku; }).join(' ')).indexOf(q) >= 0; }).slice(0, 6);
        people = people || customers();
        var cs = people.filter(function (c) { return X.norm(c.name + ' ' + c.phone + ' ' + (c.email || '')).indexOf(q) >= 0; }).slice(0, 5);
        if (os.length) h += '<li class="pl-group">Đơn hàng</li>' + os.map(function (o) { return row('order.html?code=' + o.code, o.code + ', ' + o.customer.name, vnd(o.total) + ', ' + (X.STATUS[o.status] || ''), null, 'clipboard'); }).join('');
        if (ps.length) h += '<li class="pl-group">Sản phẩm</li>' + ps.map(function (p) { return row('product.html?id=' + p.id, p.name, p.category_name, p.image); }).join('');
        if (cs.length) h += '<li class="pl-group">Khách hàng</li>' + cs.map(function (c) { return row('customer.html?id=' + encodeURIComponent(c.id), c.name, c.phone, null, 'home'); }).join('');
      } else {
        h += '<li class="pl-group">Đơn mới nhất</li>' + orders.slice(0, 4).map(function (o) { return row('order.html?code=' + o.code, o.code + ', ' + o.customer.name, vnd(o.total), null, 'clipboard'); }).join('');
      }
      if (pages.length) h += '<li class="pl-group">Trang</li>' + pages.map(function (p) { return row(p[0], p[1], '', null, p[2]); }).join('');
      list.innerHTML = h || '<li class="pl-empty">Không tìm thấy kết quả cho "' + esc(input.value) + '".</li>';
      sel = 0; mark();
    }
    function links() { return $$('a', list); }
    function mark() { links().forEach(function (a, i) { a.setAttribute('aria-selected', i === sel ? 'true' : 'false'); if (i === sel) a.scrollIntoView({ block: 'nearest' }); }); }
    function open() { if (dlg.open) return; dlg.showModal(); input.value = ''; render(); input.focus(); }
    $$('[data-palette-open]').forEach(function (b) { b.addEventListener('click', open); });
    document.addEventListener('keydown', function (e) {
      if ((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'K')) { e.preventDefault(); open(); }
    });
    input.addEventListener('input', render);
    input.addEventListener('keydown', function (e) {
      var ls = links();
      if (e.key === 'ArrowDown') { e.preventDefault(); sel = Math.min(ls.length - 1, sel + 1); mark(); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); sel = Math.max(0, sel - 1); mark(); }
      else if (e.key === 'Enter' && ls[sel]) { e.preventDefault(); location.href = ls[sel].href; }
      else if (e.key === 'Escape') { e.preventDefault(); dlg.close(); }
    });
    dlg.addEventListener('click', function (e) { if (e.target === dlg) dlg.close(); });
  }

  /* ---------------------------------------------------------------- stock */
  function allVariants() {
    var out = [];
    S.products.forEach(function (p) { p.variants.forEach(function (v) { var x = X.variant(v.sku); out.push({ sku: v.sku, p: p, name: p.name, label: v.label, stock: x.stock, price: x.price, image: p.image }); }); });
    return out;
  }
  function lowStock() { return allVariants().filter(function (x) { return x.stock <= 5; }); }

  /* ---------------------------------------------------------------- period: date range picker */
  // A period is {from, to} in day indexes relative to today (0 = today, -1 = yesterday) plus a comparison.
  function dateOf(k) { return new Date(TODAY.getTime() + k * DAY); }
  function kOf(d) { var x = new Date(d); x.setHours(0, 0, 0, 0); return Math.round((x - TODAY) / DAY); }
  function iso(k) { var d = dateOf(k); return d.getFullYear() + '-' + ('0' + (d.getMonth() + 1)).slice(-2) + '-' + ('0' + d.getDate()).slice(-2); }
  function fromIso(s) { var p = String(s || '').split('-'); return p.length === 3 ? kOf(new Date(+p[0], +p[1] - 1, +p[2])) : null; }
  function dmy(k) { return X.dayText(dateOf(k)); }
  function lastYear(k) { var d = dateOf(k); d.setFullYear(d.getFullYear() - 1); return kOf(d); }
  function monDow(k) { return (dateOf(k).getDay() + 6) % 7; }
  var PRESETS = [
    ['today', 'Hôm nay', function () { return [0, 0]; }],
    ['yesterday', 'Hôm qua', function () { return [-1, -1]; }],
    ['7d', '7 ngày qua', function () { return [-6, 0]; }],
    ['30d', '30 ngày qua', function () { return [-29, 0]; }],
    ['90d', '90 ngày qua', function () { return [-89, 0]; }],
    ['week', 'Tuần này', function () { return [-monDow(0), 0]; }],
    ['lastweek', 'Tuần trước', function () { var s = -monDow(0) - 7; return [s, s + 6]; }],
    ['month', 'Tháng này', function () { return [1 - TODAY.getDate(), 0]; }],
    ['lastmonth', 'Tháng trước', function () { var e = -TODAY.getDate(); return [e + 1 - dateOf(e).getDate(), e]; }],
    ['quarter', 'Quý này', function () { var d = new Date(TODAY.getFullYear(), Math.floor(TODAY.getMonth() / 3) * 3, 1); return [kOf(d), 0]; }],
    ['lastquarter', 'Quý trước', function () { var q = Math.floor(TODAY.getMonth() / 3) * 3; return [kOf(new Date(TODAY.getFullYear(), q - 3, 1)), kOf(new Date(TODAY.getFullYear(), q, 0))]; }],
    ['year', 'Năm nay', function () { return [kOf(new Date(TODAY.getFullYear(), 0, 1)), 0]; }],
    ['lastyear', 'Năm trước', function () { return [kOf(new Date(TODAY.getFullYear() - 1, 0, 1)), kOf(new Date(TODAY.getFullYear() - 1, 11, 31))]; }],
    ['12m', '12 tháng qua', function () { return [lastYear(0) + 1, 0]; }]
  ];
  var CMP = [['prev', 'Kỳ liền trước'], ['year', 'Cùng kỳ năm trước'], ['none', 'Không so sánh']];
  function presetLabel(key) { var p = PRESETS.filter(function (x) { return x[0] === key; })[0]; return p ? p[1] : 'Tùy chọn'; }
  function shiftMonths(k, n) {
    var d = dateOf(k), day = d.getDate(), x = new Date(d.getFullYear(), d.getMonth() - n, 1);
    x.setDate(Math.min(day, new Date(x.getFullYear(), x.getMonth() + 1, 0).getDate()));
    return kOf(x);
  }
  function compareRange(from, to, cmp, preset) {
    if (cmp === 'none') return null;
    if (cmp === 'year') return [lastYear(from), lastYear(to)];
    // period to date: same days of the previous week, month or quarter (Sapo, Shopify)
    if (preset === 'week') return [from - 7, to - 7];
    if (preset === 'month') return [shiftMonths(from, 1), shiftMonths(to, 1)];
    if (preset === 'quarter') return [shiftMonths(from, 3), shiftMonths(to, 3)];
    if (preset === 'year') return [lastYear(from), lastYear(to)];
    if (preset === 'lastmonth') { var e = from - 1; return [e + 1 - dateOf(e).getDate(), e]; }
    var len = to - from + 1;
    return [from - len, from - 1];
  }
  function readPeriod() {
    var saved = X.get('adm:period', null) || { preset: '30d', cmp: 'prev' };
    var p = { preset: params.get('p') || saved.preset, cmp: params.get('cmp') || saved.cmp || 'prev', from: null, to: null };
    if (params.get('from') && params.get('to')) { p.preset = 'custom'; p.from = fromIso(params.get('from')); p.to = fromIso(params.get('to')); }
    else if (p.preset === 'custom') { p.from = saved.from; p.to = saved.to; }
    var pr = PRESETS.filter(function (x) { return x[0] === p.preset; })[0];
    if (pr) { var r = pr[2](); p.from = r[0]; p.to = r[1]; }
    if (p.from == null || p.to == null || p.from > p.to || p.to > 0) { p.preset = '30d'; p.from = -29; p.to = 0; }
    return p;
  }
  function writePeriod(p) {
    X.set('adm:period', { preset: p.preset, cmp: p.cmp, from: p.from, to: p.to });
    var u = new URL(location.href);
    ['p', 'from', 'to', 'cmp'].forEach(function (k) { u.searchParams.delete(k); });
    if (p.preset === 'custom') { u.searchParams.set('from', iso(p.from)); u.searchParams.set('to', iso(p.to)); } else u.searchParams.set('p', p.preset);
    u.searchParams.set('cmp', p.cmp);
    try { history.replaceState(null, '', u.toString()); } catch (e) { /* file:// in some browsers */ }
  }
  function describe(p) {
    var c = compareRange(p.from, p.to, p.cmp, p.preset);
    p.cmpFrom = c ? c[0] : null; p.cmpTo = c ? c[1] : null; p.days = p.to - p.from + 1;
    p.label = p.preset === 'custom' ? 'Tùy chọn' : presetLabel(p.preset);
    p.range = p.from === p.to ? dmy(p.from) : dmy(p.from) + ' - ' + dmy(p.to);
    p.cmpRange = c ? (c[0] === c[1] ? dmy(c[0]) : dmy(c[0]) + ' - ' + dmy(c[1])) : '';
    return p;
  }
  function periodControl(onChange) {
    var box = $('[data-period]'); if (!box) return;
    var p = describe(readPeriod()), draft;
    var minK = SEED_DAYS ? -(SEED_DAYS - 1) : lastYear(lastYear(0));
    box.innerHTML = '<button class="period-btn" type="button" aria-haspopup="dialog" aria-expanded="false" data-period-btn>' + X.icon('calendar', 'i-sm') +
      '<span class="period-txt"><b data-period-label></b><small data-period-range></small></span>' + X.icon('down', 'i-sm') + '</button>' +
      '<div class="period-pop" role="dialog" aria-label="Chọn khoảng thời gian" data-period-pop hidden>' +
      '<div class="period-body"><ul class="period-presets" role="listbox" aria-label="Khoảng có sẵn">' +
      PRESETS.map(function (x) { return '<li><button type="button" role="option" data-preset="' + x[0] + '">' + X.icon('check', 'i-sm') + '<span>' + x[1] + '</span><small data-preset-range="' + x[0] + '"></small></button></li>'; }).join('') +
      '<li><button type="button" role="option" data-preset="custom">' + X.icon('check', 'i-sm') + '<span>Tùy chọn</span></button></li></ul>' +
      '<div class="period-side"><fieldset class="period-dates"><legend>Từ ngày, đến ngày</legend>' +
      '<label><span>Từ ngày</span><input type="text" inputmode="numeric" placeholder="dd/mm/yyyy" maxlength="10" data-pfrom></label><label><span>Đến ngày</span><input type="text" inputmode="numeric" placeholder="dd/mm/yyyy" maxlength="10" data-pto></label>' +
      '<p class="period-err" data-perr hidden></p></fieldset>' +
      '<fieldset class="period-cmp"><legend>So sánh với</legend>' + CMP.map(function (c) { return '<label><input type="radio" name="cmp" value="' + c[0] + '"><span>' + c[1] + '</span></label>'; }).join('') +
      '<p class="muted small" data-pcmp-range></p></fieldset></div></div>' +
      '<div class="period-foot"><button class="btn btn-ghost" type="button" data-period-cancel>Hủy</button><button class="btn btn-primary" type="button" data-period-apply>Áp dụng</button></div></div>';
    var btn = $('[data-period-btn]', box), pop = $('[data-period-pop]', box), fIn = $('[data-pfrom]', box), tIn = $('[data-pto]', box);
    function parseDmy(v) {
      var m = String(v || '').trim().match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})$/);
      if (!m) return null;
      var d = new Date(+m[3], +m[2] - 1, +m[1]);
      return d.getDate() === +m[1] && d.getMonth() === +m[2] - 1 ? kOf(d) : null;
    }
    [fIn, tIn].forEach(function (inp) {
      inp.addEventListener('input', function () {
        var v = inp.value.replace(/[^0-9]/g, '').slice(0, 8);
        inp.value = v.length > 4 ? v.slice(0, 2) + '/' + v.slice(2, 4) + '/' + v.slice(4) : v.length > 2 ? v.slice(0, 2) + '/' + v.slice(2) : v;
      });
    });
    PRESETS.forEach(function (x) { var r = x[2](); $('[data-preset-range="' + x[0] + '"]', box).textContent = r[0] === r[1] ? dmy(r[0]).slice(0, 5) : dmy(r[0]).slice(0, 5) + ' - ' + dmy(r[1]).slice(0, 5); });
    function paint() {
      $('[data-period-label]', box).textContent = p.label;
      $('[data-period-range]', box).textContent = p.range;
      var note = 'Từ ' + p.range + (p.cmpRange ? ', so với ' + p.cmpRange + ' (' + CMP.filter(function (c) { return c[0] === p.cmp; })[0][1].toLowerCase() + ')' : '') + '.';
      if (SEED_DAYS && p.cmpFrom != null && p.cmpFrom < minK) note += ' Dữ liệu mẫu bắt đầu từ ' + dmy(minK) + ', nên kỳ so sánh thiếu một phần.';
      var rn = $('[data-range-note]'); if (rn) rn.textContent = note;
    }
    function syncDraft() {
      $$('[data-preset]', box).forEach(function (b) { b.setAttribute('aria-selected', b.dataset.preset === draft.preset ? 'true' : 'false'); });
      fIn.value = dmy(draft.from); tIn.value = dmy(draft.to);
      $$('input[name=cmp]', box).forEach(function (r) { r.checked = r.value === draft.cmp; });
      describe(draft);
      $('[data-pcmp-range]', box).textContent = draft.cmpRange ? 'Kỳ so sánh: ' + draft.cmpRange : '';
    }
    function open(v) {
      pop.hidden = !v; btn.setAttribute('aria-expanded', v ? 'true' : 'false');
      if (v) { draft = Object.assign({}, p); syncDraft(); var sel = $('[data-preset][aria-selected=true]', box); (sel || fIn).focus(); }
    }
    btn.addEventListener('click', function (e) { e.stopPropagation(); open(pop.hidden); });
    document.addEventListener('click', function (e) { if (!pop.hidden && !box.contains(e.target)) open(false); });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && !pop.hidden) { open(false); btn.focus(); } });
    $$('[data-preset]', box).forEach(function (b) {
      b.addEventListener('click', function () {
        draft.preset = b.dataset.preset;
        if (draft.preset !== 'custom') { var r = PRESETS.filter(function (x) { return x[0] === draft.preset; })[0][2](); draft.from = r[0]; draft.to = r[1]; }
        syncDraft();
        if (draft.preset === 'custom') fIn.focus();
      });
      b.addEventListener('dblclick', function () { apply(); });
    });
    [fIn, tIn].forEach(function (inp) {
      inp.addEventListener('change', function () {
        var f = parseDmy(fIn.value), t = parseDmy(tIn.value), err = $('[data-perr]', box);
        err.hidden = true;
        if (f == null || t == null) { err.hidden = false; err.textContent = 'Nhập ngày theo dạng ngày/tháng/năm, ví dụ ' + dmy(-29) + '.'; return; }
        if (f > t) { err.hidden = false; err.textContent = 'Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.'; return; }
        if (t > 0 || f < minK) { err.hidden = false; err.textContent = 'Chọn trong khoảng ' + dmy(minK) + ' đến ' + dmy(0) + '.'; }
        f = Math.max(minK, f); t = Math.min(0, t);
        draft.preset = 'custom'; draft.from = Math.max(minK, f); draft.to = Math.min(0, t); syncDraft();
      });
    });
    $$('input[name=cmp]', box).forEach(function (r) { r.addEventListener('change', function () { draft.cmp = r.value; syncDraft(); }); });
    $('[data-period-cancel]', box).addEventListener('click', function () { open(false); btn.focus(); });
    function apply() {
      if (!$('[data-perr]', box).hidden) return;
      p = describe(Object.assign({}, draft)); writePeriod(p); open(false); paint(); onChange(p); btn.focus();
    }
    $('[data-period-apply]', box).addEventListener('click', apply);
    paint(); onChange(p);
  }

  /* ---------------------------------------------------------------- metrics and buckets */
  var METRICS = {
    revenue: { label: 'Doanh thu thuần', fmt: function (v) { return vnd(v); }, tile: function (v) { return v >= 1e9 ? (v / 1e9).toFixed(2).replace('.', ',') + ' tỷ đ' : v >= 1e8 ? (v / 1e6).toFixed(1).replace('.', ',') + ' triệu đ' : vnd(v); }, axis: function (v) { return compact(v); } },
    orders: { label: 'Đơn hàng', fmt: function (v) { return num(v) + ' đơn'; }, axis: function (v) { return compact(v); } },
    aov: { label: 'Giá trị đơn trung bình', fmt: function (v) { return vnd(v); }, axis: function (v) { return compact(v); } },
    sessions: { label: 'Lượt truy cập', fmt: function (v) { return num(v); }, axis: function (v) { return compact(v); } },
    conv: { label: 'Tỷ lệ chuyển đổi', fmt: function (v) { return v.toFixed(2).replace('.', ',') + '%'; }, axis: function (v) { return v.toFixed(1).replace('.', ',') + '%'; }, rate: true },
    returning: { label: 'Tỷ lệ khách quay lại', fmt: function (v) { return v.toFixed(1).replace('.', ',') + '%'; }, axis: function (v) { return Math.round(v) + '%'; }, rate: true }
  };
  var index = null;
  function buildIndex() {
    if (index && index.src === allOrders()) return index;
    var os = allOrders(), byDay = {}, first = {}, traffic = {};
    os.slice().reverse().forEach(function (o) {
      var key = o.customer.phone + o.customer.name;
      if (!(key in first)) first[key] = o.k;
      o.ckey = key;
    });
    os.forEach(function (o) { (byDay[o.k] = byDay[o.k] || []).push(o); });
    (SEED.traffic || []).forEach(function (r) { traffic[r[0]] = r; });
    index = { src: os, byDay: byDay, first: first, traffic: traffic };
    return index;
  }
  function ordersIn(from, to) {
    var ix = buildIndex(), out = [];
    for (var k = from; k <= to; k++) if (ix.byDay[k]) out = out.concat(ix.byDay[k]);
    return out;
  }
  function measure(from, to, hour) {
    var ix = buildIndex(), all = ordersIn(from, to);
    if (hour != null) all = all.filter(function (o) { return o.hour === hour; });
    var ok = all.filter(live), rev = ok.reduce(function (a, o) { return a + net(o); }, 0);
    var t = { sessions: 0, carts: 0, checkouts: 0 };
    for (var k = from; k <= to; k++) { var r = ix.traffic[k]; if (r) { t.sessions += r[1]; t.carts += r[2]; t.checkouts += r[3]; } }
    if (hour != null) { t.sessions = Math.round(t.sessions * (ok.length / Math.max(1, ordersIn(from, to).filter(live).length))); }
    var web = all.filter(function (o) { return o.channel === 'Website'; }).length;
    var buyers = {}, ret = 0, newc = 0;
    ok.forEach(function (o) { buyers[o.ckey] = (buyers[o.ckey] || 0) + 1; });
    var revNew = 0, revRet = 0, isRet = {};
    Object.keys(buyers).forEach(function (key) { isRet[key] = ix.first[key] < from || buyers[key] > 1; if (isRet[key]) ret++; if (ix.first[key] >= from && ix.first[key] <= to) newc++; });
    ok.forEach(function (o) { if (ix.first[o.ckey] < from || ix.first[o.ckey] < o.k) revRet += net(o); else revNew += net(o); });
    var nb = Object.keys(buyers).length;
    return {
      all: all, ok: ok, revenue: rev, orders: ok.length, aov: ok.length ? rev / ok.length : 0, sessions: t.sessions, carts: t.carts, checkouts: t.checkouts,
      web: web, conv: t.sessions ? web * 100 / t.sessions : 0, returning: nb ? ret * 100 / nb : 0, buyers: nb, newc: newc, retc: ret, revNew: revNew, revRet: revRet, days: to - from + 1,
      cancelled: all.filter(function (o) { return o.status === 'cancelled'; }).length, returned: all.filter(function (o) { return o.status === 'returned'; }).length
    };
  }
  function grains(days) { return days === 1 ? ['hour'] : days <= 31 ? ['day', 'week'] : days <= 120 ? ['day', 'week', 'month'] : ['week', 'month']; }
  function defaultGrain(days) { return days === 1 ? 'hour' : days <= 62 ? 'day' : days <= 180 ? 'week' : 'month'; }
  var GRAIN_LABEL = { hour: 'Giờ', day: 'Ngày', week: 'Tuần', month: 'Tháng' };
  function buckets(from, to, grain) {
    var out = [];
    if (grain === 'hour') { for (var h = 0; h < 24; h++) out.push({ label: h + 'h', from: from, to: to, hour: h }); return out; }
    var k = from;
    while (k <= to) {
      var d = dateOf(k), end;
      if (grain === 'day') end = k;
      else if (grain === 'week') end = Math.min(to, k + (6 - monDow(k)));
      else end = Math.min(to, k + (new Date(d.getFullYear(), d.getMonth() + 1, 0).getDate() - d.getDate()));
      var label = grain === 'day' ? dmy(k).slice(0, 5) : grain === 'week' ? 'Tuần ' + dmy(k).slice(0, 5) : 'T' + (d.getMonth() + 1) + '/' + d.getFullYear();
      out.push({ label: label, from: k, to: end });
      k = end + 1;
    }
    return out;
  }
  function series(from, to, grain, metric) {
    return buckets(from, to, grain).map(function (b) { var m = measure(b.from, b.to, b.hour); return { label: b.label, from: b.from, to: b.to, v: m[metric] }; });
  }
  function change(cur, prev, rate) {
    if (prev == null) return null;
    if (!prev && !rate) return cur > 0 ? 100 : cur < 0 ? -100 : 0;
    return rate ? cur - prev : (cur - prev) * 100 / prev;
  }
  function deltaHtml(d, rate) {
    if (d == null || !isFinite(d)) return '<span class="muted">Chưa có kỳ so sánh</span>';
    if (Math.abs(d) < 0.05) return '<span class="delta flat">0' + (rate ? ' điểm' : '%') + '</span>';
    var up = d >= 0;
    return '<span class="delta ' + (up ? 'up' : 'down') + '">' + X.icon(up ? 'up' : 'down', 'i-xs') + (up ? '+' : '') + d.toFixed(1).replace('.', ',') + (rate ? ' điểm' : '%') + '</span>';
  }
  function kpiTiles(el, cur, prev, sparks, active, onPick) {
    var keys = ['revenue', 'orders', 'aov', 'sessions', 'conv', 'returning'];
    el.innerHTML = keys.map(function (key) {
      var M = METRICS[key], d = prev ? change(cur[key], prev[key], M.rate) : null;
      return '<li><button class="kpi kpi-btn" type="button" data-kpi="' + key + '" aria-pressed="' + (key === active) + '" title="' + M.label + ': ' + M.fmt(cur[key]) + '"><small>' + M.label + '</small><b>' + (M.tile || M.fmt)(cur[key]) + '</b>' + (cur.days > 1 && (key === 'revenue' || key === 'orders') ? '<span class="kpi-avg">' + (key === 'revenue' ? compact(cur.revenue / cur.days) + ' đ' : (cur.orders / cur.days).toFixed(1).replace('.', ',') + ' đơn') + ' mỗi ngày</span>' : '') +
        '<span class="kpi-foot">' + (prev ? deltaHtml(d, M.rate) + '<span class="muted">' + (prev.label || '') + '</span>' : '') + '</span>' + (sparks && sparks[key] ? spark(sparks[key]) : '') + '</button></li>';
    }).join('');
    if (onPick) $$('[data-kpi]', el).forEach(function (b) { b.addEventListener('click', function () { onPick(b.dataset.kpi); }); });
  }
  function stats(orders, from, to) { var m = measure(from, to); m.count = m.orders; m.newc = m.newc; return m; }

  /* ---------------------------------------------------------------- pages */
  var page = ($('[data-admin]') || {}).dataset ? $('[data-admin]').dataset.admin : '';
  var exporters = {
    orders: function () { csv('don-hang', [['Mã đơn', 'Ngày', 'Khách', 'Điện thoại', 'Tỉnh', 'Kênh', 'Thanh toán', 'Đã thu', 'Trạng thái', 'Tạm tính', 'Giảm', 'Phí giao', 'Tổng']].concat(allOrders().map(function (o) { return [o.code, X.dateText(o.created), o.customer.name, o.customer.phone, (o.address || {}).province || (o.store || {}).name, o.channel, o.payment.name, o.paid ? 'Có' : 'Không', X.STATUS[o.status], o.subtotal, o.discount, o.shipping_fee, o.total]; }))); },
    products: function () { csv('san-pham', [['Mã', 'Tên', 'Danh mục', 'Thương hiệu', 'SKU', 'Phiên bản', 'Giá', 'Giá gốc', 'Tồn']].concat(allVariants().map(function (x) { var v = X.variant(x.sku); return [x.p.id, x.name, x.p.category_name, x.p.brand_name, x.sku, x.label, v.price, v.old || '', v.stock]; }))); },
    inventory: function () { csv('ton-kho', [['SKU', 'Sản phẩm', 'Phiên bản', 'Tồn', 'Giá trị tồn']].concat(allVariants().map(function (x) { return [x.sku, x.name, x.label, x.stock, x.stock * x.price]; }))); },
    customers: function () { csv('khach-hang', [['Mã', 'Tên', 'Điện thoại', 'Email', 'Tỉnh', 'Số đơn', 'Tổng chi', 'Đơn gần nhất']].concat(customers().map(function (c) { return [c.id, c.name, c.phone, c.email, c.city, c.orders.length, c.spent, c.last ? X.dayText(c.last) : '']; }))); },
    report: function () { var r = productReport(reportRange[0], reportRange[1]); csv('bao-cao-san-pham', [['Sản phẩm', 'Danh mục', 'Số lượng', 'Doanh thu', 'Số đơn']].concat(r.map(function (x) { return [x.name, x.cat, x.qty, x.rev, x.orders]; }))); }
  };

  var reportRange = [-29, 0];
  function productReport(from, to) {
    if (to == null) { to = 0; from = -(from - 1); }
    var m = {};
    allOrders().filter(function (o) { return live(o) && inRange(o, from, to); }).forEach(function (o) {
      o.items.forEach(function (it) {
        var p = X.byId[it.id] || {}, r = m[it.id] = m[it.id] || { id: it.id, name: it.name, cat: p.category_name || '', brand: p.brand_name || '', image: p.image, qty: 0, rev: 0, orders: 0 };
        r.qty += it.qty; r.rev += it.price * it.qty; r.orders++;
      });
    });
    return Object.keys(m).map(function (k) { return m[k]; }).sort(function (a, b) { return b.rev - a.rev; });
  }
  function groupSum(list, key, val) {
    var m = {};
    list.forEach(function (o) { var k = key(o); if (k == null) return; m[k] = (m[k] || 0) + val(o); });
    return Object.keys(m).map(function (k) { return { label: k, value: m[k] }; }).sort(function (a, b) { return b.value - a.value; });
  }

  function funnelHtml(m) {
    var steps = [['Lượt truy cập website', m.sessions], ['Thêm vào giỏ', m.carts], ['Bắt đầu thanh toán', m.checkouts], ['Đặt hàng trên website', m.web]];
    if (!m.sessions) return '<p class="muted">Chưa có số liệu truy cập cho khoảng này.</p>';
    return '<ol class="funnel">' + steps.map(function (s, i) {
      var share = s[1] * 100 / steps[0][1], step = i ? (steps[i - 1][1] ? s[1] * 100 / steps[i - 1][1] : 0) : 100;
      return '<li><div class="fn-head"><span>' + s[0] + '</span><b>' + num(s[1]) + '</b></div><span class="fn-track"><span class="fn-bar" style="width:' + Math.max(1.5, share) + '%"></span></span>' +
        '<small>' + (i ? step.toFixed(1).replace('.', ',') + '% từ bước trước, ' : '') + share.toFixed(share < 10 ? 2 : 1).replace('.', ',') + '% lượt truy cập</small></li>';
    }).join('') + '</ol>';
  }
  var HEAT = ['#eef0f3', '#cde2fb', '#9ec5f4', '#6da7ec', '#3987e5', '#256abf', '#104281'];
  function heatmap(el, list) {
    var days = ['Thứ 2', 'Thứ 3', 'Thứ 4', 'Thứ 5', 'Thứ 6', 'Thứ 7', 'CN'], hours = [], grid = [], max = 0, outside = 0;
    for (var h = 7; h <= 23; h++) hours.push(h);
    for (var r = 0; r < 7; r++) grid.push(hours.map(function () { return 0; }));
    list.forEach(function (o) {
      if (o.hour < 7) { outside++; return; }
      var r = (new Date(o.created).getDay() + 6) % 7; grid[r][o.hour - 7]++;
    });
    grid.forEach(function (row) { row.forEach(function (v) { if (v > max) max = v; }); });
    function bin(v) { return v === 0 ? 0 : Math.min(6, 1 + Math.floor((v / max) * 5.999)); }
    var h = '<div class="heat" role="table" aria-label="Số đơn theo thứ và giờ"><div class="heat-row heat-hours" role="row"><span role="columnheader"></span>' +
      hours.map(function (x) { return '<span role="columnheader">' + (x % 2 ? '' : x + 'h') + '</span>'; }).join('') + '</div>';
    grid.forEach(function (row, r) {
      h += '<div class="heat-row" role="row"><span class="heat-day" role="rowheader">' + days[r] + '</span>' + row.map(function (v, c) {
        return '<span class="heat-cell" role="cell" tabindex="-1" style="background:' + HEAT[bin(v)] + '" data-hv="' + days[r] + ', ' + hours[c] + 'h-' + (hours[c] + 1) + 'h|' + v + '"><span class="sr">' + v + ' đơn</span></span>';
      }).join('') + '</div>';
    });
    h += '</div><div class="heat-legend"><span>Ít đơn</span>' + HEAT.slice(1).map(function (c) { return '<i style="background:' + c + '"></i>'; }).join('') + '<span>Nhiều đơn</span></div>';
    el.innerHTML = h;
    $$('[data-hv]', el).forEach(function (c) {
      var p = c.dataset.hv.split('|');
      c.addEventListener('pointermove', function (e) { showTip(e, '<b class="tip-title">' + esc(p[0]) + '</b><span class="tip-row"><b>' + num(+p[1]) + ' đơn</b></span>'); });
      c.addEventListener('pointerleave', hideTip);
    });
    return outside;
  }
  function leaders(el, rows, empty) {
    el.innerHTML = rows.length ? rows.map(function (r, i) {
      return '<li><span class="ld-rank">' + (i + 1) + '</span><span class="ld-main">' + (r.href ? '<a href="' + r.href + '">' + esc(r.name) + '</a>' : esc(r.name)) + '<small>' + esc(r.sub) + '</small></span><b>' + r.value + '</b></li>';
    }).join('') : '<li class="muted">' + empty + '</li>';
  }

  function pageIndex() {
    var hr = new Date().getHours();
    $('[data-greeting]').textContent = (hr < 11 ? 'Chào buổi sáng, ' : hr < 18 ? 'Chào buổi chiều, ' : 'Chào buổi tối, ') + staff.name + '.';
    var metric = X.get('adm:metric', 'revenue'), grain = null, period = null;
    if (!METRICS[metric]) metric = 'revenue';
    function drawMain() {
      var M = METRICS[metric], gs = grains(period.days);
      if (!grain || gs.indexOf(grain) < 0) grain = defaultGrain(period.days);
      $('[data-main-title]').textContent = M.label + ' theo ' + GRAIN_LABEL[grain].toLowerCase();
      $('[data-grain]').innerHTML = gs.length > 1 ? gs.map(function (g) { return '<button type="button" data-g="' + g + '" aria-pressed="' + (g === grain) + '">' + GRAIN_LABEL[g] + '</button>'; }).join('') : '';
      $$('[data-g]').forEach(function (b) { b.addEventListener('click', function () { grain = b.dataset.g; drawMain(); }); });
      var cur = series(period.from, period.to, grain, metric);
      var cmp = period.cmpFrom != null ? series(period.cmpFrom, period.cmpTo, grain, metric) : null;
      var lines = [{ name: period.range, values: cur.map(function (x) { return x.v; }), color: C1 }];
      if (cmp) lines.push({ name: 'Kỳ so sánh ' + period.cmpRange, values: cur.map(function (x, i) { return cmp[i] ? cmp[i].v : 0; }), color: CPREV, muted: true });
      lineChart($('[data-chart="main"]'), cur.map(function (x) { return x.label; }), lines, M.fmt, M.axis);
      $('[data-main-legend]').innerHTML = '<span><i style="background:' + C1 + '"></i>' + esc(period.label) + '</span>' + (cmp ? '<span><i style="background:' + CPREV + '"></i>' + (period.cmp === 'year' ? 'Năm trước' : 'Kỳ trước') + '</span>' : '');
      tableView($('[data-chart-table="main"]'), [GRAIN_LABEL[grain], M.label].concat(cmp ? ['Kỳ so sánh'] : []), cur.map(function (x, i) { return [x.label, M.fmt(x.v)].concat(cmp ? [cmp[i] ? M.fmt(cmp[i].v) : ''] : []); }));
      $$('[data-kpi]').forEach(function (b) { b.setAttribute('aria-pressed', b.dataset.kpi === metric ? 'true' : 'false'); });
    }
    periodControl(function (p) {
      period = p; grain = null;
      var cur = measure(p.from, p.to), prev = p.cmpFrom != null ? measure(p.cmpFrom, p.cmpTo) : null;
      if (prev) prev.label = p.cmp === 'year' ? 'so với năm trước' : 'so với kỳ trước';
      var sg = p.days === 1 ? 'hour' : p.days <= 90 ? 'day' : 'week', sparks = {};
      Object.keys(METRICS).forEach(function (key) { sparks[key] = series(p.from, p.to, sg, key).map(function (x) { return x.v; }); });
      kpiTiles($('[data-kpis]'), cur, prev, sparks, metric, function (key) { metric = key; X.set('adm:metric', key); drawMain(); });
      drawMain();
      $('[data-funnel]').innerHTML = funnelHtml(cur);
      var tot = cur.revNew + cur.revRet;
      $('[data-newret]').innerHTML = cur.buyers ? '<div class="split"><span style="width:' + (tot ? cur.revNew * 100 / tot : 50) + '%"></span><span style="width:' + (tot ? cur.revRet * 100 / tot : 50) + '%"></span></div>' +
        '<dl class="split-legend"><div><dt><i class="sw-new"></i>Khách mới</dt><dd><b>' + num(cur.buyers - cur.retc) + '</b> khách, ' + compact(cur.revNew) + ' đ (' + (tot ? Math.round(cur.revNew * 100 / tot) : 0) + '%)</dd></div>' +
        '<div><dt><i class="sw-ret"></i>Khách quay lại</dt><dd><b>' + num(cur.retc) + '</b> khách, ' + compact(cur.revRet) + ' đ (' + (tot ? Math.round(cur.revRet * 100 / tot) : 0) + '%)</dd></div></dl>' +
        '<p class="muted small">Khách quay lại là khách đã mua trước kỳ này hoặc mua từ 2 đơn trong kỳ.</p>' : '<p class="muted">Chưa có khách mua trong khoảng này.</p>';
      var money = function (v) { return compact(v) + ' đ'; };
      barList($('[data-chart="channel"]'), groupSum(cur.ok, function (o) { return o.channel; }, net), money, { share: true });
      barList($('[data-chart="pay"]'), groupSum(cur.ok, function (o) { return o.payment.name; }, function () { return 1; }), function (v) { return num(v) + ' đơn'; }, { share: true });
      barList($('[data-chart="cat"]'), groupSum([].concat.apply([], cur.ok.map(function (o) { return o.items; })), function (it) { return (X.byId[it.id] || {}).category_name; }, function (it) { return it.price * it.qty; }), money, { share: true });
      barList($('[data-chart="status"]'), (S.statuses || []).map(function (s) { return { label: s[1], value: cur.all.filter(function (o) { return o.status === s[0]; }).length }; }).filter(function (r) { return r.value; }), num);
      var shipped = cur.all.filter(function (o) { return o.status === 'delivered' || o.status === 'returned'; }).length;
      var codOpen = cur.all.filter(function (o) { return o.status === 'delivered' && !o.paid; });
      $('[data-ops]').innerHTML = '<div><dt>Tỷ lệ hủy</dt><dd>' + (cur.all.length ? (cur.cancelled * 100 / cur.all.length).toFixed(1).replace('.', ',') : '0') + '%</dd></div>' +
        '<div><dt>Tỷ lệ trả hàng</dt><dd>' + (shipped ? (cur.returned * 100 / shipped).toFixed(1).replace('.', ',') : '0') + '%</dd></div>' +
        '<div><dt>COD đã giao chưa đối soát</dt><dd>' + num(codOpen.length) + ' đơn, ' + compact(codOpen.reduce(function (a, o) { return a + o.total; }, 0)) + ' đ</dd></div>';
      var outside = heatmap($('[data-heat]'), cur.ok);
      $('[data-heat-note]').textContent = num(cur.ok.length) + ' đơn' + (outside ? ', ' + outside + ' đơn trước 7h không hiện' : '');
      var top = productReport(p.from, p.to).slice(0, 8);
      $('[data-top]').innerHTML = '<thead><tr><th>Sản phẩm</th><th>Danh mục</th><th class="num">Đã bán</th><th class="num">Doanh thu</th><th class="num">Tỷ trọng</th></tr></thead><tbody>' + (top.map(function (r) {
        return '<tr><td><a class="cell-prod" href="product.html?id=' + r.id + '"><img src="' + R + r.image + '" alt="">' + esc(r.name) + '</a></td><td>' + esc(r.cat) + '</td><td class="num">' + num(r.qty) + '</td><td class="num">' + vnd(r.rev) + '</td><td class="num">' + (cur.revenue ? (r.rev * 100 / cur.ok.reduce(function (a, o) { return a + o.subtotal; }, 0)).toFixed(1).replace('.', ',') : '0') + '%</td></tr>';
      }).join('') || '<tr><td colspan="5" class="muted">Chưa có đơn trong khoảng này.</td></tr>') + '</tbody>';
      var custs = {};
      cur.ok.forEach(function (o) { var c = custs[o.ckey] = custs[o.ckey] || { name: o.customer.name, id: o.customer.id, phone: o.customer.phone, n: 0, spent: 0 }; c.n++; c.spent += o.total; });
      leaders($('[data-top-customers]'), Object.keys(custs).map(function (k) { return custs[k]; }).sort(function (a, b) { return b.spent - a.spent; }).slice(0, 5).map(function (c) {
        return { name: c.name, sub: c.n + ' đơn', value: compact(c.spent) + ' đ', href: c.id ? 'customer.html?id=' + encodeURIComponent(c.id) : 'customer.html?phone=' + encodeURIComponent(c.phone) };
      }), 'Chưa có khách mua trong khoảng này.');
      var vouch = {};
      cur.ok.forEach(function (o) { if (!o.voucher) return; var v = vouch[o.voucher] = vouch[o.voucher] || { n: 0, d: 0 }; v.n++; v.d += o.discount; });
      leaders($('[data-top-vouchers]'), Object.keys(vouch).sort(function (a, b) { return vouch[b].n - vouch[a].n; }).slice(0, 5).map(function (code) {
        return { name: code, sub: 'Đã giảm ' + compact(vouch[code].d) + ' đ', value: num(vouch[code].n) + ' lượt' };
      }), 'Chưa có đơn dùng mã trong khoảng này.');
    });
    var orders = allOrders();
    var pend = orders.filter(function (o) { return o.status === 'pending'; }), pack = orders.filter(function (o) { return o.status === 'confirmed' || o.status === 'packing'; });
    var unpaid = orders.filter(function (o) { return o.status === 'delivered' && !o.paid; }), low = lowStock(), revs = X.get('reviews', []).filter(function (r) { return r.status === 'pending'; });
    var todo = [
      [pend.length, 'đơn chờ xác nhận', 'orders.html?status=pending'],
      [pack.length, 'đơn cần đóng gói và giao', 'orders.html?status=confirmed'],
      [low.filter(function (x) { return x.stock === 0; }).length, 'phiên bản hết hàng', 'inventory.html?tab=out'],
      [low.filter(function (x) { return x.stock > 0; }).length, 'phiên bản sắp hết', 'inventory.html?tab=low'],
      [unpaid.length, 'đơn đã giao chưa đối soát tiền', 'orders.html?paid=0'],
      [revs.length + X.get('questions', []).length, 'đánh giá, câu hỏi chờ trả lời', 'products.html']
    ];
    $('[data-todo]').innerHTML = todo.map(function (t) { return '<li class="' + (t[0] ? '' : 'done') + '"><a href="' + t[2] + '"><b>' + num(t[0]) + '</b><span>' + t[1] + '</span>' + X.icon('chev_r', 'i-xs') + '</a></li>'; }).join('');
    leaders($('[data-low-stock]'), lowStock().sort(function (a, b) { return a.stock - b.stock; }).slice(0, 6).map(function (x) {
      return { name: x.name, sub: x.label, value: x.stock === 0 ? '<span class="badge badge-bad">Hết hàng</span>' : '<span class="badge badge-warn">Còn ' + x.stock + '</span>', href: 'product.html?id=' + x.p.id };
    }), 'Không có phiên bản nào sắp hết.');
    $('[data-recent-orders]').innerHTML = orders.slice(0, 6).map(function (o) {
      return '<li><a href="order.html?code=' + o.code + '"><span><b>' + esc(o.code) + '</b><small>' + esc(o.customer.name) + ', ' + X.dateText(o.created) + '</small></span><span class="mo-r"><b>' + vnd(o.total) + '</b>' + badge(o.status) + '</span></a></li>';
    }).join('');
  }

  /* ---------------------------------------------------------------- ui helpers: confirm, toast with undo, keys */
  var ui = {
    confirm: function (o) {
      return new Promise(function (resolve) {
        var d = document.createElement('dialog');
        d.className = 'modal confirm';
        d.innerHTML = '<h2>' + esc(o.title) + '</h2>' + (o.text ? '<p>' + o.text + '</p>' : '') +
          '<div class="form-row form-row-end"><button class="btn btn-ghost" type="button" value="0">' + esc(o.cancel || 'Quay lại') + '</button>' +
          '<button class="btn ' + (o.danger ? 'btn-danger-solid' : 'btn-primary') + '" type="button" value="1">' + esc(o.ok || 'Đồng ý') + '</button></div>';
        document.body.appendChild(d);
        function done(v) { d.close(); d.remove(); resolve(v); }
        $$('button', d).forEach(function (b) { b.addEventListener('click', function () { done(b.value === '1'); }); });
        d.addEventListener('cancel', function (e) { e.preventDefault(); done(false); });
        d.showModal(); $('button[value="1"]', d).focus();
      });
    },
    toast: function (html, undo) {
      var box = $('[data-toasts]'); if (!box) return;
      var t = document.createElement('div');
      t.className = 'toast'; t.setAttribute('role', 'status');
      t.innerHTML = X.icon('check') + '<span>' + html + '</span>' + (undo ? '<button type="button" class="toast-undo">Hoàn tác</button>' : '');
      box.appendChild(t);
      var timer = setTimeout(function () { t.remove(); }, undo ? 7000 : 3500);
      if (undo) $('.toast-undo', t).addEventListener('click', function () { clearTimeout(timer); t.remove(); undo(); });
    }
  };
  function applyStatus(o, st, note) {
    var snap;
    changeOrder(o, function (x) {
      snap = { status: x.status, paid: x.paid, n: (x.history || []).length };
      x.status = st; x.history = x.history || [];
      x.history.push({ status: st, at: new Date().toISOString(), note: note || '', by: staff.name });
      if (st === 'delivered' && o.payment.id === 'cod') x.paid = true;
    });
    return function () { changeOrder(o, function (x) { x.status = snap.status; x.paid = snap.paid; x.history = (x.history || []).slice(0, snap.n); }); };
  }
  function typing(e) { var t = e.target; return t && (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName)); }
  var SERIAL_CATS = ['dien-thoai', 'laptop', 'may-tinh-bang'];
  function needsSerial(o) { return o.items.filter(function (it) { return SERIAL_CATS.indexOf((X.byId[it.id] || {}).category) >= 0; }); }
  // One place decides what a status change needs: serials before shipping, a confirm before cancel or return.
  function runAction(o, st, after) {
    var missing = st === 'shipping' ? needsSerial(o).filter(function (it) { return !((o.serials || {})[it.sku]); }) : [];
    if (missing.length) {
      ui.toast('Nhập IMEI hoặc số serial cho ' + missing.length + ' máy trước khi giao. <a href="order.html?code=' + o.code + '#serials">Mở đơn</a>');
      return;
    }
    var ask = st === 'cancelled' ? { title: 'Hủy đơn ' + o.code + '?', text: 'Khách ' + esc(o.customer.name) + ', ' + vnd(o.total) + '. Hàng được trả về tồn kho.', ok: 'Hủy đơn', danger: true }
      : st === 'returned' ? { title: 'Nhận hàng trả lại đơn ' + o.code + '?', text: 'Đơn chuyển sang Trả hàng và không tính vào doanh thu.', ok: 'Xác nhận trả hàng', danger: true } : null;
    (ask ? ui.confirm(ask) : Promise.resolve(true)).then(function (ok) {
      if (!ok) return;
      var undo = applyStatus(o, st, st === 'cancelled' ? 'Cửa hàng hủy đơn' : '');
      ui.toast('Đơn ' + esc(o.code) + ' chuyển sang <b>' + esc(X.STATUS[st]) + '</b>', function () { undo(); after(); ui.toast('Đã hoàn tác đơn ' + esc(o.code)); });
      after();
    });
  }
  function actionButtons(o, compact) {
    var h = '';
    if (NEXT[o.status]) h += '<button class="btn btn-primary' + (compact ? ' btn-sm' : '') + '" type="button" data-act="' + NEXT[o.status][0] + '">' + NEXT[o.status][1] + '</button>';
    if (o.status === 'delivered') h += '<button class="btn btn-outline' + (compact ? ' btn-sm' : '') + '" type="button" data-act="returned">Nhận hàng trả lại</button>';
    if (['pending', 'confirmed', 'packing'].indexOf(o.status) >= 0) h += '<button class="btn btn-outline btn-danger' + (compact ? ' btn-sm' : '') + '" type="button" data-act="cancelled">Hủy đơn</button>';
    return h;
  }
  function stepsHtml(o) {
    if (o.status === 'cancelled' || o.status === 'returned') return '<ol class="steps-mini is-stop"><li class="on">' + esc(X.STATUS[o.status]) + '</li></ol>';
    var i = FLOW.indexOf(o.status);
    return '<ol class="steps-mini">' + FLOW.map(function (s, k) { return '<li class="' + (k < i ? 'done' : k === i ? 'on' : '') + '">' + esc(X.STATUS[s]) + '</li>'; }).join('') + '</ol>';
  }

  /* ---------------------------------------------------------------- orders list with quick view */
  function pageOrders() {
    var orders = allOrders(), tab = params.get('status') || '', pageN = 1, PER = 20, chosen = {}, cur = -1, rows = [], openCode = null;
    var sort = { key: 'created', dir: -1 };
    var q = $('[data-q]'); q.value = params.get('q') || '';
    if (params.get('paid') != null) $('[data-f-paid]').value = params.get('paid');
    var tabs = [['', 'Tất cả']].concat(S.statuses);
    var drawer = document.createElement('aside');
    drawer.className = 'qv'; drawer.hidden = true; drawer.setAttribute('aria-label', 'Xem nhanh đơn hàng');
    document.body.appendChild(drawer);
    function filtered() {
      var qq = X.norm(q.value.trim()), pay = $('[data-f-pay]').value, paid = $('[data-f-paid]').value, ch = $('[data-f-channel]').value, days = +$('[data-f-days]').value;
      return orders.filter(function (o) {
        if (qq && X.norm(o.code + ' ' + o.customer.name + ' ' + o.customer.phone).indexOf(qq) < 0) return false;
        if (pay && o.payment.id !== pay) return false;
        if (paid !== '' && !!o.paid !== (paid === '1')) return false;
        if (ch && o.channel !== ch) return false;
        if (days && !inRange(o, -(days - 1), 0)) return false;
        return true;
      });
    }
    function sorted(list) {
      var k = sort.key, d = sort.dir;
      return list.slice().sort(function (a, b) {
        var x = k === 'total' ? a.total : k === 'customer' ? X.norm(a.customer.name) : a.created, y = k === 'total' ? b.total : k === 'customer' ? X.norm(b.customer.name) : b.created;
        return x < y ? -d : x > y ? d : 0;
      });
    }
    function th(key, label, cls) {
      var on = sort.key === key;
      return '<th class="' + (cls || '') + '" aria-sort="' + (on ? (sort.dir > 0 ? 'ascending' : 'descending') : 'none') + '"><button type="button" class="th-sort" data-sort="' + key + '">' + label + X.icon(on && sort.dir > 0 ? 'up' : 'down', 'i-xs') + '</button></th>';
    }
    function draw() {
      var base = filtered();
      $('[data-status-tabs]').innerHTML = tabs.map(function (t) {
        var n = base.filter(function (o) { return !t[0] || o.status === t[0]; }).length;
        return '<button type="button" role="tab" data-st="' + t[0] + '" aria-selected="' + (t[0] === tab) + '">' + esc(t[1]) + ' <small>' + num(n) + '</small></button>';
      }).join('');
      var list = sorted(base.filter(function (o) { return !tab || o.status === tab; }));
      var pages = Math.max(1, Math.ceil(list.length / PER)); pageN = Math.min(pageN, pages);
      rows = list.slice((pageN - 1) * PER, pageN * PER);
      $('[data-orders]').innerHTML = '<thead><tr><th class="ck"><input type="checkbox" data-all aria-label="Chọn tất cả"></th><th>Mã đơn</th>' + th('created', 'Ngày đặt') + th('customer', 'Khách hàng') + '<th>Kênh</th><th>Thanh toán</th><th>Trạng thái</th>' + th('total', 'Tổng tiền', 'num') + '</tr></thead><tbody>' +
        rows.map(function (o, i) {
          return '<tr data-row="' + i + '" class="' + (o.code === openCode ? 'is-open' : '') + '"><td class="ck"><input type="checkbox" data-pick="' + o.code + '" ' + (chosen[o.code] ? 'checked' : '') + ' aria-label="Chọn ' + o.code + '"></td><td><a href="order.html?code=' + o.code + '" data-open="' + o.code + '"><b>' + esc(o.code) + '</b></a>' + (o.source === 'web' ? ' <span class="badge badge-info">Mới từ web</span>' : '') + '</td><td>' + X.dateText(o.created) + '</td><td>' + esc(o.customer.name) + '<small class="cell-sub">' + esc(o.customer.phone) + '</small></td><td>' + esc(o.channel) + '</td><td>' + payBadge(o) + '<small class="cell-sub">' + esc(o.payment.name) + '</small></td><td>' + badge(o.status) + '</td><td class="num"><b>' + vnd(o.total) + '</b><small class="cell-sub">' + o.items.reduce(function (a, it) { return a + it.qty; }, 0) + ' sản phẩm</small></td></tr>';
        }).join('') + '</tbody>';
      $('[data-orders-empty]').hidden = list.length > 0;
      $('[data-pager]').innerHTML = list.length > PER ? '<span>' + ((pageN - 1) * PER + 1) + '-' + Math.min(list.length, pageN * PER) + ' trên ' + num(list.length) + ' đơn</span><button class="btn btn-sm" type="button" data-pg="-1" ' + (pageN <= 1 ? 'disabled' : '') + ' aria-label="Trang trước">' + X.icon('chev_l', 'i-sm') + '</button><button class="btn btn-sm" type="button" data-pg="1" ' + (pageN >= pages ? 'disabled' : '') + ' aria-label="Trang sau">' + X.icon('chev_r', 'i-sm') + '</button>' : '<span>' + num(list.length) + ' đơn</span>';
      mark();
      bulkBar();
    }
    function mark() { $$('[data-row]').forEach(function (tr) { tr.classList.toggle('is-cur', +tr.dataset.row === cur); }); }
    function bulkBar() { var n = Object.keys(chosen).length; $('[data-bulk]').hidden = !n; $('[data-bulk-n]').textContent = 'Đã chọn ' + n + ' đơn'; }
    function refresh() { orders = allOrders(); draw(); if (openCode) openOrder(openCode); }
    function openOrder(code) {
      var o = orders.filter(function (x) { return x.code === code; })[0]; if (!o) return;
      openCode = code; $$('[data-row]').forEach(function (tr) { tr.classList.toggle('is-open', rows[+tr.dataset.row] && rows[+tr.dataset.row].code === code); });
      var a = o.address || {}, sn = needsSerial(o);
      drawer.innerHTML = '<div class="qv-head"><div><h2>' + esc(o.code) + '</h2><p>' + badge(o.status) + ' ' + payBadge(o) + '</p></div>' +
        '<div class="qv-nav"><button class="icon-btn" type="button" data-qv-move="-1" aria-label="Đơn trước (k)">' + X.icon('up', 'i-sm') + '</button><button class="icon-btn" type="button" data-qv-move="1" aria-label="Đơn sau (j)">' + X.icon('down', 'i-sm') + '</button>' +
        '<button class="icon-btn" type="button" data-qv-close aria-label="Đóng (Esc)">' + X.icon('x', 'i-sm') + '</button></div></div>' +
        '<div class="qv-body">' + stepsHtml(o) +
        '<div class="qv-actions">' + actionButtons(o, true) + '<a class="btn btn-outline btn-sm" href="order.html?code=' + o.code + '">Mở chi tiết</a></div>' +
        (o.status === 'packing' && sn.length ? '<p class="qv-warn">' + X.icon('alert', 'i-sm') + 'Cần nhập IMEI hoặc serial cho ' + sn.length + ' máy trong trang chi tiết trước khi giao.</p>' : '') +
        '<h3>Sản phẩm</h3><ul class="qv-items">' + o.items.map(function (it) { return '<li><img src="' + R + it.image + '" alt=""><span>' + esc(it.name) + '<small>' + esc(it.variant) + ' x ' + it.qty + '</small></span><b>' + vnd(it.price * it.qty) + '</b></li>'; }).join('') + '</ul>' +
        '<dl class="sum"><dt>Tạm tính</dt><dd>' + vnd(o.subtotal) + '</dd>' + (o.discount ? '<dt>Giảm giá' + (o.voucher ? ' (' + esc(o.voucher) + ')' : '') + '</dt><dd>-' + vnd(o.discount) + '</dd>' : '') + '<dt>Phí giao hàng</dt><dd>' + vnd(o.shipping_fee) + '</dd><dt class="grand">Khách phải trả</dt><dd class="grand">' + vnd(o.total) + '</dd></dl>' +
        '<h3>Khách hàng</h3><dl class="kv"><dt>Tên</dt><dd>' + esc(o.customer.name) + '</dd><dt>Điện thoại</dt><dd>' + esc(o.customer.phone) + '</dd><dt>Nhận hàng</dt><dd>' + (o.receive === 'pickup' && o.store ? esc(o.store.name) : esc([a.street, a.ward, a.province].filter(Boolean).join(', '))) + '</dd><dt>Thanh toán</dt><dd>' + esc(o.payment.name) + '</dd><dt>Kênh</dt><dd>' + esc(o.channel) + ', ' + X.dateText(o.created) + '</dd></dl>' +
        '</div>';
      drawer.hidden = false; document.body.classList.add('qv-on');
    }
    function closeDrawer() { drawer.hidden = true; openCode = null; document.body.classList.remove('qv-on'); $$('[data-row]').forEach(function (tr) { tr.classList.remove('is-open'); }); }
    function move(d) {
      if (!rows.length) return;
      cur = Math.max(0, Math.min(rows.length - 1, (cur < 0 ? (d > 0 ? -1 : rows.length) : cur) + d)); mark();
      var tr = $('[data-row="' + cur + '"]'); if (tr) tr.scrollIntoView({ block: 'nearest' });
      if (openCode) openOrder(rows[cur].code);
    }
    drawer.addEventListener('click', function (e) {
      var o = orders.filter(function (x) { return x.code === openCode; })[0];
      if (e.target.closest('[data-qv-close]')) { closeDrawer(); return; }
      var mv = e.target.closest('[data-qv-move]'); if (mv) { cur = rows.map(function (r) { return r.code; }).indexOf(openCode); move(+mv.dataset.qvMove); return; }
      var act = e.target.closest('[data-act]'); if (act && o) runAction(o, act.dataset.act, refresh);
    });
    document.addEventListener('click', function (e) {
      var st = e.target.closest('[data-st]'); if (st) { tab = st.dataset.st; pageN = 1; cur = -1; draw(); return; }
      var pg = e.target.closest('[data-pg]'); if (pg) { pageN += +pg.dataset.pg; cur = -1; draw(); return; }
      var so = e.target.closest('[data-sort]'); if (so) { sort = { key: so.dataset.sort, dir: sort.key === so.dataset.sort ? -sort.dir : (so.dataset.sort === 'customer' ? 1 : -1) }; draw(); return; }
      var ba = e.target.closest('[data-bulk-act]');
      if (ba) {
        var st2 = ba.dataset.bulkAct, picked = Object.keys(chosen).map(function (code) { return orders.filter(function (x) { return x.code === code; })[0]; }).filter(function (o) { return o && live(o) && o.status !== 'delivered' && o.status !== st2; });
        if (!picked.length) { ui.toast('Không có đơn nào trong các đơn đã chọn chuyển được sang "' + esc(X.STATUS[st2]) + '".'); return; }
        ui.confirm({ title: 'Chuyển ' + picked.length + ' đơn sang "' + X.STATUS[st2] + '"?', text: picked.slice(0, 5).map(function (o) { return esc(o.code); }).join(', ') + (picked.length > 5 ? ' và ' + (picked.length - 5) + ' đơn khác' : '') + '.', ok: 'Cập nhật ' + picked.length + ' đơn' }).then(function (ok) {
          if (!ok) return;
          var undos = picked.map(function (o) { return applyStatus(o, st2, 'Cập nhật hàng loạt'); });
          chosen = {}; refresh();
          ui.toast('Đã cập nhật ' + undos.length + ' đơn sang <b>' + esc(X.STATUS[st2]) + '</b>', function () { undos.forEach(function (u) { u(); }); refresh(); ui.toast('Đã hoàn tác ' + undos.length + ' đơn'); });
        });
        return;
      }
      if (e.target.closest('[data-bulk-print]')) { window.print(); return; }
      var op = e.target.closest('[data-open]');
      if (op && !(e.ctrlKey || e.metaKey || e.shiftKey)) { e.preventDefault(); cur = +op.closest('tr').dataset.row; mark(); openOrder(op.dataset.open); return; }
      var tr = e.target.closest('tr[data-row]');
      if (tr && !e.target.closest('input,a,button')) { cur = +tr.dataset.row; mark(); openOrder(rows[cur].code); }
    });
    document.addEventListener('keydown', function (e) {
      if (typing(e) || e.ctrlKey || e.metaKey || e.altKey || $('dialog[open]')) return;
      if (e.key === 'j') { e.preventDefault(); move(1); }
      else if (e.key === 'k') { e.preventDefault(); move(-1); }
      else if (e.key === 'Enter' && cur >= 0) { e.preventDefault(); openOrder(rows[cur].code); }
      else if (e.key === 'Escape' && openCode) { closeDrawer(); }
      else if (e.key === 'x' && cur >= 0) { var c = $('[data-row="' + cur + '"] [data-pick]'); if (c) { c.checked = !c.checked; c.dispatchEvent(new Event('change', { bubbles: true })); } }
      else if (e.key === '/') { e.preventDefault(); q.focus(); }
    });
    document.addEventListener('change', function (e) {
      if (e.target.matches('[data-pick]')) { if (e.target.checked) chosen[e.target.dataset.pick] = 1; else delete chosen[e.target.dataset.pick]; bulkBar(); }
      if (e.target.matches('[data-all]')) { $$('[data-pick]').forEach(function (c) { c.checked = e.target.checked; if (c.checked) chosen[c.dataset.pick] = 1; else delete chosen[c.dataset.pick]; }); bulkBar(); }
      if (e.target.matches('[data-f-pay],[data-f-paid],[data-f-channel],[data-f-days]')) { pageN = 1; cur = -1; draw(); }
    });
    q.addEventListener('input', function () { pageN = 1; cur = -1; draw(); });
    draw();
  }

  /* ---------------------------------------------------------------- order detail */
  function pageOrder() {
    var code = params.get('code');
    function find() { return allOrders().filter(function (x) { return x.code === code; })[0]; }
    var o = find();
    if (!o) { $('[data-order]').hidden = true; $('[data-order-missing]').hidden = false; return; }
    var list = allOrders().map(function (x) { return x.code; }), at = list.indexOf(code);
    function go(d) { var c = list[at + d]; if (c) location.href = 'order.html?code=' + c; }
    function render() {
      o = find();
      var sn = needsSerial(o), hasSerial = sn.length > 0;
      $('[data-o-code]').textContent = 'Đơn ' + o.code;
      $('[data-o-badges]').innerHTML = badge(o.status) + ' ' + payBadge(o);
      $('[data-o-sub]').textContent = 'Đặt lúc ' + X.dateText(o.created) + ' qua ' + o.channel + (o.source === 'web' ? ', đơn đặt thử trên website này' : '');
      $('[data-o-actions]').innerHTML = actionButtons(o) + '<button class="btn btn-outline" type="button" data-print>' + X.icon('clipboard', 'i-sm') + 'In phiếu giao</button>' +
        '<span class="pn"><button class="icon-btn" type="button" data-go="-1" aria-label="Đơn mới hơn ([)" ' + (at <= 0 ? 'disabled' : '') + '>' + X.icon('chev_l', 'i-sm') + '</button><button class="icon-btn" type="button" data-go="1" aria-label="Đơn cũ hơn (])" ' + (at >= list.length - 1 ? 'disabled' : '') + '>' + X.icon('chev_r', 'i-sm') + '</button></span>';
      $('[data-o-steps]').innerHTML = stepsHtml(o);
      $('[data-o-items]').innerHTML = '<thead><tr><th>Sản phẩm</th><th class="num">Đơn giá</th><th class="num">SL</th><th class="num">Thành tiền</th>' + (hasSerial ? '<th>IMEI, số serial</th>' : '') + '</tr></thead><tbody>' + o.items.map(function (it) {
        var needs = SERIAL_CATS.indexOf((X.byId[it.id] || {}).category) >= 0;
        return '<tr><td><a class="cell-prod" href="product.html?id=' + it.id + '"><img src="' + R + it.image + '" alt=""><span>' + esc(it.name) + '<small class="cell-sub">' + esc(it.variant) + ', ' + esc(it.sku) + '</small></span></a></td><td class="num">' + vnd(it.price) + '</td><td class="num">' + it.qty + '</td><td class="num">' + vnd(it.price * it.qty) + '</td>' +
          (hasSerial ? '<td>' + (needs ? '<input class="input-sm" data-serial="' + it.sku + '" value="' + esc((o.serials || {})[it.sku] || '') + '" placeholder="Nhập khi xuất kho" aria-label="IMEI ' + esc(it.name) + '">' : '<span class="muted">Không cần</span>') + '</td>' : '') + '</tr>';
      }).join('') + '</tbody>';
      $('[data-o-sum]').innerHTML = '<dt>Tạm tính</dt><dd>' + vnd(o.subtotal) + '</dd><dt>Giảm giá' + (o.voucher ? ' (' + esc(o.voucher) + ')' : '') + '</dt><dd>-' + vnd(o.discount) + '</dd><dt>Phí giao hàng</dt><dd>' + vnd(o.shipping_fee) + '</dd><dt class="grand">Khách phải trả</dt><dd class="grand">' + vnd(o.total) + '</dd>';
      $('[data-o-paid]').innerHTML = payBadge(o);
      $('[data-o-pay]').innerHTML = '<dl class="kv"><dt>Phương thức</dt><dd>' + esc(o.payment.name) + '</dd><dt>Số tiền</dt><dd>' + vnd(o.total) + '</dd></dl>' + (!o.paid && live(o) ? '<button class="btn btn-outline btn-sm" type="button" data-mark-paid>' + X.icon('check', 'i-sm') + 'Đánh dấu đã thu tiền</button>' : '');
      var c = o.customer, n = allOrders().filter(function (x) { return x.customer.phone === c.phone && c.phone; }).length;
      $('[data-o-customer]').innerHTML = '<dl class="kv"><dt>Tên</dt><dd>' + (c.id ? '<a href="customer.html?id=' + encodeURIComponent(c.id) + '">' + esc(c.name) + '</a>' : '<a href="customer.html?phone=' + encodeURIComponent(c.phone) + '">' + esc((c.title ? c.title + ' ' : '') + c.name) + '</a>') + '</dd><dt>Điện thoại</dt><dd><a href="tel:' + esc(c.phone) + '">' + esc(c.phone) + '</a></dd>' + (c.email ? '<dt>Email</dt><dd>' + esc(c.email) + '</dd>' : '') + '<dt>Số đơn</dt><dd>' + n + '</dd></dl>';
      var a = o.address || {}, addr = o.receive === 'pickup' && o.store ? o.store.name + ', ' + o.store.address : [a.street, a.ward, a.province].filter(Boolean).join(', ');
      $('[data-o-ship]').innerHTML = '<dl class="kv"><dt>' + (o.receive === 'pickup' ? 'Nhận tại' : 'Địa chỉ') + '</dt><dd>' + esc(addr) + '</dd>' + (o.receive !== 'pickup' ? '<dt>Vận chuyển</dt><dd>' + esc((o.shipping || {}).name || '') + '</dd>' : '') + (o.note ? '<dt>Ghi chú</dt><dd>' + esc(o.note) + '</dd>' : '') + '</dl>';
      if (o.invoice) { $('[data-o-invoice-box]').hidden = false; $('[data-o-invoice]').innerHTML = '<dl class="kv"><dt>Công ty</dt><dd>' + esc(o.invoice.company) + '</dd><dt>Mã số thuế</dt><dd>' + esc(o.invoice.tax_code) + '</dd><dt>Địa chỉ</dt><dd>' + esc(o.invoice.address) + '</dd></dl>'; }
      var tl = (o.history || []).map(function (h) { return { at: h.at, html: '<b>' + esc(X.STATUS[h.status] || h.status) + '</b>' + (h.note ? ': ' + esc(h.note) : '') + (h.by ? '<small>' + esc(h.by) + '</small>' : '') }; })
        .concat((o.notes || []).map(function (nt) { return { at: nt.at, html: '<b>Ghi chú</b>: ' + esc(nt.text) + '<small>' + esc(nt.by) + '</small>' }; }))
        .sort(function (x, y) { return y.at < x.at ? -1 : 1; });
      $('[data-o-timeline]').innerHTML = tl.map(function (t) { return '<li>' + t.html + '<small>' + X.dateText(t.at) + '</small></li>'; }).join('');
      var slip = $('[data-slip]');
      if (slip) slip.innerHTML = '<div><b>' + esc(S.shop.name) + '</b><span>' + esc(S.shop.hotline) + (S.shop.address ? ', ' + esc(S.shop.address) : '') + '</span></div><div class="slip-code"><span>PHIẾU GIAO HÀNG</span><b>' + esc(o.code) + '</b><span>' + X.dateText(o.created) + '</span></div>' +
        '<div><span>Người nhận</span><b>' + esc(c.name) + ', ' + esc(c.phone) + '</b><span>' + esc(addr) + '</span><span>Thu hộ: <b>' + (o.paid ? '0 đ (đã thanh toán)' : vnd(o.total)) + '</b></span></div>';
      if (location.hash === '#serials') { var first = $('[data-serial]'); if (first && !first.value) first.focus(); }
    }
    document.addEventListener('click', function (e) {
      var act = e.target.closest('[data-act]');
      if (act) {
        if (act.dataset.act === 'shipping') {
          var empty = $$('[data-serial]').filter(function (i) { return !i.value.trim(); });
          if (empty.length) { ui.toast('Nhập IMEI hoặc số serial cho ' + empty.length + ' máy trước khi giao.'); empty[0].focus(); return; }
        }
        runAction(o, act.dataset.act, render); return;
      }
      var g = e.target.closest('[data-go]'); if (g) { go(+g.dataset.go); return; }
      if (e.target.closest('[data-mark-paid]')) {
        var snapPaid = o.paid;
        changeOrder(o, function (x) { x.paid = true; x.history = x.history || []; x.history.push({ status: o.status, at: new Date().toISOString(), note: 'Đã thu ' + vnd(o.total), by: staff.name }); });
        render(); ui.toast('Đã ghi nhận thu ' + vnd(o.total), function () { changeOrder(o, function (x) { x.paid = snapPaid; x.history = (x.history || []).slice(0, -1); }); render(); });
        return;
      }
      if (e.target.closest('[data-print]')) window.print();
    });
    document.addEventListener('change', function (e) {
      if (e.target.matches('[data-serial]')) { changeOrder(o, function (x) { x.serials = x.serials || {}; x.serials[e.target.dataset.serial] = e.target.value.trim(); }); o = find(); ui.toast('Đã lưu IMEI, số serial'); }
    });
    document.addEventListener('keydown', function (e) {
      if (typing(e) || e.ctrlKey || e.metaKey || $('dialog[open]')) return;
      if (e.key === '[') go(-1); else if (e.key === ']') go(1);
    });
    $('[data-note-form]').addEventListener('submit', function (e) {
      e.preventDefault(); var t = e.target.note.value.trim(); if (!t) return;
      changeOrder(o, function (x) { x.notes = x.notes || []; x.notes.push({ text: t, at: new Date().toISOString(), by: staff.name }); });
      e.target.reset(); render(); ui.toast('Đã lưu ghi chú');
    });
    render();
  }

  function productStats(id, days) {
    var r = productReport(days).filter(function (x) { return x.id === id; })[0];
    return r || { qty: 0, rev: 0, orders: 0 };
  }
  /* ---------------------------------------------------------------- add product: writes the shop.json entry */
  function newProductDialog() {
    var d = document.createElement('dialog'); d.className = 'modal modal-wide';
    d.innerHTML = '<form method="dialog" data-np><div class="modal-head"><h2>Thêm sản phẩm</h2><button class="icon-btn" type="button" data-np-close aria-label="Đóng">' + X.icon('x', 'i-sm') + '</button></div>' +
      '<p class="muted small">Trang này chạy không có máy chủ, nên sản phẩm mới được thêm vào file shop.json rồi build lại. Điền thông tin, bấm Tạo, sao chép đoạn JSON và dán vào danh sách products.</p>' +
      '<div class="form-grid form-grid-2">' +
      '<label class="field field-full"><span>Tên sản phẩm</span><input name="name" required placeholder="Ví dụ: Lumen X10 5G"></label>' +
      '<label class="field"><span>Thương hiệu</span><select name="brand">' + S.brands.map(function (b) { return '<option value="' + b.id + '">' + esc(b.name) + '</option>'; }).join('') + '</select></label>' +
      '<label class="field"><span>Danh mục</span><select name="category">' + S.categories.map(function (c) { return '<option value="' + c.id + '">' + esc(c.name) + '</option>'; }).join('') + '</select></label>' +
      '<label class="field"><span>Giá bán (đ)</span><input name="price" type="number" min="1000" step="1000" required></label>' +
      '<label class="field"><span>Giá gốc (đ), để trống nếu không giảm</span><input name="old" type="number" min="0" step="1000"></label>' +
      '<label class="field"><span>Tồn kho</span><input name="stock" type="number" min="0" value="10"></label>' +
      '<label class="field"><span>Bảo hành</span><input name="warranty" placeholder="Ví dụ: 12 tháng"></label>' +
      '<label class="field field-full"><span>Màu, cách nhau bằng dấu phẩy</span><input name="colors" placeholder="Đen, Trắng"></label></div>' +
      '<div class="form-row form-row-end"><button class="btn btn-ghost" type="button" data-np-close>Hủy</button><button class="btn btn-primary" type="submit">Tạo đoạn JSON</button></div>' +
      '<div data-np-out hidden><pre class="json" data-np-json></pre><div class="form-row form-row-end"><button class="btn btn-outline" type="button" data-np-copy>' + X.icon('clipboard', 'i-sm') + 'Sao chép</button></div></div></form>';
    document.body.appendChild(d);
    var f = $('[data-np]', d);
    $$('[data-np-close]', d).forEach(function (b) { b.addEventListener('click', function () { d.close(); }); });
    d.addEventListener('close', function () { d.remove(); });
    f.addEventListener('submit', function (e) {
      e.preventDefault(); if (!f.reportValidity()) return;
      var id = X.norm(f.name.value).replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
      var palette = ['#25272c', '#eceef1', '#2f4f6f', '#b9a7d6', '#3f6b55', '#a33a3a'];
      var colors = f.colors.value.split(',').map(function (c) { return c.trim(); }).filter(Boolean);
      var p = { id: id, name: f.name.value.trim(), brand: f.brand.value, category: f.category.value, price: +f.price.value };
      if (+f.old.value > +f.price.value) p.old_price = +f.old.value;
      if (colors.length) p.options = [{ name: 'Màu sắc', values: colors.map(function (c, i) { return { name: c, hex: palette[i % palette.length] }; }) }];
      p.stock = +f.stock.value;
      if (f.warranty.value.trim()) p.warranty = f.warranty.value.trim();
      p.facets = {}; p.specs = [];
      $('[data-np-json]', d).textContent = JSON.stringify(p, null, 2);
      $('[data-np-out]', d).hidden = false;
    });
    $('[data-np-copy]', d).addEventListener('click', function () {
      var t = $('[data-np-json]', d).textContent, ok = function () { ui.toast('Đã sao chép. Dán vào products trong shop.json, thêm thông số rồi build lại.'); };
      if (navigator.clipboard) navigator.clipboard.writeText(t).then(ok, ok); else ok();
    });
    d.showModal(); f.name.focus();
  }

  function pageProducts() {
    var hidden = X.get('adm:hidden', {}), chosen = {}, q = $('[data-q]');
    function draw() {
      hidden = X.get('adm:hidden', {});
      var qq = X.norm(q.value.trim()), cat = $('[data-f-cat]').value, st = $('[data-f-status]').value;
      var list = S.products.filter(function (p) {
        var stock = p.variants.reduce(function (a, v) { return a + X.variant(v.sku).stock; }, 0);
        p._stock = stock;
        if (qq && X.norm(p.name + ' ' + p.variants.map(function (v) { return v.sku; }).join(' ')).indexOf(qq) < 0) return false;
        if (cat && p.category !== cat) return false;
        if (st === 'on' && hidden[p.id]) return false;
        if (st === 'off' && !hidden[p.id]) return false;
        if (st === 'out' && stock > 0) return false;
        return true;
      });
      $('[data-p-count]').textContent = list.length + ' sản phẩm';
      $('[data-products]').innerHTML = '<thead><tr><th class="ck"><input type="checkbox" data-all aria-label="Chọn tất cả"></th><th>Sản phẩm</th><th>Trạng thái</th><th>Danh mục</th><th class="num">Giá bán</th><th class="num">Tồn kho</th><th class="num">Bán 30 ngày</th></tr></thead><tbody>' + list.map(function (p) {
        var prices = p.variants.map(function (v) { return X.variant(v.sku).price; }), lo = Math.min.apply(null, prices), hi = Math.max.apply(null, prices), s30 = productStats(p.id, 30);
        return '<tr data-href="product.html?id=' + p.id + '"><td class="ck"><input type="checkbox" data-pick="' + p.id + '" aria-label="Chọn ' + esc(p.name) + '"></td><td><a class="cell-prod" href="product.html?id=' + p.id + '"><img src="' + R + p.image + '" alt=""><span>' + esc(p.name) + '<small class="cell-sub">' + esc(p.brand_name) + ', ' + p.variants.length + ' phiên bản</small></span></a></td>' +
          '<td>' + (hidden[p.id] ? '<span class="badge">Đang ẩn</span>' : '<span class="badge badge-ok">Đang bán</span>') + '</td><td>' + esc(p.category_name) + '</td><td class="num">' + (lo === hi ? vnd(lo) : compact(lo) + ' - ' + compact(hi) + ' đ') + '</td>' +
          '<td class="num">' + (p._stock <= 0 ? '<span class="badge badge-bad">Hết hàng</span>' : p._stock <= 5 ? '<span class="badge badge-warn">' + p._stock + '</span>' : num(p._stock)) + '</td><td class="num">' + num(s30.qty) + '</td></tr>';
      }).join('') + '</tbody>';
      $('[data-p-empty]').hidden = list.length > 0;
      bulk();
    }
    function bulk() { var n = Object.keys(chosen).length; $('[data-bulk]').hidden = !n; $('[data-bulk-n]').textContent = 'Đã chọn ' + n + ' sản phẩm'; }
    document.addEventListener('change', function (e) {
      if (e.target.matches('[data-pick]')) { if (e.target.checked) chosen[e.target.dataset.pick] = 1; else delete chosen[e.target.dataset.pick]; bulk(); }
      if (e.target.matches('[data-all]')) { $$('[data-pick]').forEach(function (c) { c.checked = e.target.checked; if (c.checked) chosen[c.dataset.pick] = 1; else delete chosen[c.dataset.pick]; }); bulk(); }
      if (e.target.matches('[data-f-cat],[data-f-status]')) draw();
    });
    document.addEventListener('click', function (e) {
      var b = e.target.closest('[data-bulk-vis]');
      if (b) { var h = X.get('adm:hidden', {}); Object.keys(chosen).forEach(function (id) { if (b.dataset.bulkVis === 'hide') h[id] = 1; else delete h[id]; }); X.set('adm:hidden', h); X.toast(X.icon('check') + (b.dataset.bulkVis === 'hide' ? 'Đã ẩn ' : 'Đã hiện ') + Object.keys(chosen).length + ' sản phẩm trên website'); chosen = {}; draw(); return; }
      if (e.target.closest('[data-new-product]')) { newProductDialog(); return; }
      var tr = e.target.closest('tr[data-href]'); if (tr && !e.target.closest('input,a')) location.href = tr.dataset.href;
    });
    q.addEventListener('input', draw);
    draw();
  }

  function pageProduct() {
    var p = X.byId[params.get('id')], f = $('[data-product-form]');
    if (!p) { location.replace('products.html'); return; }
    var edits = X.get('adm:pedit', {})[p.id] || {};
    $('[data-pf-title]').textContent = p.name;
    $('[data-pf-view]').href = R + 'p/' + p.id + '.html';
    f.name.value = p.name;
    f.desc.value = edits.desc || '';
    f.status.value = X.get('adm:hidden', {})[p.id] ? 'off' : 'on';
    f.seo_title.value = edits.seo_title || (p.name + ' | ' + S.shop.name);
    f.seo_desc.value = edits.seo_desc || (p.name + ' giá ' + vnd(p.price) + ' tại ' + S.shop.name + '.');
    var gallery = []; p.variants.forEach(function (v) { if (gallery.indexOf(v.image) < 0) gallery.push(v.image); });
    $('[data-pf-media]').innerHTML = gallery.map(function (src) { return '<li><img src="' + R + src + '" alt=""></li>'; }).join('') + '<li class="media-add">' + X.icon('plus') + '<span>Ảnh thật của shop thay ảnh minh họa khi build</span></li>';
    $('[data-pf-variants]').innerHTML = '<thead><tr><th>Phiên bản</th><th>SKU</th><th class="num">Giá bán</th><th class="num">Giá gốc</th><th class="num">Tồn kho</th></tr></thead><tbody>' + p.variants.map(function (v) {
      var x = X.variant(v.sku);
      return '<tr><td>' + esc(v.label || 'Mặc định') + '</td><td class="mono">' + esc(v.sku) + '</td><td class="num"><input class="input-sm num" type="number" min="1000" step="1000" data-v-price="' + v.sku + '" value="' + x.price + '" aria-label="Giá bán ' + esc(v.label) + '"></td>' +
        '<td class="num"><input class="input-sm num" type="number" min="0" step="1000" data-v-old="' + v.sku + '" value="' + (x.old || '') + '" aria-label="Giá gốc ' + esc(v.label) + '"></td><td class="num"><input class="input-sm num" type="number" min="0" data-v-stock="' + v.sku + '" value="' + x.stock + '" aria-label="Tồn kho ' + esc(v.label) + '"></td></tr>';
    }).join('') + '</tbody>';
    $('[data-pf-specs]').innerHTML = p.specs.map(function (g) { return '<h3 class="spec-h">' + esc(g.group) + '</h3><dl class="kv">' + g.rows.map(function (r) { return '<dt>' + esc(r[0]) + '</dt><dd>' + esc(r[1]) + '</dd>'; }).join('') + '</dl>'; }).join('') || '<p class="muted">Chưa có thông số.</p>';
    $('[data-pf-meta]').innerHTML = '<dt>Danh mục</dt><dd>' + esc(p.category_name) + '</dd><dt>Thương hiệu</dt><dd>' + esc(p.brand_name) + '</dd><dt>Bảo hành</dt><dd>' + esc(p.warranty || '') + '</dd><dt>Nhãn</dt><dd>' + (p.badges.join(', ') || 'Không') + '</dd>';
    var s30 = productStats(p.id, 30);
    $('[data-pf-perf]').innerHTML = '<dt>Đã bán</dt><dd>' + num(s30.qty) + ' sản phẩm</dd><dt>Doanh thu</dt><dd>' + vnd(s30.rev) + '</dd><dt>Số đơn</dt><dd>' + num(s30.orders) + '</dd>';
    function serp() {
      var box = $('[data-serp]');
      $('.serp-url', box).textContent = (location.host || 'cua-hang.vn') + ' > p > ' + p.id;
      $('.serp-title', box).textContent = f.seo_title.value; $('.serp-desc', box).textContent = f.seo_desc.value;
      $('[data-len="seo_title"]').textContent = f.seo_title.value.length + '/60 ký tự';
      $('[data-len="seo_desc"]').textContent = f.seo_desc.value.length + '/160 ký tự';
    }
    serp();
    var dirty = false;
    f.addEventListener('input', function () { dirty = true; $('[data-savebar]').hidden = false; serp(); });
    f.addEventListener('change', function () { dirty = true; $('[data-savebar]').hidden = false; });
    $('[data-discard]').addEventListener('click', function () { location.reload(); });
    addEventListener('beforeunload', function (e) { if (dirty) { e.preventDefault(); e.returnValue = ''; } });
    f.addEventListener('submit', function (e) {
      e.preventDefault();
      var ov = X.get('adm:products', {}), sold = X.get('sold', {}), bad = false;
      p.variants.forEach(function (v) {
        var pr = +$('[data-v-price="' + v.sku + '"]').value, old = +$('[data-v-old="' + v.sku + '"]').value || null, st = +$('[data-v-stock="' + v.sku + '"]').value;
        if (old && old <= pr) { bad = true; $('[data-v-old="' + v.sku + '"]').setAttribute('aria-invalid', 'true'); }
        ov[v.sku] = { price: pr, old_price: old, stock: st + (sold[v.sku] || 0) };
      });
      if (bad) { X.toast(X.icon('alert') + 'Giá gốc phải cao hơn giá bán, hoặc để trống.'); return; }
      X.set('adm:products', ov);
      var h = X.get('adm:hidden', {}); if (f.status.value === 'off') h[p.id] = 1; else delete h[p.id]; X.set('adm:hidden', h);
      var pe = X.get('adm:pedit', {}); pe[p.id] = { name: f.name.value, desc: f.desc.value, seo_title: f.seo_title.value, seo_desc: f.seo_desc.value }; X.set('adm:pedit', pe);
      dirty = false; $('[data-savebar]').hidden = true;
      X.toast(X.icon('check') + 'Đã lưu. Giá, tồn kho và trạng thái áp dụng ngay trên website ở trình duyệt này.');
    });
  }

  function pageInventory() {
    var tab = params.get('tab') || '', q = $('[data-q]'), dlg = $('[data-adjust]'), cur = null;
    function draw() {
      var all = allVariants(), qq = X.norm(q.value.trim()), cat = $('[data-f-cat]').value;
      $('[data-inv-kpis]').innerHTML = '<li class="kpi"><small>Tổng số máy trong kho</small><b>' + num(all.reduce(function (a, x) { return a + x.stock; }, 0)) + '</b></li><li class="kpi"><small>Giá trị tồn theo giá bán</small><b>' + METRICS.revenue.tile(all.reduce(function (a, x) { return a + x.stock * x.price; }, 0)) + '</b></li><li class="kpi"><small>Phiên bản sắp hết hoặc hết</small><b>' + num(all.filter(function (x) { return x.stock <= 5; }).length) + '</b></li>';
      $$('[data-tab]').forEach(function (b) { b.setAttribute('aria-selected', b.dataset.tab === tab ? 'true' : 'false'); });
      var list = all.filter(function (x) {
        if (tab === 'low' && !(x.stock > 0 && x.stock <= 5)) return false;
        if (tab === 'out' && x.stock !== 0) return false;
        if (cat && x.p.category !== cat) return false;
        if (qq && X.norm(x.name + ' ' + x.sku).indexOf(qq) < 0) return false;
        return true;
      });
      $('[data-inv]').innerHTML = '<thead><tr><th>Sản phẩm</th><th>SKU</th><th>Trạng thái</th><th class="num">Tồn kho</th><th class="num">Đã bán 30 ngày</th><th class="num"><span class="sr">Điều chỉnh</span></th></tr></thead><tbody>' + list.map(function (x) {
        var sold = 0; allOrders().filter(function (o) { return live(o) && inRange(o, -29, 0); }).forEach(function (o) { o.items.forEach(function (it) { if (it.sku === x.sku) sold += it.qty; }); });
        return '<tr><td><span class="cell-prod"><img src="' + R + x.image + '" alt=""><span>' + esc(x.name) + '<small class="cell-sub">' + esc(x.label) + '</small></span></span></td><td class="mono">' + esc(x.sku) + '</td><td>' +
          (x.stock === 0 ? '<span class="badge badge-bad">Hết hàng</span>' : x.stock <= 5 ? '<span class="badge badge-warn">Sắp hết</span>' : '<span class="badge badge-ok">Còn hàng</span>') + '</td><td class="num"><b>' + num(x.stock) + '</b></td><td class="num">' + num(sold) + '</td><td class="num"><button class="btn btn-sm" type="button" data-adj="' + x.sku + '">Điều chỉnh</button></td></tr>';
      }).join('') + '</tbody>';
      var log = X.get('adm:stocklog', []);
      $('[data-inv-log]').innerHTML = log.length ? '<thead><tr><th>Thời gian</th><th>Sản phẩm</th><th>Thay đổi</th><th>Lý do</th><th>Người làm</th></tr></thead><tbody>' + log.slice(0, 30).map(function (l) { return '<tr><td>' + X.dateText(l.at) + '</td><td>' + esc(l.name) + '</td><td class="num">' + esc(l.change) + '</td><td>' + esc(l.reason) + '</td><td>' + esc(l.by) + '</td></tr>'; }).join('') + '</tbody>' : '<tbody><tr><td class="muted">Chưa có điều chỉnh nào.</td></tr></tbody>';
    }
    $$('[data-tab]').forEach(function (b) { b.addEventListener('click', function () { tab = b.dataset.tab; draw(); }); });
    q.addEventListener('input', draw); $('[data-f-cat]').addEventListener('change', draw);
    document.addEventListener('click', function (e) {
      var a = e.target.closest('[data-adj]');
      if (a) { cur = X.variant(a.dataset.adj); $('[data-adj-name]').textContent = cur.name + ', ' + cur.label + ': đang có ' + cur.stock; dlg.showModal(); dlg.querySelector('[name=qty]').focus(); }
      if (e.target.closest('[data-modal-close]')) dlg.close();
    });
    $('[data-adjust-form]').addEventListener('submit', function (e) {
      var f = e.target, qty = +f.qty.value; if (!f.reportValidity()) { e.preventDefault(); return; }
      var next = f.mode.value === 'in' ? cur.stock + qty : f.mode.value === 'out' ? Math.max(0, cur.stock - qty) : qty;
      var ov = X.get('adm:products', {}), sold = X.get('sold', {}), o = ov[cur.sku] || {};
      o.stock = next + (sold[cur.sku] || 0); ov[cur.sku] = o; X.set('adm:products', ov);
      var log = X.get('adm:stocklog', []);
      log.unshift({ at: new Date().toISOString(), name: cur.name + ' (' + cur.label + ')', change: cur.stock + ' thành ' + next, reason: f.reason.value, by: staff.name });
      X.set('adm:stocklog', log); f.reset(); draw();
      X.toast(X.icon('check') + 'Đã cập nhật tồn kho ' + esc(cur.name));
    });
    draw();
  }

  function customers() {
    var m = {};
    seedCustomers.forEach(function (c) { m[c.phone + c.name] = { id: c.id, name: c.name, phone: c.phone, email: c.email, city: c.city, orders: [], spent: 0, last: null, first: null }; });
    allOrders().forEach(function (o) {
      var c = o.customer, key = c.phone + c.name;
      var r = m[key] = m[key] || { id: c.id || 'web-' + c.phone, name: c.name, phone: c.phone, email: c.email, city: (o.address || {}).province || '', orders: [], spent: 0, last: null, first: null };
      r.orders.push(o); if (live(o)) r.spent += o.total;
      if (!r.last || o.created > r.last) r.last = o.created;
      if (!r.first || o.created < r.first) r.first = o.created;
    });
    return Object.keys(m).map(function (k) { return m[k]; }).filter(function (c) { return c.orders.length; }).sort(function (a, b) { return b.spent - a.spent; });
  }
  function segment(c) {
    if (c.spent >= 5e7) return 'vip';
    if (c.orders.length >= 3) return 'loyal';
    if (c.first && dayIndex(c.first) >= -29) return 'new';
    return '';
  }
  function pageCustomers() {
    var list = customers(), tab = '', q = $('[data-q]'), city = $('[data-f-city]'), pageN = 1, PER = 25;
    list.map(function (c) { return c.city; }).filter(function (v, i, a) { return v && a.indexOf(v) === i; }).sort().forEach(function (v) { var o = document.createElement('option'); o.textContent = v; city.appendChild(o); });
    var SEG = { vip: ['VIP', 'badge-info'], loyal: ['Thân thiết', 'badge-ok'], 'new': ['Mới', 'badge-warn'] };
    function draw() {
      var qq = X.norm(q.value.trim());
      var rows = list.filter(function (c) {
        if (qq && X.norm(c.name + ' ' + c.phone + ' ' + c.email).indexOf(qq) < 0) return false;
        if (city.value && c.city !== city.value) return false;
        if (tab === 'idle') return dayIndex(c.last) < -59;
        if (tab && segment(c) !== tab && !(tab === 'loyal' && c.orders.length >= 3) && !(tab === 'vip' && c.spent >= 5e7)) return false;
        return true;
      });
      $('[data-c-count]').textContent = num(rows.length) + ' khách';
      var pages = Math.max(1, Math.ceil(rows.length / PER)); pageN = Math.min(pageN, pages);
      $('[data-customers]').innerHTML = '<thead><tr><th>Khách hàng</th><th>Nhóm</th><th>Tỉnh, thành</th><th class="num">Số đơn</th><th class="num">Tổng chi</th><th>Đơn gần nhất</th></tr></thead><tbody>' + rows.slice((pageN - 1) * PER, pageN * PER).map(function (c) {
        var s = SEG[segment(c)];
        return '<tr data-href="customer.html?id=' + encodeURIComponent(c.id) + '"><td><a href="customer.html?id=' + encodeURIComponent(c.id) + '"><b>' + esc(c.name) + '</b></a><small class="cell-sub">' + esc(c.phone) + '</small></td><td>' + (s ? '<span class="badge ' + s[1] + '">' + s[0] + '</span>' : '') + '</td><td>' + esc(c.city) + '</td><td class="num">' + c.orders.length + '</td><td class="num">' + vnd(c.spent) + '</td><td>' + (c.last ? X.dayText(c.last) : '') + '</td></tr>';
      }).join('') + '</tbody>';
      $('[data-pager]').innerHTML = rows.length > PER ? '<span>Trang ' + pageN + '/' + pages + '</span><button class="btn btn-sm" type="button" data-pg="-1" ' + (pageN <= 1 ? 'disabled' : '') + '>Trước</button><button class="btn btn-sm" type="button" data-pg="1" ' + (pageN >= pages ? 'disabled' : '') + '>Sau</button>' : '';
      $$('[data-tab]').forEach(function (b) { b.setAttribute('aria-selected', b.dataset.tab === tab ? 'true' : 'false'); });
    }
    $$('[data-tab]').forEach(function (b) { b.addEventListener('click', function () { tab = b.dataset.tab; pageN = 1; draw(); }); });
    q.addEventListener('input', function () { pageN = 1; draw(); }); city.addEventListener('change', draw);
    document.addEventListener('click', function (e) {
      var pg = e.target.closest('[data-pg]'); if (pg) { pageN += +pg.dataset.pg; draw(); }
      var tr = e.target.closest('tr[data-href]'); if (tr && !e.target.closest('a')) location.href = tr.dataset.href;
    });
    draw();
  }
  function pageCustomer() {
    var id = params.get('id'), phone = params.get('phone');
    var c = customers().filter(function (x) { return id ? x.id === id : x.phone === phone; })[0];
    if (!c) { $('[data-customer]').hidden = true; $('[data-c-missing]').hidden = false; return; }
    $('[data-c-name]').textContent = c.name;
    $('[data-c-sub]').textContent = (c.city ? c.city + ', ' : '') + 'khách từ ' + X.dayText(c.first);
    var ok = c.orders.filter(live);
    $('[data-c-kpis]').innerHTML = '<li class="kpi"><small>Tổng chi</small><b>' + vnd(c.spent) + '</b></li><li class="kpi"><small>Số đơn</small><b>' + c.orders.length + '</b></li><li class="kpi"><small>Giá trị đơn trung bình</small><b>' + vnd(ok.length ? c.spent / ok.length : 0) + '</b></li><li class="kpi"><small>Đơn gần nhất</small><b>' + X.dayText(c.last) + '</b></li>';
    $('[data-c-orders]').innerHTML = '<thead><tr><th>Mã đơn</th><th>Ngày</th><th>Trạng thái</th><th class="num">Tổng</th></tr></thead><tbody>' + c.orders.map(function (o) { return '<tr data-href="order.html?code=' + o.code + '"><td><a href="order.html?code=' + o.code + '"><b>' + esc(o.code) + '</b></a></td><td>' + X.dateText(o.created) + '</td><td>' + badge(o.status) + '</td><td class="num">' + vnd(o.total) + '</td></tr>'; }).join('') + '</tbody>';
    var bought = {}; ok.forEach(function (o) { o.items.forEach(function (it) { bought[it.id] = (bought[it.id] || 0) + it.qty; }); });
    $('[data-c-bought]').innerHTML = Object.keys(bought).map(function (pid) { var p = X.byId[pid]; return p ? '<li><img src="' + R + p.image + '" alt=""><span>' + esc(p.name) + '<small>' + bought[pid] + ' sản phẩm</small></span></li>' : ''; }).join('') || '<li class="muted">Chưa có.</li>';
    $('[data-c-info]').innerHTML = '<dt>Điện thoại</dt><dd>' + esc(c.phone) + '</dd><dt>Email</dt><dd>' + esc(c.email || 'Không có') + '</dd><dt>Tỉnh, thành</dt><dd>' + esc(c.city || '') + '</dd><dt>Mã khách</dt><dd class="mono">' + esc(c.id) + '</dd>';
    var key = 'adm:cnotes', notes = X.get(key, {});
    function drawNotes() { $('[data-c-notes]').innerHTML = (notes[c.id] || []).map(function (n) { return '<li>' + esc(n.text) + '<small>' + esc(n.by) + ', ' + X.dateText(n.at) + '</small></li>'; }).join(''); }
    $('[data-c-note]').addEventListener('submit', function (e) { e.preventDefault(); notes[c.id] = notes[c.id] || []; notes[c.id].unshift({ text: e.target.note.value, at: new Date().toISOString(), by: staff.name }); X.set(key, notes); e.target.reset(); drawNotes(); });
    drawNotes();
    document.addEventListener('click', function (e) { var tr = e.target.closest('tr[data-href]'); if (tr && !e.target.closest('a')) location.href = tr.dataset.href; });
  }

  function promos() { return X.get('adm:promos', null) || (S.promotions || []).map(function (p) { return Object.assign({ active: true }, p); }); }
  function pageForPromos() {
    var f = $('[data-promo-form]');
    function draw() {
      var list = promos();
      $('[data-promos]').innerHTML = '<thead><tr><th>Mã</th><th>Ưu đãi</th><th>Điều kiện</th><th>Đã dùng</th><th>Hết hạn</th><th>Trạng thái</th><th><span class="sr">Bật tắt</span></th></tr></thead><tbody>' + list.map(function (p, i) {
        var used = (p.used || 0) + allOrders().filter(function (o) { return o.voucher === p.code; }).length, lim = p.limit;
        return '<tr><td class="mono"><b>' + esc(p.code) + '</b></td><td>' + esc(p.label) + '</td><td>' + (p.min ? 'Đơn từ ' + vnd(p.min) : 'Mọi đơn') + '</td><td>' + (lim ? '<span class="usage"><i style="width:' + Math.min(100, used * 100 / lim) + '%"></i></span>' + num(used) + '/' + num(lim) : num(used)) + '</td><td>' + esc(p.ends || 'Không') + '</td>' +
          '<td>' + (p.active !== false ? '<span class="badge badge-ok">Đang chạy</span>' : '<span class="badge">Tạm dừng</span>') + '</td><td><button class="btn btn-sm" type="button" data-toggle-promo="' + i + '">' + (p.active !== false ? 'Tạm dừng' : 'Chạy lại') + '</button></td></tr>';
      }).join('') + '</tbody>';
    }
    function when() { var t = $('input[name=type]:checked', f).value; $$('[data-when]', f).forEach(function (el) { el.hidden = el.dataset.when.split(' ').indexOf(t) < 0; $('input', el).required = !el.hidden && el.dataset.when !== 'percent'; }); }
    $('[data-promo-new]').addEventListener('click', function () { f.hidden = false; when(); f.code.focus(); });
    $('[data-promo-cancel]').addEventListener('click', function () { f.hidden = true; });
    $('[data-gen]').addEventListener('click', function () { var c = ''; for (var i = 0; i < 8; i++) c += 'ABCDEFGHJKMNPQRSTUVWXYZ23456789'[Math.floor(Math.random() * 31)]; f.code.value = c; });
    f.addEventListener('change', when);
    f.code.addEventListener('input', function () { f.code.value = f.code.value.toUpperCase().replace(/[^A-Z0-9]/g, ''); });
    f.addEventListener('submit', function (e) {
      e.preventDefault(); if (!f.reportValidity()) return;
      var list = promos();
      if (list.some(function (p) { return p.code === f.code.value; })) { X.toast(X.icon('alert') + 'Mã ' + esc(f.code.value) + ' đã có.'); return; }
      var t = $('input[name=type]:checked', f).value, ends = f.ends.value ? f.ends.value.split('-').reverse().join('/') : '';
      list.unshift({ code: f.code.value, type: t, value: +f.value.value || 0, max: +f.max.value || undefined, min: +f.min.value || 0, limit: +f.limit.value || undefined, ends: ends, label: f.label.value, active: true, used: 0 });
      X.set('adm:promos', list); f.reset(); f.hidden = true; draw();
      X.toast(X.icon('check') + 'Đã tạo mã. Khách dùng được ngay ở giỏ hàng trên trình duyệt này.');
    });
    document.addEventListener('click', function (e) {
      var b = e.target.closest('[data-toggle-promo]'); if (!b) return;
      var list = promos(), p = list[+b.dataset.togglePromo]; p.active = p.active === false; X.set('adm:promos', list); draw();
    });
    draw();
  }

  function pageReports() {
    periodControl(function (p) {
      var cur = measure(p.from, p.to), prev = p.cmpFrom != null ? measure(p.cmpFrom, p.cmpTo) : null;
      if (prev) prev.label = p.cmp === 'year' ? 'so với năm trước' : 'so với kỳ trước';
      kpiTiles($('[data-kpis]'), cur, prev, null, null, null);
      var g = defaultGrain(p.days), cs = series(p.from, p.to, g, 'orders'), rs = series(p.from, p.to, g, 'revenue');
      $('[data-orders-title]').textContent = 'Số đơn theo ' + GRAIN_LABEL[g].toLowerCase();
      columnChart($('[data-chart="orders"]'), cs.map(function (x) { return x.label; }), cs.map(function (x) { return x.v; }), function (v) { return num(v) + ' đơn'; }, 'Số đơn');
      tableView($('[data-chart-table="orders"]'), [GRAIN_LABEL[g], 'Số đơn', 'Doanh thu thuần'], cs.map(function (x, i) { return [x.label, num(x.v), vnd(rs[i].v)]; }));
      var money = function (v) { return compact(v) + ' đ'; };
      barList($('[data-chart="channel"]'), groupSum(cur.ok, function (o) { return o.channel; }, net), money, { share: true });
      barList($('[data-chart="city"]'), groupSum(cur.ok, function (o) { return (o.address || {}).province || 'Nhận tại cửa hàng'; }, net).slice(0, 8), money, { share: true });
      barList($('[data-chart="brand"]'), groupSum([].concat.apply([], cur.ok.map(function (o) { return o.items; })), function (it) { return (X.byId[it.id] || {}).brand_name; }, function (it) { return it.price * it.qty; }), money, { share: true });
      var rep = productReport(p.from, p.to), subtotal = cur.ok.reduce(function (a, o) { return a + o.subtotal; }, 0);
      reportRange = [p.from, p.to];
      $('[data-report-table]').innerHTML = '<thead><tr><th>Sản phẩm</th><th>Danh mục</th><th class="num">Số lượng</th><th class="num">Số đơn</th><th class="num">Doanh thu</th><th class="num">Tỷ trọng</th></tr></thead><tbody>' + rep.map(function (r) {
        return '<tr><td><a class="cell-prod" href="product.html?id=' + r.id + '"><img src="' + R + r.image + '" alt="">' + esc(r.name) + '</a></td><td>' + esc(r.cat) + '</td><td class="num">' + num(r.qty) + '</td><td class="num">' + num(r.orders) + '</td><td class="num">' + vnd(r.rev) + '</td><td class="num">' + (subtotal ? (r.rev * 100 / subtotal).toFixed(1).replace('.', ',') : '0') + '%</td></tr>';
      }).join('') + '</tbody>';
    });
  }

  function pageSettings() {
    $('[data-staff]').innerHTML = '<thead><tr><th>Tên</th><th>Vai trò</th><th>Quyền</th></tr></thead><tbody>' + STAFF.map(function (s) {
      return '<tr><td>' + esc(s.name) + '</td><td>' + esc(s.role) + '</td><td>' + (s.role === 'Kho' ? 'Tồn kho, đơn hàng' : s.role === 'Bán hàng' ? 'Đơn hàng, khách hàng' : 'Toàn quyền') + '</td></tr>';
    }).join('') + '</tbody>';
    $('[data-reset-demo]').addEventListener('click', function () {
      if (!confirm('Xóa đơn đặt thử, giỏ hàng, thay đổi giá, tồn kho và trạng thái đơn trên trình duyệt này?')) return;
      var pre = 'shop:' + S.shop.slug + ':';
      Object.keys(localStorage).forEach(function (k) { if (k.indexOf(pre) === 0 && k !== pre + 'adm:staff') localStorage.removeItem(k); });
      X.toast(X.icon('check') + 'Đã xóa dữ liệu chạy thử.');
    });
  }

  shell();
  ({ index: pageIndex, orders: pageOrders, order: pageOrder, products: pageProducts, product: pageProduct, inventory: pageInventory,
     customers: pageCustomers, customer: pageCustomer, promotions: pageForPromos, reports: pageReports, settings: pageSettings }[page] || function () {})();
  var resizeT;
  addEventListener('resize', function () { clearTimeout(resizeT); resizeT = setTimeout(function () { var g = $('[data-g][aria-pressed=true]'); if (g) g.click(); }, 200); });
})();
