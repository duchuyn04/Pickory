/* Cửa hàng: identity (khách), catalog, smart-search, recommendations, personalized-content,
   commerce, shipping, activity-tracking, knowledge-base (trung tâm hỗ trợ), shopping-assistant (chat). */
/* eslint-disable */
(function () {
  const PK = window.PK;
  const D = PK.D;
  const {
    icon,
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
  const crumbs = (items) =>
    `<nav class="breadcrumb" aria-label="Breadcrumb">${items.map(([l, h], i) => (i ? icon('chevronRight', 14) : '') + (h ? `<a href="${h}">${l}</a>` : `<span>${l}</span>`)).join('')}</nav>`;
  const sectionHead = (title, sub, link) =>
    `<div class="section-head"><div><h2>${title}</h2>${sub ? `<p>${sub}</p>` : ''}</div>${link ? `<a class="link" href="${link[1]}">${link[0]} ${icon('arrowRight', 16)}</a>` : ''}</div>`;
  const dmy = (s) => s.slice(0, 10).split('-').reverse().join('/');

  // ================= Layout cửa hàng =================
  PK.layouts.shop = function (html) {
    const S = PK.S;
    const user = PK.me();
    const cats = D.categories.filter((c) => !c.parent);
    return `
    <div class="announce"><div class="inner">
      <span>${icon('truck', 14)} Miễn phí giao hàng cho đơn từ 500.000 đ</span>
      <span>${icon('refresh', 14)} Đổi trả trong 7 ngày</span>
      <span>${icon('shield', 14)} Hàng chính hãng, bảo hành tận nơi</span>
    </div></div>
    <header class="shop-header">
      <div class="inner">
        <a class="logo" href="#/" aria-label="Pickory, trang chủ">pickory<i></i></a>
        <form class="search-box" data-form="search" role="search">
          ${icon('search', 18)}
          <input name="q" placeholder="Bạn cần tìm gì? Ví dụ: laptop chơi game dưới 25 triệu" autocomplete="off"
            data-input="typeahead" aria-label="Tìm kiếm sản phẩm" role="combobox" aria-expanded="false" aria-controls="ta" maxlength="200">
          <div id="ta" class="typeahead hidden" role="listbox"></div>
        </form>
        <span class="spacer"></span>
        <div class="hdr-actions">
          <a class="hdr-link" href="#/help">${icon('help', 20)}<span>Hỗ trợ</span></a>
          ${S.role === 'customer' ? `<a class="hdr-link" href="#/orders">${icon('package', 20)}<span>Đơn hàng</span></a><a class="hdr-link" href="#/account">${icon('user', 20)}<span>${esc(user.name.split(' ').pop())}</span></a>` : `<a class="hdr-link" href="#/login">${icon('user', 20)}<span>Đăng nhập</span></a>`}
          <a class="hdr-link" href="#/cart" aria-label="Giỏ hàng, ${cartCount()} sản phẩm">${icon('bag', 20)}<span>Giỏ hàng</span>${cartCount() ? `<b class="count">${cartCount()}</b>` : ''}</a>
        </div>
      </div>
      <nav class="nav-cats" aria-label="Danh mục">${cats.map((c) => `<a href="#/c/${c.id}">${c.name}</a>`).join('')}<a href="#/c/laptop-gaming">Gaming</a><a href="#/help">Trung tâm hỗ trợ</a></nav>
    </header>
    <main class="shop" id="main">${html}</main>
    <footer class="footer">
      <div class="inner">
        <div><a class="logo" href="#/">pickory<i></i></a><p style="margin-top:12px;max-width:32ch">Laptop, điện thoại, tai nghe chính hãng. Tư vấn bởi trợ lý AI và đội ngũ CSKH.</p><p>Hotline <b>1900 0000</b> · 8:00 đến 21:00</p></div>
        <div><h4>Mua sắm</h4>${cats.map((c) => `<a href="#/c/${c.id}">${c.name}</a>`).join('')}</div>
        <div><h4>Chính sách</h4>${PK.S.kbDocs
          .filter((d) => d.type === 'policy' && d.status === 'published')
          .map(
            (d) =>
              `<a href="#/help/${d.slug}">${d.title.replace('Chính sách ', '').replace(/^./, (x) => x.toUpperCase())}</a>`,
          )
          .join('')}</div>
        <div><h4>Hỗ trợ</h4><a href="#/help">Trung tâm hỗ trợ</a><a href="#/orders">Tra cứu đơn hàng</a><a href="#" data-act="openConsent">Cài đặt quyền riêng tư</a></div>
      </div>
      <div class="legal">© 2026 Pickory. Thanh toán qua VietQR (SePay) hoặc COD, giao hàng bởi GHN.</div>
    </footer>`;
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
    if (layout === 'shop' && path !== '/flows') renderChat();
    else chatRoot.innerHTML = '';
    let consentRoot = document.getElementById('consent-root');
    if (!consentRoot) {
      consentRoot = document.createElement('div');
      consentRoot.id = 'consent-root';
      document.body.appendChild(consentRoot);
    }
    const showConsent = layout === 'shop' && S.consent === null && path !== '/flows';
    consentRoot.innerHTML = showConsent ? consentBanner() : '';
    document.body.classList.toggle('consent-on', showConsent);
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
    return `<div class="consent" role="dialog" aria-labelledby="consent-title">
      <div><b id="consent-title">Gợi ý đúng gu bạn hơn?</b>
        <p>Nếu đồng ý, Pickory ghi nhận sản phẩm bạn xem và mua để cá nhân hóa gợi ý. Bạn đổi ý bất cứ lúc nào trong tài khoản. <a href="#/help/quyen-rieng-tu"><u>Chính sách quyền riêng tư</u></a> · <a href="#" data-act="openConsent"><u>Tùy chỉnh</u></a></p></div>
      <div class="two"><button class="btn secondary" data-act="consent" data-v="necessary">Chỉ cần thiết</button><button class="btn secondary" data-act="consent" data-v="all">Đồng ý</button></div>
    </div>`;
  }
  act('consent', (d) => {
    PK.S.consent = { personalization: d.v === 'all', marketing: d.v === 'all', at: PK.now() };
    toast(d.v === 'all' ? 'Đã bật cá nhân hóa' : 'Chỉ dùng dữ liệu cần thiết');
    PK.render();
  });
  act('openConsent', () => {
    const c = PK.S.consent || { personalization: false, marketing: false };
    modal(`<h2>Quyền riêng tư</h2><p class="muted">Chọn loại dữ liệu Pickory được dùng. Lựa chọn được lưu kèm phiên bản chính sách.</p>
      <form data-form="consentCustom">
        <label class="pref-row switch"><div><b>Cần thiết</b><p>Giỏ hàng, đăng nhập, bảo mật. Luôn bật.</p></div><input type="checkbox" checked disabled></label>
        <label class="pref-row switch"><div><b>Cá nhân hóa</b><p>Ghi nhận sản phẩm bạn xem để gợi ý "Dành cho bạn".</p></div><input type="checkbox" name="personalization" ${c.personalization ? 'checked' : ''}></label>
        <label class="pref-row switch"><div><b>Tiếp thị</b><p>Nhận ưu đãi phù hợp với bạn.</p></div><input type="checkbox" name="marketing" ${c.marketing ? 'checked' : ''}></label>
        <div class="row" style="margin-top:20px;justify-content:flex-end"><button type="button" class="btn secondary" data-act="closeModal">Hủy</button><button class="btn">Lưu lựa chọn</button></div>
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
    toast('Đã lưu lựa chọn của bạn');
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
              `<a href="#/p/${p.slug}" role="option"><span class="thumb">${productImg(p, 32)}</span><span>${esc(p.name)}</span><span class="spacer"></span><b class="small">${money(p.price)}</b></a>`,
          )
          .join('');
    if (cats.length)
      html +=
        `<div class="grp">Danh mục</div>` +
        cats
          .map((c) => `<a href="#/c/${c.id}" role="option">${icon('list', 16)} ${c.name}</a>`)
          .join('');
    if (brands.length)
      html +=
        `<div class="grp">Thương hiệu</div>` +
        brands
          .map(
            (b) =>
              `<a href="#/search?q=${encodeURIComponent(b)}" role="option">${icon('tag', 16)} ${b}</a>`,
          )
          .join('');
    if (!html)
      html = `<a href="#/search?q=${encodeURIComponent(el.value)}">${icon('search', 16)} Tìm "${esc(el.value)}"</a>`;
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
        label: `Dưới ${m[1]} ${m[2] === 'k' ? 'nghìn' : 'triệu'}`,
        max: unit(num(m[1]), m[2]),
        src: 'rule',
      });
    else if ((m = t.match(/tren\s*(\d+[.,]?\d*)\s*(trieu|tr|cu|k)/)))
      chips.push({
        key: 'price',
        label: `Trên ${m[1]} triệu`,
        min: unit(num(m[1]), m[2]),
        src: 'rule',
      });
    else if ((m = t.match(/tam\s*(\d+[.,]?\d*)\s*(trieu|tr|cu|k)/))) {
      const v = unit(num(m[1]), m[2]);
      chips.push({
        key: 'price',
        label: `Khoảng ${m[1]} triệu`,
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
        results = applyChips(
          pool,
          chips.filter((c) => c.key === 'price' || c.key === 'cat'),
        );
        if (!results.length) {
          results = applyChips(
            pool,
            chips.filter((c) => c.key === 'cat'),
          ).sort((a, b) => a.price - b.price);
          const price = chips.find((c) => c.key === 'price');
          relaxed = `Không có ${chips.find((c) => c.key === 'cat')?.label.toLowerCase() || 'sản phẩm'} ${price ? price.label.toLowerCase() : ''}. Đây là các lựa chọn gần nhất.`;
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
          `<span class="chip">${esc(c.label)}${S.showNotes ? `<span class="src">${c.src === 'llm' ? 'AI' : 'quy tắc'}</span>` : ''}<a class="x" href="${href('/search', { q: query, drop: [...drop, c.key].join(',') })}" aria-label="Bỏ lọc ${esc(c.label)}">${icon('x', 14)}</a></span>`,
      )
      .join('');
    return `
      ${crumbs([['Trang chủ', '#/'], ['Tìm kiếm']])}
      <div class="page-title"><div><h1>“${esc(query)}”</h1><p class="muted" style="margin:6px 0 0">${results.length} sản phẩm phù hợp</p></div></div>
      ${chips.length || drop.length ? `<div class="row tight" style="margin-bottom:20px">${chipHtml}${drop.length ? `<a class="link small" href="${href('/search', { q: query })}">Khôi phục bộ lọc</a>` : ''}</div>` : ''}
      ${relaxed ? `<div class="alert" style="margin-bottom:24px">${icon('info', 18)}<span>${esc(relaxed)}</span></div>` : ''}
      ${!useLlm ? note('AI lỗi hoặc hết ngân sách: tìm kiếm chạy full-text và quy tắc, người dùng không thấy lỗi, các chip do AI hiểu (Chống ồn, Nhẹ) không xuất hiện.') : ''}
      ${results.length ? `<div class="product-grid">${results.map((p) => productCard(p, { source: 'smart-search' })).join('')}</div>` : `<div class="empty"><b>Không tìm thấy sản phẩm phù hợp</b><p>Thử từ khóa ngắn hơn hoặc xem toàn bộ danh mục.</p><a class="btn secondary" href="#/c/laptop">Xem tất cả laptop</a></div>`}
      ${note('Câu thử: <a href="#/search?q=laptop%20choi%20game%20duoi%2025%20trieu">laptop choi game duoi 25 trieu</a> · <a href="#/search?q=tai%20nghe%20chong%20on%20di%20may%20bay">tai nghe chong on di may bay</a> · <a href="#/search?q=laptop%20gaming%20duoi%2015%20trieu">laptop gaming dưới 15 triệu</a> (nới tiêu chí) · <a href="#/search?q=rog%20g16">rog g16</a> · <a href="#/search?q=dt%20pin%20trau%20tam%2010tr">đt pin trâu tầm 10tr</a>. Câu không có kết quả được ghi cho admin.')}`;
  });

  // ================= Trang chủ: banner, "Dành cho bạn", đã xem gần đây =================
  function heroSlides() {
    const S = PK.S;
    const slides = [
      {
        eyebrow: 'Laptop gaming',
        title: 'RTX 4060, giảm đến 4 triệu',
        sub: 'ROG, Katana, Nitro chính hãng. Giao nhanh 2 giờ nội thành TP. Hồ Chí Minh.',
        link: '#/c/laptop-gaming',
        cta: 'Mua laptop gaming',
        pid: 'p1',
        ai: false,
      },
      {
        eyebrow: 'Tai nghe chống ồn',
        title: 'Yên tĩnh trên mọi chuyến bay',
        sub: 'Sony WH-1000XM5, AirPods Pro 2, JBL Tune 770NC. Bảo hành 12 tháng.',
        link: '#/search?q=tai%20nghe%20chong%20on',
        cta: 'Xem tai nghe',
        pid: 'p17',
        ai: false,
      },
    ];
    if (PK.personalized()) {
      S.aiContents
        .filter((c) => c.kind === 'banner' && c.status === 'active' && c.persona === persona())
        .forEach((c) =>
          slides.unshift({
            eyebrow: 'Dành riêng cho bạn',
            title: c.title,
            sub: c.subtitle,
            link: '#/c/laptop-gaming',
            cta: 'Khám phá',
            pid: 'p2',
            ai: true,
          }),
        );
    }
    return slides;
  }
  function rail(items, title, sub, reasonFn, source, link) {
    if (!items.length) return '';
    return `<section class="section">${sectionHead(title, sub, link)}<div class="rail">${items.map((p) => productCard(p, { source, reason: reasonFn ? reasonFn(p) : '' })).join('')}</div></section>`;
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
      variant = PK.personalized() ? 'control (chưa có hồ sơ)' : 'none (chưa đồng ý)';
      forYou = rail(
        [...active].sort((a, b) => b.reviewCount - a.reviewCount).slice(0, 10),
        'Bán chạy tuần này',
        'Được khách Pickory mua nhiều nhất 7 ngày qua',
        null,
        'recommendations.for-you',
        ['Xem tất cả', '#/c/laptop'],
      );
    } else {
      const llm = aiUsable('low');
      variant = llm ? 'algo_llm' : 'algo (AI lỗi hoặc hết ngân sách: lý do theo mẫu)';
      const picks = active
        .filter((p) => p.cat === 'laptop-gaming' || p.tags.includes('gaming'))
        .filter((p) => !S.recent.slice(0, 1).includes(p.id))
        .slice(0, 10);
      const llmReason = {
        'laptop-gaming': 'Cùng tầm RTX bạn hay xem, nhẹ hơn',
        chuot: 'Hợp với laptop gaming bạn quan tâm',
        'tai-nghe': 'Mic khử ồn cho chơi game đồng đội',
        'man-hinh': 'Màn 180Hz phát huy card RTX',
        balo: 'Vừa laptop 16 inch',
      };
      forYou = rail(
        picks,
        'Dành cho bạn',
        'Dựa trên những gì bạn đã xem',
        (p) =>
          llm
            ? llmReason[p.cat] || 'Hợp sở thích gaming của bạn'
            : `Vì bạn quan tâm ${PK.cat(p.cat).name.toLowerCase()}`,
        'recommendations.for-you',
      );
    }
    const recent = PK.personalized() ? S.recent.map(PK.prod).filter(Boolean).slice(0, 20) : [];
    const roots = D.categories.filter((c) => !c.parent);
    const heroProd = PK.prod(s.pid);
    return `
      <section class="hero" aria-roledescription="carousel">
        <div>
          <div class="eyebrow">${s.ai ? icon('sparkle', 14) : ''}${esc(s.eyebrow)}</div>
          <h1>${esc(s.title)}</h1><p>${esc(s.sub)}</p>
          <a class="btn" href="${s.link}">${esc(s.cta)} ${icon('arrowRight', 16)}</a>
        </div>
        <div class="visual">${productImg(heroProd, 320)}</div>
        <div class="hero-nav">${slides.map((_, i) => `<button class="bar ${i === idx ? 'on' : ''}" data-act="hero" data-i="${i}" aria-label="Banner ${i + 1}"></button>`).join('')}</div>
      </section>
      <div class="perks">
        <div class="perk"><span class="ic-wrap">${icon('truck', 20)}</span><span><b>Giao nhanh 2 giờ</b>Nội thành TP. Hồ Chí Minh</span></div>
        <div class="perk"><span class="ic-wrap">${icon('shield', 20)}</span><span><b>Chính hãng 100%</b>Bảo hành tới 36 tháng</span></div>
        <div class="perk"><span class="ic-wrap">${icon('refresh', 20)}</span><span><b>Đổi trả 7 ngày</b>Lỗi là đổi mới</span></div>
        <div class="perk"><span class="ic-wrap">${icon('chat', 20)}</span><span><b>Tư vấn 8:00 đến 21:00</b>Trợ lý AI và CSKH</span></div>
      </div>
      <section class="section">${sectionHead('Mua theo danh mục')}
        <div class="cat-strip">${roots
          .map((c) => {
            const sample = S.products.find((p) => PK.inCat(p, c.id) && p.status === 'active');
            return `<a class="cat-tile" href="#/c/${c.id}"><span class="pic">${productImg(sample, 84)}</span><b>${c.name}</b><span>${S.products.filter((p) => PK.inCat(p, c.id) && p.status === 'active').length} sản phẩm</span></a>`;
          })
          .join('')}</div></section>
      ${forYou}
      ${note(`Biến thể gợi ý: <b>${esc(variant)}</b>. Chưa đồng ý cá nhân hóa thì thấy "Bán chạy". Đồng ý rồi xem vài laptop gaming (hoặc vào vai Khách hàng) để thấy "Dành cho bạn" kèm lý do. Banner "Dành riêng cho bạn" chỉ hiện sau khi admin duyệt ở <a href="#/admin/ai-content">Nội dung AI</a>.`)}
      ${recent.length ? rail(recent, 'Bạn đã xem gần đây', '', null, 'category') : ''}
      ${rail(
        active.filter((p) => ['tai-nghe', 'chuot', 'sac'].includes(p.cat)),
        'Phụ kiện đáng mua',
        'Tai nghe, chuột, sạc từ các thương hiệu hàng đầu',
        null,
        'category',
        ['Xem phụ kiện', '#/c/phu-kien'],
      )}`;
  });
  act('hero', (d) => {
    PK.S.heroIdx = Number(d.i);
    PK.render();
  });
  act('trackClick', () => {
    // activity-tracking: ghi product click kèm source, chỉ khi đã đồng ý
  });

  // ================= catalog: trang danh mục có bộ lọc =================
  const PRICE_RANGES = [
    ['', 'Mọi mức giá'],
    ['0-10000000', 'Dưới 10 triệu'],
    ['10000000-20000000', '10 đến 20 triệu'],
    ['20000000-30000000', '20 đến 30 triệu'],
    ['30000000-999999999', 'Trên 30 triệu'],
  ];
  route('/c/:id', (p, q) => {
    const S = PK.S;
    const c = PK.cat(p.id);
    if (!c)
      return `<div class="empty"><b>Không tìm thấy danh mục</b><a href="#/">Về trang chủ</a></div>`;
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
    const activeFilters =
      sel.brand.length + (sel.price ? 1 : 0) + (sel.ram ? 1 : 0) + (sel.stock ? 1 : 0);
    return `
      ${crumbs([['Trang chủ', '#/'], ...(parent ? [[parent.name, '#/c/' + parent.id]] : []), [c.name]])}
      <div class="page-title"><h1>${c.name}</h1></div>
      ${children.length ? `<div class="row tight" style="margin-bottom:24px">${children.map((x) => `<a class="chip" href="#/c/${x.id}">${x.name}</a>`).join('')}</div>` : ''}
      <div class="layout-filter">
        <aside class="filters" aria-label="Bộ lọc">
          <div class="filter-group"><h4>Thương hiệu</h4>${brands.map((b) => `<label><input type="checkbox" ${sel.brand.includes(b) ? 'checked' : ''} data-act="nav" data-href="${esc(link({ brand: toggleBrand(b) }))}"> ${b} <span class="muted">${base.filter((x) => x.brand === b).length}</span></label>`).join('')}</div>
          <div class="filter-group"><h4>Mức giá</h4>${PRICE_RANGES.map(([v, l]) => `<label><input type="radio" name="pr" ${sel.price === v ? 'checked' : ''} data-act="nav" data-href="${esc(link({ price: v }))}"> ${l}</label>`).join('')}</div>
          ${rams.length ? `<div class="filter-group"><h4>RAM</h4>${rams.map((r) => `<label><input type="checkbox" ${sel.ram === String(r) ? 'checked' : ''} data-act="nav" data-href="${esc(link({ ram: sel.ram === String(r) ? '' : r }))}"> ${r}GB <span class="muted">${base.filter((x) => x.specs.ram_gb === r).length}</span></label>`).join('')}</div>` : ''}
          <div class="filter-group"><label class="switch" style="justify-content:space-between"><span>Chỉ hiện còn hàng</span><input type="checkbox" ${sel.stock ? 'checked' : ''} data-act="nav" data-href="${esc(link({ stock: sel.stock ? '' : '1' }))}"></label></div>
          ${activeFilters ? `<a class="link small" style="margin-top:12px" href="#/c/${c.id}">Xóa ${activeFilters} bộ lọc</a>` : ''}
        </aside>
        <div>
          <div class="toolbar"><span class="muted">${list.length} sản phẩm</span>
            <label class="row tight small"><span class="muted">Sắp xếp</span><select data-change="sort" data-base="${esc(link({ sort: '' }))}">
              ${[
                ['newest', 'Mới nhất'],
                ['price_asc', 'Giá thấp đến cao'],
                ['price_desc', 'Giá cao đến thấp'],
              ]
                .map(
                  ([v, l]) =>
                    `<option value="${v}" ${sel.sort === v ? 'selected' : ''}>${l}</option>`,
                )
                .join('')}
            </select></label></div>
          ${list.length ? `<div class="product-grid">${list.map((x) => productCard(x)).join('')}</div>` : `<div class="empty"><b>Không có sản phẩm khớp bộ lọc</b><a class="link" href="#/c/${c.id}">Xóa bộ lọc</a></div>`}
          ${note('Bộ lọc nằm trên URL (chia sẻ được), có số đếm theo từng giá trị. Sản phẩm "Ngừng kinh doanh" vẫn xem được nhưng không mua được; sản phẩm ẩn và nháp không hiện.')}
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
      return `<div class="empty"><b>Sản phẩm không tồn tại</b><p>Sản phẩm đã bị ẩn hoặc đường dẫn không đúng.</p><a class="btn secondary" href="#/">Về trang chủ</a></div>`;
    if (PK.personalized() && S.recent[0] !== p.id)
      S.recent = [p.id, ...S.recent.filter((x) => x !== p.id)].slice(0, 20);
    const c = PK.cat(p.cat);
    const root = PK.cat(c.parent || c.id);
    const v = p.variants ? p.variants.find((x) => x.sku === q.v) || p.variants[0] : null;
    const color = v ? v.opts['Màu'] : undefined;
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
          return `<div class="opt-label">${axis}: <span>${v.opts[axis]}</span></div><div class="opt-row" role="radiogroup" aria-label="${axis}">${values
            .map((val) => {
              const target =
                p.variants.find(
                  (x) =>
                    x.opts[axis] === val &&
                    axes.every((a) => a === axis || x.opts[a] === v.opts[a]),
                ) || p.variants.find((x) => x.opts[axis] === val);
              const sw =
                axis === 'Màu'
                  ? `<span class="swatch" style="background:${val === 'Trắng' ? '#eceef1' : '#2b2d33'}"></span>`
                  : '';
              return `<a class="opt ${v.opts[axis] === val ? 'on' : ''} ${target.stock ? '' : 'off'}" role="radio" aria-checked="${v.opts[axis] === val}" href="#/p/${p.slug}?v=${target.sku}">${sw}${val}</a>`;
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
    groups['Bảo hành'] = [['Thời gian', p.warranty + ' tháng chính hãng']];
    const specRows = Object.entries(groups)
      .map(
        ([g, rows]) =>
          `<tr class="grp"><td colspan="2">${g}</td></tr>` +
          rows.map(([l, val]) => `<tr><td>${l}</td><td>${esc(val)}</td></tr>`).join(''),
      )
      .join('');
    // personalized-content: điểm nổi bật theo persona
    const hl = D.highlights[p.id];
    const ps = PK.personalized() && hl && hl[persona()] ? persona() : 'general';
    const hlBlock = hl
      ? `<div class="ai-block" style="margin-top:24px"><div class="row between" style="margin-bottom:12px"><h3 style="margin:0">Điểm nổi bật cho nhu cầu của bạn</h3><span class="ai-label">${icon('sparkle', 14)} Gợi ý bởi AI</span></div>
          <ul style="margin:0;padding-left:18px" class="stack">${hl[ps].map((h) => `<li>${esc(h.text)}</li>`).join('')}</ul></div>
          ${note(`Phiên bản <b>${ps}</b>, mỗi ý dựa trên thông số: ${hl[ps].map((h) => h.basedOn.join('+')).join(', ')}. Đồng ý cá nhân hóa (hoặc vai Khách hàng) thì thấy bản gaming.`)}`
      : '';
    const sum = D.reviewSummaries[p.id];
    const reviews = S.reviews.filter((r) => r.pid === p.id && r.status === 'visible');
    const sumBlock =
      sum && reviews.length >= 3
        ? `<div class="ai-block"><div class="row between" style="margin-bottom:12px"><h3 style="margin:0">Khách hàng nói gì</h3><button class="ai-label link" data-act="aiExplain">${icon('sparkle', 14)} Tóm tắt bởi AI ${icon('info', 14)}</button></div>
          <p style="color:var(--ink-2)">${esc(sum.overview)}</p>
          <div class="grid cols-2" style="margin:16px 0"><div><b class="small">Khen</b><ul class="small" style="padding-left:18px;margin:8px 0 0">${sum.pros.map((x) => `<li>${esc(x.text)} <span class="muted">· ${x.count} lượt</span></li>`).join('')}</ul></div>
          <div><b class="small">Chưa hài lòng</b><ul class="small" style="padding-left:18px;margin:8px 0 0">${sum.cons.map((x) => `<li>${esc(x.text)} <span class="muted">· ${x.count} lượt</span></li>`).join('')}</ul></div></div>
          <div class="row tight">${sum.aspects.map((a) => `<span class="aspect ${PK.personalized() && ['hiệu năng', 'tản nhiệt'].includes(a) ? 'hi' : ''}">${a}</span>`).join('')}</div></div>`
        : '';
    // commerce: review
    const avg = p.rating; // điểm tổng hợp từ toàn bộ đánh giá, dùng chung với thẻ sản phẩm
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
    const stockHtml = disc
      ? '<span class="stock out">Ngừng kinh doanh</span>'
      : stock > 5
        ? '<span class="stock">Còn hàng, giao ngay</span>'
        : stock > 0
          ? `<span class="stock low">Chỉ còn ${stock} sản phẩm</span>`
          : '<span class="stock out">Tạm hết hàng</span>';
    return `
      ${crumbs([['Trang chủ', '#/'], [root.name, '#/c/' + root.id], ...(c.parent ? [[c.name, '#/c/' + c.id]] : [])])}
      <div class="pdp">
        <div class="gallery"><div class="main">${productImg(p, 420, color)}</div>
          <div class="thumbs">${[0, 1, 2, 3].map((i) => `<button class="${i === 0 ? 'on' : ''}" aria-label="Ảnh ${i + 1}">${productImg(p, 56, color)}</button>`).join('')}</div></div>
        <div class="buybox">
          <div class="muted small" style="margin-bottom:6px">${p.brand}</div>
          <h1>${esc(p.name)}</h1>
          <div class="row tight small" style="margin-bottom:20px">${stars(avg)} <b>${avg}</b> <a class="muted" href="#/p/${p.slug}?a=reviews"><u>${p.reviewCount} đánh giá</u></a></div>
          <div class="row tight" style="align-items:baseline"><span class="price-big">${money(price)}</span>${compareAt ? `<span class="strike">${money(compareAt)}</span><span class="badge sale">Giảm ${Math.round((1 - price / compareAt) * 100)}%</span>` : ''}</div>
          ${picker}
          <div style="margin:20px 0 16px">${stockHtml}</div>
          ${
            disc
              ? `<div class="alert">${icon('info', 18)}<span>Sản phẩm đã ngừng kinh doanh. Xem các mẫu tương tự bên dưới.</span></div>`
              : `<div class="grid cols-2" style="gap:8px">
            <button class="btn block" ${stock > 0 ? '' : 'disabled'} data-act="addToCart" data-pid="${p.id}" data-sku="${v ? v.sku : p.slug.toUpperCase()}" data-source="category">${icon('bag', 18)} Thêm vào giỏ</button>
            <button class="btn secondary block" data-act="addCompare" data-pid="${p.id}">${icon('compare', 18)} So sánh</button></div>`
          }
          <button class="link small" style="margin-top:16px" data-act="askAbout" data-pid="${p.id}">${icon('chat', 16)} Hỏi trợ lý về sản phẩm này</button>
          <div class="benefits">
            <div>${icon('truck', 18)}<span><b>Giao hàng miễn phí</b> cho đơn từ 500.000 đ</span></div>
            <div>${icon('shield', 18)}<span><b>Bảo hành ${p.warranty} tháng</b> chính hãng</span></div>
            <div>${icon('refresh', 18)}<span><b>Đổi trả 7 ngày</b> nếu lỗi do nhà sản xuất</span></div>
          </div>
          ${hlBlock}
        </div>
      </div>
      <section class="section grid cols-2" style="gap:48px;align-items:start">
        <div><h2>Thông số kỹ thuật</h2><div class="table-wrap"><table class="tbl spec-table">${specRows}</table></div></div>
        <div><h2>Đánh giá tổng quan</h2>${sumBlock || `<div class="empty"><b>Chưa đủ đánh giá</b>Tóm tắt xuất hiện khi có từ 3 đánh giá.</div>`}</div>
      </section>
      ${similar.length ? rail(similar, 'Sản phẩm tương tự', 'Cùng danh mục, mức giá gần', null, 'recommendations.similar') : ''}
      ${together.length ? rail(together, 'Thường được mua kèm', '', null, 'recommendations.bought-together') : ''}
      <section class="section" id="reviews"><h2>Đánh giá từ khách đã mua</h2>
        <div class="grid" style="grid-template-columns:280px 1fr;gap:48px;align-items:start">
          <div><div class="row tight" style="align-items:baseline"><span class="price-big">${avg}</span><span class="muted">/ 5</span></div>${stars(avg, 16)}<p class="muted small" style="margin-top:6px">${reviews.length} đánh giá đã kiểm duyệt</p>
            ${dist.map(([n, cnt]) => `<div class="row tight small" style="flex-wrap:nowrap;margin-top:6px"><span style="width:12px">${n}</span>${icon('star', 12, 'fill')}<div class="bar"><i style="width:${reviews.length ? (cnt / reviews.length) * 100 : 0}%"></i></div><span class="muted" style="width:16px;text-align:right">${cnt}</span></div>`).join('')}</div>
          <div>
            ${canReview ? `<form class="card tight" data-form="review" data-pid="${p.id}" style="margin-bottom:8px"><b>Viết đánh giá của bạn</b><div class="row" style="margin:12px 0">${[1, 2, 3, 4, 5].map((n) => `<label class="check small"><input type="radio" name="rating" value="${n}" ${n === 5 ? 'checked' : ''}> ${n} sao</label>`).join('')}</div><textarea name="body" maxlength="2000" placeholder="Chia sẻ trải nghiệm của bạn (tối đa 2.000 ký tự)"></textarea><div class="row" style="justify-content:flex-end;margin-top:12px"><button class="btn sm">Gửi đánh giá</button></div></form>` : ''}
            ${reviews.map((r) => `<div class="review"><div class="row between"><div class="row tight"><span class="avatar light" style="width:30px;height:30px;font-size:12px">${PK.initials(r.user)}</span><b>${esc(r.user)}</b><span class="badge ok">Đã mua hàng</span></div><span class="small muted">${dmy(r.date)}</span></div><div style="margin:8px 0 4px">${stars(r.rating)}</div><p style="margin:0 0 6px">${esc(r.body)}</p><button class="link small" style="font-weight:500;color:var(--muted)" data-act="reportReview" data-id="${r.id}">Báo cáo</button></div>`).join('') || '<div class="empty"><b>Chưa có đánh giá</b></div>'}
          </div>
        </div>
        ${note('Chỉ khách đã nhận hàng mới viết được đánh giá. Vào vai Khách hàng (đã mua ROG G16) để thử.')}
      </section>`;
  });
  act('aiExplain', () => {
    modal(
      `<h2>Về tóm tắt bởi AI</h2><p>Nội dung được AI tổng hợp từ đánh giá thật của khách đã mua, kèm số lượt nhắc tới mỗi ý. Pickory kiểm tra để AI không đưa con số sai lệch hoặc dùng từ quảng cáo như "tốt nhất".</p><div class="row" style="justify-content:flex-end"><button class="btn" data-act="closeModal">Đã hiểu</button></div>`,
    );
  });
  act('reportReview', (d) => {
    const r = PK.S.reviews.find((x) => x.id === d.id);
    r.reports += 1;
    toast('Cảm ơn bạn, chúng tôi sẽ xem xét đánh giá này');
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
    toast('Đã thêm vào giỏ hàng');
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
      return `<h1>Giỏ hàng</h1><div class="empty">${icon('bag', 32)}<b style="margin-top:12px">Giỏ hàng đang trống</b><p>Khám phá laptop, điện thoại và phụ kiện chính hãng.</p><a class="btn" href="#/">Tiếp tục mua sắm</a></div>`;
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
    const left = S.commerceSettings.freeShip - sub;
    return `<h1>Giỏ hàng <span class="muted" style="font-weight:400">(${cartCount()})</span></h1>
      <div class="grid" style="grid-template-columns:1fr 380px;gap:48px;align-items:start">
        <div>${lines
          .map(
            (
              x,
            ) => `<div class="cart-line"><a class="pic" href="#/p/${x.p.slug}">${productImg(x.p, 80, x.v?.opts['Màu'])}</a>
            <div><a href="#/p/${x.p.slug}"><b>${esc(x.p.name)}</b></a><div class="small muted" style="margin:2px 0 10px">${x.v ? Object.values(x.v.opts).join(' · ') : esc(PK.specLine(x.p))}</div>
              ${x.problem ? `<span class="badge sale" style="margin-bottom:10px">${x.problem}</span><br>` : ''}
              <div class="qty"><button data-act="qty" data-sku="${x.l.sku}" data-delta="-1" aria-label="Giảm số lượng">${icon(x.l.qty === 1 ? 'x' : 'minus', 16)}</button><b>${x.l.qty}</b><button data-act="qty" data-sku="${x.l.sku}" data-delta="1" aria-label="Tăng số lượng">${icon('plus', 16)}</button></div></div>
            <b class="num">${money(x.price * x.l.qty)}</b></div>`,
          )
          .join('')}</div>
        <div class="card stack" style="position:sticky;top:120px"><h3>Tóm tắt đơn hàng</h3>
          <div class="summary-line"><span>Tạm tính</span><b class="num">${money(sub)}</b></div>
          <div class="summary-line"><span>Phí vận chuyển</span><span class="muted">Tính ở bước sau</span></div>
          ${left > 0 ? `<div class="small muted">Mua thêm <b>${money(left)}</b> để được miễn phí vận chuyển</div><div class="bar"><i style="width:${(sub / S.commerceSettings.freeShip) * 100}%"></i></div>` : `<div class="row tight small" style="color:var(--success)">${icon('check', 14)} Đơn được miễn phí vận chuyển</div>`}
          ${blocked ? `<div class="alert err">${icon('alert', 18)}<span>Có sản phẩm không còn bán được. Hãy bỏ khỏi giỏ để tiếp tục.</span></div>` : ''}
          <a class="btn block ${blocked ? 'disabled' : ''}" href="${blocked ? '#/cart' : '#/checkout'}">Tiến hành thanh toán</a>
          <div class="row tight small muted" style="justify-content:center">${icon('lock', 14)} Thanh toán an toàn qua VietQR hoặc COD</div></div>
      </div>
      ${rail(cross, 'Có thể bạn cần thêm', '', null, 'recommendations.cart')}`;
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
  const checkoutSteps = (n) =>
    `<div class="steps">${['Giỏ hàng', 'Thông tin giao hàng', 'Thanh toán', 'Hoàn tất'].map((s, i) => `<span class="${i === n ? 'on' : ''}"><i>${i < n ? icon('check', 12) : i + 1}</i>${s}</span>`).join('')}</div>`;
  route('/checkout', () => {
    const S = PK.S;
    const lines = cartLines();
    if (!lines.length)
      return `<div class="empty"><b>Giỏ hàng đang trống</b><a class="btn" href="#/">Tiếp tục mua sắm</a></div>`;
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
    return `${checkoutSteps(1)}<h1>Thanh toán</h1>
      <form data-form="placeOrder" class="grid" style="grid-template-columns:1fr 400px;gap:48px;align-items:start;margin-top:24px">
        <div>
          <section><h3>Thông tin liên hệ</h3>
            <div class="grid cols-2"><label class="field"><span>Họ và tên</span><input name="name" value="${esc(dr.name)}" data-input="draft" autocomplete="name"></label>
            <label class="field"><span>Số điện thoại</span><input name="phone" value="${esc(dr.phone)}" data-input="draft" autocomplete="tel" inputmode="tel"></label></div>
            <label class="field" style="margin-top:16px"><span>Email nhận xác nhận đơn</span><input name="email" type="email" value="${esc(dr.email)}" data-input="draft" autocomplete="email"></label>
            ${S.role === 'guest' ? `<p class="small muted" style="margin-top:8px">Đã có tài khoản? <a href="#/login"><u>Đăng nhập</u></a> để dùng sổ địa chỉ.</p>` : ''}
          </section>
          <section style="margin-top:32px"><h3>Địa chỉ giao hàng</h3>
            ${S.role === 'customer' ? `<label class="field" style="margin-bottom:16px"><span>Chọn từ sổ địa chỉ</span><select data-change="pickAddress"><option>Nhà riêng · 12 Nguyễn Huệ, Phường Bến Nghé, TP. Hồ Chí Minh</option><option>Công ty · 8 Lý Thường Kiệt, Phường Hoàn Kiếm, Hà Nội</option></select></label>` : ''}
            <div class="grid cols-2">
              <label class="field"><span>Tỉnh/thành phố</span><select name="province" data-change="province"><option value="">Chọn tỉnh/thành</option>${D.provinces.map((x) => `<option value="${x.code}" ${dr.province === x.code ? 'selected' : ''}>${x.name}</option>`).join('')}</select></label>
              <label class="field"><span>Phường/xã</span><select name="ward" data-change="ward" ${prov ? '' : 'disabled'}><option value="">Chọn phường/xã</option>${prov ? prov.wards.map((w) => `<option ${dr.ward === w ? 'selected' : ''}>${w}</option>`).join('') : ''}</select></label>
            </div>
            <label class="field" style="margin-top:16px"><span>Số nhà, tên đường</span><input name="street" value="${esc(dr.street)}" data-input="draft" autocomplete="street-address"></label>
            <label class="field" style="margin-top:16px"><span>Ghi chú cho người giao (không bắt buộc)</span><input name="note" value="${esc(dr.note)}" data-input="draft" placeholder="Ví dụ: giao giờ hành chính"></label>
          </section>
          <section style="margin-top:32px"><h3>Phương thức thanh toán</h3><div class="stack">
            <label class="radio-card"><input type="radio" name="method" value="sepay" ${dr.method === 'sepay' ? 'checked' : ''} data-change="method"><span><b>Chuyển khoản qua VietQR</b><br><span class="small muted">Quét mã bằng app ngân hàng, xác nhận tự động trong vài giây</span></span><span class="spacer"></span>${icon('card', 20)}</label>
            <label class="radio-card ${codOk ? '' : 'off'}"><input type="radio" name="method" value="cod" ${dr.method === 'cod' ? 'checked' : ''} ${codOk ? '' : 'disabled'} data-change="method"><span><b>Thanh toán khi nhận hàng (COD)</b><br><span class="small muted">${codOk ? 'Được xem hàng trước khi thanh toán' : `Không áp dụng cho đơn trên ${money(S.commerceSettings.codMax)}`}</span></span><span class="spacer"></span>${icon('package', 20)}</label>
          </div></section>
          <section style="margin-top:32px"><label class="switch"><input type="checkbox" name="vat" ${dr.vat ? 'checked' : ''} data-change="vat"><span><b>Xuất hóa đơn VAT</b></span></label>
            ${dr.vat ? `<div class="grid cols-2" style="margin-top:16px"><label class="field"><span>Tên công ty</span><input name="company"></label><label class="field"><span>Mã số thuế</span><input name="taxCode" placeholder="10 hoặc 13 số" inputmode="numeric"></label><label class="field"><span>Địa chỉ công ty</span><input name="companyAddress"></label><label class="field"><span>Email nhận hóa đơn</span><input name="invoiceEmail" type="email"></label></div>` : ''}
          </section>
          ${note('Lỗi được thiết kế: chưa tính được phí ship, tổng tiền đổi (409 "Giá đã thay đổi, xem lại"), hết hàng lúc đặt (409 kèm SKU). Đơn trên 20 triệu tự tắt COD.')}
        </div>
        <aside class="card stack" style="position:sticky;top:120px">
          <h3>Đơn hàng</h3>
          ${lines.map((x) => `<div class="row" style="flex-wrap:nowrap"><span style="width:56px;height:56px;background:var(--bg-2);border-radius:8px;display:flex;align-items:center;justify-content:center;flex:none;position:relative">${productImg(x.p, 46, x.v?.opts['Màu'])}<b class="badge dark" style="position:absolute;top:-6px;right:-6px;padding:3px 6px">${x.l.qty}</b></span><span class="small" style="flex:1">${esc(x.p.name)}${x.v ? `<br><span class="muted">${Object.values(x.v.opts).join(' · ')}</span>` : ''}</span><b class="small num">${money(x.price * x.l.qty)}</b></div>`).join('')}
          <hr class="sep" style="margin:4px 0">
          <div class="summary-line"><span>Tạm tính</span><span class="num">${money(sub)}</span></div>
          <div class="summary-line"><span>Vận chuyển (GHN)</span><span class="num">${fee == null ? '<span class="muted">Chọn địa chỉ</span>' : money(fee)}</span></div>
          ${discount ? `<div class="summary-line" style="color:var(--success)"><span>Miễn phí vận chuyển</span><span class="num">−${money(discount)}</span></div>` : ''}
          <div class="summary-line total"><span>Tổng cộng</span><span class="num">${money(total)}</span></div>
          ${eta ? `<div class="row tight small muted">${icon('truck', 16)} Dự kiến giao ${eta}</div>` : ''}
          <button class="btn block">Đặt hàng</button>
          <p class="xs muted" style="margin:0;text-align:center">Bằng việc đặt hàng, bạn đồng ý với <a href="#/help/chinh-sach-doi-tra"><u>chính sách đổi trả</u></a>.</p>
        </aside>
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
    if (missing.length) return toast('Vui lòng nhập ' + missing.join(', '));
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
    // Giữ hàng cho đơn (order_reserve)
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
    const finder = (x, y) => {
      for (const [ox, oy] of [
        [0, 0],
        [18, 0],
        [0, 18],
      ]) {
        if (x >= ox && x < ox + 7 && y >= oy && y < oy + 7) {
          const dx = x - ox;
          const dy = y - oy;
          return dx === 0 ||
            dy === 0 ||
            dx === 6 ||
            dy === 6 ||
            (dx >= 2 && dx <= 4 && dy >= 2 && dy <= 4)
            ? 1
            : 0;
        }
      }
      return -1;
    };
    for (let y = 0; y < 25; y++)
      for (let x = 0; x < 25; x++) {
        h = (h * 31 + seedStr.charCodeAt((x * 3 + y) % seedStr.length) + x * y) % 9973;
        const f = finder(x, y);
        if (f === 1 || (f === -1 && h % 2 === 0))
          cells += `<rect x="${x * 8}" y="${y * 8}" width="8" height="8"/>`;
      }
    return `<svg class="qr" viewBox="0 0 200 200" role="img" aria-label="Mã VietQR thanh toán">${cells}</svg>`;
  }
  route('/pay/:code', (p) => {
    const S = PK.S;
    const o = PK.order(p.code);
    if (!o) return '<div class="empty"><b>Không tìm thấy đơn</b></div>';
    if (o.payment === 'paid')
      return `<div class="empty"><b>Đã nhận thanh toán</b><p>Đơn ${o.code} đã được thanh toán.</p><a class="btn" href="#/order-success/${o.code}">Xem đơn hàng</a></div>`;
    if (o.status === 'cancelled')
      return `<div class="empty"><b>Đơn đã hủy</b><p>Đơn ${o.code} quá hạn thanh toán 30 phút nên đã được hủy.</p><a class="btn" href="#/">Về trang chủ</a></div>`;
    const total = PK.orderTotal(o);
    return `<div style="max-width:760px;margin:0 auto">${checkoutSteps(2)}
      <h1>Quét mã để thanh toán</h1><p class="muted">Mở ứng dụng ngân hàng và quét mã VietQR. Đơn được giữ trong <b id="countdown" style="color:var(--ink)">30:00</b>.</p>
      ${o.flags.includes('underpaid') ? `<div class="alert warn" style="margin:16px 0">${icon('alert', 18)}<span>Đã nhận một phần tiền nhưng chưa đủ. Vui lòng chuyển thêm phần còn thiếu hoặc liên hệ CSKH.</span></div>` : ''}
      <div class="card" style="display:grid;grid-template-columns:auto 1fr;gap:32px;align-items:center;margin-top:24px">
        ${qrSvg(o.code)}
        <div><table class="tbl plain"><tr><td class="muted">Ngân hàng</td><td><b>${S.commerceSettings.bank}</b></td></tr>
          <tr><td class="muted">Số tài khoản</td><td><b>${S.commerceSettings.account}</b></td></tr>
          <tr><td class="muted">Số tiền</td><td><b class="num" style="font-size:18px">${money(total)}</b></td></tr>
          <tr><td class="muted">Nội dung</td><td><code style="font-size:16px;font-weight:700">${o.code}</code></td></tr></table>
          <p class="small muted" style="margin:12px 0 0">Trang tự cập nhật khi nhận được tiền. Vui lòng ghi đúng nội dung chuyển khoản.</p></div>
      </div>
      <div class="proto-note" style="display:block"><b>Giả lập webhook SePay:</b>
        <div class="row tight" style="margin-top:8px">
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
      toast('Đã nhận thiếu tiền, đơn vẫn chờ thanh toán');
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
    toast('Đã nhận thanh toán');
    PK.go('/order-success/' + o.code);
  });

  route('/order-success/:code', (p) => {
    const S = PK.S;
    const o = PK.order(p.code);
    if (!o) return '<div class="empty"><b>Không tìm thấy đơn</b></div>';
    const link = `#/orders/${o.code}?t=${o.token || 'demo'}`;
    return `<div style="max-width:640px;margin:0 auto">${checkoutSteps(3)}
      <div style="width:56px;height:56px;border-radius:50%;background:var(--success-bg);color:var(--success);display:flex;align-items:center;justify-content:center;margin-bottom:16px">${icon('check', 28)}</div>
      <h1>Cảm ơn ${esc(o.customer.split(' ').pop())}, đơn hàng đã được đặt</h1>
      <p class="muted">Mã đơn <b style="color:var(--ink)">${o.code}</b>. Xác nhận đã được gửi tới ${esc(o.email)}.</p>
      <div class="card" style="margin:24px 0">
        <div class="summary-line"><span class="muted">Trạng thái</span><span>${PK.orderBadge(o.status)} ${PK.payBadge(o.payment)}</span></div>
        <div class="summary-line" style="margin-top:12px"><span class="muted">Thanh toán</span><span>${o.method === 'cod' ? 'Khi nhận hàng (COD)' : 'Chuyển khoản VietQR'}</span></div>
        <div class="summary-line" style="margin-top:12px"><span class="muted">Giao tới</span><span style="text-align:right">${esc(o.address)}</span></div>
        <div class="summary-line total" style="margin-top:12px"><span>Tổng cộng</span><span class="num">${money(PK.orderTotal(o))}</span></div>
      </div>
      <div class="row"><a class="btn" href="${S.role === 'customer' ? '#/orders/' + o.code : link}">Theo dõi đơn hàng</a><a class="btn secondary" href="#/">Tiếp tục mua sắm</a></div>
      ${S.role === 'guest' ? `<div class="card muted-bg" style="margin-top:32px"><b>Lưu đơn vào tài khoản?</b><p class="small muted" style="margin:6px 0 12px">Tạo tài khoản để theo dõi đơn và nhận gợi ý phù hợp. Đơn và giỏ hàng hiện tại sẽ được gộp vào tài khoản.</p><a class="btn sm secondary" href="#/register">Tạo tài khoản</a></div>` : ''}
      ${note(`Email xác nhận cho khách vãng lai chứa link tra cứu không cần đăng nhập: <a href="${link}">/orders/${o.code}?t=…</a>`)}
    </div>`;
  });

  // ================= commerce + shipping: đơn của tôi =================
  route('/orders', () => {
    const S = PK.S;
    if (S.role !== 'customer')
      return `<div class="auth"><h1>Đơn hàng của tôi</h1><p class="lead">Đăng nhập để xem đơn hàng. Nếu mua không cần tài khoản, hãy dùng link trong email xác nhận đơn.</p><a class="btn block" href="#/login">Đăng nhập</a></div>`;
    const mine = S.orders.filter((o) => o.userId === 'u1');
    return `<h1>Đơn hàng của tôi</h1><div class="table-wrap" style="margin-top:24px"><table class="tbl"><tr><th>Mã đơn</th><th>Ngày đặt</th><th>Sản phẩm</th><th class="num">Tổng tiền</th><th>Trạng thái</th></tr>
      ${mine.map((o) => `<tr class="click" data-act="nav" data-href="#/orders/${o.code}"><td><b>${o.code}</b></td><td class="muted">${dmy(o.createdAt)}</td><td>${esc(o.items[0].name)}${o.items.length > 1 ? ` <span class="muted">+${o.items.length - 1}</span>` : ''}</td><td class="num">${money(PK.orderTotal(o))}</td><td>${PK.orderBadge(o.status)}</td></tr>`).join('')}</table></div>`;
  });
  route('/orders/:code', (p, q) => {
    const S = PK.S;
    const o = PK.order(p.code);
    if (!o) return '<div class="empty"><b>Không tìm thấy đơn</b></div>';
    const tokenView = !!q.t && S.role !== 'customer';
    if (!tokenView && (S.role !== 'customer' || o.userId !== 'u1'))
      return `<div class="empty"><b>Không tìm thấy đơn</b>Bạn chỉ xem được đơn của chính mình.</div>`;
    const canCancel = !tokenView && ['pending_payment', 'pending_confirmation'].includes(o.status);
    const steps = ['pending_confirmation', 'confirmed', 'shipping', 'delivered'];
    const curIdx = steps.indexOf(o.status);
    return `${crumbs([['Đơn hàng', S.role === 'customer' ? '#/orders' : ''], [o.code]])}
      <div class="page-title"><div><h1>Đơn ${o.code}</h1><p class="muted" style="margin:6px 0 0">Đặt lúc ${o.createdAt.split(' ')[1]} ngày ${dmy(o.createdAt)}</p></div><div class="row tight">${PK.orderBadge(o.status)} ${PK.payBadge(o.payment)}</div></div>
      ${tokenView ? `<div class="alert" style="margin-bottom:16px">${icon('info', 18)}<span>Bạn đang xem đơn qua link trong email.</span></div>` : ''}
      ${o.status !== 'cancelled' ? `<div class="progress">${steps.map((s, i) => `<div class="${i <= curIdx ? 'on' : ''}">${PK.ORDER_STATUS[s]}</div>`).join('')}</div>` : `<div class="alert err" style="margin-bottom:24px">${icon('alert', 18)}<span>Đơn đã hủy (${esc(o.cancelReason || '')}). ${o.payment === 'refund_pending' ? 'Pickory đang hoàn tiền cho bạn trong 1 đến 3 ngày làm việc.' : ''}</span></div>`}
      <div class="grid" style="grid-template-columns:1.3fr 1fr;gap:32px;align-items:start">
        <div class="card"><h3>Sản phẩm</h3>${o.items.map((i) => `<div class="row" style="flex-wrap:nowrap;padding:8px 0"><span style="width:56px;height:56px;background:var(--bg-2);border-radius:8px;display:flex;align-items:center;justify-content:center;flex:none">${productImg(PK.prod(i.pid), 46)}</span><span style="flex:1">${esc(i.name)}<br><span class="small muted">Số lượng ${i.qty}</span></span><b class="num">${money(i.price * i.qty)}</b></div>`).join('')}
          <hr class="sep"><div class="summary-line"><span class="muted">Phí vận chuyển</span><span class="num">${o.shippingFee - o.shippingDiscount ? money(o.shippingFee - o.shippingDiscount) : 'Miễn phí'}</span></div>
          <div class="summary-line total" style="margin-top:12px"><span>Tổng cộng</span><span class="num">${money(PK.orderTotal(o))}</span></div>
          ${o.vat ? `<p class="small muted" style="margin-top:12px">Hóa đơn VAT: ${esc(o.vat.company)}, MST ${esc(o.vat.taxCode)}</p>` : ''}
          <div class="row" style="margin-top:16px">${o.status === 'pending_payment' ? `<a class="btn" href="#/pay/${o.code}">Thanh toán ngay</a>` : ''}${canCancel ? `<button class="btn danger" data-act="customerCancel" data-code="${o.code}">Hủy đơn</button>` : ''}</div></div>
        <div class="card"><h3>Giao hàng</h3><p class="small">${esc(o.customer)} · ${tokenView ? '•••• ••• ' + o.phone.slice(-3) : esc(o.phone)}<br>${esc(o.address)}</p>
          ${
            o.tracking
              ? `<p class="small" style="margin-bottom:16px">GHN · <b>${o.tracking}</b></p><ul class="timeline">${(
                  o.shipEvents || []
                )
                  .slice()
                  .reverse()
                  .map(
                    (e) =>
                      `<li class="done"><b>${PK.SHIP_STATUS[e[1]]}</b><div class="small muted">${e[0].split(' ')[1]} · ${dmy(e[0])}</div></li>`,
                  )
                  .join('')}</ul>`
              : '<p class="small muted">Đơn chưa được bàn giao cho đơn vị vận chuyển.</p>'
          }</div>
      </div>
      ${note('Khách chỉ hủy được khi đơn "Chờ thanh toán" hoặc "Chờ xác nhận". Hủy đơn đã thanh toán chuyển sang "Chờ hoàn tiền".')}`;
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
  const googleBtn = (label) =>
    `<button type="button" class="btn secondary block" data-act="googleLogin"><svg width="18" height="18" viewBox="0 0 24 24" aria-hidden="true"><path fill="#4285F4" d="M22.5 12.3c0-.8-.1-1.5-.2-2.2H12v4.2h5.9a5 5 0 0 1-2.2 3.3v2.7h3.6c2-1.9 3.2-4.7 3.2-8Z"/><path fill="#34A853" d="M12 23c3 0 5.5-1 7.3-2.7l-3.6-2.7c-1 .7-2.2 1-3.7 1-2.8 0-5.2-1.9-6.1-4.5H2.2v2.8A11 11 0 0 0 12 23Z"/><path fill="#FBBC05" d="M5.9 14.1a6.6 6.6 0 0 1 0-4.2V7.1H2.2a11 11 0 0 0 0 9.8l3.7-2.8Z"/><path fill="#EA4335" d="M12 5.4c1.6 0 3 .6 4.1 1.6l3.1-3.1A11 11 0 0 0 2.2 7.1l3.7 2.8C6.8 7.3 9.2 5.4 12 5.4Z"/></svg> ${label}</button>`;
  route(
    '/login',
    () => `<div class="auth"><h1>Đăng nhập</h1><p class="lead">Theo dõi đơn hàng và nhận gợi ý phù hợp với bạn.</p>
      ${googleBtn('Tiếp tục với Google')}<div class="or">hoặc</div>
      <form data-form="login" class="stack"><label class="field"><span>Email</span><input name="email" type="email" value="minhanh@example.vn" autocomplete="email"></label>
      <label class="field"><span class="row between">Mật khẩu <a href="#/forgot" class="small" style="font-weight:500"><u>Quên mật khẩu?</u></a></span><input name="password" type="password" autocomplete="current-password"></label>
      <button class="btn block">Đăng nhập</button></form>
      <p class="small muted" style="text-align:center;margin-top:20px">Chưa có tài khoản? <a href="#/register"><b>Tạo tài khoản</b></a></p>
      ${note('Mật khẩu đúng: 12345678. Sai 5 lần trong 15 phút thì lần 6 bị khóa 15 phút (429). Thông báo lỗi không tiết lộ email có tồn tại hay không.')}</div>`,
  );
  form('login', (data) => {
    if (failCount >= 5)
      return toast('Bạn đã nhập sai quá nhiều lần. Vui lòng thử lại sau 15 phút.');
    if (data.password !== '12345678') {
      failCount += 1;
      return toast('Email hoặc mật khẩu không đúng');
    }
    failCount = 0;
    loginCustomer();
  });
  function loginCustomer() {
    const S = PK.S;
    S.role = 'customer';
    S.draft = null;
    toast('Chào mừng Minh Anh quay lại');
    PK.go('/');
  }
  act('googleLogin', () => {
    modal(`<h2>Chọn tài khoản Google</h2><p class="muted">để tiếp tục tới Pickory</p>
      <button class="radio-card" style="width:100%;background:none;font:inherit;text-align:left" data-act="googlePick"><span class="avatar light">MA</span><span><b>Nguyễn Minh Anh</b><br><span class="small muted">minhanh@example.vn</span></span></button>
      ${note('Email Google đã xác thực và trùng tài khoản thì tự liên kết. Chưa xác thực thì không liên kết. Email thuộc nhân viên bị từ chối.')}`);
  });
  act('googlePick', () => {
    closeModal();
    loginCustomer();
  });
  route(
    '/register',
    () => `<div class="auth"><h1>Tạo tài khoản</h1><p class="lead">Mua nhanh hơn, theo dõi đơn và lưu sổ địa chỉ.</p>
      ${googleBtn('Đăng ký với Google')}<div class="or">hoặc</div>
      <form data-form="register" class="stack"><label class="field"><span>Họ và tên</span><input name="name" autocomplete="name"></label>
      <label class="field"><span>Email</span><input name="email" type="email" autocomplete="email"></label>
      <label class="field"><span>Mật khẩu</span><input name="password" type="password" autocomplete="new-password" aria-describedby="pw-hint"><span id="pw-hint" class="xs muted" style="display:block;margin-top:6px">Tối thiểu 8 ký tự</span></label>
      <button class="btn block">Tạo tài khoản</button></form>
      <p class="small muted" style="text-align:center;margin-top:20px">Đã có tài khoản? <a href="#/login"><b>Đăng nhập</b></a></p>
      ${note('Thử email đuôi @pickory.vn để thấy lỗi "email thuộc tài khoản nhân viên". Đăng ký xong được đăng nhập luôn và nhận email xác thực (hạn 24 giờ).')}</div>`,
  );
  form('register', (data) => {
    if (!/.+@.+\..+/.test(data.email)) return toast('Email không hợp lệ');
    if (String(data.password).length < 8) return toast('Mật khẩu cần tối thiểu 8 ký tự');
    if (/@pickory\.vn$/.test(data.email))
      return toast('Email này không dùng được để đăng ký tài khoản khách');
    PK.S.role = 'customer';
    modal(`<div style="width:48px;height:48px;border-radius:50%;background:var(--success-bg);color:var(--success);display:flex;align-items:center;justify-content:center;margin-bottom:16px">${icon('check', 24)}</div><h2>Tài khoản đã được tạo</h2><p>Chúng tôi đã gửi email xác thực tới <b>${esc(data.email)}</b>. Link có hiệu lực trong 24 giờ.</p>
      ${note('Xem trước email: <a href="#/verify" data-act="closeModal">pickory.vn/verify-email?t=…</a>')}
      <div class="row" style="justify-content:flex-end"><button class="btn" data-act="closeModal">Tiếp tục mua sắm</button></div>`);
    PK.render();
  });
  route(
    '/verify',
    () =>
      `<div class="auth" style="text-align:center"><div style="width:56px;height:56px;border-radius:50%;background:var(--success-bg);color:var(--success);display:inline-flex;align-items:center;justify-content:center;margin-bottom:16px">${icon('check', 28)}</div><h1>Email đã được xác thực</h1><p class="lead">Cảm ơn bạn. Tài khoản của bạn đã sẵn sàng.</p><a class="btn" href="#/">Về trang chủ</a></div>`,
  );
  route(
    '/forgot',
    () => `<div class="auth"><h1>Quên mật khẩu</h1><p class="lead">Nhập email của bạn, chúng tôi sẽ gửi link đặt lại mật khẩu.</p>
      <form data-form="forgot" class="stack"><label class="field"><span>Email</span><input name="email" type="email" autocomplete="email"></label><button class="btn block">Gửi link đặt lại</button></form>
      <p class="small" style="text-align:center;margin-top:20px"><a href="#/login">${icon('chevronLeft', 14)} Quay lại đăng nhập</a></p>
      ${note('Luôn trả cùng một thông báo và độ trễ dù email có tồn tại hay không.')}</div>`,
  );
  form('forgot', (data) => {
    modal(`<h2>Kiểm tra hộp thư</h2><p>Nếu <b>${esc(data.email)}</b> có tài khoản, chúng tôi đã gửi link đặt lại mật khẩu. Link có hiệu lực 30 phút và chỉ dùng được một lần.</p>
      ${note('Xem trước email: <a href="#/reset" data-act="closeModal">pickory.vn/reset-password?t=…</a>')}<div class="row" style="justify-content:flex-end"><button class="btn" data-act="closeModal">Đã hiểu</button></div>`);
  });
  route(
    '/reset',
    (
      _,
      q,
    ) => `<div class="auth"><h1>${q.invite ? 'Kích hoạt tài khoản nhân viên' : 'Đặt mật khẩu mới'}</h1><p class="lead">${q.invite ? 'Đặt mật khẩu để đăng nhập back-office Pickory.' : 'Mật khẩu mới sẽ đăng xuất mọi thiết bị đang đăng nhập.'}</p>
      <form data-form="reset" class="stack"><label class="field"><span>Mật khẩu mới</span><input name="password" type="password" autocomplete="new-password"><span class="xs muted" style="display:block;margin-top:6px">Tối thiểu 8 ký tự</span></label><button class="btn block">Lưu mật khẩu</button></form>
      ${note(q.invite ? 'Trang dùng chung cho lời mời nhân viên (link hạn 72 giờ, dùng 1 lần).' : 'Đặt lại xong, mọi phiên cũ bị xóa.')}</div>`,
  );
  form('reset', (data) => {
    if (String(data.password).length < 8) return toast('Mật khẩu cần tối thiểu 8 ký tự');
    toast('Đã lưu mật khẩu mới');
    const { query } = PK.parseHash();
    PK.go(query.invite ? '/admin/login' : '/login');
  });

  // ================= identity + activity-tracking: tài khoản khách =================
  route('/account', (_, q) => {
    const S = PK.S;
    if (S.role !== 'customer')
      return `<div class="auth"><h1>Tài khoản</h1><p class="lead">Đăng nhập để quản lý tài khoản của bạn.</p><a class="btn block" href="#/login">Đăng nhập</a></div>`;
    const u = PK.me();
    const tab = q.tab || 'info';
    const tabs = [
      ['info', 'Thông tin cá nhân', 'user'],
      ['addresses', 'Sổ địa chỉ', 'pin'],
      ['password', 'Mật khẩu', 'key'],
      ['privacy', 'Quyền riêng tư', 'shield'],
      ['delete', 'Xóa tài khoản', 'x'],
    ];
    let body = '';
    if (tab === 'info')
      body = `<h2>Thông tin cá nhân</h2><form data-form="profile" class="stack" style="max-width:480px;margin-top:20px"><label class="field"><span>Họ và tên</span><input name="name" value="${esc(u.name)}"></label>
        <label class="field"><span>Số điện thoại</span><input name="phone" value="${esc(u.phone)}"></label>
        <label class="field"><span>Email</span><input value="${esc(u.email)}" disabled></label><div class="row tight small" style="color:var(--success)">${icon('check', 14)} Email đã xác thực</div>
        <div><button class="btn">Lưu thay đổi</button></div></form>`;
    if (tab === 'password')
      body = `<h2>Đổi mật khẩu</h2><form data-form="changePassword" class="stack" style="max-width:480px;margin-top:20px"><label class="field"><span>Mật khẩu hiện tại</span><input type="password" name="old" autocomplete="current-password"></label><label class="field"><span>Mật khẩu mới</span><input type="password" name="password" autocomplete="new-password"></label><p class="small muted">Đổi mật khẩu sẽ đăng xuất các thiết bị khác.</p><div><button class="btn">Đổi mật khẩu</button></div></form>`;
    if (tab === 'addresses')
      body = `<div class="row between"><h2 style="margin:0">Sổ địa chỉ</h2><button class="btn sm secondary" data-act="toastMsg" data-msg="Mở form thêm địa chỉ (tối đa 10)">${icon('plus', 16)} Thêm địa chỉ</button></div><div class="grid cols-2" style="margin-top:20px">${[
        ['Nhà riêng', '12 Nguyễn Huệ, Phường Bến Nghé, TP. Hồ Chí Minh', true],
        ['Công ty', '8 Lý Thường Kiệt, Phường Hoàn Kiếm, Hà Nội', false],
      ]
        .map(
          ([l, a, def]) =>
            `<div class="card tight"><div class="row between"><b>${l}</b>${def ? '<span class="badge">Mặc định</span>' : ''}</div><p class="small" style="margin:8px 0 12px">${u.name} · ${u.phone}<br><span class="muted">${a}</span></p><div class="row tight"><button class="btn sm secondary" data-act="toastMsg" data-msg="Mở form sửa địa chỉ">Sửa</button>${def ? '' : '<button class="btn sm ghost" data-act="toastMsg" data-msg="Đã đặt làm mặc định">Đặt mặc định</button>'}</div></div>`,
        )
        .join('')}</div>`;
    if (tab === 'privacy') {
      const c = S.consent || { personalization: false, marketing: false };
      body = `<h2>Quyền riêng tư</h2><div class="card" style="margin-top:20px;max-width:640px;padding:8px 24px">
        <label class="pref-row switch"><div><b>Cần thiết</b><p>Giỏ hàng, đăng nhập, bảo mật. Luôn bật.</p></div><input type="checkbox" checked disabled></label>
        <label class="pref-row switch"><div><b>Cá nhân hóa</b><p>Gợi ý sản phẩm dựa trên những gì bạn xem và mua. Tắt thì dữ liệu bị xóa ngay.</p></div><input type="checkbox" data-change="togglePersonalization" ${c.personalization ? 'checked' : ''}></label>
        <label class="pref-row switch"><div><b>Tiếp thị</b><p>Nhận ưu đãi phù hợp qua email.</p></div><input type="checkbox" data-change="toggleMarketing" ${c.marketing ? 'checked' : ''}></label></div>
        <h3 style="margin-top:32px">Pickory hiểu bạn thích gì</h3>
        ${
          c.personalization
            ? `<div class="card" style="max-width:640px"><div class="grid cols-2"><div><div class="small muted">Danh mục quan tâm</div><b>Laptop gaming, Tai nghe</b></div><div><div class="small muted">Thương hiệu hay xem</div><b>ASUS, MSI</b></div><div><div class="small muted">Tầm giá laptop</div><b>25 đến 35 triệu</b></div><div><div class="small muted">Ưu tiên</div><b>RAM 16GB, màn 16 inch</b></div></div>
          <hr class="sep"><div class="row between"><span class="small muted">Dựa trên ${S.recent.length} sản phẩm đã xem và 1 đơn đã mua</span><button class="btn sm secondary" data-act="clearHistory">Xóa lịch sử xem</button></div></div>`
            : `<p class="muted">Bạn chưa bật cá nhân hóa nên Pickory không lưu sở thích của bạn.</p>`
        }`;
    }
    if (tab === 'delete')
      body = `<h2>Xóa tài khoản</h2><div class="card" style="max-width:560px;margin-top:20px"><p>Email và tên của bạn sẽ bị ẩn danh và bạn không thể đăng nhập lại. Lịch sử đơn hàng được giữ cho mục đích kế toán, không gắn với danh tính của bạn.</p>
        <form data-form="deleteAccount" class="stack"><label class="field"><span>Nhập mật khẩu để xác nhận</span><input type="password" name="password" autocomplete="current-password"></label><div><button class="btn danger">Xóa vĩnh viễn tài khoản</button></div></form></div>`;
    return `<div class="grid" style="grid-template-columns:240px 1fr;gap:48px;align-items:start">
      <nav aria-label="Tài khoản"><div class="row tight" style="margin-bottom:20px;flex-wrap:nowrap"><span class="avatar">${PK.initials(u.name)}</span><div style="line-height:1.3;min-width:0"><b>${esc(u.name)}</b><div class="small muted">${esc(u.email)}</div></div></div>
        ${tabs.map(([k, l, ic]) => `<a href="#/account?tab=${k}" class="row tight" style="padding:9px 12px;border-radius:10px;${tab === k ? 'background:var(--bg-2);font-weight:600' : 'color:var(--ink-2)'}">${icon(ic, 18)} ${l}</a>`).join('')}
        <a href="#" class="row tight" style="padding:9px 12px;color:var(--ink-2)" data-act="logoutCustomer">${icon('logout', 18)} Đăng xuất</a></nav>
      <div>${body}</div></div>`;
  });
  act('toastMsg', (d) => toast(d.msg));
  act('logoutCustomer', () => {
    PK.S.role = 'guest';
    PK.go('/');
    return false;
  });
  form('profile', () => toast('Đã lưu thay đổi'));
  form('changePassword', (data) =>
    String(data.password).length < 8
      ? toast('Mật khẩu mới cần tối thiểu 8 ký tự')
      : toast('Đã đổi mật khẩu, các thiết bị khác đã được đăng xuất'),
  );
  change('togglePersonalization', (el) => {
    const S = PK.S;
    S.consent = Object.assign({ marketing: false }, S.consent, {
      personalization: el.checked,
      at: PK.now(),
    });
    if (!el.checked) S.recent = [];
    toast(el.checked ? 'Đã bật cá nhân hóa' : 'Đã tắt cá nhân hóa và xóa dữ liệu hành vi');
    PK.render();
  });
  change('toggleMarketing', (el) => {
    PK.S.consent = Object.assign({ personalization: false }, PK.S.consent, {
      marketing: el.checked,
      at: PK.now(),
    });
    toast('Đã lưu lựa chọn');
  });
  act('clearHistory', () => {
    PK.S.recent = [];
    toast('Đã xóa lịch sử xem');
    PK.render();
  });
  form('deleteAccount', (data) => {
    if (data.password !== '12345678') return toast('Mật khẩu không đúng');
    PK.S.role = 'guest';
    toast('Tài khoản đã được xóa');
    PK.go('/');
  });

  // ================= knowledge-base: trung tâm hỗ trợ =================
  const TYPE_LABEL = {
    policy: 'Chính sách',
    faq: 'Câu hỏi thường gặp',
    guide: 'Hướng dẫn chọn mua',
    internal: 'Nội bộ',
  };
  const TYPE_ICON = { policy: 'shield', faq: 'help', guide: 'book' };
  const anchor = (s) =>
    norm(s)
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-|-$/g, '');
  route('/help', () => {
    const docs = PK.S.kbDocs.filter((d) => d.status === 'published' && d.type !== 'internal');
    return `<div style="text-align:center;padding:24px 0 40px"><h1 style="font-size:36px">Chúng tôi có thể giúp gì?</h1><p class="muted">Chính sách, câu hỏi thường gặp và hướng dẫn chọn mua.</p>
        <button class="btn secondary" data-act="askAbout">${icon('chat', 18)} Hỏi trợ lý Pickory</button></div>
      <div class="grid cols-3">${['policy', 'faq', 'guide']
        .map(
          (t) =>
            `<div class="card"><div class="row tight" style="margin-bottom:12px">${icon(TYPE_ICON[t], 20)}<h3 style="margin:0">${TYPE_LABEL[t]}</h3></div>${docs
              .filter((d) => d.type === t)
              .map(
                (d) =>
                  `<a href="#/help/${d.slug}" class="row between" style="padding:10px 0;border-top:1px solid var(--line-2)"><span>${d.title}</span>${icon('chevronRight', 16)}</a>`,
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
    if (!d)
      return '<div class="empty"><b>Không tìm thấy tài liệu</b><a href="#/help">Về trung tâm hỗ trợ</a></div>';
    return `${crumbs([['Trung tâm hỗ trợ', '#/help'], [TYPE_LABEL[d.type]]])}
      <div class="grid" style="grid-template-columns:220px 1fr;gap:56px;align-items:start"><aside class="toc" style="position:sticky;top:120px"><div class="small muted" style="margin-bottom:8px">Trong bài này</div>${d.body.map(([h]) => `<a href="#/help/${d.slug}?a=${anchor(h)}">${h}</a>`).join('')}</aside>
      <article class="doc-body"><h1 style="font-size:36px">${d.title}</h1><p class="small muted">Cập nhật ngày ${dmy(d.updatedAt)}</p>
        ${d.body.map(([h, t]) => `<h3 id="${anchor(h)}">${h}</h3><p>${esc(t)}</p>`).join('')}
        <div class="card muted-bg" style="margin-top:48px"><b>Vẫn cần hỗ trợ?</b><p class="small muted" style="margin:6px 0 12px">Trợ lý Pickory trả lời ngay, nhân viên CSKH hỗ trợ 8:00 đến 21:00.</p><button class="btn sm" data-act="askAbout">${icon('chat', 16)} Chat với chúng tôi</button></div></article></div>`;
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
      return `<h1>So sánh</h1><div class="empty"><b>Chưa có sản phẩm để so sánh</b>Nhấn "So sánh" trên trang sản phẩm, tối đa 4 sản phẩm cùng danh mục.</div>`;
    const c = PK.cat(items[0].cat);
    const attrs = D.attributes[c.parent || c.id] || [];
    const rows = [
      ['Giá', (p) => money(p.price)],
      ['Đánh giá', (p) => p.rating + ' / 5'],
      ...attrs.map((a) => [
        a.label,
        (p) => (p.specs[a.key] ?? '-') + (a.unit && p.specs[a.key] != null ? ' ' + a.unit : ''),
      ]),
      ['Bảo hành', (p) => p.warranty + ' tháng'],
    ];
    return `${crumbs([['Trang chủ', '#/'], [c.name, '#/c/' + c.id], ['So sánh']])}<div class="page-title"><h1>So sánh ${c.name.toLowerCase()}</h1><span class="small muted">Thông số khác nhau được in đậm</span></div>
      <div class="table-wrap"><table class="tbl"><tr><th style="width:180px"></th>${items.map((p) => `<th style="color:var(--ink);font-size:14px;font-weight:600;vertical-align:top;padding:20px 16px;white-space:normal"><div style="background:var(--bg-2);border-radius:12px;display:flex;justify-content:center;margin-bottom:12px">${productImg(p, 140)}</div><a href="#/p/${p.slug}">${esc(p.name)}</a></th>`).join('')}</tr>
      ${rows
        .map(([l, f]) => {
          const vals = items.map(f);
          const diff = new Set(vals).size > 1;
          return `<tr><td class="muted">${l}</td>${vals.map((v) => `<td style="${diff ? 'font-weight:600' : 'color:var(--muted)'}">${esc(v)}</td>`).join('')}</tr>`;
        })
        .join('')}
      <tr><td></td>${items.map((p) => `<td><button class="btn sm block" data-act="addToCart" data-pid="${p.id}" data-sku="${p.variants ? p.variants[0].sku : p.slug.toUpperCase()}" ${p.stock > 0 || p.variants ? '' : 'disabled'}>Thêm vào giỏ</button></td>`).join('')}</tr></table></div>
      ${note('Sản phẩm khác danh mục sẽ báo lỗi (ở đây tự loại bỏ).')}`;
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
      root.innerHTML = `<button class="chat-fab" data-act="chatOpen">${icon('chat', 20)} Hỏi trợ lý</button>`;
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
    const head = {
      bot: [
        `<span class="avatar">${icon('sparkle', 18)}</span>`,
        'Trợ lý Pickory',
        '<span class="status">Trả lời ngay</span>',
      ],
      waiting_human: [
        `<span class="avatar">${icon('headset', 18)}</span>`,
        'Đang kết nối CSKH',
        '<span class="status wait">Thời gian chờ khoảng 3 phút</span>',
      ],
      human: [
        '<span class="avatar light">L</span>',
        'Lan · CSKH Pickory',
        '<span class="status">Đang trực tuyến</span>',
      ],
    }[C.status];
    const left = LIMIT() - C.used;
    root.innerHTML = `<div class="chat-panel" role="dialog" aria-label="Trò chuyện với Pickory">
      <div class="chat-head">${head[0]}<div style="flex:1;min-width:0"><div class="title">${head[1]}</div>${head[2]}</div>
        ${C.status === 'bot' ? `<button class="btn sm secondary" data-act="handoff" style="height:32px">${icon('headset', 16)} Gặp nhân viên</button>` : ''}
        <button class="icon-btn" data-act="chatClose" aria-label="Đóng trò chuyện">${icon('x', 18)}</button></div>
      <div class="chat-body" id="chat-body" aria-live="polite">
        ${C.messages.length ? '<div class="msg system">Hội thoại được lưu 90 ngày để hỗ trợ bạn tốt hơn</div>' : `<div style="padding:8px 0 4px"><h3 style="margin-bottom:6px">Xin chào, mình có thể giúp gì?</h3><p class="muted" style="margin:0">Mình tìm, so sánh sản phẩm và giải đáp chính sách của Pickory.</p></div><div class="suggest">${suggestions.map((s) => `<button class="chip" data-act="chatSuggest" data-text="${esc(s)}">${esc(s)}</button>`).join('')}</div>`}
        ${C.messages.map((m, i) => renderMsg(m, i)).join('')}
        ${C.typing ? '<div class="typing" aria-label="Đang trả lời"><i></i><i></i><i></i></div>' : ''}
      </div>
      <div class="chat-foot">
        ${
          C.messages.length && C.status === 'bot' && !C.typing
            ? `<div class="suggest" style="margin-bottom:10px">${suggestions
                .slice(0, 2)
                .map(
                  (s) =>
                    `<button class="chip" data-act="chatSuggest" data-text="${esc(s)}">${esc(s)}</button>`,
                )
                .join('')}</div>`
            : ''
        }
        <form data-form="chat" class="composer"><input name="text" maxlength="1000" placeholder="Nhập câu hỏi..." autocomplete="off" aria-label="Tin nhắn"><button aria-label="Gửi">${icon('send', 16)}</button></form>
        <div class="chat-disclaimer">Trợ lý AI có thể nhầm. Giá và tồn kho trên thẻ sản phẩm là chính xác.${left <= 3 && C.status === 'bot' ? ` Còn ${Math.max(left, 0)} tin hôm nay.` : ''}</div>
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
        extra = `<form data-form="callback" data-i="${i}" class="composer" style="margin:8px auto 0;max-width:280px"><input name="phone" placeholder="Số điện thoại" inputmode="tel" aria-label="Số điện thoại"><button aria-label="Gửi">${icon('send', 16)}</button></form>`;
      if (m.form === 'csat' && !m.done)
        extra = `<div class="row tight" style="justify-content:center;margin-top:8px">${[1, 2, 3, 4, 5].map((n) => `<button class="icon-btn" style="border:1px solid var(--line)" data-act="csat" data-i="${i}" data-n="${n}" aria-label="${n} sao">${icon('star', 16)}</button>`).join('')}</div>`;
      if (m.form === 'handoffBtn')
        extra = `<div style="margin-top:8px"><button class="btn sm" data-act="handoff">${icon('headset', 16)} Gặp nhân viên</button></div>`;
      return `<div class="msg system">${esc(m.text)}${extra}</div>`;
    }
    if (m.role === 'staff')
      return `<div class="msg staff"><div class="who">${esc(m.staffName || 'Lan · CSKH')}</div>${esc(m.text)}</div>`;
    // assistant: trích dẫn [n] thành link tới đúng mục tài liệu
    const text = esc(m.text).replace(/\[(\d)\]/g, (_, n) => {
      const c = (m.cites || [])[Number(n) - 1];
      return c
        ? `<a class="cite" href="#/help/${c.slug}?a=${c.anchor}" title="${esc(c.title)}">${n}</a>`
        : '';
    });
    let extra = '';
    if (m.cards) extra += m.cards.map((id) => miniCard(PK.prod(id))).join('');
    if (m.compare) extra += compareMini(m.compare);
    if (m.order) {
      const o = PK.order(m.order);
      extra += `<div class="order-card"><div class="row between"><b>${o.code}</b>${PK.orderBadge(o.status)}</div><div style="margin:6px 0">${o.items.map((x) => esc(x.name)).join(', ')}</div><div class="muted">GHN ${o.tracking || ''} · ${PK.SHIP_STATUS[o.shipStatus] || ''}</div><a class="link" style="margin-top:8px" href="#/orders/${o.code}">Xem chi tiết ${icon('arrowRight', 14)}</a></div>`;
    }
    if (m.cites && m.cites.length)
      extra += `<div class="sources">${m.cites.map((c, k) => `<a href="#/help/${c.slug}?a=${c.anchor}"><b>${k + 1}</b>${esc(c.title)}</a>`).join('')}</div>`;
    const fb = m.noFeedback
      ? ''
      : `<div class="fb"><button class="${m.fb === 'up' ? 'on' : ''}" data-act="fb" data-i="${i}" data-v="up" aria-label="Hữu ích" aria-pressed="${m.fb === 'up'}">${icon('thumbUp', 15)}</button><button class="${m.fb === 'down' ? 'on' : ''}" data-act="fb" data-i="${i}" data-v="down" aria-label="Chưa hữu ích" aria-pressed="${m.fb === 'down'}">${icon('thumbDown', 15)}</button>${m.fb === 'down' && !m.fbReason ? ['Sai thông tin', 'Không hữu ích', 'Khác'].map((r) => `<button class="reason" data-act="fbReason" data-i="${i}" data-r="${r}">${r}</button>`).join('') : ''}</div>`;
    return `<div class="msg assistant">${text.replace(/\n/g, '<br>')}${extra}${fb}</div>`;
  }
  function miniCard(p) {
    const v = p.variants ? p.variants[0] : null;
    const stock = v ? v.stock : p.stock;
    return `<div class="mini-card"><a class="thumb" href="#/p/${p.slug}">${productImg(p, 56)}</a><div class="meta"><a href="#/p/${p.slug}">${esc(p.name)}</a>
      <div class="xs muted" style="white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${esc(PK.specLine(p))}</div><div style="margin-top:2px"><b class="num">${money(v ? v.price : p.price)}</b> <span class="xs ${stock > 0 ? '' : 'muted'}">· ${stock > 0 ? 'Còn hàng' : 'Hết hàng'}</span></div></div>
      <div class="actions">${p.variants ? `<select aria-label="Phiên bản">${p.variants.map((x) => `<option>${Object.values(x.opts).join(' · ')}</option>`).join('')}</select>` : '<span class="spacer"></span>'}
      <button class="btn sm" ${stock > 0 ? '' : 'disabled'} data-act="addToCart" data-pid="${p.id}" data-sku="${v ? v.sku : p.slug.toUpperCase()}" data-source="shopping-assistant">${icon('bag', 14)} Thêm vào giỏ</button></div></div>`;
  }
  function compareMini(ids) {
    const [a, b] = ids.map(PK.prod);
    const rows = [
      ['Giá', money(a.price), money(b.price)],
      ['CPU', a.specs.cpu, b.specs.cpu],
      ['GPU', a.specs.gpu, b.specs.gpu],
      ['RAM', a.specs.ram_gb + 'GB', b.specs.ram_gb + 'GB'],
      ['Nặng', a.specs.weight + ' kg', b.specs.weight + ' kg'],
    ];
    return `<div class="table-wrap" style="margin-top:8px"><table class="tbl compact small"><tr><th></th><th>${esc(a.name.replace('Laptop ', ''))}</th><th>${esc(b.name.replace('Laptop ', ''))}</th></tr>${rows.map((r) => `<tr><td class="muted">${r[0]}</td><td>${esc(r[1])}</td><td>${esc(r[2])}</td></tr>`).join('')}</table></div><a class="link small" style="margin-top:8px" href="#/compare?ids=${a.id},${b.id}">So sánh đầy đủ ${icon('arrowRight', 14)}</a>`;
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
    renderChat(true);
  });
  act('chatSuggest', (d) => sendChat(d.text));
  form('chat', (data) => {
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
    m.text = `Bạn đã đánh giá ${d.n}/5. Cảm ơn bạn! Trợ lý AI tiếp tục hỗ trợ nếu bạn cần.`;
    renderChat();
    PK.save();
  });
  form('callback', (data, f) => {
    const S = PK.S;
    if (!/^0\d{9}$/.test(String(data.phone).replace(/\s/g, '')))
      return toast('Số điện thoại chưa đúng');
    const m = S.chat.messages[Number(f.dataset.i)];
    m.done = true;
    m.text = 'Đã ghi nhận. CSKH sẽ gọi lại cho bạn trong giờ làm việc.';
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
    const conv = PK.S.convs.find((c) => c.id === 'c-live');
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
        text: 'Hiện ngoài giờ hỗ trợ (8:00 đến 21:00). Bạn để lại số điện thoại, CSKH sẽ gọi lại.',
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
        text: 'Đã chuyển cho nhân viên CSKH. Trợ lý AI tạm dừng trong lúc chờ.',
      });
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
        text: `Bạn đã dùng hết ${LIMIT()} tin nhắn với trợ lý hôm nay${S.role === 'guest' ? '. Đăng nhập để có 50 tin mỗi ngày' : ''}. Nhân viên CSKH vẫn sẵn sàng hỗ trợ.`,
        form: 'handoffBtn',
      });
      renderChat();
      return PK.save();
    }
    if (S.aiDown || S.budgetPct >= 120) {
      push({
        role: 'system',
        text: 'Trợ lý tạm thời không khả dụng. Bạn có thể trò chuyện với nhân viên CSKH.',
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
    }, 800);
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
      const lighter = a.specs.weight < b.specs.weight ? a : b;
      return {
        role: 'assistant',
        text: `Hai máy cùng dùng ${a.specs.gpu === b.specs.gpu ? a.specs.gpu : 'card RTX'}, hiệu năng chơi game tương đương. ${lighter.name} nhẹ hơn nên hợp mang đi học hơn.`,
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
            title: 'Chính sách đổi trả · Thời hạn đổi trả',
          },
        ],
        tools: ['search_knowledge'],
      };
    if (/bao hanh|roi vo/.test(t))
      return {
        role: 'assistant',
        text: 'Sản phẩm được bảo hành theo thời gian của hãng [1]. Hàng bị rơi vỡ không được bảo hành miễn phí, Pickory hỗ trợ báo giá sửa chữa [1].',
        cites: [
          {
            slug: 'chinh-sach-bao-hanh',
            anchor: 'hang-bi-roi-vo',
            title: 'Chính sách bảo hành · Hàng bị rơi vỡ',
          },
        ],
        tools: ['search_knowledge'],
      };
    if (/giao hang|ship|bao lau|van chuyen/.test(t))
      return {
        role: 'assistant',
        text: 'Pickory giao qua GHN: nội thành TP. Hồ Chí Minh khoảng 1 ngày, tỉnh khác 2 đến 4 ngày [1]. Đơn từ 500.000 đ được miễn phí vận chuyển [1].',
        cites: [
          {
            slug: 'chinh-sach-van-chuyen',
            anchor: 'thoi-gian-giao',
            title: 'Chính sách vận chuyển · Thời gian giao',
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
        text: 'Đi máy bay thì khả năng chống ồn là quan trọng nhất. Đây là 3 mẫu chống ồn được khách đánh giá cao:',
        cards: ['p17', 'p18', 'p19'],
        tools: ['search_products'],
      };
    if (/dien thoai|iphone|samsung|pin trau|chup anh/.test(t))
      return {
        role: 'assistant',
        text: 'Mình gợi ý vài mẫu pin lâu và chụp ảnh đẹp trong tầm giá phổ biến:',
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
        text: `Trong tầm ${m ? m[1] + ' triệu' : 'giá bạn nêu'}${light ? ', ưu tiên máy nhẹ để mang đi học' : ''}, mình gợi ý ${picks.length} mẫu.${light ? ` ${picks[0].name} nhẹ nhất (${picks[0].specs.weight} kg) mà vẫn có ${picks[0].specs.gpu}.` : ''}`,
        cards: picks.map((p) => p.id),
        tools: ['search_products'],
      };
    }
    if (/thoi tiet|bong da|chinh tri|lap trinh|viet code|bai tap/.test(t))
      return {
        role: 'assistant',
        text: 'Mình chỉ hỗ trợ về sản phẩm, đơn hàng và chính sách của Pickory. Bạn đang tìm sản phẩm gì?',
        tools: [],
      };
    return {
      role: 'assistant',
      text: 'Xin lỗi, mình chưa có thông tin về câu hỏi này.',
      noInfo: true,
      tools: ['search_knowledge'],
    };
  }

  PK.renderChat = renderChat;
})();
