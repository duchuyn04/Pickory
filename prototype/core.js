/* Lõi prototype: state, router, helper, component dùng chung, bảng điều khiển prototype. */
/* eslint-disable */
(function () {
  const D = window.PK_DATA;
  const icon = window.PK_ICON;
  const KEY = 'pickory-proto-v2';
  const clone = (x) => JSON.parse(JSON.stringify(x));

  // ---------- State dùng chung (lưu localStorage để các luồng nối nhau giữa các màn hình) ----------
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
      showNotes: false,
      devOpen: false,
      aiSettings: { budget: 30, userLimit: 50, guestLimit: 15 },
      chat: { open: false, status: 'bot', messages: [], used: 0, noInfo: 0, noticeShown: false },
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
  // Sao đánh giá bằng icon (đầy theo điểm làm tròn)
  function stars(r, size = 13) {
    const n = Math.round(Number(r));
    return `<span class="stars" aria-label="${r} trên 5 sao">${[1, 2, 3, 4, 5].map((i) => `<span class="${i <= n ? '' : 'off'}">${icon('star', size, 'fill')}</span>`).join('')}</span>`;
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
        shipping: 'info',
        confirmed: 'info',
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
  const initials = (name) =>
    name
      .split(' ')
      .slice(-2)
      .map((w) => w[0])
      .join('')
      .toUpperCase();

  // ---------- Ảnh sản phẩm: render thiết bị tông trung tính ----------
  function hue(str) {
    let h = 0;
    for (const ch of str) h = (h * 31 + ch.charCodeAt(0)) % 360;
    return h;
  }
  const FINISH = {
    graphite: { body: '#3a3d44', base: '#4b4e56', edge: '#2a2c31', line: 'none' },
    silver: { body: '#d6d7dc', base: '#e4e5e9', edge: '#b9bbc2', line: 'none' },
    white: { body: '#f4f4f6', base: '#fbfbfc', edge: '#d9d9de', line: '#d5d5da' },
  };
  function finishOf(p, color) {
    if (color === 'Trắng') return p.cat.startsWith('laptop') ? 'silver' : 'white';
    if (color === 'Đen') return 'graphite';
    if (p.cat === 'sac' || /AirPods/.test(p.name)) return 'white';
    if (p.brand === 'Apple' && p.cat.startsWith('laptop')) return 'silver';
    if (p.cat === 'laptop-van-phong') return 'silver';
    return 'graphite';
  }
  // Hình nền màn hình đổi tông theo từng sản phẩm (như ảnh chụp máy có wallpaper), thân máy giữ màu trung tính
  function screenDefs(id, h) {
    return `<defs><linearGradient id="${id}" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="hsl(${h} 30% 16%)"/><stop offset=".55" stop-color="hsl(${(h + 25) % 360} 38% 30%)"/><stop offset="1" stop-color="hsl(${(h + 50) % 360} 45% 48%)"/></linearGradient>
    <linearGradient id="pkGlare" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#fff" stop-opacity=".09"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient></defs>`;
  }
  function productImg(p, size = 120, color) {
    const c = cat(p.cat);
    const kind = c ? c.icon : 'laptop';
    const f = FINISH[finishOf(p, color)];
    const st = f.line === 'none' ? '' : ` stroke="${f.line}" stroke-width="1"`;
    const h = hue(p.slug);
    const scrId = 'pkScr' + h;
    const shadow = (cy, rx) =>
      `<ellipse cx="100" cy="${cy}" rx="${rx}" ry="4" fill="#000" opacity=".07"/>`;
    const shapes = {
      laptop: `${shadow(156, 82)}<rect x="32" y="40" width="136" height="90" rx="7" fill="${f.body}"${st}/><rect x="38" y="46" width="124" height="78" rx="2" fill="url(#pkScr)"/><path d="M38 46h70l-40 78H38z" fill="url(#pkGlare)"/><circle cx="100" cy="43" r="1" fill="${f.edge}"/>
        <path d="M18 132h164l-6 12a6 6 0 0 1-5.4 3.4H29.4A6 6 0 0 1 24 144z" fill="${f.base}"${st}/><rect x="86" y="132" width="28" height="3" rx="1.5" fill="${f.edge}"/>`,
      phone: `${shadow(160, 50)}<rect x="56" y="30" width="58" height="122" rx="13" fill="${f.body}"${st}/><rect x="62" y="36" width="26" height="26" rx="8" fill="${f.edge}"/><circle cx="70" cy="44" r="4.5" fill="#15161a"/><circle cx="80" cy="54" r="4.5" fill="#15161a"/>
        <rect x="88" y="40" width="58" height="122" rx="13" fill="${f.edge}"/><rect x="91" y="43" width="52" height="116" rx="10" fill="url(#pkScr)"/><path d="M91 43h30l-30 70z" fill="url(#pkGlare)"/><rect x="108" y="48" width="18" height="5" rx="2.5" fill="#0c0d10"/>`,
      headphone: `${shadow(160, 60)}<path d="M58 112c0-34 18-62 42-62s42 28 42 62" fill="none" stroke="${f.edge}" stroke-width="9" stroke-linecap="round"/><path d="M58 112c0-34 18-62 42-62s42 28 42 62" fill="none" stroke="${f.body}" stroke-width="5" stroke-linecap="round"/>
        <rect x="42" y="96" width="34" height="58" rx="16" fill="${f.body}"${st}/><rect x="124" y="96" width="34" height="58" rx="16" fill="${f.body}"${st}/><rect x="66" y="102" width="12" height="46" rx="6" fill="${f.edge}"/><rect x="122" y="102" width="12" height="46" rx="6" fill="${f.edge}"/>`,
      monitor: `${shadow(160, 50)}<rect x="22" y="34" width="156" height="96" rx="5" fill="${f.edge}"/><rect x="26" y="38" width="148" height="86" rx="2" fill="url(#pkScr)"/><path d="M26 38h80l-46 86H26z" fill="url(#pkGlare)"/>
        <path d="M92 130h16l4 22H88z" fill="${f.body}"/><rect x="66" y="150" width="68" height="7" rx="3.5" fill="${f.base}"/>`,
      mouse: `${shadow(152, 34)}<rect x="74" y="36" width="52" height="112" rx="26" fill="${f.body}"${st}/><path d="M100 38v42" stroke="${f.edge}" stroke-width="2"/><rect x="96" y="54" width="8" height="18" rx="4" fill="${f.edge}"/><path d="M76 84c8 4 40 4 48 0" stroke="${f.edge}" stroke-width="1.5" fill="none"/>`,
      bag: `${shadow(160, 50)}<path d="M84 42a16 16 0 0 1 32 0" fill="none" stroke="${f.edge}" stroke-width="6"/><rect x="58" y="44" width="84" height="110" rx="24" fill="${f.body}"${st}/><rect x="72" y="98" width="56" height="42" rx="12" fill="${f.base}"/><rect x="84" y="108" width="32" height="3" rx="1.5" fill="${f.edge}"/>`,
      charger: `${shadow(150, 34)}<rect x="88" y="34" width="6" height="20" rx="2" fill="#9a9ca3"/><rect x="106" y="34" width="6" height="20" rx="2" fill="#9a9ca3"/><rect x="70" y="52" width="60" height="90" rx="14" fill="${f.body}"${st}/>
        <rect x="84" y="96" width="32" height="7" rx="3.5" fill="${f.edge}"/><rect x="84" y="110" width="32" height="7" rx="3.5" fill="${f.edge}"/><rect x="90" y="124" width="20" height="7" rx="3.5" fill="${f.edge}"/>`,
    };
    return `<svg width="${size}" height="${size}" viewBox="0 0 200 200" role="img" aria-label="${esc(p.name)}">${screenDefs(scrId, h)}${(shapes[kind] || shapes.laptop).replace(/url\(#pkScr\)/g, `url(#${scrId})`)}</svg>`;
  }
  // Dòng thông số ngắn trên thẻ sản phẩm
  function specLine(p) {
    const s = p.specs;
    if (s.gpu)
      return [
        String(s.cpu).replace(/^(Intel Core |AMD |Apple )/, ''),
        s.gpu,
        s.ram_gb && s.ram_gb + 'GB',
      ]
        .filter(Boolean)
        .join(' · ');
    if (s.chip) return [s.chip, s.ram_gb + 'GB RAM', s.battery + ' mAh'].join(' · ');
    if (s.connect)
      return [
        s.anc ? 'Chống ồn chủ động' : null,
        s.connect,
        s.battery_h ? s.battery_h + ' giờ pin' : null,
      ]
        .filter(Boolean)
        .join(' · ');
    return p.short;
  }

  // ---------- Component dùng chung ----------
  function productCard(p, opts = {}) {
    const out = p.stock <= 0 && p.status === 'active';
    const disc = p.status === 'discontinued';
    let tag = '';
    if (disc) tag = '<span class="tag out">Ngừng kinh doanh</span>';
    else if (out) tag = '<span class="tag out">Tạm hết hàng</span>';
    else if (p.compareAt)
      tag = `<span class="tag sale">Giảm ${money(p.compareAt - p.price).replace(' đ', 'đ')}</span>`;
    else if (p.reviewCount > 60) tag = '<span class="tag">Bán chạy</span>';
    return `<a class="pcard" href="#/p/${p.slug}" data-act="trackClick" data-pid="${p.id}" data-source="${opts.source || 'category'}">
      <div class="img">${tag}${productImg(p, 180)}</div>
      <div class="brand">${esc(p.brand)}</div>
      <div class="name">${esc(p.name)}</div>
      <div class="spec">${esc(specLine(p))}</div>
      <div class="rating">${icon('star', 12, 'fill')} ${p.rating} <span class="muted">(${p.reviewCount})</span></div>
      <div class="price-row"><span class="price">${money(p.price)}</span>${p.compareAt ? `<span class="strike small">${money(p.compareAt)}</span>` : ''}</div>
      ${opts.reason ? `<div class="reason">${icon('sparkle', 13)}<span>${esc(opts.reason)}</span></div>` : ''}
    </a>`;
  }
  // Ghi chú cho người xem prototype, ẩn mặc định (bật trong bảng điều khiển)
  function note(html) {
    return `<div class="proto-note"><b>Ghi chú prototype:</b> ${html}</div>`;
  }
  function toast(msg) {
    const old = document.querySelector('.toast');
    if (old) old.remove();
    const t = document.createElement('div');
    t.className = 'toast';
    t.setAttribute('role', 'status');
    t.innerHTML = `${icon('check', 16)}<span></span>`;
    t.querySelector('span').textContent = msg;
    document.body.appendChild(t);
    setTimeout(() => t.remove(), 2800);
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

  // Biểu đồ cột bằng SVG (thay Recharts trong prototype), có lưới ngang mảnh
  function barChart(values, labels, opts = {}) {
    const w = 720,
      h = opts.h || 220,
      padL = 8,
      padB = 24,
      padT = 8;
    const max = Math.max(...values, 1) * 1.1;
    const bw = (w - padL * 2) / values.length;
    const y = (v) => h - padB - ((h - padB - padT) * v) / max;
    const grid = [0.25, 0.5, 0.75, 1]
      .map(
        (g) =>
          `<line x1="${padL}" x2="${w - padL}" y1="${y(max * g)}" y2="${y(max * g)}" stroke="#f0f0f2"/>`,
      )
      .join('');
    const bars = values
      .map((v, i) => {
        const color = opts.highlight && opts.highlight(i) ? '#b42318' : '#ec6436';
        return `<rect x="${padL + i * bw + bw * 0.18}" y="${y(v)}" width="${bw * 0.64}" height="${h - padB - y(v)}" rx="2" fill="${color}"><title>${esc(labels[i])}: ${opts.fmt ? opts.fmt(v) : v}</title></rect>`;
      })
      .join('');
    const ticks = labels
      .map((l, i) =>
        i % Math.ceil(labels.length / 10) === 0
          ? `<text x="${padL + i * bw + bw / 2}" y="${h - 6}" font-size="11" text-anchor="middle" fill="#9c9ca5" font-family="inherit">${esc(l)}</text>`
          : '',
      )
      .join('');
    return `<svg class="chart" viewBox="0 0 ${w} ${h}" role="img" aria-label="${esc(opts.label || 'Biểu đồ')}">${grid}${bars}<line x1="${padL}" x2="${w - padL}" y1="${h - padB}" y2="${h - padB}" stroke="#e6e6ea"/>${ticks}</svg>`;
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
  const act = (name, fn) => (ACT[name] = fn);
  const form = (name, fn) => (FORM[name] = fn);
  const change = (name, fn) => (CHANGE[name] = fn);
  const input = (name, fn) => (INPUT[name] = fn);
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
    if (e.key === 'Escape') {
      closeModal();
      if (S.devOpen) {
        S.devOpen = false;
        renderDev();
      }
    }
  });

  // ---------- Bảng điều khiển prototype (nút nổi góc trái dưới) ----------
  const ROLE_LABEL = {
    guest: 'Khách vãng lai',
    customer: 'Khách hàng · Minh Anh',
    admin: 'Admin · Hoàng Nam',
    sales: 'Sales · Đức',
    support: 'CSKH · Lan',
  };
  function renderDev() {
    let root = document.getElementById('dev-root');
    if (!root) {
      root = document.createElement('div');
      root.id = 'dev-root';
      document.body.appendChild(root);
    }
    const sw = (key, label) =>
      `<label class="pref-row switch"><span>${label}</span><input type="checkbox" data-change="devToggle" data-key="${key}" ${S[key] ? 'checked' : ''}></label>`;
    root.innerHTML = `<div class="devpill">
      ${
        S.devOpen
          ? `<div class="devpanel" role="dialog" aria-label="Điều khiển prototype">
        <h4>Xem như</h4><select data-change="role" aria-label="Vai trò">${Object.entries(ROLE_LABEL)
          .map(([v, l]) => `<option value="${v}" ${S.role === v ? 'selected' : ''}>${l}</option>`)
          .join('')}</select>
        <h4>Giả lập tình huống</h4>${sw('aiDown', 'API AI đang lỗi')}${sw('outOfHours', 'Ngoài giờ CSKH')}
        <label class="pref-row"><span>Ngân sách AI đã dùng</span><select data-change="budget" style="width:auto">${[65, 85, 105, 125].map((v) => `<option value="${v}" ${S.budgetPct === v ? 'selected' : ''}>${v}%</option>`).join('')}</select></label>
        <h4>Hiển thị</h4>${sw('showNotes', 'Ghi chú cho người review')}
        <h4>Đi tới</h4><div class="links"><a class="chip" href="#/flows">Bản đồ luồng</a><a class="chip" href="#/">Cửa hàng</a><a class="chip" href="#/admin">Back-office</a></div>
        <hr class="sep"><button class="btn sm secondary block" data-act="resetAll">Đặt lại dữ liệu mẫu</button>
      </div>`
          : ''
      }
      <button data-act="devToggleOpen" aria-expanded="${S.devOpen}"><i>PROTOTYPE</i>${esc(ROLE_LABEL[S.role])}${icon(S.devOpen ? 'chevronDown' : 'settings', 15)}</button></div>`;
  }
  act('devToggleOpen', () => {
    S.devOpen = !S.devOpen;
    renderDev();
    save();
  });
  change('devToggle', (el) => {
    S[el.dataset.key] = el.checked;
    if (el.dataset.key === 'aiDown')
      toast(
        el.checked ? 'Giả lập: API AI lỗi, circuit breaker mở' : 'API AI hoạt động bình thường',
      );
    render();
  });
  change('role', (el) => {
    S.role = el.value;
    toast('Đang xem như: ' + ROLE_LABEL[S.role]);
    if (isStaff() && !location.hash.startsWith('#/admin')) go('/admin');
    else if (!isStaff() && location.hash.startsWith('#/admin')) go('/');
    else render();
  });
  change('budget', (el) => {
    S.budgetPct = Number(el.value);
    toast(`Giả lập: đã dùng ${S.budgetPct}% ngân sách AI tháng`);
    render();
  });
  act('resetAll', () => {
    if (!confirm('Xóa toàn bộ thay đổi và quay về dữ liệu mẫu ban đầu?')) return;
    S = seed();
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
    if (!hit)
      html = `<div class="empty"><b>Không tìm thấy trang</b><a href="#/">Về trang chủ</a></div>`;
    else {
      layout = hit.r.layout;
      html = hit.r.fn(hit.params, query);
    }
    const L = window.PK.layouts;
    document.getElementById('app').innerHTML = L[layout] ? L[layout](html, path) : html;
    document.body.classList.toggle('notes-on', !!S.showNotes);
    if (S.lastPath !== path) {
      window.scrollTo(0, 0);
      S.lastPath = path;
    }
    if (window.PK.afterRender) window.PK.afterRender(layout, path);
    renderDev();
    save();
  }
  window.addEventListener('hashchange', render);

  window.PK = {
    D,
    icon,
    get S() {
      return S;
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
    initials,
    productImg,
    specLine,
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
