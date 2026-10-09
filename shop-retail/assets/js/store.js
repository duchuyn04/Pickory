/* Storefront behaviour: header, carousels, rails, listing filters, product page, cart, checkout,
   orders, tracking, stores and the customer account. Data comes from catalog.js and common.js. */
(function () {
  var X = window.Shop, S = X.S, $ = X.$, $$ = X.$$, esc = X.esc, vnd = X.vnd, R = X.root;
  var reduce = window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches;
  var params = new URLSearchParams(location.search);

  /* ---------------------------------------------------------------- shared bits */
  function cardHtml(p) {
    // the build pre-renders every card in the shop's card style; this markup is only a fallback
    if (window.CARDS && window.CARDS[p.id]) return window.CARDS[p.id].replace(/__ROOT__/g, R);
    var off = p.old_price ? Math.round((p.old_price - p.price) * 100 / p.old_price) : 0;
    return '<article class="pc" data-pc data-id="' + p.id + '" data-price="' + p.price + '" data-brand="' + p.brand + '" data-discount="' + off + '">' +
      '<a class="pc-link" href="' + R + 'p/' + p.id + '.html"><div class="pc-media"><img src="' + R + p.image + '" alt="' + esc(p.name) + '" width="400" height="400" loading="lazy">' +
      (off ? '<span class="pc-off">Giảm ' + off + '%</span>' : '') + '</div><h3 class="pc-name">' + esc(p.name) + '</h3>' +
      (p.card_specs && p.card_specs.length ? '<ul class="pc-specs">' + p.card_specs.slice(0, 3).map(function (s) { return '<li>' + esc(s) + '</li>'; }).join('') + '</ul>' : '') +
      '<div class="pc-price"><strong>' + vnd(p.price) + '</strong>' + (p.old_price ? '<s>' + vnd(p.old_price) + '</s>' : '') + '</div>' +
      (p.rating ? '<div class="pc-meta"><span class="rating"><span class="stars">' + X.icon('star', 'i-xs') + '</span><span class="rating-n">' + String(p.rating.avg).replace('.', ',') + '</span><span class="rating-c">(' + p.rating.count + ')</span></span></div>' : '') +
      '</a><div class="pc-actions"><button class="pc-cmp" type="button" data-compare="' + p.id + '" aria-pressed="false">' + X.icon('compare', 'i-sm') + '<span>So sánh</span></button>' +
      '<button class="pc-fav" type="button" data-fav="' + p.id + '" aria-pressed="false" aria-label="Yêu thích ' + esc(p.name) + '">' + X.icon('heart', 'i-sm') + '</button></div></article>';
  }
  function visible(p) { return !X.get('adm:hidden', {})[p.id]; }

  function syncBadges() {
    var n = X.cartCount();
    $$('[data-cart-count]').forEach(function (b) { b.textContent = n; b.hidden = !n; });
    var fav = X.get('fav', []), cmp = X.get('cmp', []);
    $$('[data-fav]').forEach(function (b) { b.setAttribute('aria-pressed', fav.indexOf(b.dataset.fav) >= 0 ? 'true' : 'false'); });
    $$('[data-compare]').forEach(function (b) { b.setAttribute('aria-pressed', cmp.indexOf(b.dataset.compare) >= 0 ? 'true' : 'false'); });
    var u = X.user();
    $$('[data-account-label]').forEach(function (el) { el.textContent = u ? u.name.split(' ').slice(-1)[0] : 'Đăng nhập'; });
    renderTray();
  }

  /* ---------------------------------------------------------------- header */
  function initHeader() {
    var toggle = $('[data-mega-toggle]'), mega = $('#mega');
    if (toggle && mega) {
      var open = function (v) { mega.hidden = !v; toggle.setAttribute('aria-expanded', v ? 'true' : 'false'); };
      toggle.addEventListener('click', function (e) { e.stopPropagation(); open(mega.hidden); });
      document.addEventListener('click', function (e) { if (!mega.hidden && !mega.contains(e.target)) open(false); });
      document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && !mega.hidden) { open(false); toggle.focus(); } });
    }
    $$('[data-catmenu]').forEach(function (menu) {
      var timer;
      $$('[data-catmenu-item]', menu).forEach(function (li) {
        function show() {
          clearTimeout(timer);
          $$('[data-catmenu-item]', menu).forEach(function (x) { x.classList.toggle('is-on', x === li); });
          $$('[data-catmenu-fly]', menu).forEach(function (f) { f.hidden = f.dataset.catmenuFly !== li.dataset.catmenuItem; });
          menu.classList.add('is-open');
        }
        li.addEventListener('mouseenter', show);
        li.addEventListener('focusin', show);
      });
      if (menu.closest('.hero-cats')) {
        menu.closest('.hero-cats').addEventListener('mouseleave', function () { timer = setTimeout(function () { menu.classList.remove('is-open'); }, 120); });
      }
    });
    $$('[data-open]').forEach(function (b) {
      b.addEventListener('click', function () {
        var d = document.getElementById(b.dataset.open); if (!d) return;
        d.hidden = false; document.body.style.overflow = 'hidden';
        var c = $('[data-close]', d); if (c) c.focus();
      });
    });
    $$('[data-drawer]').forEach(function (d) {
      function close() { d.hidden = true; document.body.style.overflow = ''; }
      d.addEventListener('click', function (e) { if (e.target === d || e.target.closest('[data-close]')) close(); });
      document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && !d.hidden) close(); });
    });
    // rotate the announcement on narrow screens
    var tick = $('[data-ticker]');
    if (tick && tick.children.length > 1 && !reduce) {
      var i = 0;
      setInterval(function () {
        if (innerWidth > 767) return;
        var items = tick.children; items[i].style.display = 'none';
        i = (i + 1) % items.length; items[i].style.display = 'flex';
      }, 4000);
    }
    initSuggest();
  }

  function initSuggest() {
    var form = $('[data-search]'); if (!form) return;
    var input = $('[data-search-input]', form), box = $('[data-suggest]', form), sel = -1;
    if (params.get('q') && input) input.value = params.get('q');
    function hl(text, q) {
      var n = X.norm(text), i = n.indexOf(q);
      if (i < 0 || !q) return esc(text);
      return esc(text.slice(0, i)) + '<mark>' + esc(text.slice(i, i + q.length)) + '</mark>' + esc(text.slice(i + q.length));
    }
    function render() {
      var q = X.norm(input.value.trim());
      if (q.length < 1) {
        var recent = X.get('searches', []);
        if (!recent.length) { box.hidden = true; return; }
        box.innerHTML = '<h3>Tìm kiếm gần đây</h3>' + recent.map(function (r) { return '<a href="' + R + 'search.html?q=' + encodeURIComponent(r) + '">' + X.icon('search', 'i-sm') + esc(r) + '</a>'; }).join('');
        box.hidden = false; return;
      }
      var prods = S.products.filter(function (p) { return visible(p) && X.norm(p.name + ' ' + p.brand_name + ' ' + p.category_name + ' ' + (p.keywords || '')).indexOf(q) >= 0; }).slice(0, 6);
      var cats = S.categories.filter(function (c) { return X.norm(c.name).indexOf(q) >= 0; }).slice(0, 3);
      var h = '';
      if (cats.length) h += '<h3>Danh mục</h3>' + cats.map(function (c) { return '<a href="' + R + 'c/' + c.id + '.html">' + hl(c.name, q) + '</a>'; }).join('');
      if (prods.length) h += '<h3>Sản phẩm</h3>' + prods.map(function (p) { return '<a href="' + R + 'p/' + p.id + '.html"><img src="' + R + p.image + '" alt="" width="44" height="44"><span>' + hl(p.name, q) + '</span><span class="s-price">' + vnd(p.price) + '</span></a>'; }).join('');
      h += '<a class="s-all" href="' + R + 'search.html?q=' + encodeURIComponent(input.value.trim()) + '">Xem tất cả kết quả cho "' + esc(input.value.trim()) + '"</a>';
      box.innerHTML = h; box.hidden = false; sel = -1;
    }
    input.addEventListener('input', render);
    input.addEventListener('focus', render);
    input.addEventListener('keydown', function (e) {
      var links = $$('a', box);
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault(); if (!links.length) return;
        sel = (sel + (e.key === 'ArrowDown' ? 1 : -1) + links.length) % links.length;
        links.forEach(function (a, i) { a.setAttribute('aria-selected', i === sel ? 'true' : 'false'); });
      } else if (e.key === 'Enter' && sel >= 0 && links[sel]) { e.preventDefault(); location.href = links[sel].href; }
      else if (e.key === 'Escape') box.hidden = true;
    });
    document.addEventListener('click', function (e) { if (!form.contains(e.target)) box.hidden = true; });
    form.addEventListener('submit', function () {
      var q = input.value.trim(); if (!q) return;
      var r = X.get('searches', []).filter(function (x) { return x !== q; }); r.unshift(q); X.set('searches', r.slice(0, 5));
    });
  }

  /* ---------------------------------------------------------------- home widgets */
  function initCarousels() {
    $$('[data-carousel]').forEach(function (c) {
      var slides = $$('[data-slide]', c), tabs = $$('[data-carousel-tab]', c.parentNode), i = 0, t;
      if (slides.length < 2) return;
      function go(n) {
        i = (n + slides.length) % slides.length;
        slides.forEach(function (s, k) { var on = k === i; s.classList.toggle('is-active', on); s.setAttribute('aria-hidden', on ? 'false' : 'true'); s.tabIndex = on ? 0 : -1; });
        tabs.forEach(function (b, k) { b.setAttribute('aria-selected', k === i ? 'true' : 'false'); });
      }
      function play() { stop(); if (!reduce) t = setInterval(function () { go(i + 1); }, +c.dataset.interval || 5000); }
      function stop() { clearInterval(t); }
      $('[data-carousel-prev]', c).addEventListener('click', function () { go(i - 1); play(); });
      $('[data-carousel-next]', c).addEventListener('click', function () { go(i + 1); play(); });
      tabs.forEach(function (b, k) { b.addEventListener('click', function () { go(k); play(); }); });
      c.addEventListener('mouseenter', stop); c.addEventListener('mouseleave', play);
      c.addEventListener('focusin', stop);
      var x0 = null;
      c.addEventListener('touchstart', function (e) { x0 = e.touches[0].clientX; stop(); }, { passive: true });
      c.addEventListener('touchend', function (e) { if (x0 == null) return; var dx = e.changedTouches[0].clientX - x0; if (Math.abs(dx) > 40) go(i + (dx < 0 ? 1 : -1)); x0 = null; play(); });
      play();
    });
  }

  function initRails() {
    $$('[data-rail]').forEach(function (r) {
      var track = $('[data-rail-track]', r), prev = $('[data-rail-prev]', r), next = $('[data-rail-next]', r);
      if (!track || !prev) return;
      function upd() {
        prev.disabled = track.scrollLeft < 4;
        next.disabled = track.scrollLeft + track.clientWidth >= track.scrollWidth - 4;
      }
      prev.addEventListener('click', function () { track.scrollBy({ left: -track.clientWidth, behavior: reduce ? 'auto' : 'smooth' }); });
      next.addEventListener('click', function () { track.scrollBy({ left: track.clientWidth, behavior: reduce ? 'auto' : 'smooth' }); });
      track.addEventListener('scroll', upd, { passive: true });
      addEventListener('resize', upd); upd();
    });
    $$('[data-rail-filter]').forEach(function (g) {
      var track = document.getElementById(g.dataset.railFilter);
      $$('[data-brand]', g).forEach(function (b) {
        b.addEventListener('click', function () {
          $$('[data-brand]', g).forEach(function (x) { x.classList.toggle('is-on', x === b); x.setAttribute('aria-pressed', x === b ? 'true' : 'false'); });
          $$('[data-pc]', track).forEach(function (c) { c.hidden = b.dataset.brand && c.dataset.brand !== b.dataset.brand; });
          track.scrollLeft = 0; track.dispatchEvent(new Event('scroll'));
        });
      });
    });
  }

  function initFlash() {
    $$('[data-flash]').forEach(function (f) {
      var parts = (f.dataset.ends || '23:59').split(':');
      function end() { var d = new Date(); d.setHours(+parts[0], +parts[1] || 0, 59, 999); if (d < new Date()) d.setDate(d.getDate() + 1); return d; }
      var e = end(), h = $('[data-t=h]', f), m = $('[data-t=m]', f), s = $('[data-t=s]', f);
      function tick() {
        var left = Math.max(0, e - new Date()); if (!left) e = end();
        var sec = Math.floor(left / 1000);
        h.textContent = ('0' + Math.floor(sec / 3600)).slice(-2);
        m.textContent = ('0' + Math.floor(sec % 3600 / 60)).slice(-2);
        s.textContent = ('0' + sec % 60).slice(-2);
      }
      tick(); setInterval(tick, 1000);
    });
  }

  function initMore() {
    $$('[data-more-grid]').forEach(function (g) {
      var step = +g.dataset.step || 10, shown = step, btn = $('[data-more]', g.parentNode);
      function apply() {
        var cards = $$('[data-pc]', g);
        cards.forEach(function (c, i) { c.hidden = i >= shown; });
        if (btn) btn.parentNode.hidden = cards.length <= shown;
      }
      if (btn) btn.addEventListener('click', function () { shown += step; apply(); });
      apply();
    });
  }

  /* ---------------------------------------------------------------- favourites and compare */
  function initCardActions() {
    document.addEventListener('click', function (e) {
      var f = e.target.closest('[data-fav]');
      if (f) {
        var fav = X.get('fav', []), id = f.dataset.fav, i = fav.indexOf(id);
        if (i >= 0) fav.splice(i, 1); else fav.unshift(id);
        X.set('fav', fav);
        X.toast(X.icon('heart') + (i >= 0 ? 'Đã bỏ khỏi yêu thích' : 'Đã thêm vào yêu thích. <a href="' + R + 'account/wishlist.html">Xem danh sách</a>'));
        return;
      }
      var c = e.target.closest('[data-compare]');
      if (c) {
        var cmp = X.get('cmp', []), cid = c.dataset.compare, j = cmp.indexOf(cid);
        if (j >= 0) cmp.splice(j, 1);
        else {
          var p = X.byId[cid], first = cmp.length ? X.byId[cmp[0]] : null;
          if (first && first.category !== p.category) { X.toast(X.icon('alert') + 'Chỉ so sánh sản phẩm cùng loại. Xóa danh sách hiện tại để so sánh ' + esc(p.category_name.toLowerCase()) + '.'); return; }
          if (cmp.length >= 3) { X.toast(X.icon('alert') + 'So sánh tối đa 3 sản phẩm.'); return; }
          cmp.push(cid);
        }
        X.set('cmp', cmp);
      }
    });
    var clear = $('[data-compare-clear]');
    if (clear) clear.addEventListener('click', function () { X.set('cmp', []); });
  }
  function renderTray() {
    var tray = $('[data-compare-tray]'); if (!tray) return;
    var cmp = X.get('cmp', []);
    tray.hidden = !cmp.length || document.body.classList.contains('pg-compare');
    var h = '';
    for (var i = 0; i < 3; i++) {
      var p = X.byId[cmp[i]];
      h += p ? '<li class="is-full"><img src="' + R + p.image + '" alt=""><span>' + esc(p.name) + '</span><button class="icon-btn" type="button" data-compare="' + p.id + '" aria-label="Bỏ ' + esc(p.name) + '">' + X.icon('x', 'i-sm') + '</button></li>'
        : '<li>' + X.icon('plus_circle', 'i-sm') + '<span class="muted">Thêm sản phẩm</span></li>';
    }
    $('[data-compare-items]', tray).innerHTML = h;
  }

  /* ---------------------------------------------------------------- listing */
  function initPlp() {
    var plp = $('[data-plp]'); if (!plp) return;
    var grid = $('[data-plp-grid]', plp), cards = $$('[data-pc]', grid), filters = $('[data-filters]'), scrim = $('[data-scrim]');
    var step = 20, shown = step, sort = params.get('sort') || 'popular';
    cards.forEach(function (c, i) { c.dataset.order = i; c._f = JSON.parse(c.dataset.facets || '{}'); });
    // preselect from the URL (mega menu links)
    params.forEach(function (v, k) {
      var name = k === 'brand' ? 'brand' : k === 'price' ? 'price' : 'f:' + k;
      var hit = false;
      $$('input[name="' + name + '"]', filters).forEach(function (i) { if (i.value === v) { i.checked = true; hit = true; } });
      // a price band that is not one of the listed ranges fills the min and max boxes
      if (k === 'price' && !hit) {
        var a = v.split('-'), lo = $('[data-price-min]', filters), hi = $('[data-price-max]', filters);
        if (lo && +a[0]) lo.value = +a[0];
        if (hi && +a[1]) hi.value = +a[1];
      }
    });
    function state() {
      var st = { brand: [], price: [], f: {}, instock: false, sale: false, min: +($('[data-price-min]', filters).value || 0), max: +($('[data-price-max]', filters).value || 0) };
      $$('input:checked', filters).forEach(function (i) {
        if (i.name === 'brand') st.brand.push(i.value);
        else if (i.name === 'price') st.price.push(i.value);
        else if (i.name === 'instock') st.instock = true;
        else if (i.name === 'sale') st.sale = true;
        else if (i.name.indexOf('f:') === 0) { var k = i.name.slice(2); (st.f[k] = st.f[k] || []).push(i.value); }
      });
      return st;
    }
    function match(c, st) {
      var price = +c.dataset.price;
      if (X.get('adm:hidden', {})[c.dataset.id]) return false;
      if (st.brand.length && st.brand.indexOf(c.dataset.brand) < 0) return false;
      if (st.price.length && !st.price.some(function (r) { var a = r.split('-'); return price >= (+a[0] || 0) && (!a[1] || price < +a[1]); })) return false;
      if (st.min && price < st.min) return false;
      if (st.max && price > st.max) return false;
      if (st.instock && +c.dataset.stock <= 0) return false;
      if (st.sale && !(+c.dataset.discount > 0)) return false;
      for (var k in st.f) {
        var v = c._f[k], vals = Array.isArray(v) ? v : [v];
        if (!st.f[k].some(function (x) { return vals.indexOf(x) >= 0; })) return false;
      }
      return true;
    }
    function sorter(a, b) {
      if (sort === 'price-asc') return a.dataset.price - b.dataset.price;
      if (sort === 'price-desc') return b.dataset.price - a.dataset.price;
      if (sort === 'discount') return b.dataset.discount - a.dataset.discount;
      if (sort === 'new') return (b.dataset.released || '').localeCompare(a.dataset.released || '');
      return a.dataset.order - b.dataset.order;
    }
    function apply(resetPage) {
      if (resetPage) shown = step;
      var st = state(), hits = cards.filter(function (c) { return match(c, st); }).sort(sorter);
      cards.forEach(function (c) { c.hidden = true; });
      hits.forEach(function (c, i) { grid.appendChild(c); c.hidden = i >= shown; });
      $$('[data-plp-count]').forEach(function (el) { el.textContent = hits.length + ' sản phẩm'; });
      $$('[data-plp-count-n]').forEach(function (el) { el.textContent = hits.length; });
      $('[data-plp-empty]').hidden = hits.length > 0;
      var more = $('[data-more]', plp); more.parentNode.hidden = hits.length <= shown;
      $('[data-more-n]', plp).textContent = Math.max(0, hits.length - shown);
      // active chips
      var chips = [];
      $$('input:checked', filters).forEach(function (i) { chips.push({ label: i.parentNode.textContent.trim(), input: i }); });
      if (st.min || st.max) chips.push({ label: 'Giá ' + (st.min ? 'từ ' + vnd(st.min) : '') + (st.max ? ' đến ' + vnd(st.max) : ''), range: true });
      var ul = $('[data-active-filters]');
      ul.innerHTML = chips.map(function (c, k) { return '<li><button type="button" data-chip="' + k + '">' + esc(c.label) + X.icon('x', 'i-xs') + '</button></li>'; }).join('');
      $$('[data-chip]', ul).forEach(function (b) {
        b.addEventListener('click', function () {
          var c = chips[+b.dataset.chip];
          if (c.input) c.input.checked = false; else { $('[data-price-min]', filters).value = ''; $('[data-price-max]', filters).value = ''; }
          syncQuick(); apply(true);
        });
      });
      var cnt = $('[data-filter-count]'); cnt.textContent = chips.length; cnt.hidden = !chips.length;
    }
    function syncQuick() {
      $$('[data-quick-brand]').forEach(function (b) {
        var i = $('input[name=brand][value="' + b.dataset.quickBrand + '"]', filters);
        b.setAttribute('aria-pressed', i && i.checked ? 'true' : 'false');
      });
    }
    filters.addEventListener('change', function () { syncQuick(); apply(true); });
    $$('[data-quick-brand]').forEach(function (b) {
      b.addEventListener('click', function () {
        var i = $('input[name=brand][value="' + b.dataset.quickBrand + '"]', filters);
        if (i) { i.checked = !i.checked; syncQuick(); apply(true); }
      });
    });
    $$('[data-filter-reset]').forEach(function (b) {
      b.addEventListener('click', function () {
        $$('input[type=checkbox]', filters).forEach(function (i) { i.checked = false; });
        $$('input[type=number]', filters).forEach(function (i) { i.value = ''; });
        syncQuick(); apply(true);
      });
    });
    function setSort(v) {
      sort = v;
      $$('[data-sort]').forEach(function (b) { var on = b.dataset.sort === v; b.classList.toggle('is-on', on); b.setAttribute('aria-pressed', on ? 'true' : 'false'); });
      var s = $('[data-sort-select]'); if (s) s.value = v;
      apply(true);
    }
    $$('[data-sort]').forEach(function (b) { b.addEventListener('click', function () { setSort(b.dataset.sort); }); });
    var ss = $('[data-sort-select]'); if (ss) ss.addEventListener('change', function () { setSort(ss.value); });
    $('[data-more]', plp).addEventListener('click', function () { shown += step; apply(false); });
    // mobile drawer
    function drawer(open) {
      filters.classList.toggle('is-open', open); scrim.hidden = !open;
      document.body.style.overflow = open ? 'hidden' : '';
      if (open) $('[data-filters-close]', filters).focus();
    }
    $$('[data-filters-open]').forEach(function (b) { b.addEventListener('click', function () { drawer(true); }); });
    $$('[data-filters-close]').forEach(function (b) { b.addEventListener('click', function () { drawer(false); }); });
    scrim.addEventListener('click', function () { drawer(false); });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && filters.classList.contains('is-open')) drawer(false); });
    syncQuick(); setSort(sort);
  }

  /* ---------------------------------------------------------------- product page */
  function initPdp() {
    var root = $('[data-pdp]'); if (!root) return;
    var p = X.byId[root.dataset.pdp], sel = p.options.map(function (o) { return o.values[0].name; });
    // open on the first variant that is in stock, so the page does not start on "het hang"
    var firstIn = p.variants.filter(function (v) { return X.variant(v.sku).stock > 0; })[0];
    if (firstIn) sel = firstIn.values.slice();
    var gallery = $('[data-gallery]');
    // recently viewed
    var rec = X.get('recent', []).filter(function (x) { return x !== p.id && X.byId[x]; });
    if (rec.length) {
      var box = $('[data-recent]'); box.hidden = false;
      $('[data-recent-track]').innerHTML = rec.slice(0, 10).map(function (id) { return cardHtml(X.byId[id]); }).join('');
    }
    rec.unshift(p.id); X.set('recent', rec.slice(0, 12));

    function showImg(k) {
      if (!gallery) return;
      $$('[data-gallery-img]', gallery).forEach(function (im) { im.hidden = +im.dataset.galleryImg !== k; });
      $$('[data-gallery-thumb]', gallery).forEach(function (b) { b.setAttribute('aria-selected', +b.dataset.galleryThumb === k ? 'true' : 'false'); });
      gallery._k = k;
    }
    if (gallery) {
      gallery._k = 0;
      var n = $$('[data-gallery-img]', gallery).length;
      $$('[data-gallery-thumb]', gallery).forEach(function (b) { b.addEventListener('click', function () { showImg(+b.dataset.galleryThumb); }); });
      var gp = $('[data-gallery-prev]', gallery), gn = $('[data-gallery-next]', gallery);
      if (gp) gp.addEventListener('click', function () { showImg((gallery._k - 1 + n) % n); });
      if (gn) gn.addEventListener('click', function () { showImg((gallery._k + 1) % n); });
    }
    function current() {
      var v = p.variants.filter(function (v) { return v.values.every(function (x, i) { return x === sel[i]; }); })[0];
      return v ? X.variant(v.sku) : null;
    }
    function stockOf(values) {
      var v = p.variants.filter(function (v) { return v.values.every(function (x, i) { return x === values[i]; }); })[0];
      return v ? X.variant(v.sku).stock : 0;
    }
    function update() {
      var x = current(); if (!x) return;
      var unit = X.unitPrice(x.sku);
      $('[data-v-price]').textContent = vnd(unit);
      var old = $('[data-v-old]'), off = $('[data-v-off]');
      var o = x.old && x.old > unit ? x.old : (unit < x.price ? x.price : 0);
      old.hidden = !o; off.hidden = !o;
      if (o) { old.textContent = vnd(o); off.textContent = '-' + Math.round((o - unit) * 100 / o) + '%'; }
      var mo = $('[data-v-monthly]'); if (mo) mo.textContent = vnd(Math.round(unit / 12 / 1000) * 1000);
      $$('[data-opt]').forEach(function (fs) {
        var oi = +fs.dataset.opt;
        $('[data-opt-value]', fs).textContent = sel[oi];
        $$('[data-opt-btn]', fs).forEach(function (b) {
          var vals = sel.slice(); vals[oi] = b.dataset.optBtn;
          var out = stockOf(vals) <= 0;
          b.classList.toggle('is-out', out);
          b.setAttribute('aria-pressed', b.dataset.optBtn === sel[oi] ? 'true' : 'false');
          b.title = out ? 'Hết hàng' : '';
        });
      });
      var st = $('[data-v-stock]');
      st.className = 'stock ' + (x.stock <= 0 ? 'out' : x.stock <= 5 ? 'low' : 'ok');
      st.innerHTML = x.stock <= 0 ? X.icon('alert', 'i-sm') + 'Phiên bản này tạm hết hàng' : X.icon('check', 'i-sm') + (x.stock <= 5 ? 'Chỉ còn ' + x.stock + ' sản phẩm' : 'Còn hàng, giao hoặc nhận tại cửa hàng');
      $$('[data-buy-now],[data-add-cart]').forEach(function (b) { b.hidden = x.stock <= 0; });
      var nb = $('[data-notify]'); if (nb) nb.hidden = x.stock > 0;
      // colour image
      var colorOpt = p.options.map(function (o, i) { return o.color ? i : -1; }).filter(function (i) { return i >= 0; })[0];
      if (colorOpt != null) {
        var ci = p.options[colorOpt].values.map(function (v) { return v.name; }).indexOf(sel[colorOpt]);
        showImg(Math.max(0, ci));
      }
      var bb = $('[data-buybar]');
      if (bb) { $('[data-buybar-price]', bb).textContent = vnd(unit); $('[data-buybar-variant]', bb).textContent = x.label; $('[data-buybar-img]', bb).src = x.image; }
      renderAvail();
    }
    $$('[data-opt]').forEach(function (fs) {
      $$('[data-opt-btn]', fs).forEach(function (b) {
        b.addEventListener('click', function () { sel[+fs.dataset.opt] = b.dataset.optBtn; update(); });
      });
    });
    function add(go) {
      var x = current(); if (!x) return;
      var r = X.addToCart(x.sku, 1);
      if (!r.ok) { X.toast(X.icon('alert') + esc(r.msg)); return; }
      if (go) { location.href = R + 'cart.html'; return; }
      X.toast(X.icon('check') + 'Đã thêm ' + esc(x.name) + ' (' + esc(x.label) + ') vào giỏ. <a href="' + R + 'cart.html">Xem giỏ hàng</a>');
    }
    $$('[data-add-cart]').forEach(function (b) { b.addEventListener('click', function () { add(false); }); });
    $$('[data-buy-now]').forEach(function (b) {
      b.addEventListener('click', function () {
        if (b.hasAttribute('data-installment')) X.set('pay_pref', 'installment');
        add(true);
      });
    });
    var nb = $('[data-notify]');
    if (nb) nb.addEventListener('click', function () {
      var x = current(), list = X.get('notify', []);
      list.push({ sku: x.sku, at: Date.now() }); X.set('notify', list);
      X.toast(X.icon('check') + 'Cửa hàng sẽ báo khi ' + esc(x.label) + ' có hàng lại.');
    });
    // store availability
    var av = $('[data-avail]');
    function renderAvail() {
      if (!av) return;
      var x = current(), city = $('[data-avail-city]', av).value;
      var list = (S.stores || []).filter(function (s) { return s.city === city; });
      $('[data-avail-list]', av).innerHTML = list.map(function (s) {
        var has = (S.store_stock[s.id] || null);
        var ok = has ? has.indexOf(x.sku) >= 0 : x.stock > 0;
        return '<li><div><b>' + esc(s.name) + '</b>' + esc(s.address) + '</div><span class="badge ' + (ok ? 'badge-ok' : 'badge-bad') + '">' + (ok ? 'Còn hàng' : 'Hết hàng') + '</span></li>';
      }).join('');
    }
    if (av) $('[data-avail-city]', av).addEventListener('change', renderAvail);
    // add-ons
    var ad = $('[data-addons-add]');
    if (ad) ad.addEventListener('click', function () {
      var n = 0;
      $$('[data-addon]:checked').forEach(function (i) { if (X.addToCart(i.dataset.addon, 1).ok) n++; });
      X.toast(X.icon('check') + 'Đã thêm ' + n + ' sản phẩm mua kèm vào giỏ. <a href="' + R + 'cart.html">Xem giỏ hàng</a>');
    });
    // specs
    var stg = $('[data-spec-toggle]');
    if (stg) stg.addEventListener('click', function () {
      var open = stg.getAttribute('aria-expanded') !== 'true';
      $$('[data-spec-more]').forEach(function (t) { t.hidden = !open; });
      stg.setAttribute('aria-expanded', open ? 'true' : 'false');
      stg.firstChild.textContent = open ? 'Thu gọn' : 'Xem cấu hình chi tiết';
    });
    // reviews
    $$('[data-rev-filter]').forEach(function (b) {
      b.addEventListener('click', function () {
        $$('[data-rev-filter]').forEach(function (x) { x.classList.toggle('is-on', x === b); x.setAttribute('aria-pressed', x === b ? 'true' : 'false'); });
        $$('.rev-list>li').forEach(function (li) { li.hidden = b.dataset.revFilter && li.dataset.stars !== b.dataset.revFilter; });
      });
    });
    var rf = $('[data-review-form]');
    $$('[data-review-open]').forEach(function (b) { b.addEventListener('click', function () { rf.hidden = false; $('input[name=name]', rf).focus(); }); });
    if (rf) {
      var u = X.user(); if (u) $('input[name=name]', rf).value = u.name;
      $('[data-review-cancel]', rf).addEventListener('click', function () { rf.hidden = true; });
      function stars() { var v = +$('input[name=stars]:checked', rf).value; $$('.star-input label', rf).forEach(function (l, i) { l.classList.toggle('is-on', i < v); }); }
      $$('input[name=stars]', rf).forEach(function (i) { i.addEventListener('change', stars); }); stars();
      rf.addEventListener('submit', function (e) {
        e.preventDefault(); if (!rf.reportValidity()) return;
        var list = X.get('reviews', []);
        list.push({ product: p.id, name: rf.name.value, stars: +$('input[name=stars]:checked', rf).value, text: rf.text.value, at: Date.now(), status: 'pending' });
        X.set('reviews', list); rf.reset(); rf.hidden = true; stars();
        X.toast(X.icon('check') + 'Cảm ơn bạn. Đánh giá sẽ hiển thị sau khi cửa hàng duyệt.');
      });
    }
    var qf = $('[data-qa-form]');
    if (qf) qf.addEventListener('submit', function (e) {
      e.preventDefault(); if (!qf.reportValidity()) return;
      var u = X.user(), list = X.get('questions', []);
      list.push({ product: p.id, q: qf.q.value, name: u ? u.name : 'Khách hàng', at: Date.now() });
      X.set('questions', list);
      var li = document.createElement('li');
      li.innerHTML = '<div class="qa-q"><span class="avatar" aria-hidden="true">' + esc((u ? u.name : 'K')[0]) + '</span><div><b>' + esc(u ? u.name : 'Bạn') + '</b><p>' + esc(qf.q.value) + '</p><small class="muted">Đang chờ cửa hàng trả lời</small></div></div>';
      $('[data-qa-list]').prepend(li); var em = $('[data-qa-empty]'); if (em) em.hidden = true;
      qf.reset();
    });
    update();
  }

  /* ---------------------------------------------------------------- search page */
  function initSearchPage() {
    var pg = $('[data-search-page]'); if (!pg) return;
    var q = (params.get('q') || '').trim(), brand = params.get('brand'), cat = '', sort = 'relevant';
    $('[data-search-page-input]').value = q;
    var qn = X.norm(q), words = qn.split(/\s+/).filter(Boolean);
    var base = S.products.filter(function (p) {
      if (!visible(p)) return false;
      if (brand && p.brand !== brand) return false;
      var hay = X.norm([p.name, p.brand_name, p.category_name, (p.card_specs || []).join(' '), p.keywords || ''].join(' '));
      return words.every(function (w) { return hay.indexOf(w) >= 0; });
    });
    var title = brand ? 'Sản phẩm ' + ((S.brands.filter(function (b) { return b.id === brand; })[0] || {}).name || '') : q ? 'Kết quả cho "' + q + '"' : 'Tất cả sản phẩm';
    $('[data-search-title]').innerHTML = esc(title) + ' <small>' + base.length + ' sản phẩm</small>';
    document.title = title + ' | ' + S.shop.name;
    var cats = S.categories.filter(function (c) { return base.some(function (p) { return p.category === c.id; }); });
    var catBox = $('[data-search-cats]');
    catBox.innerHTML = '<button type="button" class="chip is-on" data-c="" aria-pressed="true">Tất cả</button>' + cats.map(function (c) { return '<button type="button" class="chip" data-c="' + c.id + '" aria-pressed="false">' + esc(c.name) + '</button>'; }).join('');
    function render() {
      var list = base.filter(function (p) { return !cat || p.category === cat; });
      if (sort === 'price-asc') list.sort(function (a, b) { return a.price - b.price; });
      if (sort === 'price-desc') list.sort(function (a, b) { return b.price - a.price; });
      if (sort === 'discount') list.sort(function (a, b) { return (b.discount || 0) - (a.discount || 0); });
      $('[data-search-grid]').innerHTML = list.map(cardHtml).join('');
      var empty = !list.length; $('[data-search-empty]').hidden = !empty; $('[data-search-grid]').hidden = empty;
      if (empty) $('[data-search-empty-title]').textContent = q ? 'Không tìm thấy sản phẩm cho "' + q + '"' : 'Không có sản phẩm';
      syncBadges();
    }
    $$('[data-c]', catBox).forEach(function (b) {
      b.addEventListener('click', function () {
        cat = b.dataset.c;
        $$('[data-c]', catBox).forEach(function (x) { x.classList.toggle('is-on', x === b); x.setAttribute('aria-pressed', x === b ? 'true' : 'false'); });
        render();
      });
    });
    $('[data-search-sort]').addEventListener('change', function (e) { sort = e.target.value; render(); });
    render();
  }

  /* ---------------------------------------------------------------- compare page */
  function initComparePage() {
    var pg = $('[data-compare-page]'); if (!pg) return;
    var dlg = $('[data-compare-picker]');
    function render() {
      var ids = X.get('cmp', []).filter(function (id) { return X.byId[id]; }), items = ids.map(function (id) { return X.byId[id]; });
      $('[data-compare-empty]').hidden = items.length > 0; $('[data-compare-table]').hidden = !items.length;
      if (!items.length) return;
      var groups = [];
      items.forEach(function (p) { p.specs.forEach(function (g) { var G = groups.filter(function (x) { return x.group === g.group; })[0]; if (!G) groups.push(G = { group: g.group, keys: [] }); g.rows.forEach(function (r) { if (G.keys.indexOf(r[0]) < 0) G.keys.push(r[0]); }); }); });
      function val(p, g, k) { var G = p.specs.filter(function (x) { return x.group === g; })[0]; var r = G && G.rows.filter(function (x) { return x[0] === k; })[0]; return r ? r[1] : ''; }
      var cols = items.length < 3 ? items.length + 1 : 3;
      var h = '<table class="cmp-table"><thead><tr><th scope="col"><span class="sr">Thông số</span></th>';
      items.forEach(function (p) {
        h += '<td><div class="cmp-prod"><button class="link-btn" type="button" data-cmp-del="' + p.id + '" style="align-self:flex-end">' + X.icon('x', 'i-sm') + 'Bỏ</button><img src="' + R + p.image + '" alt=""><a href="' + R + 'p/' + p.id + '.html"><b>' + esc(p.name) + '</b></a><span class="price"><strong>' + vnd(p.price) + '</strong>' + (p.old_price ? '<s>' + vnd(p.old_price) + '</s>' : '') + '</span><button class="btn btn-primary" type="button" data-cmp-buy="' + p.variants[0].sku + '">Thêm vào giỏ</button></div></td>';
      });
      if (items.length < 3) h += '<td><button type="button" class="cmp-add" data-cmp-add>' + X.icon('plus_circle') + 'Thêm sản phẩm</button></td>';
      h += '</tr></thead><tbody>';
      h += '<tr><th scope="row">Thương hiệu</th>' + items.map(function (p) { return '<td>' + esc(p.brand_name) + '</td>'; }).join('') + (items.length < 3 ? '<td></td>' : '') + '</tr>';
      h += '<tr><th scope="row">Bảo hành</th>' + items.map(function (p) { return '<td>' + esc(p.warranty || '') + '</td>'; }).join('') + (items.length < 3 ? '<td></td>' : '') + '</tr>';
      groups.forEach(function (G) {
        h += '<tr class="cmp-grp"><th colspan="' + (cols + 1) + '" scope="colgroup">' + esc(G.group) + '</th></tr>';
        G.keys.forEach(function (k) {
          var vals = items.map(function (p) { return val(p, G.group, k); });
          var diff = vals.some(function (v) { return v !== vals[0]; });
          h += '<tr data-row-diff="' + (diff ? 1 : 0) + '"' + (diff && items.length > 1 ? ' class="is-diff"' : '') + '><th scope="row">' + esc(k) + '</th>' + vals.map(function (v) { return '<td>' + (esc(v) || '<span class="muted">Không có</span>') + '</td>'; }).join('') + (items.length < 3 ? '<td></td>' : '') + '</tr>';
        });
      });
      h += '</tbody></table>';
      $('[data-compare-table]').innerHTML = h;
      diffOnly();
    }
    function diffOnly() {
      var on = $('[data-compare-diff]').checked;
      $$('[data-row-diff="0"]').forEach(function (tr) { tr.hidden = on; });
    }
    $('[data-compare-diff]').addEventListener('change', diffOnly);
    pg.addEventListener('click', function (e) {
      var d = e.target.closest('[data-cmp-del]');
      if (d) { X.set('cmp', X.get('cmp', []).filter(function (x) { return x !== d.dataset.cmpDel; })); render(); }
      var b = e.target.closest('[data-cmp-buy]');
      if (b) { var r = X.addToCart(b.dataset.cmpBuy, 1); X.toast(r.ok ? X.icon('check') + 'Đã thêm vào giỏ. <a href="' + R + 'cart.html">Xem giỏ hàng</a>' : X.icon('alert') + esc(r.msg)); }
      if (e.target.closest('[data-cmp-add]')) { dlg.showModal(); pick(''); $('[data-compare-pick-q]').focus(); }
      if (e.target.closest('[data-modal-close]')) dlg.close();
      var pk = e.target.closest('[data-pick]');
      if (pk) { var c = X.get('cmp', []); c.push(pk.dataset.pick); X.set('cmp', c); dlg.close(); render(); }
    });
    function pick(q) {
      var ids = X.get('cmp', []), cat = ids.length ? X.byId[ids[0]].category : null, qn = X.norm(q);
      var list = S.products.filter(function (p) { return visible(p) && ids.indexOf(p.id) < 0 && (!cat || p.category === cat) && X.norm(p.name).indexOf(qn) >= 0; }).slice(0, 20);
      $('[data-compare-pick-list]').innerHTML = list.map(function (p) { return '<li><button type="button" data-pick="' + p.id + '"><img src="' + R + p.image + '" alt=""><span>' + esc(p.name) + '<br><b class="price"><strong>' + vnd(p.price) + '</strong></b></span></button></li>'; }).join('') || '<li class="muted">Không có sản phẩm phù hợp.</li>';
    }
    $('[data-compare-pick-q]').addEventListener('input', function (e) { pick(e.target.value); });
    render();
  }

  /* ---------------------------------------------------------------- cart */
  function voucherUi(scope, getTotals, onChange) {
    var f = $('[data-voucher-form]', scope); if (!f) return;
    var input = $('[data-voucher-input]', f), msg = $('[data-voucher-msg]', scope);
    input.value = X.get('voucher', '') || '';
    function show() {
      var t = getTotals();
      msg.className = 'voucher-msg ' + (t.promo ? 'ok' : t.msg ? 'bad' : '');
      msg.textContent = t.promo ? 'Đã áp dụng ' + t.promo.code + ': ' + t.promo.label : t.msg;
    }
    f.addEventListener('submit', function (e) { e.preventDefault(); X.set('voucher', input.value.trim().toUpperCase()); onChange(); show(); });
    $$('[data-voucher-use]', scope).forEach(function (b) { b.addEventListener('click', function () { input.value = b.dataset.voucherUse; X.set('voucher', b.dataset.voucherUse); onChange(); show(); }); });
    if (input.value) show();
  }
  function initCart() {
    var pg = $('[data-cart-page]'); if (!pg) return;
    function selected() { return X.cart().filter(function (l) { return l.sel !== false; }); }
    function tot() { return X.totals(selected(), { code: X.get('voucher', '') }); }
    function render() {
      var c = X.cart();
      $('[data-cart-full]').hidden = !c.length; $('[data-cart-empty]').hidden = !!c.length;
      $('[data-cart-n]').textContent = c.length ? '(' + X.cartCount() + ' sản phẩm)' : '';
      if (!c.length) return;
      $('[data-cart-list]').innerHTML = c.map(function (l) {
        var x = X.variant(l.sku), u = X.unitPrice(l.sku), max = Math.min(5, x.stock);
        return '<li class="cart-row" data-line="' + l.sku + '"><input type="checkbox" data-line-sel ' + (l.sel !== false ? 'checked' : '') + ' aria-label="Chọn ' + esc(x.name) + '">' +
          '<img src="' + x.image + '" alt="" width="88" height="88"><div class="cart-info"><a href="' + R + 'p/' + x.p.id + '.html">' + esc(x.name) + '</a><small>' + esc(x.label) + '</small>' +
          (x.stock <= 0 ? '<span class="warn">Hết hàng, bỏ chọn để đặt các sản phẩm khác</span>' : x.stock <= 3 ? '<span class="warn">Chỉ còn ' + x.stock + ' sản phẩm</span>' : '') +
          '<div class="cart-links"><button type="button" data-line-fav>Lưu vào yêu thích</button><button type="button" data-line-del>Xóa</button></div></div>' +
          '<div class="cart-side"><span class="price"><strong>' + vnd(u * l.qty) + '</strong>' + (x.old && x.old > u ? '<s>' + vnd(x.old * l.qty) + '</s>' : '') + '</span>' +
          '<div class="qty"><button type="button" data-qty="-1" aria-label="Giảm số lượng"' + (l.qty <= 1 ? ' disabled' : '') + '>' + X.icon('minus', 'i-sm') + '</button><input type="number" min="1" max="' + max + '" value="' + l.qty + '" aria-label="Số lượng" data-qty-input><button type="button" data-qty="1" aria-label="Tăng số lượng"' + (l.qty >= max ? ' disabled' : '') + '>' + X.icon('plus', 'i-sm') + '</button></div></div></li>';
      }).join('');
      var t = tot();
      $('[data-totals]', pg).innerHTML = X.totalsHtml(t, true);
      var std = (S.shipping || [])[0], fs = $('[data-free-ship]');
      if (std && std.free_over) {
        fs.hidden = false;
        var left = std.free_over - t.subtotal;
        $('[data-free-ship-text]').innerHTML = left > 0 ? 'Mua thêm <b>' + vnd(left) + '</b> để được miễn phí ' + esc(std.name.toLowerCase()) : 'Đơn hàng được miễn phí ' + esc(std.name.toLowerCase());
        $('i', fs).style.setProperty('--w', Math.min(100, t.subtotal * 100 / std.free_over) + '%');
      }
      var link = $('[data-checkout-link]'), none = !selected().length || selected().some(function (l) { return X.variant(l.sku).stock <= 0; });
      link.setAttribute('aria-disabled', none ? 'true' : 'false');
    }
    pg.addEventListener('click', function (e) {
      var row = e.target.closest('[data-line]'); if (!row) return;
      var c = X.cart(), l = c.filter(function (x) { return x.sku === row.dataset.line; })[0];
      var q = e.target.closest('[data-qty]');
      if (q) { var x = X.variant(l.sku); l.qty = Math.max(1, Math.min(Math.min(5, x.stock), l.qty + +q.dataset.qty)); X.saveCart(c); }
      if (e.target.closest('[data-line-del]')) { X.saveCart(c.filter(function (x) { return x !== l; })); X.toast(X.icon('trash') + 'Đã xóa ' + esc(X.variant(l.sku).name) + ' khỏi giỏ'); }
      if (e.target.closest('[data-line-fav]')) { var fav = X.get('fav', []); if (fav.indexOf(l.id) < 0) fav.unshift(l.id); X.set('fav', fav); X.saveCart(c.filter(function (x) { return x !== l; })); X.toast(X.icon('heart') + 'Đã chuyển vào yêu thích'); }
      render();
    });
    pg.addEventListener('change', function (e) {
      var row = e.target.closest('[data-line]'); if (!row) return;
      var c = X.cart(), l = c.filter(function (x) { return x.sku === row.dataset.line; })[0];
      if (e.target.matches('[data-line-sel]')) l.sel = e.target.checked;
      if (e.target.matches('[data-qty-input]')) { var x = X.variant(l.sku); l.qty = Math.max(1, Math.min(Math.min(5, x.stock), +e.target.value || 1)); }
      X.saveCart(c); render();
    });
    $('[data-checkout-link]').addEventListener('click', function (e) {
      if (this.getAttribute('aria-disabled') === 'true') { e.preventDefault(); X.toast(X.icon('alert') + 'Chọn ít nhất một sản phẩm còn hàng để đặt.'); }
    });
    voucherUi(pg, tot, render);
    render();
  }

  /* ---------------------------------------------------------------- checkout */
  function fillProvinces(sel, value) {
    if (!sel || sel.options.length > 1) return;
    S.provinces.forEach(function (p) { var o = document.createElement('option'); o.textContent = p; sel.appendChild(o); });
    if (value) sel.value = value;
  }
  function initCheckout() {
    var pg = $('[data-checkout]'); if (!pg) return;
    var lines = X.cart().filter(function (l) { return l.sel !== false && X.variant(l.sku).stock > 0; });
    if (!lines.length) { $('[data-co-empty]').hidden = false; return; }
    $('[data-co-wrap]').hidden = false;
    var form = $('[data-co-form]'), u = X.user(), addr = (X.get('addresses', []).filter(function (a) { return a.default; })[0] || X.get('addresses', [])[0]);
    fillProvinces($('[data-province]', form), addr && addr.province);
    if (u) { form.name.value = u.name || ''; form.phone.value = u.phone || ''; form.email.value = u.email || ''; $('[data-co-login]').hidden = true; }
    if (addr) { form.ward.value = addr.ward || ''; form.street.value = addr.street || ''; }
    var pref = X.get('pay_pref', null);
    if (pref) { var pr = $('input[name=payment][value="' + pref + '"]', form); if (pr) pr.checked = true; X.set('pay_pref', null); }
    function receive() { return ($('input[name=receive]:checked', form) || {}).value || 'ship'; }
    function method() { var id = ($('input[name=shipping]:checked', form) || {}).value; return (S.shipping || []).filter(function (s) { return s.id === id; })[0]; }
    function tot() { return X.totals(lines, { code: X.get('voucher', ''), shipping: method(), receive: receive() }); }
    function render() {
      $('[data-co-count]').textContent = '(' + lines.reduce(function (n, l) { return n + l.qty; }, 0) + ' sản phẩm)';
      $('[data-co-items]').innerHTML = lines.map(function (l) {
        var x = X.variant(l.sku);
        return '<li><img src="' + x.image + '" alt=""><span>' + esc(x.name) + '<small>' + esc(x.label) + ' x ' + l.qty + '</small></span><b>' + vnd(X.unitPrice(l.sku) * l.qty) + '</b></li>';
      }).join('');
      var pick = receive() === 'pickup';
      $('[data-co-ship]').hidden = pick;
      var pu = $('[data-co-pickup]'); if (pu) pu.hidden = !pick;
      $$('[data-co-ship] [required], [data-co-ship] [data-req]').forEach(function (i) { i.dataset.req = 1; i.required = !pick; });
      // express delivery only in listed cities
      var prov = form.province.value;
      $$('[data-ship-method]').forEach(function (lab) {
        var cities = lab.dataset.cities ? lab.dataset.cities.split('|') : null, ok = !cities || !prov || cities.indexOf(prov) >= 0;
        lab.classList.toggle('is-disabled', !ok); $('input', lab).disabled = !ok;
        if (!ok && $('input', lab).checked) { var first = $('[data-ship-method] input:not([disabled])'); if (first) first.checked = true; }
        var fee = $('[data-ship-fee]', lab), sub = tot().subtotal, free = +fee.dataset.freeOver;
        fee.textContent = (+fee.dataset.shipFee === 0 || (free && sub >= free)) ? 'Miễn phí' : vnd(+fee.dataset.shipFee);
      });
      $('[data-totals]', pg).innerHTML = X.totalsHtml(tot(), true);
    }
    form.addEventListener('change', function (e) {
      if (e.target.matches('[data-invoice-toggle]')) $('[data-invoice]').hidden = !e.target.checked;
      render();
    });
    voucherUi($('.co-summary'), tot, render);
    function invalid(el, msg) {
      el.setAttribute('aria-invalid', 'true');
      var f = el.closest('.field'), m = f && $('.err', f);
      if (f && !m) { m = document.createElement('small'); m.className = 'err'; f.appendChild(m); }
      if (m) m.textContent = msg;
    }
    form.addEventListener('submit', function (e) {
      e.preventDefault();
      $$('[aria-invalid]', form).forEach(function (el) { el.removeAttribute('aria-invalid'); });
      $$('.err', form).forEach(function (el) { el.remove(); });
      var bad = null;
      $$('input,select,textarea', form).forEach(function (el) {
        if (el.closest('[hidden]') || el.disabled) return;
        if (!el.checkValidity()) {
          invalid(el, el.validity.valueMissing ? 'Vui lòng nhập thông tin này' : el.name === 'phone' ? 'Số điện thoại gồm 10 số, bắt đầu bằng 03, 05, 07, 08 hoặc 09' : 'Thông tin chưa đúng định dạng');
          bad = bad || el;
        }
      });
      if (bad) { bad.focus(); return; }
      var t = tot(), m = method(), pay = (S.payments || []).filter(function (p) { return p.id === form.payment.value; })[0], rc = receive();
      var store = rc === 'pickup' ? (S.stores || []).filter(function (s) { return s.id === form.store.value; })[0] : null;
      var now = new Date().toISOString();
      var order = {
        code: X.nextCode(), created: now, status: 'pending',
        customer: { title: form.title.value, name: form.name.value.trim(), phone: form.phone.value.replace(/\s/g, ''), email: form.email.value.trim() },
        receive: rc, address: rc === 'ship' ? { province: form.province.value, ward: form.ward.value.trim(), street: form.street.value.trim() } : null,
        store: store ? { id: store.id, name: store.name, address: store.address } : null,
        shipping: rc === 'ship' && m ? { id: m.id, name: m.name, eta: m.eta, fee: t.shipping } : null,
        payment: { id: pay.id, name: pay.name }, paid: false, note: form.note.value.trim(),
        invoice: form.invoice.checked ? { company: form.company.value, tax_code: form.tax_code.value, address: form.company_address.value } : null,
        items: lines.map(function (l) { var x = X.variant(l.sku); return { id: x.p.id, sku: l.sku, name: x.name, variant: x.label, price: X.unitPrice(l.sku), qty: l.qty, image: x.p.image }; }),
        subtotal: t.subtotal, discount: t.discount, voucher: t.promo ? t.promo.code : null, shipping_fee: t.shipping || 0, total: t.total,
        history: [{ status: 'pending', at: now, note: 'Khách đặt hàng trên website' }], channel: 'Website'
      };
      var list = X.orders(); list.unshift(order); X.saveOrders(list);
      var sold = X.get('sold', {}); order.items.forEach(function (it) { sold[it.sku] = (sold[it.sku] || 0) + it.qty; }); X.set('sold', sold);
      X.saveCart(X.cart().filter(function (l) { return !lines.some(function (x) { return x.sku === l.sku; }); }));
      X.set('voucher', '');
      if (rc === 'ship') {
        var book = X.get('addresses', []);
        if (!book.some(function (a) { return a.street === order.address.street && a.ward === order.address.ward; })) {
          book.push({ name: order.customer.name, phone: order.customer.phone, province: order.address.province, ward: order.address.ward, street: order.address.street, default: !book.length });
          X.set('addresses', book);
        }
      }
      var mine = X.get('my_orders', []); mine.unshift(order.code); X.set('my_orders', mine);
      location.href = 'order.html?code=' + order.code;
    });
    render();
  }

  /* ---------------------------------------------------------------- order views */
  var STEPS = [['pending', 'Đã đặt hàng'], ['confirmed', 'Đã xác nhận'], ['packing', 'Đang chuẩn bị'], ['shipping', 'Đang giao'], ['delivered', 'Đã giao']];
  function eta(o) {
    if (o.receive === 'pickup') return 'Nhận tại cửa hàng';
    if (o.status === 'delivered') return 'Đã giao';
    var d = new Date(o.created), days = /2 giờ/.test((o.shipping || {}).eta || '') ? 0 : 3;
    d.setDate(d.getDate() + days);
    return days ? 'Trước ' + X.dayText(d) : 'Trong ngày ' + X.dayText(d);
  }
  function itemsHtml(o) {
    return o.items.map(function (it) {
      return '<li><img src="' + R + it.image + '" alt=""><span><a href="' + R + 'p/' + it.id + '.html">' + esc(it.name) + '</a><small>' + esc(it.variant) + ' x ' + it.qty + '</small></span><b>' + vnd(it.price * it.qty) + '</b></li>';
    }).join('');
  }
  function orderTotals(o) {
    return '<dt>Tạm tính</dt><dd>' + vnd(o.subtotal) + '</dd><dt>Phí giao hàng</dt><dd>' + (o.shipping_fee ? vnd(o.shipping_fee) : 'Miễn phí') + '</dd>' +
      (o.discount ? '<dt>Mã ' + esc(o.voucher || '') + '</dt><dd class="disc">-' + vnd(o.discount) + '</dd>' : '') +
      '<dt class="grand">Tổng cộng</dt><dd class="grand">' + vnd(o.total) + '</dd>';
  }
  function trackerHtml(o) {
    if (o.status === 'cancelled' || o.status === 'returned') {
      return '<li class="is-done"><span class="dot">' + X.icon('check', 'i-sm') + '</span>Đã đặt hàng</li><li class="is-on"><span class="dot">' + X.icon('x', 'i-sm') + '</span>' + esc(X.STATUS[o.status]) + '</li>';
    }
    var idx = STEPS.map(function (s) { return s[0]; }).indexOf(o.status);
    return STEPS.map(function (s, i) {
      var label = s[0] === 'shipping' && o.receive === 'pickup' ? 'Sẵn sàng tại cửa hàng' : s[0] === 'delivered' && o.receive === 'pickup' ? 'Đã nhận hàng' : s[1];
      return '<li class="' + (i < idx ? 'is-done' : i === idx ? 'is-on' : '') + '"><span class="dot">' + (i <= idx ? X.icon('check', 'i-sm') : i + 1) + '</span>' + esc(label) + '</li>';
    }).join('');
  }
  function timelineHtml(o) {
    return (o.history || []).slice().reverse().map(function (h) {
      return '<li><b>' + esc(X.STATUS[h.status] || h.status) + '</b>' + (h.note ? '<br>' + esc(h.note) : '') + '<small>' + X.dateText(h.at) + '</small></li>';
    }).join('');
  }
  function shipHtml(o) {
    if (o.receive === 'pickup' && o.store) return '<dl class="kv"><dt>Nhận tại</dt><dd><b>' + esc(o.store.name) + '</b><br>' + esc(o.store.address) + '</dd><dt>Người nhận</dt><dd>' + esc(o.customer.name) + ', ' + esc(o.customer.phone) + '</dd></dl>';
    var a = o.address || {};
    return '<dl class="kv"><dt>Người nhận</dt><dd>' + esc(o.customer.title + ' ' + o.customer.name) + ', ' + esc(o.customer.phone) + '</dd><dt>Địa chỉ</dt><dd>' + esc([a.street, a.ward, a.province].filter(Boolean).join(', ')) + '</dd>' +
      (o.shipping ? '<dt>Giao hàng</dt><dd>' + esc(o.shipping.name) + ' (' + esc(o.shipping.eta) + ')</dd>' : '') + '<dt>Dự kiến</dt><dd>' + esc(eta(o)) + '</dd>' + (o.note ? '<dt>Ghi chú</dt><dd>' + esc(o.note) + '</dd>' : '') + '</dl>';
  }
  function badge(o) { return '<span class="badge ' + (X.STATUS_CLASS[o.status] || '') + '">' + esc(X.STATUS[o.status] || o.status) + '</span>'; }

  function initOrderPage() {
    var pg = $('[data-order-page]'); if (!pg) return;
    var o = X.findOrder(params.get('code'));
    if (!o) { $('[data-order-missing]').hidden = false; return; }
    $('[data-order-body]').hidden = false;
    $('[data-o-name]').textContent = o.customer.title + ' ' + o.customer.name;
    $('[data-o-code]').textContent = o.code;
    $('[data-o-items]').innerHTML = itemsHtml(o);
    $('[data-o-totals]').innerHTML = orderTotals(o);
    $('[data-o-ship]').innerHTML = shipHtml(o);
    var pay = (S.payments || []).filter(function (p) { return p.id === o.payment.id; })[0] || {};
    var h = '<p><b>' + esc(o.payment.name) + '</b></p>';
    if (pay.bank) {
      h += '<div class="bank"><dl class="kv"><dt>Ngân hàng</dt><dd>' + esc(pay.bank.name) + '</dd><dt>Số tài khoản</dt><dd><b>' + esc(pay.bank.account) + '</b></dd><dt>Chủ tài khoản</dt><dd>' + esc(pay.bank.holder) + '</dd><dt>Số tiền</dt><dd><b>' + vnd(o.total) + '</b></dd><dt>Nội dung</dt><dd><b>' + esc(o.code) + '</b></dd></dl></div>';
    } else if (o.payment.id === 'cod') h += '<p class="muted">Chuẩn bị ' + vnd(o.total) + ' khi nhận hàng.</p>';
    else if (o.payment.id === 'installment') h += '<p class="muted">Nhân viên gọi xác nhận kỳ hạn trả góp trước khi giao.</p>';
    else h += '<p class="muted">' + esc(pay.note || '') + '</p>';
    $('[data-o-pay]').innerHTML = h;
    $('[data-o-track]').href = 'track.html?code=' + o.code + '&phone=' + encodeURIComponent(o.customer.phone);
  }

  function initTrack() {
    var pg = $('[data-track-page]'); if (!pg) return;
    var f = $('[data-track-form]');
    function show(code, phone) {
      var o = X.findOrder(code);
      var ok = o && o.customer.phone.replace(/\D/g, '').slice(-9) === String(phone).replace(/\D/g, '').slice(-9);
      $('[data-track-error]').hidden = !!ok; $('[data-track-result]').hidden = !ok;
      if (!ok) return;
      $('[data-t-code]').textContent = o.code; $('[data-t-date]').textContent = X.dateText(o.created);
      $('[data-t-eta]').textContent = eta(o); $('[data-t-status]').outerHTML = badge(o).replace('class="badge', 'data-t-status class="badge');
      var st = $('[data-t-steps]'); st.innerHTML = trackerHtml(o); st.classList.toggle('is-cancel', o.status === 'cancelled');
      $('[data-t-items]').innerHTML = itemsHtml(o); $('[data-t-totals]').innerHTML = orderTotals(o);
      $('[data-t-timeline]').innerHTML = timelineHtml(o);
    }
    f.addEventListener('submit', function (e) { e.preventDefault(); if (f.reportValidity()) show(f.code.value, f.phone.value); });
    if (params.get('code')) { f.code.value = params.get('code'); f.phone.value = params.get('phone') || ''; if (f.phone.value) show(f.code.value, f.phone.value); }
  }

  /* ---------------------------------------------------------------- stores, deals */
  function initStores() {
    var pg = $('[data-stores-page]'); if (!pg) return;
    var map = $('[data-store-map]');
    function openState(li) {
      var m = (li.dataset.hours || '').match(/(\d{1,2})[:h](\d{2})\s*-\s*(\d{1,2})[:h](\d{2})/), el = $('[data-open-state]', li);
      if (!m) return;
      var now = new Date(), t = now.getHours() * 60 + now.getMinutes(), open = t >= +m[1] * 60 + +m[2] && t < +m[3] * 60 + +m[4];
      el.textContent = open ? 'Đang mở cửa' : 'Đã đóng cửa'; el.className = 'open-state ' + (open ? 'is-open' : 'is-closed');
    }
    $$('[data-store]').forEach(function (li) {
      openState(li);
      $('[data-store-pick]', li).addEventListener('click', function () {
        $$('[data-store-pick]').forEach(function (b) { b.setAttribute('aria-pressed', b === this ? 'true' : 'false'); }, this);
        map.src = 'https://www.google.com/maps?q=' + encodeURIComponent(li.dataset.map) + '&output=embed';
      });
    });
    $('[data-store-city]').addEventListener('change', function (e) {
      $$('[data-store]').forEach(function (li) { li.hidden = e.target.value && li.dataset.city !== e.target.value; });
    });
  }
  function initCopy() {
    $$('[data-copy]').forEach(function (b) {
      b.addEventListener('click', function () {
        var done = function () { X.toast(X.icon('check') + 'Đã sao chép mã ' + esc(b.dataset.copy)); };
        if (navigator.clipboard) navigator.clipboard.writeText(b.dataset.copy).then(done, done); else done();
        X.set('voucher', b.dataset.copy);
      });
    });
  }

  /* ---------------------------------------------------------------- account */
  function myOrders() {
    var mine = X.get('my_orders', []), u = X.user();
    return X.orders().filter(function (o) { return mine.indexOf(o.code) >= 0 || (u && o.customer.phone === u.phone); });
  }
  function orderRow(o) {
    var thumbs = o.items.slice(0, 3).map(function (it) { return '<img src="' + R + it.image + '" alt="">'; }).join('');
    return '<li><div class="thumbs">' + thumbs + '</div><div class="o-info"><b>' + esc(o.items[0].name) + (o.items.length > 1 ? ' và ' + (o.items.length - 1) + ' sản phẩm khác' : '') + '</b><small>Mã ' + esc(o.code) + ', ' + X.dateText(o.created) + '</small></div>' +
      '<div class="o-side">' + badge(o) + '<b>' + vnd(o.total) + '</b><div class="o-act"><a href="' + R + 'account/order.html?code=' + o.code + '">Chi tiết</a><a href="' + R + 'track.html?code=' + o.code + '&phone=' + encodeURIComponent(o.customer.phone) + '">Theo dõi</a></div></div></li>';
  }
  function initAccount() {
    var pg = $('[data-account]');
    var auth = $('[data-auth]');
    if (auth) {
      $$('[data-auth-tab]').forEach(function (t) {
        t.addEventListener('click', function () {
          $$('[data-auth-tab]').forEach(function (x) { x.setAttribute('aria-selected', x === t ? 'true' : 'false'); });
          $$('[data-auth-form]').forEach(function (f) { f.hidden = f.dataset.authForm !== t.dataset.authTab; });
        });
      });
      var next = params.get('next') === 'checkout' ? R + 'checkout.html' : R + 'account/index.html';
      var lf = $('[data-auth-form=login]'), rf = $('[data-auth-form=register]');
      lf.addEventListener('submit', function (e) {
        e.preventDefault(); if (!lf.reportValidity()) return;
        var id = lf.id.value.trim(), users = X.get('users', []);
        var u = users.filter(function (x) { return (x.phone === id.replace(/\s/g, '') || x.email === id) && x.pw === btoa(unescape(encodeURIComponent(lf.password.value))); })[0];
        if (!u) { var er = $('[data-auth-error]'); er.hidden = false; er.textContent = 'Sai số điện thoại, email hoặc mật khẩu.'; return; }
        X.set('user', { name: u.name, phone: u.phone, email: u.email }); location.href = next;
      });
      rf.addEventListener('submit', function (e) {
        e.preventDefault(); if (!rf.reportValidity()) return;
        var users = X.get('users', []), phone = rf.phone.value.replace(/\s/g, '');
        if (users.some(function (x) { return x.phone === phone; })) { X.toast(X.icon('alert') + 'Số điện thoại đã có tài khoản. Hãy đăng nhập.'); return; }
        users.push({ name: rf.name.value.trim(), phone: phone, email: rf.email.value.trim(), pw: btoa(unescape(encodeURIComponent(rf.password.value))) });
        X.set('users', users); X.set('user', { name: rf.name.value.trim(), phone: phone, email: rf.email.value.trim() });
        location.href = next;
      });
      var fg = $('[data-forgot]'); if (fg) fg.addEventListener('click', function (e) { e.preventDefault(); X.toast(X.icon('alert') + 'Gọi ' + esc(S.shop.hotline) + ' để lấy lại mật khẩu.'); });
      return;
    }
    if (!pg) return;
    var u = X.user();
    if (!u) { location.replace(R + 'account/login.html'); return; }
    $$('[data-user-name]').forEach(function (el) { el.textContent = u.name; });
    $$('[data-user-phone]').forEach(function (el) { el.textContent = u.phone; });
    $$('[data-user-initial]').forEach(function (el) { el.textContent = u.name.trim().split(' ').slice(-1)[0][0]; });
    $$('[data-logout]').forEach(function (b) { b.addEventListener('click', function () { X.set('user', null); location.href = R + 'index.html'; }); });
    var mine = myOrders(), page = pg.dataset.account;
    if (page === 'index') {
      $('[data-stat-orders]').textContent = mine.length;
      $('[data-stat-shipping]').textContent = mine.filter(function (o) { return ['confirmed', 'packing', 'shipping'].indexOf(o.status) >= 0; }).length;
      $('[data-stat-fav]').textContent = X.get('fav', []).length;
      $('[data-stat-spent]').textContent = vnd(mine.filter(function (o) { return o.status === 'delivered'; }).reduce(function (s, o) { return s + o.total; }, 0));
      $('[data-orders-recent]').innerHTML = mine.slice(0, 3).map(orderRow).join('') || '<li class="muted">Chưa có đơn hàng.</li>';
      var a = X.get('addresses', []).filter(function (x) { return x.default; })[0];
      if (a) $('[data-default-address]').innerHTML = '<p><b>' + esc(a.name) + '</b>, ' + esc(a.phone) + '<br>' + esc([a.street, a.ward, a.province].join(', ')) + '</p>';
    }
    if (page === 'orders') {
      var tab = params.get('tab') || '';
      function draw() {
        var list = mine.filter(function (o) { return !tab || o.status === tab || (tab === 'shipping' && ['confirmed', 'packing', 'shipping'].indexOf(o.status) >= 0); });
        $('[data-orders-list]').innerHTML = list.map(orderRow).join('');
        $('[data-orders-empty]').hidden = list.length > 0;
        $$('[data-tab]').forEach(function (b) { b.setAttribute('aria-selected', b.dataset.tab === tab ? 'true' : 'false'); });
      }
      $$('[data-tab]').forEach(function (b) { b.addEventListener('click', function () { tab = b.dataset.tab; draw(); }); });
      draw();
    }
    if (page === 'order') {
      var o = X.findOrder(params.get('code'));
      if (!o) { $('[data-acc-order]').hidden = true; $('[data-order-missing]').hidden = false; return; }
      $('[data-t-code]').textContent = o.code; $('[data-t-status]').outerHTML = badge(o);
      $('[data-t-date]').textContent = 'Đặt lúc ' + X.dateText(o.created) + ', ' + o.payment.name;
      $('[data-t-steps]').innerHTML = trackerHtml(o); $('[data-t-items]').innerHTML = itemsHtml(o);
      $('[data-t-totals]').innerHTML = orderTotals(o); $('[data-t-ship]').innerHTML = shipHtml(o); $('[data-t-timeline]').innerHTML = timelineHtml(o);
      $('[data-reorder]').addEventListener('click', function () { o.items.forEach(function (it) { X.addToCart(it.sku, it.qty); }); location.href = R + 'cart.html'; });
      var cb = $('[data-cancel-order]'); cb.hidden = o.status !== 'pending';
      cb.addEventListener('click', function () {
        if (!confirm('Hủy đơn ' + o.code + '?')) return;
        X.updateOrder(o.code, function (x) { x.status = 'cancelled'; x.history.push({ status: 'cancelled', at: new Date().toISOString(), note: 'Khách hủy trên website' }); });
        location.reload();
      });
    }
    if (page === 'wishlist') {
      var fav = X.get('fav', []).map(function (id) { return X.byId[id]; }).filter(Boolean);
      $('[data-wishlist-grid]').innerHTML = fav.map(cardHtml).join('');
      $('[data-wishlist-empty]').hidden = fav.length > 0;
      document.addEventListener('shop:change', function (e) { if (e.detail.key === 'fav') location.reload(); });
    }
    if (page === 'addresses') {
      var f = $('[data-addr-form]');
      fillProvinces($('[data-province]', f));
      function drawA() {
        var list = X.get('addresses', []);
        $('[data-addr-list]').innerHTML = list.map(function (a, i) {
          return '<li>' + (a.default ? '<span class="badge badge-ok">Mặc định</span>' : '') + '<b>' + esc(a.name) + '</b><span>' + esc(a.phone) + '</span><span>' + esc([a.street, a.ward, a.province].join(', ')) + '</span><div class="acts">' +
            (a.default ? '' : '<button type="button" data-addr-def="' + i + '">Đặt mặc định</button>') + '<button type="button" data-addr-del="' + i + '">Xóa</button></div></li>';
        }).join('') || '<li class="muted">Chưa có địa chỉ.</li>';
      }
      $('[data-addr-new]').addEventListener('click', function () { f.hidden = false; f.name.value = u.name; f.phone.value = u.phone; f.name.focus(); });
      $('[data-addr-cancel]').addEventListener('click', function () { f.hidden = true; });
      f.addEventListener('submit', function (e) {
        e.preventDefault(); if (!f.reportValidity()) return;
        var list = X.get('addresses', []), d = f.default.checked || !list.length;
        if (d) list.forEach(function (a) { a.default = false; });
        list.push({ name: f.name.value, phone: f.phone.value, province: f.province.value, ward: f.ward.value, street: f.street.value, default: d });
        X.set('addresses', list); f.reset(); f.hidden = true; drawA();
      });
      $('[data-addr-list]').addEventListener('click', function (e) {
        var list = X.get('addresses', []), d = e.target.closest('[data-addr-def]'), x = e.target.closest('[data-addr-del]');
        if (d) { list.forEach(function (a, i) { a.default = i === +d.dataset.addrDef; }); }
        if (x) { list.splice(+x.dataset.addrDel, 1); if (list.length && !list.some(function (a) { return a.default; })) list[0].default = true; }
        X.set('addresses', list); drawA();
      });
      drawA();
    }
  }

  function initClamp() {
    $$('[data-clamp]').forEach(function (el) {
      var btn = $('[data-clamp-toggle]', el);
      if (el.scrollHeight > 340) { el.classList.add('is-clamped'); btn.hidden = false; }
      btn.addEventListener('click', function () { var c = el.classList.toggle('is-clamped'); btn.firstChild.textContent = c ? 'Xem thêm' : 'Thu gọn'; });
    });
  }

  /* ---------------------------------------------------------------- boot */
  function boot() {
    initHeader(); initCarousels(); initRails(); initFlash(); initMore(); initCardActions();
    initPlp(); initPdp(); initSearchPage(); initComparePage(); initCart(); initCheckout();
    initOrderPage(); initTrack(); initStores(); initCopy(); initAccount(); initClamp();
    // admin price, stock and visibility overrides on static cards
    var hidden = X.get('adm:hidden', {});
    $$('[data-pc]').forEach(function (c) { if (hidden[c.dataset.id]) c.remove(); });
    syncBadges();
    document.addEventListener('shop:change', syncBadges);
    addEventListener('storage', syncBadges);
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot); else boot();
})();
