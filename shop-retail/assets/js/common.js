/* Shared state for storefront and admin. Everything lives in localStorage under one prefix per shop,
   so a demo works from file:// and an order placed in the shop shows up in the admin. */
(function () {
  var S = window.SHOP || {};
  var P = 'shop:' + ((S.shop && S.shop.slug) || 'x') + ':';
  var ROOT = window.ROOT || '';

  function get(k, d) {
    try { var v = localStorage.getItem(P + k); return v == null ? d : JSON.parse(v); } catch (e) { return d; }
  }
  function set(k, v) {
    try { localStorage.setItem(P + k, JSON.stringify(v)); } catch (e) { /* private mode: state lasts for this page only */ }
    document.dispatchEvent(new CustomEvent('shop:change', { detail: { key: k } }));
  }

  var fmt = new Intl.NumberFormat('vi-VN');
  function vnd(n) { return fmt.format(Math.round(n || 0)) + ' đ'; }
  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }
  function norm(s) {
    return String(s || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd');
  }
  function $(sel, el) { return (el || document).querySelector(sel); }
  function $$(sel, el) { return Array.prototype.slice.call((el || document).querySelectorAll(sel)); }

  var ICONS = {
    check: '<path d="M20 6 9 17l-5-5"/>',
    x: '<path d="M18 6 6 18"/><path d="m6 6 12 12"/>',
    trash: '<path d="M3 6h18"/><path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6"/><path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2"/>',
    plus: '<path d="M5 12h14"/><path d="M12 5v14"/>',
    minus: '<path d="M5 12h14"/>',
    heart: '<path d="M19 14c1.49-1.46 3-3.21 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.76 0-3 .5-4.5 2-1.5-1.5-2.74-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.3 1.5 4.05 3 5.5l7 7Z"/>',
    package: '<path d="m7.5 4.27 9 5.15"/><path d="M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z"/><path d="m3.3 7 8.7 5 8.7-5"/><path d="M12 22V12"/>',
    truck: '<path d="M14 18V6a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v11a1 1 0 0 0 1 1h2"/><path d="M15 18H9"/><path d="M19 18h2a1 1 0 0 0 1-1v-3.65a1 1 0 0 0-.22-.624l-3.48-4.35A1 1 0 0 0 17.52 8H14"/><circle cx="17" cy="18" r="2"/><circle cx="7" cy="18" r="2"/>',
    clipboard: '<rect width="8" height="4" x="8" y="2" rx="1" ry="1"/><path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"/>',
    home: '<path d="m3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/>',
    plus_circle: '<circle cx="12" cy="12" r="10"/><path d="M8 12h8"/><path d="M12 8v8"/>',
    alert: '<path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3"/><path d="M12 9v4"/><path d="M12 17h.01"/>',
    store: '<path d="m2 7 4.41-4.41A2 2 0 0 1 7.83 2h8.34a2 2 0 0 1 1.42.59L22 7"/><path d="M4 12v8a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8"/><path d="M2 7h20"/>',
    search: '<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>',
    star: '<polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/>',
    calendar: '<rect width="18" height="18" x="3" y="4" rx="2"/><line x1="16" x2="16" y1="2" y2="6"/><line x1="8" x2="8" y1="2" y2="6"/><line x1="3" x2="21" y1="10" y2="10"/>',
    chev_r: '<path d="m9 18 6-6-6-6"/>',
    chev_l: '<path d="m15 18-6-6 6-6"/>',
    up: '<path d="m18 15-6-6-6 6"/>',
    down: '<path d="m6 9 6 6 6-6"/>',
    compare: '<circle cx="18" cy="18" r="3"/><circle cx="6" cy="6" r="3"/><path d="M13 6h3a2 2 0 0 1 2 2v7"/><path d="M11 18H8a2 2 0 0 1-2-2V9"/>'
  };
  function icon(name, cls) {
    return '<svg class="i ' + (cls || '') + '" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + (ICONS[name] || '') + '</svg>';
  }

  /* catalogue with admin overrides (price, stock, hidden) */
  var byId = {}, bySku = {};
  (S.products || []).forEach(function (p) {
    byId[p.id] = p;
    p.variants.forEach(function (v) { bySku[v.sku] = { p: p, v: v }; });
  });
  function override(sku) { return (get('adm:products', {})[sku]) || {}; }
  function variant(sku) {
    var r = bySku[sku];
    if (!r) return null;
    var o = override(sku);
    var stock = o.stock != null ? o.stock : r.v.stock;
    var sold = get('sold', {})[sku] || 0;
    return {
      p: r.p, v: r.v, sku: sku, name: r.p.name, label: r.v.label, image: ROOT + (r.v.image || r.p.image),
      price: o.price != null ? o.price : r.v.price, old: o.old_price !== undefined ? o.old_price : r.v.old_price,
      stock: Math.max(0, (stock == null ? 99 : stock) - sold), hidden: !!get('adm:hidden', {})[r.p.id]
    };
  }
  function flashPrice(id) {
    var f = (S.flash || {})[id];
    return f && f.sale_price ? f : null;
  }
  function unitPrice(sku) {
    var x = variant(sku);
    if (!x) return 0;
    var f = flashPrice(x.p.id);
    if (f) return Math.max(0, x.price - (x.p.price - f.sale_price));
    return x.price;
  }

  /* cart */
  function cart() { return get('cart', []).filter(function (l) { return bySku[l.sku]; }); }
  function saveCart(c) { set('cart', c); }
  function addToCart(sku, qty) {
    var c = cart(), x = variant(sku);
    if (!x || x.stock <= 0) return { ok: false, msg: 'Phiên bản này đã hết hàng.' };
    var line = c.filter(function (l) { return l.sku === sku; })[0];
    var want = (line ? line.qty : 0) + (qty || 1);
    if (want > Math.min(5, x.stock)) return { ok: false, msg: 'Mỗi đơn mua tối đa ' + Math.min(5, x.stock) + ' sản phẩm này.' };
    if (line) line.qty = want; else c.push({ sku: sku, id: x.p.id, qty: qty || 1, sel: true });
    saveCart(c);
    return { ok: true };
  }
  function cartCount() { return cart().reduce(function (n, l) { return n + l.qty; }, 0); }

  /* vouchers and totals */
  function findPromo(code) {
    code = String(code || '').trim().toUpperCase();
    var local = get('adm:promos', null);
    var list = local || S.promotions || [];
    return list.filter(function (p) { return p.code.toUpperCase() === code && p.active !== false; })[0] || null;
  }
  function shippingFee(method, subtotal, receive) {
    if (receive === 'pickup' || !method) return 0;
    if (method.free_over && subtotal >= method.free_over) return 0;
    return method.fee || 0;
  }
  function totals(lines, opts) {
    opts = opts || {};
    var sub = 0, saved = 0;
    lines.forEach(function (l) {
      var x = variant(l.sku); if (!x) return;
      var u = unitPrice(l.sku);
      sub += u * l.qty;
      if (x.old && x.old > u) saved += (x.old - u) * l.qty;
    });
    var ship = opts.shipping === undefined ? null : shippingFee(opts.shipping, sub, opts.receive);
    var disc = 0, msg = '', promo = opts.code ? findPromo(opts.code) : null;
    if (opts.code && !promo) msg = 'Mã không đúng hoặc đã hết hạn.';
    if (promo) {
      if (sub < (promo.min || 0)) { msg = 'Mã ' + promo.code + ' áp dụng cho đơn từ ' + vnd(promo.min) + '.'; promo = null; }
      else if (promo.type === 'fixed') disc = promo.value;
      else if (promo.type === 'percent') disc = Math.min(Math.round(sub * promo.value / 100), promo.max || Infinity);
      else if (promo.type === 'shipping') disc = ship || 0;
    }
    disc = Math.min(disc, sub + (ship || 0));
    return { subtotal: sub, saved: saved, shipping: ship, discount: disc, promo: promo, msg: msg, total: Math.max(0, sub - disc + (ship || 0)) };
  }
  function totalsHtml(t, showShipping) {
    var h = '<dt>Tạm tính</dt><dd>' + vnd(t.subtotal) + '</dd>';
    if (t.saved) h += '<dt>Đã tiết kiệm so với giá gốc</dt><dd class="disc">' + vnd(t.saved) + '</dd>';
    if (showShipping) h += '<dt>Phí giao hàng</dt><dd>' + (t.shipping == null ? 'Tính ở bước sau' : (t.shipping ? vnd(t.shipping) : 'Miễn phí')) + '</dd>';
    if (t.discount) h += '<dt>Mã ' + esc(t.promo ? t.promo.code : '') + '</dt><dd class="disc">-' + vnd(t.discount) + '</dd>';
    h += '<dt class="grand">Tổng thanh toán</dt><dd class="grand">' + vnd(t.total) + '</dd><dd class="vat">Giá đã gồm VAT</dd>';
    return h;
  }

  /* orders: the shop's own (placed in this browser) plus status changes made in admin */
  var STATUS = {};
  (S.statuses || []).forEach(function (s) { STATUS[s[0]] = s[1]; });
  var STATUS_CLASS = { pending: 'badge-warn', confirmed: 'badge-info', packing: 'badge-info', shipping: 'badge-info', delivered: 'badge-ok', cancelled: 'badge-bad', returned: 'badge-bad' };
  function orders() { return get('orders', []); }
  function saveOrders(list) { set('orders', list); }
  function findOrder(code) {
    code = String(code || '').trim().toUpperCase();
    return orders().filter(function (o) { return o.code === code; })[0] || null;
  }
  function updateOrder(code, fn) {
    var list = orders(), o = list.filter(function (x) { return x.code === code; })[0];
    if (!o) return null;
    fn(o); saveOrders(list); return o;
  }
  function nextCode() {
    var n = get('seq', 0) + 1; set('seq', n);
    return 'WEB' + String(100000 + n).slice(1) + String(Date.now()).slice(-3);
  }
  function dateText(d) {
    d = new Date(d);
    return ('0' + d.getDate()).slice(-2) + '/' + ('0' + (d.getMonth() + 1)).slice(-2) + '/' + d.getFullYear() + ' ' + ('0' + d.getHours()).slice(-2) + ':' + ('0' + d.getMinutes()).slice(-2);
  }
  function dayText(d) {
    d = new Date(d);
    return ('0' + d.getDate()).slice(-2) + '/' + ('0' + (d.getMonth() + 1)).slice(-2) + '/' + d.getFullYear();
  }

  /* toasts */
  function toast(html) {
    var box = $('[data-toasts]'); if (!box) return;
    var t = document.createElement('div');
    t.className = 'toast'; t.setAttribute('role', 'status'); t.innerHTML = html;
    box.appendChild(t);
    setTimeout(function () { t.remove(); }, 3800);
  }

  window.Shop = {
    S: S, get: get, set: set, vnd: vnd, esc: esc, norm: norm, $: $, $$: $$, icon: icon, root: ROOT,
    byId: byId, bySku: bySku, variant: variant, unitPrice: unitPrice, flashPrice: flashPrice,
    cart: cart, saveCart: saveCart, addToCart: addToCart, cartCount: cartCount,
    findPromo: findPromo, totals: totals, totalsHtml: totalsHtml, shippingFee: shippingFee,
    orders: orders, saveOrders: saveOrders, findOrder: findOrder, updateOrder: updateOrder, nextCode: nextCode,
    STATUS: STATUS, STATUS_CLASS: STATUS_CLASS, dateText: dateText, dayText: dayText, toast: toast,
    user: function () { return get('user', null); }
  };
})();
