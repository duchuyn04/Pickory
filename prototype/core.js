/* Lõi prototype: state, router, helper, component dùng chung, thanh điều khiển. */
/* eslint-disable */
(function () {
  const D = window.PK_DATA;
  const KEY = 'pickory-proto-v1';
  const clone = (x) => JSON.parse(JSON.stringify(x));

  // ---------- State dùng chung cho toàn prototype (lưu localStorage để luồng nối nhau) ----------
  function seed() {
    return {
      role: 'guest', // guest | customer | admin | sales | support
      consent: null, // null = chưa chọn; { personalization, marketing }
      cart: [],
      recent: [],
      products: clone(D.products),
      orders: clone(D.orders),
      reviews: clone(D.reviews),
      sepayTx: clone(D.sepayTx),
      stockMoves: clone(D.stockMoves),
      convs: clone(D.conversations),
      callbacks: clone(D.callbacks),
      canned: clone(D.canned),
      aiContents: clone(D.aiContents),
      users: clone(D.users),
      roles: clone(D.roles),
      audit: clone(D.auditLogs),
      kbDocs: clone(D.kbDocs),
      aiDown: false,
      outOfHours: false,
      budgetPct: 65,
      aiSettings: { budget: 30, userLimit: 50, guestLimit: 15 },
      chat: {
        open: false,
        status: 'bot',
        messages: [],
        used: 0,
        noInfo: 0,
        convId: null,
        csat: null,
        noticeShown: false,
      },
      presence: 'available',
      experiment: { control: 10, algo: 45, llm: 45 },
      complements: [
        { from: 'Laptop', to: 'Chuột', weight: 1, repurchasable: true },
        { from: 'Laptop', to: 'Balo', weight: 0.8, repurchasable: false },
        { from: 'Laptop', to: 'Tai nghe', weight: 0.6, repurchasable: true },
        { from: 'Điện thoại', to: 'Sạc', weight: 1, repurchasable: true },
        { from: 'Điện thoại', to: 'Tai nghe', weight: 0.7, repurchasable: true },
      ],
      supportSettings: {
        start: '08:00',
        end: '21:00',
        nudge: 10,
        autoClose: 30,
        holidays: 'Tết Nguyên đán, 30/4, 1/5, 2/9',
      },
      commerceSettings: {
        freeShip: 500000,
        codMax: 20000000,
        account: '0123456789',
        bank: 'MB Bank',
      },
      shippingSettings: {
        name: 'Kho Pickory',
        phone: '028 3333 4444',
        address: '1 Lê Duẩn, Phường Bến Nghé, TP. Hồ Chí Minh',
        shopId: '885512',
        service: 'Chuẩn',
      },
      heroIdx: 0,
      pendingPay: null,
      lastPath: '',
    };
  }
  let S;
  try {
    S = JSON.parse(localStorage.getItem(KEY)) || seed();
  } catch (e) {
    S = seed();
  }
  function save() {
    try {
      localStorage.setItem(KEY, JSON.stringify(S));
    } catch (e) {
      /* trình duyệt chặn storage: prototype vẫn chạy, chỉ không nhớ state */
    }
  }

  // ---------- Helper ----------
  const esc = (s) =>
    String(s ?? '').replace(
      /[&<>"']/g,
      (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c],
    );
  const money = (n) => Number(n).toLocaleString('vi-VN') + ' đ';
  const stars = (r) => '★★★★★'.slice(0, Math.round(r)) + '☆☆☆☆☆'.slice(0, 5 - Math.round(r));
  const norm = (s) =>
    String(s).toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd');
  const prod = (id) => S.products.find((p) => p.id === id);
  const prodBySlug = (slug) => S.products.find((p) => p.slug === slug);
  const cat = (id) => D.categories.find((c) => c.id === id);
  const catChildren = (id) => D.categories.filter((c) => c.parent === id).map((c) => c.id);
  const inCat = (p, cid) => p.cat === cid || catChildren(cid).includes(p.cat);
  const visible = (p) => p.status === 'active' || p.status === 'discontinued';
  const order = (code) => S.orders.find((o) => o.code === code);
  const orderTotal = (o) =>
    o.items.reduce((s, i) => s + i.price * i.qty, 0) + o.shippingFee - o.shippingDiscount;
  const now = () => {
    const d = new Date();
    const p = (x) => String(x).padStart(2, '0');
    return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
  };
  function orderCode() {
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'; // không có 0, O, 1, I
    let c = 'PK';
    for (let i = 0; i < 8; i++) c += chars[Math.floor(Math.random() * chars.length)];
    return c;
  }

  const ORDER_STATUS = {
    pending_payment: 'Chờ thanh toán',
    pending_confirmation: 'Chờ xác nhận',
    confirmed: 'Đã xác nhận',
    shipping: 'Đang giao',
    delivered: 'Đã giao',
    returned: 'Đã hoàn hàng',
    cancelled: 'Đã hủy',
  };
  const PAY_STATUS = {
    unpaid: 'Chưa thanh toán',
    paid: 'Đã thanh toán',
    refund_pending: 'Chờ hoàn tiền',
    refunded: 'Đã hoàn tiền',
  };
  const SHIP_STATUS = {
    pending_pickup: 'Chờ lấy hàng',
    in_transit: 'Đang trung chuyển',
    delivering: 'Đang giao',
    delivered: 'Đã giao',
    delivery_failed: 'Giao thất bại',
    returning: 'Đang hoàn',
    returned: 'Đã hoàn',
    cancelled: 'Đã hủy',
    exception: 'Có sự cố',
  };
  function orderBadge(s) {
    const cls =
      {
        delivered: 'ok',
        cancelled: 'gray',
        pending_payment: 'warn',
        pending_confirmation: 'warn',
        shipping: '',
        confirmed: '',
        returned: 'gray',
      }[s] ?? '';
    return `<span class="badge ${cls}">${ORDER_STATUS[s] ?? s}</span>`;
  }
  function payBadge(s) {
    const cls = { paid: 'ok', unpaid: 'gray', refund_pending: 'warn', refunded: 'gray' }[s] ?? '';
    return `<span class="badge ${cls}">${PAY_STATUS[s] ?? s}</span>`;
  }

  // ---------- Người dùng hiện tại và quyền ----------
  const ACTOR_BY_ROLE = { customer: 'u1', admin: 's1', sales: 's2', support: 's3' };
  const me = () => S.users.find((u) => u.id === ACTOR_BY_ROLE[S.role]) || null;
  const isStaff = () => ['admin', 'sales', 'support'].includes(S.role);
  function permsOf(user) {
    if (!user) return new Set();
    if (user.roles.includes('admin')) return new Set(D.allPerms); // admin có mọi quyền ngầm định
    const set = new Set();
    user.roles.forEach((code) => {
      const r = S.roles.find((x) => x.code === code);
      if (r && Array.isArray(r.perms)) r.perms.forEach((p) => set.add(p));
    });
    return set;
  }
  const can = (perm) => permsOf(me()).has(perm);
  const personalized = () => !!(S.consent && S.consent.personalization);

  // ---------- Ảnh sản phẩm giả lập bằng SVG ----------
  function hue(str) {
    let h = 0;
    for (const ch of str) h = (h * 31 + ch.charCodeAt(0)) % 360;
    return h;
  }
  function productImg(p, size = 120) {
    const c = cat(p.cat);
    const icon = c ? c.icon : 'laptop';
    const h = hue(p.brand || 'x');
    const a = `hsl(${h} 45% 30%)`;
    const b = `hsl(${(h + 40) % 360} 60% 55%)`;
    const shapes = {
      laptop: `<rect x="22" y="26" width="76" height="50" rx="5" fill="${a}"/><rect x="27" y="31" width="66" height="40" rx="2" fill="${b}"/><path d="M12 80h96l-8 10H20z" fill="${a}"/>`,
      phone: `<rect x="40" y="12" width="40" height="88" rx="9" fill="${a}"/><rect x="44" y="20" width="32" height="70" rx="4" fill="${b}"/>`,
      headphone: `<path d="M28 70V58a32 32 0 0 1 64 0v12" fill="none" stroke="${a}" stroke-width="8"/><rect x="20" y="62" width="20" height="30" rx="8" fill="${b}"/><rect x="80" y="62" width="20" height="30" rx="8" fill="${b}"/>`,
      monitor: `<rect x="14" y="20" width="92" height="58" rx="5" fill="${a}"/><rect x="19" y="25" width="82" height="48" rx="2" fill="${b}"/><rect x="54" y="78" width="12" height="12" fill="${a}"/><rect x="40" y="90" width="40" height="5" rx="2" fill="${a}"/>`,
      mouse: `<rect x="40" y="20" width="40" height="76" rx="20" fill="${a}"/><path d="M60 22v26" stroke="${b}" stroke-width="4"/><rect x="56" y="34" width="8" height="12" rx="4" fill="${b}"/>`,
      bag: `<rect x="28" y="30" width="64" height="66" rx="14" fill="${a}"/><path d="M45 30a15 15 0 0 1 30 0" fill="none" stroke="${a}" stroke-width="6"/><rect x="38" y="58" width="44" height="26" rx="6" fill="${b}"/>`,
      charger: `<rect x="36" y="30" width="48" height="56" rx="10" fill="${a}"/><path d="M50 30V16M70 30V16" stroke="${a}" stroke-width="6"/><path d="M62 42l-10 16h10l-4 14 12-18H60z" fill="${b}"/>`,
    };
    return `<svg width="${size}" height="${size}" viewBox="0 0 120 120" role="img" aria-label="${esc(p.name)}">${shapes[icon] || shapes.laptop}</svg>`;
  }

  // ---------- Component dùng chung ----------
  function productCard(p, opts = {}) {
    const out = p.stock <= 0 && p.status === 'active';
    const disc = p.status === 'discontinued';
    let badge = '';
    if (disc) badge = '<span class="badge gray">Ngừng kinh doanh</span>';
    else if (out) badge = '<span class="badge gray">Hết hàng</span>';
    else if (p.compareAt)
      badge = `<span class="badge sale">-${Math.round((1 - p.price / p.compareAt) * 100)}%</span>`;
    else if (p.reviewCount > 60) badge = '<span class="badge">Phổ biến</span>';
    return `<a class="pcard" href="#/p/${p.slug}" data-act="trackClick" data-pid="${p.id}" data-source="${opts.source || 'category'}">
      <div class="img">${badge}${productImg(p, 130)}</div>
      <div class="name">${esc(p.name)}</div>
      <div class="row small"><span class="stars">${stars(p.rating)}</span><span class="muted">${p.rating} (${p.reviewCount})</span></div>
      <div><span class="price">${money(p.price)}</span> ${p.compareAt ? `<span class="strike small">${money(p.compareAt)}</span>` : ''}</div>
      ${opts.reason ? `<div class="reason">${esc(opts.reason)}</div>` : ''}
    </a>`;
  }
  function note(html) {
    return `<div class="proto-note"><b>Ghi chú prototype:</b> ${html}</div>`;
  }
  function toast(msg) {
    const old = document.querySelector('.toast');
    if (old) old.remove();
    const t = document.createElement('div');
    t.className = 'toast';
    t.textContent = msg;
    document.body.appendChild(t);
    setTimeout(() => t.remove(), 2600);
  }
  function modal(html, wide) {
    closeModal();
    const bg = document.createElement('div');
    bg.className = 'modal-bg';
    bg.id = 'modal';
    bg.innerHTML = `<div class="modal ${wide ? 'wide' : ''}" role="dialog" aria-modal="true">${html}</div>`;
    bg.addEventListener('click', (e) => {
      if (e.target === bg) closeModal();
    });
    document.body.appendChild(bg);
    const first = bg.querySelector('input,select,textarea,button');
    if (first) first.focus();
  }
  function closeModal() {
    const m = document.getElementById('modal');
    if (m) m.remove();
  }

  // Biểu đồ cột và đường bằng SVG (thay Recharts trong prototype)
  function barChart(values, labels, opts = {}) {
    const w = 640,
      h = opts.h || 180,
      pad = 28;
    const max = Math.max(...values, 1);
    const bw = (w - pad * 2) / values.length;
    const bars = values
      .map((v, i) => {
        const bh = ((h - pad * 2) * v) / max;
        const color =
          opts.highlight && opts.highlight(i)
            ? 'var(--color-sale-ink)'
            : opts.color || 'var(--color-ink)';
        return `<rect x="${pad + i * bw + 2}" y="${h - pad - bh}" width="${Math.max(bw - 4, 2)}" height="${bh}" rx="2" fill="${color}"><title>${esc(labels[i])}: ${opts.fmt ? opts.fmt(v) : v}</title></rect>`;
      })
      .join('');
    const ticks = labels
      .map((l, i) =>
        i % Math.ceil(labels.length / 10) === 0
          ? `<text x="${pad + i * bw + bw / 2}" y="${h - 8}" font-size="10" text-anchor="middle" fill="#6b6b76">${esc(l)}</text>`
          : '',
      )
      .join('');
    const ref = opts.refLine
      ? `<line x1="${pad}" x2="${w - pad}" y1="${h - pad - ((h - pad * 2) * opts.refLine) / max}" y2="${h - pad - ((h - pad * 2) * opts.refLine) / max}" stroke="var(--color-warning)" stroke-dasharray="4 4"/>`
      : '';
    return `<svg class="chart" viewBox="0 0 ${w} ${h}">${bars}${ref}<line x1="${pad}" x2="${w - pad}" y1="${h - pad}" y2="${h - pad}" stroke="#ececec"/>${ticks}</svg>`;
  }

  // ---------- Router ----------
  const routes = [];
  function route(pattern, fn, layout = 'shop') {
    const keys = [];
    const re = new RegExp(
      '^' + pattern.replace(/:(\w+)/g, (_, k) => (keys.push(k), '([^/]+)')) + '$',
    );
    routes.push({ re, keys, fn, layout });
  }
  function parseHash() {
    const raw = location.hash.slice(1) || '/flows';
    const [path, qs] = raw.split('?');
    const query = Object.fromEntries(new URLSearchParams(qs || ''));
    return { path: path || '/', query };
  }
  function go(path) {
    location.hash = path;
  }

  // ---------- Hành động (event delegation) ----------
  const ACT = {};
  const FORM = {};
  const CHANGE = {};
  const INPUT = {};
  function act(name, fn) {
    ACT[name] = fn;
  }
  function form(name, fn) {
    FORM[name] = fn;
  }
  function change(name, fn) {
    CHANGE[name] = fn;
  }
  function input(name, fn) {
    INPUT[name] = fn;
  }
  document.addEventListener('click', (e) => {
    const el = e.target.closest('[data-act]');
    if (!el || !ACT[el.dataset.act]) return;
    const keep = ACT[el.dataset.act](el.dataset, el, e);
    if (el.tagName === 'BUTTON' || keep === false) e.preventDefault();
  });
  document.addEventListener('submit', (e) => {
    const f = e.target.closest('form[data-form]');
    if (!f || !FORM[f.dataset.form]) return;
    e.preventDefault();
    const data = {};
    new FormData(f).forEach((v, k) => {
      data[k] = data[k] !== undefined ? [].concat(data[k], v) : v;
    });
    FORM[f.dataset.form](data, f);
  });
  document.addEventListener('change', (e) => {
    const el = e.target.closest('[data-change]');
    if (el && CHANGE[el.dataset.change]) CHANGE[el.dataset.change](el, e);
  });
  document.addEventListener('input', (e) => {
    const el = e.target.closest('[data-input]');
    if (el && INPUT[el.dataset.input]) INPUT[el.dataset.input](el, e);
  });
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') closeModal();
  });

  // ---------- Thanh điều khiển prototype ----------
  function devbar() {
    const roleOpts = [
      ['guest', 'Khách vãng lai'],
      ['customer', 'Khách hàng: Minh Anh'],
      ['admin', 'Admin: Hoàng Nam'],
      ['sales', 'Sales: Đức'],
      ['support', 'CSKH: Lan'],
    ]
      .map(([v, l]) => `<option value="${v}" ${S.role === v ? 'selected' : ''}>${l}</option>`)
      .join('');
    const budgetOpts = [65, 85, 105, 125]
      .map((v) => `<option value="${v}" ${S.budgetPct === v ? 'selected' : ''}>${v}%</option>`)
      .join('');
    return `<div class="devbar">
      <span class="tag">PROTOTYPE</span>
      <a href="#/flows" style="color:#fff">Bản đồ luồng</a>
      <a href="#/" style="color:#fff">Cửa hàng</a>
      <a href="#/admin" style="color:#fff">Back-office</a>
      <span class="spacer"></span>
      <label>Xem như <select data-change="role" aria-label="Đổi vai trò">${roleOpts}</select></label>
      <label><input type="checkbox" data-change="aiDown" ${S.aiDown ? 'checked' : ''}> AI lỗi</label>
      <label><input type="checkbox" data-change="outOfHours" ${S.outOfHours ? 'checked' : ''}> Ngoài giờ CSKH</label>
      <label>Ngân sách AI <select data-change="budget">${budgetOpts}</select></label>
      <button data-act="resetAll">Đặt lại dữ liệu</button>
    </div>`;
  }
  change('role', (el) => {
    S.role = el.value;
    if (S.role === 'customer' && S.chat.convId === null) S.chat.used = 0;
    toast('Đang xem như: ' + el.options[el.selectedIndex].text);
    if (isStaff() && !location.hash.startsWith('#/admin')) go('/admin');
    else if (!isStaff() && location.hash.startsWith('#/admin')) go('/');
    else render();
  });
  change('aiDown', (el) => {
    S.aiDown = el.checked;
    toast(
      S.aiDown ? 'Giả lập: API AI đang lỗi (circuit breaker mở)' : 'API AI hoạt động bình thường',
    );
    render();
  });
  change('outOfHours', (el) => {
    S.outOfHours = el.checked;
    render();
  });
  change('budget', (el) => {
    S.budgetPct = Number(el.value);
    toast(`Giả lập: đã dùng ${S.budgetPct}% ngân sách AI tháng`);
    render();
  });
  act('resetAll', () => {
    if (!confirm('Xóa toàn bộ thay đổi và quay về dữ liệu mẫu ban đầu?')) return;
    S = seed();
    window.PK.S = S;
    save();
    go('/flows');
    render();
  });
  act('closeModal', () => closeModal());

  // ---------- Vẽ trang ----------
  function render() {
    const { path, query } = parseHash();
    let hit = null;
    for (const r of routes) {
      const m = path.match(r.re);
      if (m) {
        const params = {};
        r.keys.forEach((k, i) => (params[k] = decodeURIComponent(m[i + 1])));
        hit = { r, params };
        break;
      }
    }
    let html;
    let layout = 'shop';
    if (!hit) html = `<h1>Không tìm thấy trang</h1><p><a href="#/">Về trang chủ</a></p>`;
    else {
      layout = hit.r.layout;
      html = hit.r.fn(hit.params, query);
    }
    const root = document.getElementById('app');
    const L = window.PK.layouts;
    root.innerHTML = devbar() + (L[layout] ? L[layout](html, path) : html);
    if (S.lastPath !== path) {
      window.scrollTo(0, 0);
      S.lastPath = path;
    }
    if (window.PK.afterRender) window.PK.afterRender(layout, path);
    save();
  }
  window.addEventListener('hashchange', render);

  window.PK = {
    D,
    get S() {
      return S;
    },
    set S(v) {
      S = v;
    },
    save,
    render,
    route,
    go,
    act,
    form,
    change,
    input,
    esc,
    money,
    stars,
    norm,
    prod,
    prodBySlug,
    cat,
    catChildren,
    inCat,
    visible,
    order,
    orderTotal,
    now,
    orderCode,
    ORDER_STATUS,
    PAY_STATUS,
    SHIP_STATUS,
    orderBadge,
    payBadge,
    me,
    isStaff,
    permsOf,
    can,
    personalized,
    productImg,
    productCard,
    note,
    toast,
    modal,
    closeModal,
    barChart,
    parseHash,
    layouts: {},
  };
})();
