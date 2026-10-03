/* Cửa hàng: identity (khách), catalog, smart-search, recommendations, personalized-content,
   commerce, shipping, activity-tracking, knowledge-base (trung tâm hỗ trợ), shopping-assistant (chat). */
/* eslint-disable */
(function () {
  const PK = window.PK;
  const D = PK.D;
  const {
    esc,
    money,
    stars,
    norm,
    route,
    act,
    form,
    change,
    input,
    note,
    toast,
    modal,
    closeModal,
    productCard,
    productImg,
  } = PK;

  // Dựng lại hash từ path + query (bỏ khóa có giá trị rỗng)
  function href(path, query) {
    const q = Object.entries(query || {}).filter(([, v]) => v !== '' && v != null);
    return '#' + path + (q.length ? '?' + new URLSearchParams(q).toString() : '');
  }
  const cartCount = () => PK.S.cart.reduce((s, l) => s + l.qty, 0);
  const persona = () =>
    PK.S.role === 'customer' || PK.S.recent.some((id) => PK.prod(id)?.cat === 'laptop-gaming')
      ? 'gaming'
      : 'general';
  const aiUsable = (tier) =>
    !PK.S.aiDown && (tier === 'high' ? PK.S.budgetPct < 120 : PK.S.budgetPct < 100);

  // ================= Layout cửa hàng =================
  PK.layouts.shop = function (html) {
    const S = PK.S;
    const user = PK.me();
    const cats = D.categories.filter((c) => !c.parent);
    return `
    <header class="shop-header">
      <div class="inner">
        <a class="logo" href="#/">pick<span>ory</span></a>
        <form class="search-box" data-form="search" role="search">
          <input name="q" placeholder="Tìm laptop, điện thoại... thử gõ: laptop choi game duoi 25 trieu" autocomplete="off"
            data-input="typeahead" aria-label="Tìm kiếm" role="combobox" aria-expanded="false" maxlength="200">
          <div id="ta" class="typeahead hidden"></div>
        </form>
        <span class="spacer"></span>
        <a class="icon-btn" href="#/help">Trợ giúp</a>
        ${S.role === 'customer' ? `<a class="icon-btn" href="#/orders">Đơn hàng</a><a class="icon-btn" href="#/account">${esc(user.name.split(' ').pop())}</a>` : `<a class="icon-btn" href="#/login">Đăng nhập</a>`}
        <a class="icon-btn" href="#/cart">Giỏ hàng <span class="count">${cartCount()}</span></a>
      </div>
      <nav class="nav-cats">${cats.map((c) => `<a href="#/c/${c.id}">${c.name}</a>`).join('')}<a href="#/help/chinh-sach-doi-tra">Đổi trả</a></nav>
    </header>
    <main class="shop">${html}</main>
    <footer class="footer"><div class="inner">
      <div><b>pickory</b><br>Cửa hàng đồ công nghệ có trợ lý AI.<br>Hotline 1900 0000</div>
      <div><b>Chính sách</b>${PK.S.kbDocs
        .filter((d) => d.type === 'policy' && d.status === 'published')
        .map((d) => `<a href="#/help/${d.slug}">${d.title}</a>`)
        .join('')}</div>
      <div><b>Hỗ trợ</b><a href="#/help">Trung tâm hỗ trợ</a><a href="#/orders">Tra cứu đơn hàng</a><a href="#" data-act="openConsent">Cài đặt quyền riêng tư</a></div>
    </div></footer>`;
  };
  PK.layouts.bare = (html) => html;

  PK.afterRender = function (layout, path) {
    const S = PK.S;
    let chatRoot = document.getElementById('chat-root');
    if (!chatRoot) {
      chatRoot = document.createElement('div');
      chatRoot.id = 'chat-root';
      document.body.appendChild(chatRoot);
    }
    if (layout === 'shop') renderChat();
    else chatRoot.innerHTML = '';
    let consentRoot = document.getElementById('consent-root');
    if (!consentRoot) {
      consentRoot = document.createElement('div');
      consentRoot.id = 'consent-root';
      document.body.appendChild(consentRoot);
    }
    consentRoot.innerHTML =
      layout === 'shop' && S.consent === null && path !== '/flows' ? consentBanner() : '';
    // Cuộn tới mục trong tài liệu khi đi từ trích dẫn [n]
    const { query } = PK.parseHash();
    if (query.a) {
      const el = document.getElementById(query.a);
      if (el) {
        el.scrollIntoView({ block: 'start' });
        el.classList.add('flash');
      }
    }
    if (window.PK.onPage) window.PK.onPage(path);
  };

  // ================= identity: banner đồng ý =================
  function consentBanner() {
    return `<div class="consent" role="dialog" aria-label="Đồng ý thu thập dữ liệu">
      <b>Pickory dùng dữ liệu để gợi ý đúng gu bạn</b>
      <p class="small muted" style="margin:6px 0 0">Nếu đồng ý, chúng tôi ghi nhận sản phẩm bạn xem và mua để cá nhân hóa gợi ý. Bạn đổi ý bất cứ lúc nào trong Tài khoản. <a href="#/help/quyen-rieng-tu">Chính sách</a></p>
      <div class="two"><button class="btn" data-act="consent" data-v="all">Đồng ý</button><button class="btn" data-act="consent" data-v="necessary">Chỉ cần thiết</button></div>
      <div style="text-align:center;margin-top:8px"><a href="#" class="small" data-act="openConsent">Tùy chỉnh</a></div>
    </div>`;
  }
  act('consent', (d) => {
    PK.S.consent = { personalization: d.v === 'all', marketing: d.v === 'all', at: PK.now() };
    toast(d.v === 'all' ? 'Đã bật cá nhân hóa' : 'Chỉ dùng dữ liệu cần thiết');
    PK.render();
  });
  act('openConsent', () => {
    const c = PK.S.consent || { personalization: false, marketing: false };
    modal(`<h2>Tùy chỉnh quyền riêng tư</h2>
      <form data-form="consentCustom" class="stack">
        <label class="switch"><input type="checkbox" checked disabled> <span><b>Cần thiết</b><br><span class="small muted">Giỏ hàng, đăng nhập, bảo mật. Luôn bật.</span></span></label>
        <label class="switch"><input type="checkbox" name="personalization" ${c.personalization ? 'checked' : ''}> <span><b>Cá nhân hóa</b><br><span class="small muted">Ghi nhận sản phẩm bạn xem để gợi ý "Dành cho bạn".</span></span></label>
        <label class="switch"><input type="checkbox" name="marketing" ${c.marketing ? 'checked' : ''}> <span><b>Tiếp thị</b><br><span class="small muted">Nhận ưu đãi phù hợp.</span></span></label>
        <div class="row"><button class="btn">Lưu lựa chọn</button><button type="button" class="btn secondary" data-act="closeModal">Hủy</button></div>
      </form>`);
    return false;
  });
  form('consentCustom', (data) => {
    PK.S.consent = {
      personalization: !!data.personalization,
      marketing: !!data.marketing,
      at: PK.now(),
    };
    if (!data.personalization) PK.S.recent = [];
    closeModal();
    toast('Đã lưu lựa chọn (ghi 1 bản ghi consent mới)');
    PK.render();
  });

  // ================= smart-search: ô tìm kiếm và gợi ý tức thì =================
  form('search', (data) => {
    const q = String(data.q || '').trim();
    if (q) PK.go('/search?q=' + encodeURIComponent(q));
  });
  input('typeahead', (el) => {
    const box = document.getElementById('ta');
    const t = norm(el.value.trim());
    if (t.length < 2) {
      box.classList.add('hidden');
      el.setAttribute('aria-expanded', 'false');
      return;
    }
    const prods = PK.S.products
      .filter((p) => p.status === 'active' && norm(p.name + ' ' + p.brand).includes(t))
      .slice(0, 5);
    const cats = D.categories.filter((c) => norm(c.name).includes(t)).slice(0, 3);
    const brands = D.brands.filter((b) => norm(b).includes(t)).slice(0, 3);
    let html = '';
    if (prods.length)
      html +=
        `<div class="grp">Sản phẩm</div>` +
        prods
          .map(
            (p) =>
              `<a href="#/p/${p.slug}">${productImg(p, 32)}<span>${esc(p.name)}</span><span class="spacer"></span><b class="small">${money(p.price)}</b></a>`,
          )
          .join('');
    if (cats.length)
      html +=
        `<div class="grp">Danh mục</div>` +
        cats.map((c) => `<a href="#/c/${c.id}">${c.name}</a>`).join('');
    if (brands.length)
      html +=
        `<div class="grp">Thương hiệu</div>` +
        brands.map((b) => `<a href="#/search?q=${encodeURIComponent(b)}">${b}</a>`).join('');
    if (!html)
      html = `<div class="small muted" style="padding:8px">Nhấn Enter để tìm "${esc(el.value)}"</div>`;
    html += `<div class="small muted" style="padding:6px 8px 2px">Gợi ý tức thì không dùng AI, trễ 150 ms</div>`;
    box.innerHTML = html;
    box.classList.remove('hidden');
    el.setAttribute('aria-expanded', 'true');
  });
  document.addEventListener('click', (e) => {
    const box = document.getElementById('ta');
    if (box && !e.target.closest('.search-box')) box.classList.add('hidden');
  });

  // Hiểu câu tìm kiếm tự nhiên: quy tắc trước, phần còn lại giả lập LLM
  function parseQuery(q, useLlm) {
    let t = ' ' + norm(q) + ' ';
    t = t
      .replace(/ lap /g, ' laptop ')
      .replace(/ dt /g, ' dien thoai ')
      .replace(/tai phone/g, 'tai nghe');
    const chips = [];
    const num = (s) => parseFloat(String(s).replace(',', '.'));
    const unit = (n, u) => (u === 'k' ? n * 1000 : n * 1000000);
    let m;
    if ((m = t.match(/(\d+[.,]?\d*)\s*-\s*(\d+[.,]?\d*)\s*(trieu|tr|cu)/)))
      chips.push({
        key: 'price',
        label: `${m[1]} đến ${m[2]} triệu`,
        min: unit(num(m[1])),
        max: unit(num(m[2])),
        src: 'rule',
      });
    else if ((m = t.match(/duoi\s*(\d+[.,]?\d*)\s*(trieu|tr|cu|k)/)))
      chips.push({
        key: 'price',
        label: `dưới ${m[1]} ${m[2] === 'k' ? 'nghìn' : 'triệu'}`,
        max: unit(num(m[1]), m[2]),
        src: 'rule',
      });
    else if ((m = t.match(/tren\s*(\d+[.,]?\d*)\s*(trieu|tr|cu|k)/)))
      chips.push({
        key: 'price',
        label: `trên ${m[1]} triệu`,
        min: unit(num(m[1]), m[2]),
        src: 'rule',
      });
    else if ((m = t.match(/tam\s*(\d+[.,]?\d*)\s*(trieu|tr|cu|k)/))) {
      const v = unit(num(m[1]), m[2]);
      chips.push({
        key: 'price',
        label: `khoảng ${m[1]} triệu (±15%)`,
        min: v * 0.85,
        max: v * 1.15,
        src: 'rule',
      });
    }
    if (/laptop/.test(t) && /(gaming|game)/.test(t))
      chips.push({
        key: 'cat',
        label: 'Laptop gaming',
        cat: 'laptop-gaming',
        src: /choi game/.test(t) ? 'llm' : 'rule',
      });
    else if (/laptop/.test(t) && /(van phong|sinh vien|mong nhe)/.test(t))
      chips.push({ key: 'cat', label: 'Laptop văn phòng', cat: 'laptop-van-phong', src: 'llm' });
    else if (/laptop/.test(t))
      chips.push({ key: 'cat', label: 'Laptop', cat: 'laptop', src: 'rule' });
    else if (/dien thoai|iphone|galaxy/.test(t))
      chips.push({ key: 'cat', label: 'Điện thoại', cat: 'dien-thoai', src: 'rule' });
    else if (/tai nghe/.test(t))
      chips.push({ key: 'cat', label: 'Tai nghe', cat: 'tai-nghe', src: 'rule' });
    else if (/man hinh/.test(t))
      chips.push({ key: 'cat', label: 'Màn hình', cat: 'man-hinh', src: 'rule' });
    else if (/chuot/.test(t)) chips.push({ key: 'cat', label: 'Chuột', cat: 'chuot', src: 'rule' });
    if (useLlm) {
      if (/chong on|may bay/.test(t))
        chips.push({ key: 'tag', label: 'Chống ồn', tag: 'chống ồn', src: 'llm' });
      if (/pin trau/.test(t))
        chips.push({ key: 'tag', label: 'Pin trâu', tag: 'pin trâu', src: 'llm' });
      if (/chup anh/.test(t))
        chips.push({ key: 'tag', label: 'Chụp ảnh đẹp', tag: 'chụp ảnh', src: 'llm' });
      if (/nhe|mang di hoc/.test(t) && /laptop/.test(t))
        chips.push({ key: 'light', label: 'Nhẹ dưới 2,3 kg', src: 'llm' });
    }
    return chips;
  }
  function applyChips(list, chips) {
    return list.filter((p) =>
      chips.every((c) => {
        if (c.key === 'price')
          return (c.min == null || p.price >= c.min) && (c.max == null || p.price <= c.max);
        if (c.key === 'cat') return PK.inCat(p, c.cat);
        if (c.key === 'tag') return p.tags.includes(c.tag);
        if (c.key === 'light') return (p.specs.weight || 9) < 2.3;
        return true;
      }),
    );
  }

  route('/search', (_, q) => {
    const S = PK.S;
    const query = String(q.q || '');
    const useLlm = aiUsable('high');
    const drop = (q.drop || '').split(',').filter(Boolean);
    const chips = parseQuery(query, useLlm).filter((c) => !drop.includes(c.key));
    const pool = S.products.filter((p) => p.status === 'active');
    let results;
    let relaxed = '';
    if (chips.length) {
      results = applyChips(pool, chips);
      if (!results.length) {
        const noAttr = chips.filter((c) => c.key === 'price' || c.key === 'cat');
        results = applyChips(pool, noAttr);
        relaxed = 'Đã nới tiêu chí phụ.';
        if (!results.length) {
          results = applyChips(
            pool,
            chips.filter((c) => c.key === 'cat'),
          ).sort((a, b) => a.price - b.price);
          const price = chips.find((c) => c.key === 'price');
          relaxed = `Không có ${chips.find((c) => c.key === 'cat')?.label.toLowerCase() || 'sản phẩm'} ${price ? price.label : ''}. Đây là các lựa chọn gần nhất.`;
        }
      }
    } else {
      const tokens = norm(query)
        .split(/\s+/)
        .filter((x) => x.length > 1);
      results = pool
        .map((p) => ({
          p,
          s: tokens.filter((tk) =>
            norm(
              p.name +
                ' ' +
                p.brand +
                ' ' +
                p.tags.join(' ') +
                ' ' +
                Object.values(p.specs).join(' '),
            ).includes(tk),
          ).length,
        }))
        .filter((x) => x.s > 0)
        .sort((a, b) => b.s - a.s)
        .map((x) => x.p);
    }
    const chipHtml = chips
      .map(
        (c) =>
          `<span class="chip">${esc(c.label)} <span class="src">${c.src === 'llm' ? 'AI hiểu' : 'quy tắc'}</span><a href="${href('/search', { q: query, drop: [...drop, c.key].join(',') })}" aria-label="Bỏ ${esc(c.label)}">×</a></span>`,
      )
      .join(' ');
    return `
      <div class="breadcrumb"><a href="#/">Trang chủ</a> / Tìm kiếm</div>
      <h1>Kết quả cho "${esc(query)}"</h1>
      ${chips.length ? `<div class="row" style="margin-bottom:12px"><span class="small muted">Đã hiểu:</span> ${chipHtml}</div>` : ''}
      ${drop.length ? `<p class="small"><a href="${href('/search', { q: query })}">Khôi phục bộ lọc đã bỏ</a></p>` : ''}
      ${relaxed ? `<div class="alert warn" style="margin-bottom:16px">${esc(relaxed)}</div>` : ''}
      ${!useLlm ? `<div class="alert" style="margin-bottom:16px">Tìm kiếm đang chạy chế độ dự phòng (full-text và quy tắc). Người dùng không thấy lỗi.</div>` : ''}
      <p class="muted">${results.length} sản phẩm</p>
      ${results.length ? `<div class="product-grid">${results.map((p) => productCard(p, { source: 'smart-search' })).join('')}</div>` : `<div class="card muted-bg"><b>Không tìm thấy sản phẩm phù hợp.</b><p class="muted small">Câu tìm được ghi vào danh sách "không có kết quả" cho admin xem.</p><a class="btn secondary" href="#/c/laptop">Xem tất cả laptop</a></div>`}
      ${note('Thử các câu: <a href="#/search?q=laptop%20choi%20game%20duoi%2025%20trieu">laptop choi game duoi 25 trieu</a>, <a href="#/search?q=tai%20nghe%20chong%20on%20di%20may%20bay">tai nghe chong on di may bay</a>, <a href="#/search?q=laptop%20gaming%20duoi%2015%20trieu">laptop gaming dưới 15 triệu</a> (nới tiêu chí), <a href="#/search?q=rog%20g16">rog g16</a>, <a href="#/search?q=dt%20pin%20trau%20tam%2010tr">đt pin trâu tầm 10tr</a>. Bật "AI lỗi" để thấy chế độ dự phòng.')}`;
  });

  // ================= Trang chủ: banner AI, "Dành cho bạn", đã xem gần đây =================
  function heroSlides() {
    const S = PK.S;
    const slides = [
      {
        title: 'Laptop gaming RTX 4060 giảm đến 4 triệu',
        sub: 'Giao nhanh 2 giờ nội thành TP. Hồ Chí Minh',
        link: '#/c/laptop-gaming',
        ai: false,
      },
      {
        title: 'Tai nghe chống ồn cho mọi chuyến đi',
        sub: 'Sony, Apple, JBL chính hãng, bảo hành 12 tháng',
        link: '#/search?q=tai%20nghe%20chong%20on',
        ai: false,
      },
    ];
    const approved = S.aiContents.filter((c) => c.kind === 'banner' && c.status === 'active');
    if (PK.personalized()) {
      approved
        .filter((c) => c.persona === persona())
        .forEach((c) =>
          slides.unshift({ title: c.title, sub: c.subtitle, link: '#/c/laptop-gaming', ai: true }),
        );
    }
    return slides;
  }
  function recRail(placement, items, title, reasonFn, source) {
    if (!items.length) return '';
    return `<section class="section"><div class="section-head"><h2>${title}</h2></div>
      <div class="rail">${items.map((p) => productCard(p, { source, reason: reasonFn ? reasonFn(p) : '' })).join('')}</div></section>`;
  }
  route('/', () => {
    const S = PK.S;
    const slides = heroSlides();
    const idx = S.heroIdx % slides.length;
    const s = slides[idx];
    const active = S.products.filter((p) => p.status === 'active' && p.stock > 0);
    const hasProfile = PK.personalized() && (S.role === 'customer' || S.recent.length > 0);
    let forYou;
    let variant;
    if (!hasProfile) {
      variant = PK.personalized() ? 'control/không có hồ sơ' : 'none (chưa đồng ý)';
      forYou = recRail(
        'for-you',
        [...active].sort((a, b) => b.reviewCount - a.reviewCount).slice(0, 8),
        'Bán chạy',
        null,
        'recommendations.for-you',
      );
    } else {
      const llm = aiUsable('low');
      variant = llm ? 'algo_llm' : 'algo (ngân sách hết hoặc AI lỗi: lý do theo mẫu)';
      const picks = active
        .filter((p) => p.cat === 'laptop-gaming' || p.tags.includes('gaming'))
        .filter((p) => !S.recent.slice(0, 1).includes(p.id))
        .slice(0, 8);
      const llmReason = {
        'laptop-gaming': 'Hợp gu RTX bạn hay xem, nhẹ hơn máy bạn vừa xem',
        chuot: 'Đi cùng laptop gaming, switch bền cho FPS',
        'tai-nghe': 'Mic khử ồn, hợp chơi game cùng đồng đội',
        'man-hinh': 'Màn 180Hz phát huy hết card RTX',
      };
      forYou = recRail(
        'for-you',
        picks,
        'Dành cho bạn',
        (p) =>
          llm
            ? llmReason[p.cat] || 'Vì bạn quan tâm đồ gaming'
            : `Vì bạn quan tâm ${PK.cat(p.cat).name}`,
        'recommendations.for-you',
      );
    }
    const recent = PK.personalized() ? S.recent.map(PK.prod).filter(Boolean).slice(0, 20) : [];
    const roots = D.categories.filter((c) => !c.parent);
    return `
      <div class="hero">
        <div>
          ${s.ai ? '<span class="badge ai" style="margin-bottom:12px">Gợi ý bởi AI</span>' : ''}
          <h1>${esc(s.title)}</h1><p style="opacity:.8">${esc(s.sub)}</p>
          <a class="btn" href="${s.link}">Xem ngay</a>
        </div>
        <div style="display:flex;justify-content:center">${productImg(S.products[s.ai ? 0 : idx === 1 ? 16 : 0], 200)}</div>
      </div>
      <div class="dots">${slides.map((_, i) => `<span class="${i === idx ? 'on' : ''}" data-act="hero" data-i="${i}" role="button" aria-label="Banner ${i + 1}"></span>`).join('')}</div>
      <section class="section"><div class="grid cols-4">${roots.map((c) => `<a class="card muted-bg" href="#/c/${c.id}" style="text-decoration:none"><b>${c.name}</b><div class="muted small">${S.products.filter((p) => PK.inCat(p, c.id) && p.status === 'active').length} sản phẩm</div></a>`).join('')}</div></section>
      ${forYou}
      ${note(`Biến thể gợi ý: <b>${esc(variant)}</b>. Chưa đồng ý cá nhân hóa thì thấy "Bán chạy". Đồng ý rồi xem vài laptop gaming (hoặc chọn vai trò Khách hàng) để thấy "Dành cho bạn" có lý do AI. Banner AI chỉ hiện sau khi admin duyệt ở <a href="#/admin/ai-content">Nội dung AI</a>.`)}
      ${recent.length ? recRail('recent', recent, 'Đã xem gần đây', null, 'category') : ''}`;
  });
  act('hero', (d) => {
    PK.S.heroIdx = Number(d.i);
    PK.render();
  });
  act('trackClick', (d) => {
    // activity-tracking: rec_click / product click có source (chỉ ghi khi đã đồng ý)
  });

  // ================= catalog: trang danh mục có bộ lọc =================
  const PRICE_RANGES = [
    ['', 'Tất cả'],
    ['0-10000000', 'Dưới 10 triệu'],
    ['10000000-20000000', '10 đến 20 triệu'],
    ['20000000-30000000', '20 đến 30 triệu'],
    ['30000000-999999999', 'Trên 30 triệu'],
  ];
  route('/c/:id', (p, q) => {
    const S = PK.S;
    const c = PK.cat(p.id);
    if (!c) return '<h1>Không tìm thấy danh mục</h1>';
    const base = S.products.filter((x) => PK.visible(x) && PK.inCat(x, c.id));
    const sel = {
      brand: (q.brand || '').split(',').filter(Boolean),
      price: q.price || '',
      ram: q.ram || '',
      stock: q.stock || '',
      sort: q.sort || 'newest',
    };
    let list = base.filter(
      (x) =>
        (!sel.brand.length || sel.brand.includes(x.brand)) &&
        (!sel.ram || String(x.specs.ram_gb) === sel.ram) &&
        (!sel.stock || x.stock > 0),
    );
    if (sel.price) {
      const [a, b] = sel.price.split('-').map(Number);
      list = list.filter((x) => x.price >= a && x.price <= b);
    }
    if (sel.sort === 'price_asc') list.sort((a, b) => a.price - b.price);
    if (sel.sort === 'price_desc') list.sort((a, b) => b.price - a.price);
    const brands = [...new Set(base.map((x) => x.brand))];
    const rams = [...new Set(base.map((x) => x.specs.ram_gb).filter(Boolean))].sort(
      (a, b) => a - b,
    );
    const link = (changes) => href('/c/' + c.id, Object.assign({}, q, changes));
    const toggleBrand = (b) =>
      (sel.brand.includes(b) ? sel.brand.filter((x) => x !== b) : [...sel.brand, b]).join(',');
    const parent = c.parent ? PK.cat(c.parent) : null;
    const children = D.categories.filter((x) => x.parent === c.id);
    return `
      <div class="breadcrumb"><a href="#/">Trang chủ</a> / ${parent ? `<a href="#/c/${parent.id}">${parent.name}</a> / ` : ''}${c.name}</div>
      <div class="row between"><h1>${c.name}</h1>
        <label class="row small">Sắp xếp <select style="width:auto" data-change="sort" data-base="${esc(link({ sort: '' }))}">
          ${[
            ['newest', 'Mới nhất'],
            ['price_asc', 'Giá tăng dần'],
            ['price_desc', 'Giá giảm dần'],
          ]
            .map(
              ([v, l]) => `<option value="${v}" ${sel.sort === v ? 'selected' : ''}>${l}</option>`,
            )
            .join('')}
        </select></label></div>
      ${children.length ? `<div class="row" style="margin-bottom:16px">${children.map((x) => `<a class="chip" href="#/c/${x.id}">${x.name}</a>`).join('')}</div>` : ''}
      <div class="layout-filter">
        <aside>
          <div class="filter-group"><h4>Thương hiệu</h4>${brands.map((b) => `<label><input type="checkbox" ${sel.brand.includes(b) ? 'checked' : ''} data-act="nav" data-href="${esc(link({ brand: toggleBrand(b) }))}"> ${b} <span class="muted small">${base.filter((x) => x.brand === b).length}</span></label>`).join('')}</div>
          <div class="filter-group"><h4>Khoảng giá</h4>${PRICE_RANGES.map(([v, l]) => `<label><input type="radio" name="pr" ${sel.price === v ? 'checked' : ''} data-act="nav" data-href="${esc(link({ price: v }))}"> ${l}</label>`).join('')}</div>
          ${rams.length ? `<div class="filter-group"><h4>RAM</h4>${rams.map((r) => `<label><input type="checkbox" ${sel.ram === String(r) ? 'checked' : ''} data-act="nav" data-href="${esc(link({ ram: sel.ram === String(r) ? '' : r }))}"> ${r}GB <span class="muted small">(${base.filter((x) => x.specs.ram_gb === r).length})</span></label>`).join('')}</div>` : ''}
          <div class="filter-group"><label><input type="checkbox" ${sel.stock ? 'checked' : ''} data-act="nav" data-href="${esc(link({ stock: sel.stock ? '' : '1' }))}"> Chỉ hiện còn hàng</label></div>
        </aside>
        <div>
          <p class="muted">${list.length} sản phẩm</p>
          ${list.length ? `<div class="product-grid">${list.map((x) => productCard(x)).join('')}</div>` : '<div class="card muted-bg">Không có sản phẩm khớp bộ lọc.</div>'}
          ${note('Bộ lọc nằm trên URL (chia sẻ được), số đếm theo từng giá trị thuộc tính. Sản phẩm "Ngừng kinh doanh" vẫn xem được nhưng không mua được; sản phẩm ẩn và nháp không hiện.')}
        </div>
      </div>`;
  });
  act('nav', (d) => {
    location.hash = d.href.slice(1);
  });
  change('sort', (el) => {
    const base = el.dataset.base;
    location.hash = (base + (base.includes('?') ? '&' : '?') + 'sort=' + el.value).slice(1);
  });

  // ================= catalog + personalized-content + recommendations: trang sản phẩm =================
  route('/p/:slug', (params, q) => {
    const S = PK.S;
    const p = PK.prodBySlug(params.slug);
    if (!p || p.status === 'hidden' || p.status === 'draft')
      return `<h1>404</h1><p>Sản phẩm không tồn tại hoặc đã bị ẩn.</p><a href="#/">Về trang chủ</a>`;
    if (PK.personalized() && S.recent[0] !== p.id)
      S.recent = [p.id, ...S.recent.filter((x) => x !== p.id)].slice(0, 20);
    const c = PK.cat(p.cat);
    const v = p.variants ? p.variants.find((x) => x.sku === q.v) || p.variants[0] : null;
    const price = v ? v.price : p.price;
    const compareAt = v ? v.compareAt : p.compareAt;
    const stock = v ? v.stock : p.stock;
    const disc = p.status === 'discontinued';
    // Bộ chọn biến thể theo từng trục (Dung lượng, Màu)
    let picker = '';
    if (p.variants) {
      const axes = Object.keys(p.variants[0].opts);
      picker = axes
        .map((axis) => {
          const values = [...new Set(p.variants.map((x) => x.opts[axis]))];
          return `<div class="small muted">${axis}</div><div class="opt-row">${values
            .map((val) => {
              const target =
                p.variants.find(
                  (x) =>
                    x.opts[axis] === val &&
                    axes.every((a) => a === axis || x.opts[a] === v.opts[a]),
                ) || p.variants.find((x) => x.opts[axis] === val);
              return `<a class="opt ${v.opts[axis] === val ? 'on' : ''}" href="#/p/${p.slug}?v=${target.sku}" style="text-decoration:none">${val}</a>`;
            })
            .join('')}</div>`;
        })
        .join('');
    }
    // Bảng thông số nhóm theo group
    const attrs = D.attributes[c.parent || c.id] || [];
    const groups = {};
    attrs.forEach((a) => {
      if (p.specs[a.key] === undefined) return;
      const val =
        typeof p.specs[a.key] === 'boolean'
          ? p.specs[a.key]
            ? 'Có'
            : 'Không'
          : p.specs[a.key] + (a.unit ? ' ' + a.unit : '');
      (groups[a.group] = groups[a.group] || []).push([a.label, val]);
    });
    const specRows = Object.entries(groups)
      .map(
        ([g, rows]) =>
          `<tr><td colspan="2"><b>${g}</b></td></tr>` +
          rows.map(([l, val]) => `<tr><td>${l}</td><td>${esc(val)}</td></tr>`).join(''),
      )
      .join('');
    // personalized-content: điểm nổi bật theo persona
    const hl = D.highlights[p.id];
    const ps = PK.personalized() && hl && hl[persona()] ? persona() : 'general';
    const hlBlock = hl
      ? `<div class="ai-block"><div class="row between"><h3 style="margin:0">Điểm nổi bật cho nhu cầu của bạn</h3><span class="ai-label">Gợi ý bởi AI</span></div>
          <ul>${hl[ps].map((h) => `<li>${esc(h.text)} <span class="small muted">(${h.basedOn.join(', ')})</span></li>`).join('')}</ul>
          <div class="small muted">Phiên bản: ${ps === 'gaming' ? 'persona gaming' : 'chung (general)'}</div></div>`
      : '';
    const sum = D.reviewSummaries[p.id];
    const reviews = S.reviews.filter((r) => r.pid === p.id && r.status === 'visible');
    const sumBlock =
      sum && reviews.length >= 3
        ? `<div class="ai-block"><div class="row between"><h3 style="margin:0">Khách hàng nói gì</h3><span class="ai-label">Tóm tắt bởi AI <a href="#" data-act="aiExplain" title="Giải thích">ⓘ</a></span></div>
          <p>${esc(sum.overview)}</p>
          <div class="grid cols-2"><div><b class="small">Ưu điểm</b><ul>${sum.pros.map((x) => `<li>${esc(x.text)} <span class="muted small">${x.count} lượt nhắc</span></li>`).join('')}</ul></div>
          <div><b class="small">Nhược điểm</b><ul>${sum.cons.map((x) => `<li>${esc(x.text)} <span class="muted small">${x.count} lượt nhắc</span></li>`).join('')}</ul></div></div>
          <div class="row">${sum.aspects.map((a) => `<span class="chip" style="${PK.personalized() && ['hiệu năng', 'tản nhiệt'].includes(a) ? 'font-weight:700;border-color:var(--color-ink)' : ''}">${a}</span>`).join('')}</div></div>`
        : '';
    // commerce: review
    const avg = reviews.length
      ? (reviews.reduce((s, r) => s + r.rating, 0) / reviews.length).toFixed(1)
      : p.rating;
    const dist = [5, 4, 3, 2, 1].map((n) => [n, reviews.filter((r) => r.rating === n).length]);
    const canReview =
      S.role === 'customer' &&
      S.orders.some(
        (o) => o.userId === 'u1' && o.status === 'delivered' && o.items.some((i) => i.pid === p.id),
      );
    // recommendations: sản phẩm tương tự, thường mua kèm
    const similar = S.products
      .filter(
        (x) =>
          x.id !== p.id &&
          x.status === 'active' &&
          x.stock > 0 &&
          x.cat === p.cat &&
          Math.abs(x.price - p.price) <= p.price * 0.3,
      )
      .slice(0, 8);
    const compCats =
      {
        'laptop-gaming': ['chuot', 'balo', 'tai-nghe'],
        'laptop-van-phong': ['chuot', 'sac', 'balo'],
        'dien-thoai': ['sac', 'tai-nghe'],
      }[p.cat] || [];
    const together = S.products
      .filter(
        (x) =>
          compCats.includes(x.cat) &&
          x.status === 'active' &&
          x.stock > 0 &&
          !S.cart.some((l) => l.pid === x.id),
      )
      .slice(0, 6);
    return `
      <div class="breadcrumb"><a href="#/">Trang chủ</a> / <a href="#/c/${c.parent || c.id}">${PK.cat(c.parent || c.id).name}</a> / <a href="#/c/${c.id}">${c.name}</a></div>
      <div class="pdp">
        <div class="gallery"><div class="main">${productImg(p, 320)}</div><div class="thumbs">${[1, 2, 3, 4].map(() => `<div>${productImg(p, 48)}</div>`).join('')}</div></div>
        <div class="stack">
          <div class="row">${disc ? '<span class="badge gray">Ngừng kinh doanh</span>' : ''}<span class="muted small">${p.brand}</span></div>
          <h1>${esc(p.name)}${v ? ` <span class="muted" style="font-weight:400">${Object.values(v.opts).join(' ')}</span>` : ''}</h1>
          <div class="row small"><span class="stars">${stars(avg)}</span> ${avg} · ${p.reviewCount} đánh giá · Bảo hành ${p.warranty} tháng</div>
          <div><span class="price-big">${money(price)}</span> ${compareAt ? `<span class="strike">${money(compareAt)}</span> <span class="badge sale">-${Math.round((1 - price / compareAt) * 100)}%</span>` : ''}</div>
          ${picker}
          <div class="small ${stock > 0 ? '' : 'muted'}">${disc ? 'Sản phẩm đã ngừng kinh doanh.' : stock > 5 ? 'Còn hàng' : stock > 0 ? `Chỉ còn ${stock} sản phẩm` : 'Hết hàng'}</div>
          <div class="row">
            ${disc ? '' : `<button class="btn" ${stock > 0 ? '' : 'disabled'} data-act="addToCart" data-pid="${p.id}" data-sku="${v ? v.sku : p.slug.toUpperCase()}" data-source="category">Thêm vào giỏ</button>`}
            <button class="btn secondary" data-act="addCompare" data-pid="${p.id}">So sánh</button>
            <button class="btn ghost" data-act="askAbout" data-pid="${p.id}">Hỏi trợ lý về máy này</button>
          </div>
          <p class="muted">${esc(p.short)}</p>
          ${hlBlock}
        </div>
      </div>
      <div class="grid cols-2 section">
        <div><h2>Thông số kỹ thuật</h2>${specRows ? `<table class="tbl spec-table">${specRows}</table>` : '<p class="muted">Đang cập nhật.</p>'}</div>
        <div>${sumBlock || note('Sản phẩm có dưới 3 đánh giá hoặc chưa có tóm tắt: khối "Khách hàng nói gì" bị ẩn và hệ thống xếp hàng tạo nội dung.')}</div>
      </div>
      ${note(`Persona hiện tại: <b>${PK.personalized() ? persona() : 'không cá nhân hóa'}</b>. Khối "Điểm nổi bật" đổi nội dung theo persona (gaming và general). Thử bật/tắt đồng ý ở <a href="#" data-act="openConsent">Tùy chỉnh</a>. Ngân sách AI ≥ 100% thì không tạo nội dung mới nhưng nội dung cũ vẫn hiện.`)}
      ${similar.length ? recRail('similar', similar, 'Sản phẩm tương tự', () => 'Cùng danh mục, giá ±30%', 'recommendations.similar') : ''}
      ${together.length ? recRail('bought-together', together, 'Thường mua kèm', () => 'Hay được mua cùng ' + c.name.toLowerCase(), 'recommendations.bought-together') : ''}
      <section class="section" id="reviews"><h2>Đánh giá</h2>
        <div class="grid cols-3">
          <div class="card"><div class="price-big">${avg}<span class="muted" style="font-size:16px">/5</span></div><div class="stars">${stars(avg)}</div>
            ${dist.map(([n, cnt]) => `<div class="row small"><span>${n}★</span><div class="bar"><i style="width:${reviews.length ? (cnt / reviews.length) * 100 : 0}%"></i></div><span class="muted">${cnt}</span></div>`).join('')}</div>
          <div style="grid-column: span 2" class="stack">
            ${canReview ? `<form class="card" data-form="review" data-pid="${p.id}"><b>Viết đánh giá</b><div class="row" style="margin:8px 0">${[1, 2, 3, 4, 5].map((n) => `<label class="row small"><input type="radio" name="rating" value="${n}" ${n === 5 ? 'checked' : ''}> ${n}★</label>`).join('')}</div><textarea name="body" maxlength="2000" placeholder="Chia sẻ trải nghiệm (tối đa 2.000 ký tự, không kèm ảnh)"></textarea><button class="btn sm" style="margin-top:8px">Gửi đánh giá</button></form>` : `<p class="small muted">Chỉ khách đã mua và nhận hàng mới viết được đánh giá. ${S.role !== 'customer' ? 'Chọn vai trò Khách hàng (đã mua ROG G16) để thử.' : ''}</p>`}
            ${reviews.map((r) => `<div class="card"><div class="row between"><b>${esc(r.user)}</b><span class="small muted">${r.date}</span></div><div class="stars">${stars(r.rating)}</div><p style="margin:6px 0 0">${esc(r.body)}</p><a href="#" class="small muted" data-act="reportReview" data-id="${r.id}">Báo cáo</a></div>`).join('') || '<p class="muted">Chưa có đánh giá.</p>'}
          </div>
        </div>
      </section>`;
  });
  act('aiExplain', () => {
    modal(
      `<h2>Tóm tắt bởi AI</h2><p>Nội dung được AI tổng hợp từ đánh giá thật của khách đã mua. Mỗi ý có số lượt nhắc tới. Hệ thống kiểm tra để AI không bịa con số hoặc dùng từ như "tốt nhất".</p><button class="btn" data-act="closeModal">Đã hiểu</button>`,
    );
    return false;
  });
  act('reportReview', (d) => {
    const r = PK.S.reviews.find((x) => x.id === d.id);
    r.reports += 1;
    toast('Đã gửi báo cáo, CSKH sẽ xem xét');
    return false;
  });
  form('review', (data, f) => {
    if (!String(data.body || '').trim()) return toast('Hãy viết vài dòng nhận xét');
    PK.S.reviews.unshift({
      id: 'r' + Date.now(),
      pid: f.dataset.pid,
      user: 'Minh Anh',
      rating: Number(data.rating),
      body: data.body,
      date: PK.now().slice(0, 10),
      status: 'visible',
      reports: 0,
    });
    toast('Cảm ơn bạn đã đánh giá');
    PK.render();
  });

  // ================= commerce: giỏ hàng =================
  act('addToCart', (d) => {
    const S = PK.S;
    const line = S.cart.find((l) => l.sku === d.sku);
    if (line) {
      if (line.qty >= 5) return toast('Tối đa 5 sản phẩm mỗi loại');
      line.qty += 1;
    } else {
      if (S.cart.length >= 20) return toast('Giỏ hàng tối đa 20 dòng');
      S.cart.push({ pid: d.pid, sku: d.sku, qty: 1, source: d.source || 'category' });
    }
    toast('Đã thêm vào giỏ' + (d.source === 'shopping-assistant' ? ' (nguồn: trợ lý AI)' : ''));
    PK.render();
  });
  act('qty', (d) => {
    const S = PK.S;
    const line = S.cart.find((l) => l.sku === d.sku);
    line.qty = Math.min(5, Math.max(0, line.qty + Number(d.delta)));
    if (!line.qty) S.cart = S.cart.filter((l) => l !== line);
    PK.render();
  });
  function cartLines() {
    return PK.S.cart.map((l) => {
      const p = PK.prod(l.pid);
      const v = p.variants ? p.variants.find((x) => x.sku === l.sku) : null;
      const price = v ? v.price : p.price;
      const stock = v ? v.stock : p.stock;
      const problem =
        p.status === 'discontinued'
          ? 'Ngừng kinh doanh'
          : stock <= 0
            ? 'Hết hàng'
            : stock < l.qty
              ? `Chỉ còn ${stock}`
              : '';
      return { l, p, v, price, problem };
    });
  }
  route('/cart', () => {
    const S = PK.S;
    const lines = cartLines();
    if (!lines.length)
      return `<h1>Giỏ hàng</h1><div class="card muted-bg"><p>Giỏ hàng trống.</p><a class="btn" href="#/c/laptop">Mua sắm ngay</a></div>${note('Thêm sản phẩm từ trang sản phẩm hoặc từ thẻ sản phẩm trong chat trợ lý.')}`;
    const sub = lines.reduce((s, x) => s + x.price * x.l.qty, 0);
    const blocked = lines.some((x) => x.problem);
    const cross = S.products
      .filter(
        (x) =>
          ['chuot', 'balo', 'sac'].includes(x.cat) &&
          !S.cart.some((l) => l.pid === x.id) &&
          x.stock > 0,
      )
      .slice(0, 6);
    return `<h1>Giỏ hàng</h1>
      <div class="grid" style="grid-template-columns:1fr 340px">
        <div class="stack">${lines
          .map(
            (x) => `<div class="card row" style="flex-wrap:nowrap">
            <div style="width:80px;height:80px;background:var(--color-surface-muted);border-radius:10px;display:flex;align-items:center;justify-content:center;flex:none">${productImg(x.p, 60)}</div>
            <div style="flex:1"><a href="#/p/${x.p.slug}"><b>${esc(x.p.name)}</b></a><div class="small muted">${x.v ? Object.values(x.v.opts).join(', ') : ''} · SKU ${x.l.sku}</div>
              ${x.problem ? `<span class="badge sale">${x.problem}</span>` : ''}${x.l.source === 'shopping-assistant' ? ' <span class="badge ai">Từ trợ lý AI</span>' : ''}</div>
            <div class="row"><button class="btn sm secondary" data-act="qty" data-sku="${x.l.sku}" data-delta="-1" aria-label="Giảm">−</button><b>${x.l.qty}</b><button class="btn sm secondary" data-act="qty" data-sku="${x.l.sku}" data-delta="1" aria-label="Tăng">+</button></div>
            <b style="width:130px;text-align:right">${money(x.price * x.l.qty)}</b></div>`,
          )
          .join('')}</div>
        <div class="card stack"><div class="row between"><span>Tạm tính</span><b>${money(sub)}</b></div>
          <div class="small muted">${sub >= S.commerceSettings.freeShip ? 'Đơn được miễn phí vận chuyển' : `Mua thêm ${money(S.commerceSettings.freeShip - sub)} để được miễn phí vận chuyển`}</div>
          ${blocked ? '<div class="alert err">Có sản phẩm không còn bán được. Hãy bỏ khỏi giỏ để tiếp tục.</div>' : ''}
          <a class="btn block ${blocked ? 'disabled' : ''}" href="${blocked ? '#/cart' : '#/checkout'}">Thanh toán</a></div>
      </div>
      ${recRail('cart', cross, 'Có thể bạn cần thêm', () => 'Phụ kiện hay mua cùng', 'recommendations.cart')}`;
  });

  // ================= commerce + shipping: checkout 1 trang =================
  function draft() {
    const S = PK.S;
    if (!S.draft) {
      const u = S.role === 'customer' ? PK.me() : null;
      S.draft = {
        name: u ? u.name : '',
        phone: u ? u.phone : '',
        email: u ? u.email : '',
        province: u ? 'HCM' : '',
        ward: u ? 'Phường Bến Nghé' : '',
        street: u ? '12 Nguyễn Huệ' : '',
        method: 'sepay',
        vat: false,
        note: '',
      };
    }
    return S.draft;
  }
  route('/checkout', () => {
    const S = PK.S;
    const lines = cartLines();
    if (!lines.length)
      return `<h1>Thanh toán</h1><p>Giỏ hàng trống. <a href="#/">Tiếp tục mua sắm</a></p>`;
    const dr = draft();
    const sub = lines.reduce((s, x) => s + x.price * x.l.qty, 0);
    const prov = D.provinces.find((x) => x.code === dr.province);
    const fee = prov ? prov.fee : null;
    const discount = fee != null && sub >= S.commerceSettings.freeShip ? fee : 0;
    const total = sub + (fee || 0) - discount;
    const codOk = total <= S.commerceSettings.codMax;
    const eta = prov
      ? new Date(Date.now() + prov.days * 86400000).toLocaleDateString('vi-VN', {
          weekday: 'long',
          day: '2-digit',
          month: '2-digit',
        })
      : '';
    return `<h1>Thanh toán</h1>
      <form data-form="placeOrder" class="grid" style="grid-template-columns:1fr 360px;align-items:start">
        <div class="stack">
          <div class="card stack"><h3>Thông tin liên hệ</h3>
            <div class="grid cols-2"><label class="field"><span>Họ tên *</span><input name="name" value="${esc(dr.name)}" data-input="draft"></label>
            <label class="field"><span>Số điện thoại *</span><input name="phone" value="${esc(dr.phone)}" data-input="draft"></label></div>
            <label class="field"><span>Email * (nhận xác nhận đơn${S.role === 'guest' ? ' và link tra cứu' : ''})</span><input name="email" type="email" value="${esc(dr.email)}" data-input="draft"></label>
            ${S.role === 'guest' ? '<p class="small muted" style="margin:0">Bạn đang mua không cần tài khoản. <a href="#/login">Đăng nhập</a> để dùng sổ địa chỉ.</p>' : ''}
          </div>
          <div class="card stack"><h3>Địa chỉ giao hàng</h3>
            ${S.role === 'customer' ? `<label class="field"><span>Sổ địa chỉ</span><select data-change="pickAddress"><option>Nhà riêng: 12 Nguyễn Huệ, Phường Bến Nghé, TP. Hồ Chí Minh (mặc định)</option><option>Công ty: 8 Lý Thường Kiệt, Phường Hoàn Kiếm, Hà Nội</option></select></label>` : ''}
            <div class="grid cols-2">
              <label class="field"><span>Tỉnh/thành *</span><select name="province" data-change="province"><option value="">Chọn tỉnh/thành</option>${D.provinces.map((x) => `<option value="${x.code}" ${dr.province === x.code ? 'selected' : ''}>${x.name}</option>`).join('')}</select></label>
              <label class="field"><span>Phường/xã *</span><select name="ward" data-change="ward" ${prov ? '' : 'disabled'}><option value="">Chọn phường/xã</option>${prov ? prov.wards.map((w) => `<option ${dr.ward === w ? 'selected' : ''}>${w}</option>`).join('') : ''}</select></label>
            </div>
            <label class="field"><span>Số nhà, tên đường *</span><input name="street" value="${esc(dr.street)}" data-input="draft"></label>
            <label class="field"><span>Ghi chú</span><input name="note" value="${esc(dr.note)}" data-input="draft" placeholder="Giao giờ hành chính..."></label>
          </div>
          <div class="card stack"><h3>Phương thức thanh toán</h3>
            <label class="row"><input type="radio" name="method" value="sepay" ${dr.method === 'sepay' ? 'checked' : ''} data-change="method"> <b>Chuyển khoản (SePay, VietQR)</b> <span class="small muted">Xác nhận tự động trong vài giây</span></label>
            <label class="row ${codOk ? '' : 'muted'}"><input type="radio" name="method" value="cod" ${dr.method === 'cod' ? 'checked' : ''} ${codOk ? '' : 'disabled'} data-change="method"> <b>Thanh toán khi nhận hàng (COD)</b> ${codOk ? '' : `<span class="small">Không áp dụng cho đơn trên ${money(S.commerceSettings.codMax)}</span>`}</label>
          </div>
          <div class="card stack"><label class="row"><input type="checkbox" name="vat" ${dr.vat ? 'checked' : ''} data-change="vat"> <b>Xuất hóa đơn VAT</b></label>
            ${dr.vat ? `<div class="grid cols-2"><label class="field"><span>Tên công ty *</span><input name="company"></label><label class="field"><span>Mã số thuế * (10 hoặc 13 số)</span><input name="taxCode" placeholder="0312345678"></label><label class="field"><span>Địa chỉ công ty *</span><input name="companyAddress"></label><label class="field"><span>Email nhận hóa đơn *</span><input name="invoiceEmail" type="email"></label></div>` : ''}
          </div>
          ${note('Lỗi được thiết kế: chưa tính được phí ship ("Chưa tính được phí vận chuyển, thử lại"), tổng tiền đổi (409 "Giá đã thay đổi, xem lại"), hết hàng lúc đặt (409 kèm danh sách SKU). Đơn trên 20 triệu tự tắt COD.')}
        </div>
        <div class="card stack" style="position:sticky;top:140px">
          <h3>Đơn hàng (${lines.length})</h3>
          ${lines.map((x) => `<div class="row between small"><span>${x.l.qty} × ${esc(x.p.name)}</span><b>${money(x.price * x.l.qty)}</b></div>`).join('')}
          <hr style="border:0;border-top:1px solid var(--color-border);width:100%">
          <div class="row between"><span>Tạm tính</span><span>${money(sub)}</span></div>
          <div class="row between"><span>Phí vận chuyển (GHN)</span><span>${fee == null ? '<span class="muted">Chọn địa chỉ</span>' : money(fee)}</span></div>
          ${discount ? `<div class="row between"><span>Giảm phí vận chuyển</span><span>−${money(discount)}</span></div>` : ''}
          <div class="row between"><b>Tổng</b><b class="price-big" style="font-size:22px">${money(total)}</b></div>
          ${eta ? `<div class="small muted">Dự kiến giao: ${eta}</div>` : ''}
          <button class="btn block">Đặt hàng</button>
          <div class="small muted">Nhấn đặt hàng là bạn đồng ý với <a href="#/help/chinh-sach-doi-tra">chính sách đổi trả</a>.</div>
        </div>
      </form>`;
  });
  input('draft', (el) => {
    draft()[el.name] = el.value;
  });
  change('province', (el) => {
    const dr = draft();
    dr.province = el.value;
    dr.ward = '';
    PK.render();
  });
  change('ward', (el) => {
    draft().ward = el.value;
  });
  change('method', (el) => {
    draft().method = el.value;
  });
  change('vat', (el) => {
    draft().vat = el.checked;
    PK.render();
  });
  change('pickAddress', (el) => {
    const dr = draft();
    if (el.selectedIndex === 1)
      Object.assign(dr, { province: 'HN', ward: 'Phường Hoàn Kiếm', street: '8 Lý Thường Kiệt' });
    else Object.assign(dr, { province: 'HCM', ward: 'Phường Bến Nghé', street: '12 Nguyễn Huệ' });
    PK.render();
  });
  form('placeOrder', (data) => {
    const S = PK.S;
    const dr = draft();
    Object.assign(dr, {
      name: data.name,
      phone: data.phone,
      email: data.email,
      street: data.street,
      note: data.note,
    });
    const missing = [];
    if (!String(data.name).trim()) missing.push('họ tên');
    if (!/^0\d{9}$/.test(String(data.phone).replace(/\s/g, '')))
      missing.push('số điện thoại hợp lệ');
    if (!/.+@.+\..+/.test(data.email)) missing.push('email');
    if (!dr.province || !dr.ward || !String(data.street).trim()) missing.push('địa chỉ đầy đủ');
    if (dr.vat && !/^\d{10}(-?\d{3})?$/.test(String(data.taxCode || '')))
      missing.push('mã số thuế 10 hoặc 13 số');
    if (missing.length) return toast('Còn thiếu: ' + missing.join(', '));
    const lines = cartLines();
    const prov = D.provinces.find((x) => x.code === dr.province);
    const sub = lines.reduce((s, x) => s + x.price * x.l.qty, 0);
    const code = PK.orderCode();
    const o = {
      code,
      userId: S.role === 'customer' ? 'u1' : null,
      customer: data.name,
      phone: data.phone,
      email: data.email,
      address: `${data.street}, ${dr.ward}, ${prov.name}`,
      createdAt: PK.now(),
      items: lines.map((x) => ({
        pid: x.p.id,
        name: x.p.name + (x.v ? ' ' + Object.values(x.v.opts).join(' ') : ''),
        sku: x.l.sku,
        qty: x.l.qty,
        price: x.price,
      })),
      shippingFee: prov.fee,
      shippingDiscount: sub >= S.commerceSettings.freeShip ? prov.fee : 0,
      method: dr.method,
      status: dr.method === 'sepay' ? 'pending_payment' : 'pending_confirmation',
      payment: 'unpaid',
      flags: dr.vat ? ['invoice_requested'] : [],
      vat: dr.vat
        ? {
            company: data.company,
            taxCode: data.taxCode,
            address: data.companyAddress,
            email: data.invoiceEmail,
          }
        : null,
      token: Math.random().toString(36).slice(2, 10),
      history: [
        [
          PK.now(),
          null,
          dr.method === 'sepay' ? 'pending_payment' : 'pending_confirmation',
          'Khách đặt hàng',
        ],
      ],
    };
    // Giữ hàng (order_reserve)
    lines.forEach((x) => {
      if (x.v) x.v.stock -= x.l.qty;
      else x.p.stock -= x.l.qty;
      S.stockMoves.unshift({
        sku: x.l.sku,
        delta: -x.l.qty,
        type: 'order_reserve',
        ref: code,
        reason: '',
        at: PK.now(),
      });
    });
    S.orders.unshift(o);
    S.cart = [];
    S.draft = null;
    PK.go(dr.method === 'sepay' ? '/pay/' + code : '/order-success/' + code);
  });

  // ================= commerce: trang VietQR chờ thanh toán =================
  function qrSvg(seedStr) {
    let h = 7;
    let cells = '';
    for (let y = 0; y < 25; y++)
      for (let x = 0; x < 25; x++) {
        h = (h * 31 + seedStr.charCodeAt((x + y) % seedStr.length)) % 9973;
        const finder = (x < 7 && y < 7) || (x > 17 && y < 7) || (x < 7 && y > 17);
        const on = finder
          ? x % 6 === 0 ||
            y % 6 === 0 ||
            (x % 6 > 1 && x % 6 < 5 && y % 6 > 1 && y % 6 < 5) ||
            (x > 17 && (x - 18) % 6 === 0) ||
            (y > 17 && (y - 18) % 6 === 0)
          : h % 2 === 0;
        if (on) cells += `<rect x="${x * 7}" y="${y * 7}" width="7" height="7"/>`;
      }
    return `<svg class="qr" viewBox="0 0 175 175" aria-label="Mã VietQR">${cells}</svg>`;
  }
  route('/pay/:code', (p) => {
    const S = PK.S;
    const o = PK.order(p.code);
    if (!o) return '<h1>Không tìm thấy đơn</h1>';
    if (o.payment === 'paid')
      return `<div class="card" style="max-width:520px;margin:0 auto;text-align:center"><h1>Đã nhận thanh toán</h1><p>Đơn ${o.code} đã thanh toán.</p><a class="btn" href="#/order-success/${o.code}">Xem đơn hàng</a></div>`;
    if (o.status === 'cancelled')
      return `<div class="card" style="max-width:520px;margin:0 auto;text-align:center"><h1>Đơn đã hủy</h1><p>Đơn ${o.code} quá hạn thanh toán 30 phút nên đã hủy và trả hàng về kho.</p><a class="btn" href="#/">Về trang chủ</a></div>`;
    const total = PK.orderTotal(o);
    return `<div class="card" style="max-width:640px;margin:0 auto">
      <div class="steps"><span class="s">Giỏ hàng</span>›<span class="s">Thông tin</span>›<span class="s on">Thanh toán</span>›<span class="s">Hoàn tất</span></div>
      <h1 style="margin-top:16px">Quét mã để thanh toán</h1>
      ${o.flags.includes('underpaid') ? '<div class="alert warn">Đã nhận một phần tiền nhưng chưa đủ. Vui lòng chuyển thêm phần còn thiếu hoặc liên hệ CSKH.</div>' : ''}
      <div class="row" style="align-items:flex-start;gap:24px;margin-top:12px">
        ${qrSvg(o.code)}
        <table class="tbl" style="flex:1"><tr><td class="muted">Ngân hàng</td><td><b>${S.commerceSettings.bank}</b></td></tr>
          <tr><td class="muted">Số tài khoản</td><td><b>${S.commerceSettings.account}</b></td></tr>
          <tr><td class="muted">Số tiền</td><td><b>${money(total)}</b></td></tr>
          <tr><td class="muted">Nội dung</td><td><b style="font-size:18px">${o.code}</b></td></tr>
          <tr><td class="muted">Còn lại</td><td><b id="countdown">30:00</b></td></tr></table>
      </div>
      <p class="small muted" style="margin-top:12px">Trang tự kiểm tra mỗi 3 giây và chuyển tiếp khi nhận được tiền. Ghi đúng mã đơn trong nội dung chuyển khoản.</p>
      <div class="proto-note"><b>Giả lập webhook SePay:</b>
        <div class="row" style="margin-top:6px">
          <button class="btn sm" data-act="webhook" data-code="${o.code}" data-kind="ok">Chuyển đủ tiền</button>
          <button class="btn sm secondary" data-act="webhook" data-code="${o.code}" data-kind="under">Chuyển thiếu</button>
          <button class="btn sm secondary" data-act="webhook" data-code="${o.code}" data-kind="over">Chuyển thừa</button>
          <button class="btn sm danger" data-act="webhook" data-code="${o.code}" data-kind="expire">Quá hạn 30 phút</button>
        </div></div>
    </div>`;
  });
  let countdownTimer = null;
  PK.onPage = function (path) {
    clearInterval(countdownTimer);
    if (!path.startsWith('/pay/')) return;
    let left = 30 * 60;
    countdownTimer = setInterval(() => {
      left -= 1;
      const el = document.getElementById('countdown');
      if (!el) return clearInterval(countdownTimer);
      el.textContent = `${String(Math.floor(left / 60)).padStart(2, '0')}:${String(left % 60).padStart(2, '0')}`;
    }, 1000);
  };
  act('webhook', (d) => {
    const S = PK.S;
    const o = PK.order(d.code);
    const total = PK.orderTotal(o);
    const txId = 'FT' + Date.now().toString().slice(-8);
    if (d.kind === 'expire') {
      o.status = 'cancelled';
      o.cancelReason = 'quá hạn';
      o.history.push([
        PK.now(),
        'pending_payment',
        'cancelled',
        'Hệ thống: quá hạn thanh toán 30 phút',
      ]);
      o.items.forEach((i) =>
        S.stockMoves.unshift({
          sku: i.sku,
          delta: i.qty,
          type: 'order_release',
          ref: o.code,
          reason: 'Đơn quá hạn',
          at: PK.now(),
        }),
      );
      toast('Đơn bị hủy do quá hạn, hàng được trả về kho');
      return PK.render();
    }
    const amount = d.kind === 'under' ? total - 500000 : d.kind === 'over' ? total + 200000 : total;
    S.sepayTx.unshift({
      id: txId,
      amount,
      content: o.code,
      receivedAt: PK.now(),
      match: 'matched',
      order: o.code,
      note:
        d.kind === 'under'
          ? 'Thiếu tiền (underpaid)'
          : d.kind === 'over'
            ? 'Thừa tiền (overpaid)'
            : '',
    });
    if (d.kind === 'under') {
      if (!o.flags.includes('underpaid')) o.flags.push('underpaid');
      toast('Webhook: nhận thiếu tiền, đơn vẫn chờ thanh toán');
      return PK.render();
    }
    if (d.kind === 'over') o.flags.push('overpaid');
    o.flags = o.flags.filter((f) => f !== 'underpaid');
    o.payment = 'paid';
    o.status = 'pending_confirmation';
    o.history.push([
      PK.now(),
      'pending_payment',
      'pending_confirmation',
      'Webhook SePay xác nhận đã thanh toán',
    ]);
    toast('Webhook SePay: đã nhận tiền');
    PK.go('/order-success/' + o.code);
  });

  route('/order-success/:code', (p) => {
    const S = PK.S;
    const o = PK.order(p.code);
    if (!o) return '<h1>Không tìm thấy đơn</h1>';
    const link = `#/orders/${o.code}?t=${o.token || 'demo'}`;
    return `<div class="card" style="max-width:640px;margin:0 auto">
      <div class="steps"><span class="s">Giỏ hàng</span>›<span class="s">Thông tin</span>›<span class="s">Thanh toán</span>›<span class="s on">Hoàn tất</span></div>
      <h1 style="margin-top:16px">Đặt hàng thành công</h1>
      <p>Mã đơn <b>${o.code}</b> · ${PK.orderBadge(o.status)} ${PK.payBadge(o.payment)}</p>
      <p>Tổng tiền <b>${money(PK.orderTotal(o))}</b> · ${o.method === 'cod' ? 'Thanh toán khi nhận hàng' : 'Chuyển khoản SePay'}</p>
      <div class="row"><a class="btn" href="${S.role === 'customer' ? '#/orders/' + o.code : link}">Xem đơn hàng</a><a class="btn secondary" href="#/">Tiếp tục mua sắm</a></div>
      <div class="card muted-bg" style="margin-top:20px"><div class="small muted">Xem trước email gửi tới ${esc(o.email)}</div>
        <b>Pickory: Xác nhận đơn hàng ${o.code}</b><p class="small">Cảm ơn ${esc(o.customer)} đã mua hàng. ${S.role === 'guest' ? `Tra cứu đơn mà không cần tài khoản: <a href="${link}">pickory.vn/orders/${o.code}?t=…</a>` : 'Xem đơn trong mục Đơn hàng của tôi.'}</p></div>
      ${S.role === 'guest' ? note('Khách vãng lai có thể <a href="#/register">đăng ký</a> ngay sau khi mua: đơn và giỏ được gộp vào tài khoản (UserLinkedEvent).') : ''}
    </div>`;
  });

  // ================= commerce + shipping: đơn của tôi =================
  route('/orders', () => {
    const S = PK.S;
    if (S.role !== 'customer')
      return `<h1>Đơn hàng của tôi</h1><div class="card muted-bg"><p>Đăng nhập để xem đơn hàng. Khách mua không cần tài khoản thì dùng link trong email xác nhận.</p><a class="btn" href="#/login">Đăng nhập</a></div>`;
    const mine = S.orders.filter((o) => o.userId === 'u1');
    return `<h1>Đơn hàng của tôi</h1><div class="table-wrap"><table class="tbl"><tr><th>Mã đơn</th><th>Ngày</th><th class="num">Tổng</th><th>Trạng thái</th><th>Thanh toán</th></tr>
      ${mine.map((o) => `<tr class="click" data-act="nav" data-href="#/orders/${o.code}"><td><b>${o.code}</b></td><td>${o.createdAt}</td><td class="num">${money(PK.orderTotal(o))}</td><td>${PK.orderBadge(o.status)}</td><td>${PK.payBadge(o.payment)}</td></tr>`).join('')}</table></div>`;
  });
  route('/orders/:code', (p, q) => {
    const S = PK.S;
    const o = PK.order(p.code);
    if (!o) return '<h1>Không tìm thấy đơn</h1>';
    const tokenView = !!q.t && S.role !== 'customer';
    if (!tokenView && (S.role !== 'customer' || o.userId !== 'u1'))
      return `<h1>Không tìm thấy đơn</h1><p class="muted">Bạn chỉ xem được đơn của chính mình.</p>`;
    const canCancel = !tokenView && ['pending_payment', 'pending_confirmation'].includes(o.status);
    const steps = ['pending_confirmation', 'confirmed', 'shipping', 'delivered'];
    const curIdx = steps.indexOf(o.status);
    return `<div class="breadcrumb"><a href="#/orders">Đơn hàng</a> / ${o.code}</div>
      <div class="row between"><h1>Đơn ${o.code}</h1><div class="row">${PK.orderBadge(o.status)} ${PK.payBadge(o.payment)}</div></div>
      ${tokenView ? '<div class="alert">Bạn đang xem đơn bằng link trong email (chỉ đọc).</div>' : ''}
      ${o.status !== 'cancelled' ? `<div class="steps" style="margin:16px 0">${steps.map((s, i) => `<span class="s ${i <= curIdx ? 'on' : ''}">${PK.ORDER_STATUS[s]}</span>`).join('›')}</div>` : `<div class="alert err">Đơn đã hủy (${esc(o.cancelReason || '')}). ${o.payment === 'refund_pending' ? 'Pickory đang hoàn tiền cho bạn.' : ''}</div>`}
      <div class="grid cols-2" style="margin-top:16px">
        <div class="card stack"><h3>Sản phẩm</h3>${o.items.map((i) => `<div class="row between"><span>${i.qty} × ${esc(i.name)}</span><b>${money(i.price * i.qty)}</b></div>`).join('')}
          <div class="row between small"><span>Phí vận chuyển</span><span>${money(o.shippingFee - o.shippingDiscount)}</span></div>
          <div class="row between"><b>Tổng</b><b>${money(PK.orderTotal(o))}</b></div>
          ${o.vat ? `<div class="small muted">Hóa đơn VAT: ${esc(o.vat.company)}, MST ${esc(o.vat.taxCode)}</div>` : ''}
          ${o.status === 'pending_payment' ? `<a class="btn" href="#/pay/${o.code}">Thanh toán ngay</a>` : ''}
          ${canCancel ? `<button class="btn danger" data-act="customerCancel" data-code="${o.code}">Hủy đơn</button>` : ''}</div>
        <div class="card stack"><h3>Giao hàng</h3><p class="small">${esc(o.customer)} · ${tokenView ? '***' : esc(o.phone)}<br>${esc(o.address)}</p>
          ${
            o.tracking
              ? `<p>Mã vận đơn GHN: <b>${o.tracking}</b> · <span class="badge">${PK.SHIP_STATUS[o.shipStatus] || ''}</span></p>
          <ul class="timeline">${(o.shipEvents || []).map((e) => `<li class="done"><b>${PK.SHIP_STATUS[e[1]]}</b><div class="small muted">${e[0]}</div></li>`).join('')}</ul>`
              : '<p class="muted small">Chưa tạo vận đơn.</p>'
          }</div>
      </div>
      ${note('Khách chỉ hủy được khi đơn ở "Chờ thanh toán" hoặc "Chờ xác nhận". Hủy đơn đã thanh toán chuyển sang "Chờ hoàn tiền".')}`;
  });
  act('customerCancel', (d) => {
    const o = PK.order(d.code);
    if (!confirm('Hủy đơn ' + o.code + '?')) return;
    o.history.push([PK.now(), o.status, 'cancelled', 'Khách hủy đơn']);
    o.status = 'cancelled';
    o.cancelReason = 'khách hủy';
    if (o.payment === 'paid') o.payment = 'refund_pending';
    toast('Đã hủy đơn');
    PK.render();
  });

  // ================= identity: đăng nhập, đăng ký, quên mật khẩu =================
  let failCount = 0;
  route(
    '/login',
    () => `<div class="card" style="max-width:420px;margin:0 auto"><h1>Đăng nhập</h1>
      <form data-form="login" class="stack"><label class="field"><span>Email</span><input name="email" type="email" value="minhanh@example.vn"></label>
      <label class="field"><span>Mật khẩu</span><input name="password" type="password" placeholder="Mật khẩu đúng: 12345678"></label>
      <button class="btn block">Đăng nhập</button></form>
      <button class="btn secondary block" style="margin-top:8px" data-act="googleLogin">Đăng nhập bằng Google</button>
      <div class="row between small" style="margin-top:12px"><a href="#/forgot">Quên mật khẩu?</a><a href="#/register">Tạo tài khoản</a></div>
      ${note('Sai mật khẩu 5 lần trong 15 phút thì lần 6 bị khóa 15 phút (429), kể cả nhập đúng. Thông báo lỗi không tiết lộ email có tồn tại hay không.')}</div>`,
  );
  form('login', (data) => {
    if (failCount >= 5) return toast('Bạn đã nhập sai quá nhiều lần. Thử lại sau 15 phút.');
    if (data.password !== '12345678') {
      failCount += 1;
      return toast(`Email hoặc mật khẩu không đúng (lần ${failCount}/5)`);
    }
    failCount = 0;
    loginCustomer();
  });
  function loginCustomer() {
    const S = PK.S;
    S.role = 'customer';
    S.draft = null;
    toast('Chào Minh Anh! Đã gộp giỏ hàng và lịch sử xem của phiên khách (UserLinkedEvent)');
    PK.go('/');
  }
  act('googleLogin', () => {
    modal(`<h2>Google</h2><p>Chọn tài khoản để tiếp tục với Pickory</p>
      <button class="btn secondary block" data-act="googlePick">minhanh@example.vn (email đã xác thực, trùng tài khoản: tự liên kết)</button>
      <p class="small muted" style="margin-top:12px">Email Google chưa xác thực thì không tự liên kết. Email thuộc nhân viên bị từ chối.</p>`);
  });
  act('googlePick', () => {
    closeModal();
    loginCustomer();
  });
  route(
    '/register',
    () => `<div class="card" style="max-width:420px;margin:0 auto"><h1>Tạo tài khoản</h1>
      <form data-form="register" class="stack"><label class="field"><span>Họ tên</span><input name="name"></label>
      <label class="field"><span>Email</span><input name="email" type="email"></label>
      <label class="field"><span>Mật khẩu (tối thiểu 8 ký tự)</span><input name="password" type="password"></label>
      <button class="btn block">Đăng ký</button></form>
      <button class="btn secondary block" style="margin-top:8px" data-act="googleLogin">Đăng ký bằng Google</button>
      ${note('Thử email có đuôi @pickory.vn để thấy lỗi "email thuộc tài khoản nhân viên". Đăng ký xong được đăng nhập luôn và nhận email xác thực (hạn 24 giờ).')}</div>`,
  );
  form('register', (data) => {
    if (!/.+@.+\..+/.test(data.email)) return toast('Email không hợp lệ');
    if (String(data.password).length < 8) return toast('Mật khẩu cần tối thiểu 8 ký tự');
    if (/@pickory\.vn$/.test(data.email))
      return toast('Email này thuộc tài khoản nhân viên, không đăng ký làm khách được');
    PK.S.role = 'customer';
    modal(`<h2>Đăng ký thành công</h2><p>Chúng tôi đã gửi email xác thực tới <b>${esc(data.email)}</b>.</p>
      <div class="card muted-bg"><div class="small muted">Xem trước email</div><b>Xác thực email Pickory</b><p class="small">Nhấn vào link để xác thực (hạn 24 giờ): <a href="#/verify" data-act="closeModal">pickory.vn/verify-email?t=…</a></p></div>
      <button class="btn" style="margin-top:12px" data-act="closeModal">Tiếp tục mua sắm</button>`);
    PK.render();
  });
  route(
    '/verify',
    () =>
      `<div class="card" style="max-width:420px;margin:0 auto;text-align:center"><h1>Đã xác thực email</h1><p>Cảm ơn bạn. Tài khoản đã được xác thực.</p><a class="btn" href="#/">Về trang chủ</a></div>`,
  );
  route(
    '/forgot',
    () => `<div class="card" style="max-width:420px;margin:0 auto"><h1>Quên mật khẩu</h1>
      <form data-form="forgot" class="stack"><label class="field"><span>Email</span><input name="email" type="email"></label><button class="btn block">Gửi link đặt lại</button></form>
      ${note('Luôn trả cùng một thông báo và độ trễ dù email có tồn tại hay không.')}</div>`,
  );
  form('forgot', (data) => {
    modal(`<h2>Kiểm tra email của bạn</h2><p>Nếu ${esc(data.email)} có tài khoản, chúng tôi đã gửi link đặt lại mật khẩu. Link có hạn 30 phút và chỉ dùng được 1 lần.</p>
      <div class="card muted-bg"><div class="small muted">Xem trước email</div><b>Đặt lại mật khẩu Pickory</b><p class="small"><a href="#/reset" data-act="closeModal">pickory.vn/reset-password?t=…</a></p></div>`);
  });
  route(
    '/reset',
    (
      _,
      q,
    ) => `<div class="card" style="max-width:420px;margin:0 auto"><h1>${q.invite ? 'Đặt mật khẩu cho tài khoản nhân viên' : 'Đặt lại mật khẩu'}</h1>
      <form data-form="reset" class="stack"><label class="field"><span>Mật khẩu mới (tối thiểu 8 ký tự)</span><input name="password" type="password"></label><button class="btn block">Lưu mật khẩu</button></form>
      ${note(q.invite ? 'Trang này dùng chung cho lời mời nhân viên (link hạn 72 giờ, dùng 1 lần).' : 'Đặt lại xong, mọi phiên đăng nhập cũ bị xóa.')}</div>`,
  );
  form('reset', (data) => {
    if (String(data.password).length < 8) return toast('Mật khẩu cần tối thiểu 8 ký tự');
    toast('Đã đặt mật khẩu mới. Mọi phiên cũ đã bị đăng xuất.');
    const { query } = PK.parseHash();
    PK.go(query.invite ? '/admin/login' : '/login');
  });

  // ================= identity + activity-tracking: tài khoản khách =================
  route('/account', (_, q) => {
    const S = PK.S;
    if (S.role !== 'customer')
      return `<h1>Tài khoản</h1><p><a href="#/login">Đăng nhập</a> để quản lý tài khoản.</p>`;
    const u = PK.me();
    const tab = q.tab || 'info';
    const tabs = [
      ['info', 'Thông tin'],
      ['password', 'Đổi mật khẩu'],
      ['addresses', 'Sổ địa chỉ'],
      ['privacy', 'Quyền riêng tư'],
      ['delete', 'Xóa tài khoản'],
    ];
    let body = '';
    if (tab === 'info')
      body = `<form data-form="profile" class="stack" style="max-width:420px"><label class="field"><span>Họ tên</span><input name="name" value="${esc(u.name)}"></label>
        <label class="field"><span>Số điện thoại</span><input name="phone" value="${esc(u.phone)}"></label>
        <label class="field"><span>Email (không đổi được)</span><input value="${esc(u.email)}" disabled></label> <span class="badge ok">Đã xác thực</span>
        <button class="btn">Lưu</button></form>`;
    if (tab === 'password')
      body = `<form data-form="changePassword" class="stack" style="max-width:420px"><label class="field"><span>Mật khẩu hiện tại</span><input type="password" name="old"></label><label class="field"><span>Mật khẩu mới</span><input type="password" name="password"></label><button class="btn">Đổi mật khẩu</button>${note('Đổi mật khẩu xong, mọi phiên khác bị đăng xuất.')}</form>`;
    if (tab === 'addresses')
      body = `<div class="grid cols-2">${[
        ['Nhà riêng', '12 Nguyễn Huệ, Phường Bến Nghé, TP. Hồ Chí Minh', true],
        ['Công ty', '8 Lý Thường Kiệt, Phường Hoàn Kiếm, Hà Nội', false],
      ]
        .map(
          ([l, a, def]) =>
            `<div class="card"><div class="row between"><b>${l}</b>${def ? '<span class="badge">Mặc định</span>' : ''}</div><p class="small">${u.name} · ${u.phone}<br>${a}</p><div class="row"><button class="btn sm secondary" data-act="toastMsg" data-msg="Mở form sửa địa chỉ">Sửa</button><button class="btn sm ghost" data-act="toastMsg" data-msg="Đã xóa địa chỉ">Xóa</button></div></div>`,
        )
        .join('')}</div>
        <button class="btn secondary" style="margin-top:12px" data-act="toastMsg" data-msg="Mở form thêm địa chỉ (tối đa 10)">Thêm địa chỉ</button>`;
    if (tab === 'privacy') {
      const c = S.consent || { personalization: false, marketing: false };
      const interests = S.recent.map(PK.prod).filter(Boolean);
      body = `<div class="grid cols-2"><div class="card stack"><h3>Lựa chọn của bạn</h3>
        <label class="switch"><input type="checkbox" checked disabled> Cần thiết (luôn bật)</label>
        <label class="switch"><input type="checkbox" data-change="togglePersonalization" ${c.personalization ? 'checked' : ''}> Cá nhân hóa</label>
        <label class="switch"><input type="checkbox" data-change="toggleMarketing" ${c.marketing ? 'checked' : ''}> Tiếp thị</label>
        <p class="small muted">Phiên bản chính sách 2026-09. Mỗi lần đổi được lưu thành 1 bản ghi mới.</p></div>
        <div class="card stack"><h3>Hệ thống hiểu bạn thích gì</h3>
        ${
          c.personalization
            ? `<p>Quan tâm: <b>Laptop gaming</b> (nhiều nhất), Tai nghe.<br>Thương hiệu hay xem: ASUS, MSI.<br>Tầm giá laptop: 25 đến 35 triệu.<br>Ưu tiên RAM 16GB, màn hình 16 inch.<br>Đã mua: chuột Logitech G502.</p>
          <p class="small muted">${interests.length} sản phẩm đã xem gần đây.</p><button class="btn secondary sm" data-act="clearHistory">Xóa lịch sử</button>`
            : '<p class="muted">Bạn chưa bật cá nhân hóa nên hệ thống không lưu sở thích.</p>'
        }</div></div>`;
    }
    if (tab === 'delete')
      body = `<div class="card stack" style="max-width:480px"><h3>Xóa tài khoản</h3><p>Email và tên sẽ bị ẩn danh, bạn không thể đăng nhập lại. Đơn hàng cũ vẫn được giữ cho kế toán.</p>
        <form data-form="deleteAccount" class="stack"><label class="field"><span>Nhập mật khẩu để xác nhận</span><input type="password" name="password" placeholder="12345678"></label><button class="btn danger">Xóa vĩnh viễn</button></form></div>`;
    return `<h1>Tài khoản của tôi</h1><div class="tabs">${tabs.map(([k, l]) => `<a href="#/account?tab=${k}" class="${tab === k ? 'on' : ''}">${l}</a>`).join('')}</div>${body}`;
  });
  act('toastMsg', (d) => toast(d.msg));
  form('profile', () => toast('Đã lưu thông tin'));
  form('changePassword', (data) =>
    String(data.password).length < 8
      ? toast('Mật khẩu mới cần tối thiểu 8 ký tự')
      : toast('Đã đổi mật khẩu, các phiên khác đã đăng xuất'),
  );
  change('togglePersonalization', (el) => {
    const S = PK.S;
    S.consent = Object.assign({ marketing: false }, S.consent, {
      personalization: el.checked,
      at: PK.now(),
    });
    if (!el.checked) S.recent = [];
    toast(el.checked ? 'Đã bật cá nhân hóa' : 'Đã tắt cá nhân hóa và xóa ngay dữ liệu hành vi');
    PK.render();
  });
  change('toggleMarketing', (el) => {
    PK.S.consent = Object.assign({ personalization: false }, PK.S.consent, {
      marketing: el.checked,
      at: PK.now(),
    });
    toast('Đã lưu');
  });
  act('clearHistory', () => {
    PK.S.recent = [];
    toast('Đã xóa lịch sử xem');
    PK.render();
  });
  form('deleteAccount', (data) => {
    if (data.password !== '12345678') return toast('Mật khẩu không đúng');
    PK.S.role = 'guest';
    toast('Đã xóa tài khoản (UserDeletedEvent). Email gốc không còn trong hệ thống.');
    PK.go('/');
  });

  // ================= knowledge-base: trung tâm hỗ trợ =================
  const TYPE_LABEL = {
    policy: 'Chính sách',
    faq: 'Câu hỏi thường gặp',
    guide: 'Hướng dẫn chọn mua',
    internal: 'Nội bộ',
  };
  const anchor = (s) =>
    norm(s)
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-|-$/g, '');
  route('/help', () => {
    const docs = PK.S.kbDocs.filter((d) => d.status === 'published' && d.type !== 'internal');
    return `<h1>Trung tâm hỗ trợ</h1><p class="muted">Chính sách, câu hỏi thường gặp và hướng dẫn chọn mua.</p>
      <div class="grid cols-3">${['policy', 'faq', 'guide']
        .map(
          (t) =>
            `<div class="card"><h3>${TYPE_LABEL[t]}</h3>${docs
              .filter((d) => d.type === t)
              .map(
                (d) =>
                  `<a href="#/help/${d.slug}" style="display:block;padding:4px 0">${d.title}</a>`,
              )
              .join('')}</div>`,
        )
        .join('')}</div>
      ${note('Tài liệu nội bộ và bản nháp không hiện ở đây. Trích dẫn [n] trong chat mở đúng mục của tài liệu.')}`;
  });
  route('/help/:slug', (p) => {
    const d = PK.S.kbDocs.find(
      (x) => x.slug === p.slug && x.status === 'published' && x.type !== 'internal',
    );
    if (!d) return '<h1>Không tìm thấy tài liệu</h1>';
    return `<div class="breadcrumb"><a href="#/help">Trung tâm hỗ trợ</a> / ${TYPE_LABEL[d.type]}</div>
      <div class="grid" style="grid-template-columns:220px 1fr;gap:32px"><aside><b class="small">Mục lục</b>${d.body.map(([h]) => `<a href="#/help/${d.slug}?a=${anchor(h)}" style="display:block;padding:4px 0" class="small">${h}</a>`).join('')}</aside>
      <article class="doc-body"><h1>${d.title}</h1><p class="small muted">Cập nhật ${d.updatedAt} · Hiệu lực từ ${d.effectiveFrom}</p>
        ${d.body.map(([h, t]) => `<h3 id="${anchor(h)}">${h}</h3><p>${esc(t)}</p>`).join('')}</article></div>`;
  });

  // ================= catalog: so sánh =================
  act('addCompare', (d) => {
    const S = PK.S;
    const p = PK.prod(d.pid);
    S.compare = (S.compare || []).filter((id) => PK.prod(id).cat === p.cat);
    if (!S.compare.includes(p.id)) S.compare.push(p.id);
    if (S.compare.length > 4) S.compare.shift();
    PK.go('/compare');
  });
  route('/compare', (_, q) => {
    const S = PK.S;
    const ids = q.ids ? q.ids.split(',') : S.compare || [];
    const items = ids.map(PK.prod).filter(Boolean);
    if (!items.length)
      return `<h1>So sánh</h1><p class="muted">Chưa có sản phẩm. Nhấn "So sánh" trên trang sản phẩm (tối đa 4 sản phẩm cùng danh mục).</p>`;
    const c = PK.cat(items[0].cat);
    const attrs = D.attributes[c.parent || c.id] || [];
    const rows = [
      ['Giá', (p) => money(p.price)],
      ['Đánh giá', (p) => p.rating + '★'],
      ...attrs.map((a) => [
        a.label,
        (p) => (p.specs[a.key] ?? '-') + (a.unit && p.specs[a.key] != null ? ' ' + a.unit : ''),
      ]),
      ['Bảo hành', (p) => p.warranty + ' tháng'],
    ];
    return `<h1>So sánh ${c.name.toLowerCase()}</h1>
      <div class="table-wrap"><table class="tbl"><tr><th></th>${items.map((p) => `<th style="text-transform:none;font-size:14px;color:var(--color-ink)">${productImg(p, 80)}<br><a href="#/p/${p.slug}">${esc(p.name)}</a></th>`).join('')}</tr>
      ${rows
        .map(([l, f]) => {
          const vals = items.map(f);
          const diff = new Set(vals).size > 1;
          return `<tr><td class="muted">${l}</td>${vals.map((v) => `<td style="${diff ? 'background:#fffbea' : ''}">${esc(v)}</td>`).join('')}</tr>`;
        })
        .join('')}</table></div>
      ${note('Dòng tô vàng là thông số khác nhau. Thêm sản phẩm khác danh mục sẽ báo lỗi (ở đây tự loại bỏ).')}`;
  });

  // ================= shopping-assistant: widget chat =================
  const LIMIT = () =>
    PK.S.role === 'customer' ? PK.S.aiSettings.userLimit : PK.S.aiSettings.guestLimit;
  function currentProduct() {
    const { path } = PK.parseHash();
    return path.startsWith('/p/') ? PK.prodBySlug(path.slice(3)) : null;
  }
  function renderChat(focus) {
    const S = PK.S;
    const root = document.getElementById('chat-root');
    const C = S.chat;
    // Giữ focus ở ô nhập nếu người dùng đang thao tác trong chat
    const keepFocus = focus || root.contains(document.activeElement);
    if (!C.open) {
      root.innerHTML = `<button class="chat-fab" data-act="chatOpen">Hỏi trợ lý Pickory</button>`;
      return;
    }
    const cp = currentProduct();
    const suggestions = cp
      ? ['Máy này có nóng không?', 'So với máy tương tự?', 'Đổi trả trong bao lâu?']
      : [
          'Laptop chơi game tầm 25 triệu, nhẹ để mang đi học',
          'Tai nghe chống ồn đi máy bay',
          'Đổi trả trong bao lâu?',
          'Đơn của tôi đến đâu rồi?',
        ];
    const statusLabel = {
      bot: '<span class="badge ai">Trợ lý AI</span>',
      waiting_human: '<span class="badge warn">Đang chờ nhân viên</span>',
      human: '<span class="badge ok">Lan - CSKH</span>',
    }[C.status];
    root.innerHTML = `<div class="chat-panel" role="dialog" aria-label="Trợ lý Pickory">
      <div class="chat-head"><b>Trợ lý Pickory</b> ${statusLabel}<span class="spacer"></span>
        ${C.status === 'bot' ? '<button class="btn sm secondary" data-act="handoff">Gặp nhân viên</button>' : ''}
        <button class="btn sm ghost" data-act="chatClose" aria-label="Đóng">✕</button></div>
      <div class="chat-body" id="chat-body">
        ${!C.noticeShown ? '' : '<div class="msg system">Hội thoại được lưu 90 ngày để cải thiện chất lượng hỗ trợ.</div>'}
        ${C.messages.length ? '' : `<div class="msg assistant">Chào bạn! Mình là trợ lý AI của Pickory. Mình giúp tìm, so sánh sản phẩm và giải đáp chính sách.</div><div class="row">${suggestions.map((s) => `<button class="chip" data-act="chatSuggest" data-text="${esc(s)}">${esc(s)}</button>`).join('')}</div>`}
        ${C.messages.map((m, i) => renderMsg(m, i)).join('')}
        ${C.typing ? '<div class="typing">Trợ lý đang trả lời...</div>' : ''}
      </div>
      <div class="chat-foot">
        ${
          C.messages.length && C.status === 'bot' && !C.typing
            ? `<div class="row" style="margin-bottom:8px">${suggestions
                .slice(0, 2)
                .map(
                  (s) =>
                    `<button class="chip" data-act="chatSuggest" data-text="${esc(s)}">${esc(s)}</button>`,
                )
                .join('')}</div>`
            : ''
        }
        <form data-form="chat"><input name="text" maxlength="1000" placeholder="Hỏi về sản phẩm, đơn hàng, chính sách..." autocomplete="off" aria-label="Tin nhắn"><button class="btn" aria-label="Gửi">Gửi</button></form>
        <div class="chat-disclaimer">Trợ lý AI có thể sai. Thông tin giá và tồn kho trên thẻ sản phẩm là chính xác. · Đã dùng ${C.used}/${LIMIT()} tin hôm nay</div>
      </div></div>`;
    const body = document.getElementById('chat-body');
    body.scrollTop = body.scrollHeight;
    if (keepFocus) root.querySelector('input[name=text]').focus();
  }
  function renderMsg(m, i) {
    if (m.role === 'user') return `<div class="msg user">${esc(m.text)}</div>`;
    if (m.role === 'system') {
      let extra = '';
      if (m.form === 'callback' && !m.done)
        extra = `<form data-form="callback" data-i="${i}" class="row" style="margin-top:6px;justify-content:center"><input name="phone" placeholder="Số điện thoại" style="width:160px"><button class="btn sm">Gửi</button></form>`;
      if (m.form === 'csat' && !m.done)
        extra = `<div class="row" style="justify-content:center;margin-top:6px">${[1, 2, 3, 4, 5].map((n) => `<button class="btn sm secondary" data-act="csat" data-i="${i}" data-n="${n}">${n}★</button>`).join('')}</div>`;
      if (m.form === 'handoffBtn')
        extra = `<div style="margin-top:6px"><button class="btn sm" data-act="handoff">Gặp nhân viên</button></div>`;
      return `<div class="msg system">${esc(m.text)}${extra}</div>`;
    }
    if (m.role === 'staff')
      return `<div class="msg staff"><div class="who">${esc(m.staffName || 'Lan - CSKH')}</div>${esc(m.text)}</div>`;
    // assistant: trích dẫn [n] thành link tới đúng mục tài liệu
    let text = esc(m.text).replace(/\[(\d)\]/g, (_, n) => {
      const c = (m.cites || [])[Number(n) - 1];
      return c
        ? `<a class="cite" href="#/help/${c.slug}?a=${c.anchor}" title="${esc(c.title)}">[${n}]</a>`
        : `[${n}]`;
    });
    let extra = '';
    if (m.cards) extra += m.cards.map((id) => miniCard(PK.prod(id))).join('');
    if (m.compare) extra += compareMini(m.compare);
    if (m.order) {
      const o = PK.order(m.order);
      extra += `<div class="mini-card" style="display:block"><b>${o.code}</b> · ${PK.orderBadge(o.status)}<div class="small">${o.items.map((x) => esc(x.name)).join(', ')}</div><div class="small muted">Vận đơn ${o.tracking || 'chưa có'} · ${PK.SHIP_STATUS[o.shipStatus] || ''}</div><a class="small" href="#/orders/${o.code}">Xem chi tiết</a></div>`;
    }
    if (m.cites && m.cites.length)
      extra += `<div class="small muted" style="margin-top:6px">Nguồn: ${m.cites.map((c, k) => `<a href="#/help/${c.slug}?a=${c.anchor}">[${k + 1}] ${esc(c.title)}</a>`).join(' · ')}</div>`;
    const fb = m.noFeedback
      ? ''
      : `<div class="fb"><button class="${m.fb === 'up' ? 'on' : ''}" data-act="fb" data-i="${i}" data-v="up" aria-label="Hữu ích">👍</button><button class="${m.fb === 'down' ? 'on' : ''}" data-act="fb" data-i="${i}" data-v="down" aria-label="Chưa tốt">👎</button>${m.fb === 'down' && !m.fbReason ? ['sai thông tin', 'không hữu ích', 'khác'].map((r) => `<button data-act="fbReason" data-i="${i}" data-r="${r}">${r}</button>`).join('') : ''}</div>`;
    return `<div class="msg assistant">${text.replace(/\n/g, '<br>')}${extra}${fb}</div>`;
  }
  function miniCard(p) {
    const v = p.variants ? p.variants[0] : null;
    const stock = v ? v.stock : p.stock;
    return `<div class="mini-card"><div class="thumb">${productImg(p, 52)}</div><div style="flex:1;min-width:0"><a href="#/p/${p.slug}"><b class="small">${esc(p.name)}</b></a>
      <div><b>${money(v ? v.price : p.price)}</b></div>${p.variants ? `<select class="small" style="padding:2px;margin:2px 0">${p.variants.map((x) => `<option>${Object.values(x.opts).join(' ')}</option>`).join('')}</select>` : ''}
      <div class="small muted">${stock > 0 ? 'Còn hàng' : 'Hết hàng'}</div></div>
      <button class="btn sm" ${stock > 0 ? '' : 'disabled'} data-act="addToCart" data-pid="${p.id}" data-sku="${v ? v.sku : p.slug.toUpperCase()}" data-source="shopping-assistant">Thêm vào giỏ</button></div>`;
  }
  function compareMini(ids) {
    const [a, b] = ids.map(PK.prod);
    const rows = [
      ['Giá', money(a.price), money(b.price)],
      ['CPU', a.specs.cpu, b.specs.cpu],
      ['GPU', a.specs.gpu, b.specs.gpu],
      ['RAM', a.specs.ram_gb + 'GB', b.specs.ram_gb + 'GB'],
      ['Trọng lượng', a.specs.weight + ' kg', b.specs.weight + ' kg'],
    ];
    return `<table class="tbl small" style="margin-top:6px"><tr><th></th><th>${esc(a.name.replace('Laptop ', ''))}</th><th>${esc(b.name.replace('Laptop ', ''))}</th></tr>${rows.map((r) => `<tr><td class="muted">${r[0]}</td><td>${esc(r[1])}</td><td>${esc(r[2])}</td></tr>`).join('')}</table><a class="small" href="#/compare?ids=${a.id},${b.id}">Xem so sánh đầy đủ</a>`;
  }
  act('chatOpen', () => {
    const C = PK.S.chat;
    C.open = true;
    C.noticeShown = true;
    renderChat(true);
    PK.save();
  });
  act('chatClose', () => {
    PK.S.chat.open = false;
    renderChat();
    PK.save();
  });
  act('askAbout', () => {
    PK.S.chat.open = true;
    PK.S.chat.noticeShown = true;
    renderChat();
  });
  act('chatSuggest', (d) => sendChat(d.text));
  form('chat', (data, f) => {
    const t = String(data.text || '').trim();
    if (t) sendChat(t);
  });
  act('fb', (d) => {
    const m = PK.S.chat.messages[Number(d.i)];
    m.fb = d.v;
    renderChat();
    PK.save();
  });
  act('fbReason', (d) => {
    PK.S.chat.messages[Number(d.i)].fbReason = d.r;
    toast('Cảm ơn góp ý của bạn');
    renderChat();
    PK.save();
  });
  act('csat', (d) => {
    const m = PK.S.chat.messages[Number(d.i)];
    m.done = true;
    m.text = `Bạn đã đánh giá ${d.n}★. Cảm ơn bạn! Trợ lý AI sẽ tiếp tục hỗ trợ.`;
    renderChat();
    PK.save();
  });
  form('callback', (data, f) => {
    const S = PK.S;
    if (!/^0\d{9}$/.test(String(data.phone).replace(/\s/g, '')))
      return toast('Số điện thoại chưa đúng');
    const m = S.chat.messages[Number(f.dataset.i)];
    m.done = true;
    m.text = 'Đã ghi nhận số điện thoại. CSKH sẽ gọi lại trong giờ làm việc.';
    S.callbacks.unshift({
      id: 'cb' + Date.now(),
      phone: String(data.phone)
        .replace(/\s/g, '')
        .replace(/(\d{4})\d{3}(\d{3})/, '$1 *** $2'),
      note: 'Từ chat ngoài giờ',
      conv: 'c-live',
      createdAt: PK.now(),
      status: 'pending',
    });
    renderChat();
    PK.save();
  });

  // Đẩy tin vào hội thoại đang chờ ở support-desk (để vai trò CSKH thấy)
  function syncLive(role, text) {
    const S = PK.S;
    const conv = S.convs.find((c) => c.id === 'c-live');
    if (conv && conv.status !== 'closed') conv.messages.push([role, text]);
  }
  function push(m) {
    PK.S.chat.messages.push(m);
    syncLive(m.role, m.text);
  }
  act('handoff', () => handoff('Khách yêu cầu gặp nhân viên'));
  function handoff(reason) {
    const S = PK.S;
    const C = S.chat;
    if (S.outOfHours) {
      push({
        role: 'system',
        text: 'Hiện ngoài giờ hỗ trợ (8:00 đến 21:00). Bạn để lại SĐT, CSKH sẽ gọi lại.',
        form: 'callback',
      });
    } else {
      C.status = 'waiting_human';
      S.convs = S.convs.filter((c) => c.id !== 'c-live');
      S.convs.unshift({
        id: 'c-live',
        customer: S.role === 'customer' ? 'Nguyễn Minh Anh (bạn)' : 'Khách vãng lai (bạn)',
        userId: S.role === 'customer' ? 'u1' : null,
        status: 'waiting_human',
        reason,
        waitingSince: 0,
        assignedTo: null,
        page: PK.parseHash().path,
        messages: C.messages.filter((m) => m.role !== 'system').map((m) => [m.role, m.text]),
        suggested: C.messages.flatMap((m) => m.cards || []),
      });
      push({
        role: 'system',
        text: 'Đã chuyển cho nhân viên CSKH. Thời gian chờ dự kiến khoảng 3 phút. Trợ lý AI tạm dừng.',
      });
      toast('Mẹo: đổi vai trò sang "CSKH: Lan" để nhận hội thoại này');
    }
    renderChat();
    PK.save();
  }

  function sendChat(text) {
    const S = PK.S;
    const C = S.chat;
    if (text.length > 1000) return toast('Tin nhắn tối đa 1.000 ký tự');
    push({ role: 'user', text });
    if (C.status !== 'bot') {
      renderChat();
      return PK.save();
    }
    if (C.used >= LIMIT()) {
      push({
        role: 'system',
        text: `Bạn đã dùng hết ${LIMIT()} tin nhắn với trợ lý hôm nay${S.role === 'guest' ? '. Đăng nhập để có 50 tin mỗi ngày' : ''}. Bạn vẫn có thể gặp nhân viên CSKH.`,
        form: 'handoffBtn',
      });
      renderChat();
      return PK.save();
    }
    if (S.aiDown || S.budgetPct >= 120) {
      push({
        role: 'system',
        text: 'Trợ lý tạm thời không khả dụng. Bạn có thể gặp nhân viên CSKH.',
        form: 'handoffBtn',
      });
      renderChat();
      return PK.save();
    }
    C.used += 1;
    C.typing = true;
    renderChat();
    setTimeout(() => {
      C.typing = false;
      const reply = botReply(text);
      if (reply === 'HANDOFF') return handoff('Khách yêu cầu gặp nhân viên');
      push(reply);
      if (reply.noInfo) {
        C.noInfo += 1;
        if (C.noInfo >= 2) {
          C.noInfo = 0;
          return handoff('Bot không trả lời được 2 lần');
        }
      } else C.noInfo = 0;
      renderChat();
      PK.save();
    }, 700);
  }

  // Kịch bản trả lời giả lập theo các tool: search_products, compare_products, get_review_insights, search_knowledge, get_my_orders
  function botReply(text) {
    const S = PK.S;
    const t = norm(text);
    const cp = currentProduct();
    const lastCards = S.chat.messages.flatMap((m) => m.cards || []);
    if (/nhan vien|nguoi that|tu van vien/.test(t)) return 'HANDOFF';
    if (/so sanh|so voi/.test(t)) {
      let ids = lastCards.slice(-2);
      if (cp && ids.length < 2)
        ids = [cp.id, S.products.find((p) => p.cat === cp.cat && p.id !== cp.id).id];
      if (ids.length < 2) ids = ['p1', 'p2'];
      const [a, b] = ids.map(PK.prod);
      return {
        role: 'assistant',
        text: `So sánh nhanh ${a.name} và ${b.name}: ${a.specs.weight < b.specs.weight ? a.name : b.name} nhẹ hơn, cả hai đều có ${a.specs.gpu}. Giá chính xác xem trên thẻ sản phẩm.`,
        compare: [a.id, b.id],
        tools: ['compare_products'],
      };
    }
    if (/nong|tan nhiet|on khong|quat/.test(t)) {
      const p = cp && D.reviewSummaries[cp.id] ? cp : PK.prod('p1');
      return {
        role: 'assistant',
        text: `Theo 11 đánh giá của khách đã mua ${p.name}, quạt hơi ồn khi chơi game lâu nhưng nhiệt độ vẫn giữ dưới 85 độ. Khi làm việc văn phòng máy chạy êm.`,
        cards: [p.id],
        tools: ['get_review_insights'],
      };
    }
    if (/doi tra|tra hang/.test(t))
      return {
        role: 'assistant',
        text: 'Bạn được đổi trả trong 7 ngày kể từ khi nhận hàng nếu sản phẩm lỗi do nhà sản xuất [1]. Sản phẩm còn nguyên hộp được đổi sang mẫu khác trong 3 ngày [1].',
        cites: [
          {
            slug: 'chinh-sach-doi-tra',
            anchor: 'thoi-han-doi-tra',
            title: 'Chính sách đổi trả > Thời hạn đổi trả',
          },
        ],
        tools: ['search_knowledge'],
      };
    if (/bao hanh|roi vo/.test(t))
      return {
        role: 'assistant',
        text: 'Sản phẩm được bảo hành theo thời gian của hãng [1]. Riêng hàng bị rơi vỡ không được bảo hành miễn phí, Pickory hỗ trợ báo giá sửa [1].',
        cites: [
          {
            slug: 'chinh-sach-bao-hanh',
            anchor: 'hang-bi-roi-vo',
            title: 'Chính sách bảo hành > Hàng bị rơi vỡ',
          },
        ],
        tools: ['search_knowledge'],
      };
    if (/giao hang|ship|bao lau|van chuyen/.test(t))
      return {
        role: 'assistant',
        text: 'Pickory giao qua GHN, nội thành TP. Hồ Chí Minh khoảng 1 ngày, tỉnh khác 2 đến 4 ngày [1]. Đơn từ 500.000 đ được miễn phí vận chuyển [1].',
        cites: [
          {
            slug: 'chinh-sach-van-chuyen',
            anchor: 'thoi-gian-giao',
            title: 'Chính sách vận chuyển > Thời gian giao',
          },
        ],
        tools: ['search_knowledge'],
      };
    if (/don (hang|cua)|den dau|ma don|don toi/.test(t)) {
      if (S.role !== 'customer')
        return {
          role: 'assistant',
          text: 'Bạn đăng nhập để mình tra đơn nhé. Nếu mua không cần tài khoản, hãy dùng link tra cứu trong email xác nhận đơn.',
          tools: [],
        };
      return {
        role: 'assistant',
        text: 'Đơn gần nhất của bạn đang được GHN giao, dự kiến tới trong hôm nay.',
        order: 'PK7KX2M9QD',
        tools: ['get_my_orders', 'get_order_status'],
      };
    }
    if (/tai nghe/.test(t))
      return {
        role: 'assistant',
        text: 'Với nhu cầu đi máy bay, chống ồn là quan trọng nhất. Mình gợi ý 3 mẫu chống ồn tốt theo đánh giá của khách:',
        cards: ['p17', 'p18', 'p19'],
        tools: ['search_products'],
      };
    if (/dien thoai|iphone|samsung|pin trau|chup anh/.test(t))
      return {
        role: 'assistant',
        text: 'Mình gợi ý vài mẫu điện thoại pin lâu và chụp ảnh đẹp trong tầm giá phổ biến:',
        cards: ['p14', 'p15', 'p13'],
        tools: ['search_products'],
      };
    if (/laptop|game|may tinh/.test(t)) {
      const m = t.match(/(\d+)\s*(trieu|tr|cu)/);
      const max = m ? Number(m[1]) * 1000000 * 1.05 : 30000000;
      const light = /nhe|di hoc|mang di/.test(t);
      let picks = S.products.filter(
        (p) => p.cat === 'laptop-gaming' && p.status === 'active' && p.stock > 0 && p.price <= max,
      );
      if (light) picks.sort((a, b) => a.specs.weight - b.specs.weight);
      picks = picks.slice(0, 3);
      if (!picks.length)
        return {
          role: 'assistant',
          text: 'Mình chưa tìm được laptop gaming còn hàng trong tầm giá này. Bạn có thể nới ngân sách thêm một chút không?',
          tools: ['search_products'],
        };
      return {
        role: 'assistant',
        text: `Trong tầm ${m ? m[1] + ' triệu' : 'giá bạn nêu'}${light ? ' và cần nhẹ để mang đi học' : ''}, mình gợi ý ${picks.length} mẫu:\n• ${picks.map((p) => `${p.name}: ${p.specs.gpu}, nặng ${p.specs.weight} kg`).join('\n• ')}\nGiá chính xác xem trên thẻ sản phẩm.`,
        cards: picks.map((p) => p.id),
        tools: ['search_products'],
      };
    }
    if (/thoi tiet|bong da|chinh tri|lap trinh|viet code|bai tap/.test(t))
      return {
        role: 'assistant',
        text: 'Mình chỉ hỗ trợ về sản phẩm, đơn hàng và chính sách của Pickory thôi. Bạn cần tìm sản phẩm gì không?',
        tools: [],
      };
    return {
      role: 'assistant',
      text: 'Mình chưa có thông tin về câu hỏi này.',
      noInfo: true,
      tools: ['search_knowledge'],
    };
  }

  PK.renderChat = renderChat;
})();
