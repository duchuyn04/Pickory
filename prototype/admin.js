/* Back-office: admin-console (khung, menu theo quyền, báo cáo), identity (nhân viên, role, khách, nhật ký),
   catalog, commerce, shipping, support-desk, knowledge-base, personalized-content, recommendations,
   smart-search, shopping-assistant (xem hội thoại AI), ai-gateway. */
/* eslint-disable */
(function () {
  const PK = window.PK;
  const D = PK.D;
  const {
    icon,
    esc,
    money,
    route,
    act,
    form,
    change,
    input,
    note,
    toast,
    modal,
    closeModal,
    productImg,
    barChart,
  } = PK;

  function audit(action, target, diff) {
    PK.S.audit.unshift({ at: PK.now(), actor: PK.me().name, action, target, diff: diff || '' });
  }
  const forbidden = (perm) =>
    `<div class="empty" style="max-width:560px">${icon('lock', 28)}<b style="margin-top:12px">Bạn không có quyền truy cập</b>Trang này cần quyền <code>${perm}</code>. Liên hệ quản trị viên nếu bạn cần truy cập.</div>${note('Backend vẫn kiểm tra quyền bằng requirePermission và trả 403; ẩn menu chỉ để tiện dùng.')}`;
  const gate = (perm, fn) => (params, q) => (PK.can(perm) ? fn(params, q) : forbidden(perm));
  // Chưa đăng nhập nhân viên thì không chạy trang, layout sẽ hiện form đăng nhập
  const adminRoute = (path, perm, fn) => {
    const page = perm ? gate(perm, fn) : fn;
    route(path, (params, q) => (PK.isStaff() ? page(params, q) : ''), 'admin');
  };
  const head = (title, sub, actions = '') =>
    `<div class="page-head"><div><h1>${title}</h1>${sub ? `<div class="sub">${sub}</div>` : ''}</div><div class="row tight">${actions}</div></div>`;
  const crumbs = (items) =>
    `<nav class="breadcrumb" aria-label="Breadcrumb">${items.map(([l, h], i) => (i ? icon('chevronRight', 14) : '') + (h ? `<a href="${h}">${l}</a>` : `<span>${l}</span>`)).join('')}</nav>`;
  const kpi = (l, v, d, neg) =>
    `<div class="kpi"><div class="l">${l}</div><div class="v">${v}</div>${d ? `<div class="d ${neg ? 'neg' : ''}">${d}</div>` : ''}</div>`;
  const dmy = (s) =>
    s.slice(0, 10).split('-').reverse().join('/') + (s.length > 10 ? ' ' + s.slice(11) : '');

  // ================= admin-console: khung back-office =================
  const MENU = [
    { sec: 'Vận hành' },
    { label: 'Tổng quan', path: '/admin', perm: null, icon: 'home' },
    {
      label: 'Đơn hàng',
      path: '/admin/orders',
      perm: 'orders:read',
      icon: 'package',
      badge: () => PK.S.orders.filter((o) => o.status === 'pending_confirmation').length,
    },
    {
      label: 'Đối soát thanh toán',
      path: '/admin/payments',
      perm: 'payments:read',
      icon: 'card',
      badge: () => PK.S.sepayTx.filter((t) => t.match === 'unmatched').length,
    },
    {
      label: 'CSKH',
      path: '/admin/support',
      perm: 'conversations:read',
      icon: 'headset',
      badge: () =>
        PK.S.convs.filter((c) => c.status === 'waiting_human').length +
        PK.S.callbacks.filter((c) => c.status === 'pending').length,
    },
    {
      label: 'Kiểm duyệt đánh giá',
      path: '/admin/reviews',
      perm: 'reviews:moderate',
      icon: 'star',
      badge: () => PK.S.reviews.filter((r) => r.reports >= 3 && r.status === 'visible').length,
    },
    { sec: 'Sản phẩm' },
    { label: 'Sản phẩm', path: '/admin/products', perm: 'products:read', icon: 'box' },
    { label: 'Danh mục', path: '/admin/categories', perm: 'products:write', icon: 'layers' },
    { label: 'Thương hiệu', path: '/admin/brands', perm: 'products:write', icon: 'tag' },
    { label: 'Tồn kho', path: '/admin/inventory', perm: 'inventory:write', icon: 'store' },
    { sec: 'AI và nội dung' },
    {
      label: 'Hội thoại AI',
      path: '/admin/conversations',
      perm: 'conversations:read',
      icon: 'chat',
    },
    { label: 'Kho tri thức', path: '/admin/knowledge', perm: 'knowledge:read', icon: 'book' },
    {
      label: 'Nội dung AI',
      path: '/admin/ai-content',
      perm: 'products:read',
      icon: 'sparkle',
      badge: () => PK.S.aiContents.filter((c) => c.status === 'pending_approval').length,
    },
    { label: 'Gợi ý sản phẩm', path: '/admin/recommendations', perm: 'reports:read', icon: 'wand' },
    { label: 'Tìm kiếm', path: '/admin/search', perm: 'reports:read', icon: 'search' },
    { label: 'Chi phí AI', path: '/admin/ai', perm: 'ai-usage:read', icon: 'cpu' },
    { sec: 'Phân tích' },
    { label: 'Báo cáo', path: '/admin/reports', perm: 'reports:read', icon: 'chart' },
    { sec: 'Hệ thống' },
    { label: 'Khách hàng', path: '/admin/customers', perm: 'customers:read', icon: 'user' },
    { label: 'Nhân viên', path: '/admin/staff', perm: 'staff:read', icon: 'users' },
    { label: 'Role và quyền', path: '/admin/roles', perm: 'roles:manage', icon: 'key' },
    { label: 'Nhật ký thao tác', path: '/admin/audit', perm: 'audit-logs:read', icon: 'history' },
    {
      label: 'Cấu hình',
      path: '/admin/settings',
      perm: null,
      icon: 'settings',
      any: [
        'commerce-settings:manage',
        'shipping-settings:manage',
        'support-settings:manage',
        'ai-settings:manage',
      ],
    },
  ];
  const menuVisible = (m) => (m.any ? m.any.some(PK.can) : !m.perm || PK.can(m.perm));

  PK.layouts.admin = function (html, path) {
    const S = PK.S;
    if (!PK.isStaff()) return staffLogin();
    const u = PK.me();
    const items = MENU.filter((m) => m.sec || menuVisible(m));
    // Bỏ tiêu đề nhóm không còn mục nào
    const navHtml = items
      .filter((m, i) => !m.sec || (items[i + 1] && !items[i + 1].sec))
      .map((m) => {
        if (m.sec) return `<div class="sec">${m.sec}</div>`;
        const on = m.path === '/admin' ? path === '/admin' : path.startsWith(m.path);
        const b = m.badge ? m.badge() : 0;
        return `<a href="#${m.path}" class="${on ? 'on' : ''}" ${on ? 'aria-current="page"' : ''}>${icon(m.icon, 18)}<span>${m.label}</span>${b ? `<span class="cnt">${b}</span>` : ''}</a>`;
      })
      .join('');
    const handlesChat = PK.can('conversations:reply');
    return `<div class="admin">
      <aside>
        <div class="brand"><a class="logo" href="#/admin" style="font-size:20px">pickory<i></i></a><small>Back-office</small></div>
        ${handlesChat ? `<label class="presence switch" style="display:flex;justify-content:space-between"><span class="row tight"><span class="badge ${S.presence === 'available' ? 'ok' : 'gray'}" style="background:none;padding:0">${S.presence === 'available' ? 'Sẵn sàng nhận chat' : 'Tạm vắng'}</span></span><input type="checkbox" data-change="presence" ${S.presence === 'available' ? 'checked' : ''} aria-label="Trạng thái nhận chat"></label>` : ''}
        <nav aria-label="Back-office">${navHtml}</nav>
        <div class="me-box"><span class="avatar">${PK.initials(u.name)}</span><div class="info"><b>${esc(u.name)}</b><span>${u.roles.map((r) => S.roles.find((x) => x.code === r).name).join(', ')}</span></div>
          <button class="icon-btn" data-act="staffLogout" aria-label="Đăng xuất" title="Đăng xuất">${icon('logout', 18)}</button></div>
      </aside>
      <main class="content" id="main">${html}</main></div>`;
  };
  function staffLogin() {
    return `<div class="login-wrap"><div class="login-card">
      <a class="logo" href="#/" style="margin-bottom:24px">pickory<i></i></a>
      <h1 style="font-size:24px">Đăng nhập back-office</h1><p class="muted">Dành cho nhân viên Pickory.</p>
      <form data-form="staffLogin" class="stack" style="margin-top:20px"><label class="field"><span>Email công việc</span><input name="email" type="email" value="nam.admin@pickory.vn" autocomplete="username"></label>
      <label class="field"><span>Mật khẩu</span><input name="password" type="password" autocomplete="current-password"></label><button class="btn block">Đăng nhập</button></form>
      <p class="xs muted" style="margin-top:16px">Phiên làm việc kéo dài 12 giờ. Quên mật khẩu? Liên hệ quản trị viên.</p>
      <div class="proto-note" style="display:block"><b>Đăng nhập nhanh</b> (mật khẩu 12345678):
        <div class="row tight" style="margin-top:8px"><button class="btn sm" data-act="quickStaff" data-role="admin">Admin</button><button class="btn sm secondary" data-act="quickStaff" data-role="sales">Sales</button><button class="btn sm secondary" data-act="quickStaff" data-role="support">CSKH</button></div></div>
    </div></div>`;
  }
  route('/admin/login', () => '', 'admin');
  form('staffLogin', (data) => {
    const role = {
      'nam.admin@pickory.vn': 'admin',
      'duc.sales@pickory.vn': 'sales',
      'lan.cskh@pickory.vn': 'support',
    }[data.email];
    if (!role || data.password !== '12345678') return toast('Email hoặc mật khẩu không đúng');
    PK.S.role = role;
    PK.go('/admin');
  });
  act('quickStaff', (d) => {
    PK.S.role = d.role;
    PK.go('/admin');
    PK.render();
  });
  act('staffLogout', () => {
    PK.S.role = 'guest';
    PK.go('/admin/login');
  });
  change('presence', (el) => {
    PK.S.presence = el.checked ? 'available' : 'away';
    toast(el.checked ? 'Bạn đang sẵn sàng nhận chat' : 'Tạm vắng: không nhận chat mới');
    PK.render();
  });

  // ================= admin-console: trang chủ theo role =================
  adminRoute('/admin', null, () => {
    const S = PK.S;
    const cards = [];
    if (PK.can('orders:read')) {
      cards.push([
        'Đơn chờ xác nhận',
        S.orders.filter((o) => o.status === 'pending_confirmation').length,
        '#/admin/orders?tab=pending_confirmation',
        'package',
      ]);
      cards.push([
        'Đơn COD chưa nhận tiền',
        S.orders.filter(
          (o) => o.method === 'cod' && o.status === 'delivered' && o.payment === 'unpaid',
        ).length,
        '#/admin/payments?tab=cod',
        'card',
      ]);
    }
    if (PK.can('payments:read'))
      cards.push([
        'Giao dịch SePay chưa khớp',
        S.sepayTx.filter((t) => t.match === 'unmatched').length,
        '#/admin/payments',
        'refresh',
      ]);
    if (PK.can('products:read'))
      cards.push([
        'SKU sắp hết hàng',
        lowStock().length,
        PK.can('inventory:write') ? '#/admin/inventory' : '#/admin/products?low=1',
        'box',
      ]);
    if (PK.can('conversations:read')) {
      cards.push([
        'Hội thoại đang chờ',
        S.convs.filter((c) => c.status === 'waiting_human').length,
        '#/admin/support',
        'headset',
      ]);
      cards.push([
        'Yêu cầu gọi lại',
        S.callbacks.filter((c) => c.status === 'pending').length,
        '#/admin/support?tab=callbacks',
        'phone',
      ]);
    }
    if (PK.can('products:write'))
      cards.push([
        'Banner AI chờ duyệt',
        S.aiContents.filter((c) => c.status === 'pending_approval').length,
        '#/admin/ai-content',
        'sparkle',
      ]);
    const rd = D.revenueDaily.slice(-14);
    return `${head(`Chào ${esc(PK.me().name.split(' ').pop())}`, new Date().toLocaleDateString('vi-VN', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }))}
      ${S.budgetPct >= 80 && PK.can('ai-usage:read') ? `<div class="alert warn" style="margin-bottom:20px">${icon('alert', 18)}<span>Đã dùng <b>${S.budgetPct}%</b> ngân sách AI tháng. ${S.budgetPct >= 100 ? 'Nội dung cá nhân hóa, xếp hạng gợi ý và bản nháp CSKH đang tạm dừng.' : 'Sắp chạm ngưỡng 100%.'} <a href="#/admin/ai"><u>Xem chi tiết</u></a></span></div>` : ''}
      <h3 style="margin-bottom:12px">Việc cần làm</h3>
      <div class="grid cols-3">${cards.map(([l, v, h, ic]) => `<a class="todo" href="${h}"><div class="top">${icon(ic, 18)}${icon('arrowRight', 16)}</div><div><div class="v">${v}</div><div class="l">${l}</div></div></a>`).join('')}</div>
      ${
        PK.can('reports:read')
          ? `<div class="panel" style="margin-top:32px"><div class="ph"><h3>Doanh thu 14 ngày</h3><a class="link small" href="#/admin/reports">Báo cáo đầy đủ ${icon('arrowRight', 14)}</a></div><div class="pb">${barChart(
              rd.map((x) => x.revenue),
              rd.map((x) => String(x.day)),
              { fmt: money, h: 180, label: 'Doanh thu 14 ngày' },
            )}</div></div>`
          : ''
      }
      ${note('Trang chủ hiện việc cần làm theo quyền: sales thấy đơn và thanh toán, CSKH thấy hàng chờ hội thoại, admin thấy tất cả và banner chờ duyệt.')}`;
  });

  // ================= commerce: đơn hàng =================
  const TABS = [
    ['', 'Tất cả'],
    ['pending_payment', 'Chờ thanh toán'],
    ['pending_confirmation', 'Chờ xác nhận'],
    ['confirmed', 'Đã xác nhận'],
    ['shipping', 'Đang giao'],
    ['delivered', 'Đã giao'],
    ['cancelled', 'Đã hủy'],
  ];
  const FLAG_LABEL = {
    underpaid: 'Thiếu tiền',
    overpaid: 'Thừa tiền',
    invoice_requested: 'Cần xuất VAT',
  };
  adminRoute('/admin/orders', 'orders:read', (_, q) => {
    const S = PK.S;
    const tab = q.tab || '';
    let list = S.orders.filter((o) => !tab || o.status === tab);
    if (q.flag) list = list.filter((o) => o.flags.includes(q.flag));
    return `${head('Đơn hàng', '', `<button class="btn secondary" data-act="exportOrders">${icon('arrowRight', 16)} Xuất Excel</button>`)}
      <div class="tabs">${TABS.map(([k, l]) => `<a href="#/admin/orders${k ? '?tab=' + k : ''}" class="${tab === k ? 'on' : ''}">${l} <span class="n">${S.orders.filter((o) => !k || o.status === k).length}</span></a>`).join('')}</div>
      <div class="filters-bar"><select data-change="orderFlag" aria-label="Lọc theo cờ"><option value="">Tất cả cờ</option>${Object.entries(
        FLAG_LABEL,
      )
        .map(([k, l]) => `<option value="${k}" ${q.flag === k ? 'selected' : ''}>${l}</option>`)
        .join('')}</select></div>
      <div class="table-wrap"><table class="tbl"><tr><th>Mã đơn</th><th>Ngày</th><th>Khách hàng</th><th class="num">Tổng tiền</th><th>Thanh toán</th><th>Trạng thái</th><th>Cờ</th></tr>
      ${list.map((o) => `<tr class="click" data-act="nav" data-href="#/admin/orders/${o.code}"><td><b>${o.code}</b></td><td class="muted">${dmy(o.createdAt)}</td><td>${esc(o.customer)}${o.userId ? '' : '<div class="xs muted">Khách vãng lai</div>'}</td><td class="num">${money(PK.orderTotal(o))}</td><td><div>${o.method === 'cod' ? 'COD' : 'VietQR'}</div>${PK.payBadge(o.payment)}</td><td>${PK.orderBadge(o.status)}</td><td>${o.flags.map((f) => `<span class="badge warn">${FLAG_LABEL[f]}</span>`).join(' ')}</td></tr>`).join('') || `<tr><td colspan="7"><div class="empty" style="border:0"><b>Không có đơn hàng</b></div></td></tr>`}</table></div>`;
  });
  change('orderFlag', (el) => {
    const { query } = PK.parseHash();
    query.flag = el.value;
    location.hash =
      '/admin/orders?' + new URLSearchParams(Object.entries(query).filter(([, v]) => v)).toString();
  });
  act('exportOrders', () => {
    if (!PK.can('reports:export')) return toast('Bạn cần quyền reports:export để xuất dữ liệu');
    audit('report.export', 'orders', 'Xuất ' + PK.S.orders.length + ' đơn');
    toast('Đã tạo file don-hang.xlsx');
  });

  adminRoute('/admin/orders/:code', 'orders:read', (p) => {
    const o = PK.order(p.code);
    if (!o) return '<div class="empty"><b>Không tìm thấy đơn</b></div>';
    const btns = [];
    if (o.status === 'pending_confirmation' && PK.can('orders:update-status'))
      btns.push(
        `<button class="btn" data-act="orderConfirm" data-code="${o.code}">${icon('check', 16)} Xác nhận đơn</button>`,
      );
    if (o.status === 'confirmed' && PK.can('orders:fulfill'))
      btns.push(
        `<button class="btn" data-act="shipCreate" data-code="${o.code}">${icon('truck', 16)} Tạo vận đơn</button>`,
      );
    if (o.status === 'shipping' && PK.can('orders:fulfill'))
      btns.push(
        `<button class="btn secondary" data-act="shipCancel" data-code="${o.code}">Hủy vận đơn</button>`,
      );
    if (
      o.status === 'delivered' &&
      o.method === 'cod' &&
      o.payment === 'unpaid' &&
      PK.can('payments:reconcile')
    )
      btns.push(
        `<button class="btn" data-act="codReceived" data-code="${o.code}">Đã nhận tiền COD</button>`,
      );
    if (o.payment === 'refund_pending' && PK.can('refunds:manage'))
      btns.push(
        `<button class="btn" data-act="refunded" data-code="${o.code}">Đã hoàn tiền</button>`,
      );
    if (o.flags.includes('invoice_requested') && PK.can('orders:update-status'))
      btns.push(
        `<button class="btn secondary" data-act="invoiced" data-code="${o.code}">Đã xuất hóa đơn</button>`,
      );
    if (
      ['pending_payment', 'pending_confirmation', 'confirmed'].includes(o.status) &&
      PK.can('orders:cancel')
    )
      btns.push(
        `<button class="btn danger" data-act="orderCancel" data-code="${o.code}">Hủy đơn</button>`,
      );
    return `${crumbs([['Đơn hàng', '#/admin/orders'], [o.code]])}
      ${head(`${o.code} ${PK.orderBadge(o.status)}`, `Đặt lúc ${dmy(o.createdAt)} · ${o.method === 'cod' ? 'COD' : 'VietQR'} · ${PK.PAY_STATUS[o.payment]}`, btns.join(''))}
      ${o.flags.length ? `<div class="row tight" style="margin-bottom:16px">${o.flags.map((f) => `<span class="badge warn">${FLAG_LABEL[f]}</span>`).join('')}</div>` : ''}
      ${o.status === 'shipping' && PK.can('orders:fulfill') ? `<div class="proto-note" style="display:block"><b>Giả lập:</b> <button class="btn sm secondary" data-act="ghnDelivered" data-code="${o.code}">Webhook GHN báo đã giao</button></div>` : ''}
      <div class="grid" style="grid-template-columns:1.4fr 1fr;align-items:start;gap:24px">
        <div class="stack">
          <div class="panel"><div class="ph"><h3>Sản phẩm</h3></div><div class="pb">${o.items.map((i) => `<div class="row" style="flex-wrap:nowrap;padding:6px 0"><span style="width:48px;height:48px;background:var(--bg-2);border-radius:8px;display:flex;align-items:center;justify-content:center;flex:none">${productImg(PK.prod(i.pid), 40)}</span><span style="flex:1">${esc(i.name)}<div class="xs muted">${i.sku} · SL ${i.qty}</div></span><b class="num">${money(i.price * i.qty)}</b></div>`).join('')}
            <hr class="sep"><div class="summary-line"><span class="muted">Phí vận chuyển</span><span class="num">${money(o.shippingFee)}</span></div>
            ${o.shippingDiscount ? `<div class="summary-line" style="margin-top:6px"><span class="muted">Shop chịu phí ship</span><span class="num">−${money(o.shippingDiscount)}</span></div>` : ''}
            <div class="summary-line total" style="margin-top:12px"><span>Tổng cộng</span><span class="num">${money(PK.orderTotal(o))}</span></div></div></div>
          <div class="panel"><div class="ph"><h3>Lịch sử trạng thái</h3></div><div class="pb"><ul class="timeline">${o.history
            .slice()
            .reverse()
            .map(
              (h) =>
                `<li class="done"><b>${PK.ORDER_STATUS[h[2]]}</b><div class="small muted">${dmy(h[0])} · ${esc(h[3])}</div></li>`,
            )
            .join('')}</ul></div></div>
        </div>
        <div class="stack">
          <div class="panel"><div class="ph"><h3>Khách hàng</h3></div><div class="pb small"><b>${esc(o.customer)}</b><br>${esc(o.phone)}<br>${esc(o.email)}<hr class="sep"><div class="muted">Giao tới</div>${esc(o.address)}</div></div>
          ${o.vat ? `<div class="panel"><div class="ph"><h3>Hóa đơn VAT</h3></div><div class="pb small">${esc(o.vat.company)}<br>MST ${esc(o.vat.taxCode)}<br>${esc(o.vat.address)}<br>${esc(o.vat.email)}</div></div>` : ''}
          <div class="panel"><div class="ph"><h3>Vận chuyển</h3>${o.tracking ? `<span class="badge info">${PK.SHIP_STATUS[o.shipStatus]}</span>` : ''}</div><div class="pb">${
            o.tracking
              ? `<div class="small" style="margin-bottom:16px">GHN · <b>${o.tracking}</b></div><ul class="timeline">${(
                  o.shipEvents || []
                )
                  .slice()
                  .reverse()
                  .map(
                    (e) =>
                      `<li class="done"><b>${PK.SHIP_STATUS[e[1]]}</b><div class="small muted">${dmy(e[0])} · <code>${e[2]}</code></div></li>`,
                  )
                  .join('')}</ul>`
              : '<p class="small muted" style="margin:0">Chưa có vận đơn.</p>'
          }</div></div>
        </div>
      </div>
      ${note('Nút hiện theo quyền và trạng thái. Đơn đang giao phải hủy vận đơn trước rồi mới hủy đơn. Chuyển trạng thái sai thứ tự trả 409. Mỗi thao tác ghi 1 dòng nhật ký.')}`;
  });
  function setStatus(o, to, why) {
    o.history.push([PK.now(), o.status, to, PK.me().name + (why ? ': ' + why : '')]);
    audit('order.status_change', 'order:' + o.code, `${o.status} → ${to}`);
    o.status = to;
  }
  act('orderConfirm', (d) => {
    setStatus(PK.order(d.code), 'confirmed');
    toast('Đã xác nhận đơn');
    PK.render();
  });
  act('shipCreate', (d) => {
    const o = PK.order(d.code);
    modal(`<h2>Tạo vận đơn GHN</h2><p class="muted">Kiểm tra thông tin kiện hàng trước khi gửi sang GHN.</p><form data-form="shipCreate" data-code="${o.code}" class="stack">
      <div class="grid cols-2"><label class="field"><span>Khối lượng (gram)</span><input name="w" value="${2600 * o.items.length}" inputmode="numeric"></label><label class="field"><span>Kích thước D × R × C (cm)</span><input name="dim" value="40 × 30 × 8"></label>
      <label class="field"><span>Thu hộ COD</span><input value="${o.method === 'cod' && o.payment === 'unpaid' ? money(PK.orderTotal(o)) : '0 đ (đã thanh toán)'}" disabled></label><label class="field"><span>Giá trị khai giá</span><input value="${money(PK.orderTotal(o))}" disabled></label></div>
      <div class="alert small">${icon('info', 16)}<span>Cho xem hàng, không cho thử. Nếu lỗi mạng, hệ thống tra GHN theo mã đơn trước khi thử lại để tránh tạo trùng.</span></div>
      <div class="row" style="justify-content:flex-end"><button type="button" class="btn secondary" data-act="closeModal">Hủy</button><button class="btn">Tạo vận đơn</button></div></form>`);
  });
  form('shipCreate', (_, f) => {
    const o = PK.order(f.dataset.code);
    o.tracking = 'GHN' + Math.random().toString(36).slice(2, 9).toUpperCase();
    o.shipStatus = 'pending_pickup';
    o.shipEvents = [[PK.now(), 'pending_pickup', 'ready_to_pick']];
    setStatus(o, 'shipping', 'tạo vận đơn ' + o.tracking);
    closeModal();
    toast('Đã tạo vận đơn ' + o.tracking);
    PK.render();
  });
  act('shipCancel', (d) => {
    const o = PK.order(d.code);
    o.shipEvents.push([PK.now(), 'cancelled', 'cancel']);
    o.shipStatus = 'cancelled';
    o.tracking = null;
    setStatus(o, 'confirmed', 'hủy vận đơn');
    PK.render();
  });
  act('ghnDelivered', (d) => {
    const o = PK.order(d.code);
    o.shipEvents.push([PK.now(), 'delivering', 'delivering'], [PK.now(), 'delivered', 'delivered']);
    o.shipStatus = 'delivered';
    o.history.push([PK.now(), 'shipping', 'delivered', 'GHN webhook']);
    o.status = 'delivered';
    toast('GHN báo đã giao hàng');
    PK.render();
  });
  act('orderCancel', (d) => {
    modal(`<h2>Hủy đơn ${d.code}</h2><p class="muted">Hàng được trả về kho. Đơn đã thanh toán chuyển sang chờ hoàn tiền.</p><form data-form="orderCancel" data-code="${d.code}" class="stack"><label class="field"><span>Lý do</span><select name="reason"><option>Shop hủy: hết hàng</option><option>Khách yêu cầu hủy qua hotline</option><option>Không liên lạc được khách</option></select></label>
      <div class="row" style="justify-content:flex-end"><button type="button" class="btn secondary" data-act="closeModal">Đóng</button><button class="btn danger">Hủy đơn</button></div></form>`);
  });
  form('orderCancel', (data, f) => {
    const o = PK.order(f.dataset.code);
    setStatus(o, 'cancelled', data.reason);
    o.cancelReason = data.reason.startsWith('Khách') ? 'khách hủy' : 'shop hủy';
    if (o.payment === 'paid') o.payment = 'refund_pending';
    closeModal();
    toast('Đã hủy đơn');
    PK.render();
  });
  act('codReceived', (d) => {
    const o = PK.order(d.code);
    o.payment = 'paid';
    audit('payment.cod_received', 'order:' + o.code, 'unpaid → paid');
    toast('Đã ghi nhận tiền COD');
    PK.render();
  });
  act('refunded', (d) => {
    modal(
      `<h2>Xác nhận đã hoàn tiền</h2><form data-form="refunded" data-code="${d.code}" class="stack"><label class="field"><span>Mã giao dịch hoàn tiền</span><input name="tx" value="FT${Date.now().toString().slice(-8)}"></label><div class="row" style="justify-content:flex-end"><button type="button" class="btn secondary" data-act="closeModal">Hủy</button><button class="btn">Xác nhận</button></div></form>`,
    );
  });
  form('refunded', (data, f) => {
    const o = PK.order(f.dataset.code);
    o.payment = 'refunded';
    audit('payment.refund', 'order:' + o.code, 'refund_pending → refunded (' + data.tx + ')');
    closeModal();
    PK.render();
  });
  act('invoiced', (d) => {
    const o = PK.order(d.code);
    o.flags = o.flags.filter((f) => f !== 'invoice_requested');
    audit('order.invoice_issued', 'order:' + o.code, '');
    PK.render();
  });

  // ================= commerce: đối soát thanh toán =================
  adminRoute('/admin/payments', 'payments:read', (_, q) => {
    const S = PK.S;
    const tab = q.tab || 'sepay';
    const MATCH = {
      matched: '<span class="badge ok">Đã khớp</span>',
      unmatched: '<span class="badge warn">Chưa khớp</span>',
      ignored: '<span class="badge gray">Không thuộc đơn</span>',
    };
    const cod = S.orders.filter(
      (o) => o.method === 'cod' && o.status === 'delivered' && o.payment === 'unpaid',
    );
    return `${head('Đối soát thanh toán', 'Giao dịch chuyển khoản từ SePay và tiền thu hộ COD')}
      <div class="tabs"><a href="#/admin/payments" class="${tab === 'sepay' ? 'on' : ''}">Giao dịch SePay <span class="n">${S.sepayTx.length}</span></a><a href="#/admin/payments?tab=cod" class="${tab === 'cod' ? 'on' : ''}">COD chưa nhận tiền <span class="n">${cod.length}</span></a></div>
      ${
        tab === 'sepay'
          ? `<div class="table-wrap"><table class="tbl"><tr><th>Mã giao dịch</th><th class="num">Số tiền</th><th>Nội dung chuyển khoản</th><th>Thời gian</th><th>Trạng thái</th><th>Đơn hàng</th><th></th></tr>
        ${S.sepayTx
          .map(
            (
              t,
            ) => `<tr><td class="muted">${t.id}</td><td class="num"><b>${money(t.amount)}</b></td><td>${esc(t.content)}${t.note ? `<div class="xs muted">${esc(t.note)}</div>` : ''}</td><td class="muted">${dmy(t.receivedAt)}</td><td>${MATCH[t.match]}</td><td>${t.order ? `<a href="#/admin/orders/${t.order}"><u>${t.order}</u></a>` : '<span class="muted">-</span>'}</td>
          <td>${t.match === 'unmatched' && PK.can('payments:reconcile') ? `<div class="row tight" style="flex-wrap:nowrap"><button class="btn sm" data-act="txMatch" data-id="${t.id}">Khớp vào đơn</button><button class="btn sm ghost" data-act="txIgnore" data-id="${t.id}">Bỏ qua</button></div>` : ''}</td></tr>`,
          )
          .join('')}</table></div>
        ${note('Webhook SePay tự khớp theo mã đơn trong nội dung. Không có mã thì vào "Chưa khớp" để khớp tay. Tiền về sau khi đơn quá hạn: thử giữ lại hàng, được thì khôi phục đơn, không thì chuyển chờ hoàn tiền.')}`
          : `<div class="table-wrap"><table class="tbl"><tr><th>Đơn hàng</th><th>Khách hàng</th><th class="num">Tiền thu hộ</th><th>Giao lúc</th><th></th></tr>${cod.map((o) => `<tr><td><a href="#/admin/orders/${o.code}"><b>${o.code}</b></a></td><td>${esc(o.customer)}</td><td class="num">${money(PK.orderTotal(o))}</td><td class="muted">${dmy(o.history[o.history.length - 1][0])}</td><td>${PK.can('payments:reconcile') ? `<button class="btn sm" data-act="codReceived" data-code="${o.code}">Đã nhận tiền</button>` : ''}</td></tr>`).join('') || `<tr><td colspan="5"><div class="empty" style="border:0"><b>Đã nhận đủ tiền COD</b></div></td></tr>`}</table></div>`
      }`;
  });
  act('txMatch', (d) => {
    const cands = PK.S.orders.filter(
      (o) => ['pending_payment', 'cancelled'].includes(o.status) && o.payment !== 'paid',
    );
    modal(`<h2>Khớp giao dịch ${d.id}</h2><p class="muted">Chọn đơn hàng tương ứng với khoản tiền này.</p><form data-form="txMatch" data-id="${d.id}" class="stack"><label class="field"><span>Đơn hàng</span><select name="order">${cands.map((o) => `<option value="${o.code}">${o.code} · ${esc(o.customer)} · ${money(PK.orderTotal(o))} · ${PK.ORDER_STATUS[o.status]}</option>`).join('')}</select></label>
      <div class="row" style="justify-content:flex-end"><button type="button" class="btn secondary" data-act="closeModal">Hủy</button><button class="btn">Khớp giao dịch</button></div></form>`);
  });
  form('txMatch', (data, f) => {
    const t = PK.S.sepayTx.find((x) => x.id === f.dataset.id);
    const o = PK.order(data.order);
    t.match = 'matched';
    t.order = o.code;
    if (o.status === 'cancelled') {
      o.payment = 'refund_pending';
      t.note = 'Đơn đã hủy và hết hàng để giữ lại: chuyển chờ hoàn tiền';
    } else {
      o.payment = 'paid';
      o.history.push([
        PK.now(),
        o.status,
        'pending_confirmation',
        PK.me().name + ' khớp tay giao dịch ' + t.id,
      ]);
      o.status = 'pending_confirmation';
    }
    audit('payment.reconcile', 'tx:' + t.id, 'unmatched → ' + o.code);
    closeModal();
    PK.render();
  });
  act('txIgnore', (d) => {
    const t = PK.S.sepayTx.find((x) => x.id === d.id);
    t.match = 'ignored';
    audit('payment.ignore', 'tx:' + t.id, '');
    PK.render();
  });

  // ================= catalog: sản phẩm =================
  const P_STATUS = {
    draft: '<span class="badge gray">Nháp</span>',
    active: '<span class="badge ok">Đang bán</span>',
    hidden: '<span class="badge gray">Đã ẩn</span>',
    discontinued: '<span class="badge warn">Ngừng kinh doanh</span>',
  };
  const totalStock = (p) => (p.variants ? p.variants.reduce((s, v) => s + v.stock, 0) : p.stock);
  adminRoute('/admin/products', 'products:read', (_, q) => {
    const S = PK.S;
    const kw = PK.norm(q.q || '');
    let list = S.products.filter(
      (p) =>
        (!q.status || p.status === q.status) &&
        (!kw || PK.norm(p.name + ' ' + p.slug).includes(kw)),
    );
    if (q.low) list = list.filter((p) => totalStock(p) <= 5);
    return `${head('Sản phẩm', `${S.products.length} sản phẩm`, PK.can('products:write') ? `<a class="btn" href="#/admin/products/new">${icon('plus', 16)} Thêm sản phẩm</a>` : '<span class="badge gray">Chỉ xem</span>')}
      <form data-form="productSearch" class="filters-bar"><input name="q" value="${esc(q.q || '')}" placeholder="Tìm theo tên hoặc SKU" style="min-width:260px" aria-label="Tìm sản phẩm">
        <select name="status" aria-label="Trạng thái"><option value="">Mọi trạng thái</option>${Object.entries(
          { draft: 'Nháp', active: 'Đang bán', hidden: 'Đã ẩn', discontinued: 'Ngừng kinh doanh' },
        )
          .map(([s, l]) => `<option value="${s}" ${q.status === s ? 'selected' : ''}>${l}</option>`)
          .join('')}</select>
        <label class="check small"><input type="checkbox" name="low" ${q.low ? 'checked' : ''}> Sắp hết hàng</label><button class="btn sm secondary">Lọc</button></form>
      <div class="table-wrap"><table class="tbl"><tr><th>Sản phẩm</th><th>Danh mục</th><th>Trạng thái</th><th class="num">Giá từ</th><th class="num">Tồn kho</th></tr>
      ${list.map((p) => `<tr class="click" data-act="nav" data-href="#/admin/products/${p.id}"><td><div class="row" style="flex-wrap:nowrap"><span style="width:44px;height:44px;background:var(--bg-2);border-radius:8px;display:flex;align-items:center;justify-content:center;flex:none">${productImg(p, 36)}</span><span><b>${esc(p.name)}</b><div class="xs muted">${p.brand}</div></span></div></td><td>${PK.cat(p.cat).name}</td><td>${P_STATUS[p.status]}</td><td class="num">${money(p.price)}</td><td class="num">${totalStock(p) <= 5 ? `<span class="badge ${totalStock(p) ? 'warn' : 'sale'}">${totalStock(p)}</span>` : totalStock(p)}</td></tr>`).join('')}</table></div>`;
  });
  form('productSearch', (data) => {
    location.hash =
      '/admin/products?' +
      new URLSearchParams(
        Object.entries({ q: data.q, status: data.status, low: data.low ? '1' : '' }).filter(
          ([, v]) => v,
        ),
      ).toString();
  });
  adminRoute('/admin/products/:id', 'products:read', (p) => {
    const isNew = p.id === 'new';
    const prod = isNew
      ? {
          id: 'new',
          name: '',
          slug: '',
          brand: 'ASUS',
          cat: 'laptop-gaming',
          status: 'draft',
          specs: {},
          price: 0,
          warranty: 24,
          short: '',
          variants: null,
          stock: 0,
        }
      : PK.prod(p.id);
    const ro = !PK.can('products:write');
    const dis = ro ? 'disabled' : '';
    const c = PK.cat(prod.cat);
    const attrs = D.attributes[c.parent || c.id] || [];
    const variants = prod.variants || [
      {
        sku: (prod.slug || 'SKU-MOI').toUpperCase(),
        opts: {},
        price: prod.price,
        compareAt: prod.compareAt,
        stock: prod.stock,
      },
    ];
    return `${crumbs([['Sản phẩm', '#/admin/products'], [isNew ? 'Thêm mới' : esc(prod.name)]])}
      ${head(`${isNew ? 'Thêm sản phẩm' : esc(prod.name)} ${P_STATUS[prod.status]}`, '', ro ? '<span class="badge gray">Chỉ xem</span>' : `<button class="btn secondary" data-act="pStatus" data-id="${prod.id}" data-s="draft">Lưu nháp</button><button class="btn" data-act="pStatus" data-id="${prod.id}" data-s="active">Đăng bán</button>`)}
      <div class="grid" style="grid-template-columns:2fr 1fr;align-items:start;gap:24px">
        <div class="stack">
          <div class="panel"><div class="ph"><h3>Thông tin chung</h3></div><div class="pb stack">
            <div class="grid cols-2"><label class="field"><span>Tên sản phẩm</span><input value="${esc(prod.name)}" ${dis}></label><label class="field"><span>Đường dẫn (slug)</span><input value="${esc(prod.slug)}" ${dis}></label>
            <label class="field"><span>Thương hiệu</span><select ${dis}>${D.brands.map((b) => `<option ${b === prod.brand ? 'selected' : ''}>${b}</option>`).join('')}</select></label>
            <label class="field"><span>Danh mục</span><select ${dis}>${D.categories
              .filter((x) => !D.categories.some((y) => y.parent === x.id))
              .map((x) => `<option ${x.id === prod.cat ? 'selected' : ''}>${x.name}</option>`)
              .join('')}</select></label></div>
            <label class="field"><span>Mô tả ngắn</span><input value="${esc(prod.short)}" ${dis}></label>
            <label class="field"><span>Mô tả chi tiết (Markdown)</span><textarea ${dis}>## Điểm nổi bật\n- ${esc(prod.short)}</textarea></label></div></div>
          <div class="panel"><div class="ph"><h3>Thông số kỹ thuật</h3><span class="small muted">Theo thuộc tính của ${c.name}</span></div><div class="pb">
            <div class="grid cols-2">${attrs.map((a) => `<label class="field"><span>${a.label}${a.unit ? ' (' + a.unit + ')' : ''}</span><input value="${esc(prod.specs[a.key] ?? '')}" ${dis}></label>`).join('') || '<p class="muted small">Danh mục chưa có thuộc tính.</p>'}</div></div></div>
          <div class="panel"><div class="ph"><h3>Biến thể</h3>${ro ? '' : `<button class="btn sm secondary" data-act="toastMsg" data-msg="Thêm trục tùy chọn (tối đa 3)">${icon('plus', 14)} Thêm tùy chọn</button>`}</div>
            <div class="table-wrap"><table class="tbl compact small"><tr><th>SKU</th><th>Tùy chọn</th><th class="num">Giá bán</th><th class="num">Giá gạch</th><th class="num">Tồn</th><th>Khối lượng</th><th>Kích thước</th><th>Bán</th></tr>
            ${variants.map((v) => `<tr><td><code>${v.sku}</code></td><td>${Object.values(v.opts).join(' · ') || '-'}</td><td class="num">${money(v.price)}</td><td class="num muted">${v.compareAt ? money(v.compareAt) : '-'}</td><td class="num">${v.stock}</td><td>2.500 g</td><td>36×26×3 cm</td><td><label class="switch"><input type="checkbox" checked ${dis} aria-label="Đang bán"></label></td></tr>`).join('')}</table></div></div>
        </div>
        <div class="stack">
          <div class="panel"><div class="ph"><h3>Trạng thái</h3></div><div class="pb stack">${P_STATUS[prod.status]}
            ${ro ? '' : `<div class="stack"><button class="btn secondary block" data-act="pStatus" data-id="${prod.id}" data-s="hidden">Ẩn khỏi cửa hàng</button><button class="btn secondary block" data-act="pStatus" data-id="${prod.id}" data-s="discontinued">Ngừng kinh doanh</button><button class="btn danger block" data-act="pDelete" data-id="${prod.id}">Xóa sản phẩm</button></div>`}</div></div>
          <div class="panel"><div class="ph"><h3>Hình ảnh</h3></div><div class="pb"><div class="grid cols-3" style="gap:8px">${[1, 2, 3].map(() => `<div style="aspect-ratio:1;background:var(--bg-2);border-radius:8px;display:flex;align-items:center;justify-content:center">${isNew ? icon('image', 20) : productImg(prod, 64)}</div>`).join('')}</div>
            ${ro ? '' : `<button class="btn secondary sm block" style="margin-top:12px" data-act="toastMsg" data-msg="Chọn ảnh JPG, PNG, WebP tối đa 5 MB, tối đa 10 ảnh">${icon('plus', 14)} Tải ảnh lên</button>`}<p class="xs muted" style="margin:8px 0 0">Kéo để sắp xếp. Gắn ảnh với từng màu và nhập mô tả ảnh.</p></div></div>
        </div>
      </div>
      ${note('Đăng bán cần ít nhất 1 ảnh và 1 biến thể có giá, khối lượng, kích thước. Giá gạch phải lớn hơn giá bán. Chỉ xóa hẳn được sản phẩm chưa từng có đơn. Form này chỉ minh họa, sửa trường chưa được lưu.')}`;
  });
  act('pStatus', (d) => {
    if (d.id === 'new') {
      if (d.s === 'active')
        return toast(
          'Chưa đăng bán được: cần ít nhất 1 ảnh và 1 biến thể có giá, khối lượng, kích thước',
        );
      toast('Đã lưu nháp sản phẩm mới');
      return PK.go('/admin/products');
    }
    const p = PK.prod(d.id);
    audit('product.status_change', 'product:' + p.id, `${p.status} → ${d.s}`);
    p.status = d.s;
    toast('Đã cập nhật trạng thái sản phẩm');
    PK.render();
  });
  act('pDelete', (d) => {
    const hasOrder = PK.S.orders.some((o) => o.items.some((i) => i.pid === d.id));
    toast(
      hasOrder
        ? 'Không xóa được vì sản phẩm đã có đơn. Hãy chọn "Ngừng kinh doanh".'
        : 'Đã xóa sản phẩm',
    );
  });

  // ================= catalog: danh mục, thuộc tính, thương hiệu =================
  adminRoute('/admin/categories', 'products:write', (_, q) => {
    const sel = q.c || 'laptop';
    const attrs = D.attributes[sel] || [];
    const tree = D.categories
      .filter((c) => !c.parent)
      .map(
        (c) =>
          `<a href="#/admin/categories?c=${c.id}" class="row between" style="padding:8px 12px;border-radius:8px;${sel === c.id ? 'background:var(--bg-2);font-weight:600' : ''}"><span>${c.name}</span><span class="xs muted">${PK.S.products.filter((p) => PK.inCat(p, c.id)).length}</span></a>${D.categories
            .filter((x) => x.parent === c.id)
            .map(
              (x) => `<div class="small muted" style="padding:4px 12px 4px 28px">${x.name}</div>`,
            )
            .join('')}`,
      )
      .join('');
    return `${head('Danh mục và thuộc tính', 'Cây danh mục 2 cấp, thuộc tính sinh form thông số và bộ lọc', `<button class="btn" data-act="toastMsg" data-msg="Mở form tạo danh mục">${icon('plus', 16)} Thêm danh mục</button>`)}
      <div class="grid" style="grid-template-columns:260px 1fr;align-items:start;gap:24px"><div class="panel"><div class="pb" style="padding:8px">${tree}</div></div>
      <div class="panel"><div class="ph"><h3>Thuộc tính của ${PK.cat(sel).name}</h3><button class="btn sm secondary" data-act="toastMsg" data-msg="Mở form thêm thuộc tính">${icon('plus', 14)} Thêm thuộc tính</button></div>
        <div class="table-wrap"><table class="tbl"><tr><th>Mã</th><th>Nhãn</th><th>Nhóm</th><th>Kiểu</th><th>Đơn vị</th><th>Bắt buộc</th><th>Dùng để lọc</th></tr>
        ${attrs.map((a) => `<tr><td><code>${a.key}</code></td><td>${a.label}</td><td>${a.group}</td><td class="muted">${a.type}</td><td class="muted">${a.unit || '-'}</td><td>${a.key === 'cpu' || a.key === 'chip' ? `<span class="perm-yes">${icon('check', 16)}</span>` : ''}</td><td>${a.filterable ? `<span class="perm-yes">${icon('check', 16)}</span>` : ''}</td></tr>`).join('') || '<tr><td colspan="7"><div class="empty" style="border:0"><b>Chưa có thuộc tính</b>Chọn Laptop, Điện thoại hoặc Tai nghe để xem mẫu.</div></td></tr>'}</table></div></div></div>
      ${note('Không xóa hoặc đổi kiểu thuộc tính đã có sản phẩm dùng.')}`;
  });
  adminRoute(
    '/admin/brands',
    'products:write',
    () => `${head('Thương hiệu', `${D.brands.length} thương hiệu`, `<button class="btn" data-act="toastMsg" data-msg="Mở form thêm thương hiệu">${icon('plus', 16)} Thêm thương hiệu</button>`)}
      <div class="table-wrap"><table class="tbl"><tr><th>Thương hiệu</th><th>Slug</th><th class="num">Sản phẩm</th></tr>${D.brands.map((b) => `<tr><td><div class="row tight"><span class="avatar light" style="width:32px;height:32px;border-radius:8px;font-size:12px">${b.slice(0, 2).toUpperCase()}</span><b>${b}</b></div></td><td class="muted">${PK.norm(b).replace(/\s+/g, '-')}</td><td class="num">${PK.S.products.filter((p) => p.brand === b).length}</td></tr>`).join('')}</table></div>`,
  );

  // ================= catalog: tồn kho =================
  function lowStock() {
    const rows = [];
    PK.S.products
      .filter((p) => p.status === 'active')
      .forEach((p) =>
        (p.variants || [{ sku: p.slug.toUpperCase(), stock: p.stock }]).forEach(
          (v) => v.stock <= 5 && rows.push({ p, v }),
        ),
      );
    return rows;
  }
  adminRoute('/admin/inventory', 'inventory:write', () => {
    const S = PK.S;
    const TYPE = {
      manual: 'Điều chỉnh',
      order_reserve: 'Giữ cho đơn',
      order_release: 'Trả từ đơn',
      return: 'Khách trả hàng',
    };
    return `${head('Tồn kho', '', `<button class="btn" data-act="stockAdjust">${icon('plus', 16)} Điều chỉnh tồn</button>`)}
      <div class="grid" style="grid-template-columns:1fr 1.4fr;align-items:start;gap:24px">
        <div class="panel"><div class="ph"><h3>Sắp hết hàng</h3><span class="small muted">Ngưỡng 5</span></div><div class="table-wrap"><table class="tbl"><tr><th>SKU</th><th>Sản phẩm</th><th class="num">Tồn</th></tr>${lowStock()
          .map(
            (x) =>
              `<tr><td><code>${x.v.sku}</code></td><td class="small">${esc(x.p.name)}</td><td class="num"><span class="badge ${x.v.stock ? 'warn' : 'sale'}">${x.v.stock}</span></td></tr>`,
          )
          .join('')}</table></div></div>
        <div class="panel"><div class="ph"><h3>Lịch sử xuất nhập</h3></div><div class="table-wrap"><table class="tbl compact small"><tr><th>Thời gian</th><th>SKU</th><th class="num">Thay đổi</th><th>Loại</th><th>Tham chiếu</th><th>Lý do</th></tr>${S.stockMoves.map((m) => `<tr><td class="muted">${dmy(m.at)}</td><td><code>${m.sku}</code></td><td class="num" style="color:${m.delta > 0 ? 'var(--success)' : 'var(--ink)'}">${m.delta > 0 ? '+' : ''}${m.delta}</td><td>${TYPE[m.type]}</td><td>${esc(m.ref)}</td><td class="muted">${esc(m.reason) || '-'}</td></tr>`).join('')}</table></div></div>
      </div>`;
  });
  act('stockAdjust', () => {
    const skus = PK.S.products.flatMap((p) =>
      (p.variants || [{ sku: p.slug.toUpperCase() }]).map((v) => v.sku),
    );
    modal(`<h2>Điều chỉnh tồn kho</h2><form data-form="stockAdjust" class="stack"><label class="field"><span>SKU</span><select name="sku">${skus.map((s) => `<option>${s}</option>`).join('')}</select></label>
      <div class="grid cols-2"><label class="field"><span>Số lượng (+ nhập, − xuất)</span><input name="delta" type="number" value="5"></label><label class="field"><span>Loại</span><select name="type"><option value="manual">Điều chỉnh</option><option value="return">Khách trả hàng</option></select></label></div>
      <label class="field"><span>Lý do</span><input name="reason" placeholder="Ví dụ: nhập lô hàng tháng 10"></label>
      <div class="row" style="justify-content:flex-end"><button type="button" class="btn secondary" data-act="closeModal">Hủy</button><button class="btn">Lưu</button></div></form>`);
  });
  form('stockAdjust', (data) => {
    if (!String(data.reason).trim()) return toast('Vui lòng nhập lý do điều chỉnh');
    const S = PK.S;
    S.products.forEach((p) => {
      if (p.variants)
        p.variants.forEach((v) => v.sku === data.sku && (v.stock += Number(data.delta)));
      else if (p.slug.toUpperCase() === data.sku) p.stock += Number(data.delta);
    });
    S.stockMoves.unshift({
      sku: data.sku,
      delta: Number(data.delta),
      type: data.type,
      ref: PK.me().name,
      reason: data.reason,
      at: PK.now(),
    });
    audit('inventory.adjust', 'sku:' + data.sku, (Number(data.delta) > 0 ? '+' : '') + data.delta);
    closeModal();
    PK.render();
  });

  // ================= commerce: kiểm duyệt đánh giá =================
  adminRoute('/admin/reviews', 'reviews:moderate', () => {
    const list = [...PK.S.reviews].sort((a, b) => b.reports - a.reports);
    return `${head('Kiểm duyệt đánh giá', 'Đánh giá bị báo cáo từ 3 lần được đưa lên đầu')}
      <div class="table-wrap"><table class="tbl"><tr><th>Đánh giá</th><th>Sản phẩm</th><th class="num">Báo cáo</th><th>Trạng thái</th><th></th></tr>
      ${list
        .map(
          (
            r,
          ) => `<tr><td style="max-width:420px"><div class="row tight"><b>${esc(r.user)}</b>${PK.stars(r.rating, 12)}</div><div class="small" style="margin-top:4px">${esc(r.body)}</div></td><td class="small">${esc(PK.prod(r.pid).name)}</td><td class="num">${r.reports >= 3 ? `<span class="badge sale">${r.reports}</span>` : r.reports}</td><td>${r.status === 'visible' ? '<span class="badge ok">Đang hiện</span>' : '<span class="badge gray">Đã ẩn</span>'}</td>
        <td><button class="btn sm secondary" data-act="reviewToggle" data-id="${r.id}">${r.status === 'visible' ? 'Ẩn' : 'Hiện lại'}</button></td></tr>`,
        )
        .join('')}</table></div>
      ${note('Ẩn hoặc hiện đều cần lý do; điểm trung bình sản phẩm được tính lại.')}`;
  });
  act('reviewToggle', (d) => {
    const r = PK.S.reviews.find((x) => x.id === d.id);
    modal(
      `<h2>${r.status === 'visible' ? 'Ẩn' : 'Hiện lại'} đánh giá</h2><form data-form="reviewToggle" data-id="${r.id}" class="stack"><label class="field"><span>Lý do</span><select name="reason"><option>Spam, chứa thông tin liên hệ</option><option>Ngôn từ không phù hợp</option><option>Không liên quan sản phẩm</option><option>Báo cáo sai, hiện lại</option></select></label><div class="row" style="justify-content:flex-end"><button type="button" class="btn secondary" data-act="closeModal">Hủy</button><button class="btn">Xác nhận</button></div></form>`,
    );
  });
  form('reviewToggle', (data, f) => {
    const r = PK.S.reviews.find((x) => x.id === f.dataset.id);
    r.status = r.status === 'visible' ? 'hidden' : 'visible';
    audit('review.' + (r.status === 'hidden' ? 'hide' : 'show'), 'review:' + r.id, data.reason);
    closeModal();
    PK.render();
  });

  // ================= support-desk: bàn làm việc CSKH 3 cột =================
  adminRoute('/admin/support', 'conversations:read', (_, q) => {
    const S = PK.S;
    const tab = q.tab || 'waiting';
    const meId = PK.me().id;
    const lists = {
      waiting: S.convs.filter((c) => c.status === 'waiting_human'),
      mine: S.convs.filter((c) => c.status === 'human' && c.assignedTo === meId),
    };
    const top = `${head('CSKH', '', `<a class="btn secondary" href="#/admin/support/canned">Câu trả lời mẫu</a><a class="btn secondary" href="#/admin/support/metrics">${icon('chart', 16)} Chỉ số</a>`)}
      <div class="tabs"><a href="#/admin/support" class="${tab === 'waiting' ? 'on' : ''}">Đang chờ <span class="n">${lists.waiting.length}</span></a><a href="#/admin/support?tab=mine" class="${tab === 'mine' ? 'on' : ''}">Của tôi <span class="n">${lists.mine.length}</span></a><a href="#/admin/support?tab=callbacks" class="${tab === 'callbacks' ? 'on' : ''}">Gọi lại <span class="n">${S.callbacks.filter((c) => c.status === 'pending').length}</span></a></div>`;
    if (tab === 'callbacks') {
      const ST = {
        pending: '<span class="badge warn">Chờ gọi</span>',
        done: '<span class="badge ok">Đã gọi</span>',
        unreachable: '<span class="badge gray">Không liên lạc được</span>',
      };
      return (
        top +
        `<div class="table-wrap"><table class="tbl"><tr><th>Số điện thoại</th><th>Ghi chú</th><th>Hội thoại</th><th>Thời gian</th><th>Trạng thái</th><th></th></tr>${S.callbacks.map((c) => `<tr><td><b>${c.phone}</b></td><td>${esc(c.note)}</td><td class="muted">${c.conv}</td><td class="muted">${dmy(c.createdAt)}</td><td>${ST[c.status]}</td><td>${c.status === 'pending' ? `<div class="row tight" style="flex-wrap:nowrap"><button class="btn sm" data-act="cbSet" data-id="${c.id}" data-s="done">Đã gọi</button><button class="btn sm ghost" data-act="cbSet" data-id="${c.id}" data-s="unreachable">Không liên lạc được</button></div>` : ''}</td></tr>`).join('')}</table></div>${note('SĐT tự xóa sau 30 ngày và không bao giờ gửi sang AI. Khách để lại tối đa 3 yêu cầu mỗi ngày.')}`
      );
    }
    const list = lists[tab];
    const cur = S.convs.find((c) => c.id === q.c) || list[0];
    const queue =
      list
        .map(
          (c) =>
            `<div class="qitem ${cur && cur.id === c.id ? 'on' : ''}" data-act="nav" data-href="#/admin/support?tab=${tab}&c=${c.id}" role="button" tabindex="0"><span class="avatar light" style="width:32px;height:32px;font-size:12px">${PK.initials(c.customer.replace(/\(.*\)/, '').replace('#', '')) || 'K'}</span><div class="body"><div class="t"><span>${esc(c.customer)}</span><span class="xs muted" style="font-weight:400">${c.status === 'waiting_human' ? c.waitingSince + ' phút' : ''}</span></div><div class="s">${esc(c.reason)}</div>${c.waitingSince >= S.supportSettings.nudge ? '<span class="badge sale" style="margin-top:6px">Chờ quá 10 phút</span>' : ''}</div></div>`,
        )
        .join('') ||
      `<div class="empty" style="border:0;margin:16px"><b>Không có hội thoại</b>${tab === 'waiting' ? 'Hàng chờ đang trống.' : ''}</div>`;
    let center = `<div class="empty" style="border:0;margin:auto">${icon('chat', 28)}<b style="margin-top:12px">Chọn một hội thoại</b></div>`;
    let right = '';
    if (cur) {
      const mine = cur.status === 'human' && cur.assignedTo === meId;
      const canReply = mine && PK.can('conversations:reply');
      const draftOk = !PK.S.aiDown && PK.S.budgetPct < 100;
      const WHO = { user: 'Khách', assistant: 'Trợ lý AI', staff: 'Nhân viên', system: '' };
      center = `<div class="desk-head"><div><b>${esc(cur.customer)}</b><div class="xs muted">${esc(cur.reason)}</div></div><div class="row tight">
          ${cur.status === 'waiting_human' && PK.can('conversations:reply') ? `<button class="btn sm" data-act="claim" data-id="${cur.id}">Nhận hội thoại</button>` : ''}
          ${mine ? `<button class="btn sm secondary" data-act="transfer" data-id="${cur.id}">Chuyển</button><button class="btn sm danger" data-act="endSession" data-id="${cur.id}">Kết thúc</button>` : ''}</div></div>
        <div class="chat-body">${cur.messages.map(([role, text]) => (role === 'system' ? `<div class="msg system">${esc(text)}</div>` : `<div class="msg ${role === 'user' ? 'staff' : role === 'staff' ? 'user' : 'assistant'}" style="${role === 'assistant' ? 'border:1px solid var(--line);border-radius:16px 16px 16px 4px;padding:9px 14px' : ''}${role === 'user' ? 'align-self:flex-start' : ''}"><div class="who" style="${role === 'staff' ? 'color:#c9c9d1' : ''}">${WHO[role]}</div>${esc(text)}</div>`)).join('')}</div>
        ${
          canReply
            ? `<form data-form="staffReply" data-id="${cur.id}" class="desk-compose stack">
            <textarea name="text" id="composer" placeholder="Nhập trả lời. Gõ / để chọn câu mẫu." data-input="slash" aria-label="Trả lời khách"></textarea>
            <div id="slash" class="hidden"></div>
            <div class="row between"><button type="button" class="btn sm secondary" data-act="draft" data-id="${cur.id}" ${draftOk ? '' : 'disabled'}>${icon('sparkle', 14)} ${draftOk ? 'Gợi ý trả lời' : 'Gợi ý tạm không khả dụng'}</button><button class="btn sm">${icon('send', 14)} Gửi</button></div>
            <div id="draft-src" class="xs muted"></div></form>`
            : `<div class="desk-compose small muted">${cur.status === 'waiting_human' ? 'Nhận hội thoại để trả lời khách.' : 'Hội thoại do đồng nghiệp phụ trách.'}</div>`
        }`;
      const cust = cur.userId ? S.users.find((u) => u.id === cur.userId) : null;
      const orders = cur.userId ? S.orders.filter((o) => o.userId === cur.userId) : [];
      right = `<div class="side-sec"><h4>Khách hàng</h4>${cust ? `<b>${esc(cust.name)}</b><div class="small muted">${esc(cust.email)}</div>` : '<div class="small">Khách vãng lai</div><div class="xs muted">Nhận diện theo phiên trình duyệt</div>'}</div>
        <div class="side-sec"><h4>Đơn gần đây</h4>${orders.map((o) => `<div class="row between small" style="padding:3px 0"><a href="#/admin/orders/${o.code}"><u>${o.code}</u></a>${PK.orderBadge(o.status)}</div>`).join('') || '<div class="small muted">Chưa có đơn</div>'}</div>
        <div class="side-sec"><h4>Trợ lý đã gợi ý</h4>${(cur.suggested || []).map((id) => `<div class="small">${esc(PK.prod(id).name)}</div>`).join('') || '<div class="small muted">Không có</div>'}</div>
        <div class="side-sec" style="border:0"><h4>Đang xem trang</h4><div class="small"><code>${esc(cur.page)}</code></div></div>`;
    }
    return (
      top +
      `${S.presence === 'away' ? `<div class="alert warn" style="margin-bottom:16px">${icon('info', 18)}<span>Bạn đang Tạm vắng nên không nhận hội thoại mới.</span></div>` : ''}
      <div class="desk"><div class="col" style="overflow-y:auto">${queue}</div><div class="col">${center}</div><div class="col" style="overflow-y:auto">${right}</div></div>
      ${note('Thử luồng: ở cửa hàng mở chat, bấm "Gặp nhân viên" (hội thoại "(bạn)" xuất hiện ở đây), vào vai CSKH, Nhận, Gợi ý trả lời, Gửi, quay lại vai khách để thấy tin, rồi Kết thúc để khách chấm điểm.')}`
    );
  });
  act('claim', (d) => {
    const c = PK.S.convs.find((x) => x.id === d.id);
    if (c.assignedTo) return toast('Hội thoại đã có người nhận');
    c.status = 'human';
    c.assignedTo = PK.me().id;
    c.messages.push(['system', PK.me().name.replace(' CSKH', '') + ' đã nhận hội thoại']);
    if (c.id === 'c-live') {
      PK.S.chat.status = 'human';
      PK.S.chat.messages.push({
        role: 'system',
        text: 'Lan từ CSKH Pickory đã tham gia hội thoại',
      });
    }
    audit('conversation.claim', 'conversation:' + c.id, '');
    PK.go('/admin/support?tab=mine&c=' + c.id);
  });
  act('draft', () => {
    const el = document.getElementById('composer');
    el.value =
      'Chào bạn, Pickory hỗ trợ đổi trả trong 7 ngày nếu sản phẩm lỗi do nhà sản xuất. Bạn cho em xin mã đơn để kiểm tra giúp nhé.';
    document.getElementById('draft-src').innerHTML =
      `Nguồn: Chính sách đổi trả · Thời hạn đổi trả, Quy trình xử lý khiếu nại (nội bộ). Bản nháp không tự gửi, hãy kiểm tra trước khi gửi.`;
    el.focus();
  });
  input('slash', (el) => {
    const box = document.getElementById('slash');
    if (!el.value.startsWith('/')) return box.classList.add('hidden');
    box.className = 'card tight small';
    box.innerHTML =
      PK.S.canned
        .filter((c) => c.shortcut.startsWith(el.value.split(' ')[0]))
        .map(
          (c) =>
            `<div style="padding:6px;cursor:pointer;border-radius:6px" data-act="useCanned" data-body="${esc(c.body)}"><code>${c.shortcut}</code> ${esc(c.body)}</div>`,
        )
        .join('') || '<span class="muted">Không có câu mẫu khớp</span>';
  });
  act('useCanned', (d) => {
    const el = document.getElementById('composer');
    el.value = d.body.replace('{ten_khach}', 'bạn').replace('{ma_don}', 'PK7KX2M9QD');
    document.getElementById('slash').classList.add('hidden');
    el.focus();
  });
  form('staffReply', (data, f) => {
    const text = String(data.text || '').trim();
    if (!text) return;
    const c = PK.S.convs.find((x) => x.id === f.dataset.id);
    c.messages.push(['staff', text]);
    if (c.id === 'c-live')
      PK.S.chat.messages.push({
        role: 'staff',
        text,
        staffName: PK.me().name.replace(' CSKH', '') + ' · CSKH',
      });
    PK.render();
  });
  act('transfer', (d) => {
    const others = PK.S.users.filter(
      (u) =>
        u.kind === 'staff' && u.id !== PK.me().id && u.roles.includes('support') && !u.lockedAt,
    );
    modal(`<h2>Chuyển hội thoại</h2><form data-form="transfer" data-id="${d.id}" class="stack"><label class="field"><span>Đồng nghiệp đang sẵn sàng</span><select name="to">${others.map((u) => `<option value="${u.id}">${esc(u.name)}</option>`).join('')}</select></label>
      <label class="field"><span>Ghi chú nội bộ</span><textarea name="note" placeholder="Ví dụ: khách hỏi hoàn tiền, đã kiểm tra giao dịch"></textarea></label><div class="row" style="justify-content:flex-end"><button type="button" class="btn secondary" data-act="closeModal">Hủy</button><button class="btn">Chuyển</button></div></form>`);
  });
  form('transfer', (data, f) => {
    const c = PK.S.convs.find((x) => x.id === f.dataset.id);
    c.assignedTo = data.to;
    c.messages.push(['system', 'Đã chuyển cho ' + PK.S.users.find((u) => u.id === data.to).name]);
    audit('conversation.transfer', 'conversation:' + c.id, '→ ' + data.to);
    closeModal();
    PK.go('/admin/support');
  });
  act('endSession', (d) => {
    const c = PK.S.convs.find((x) => x.id === d.id);
    c.status = 'closed';
    if (c.id === 'c-live') {
      PK.S.chat.status = 'bot';
      PK.S.chat.messages.push({
        role: 'system',
        text: 'Phiên hỗ trợ đã kết thúc. Bạn đánh giá trải nghiệm giúp Pickory nhé',
        form: 'csat',
      });
    }
    audit('conversation.close', 'conversation:' + c.id, '');
    toast('Đã kết thúc phiên hỗ trợ');
    PK.go('/admin/support');
  });
  act('cbSet', (d) => {
    PK.S.callbacks.find((c) => c.id === d.id).status = d.s;
    PK.render();
  });
  adminRoute(
    '/admin/support/canned',
    'conversations:read',
    () => `${crumbs([['CSKH', '#/admin/support'], ['Câu trả lời mẫu']])}${head('Câu trả lời mẫu', 'Gõ / trong ô trả lời để chèn', `<button class="btn" data-act="toastMsg" data-msg="Mở form thêm câu mẫu">${icon('plus', 16)} Thêm câu mẫu</button>`)}
      <div class="table-wrap"><table class="tbl"><tr><th>Phím tắt</th><th>Nội dung</th><th>Phạm vi</th></tr>${PK.S.canned.map((c) => `<tr><td><code>${c.shortcut}</code></td><td>${esc(c.body)}</td><td><span class="badge">${c.scope}</span></td></tr>`).join('')}</table></div><p class="small muted" style="margin-top:12px">Biến hỗ trợ: <code>{ten_khach}</code>, <code>{ma_don}</code></p>`,
  );
  adminRoute(
    '/admin/support/metrics',
    'conversations:read',
    () => `${crumbs([['CSKH', '#/admin/support'], ['Chỉ số']])}${head('Chỉ số CSKH', '30 ngày gần nhất')}
      <div class="grid cols-4">${[
        ['Chuyển cho nhân viên', '128', '+12%'],
        ['Xử lý trong giờ', '83%'],
        ['Phản hồi đầu (p50 / p90)', '48 giây / 3 phút'],
        ['Thời gian xử lý (p50 / p90)', '6 / 19 phút'],
        ['CSAT', '4,6 / 5', '92 lượt'],
        ['Gọi lại hoàn thành', '27 / 31'],
        ['Dùng bản nháp AI', '61%', '+8 điểm'],
      ]
        .map(([l, v, d]) => kpi(l, v, d))
        .join('')}</div>`,
  );

  // ================= shopping-assistant: xem hội thoại AI (phía nhân viên) =================
  const AI_CONVS = [
    {
      id: 'ai-901',
      who: 'Khách vãng lai',
      at: '2026-10-03 09:41',
      promptVersion: 'assistant@7',
      flags: [],
      turns: [
        { role: 'user', text: 'laptop nào chơi game tốt tầm 25 triệu, nhẹ để mang đi học' },
        {
          role: 'assistant',
          text: 'Trong tầm 25 triệu, ưu tiên máy nhẹ, mình gợi ý 3 mẫu: Acer Nitro V 15, ASUS TUF A15, MSI Katana 15.',
          tools: [
            [
              'search_products',
              '{ category: "laptop-gaming", maxPrice: 26250000, sort: "weight" }',
              '3 sản phẩm',
            ],
          ],
          cards: ['p3', 'p4', 'p2'],
          fb: 'up',
        },
        { role: 'user', text: 'so sánh 2 con đầu' },
        {
          role: 'assistant',
          text: 'Hai máy cùng hiệu năng tương đương, Acer Nitro V 15 nhẹ hơn.',
          tools: [['compare_products', '{ ids: ["p3","p4"] }', 'bảng 6 thuộc tính']],
          fb: null,
        },
      ],
    },
    {
      id: 'ai-902',
      who: 'Trần Quốc Bảo',
      at: '2026-10-02 20:15',
      promptVersion: 'assistant@7',
      flags: ['price_mismatch'],
      turns: [
        { role: 'user', text: 'MSI Katana 15 giá bao nhiêu?' },
        {
          role: 'assistant',
          text: 'MSI Katana 15 đang có giá khoảng 23,9 triệu.',
          tools: [['get_product_details', '{ id: "p2" }', 'giá 24.490.000 đ']],
          cards: ['p2'],
          fb: 'down',
          fbReason: 'Sai thông tin',
          flag: 'price_mismatch',
        },
      ],
    },
    {
      id: 'ai-903',
      who: 'Lê Thu Hà',
      at: '2026-10-02 14:02',
      promptVersion: 'assistant@7',
      flags: [],
      turns: [
        { role: 'user', text: 'đổi trả trong bao lâu' },
        {
          role: 'assistant',
          text: 'Bạn được đổi trả trong 7 ngày nếu sản phẩm lỗi do nhà sản xuất [1].',
          tools: [
            ['search_knowledge', '{ query: "đổi trả bao lâu" }', '2 đoạn, điểm cao nhất 0,82'],
          ],
          cites: ['Chính sách đổi trả · Thời hạn đổi trả (v3)'],
          fb: 'up',
        },
      ],
    },
  ];
  const FLAG_TXT = {
    price_mismatch: 'Giá lệch với dữ liệu',
    unknown_product: 'Sản phẩm không tồn tại',
    missing_citation: 'Thiếu trích dẫn',
  };
  adminRoute('/admin/conversations', 'conversations:read', (_, q) => {
    const S = PK.S;
    // Hội thoại thật trong phiên prototype (chat ở cửa hàng) được đưa lên đầu
    const live = S.chat.messages.length
      ? [
          {
            id: 'live',
            who: S.role === 'customer' ? 'Nguyễn Minh Anh' : 'Phiên chat của bạn',
            at: PK.now(),
            promptVersion: 'assistant@7',
            flags: [],
            turns: S.chat.messages
              .filter((m) => m.role === 'user' || m.role === 'assistant')
              .map((m) => ({
                role: m.role,
                text: m.text,
                tools: (m.tools || []).map((t) => [t, '', '']),
                cards: m.cards,
                cites: (m.cites || []).map((c) => c.title),
                fb: m.fb,
                fbReason: m.fbReason,
              })),
          },
        ]
      : [];
    const all = [...live, ...AI_CONVS];
    const cur = all.find((c) => c.id === q.c) || all[0];
    const list = all
      .map(
        (c) =>
          `<div class="qitem ${cur.id === c.id ? 'on' : ''}" data-act="nav" data-href="#/admin/conversations?c=${c.id}" role="button" tabindex="0"><div class="body"><div class="t"><span>${esc(c.who)}</span><span class="xs muted" style="font-weight:400">${dmy(c.at).slice(0, 5)}</span></div><div class="s">${esc(c.turns[0]?.text || '')}</div>${c.flags.length ? `<span class="badge sale" style="margin-top:6px">${FLAG_TXT[c.flags[0]]}</span>` : ''}</div></div>`,
      )
      .join('');
    const turns = cur.turns
      .map((t) =>
        t.role === 'user'
          ? `<div class="msg staff" style="align-self:flex-start"><div class="who">Khách</div>${esc(t.text)}</div>`
          : `<div style="border:1px solid var(--line);border-radius:12px;padding:12px 14px">
              <div class="row between"><span class="ai-label">${icon('sparkle', 14)} Trợ lý AI</span><span class="row tight">${t.flag ? `<span class="badge sale">${FLAG_TXT[t.flag]}</span>` : ''}${t.fb === 'up' ? `<span class="badge ok">Khách thích</span>` : t.fb === 'down' ? `<span class="badge warn">Không thích${t.fbReason ? ': ' + esc(t.fbReason) : ''}</span>` : ''}</span></div>
              <p style="margin:8px 0">${esc(t.text)}</p>
              ${(t.tools || []).length ? `<div class="table-wrap"><table class="tbl compact small"><tr><th>Tool</th><th>Tham số</th><th>Kết quả</th></tr>${t.tools.map(([n, a, r]) => `<tr><td><code>${n}</code></td><td class="muted"><code>${esc(a) || '-'}</code></td><td>${esc(r) || '-'}</td></tr>`).join('')}</table></div>` : ''}
              ${(t.cards || []).length ? `<div class="xs muted" style="margin-top:8px">Thẻ sản phẩm: ${t.cards.map((id) => esc(PK.prod(id).name)).join(', ')}</div>` : ''}
              ${(t.cites || []).length ? `<div class="xs muted" style="margin-top:4px">Trích dẫn: ${t.cites.map(esc).join(', ')}</div>` : ''}
            </div>`,
      )
      .join('');
    return `${head('Hội thoại AI', 'Xem lại trợ lý đã trả lời gì, gọi tool nào và khách phản hồi ra sao')}
      <div class="grid cols-4" style="margin-bottom:20px">${kpi('Hội thoại 30 ngày', '1.230')}${kpi('Tỉ lệ khách thích', '84%')}${kpi('Chuyển cho CSKH', '10%')}${kpi('Cờ grounding', '1,8%', 'giảm 0,4 điểm')}</div>
      <div class="desk" style="grid-template-columns:300px 1fr"><div class="col" style="overflow-y:auto">${list}</div>
        <div class="col"><div class="desk-head"><div><b>${esc(cur.who)}</b><div class="xs muted">${dmy(cur.at)} · prompt <code>${cur.promptVersion}</code></div></div></div><div class="chat-body">${turns}</div></div></div>
      ${note('Phiên chat bạn vừa thử ở cửa hàng hiện ở đầu danh sách. Hội thoại lưu 90 ngày. Cờ grounding: giá lệch, sản phẩm không tồn tại, thiếu trích dẫn.')}`;
  });

  // ================= knowledge-base =================
  const KB_ST = {
    draft: '<span class="badge gray">Nháp</span>',
    published: '<span class="badge ok">Đã đăng</span>',
    archived: '<span class="badge gray">Lưu trữ</span>',
  };
  const KB_TYPE = { policy: 'Chính sách', faq: 'Hỏi đáp', guide: 'Hướng dẫn', internal: 'Nội bộ' };
  adminRoute('/admin/knowledge', 'knowledge:read', () => {
    const w = PK.can('knowledge:write');
    return `${head('Kho tri thức', 'Nguồn trả lời cho trợ lý AI và trung tâm hỗ trợ', `<a class="btn secondary" href="#/admin/knowledge-test">${icon('search', 16)} Thử truy xuất</a>${w ? `<button class="btn secondary" data-act="kbImport">Nhập DOCX/PDF</button><a class="btn" href="#/admin/knowledge/new">${icon('plus', 16)} Viết tài liệu</a>` : ''}`)}
      ${PK.S.aiDown ? `<div class="alert err" style="margin-bottom:16px">${icon('alert', 18)}<span>Lập chỉ mục phiên bản mới của "Chính sách đổi trả" thất bại. Phiên bản cũ vẫn đang được dùng. <button class="link small" data-act="toastMsg" data-msg="Đã xếp hàng lập chỉ mục lại">Thử lại</button></span></div>` : ''}
      <div class="table-wrap"><table class="tbl"><tr><th>Tài liệu</th><th>Loại</th><th>Trạng thái</th><th>Hiệu lực từ</th><th>Cập nhật</th></tr>
      ${PK.S.kbDocs.map((d) => `<tr class="click" data-act="nav" data-href="#/admin/knowledge/${d.slug}"><td><b>${esc(d.title)}</b><div class="xs muted">/${d.slug}</div></td><td>${d.type === 'internal' ? `<span class="badge dark">${icon('lock', 12)} Nội bộ</span>` : `<span class="badge">${KB_TYPE[d.type]}</span>`}</td><td>${KB_ST[d.status]}</td><td class="muted">${dmy(d.effectiveFrom)}</td><td class="muted">${dmy(d.updatedAt)}</td></tr>`).join('')}</table></div>
      ${note('Tài liệu nội bộ chỉ dùng cho bản nháp của CSKH, trợ lý của khách không thấy. Đăng bản mới thì trợ lý dùng trong 1 phút, không có lúc trống nội dung.')}`;
  });
  adminRoute('/admin/knowledge/:slug', 'knowledge:read', (p) => {
    const d = PK.S.kbDocs.find((x) => x.slug === p.slug) || {
      slug: '',
      title: '',
      type: 'faq',
      status: 'draft',
      tags: [],
      effectiveFrom: '2026-10-03',
      updatedAt: '2026-10-03',
      body: [['Tiêu đề mục', 'Nội dung']],
    };
    const w = PK.can('knowledge:write');
    const md = '# ' + d.title + '\n\n' + d.body.map(([h, t]) => `## ${h}\n${t}`).join('\n\n');
    return `${crumbs([['Kho tri thức', '#/admin/knowledge'], [esc(d.title || 'Tài liệu mới')]])}
      ${head(`${esc(d.title || 'Tài liệu mới')} ${KB_ST[d.status]}`, '', w ? `<button class="btn ghost" data-act="kbSet" data-slug="${d.slug}" data-s="archived">Lưu trữ</button><button class="btn secondary" data-act="kbSet" data-slug="${d.slug}" data-s="draft">${d.status === 'published' ? 'Gỡ đăng' : 'Lưu nháp'}</button><button class="btn" data-act="kbSet" data-slug="${d.slug}" data-s="published">Đăng</button>` : '<span class="badge gray">Chỉ xem</span>')}
      <div class="grid cols-4" style="margin-bottom:16px"><label class="field"><span>Đường dẫn</span><input value="${d.slug}" ${w ? '' : 'disabled'}></label><label class="field"><span>Loại</span><select ${w ? '' : 'disabled'}>${Object.entries(
        KB_TYPE,
      )
        .map(([t, l]) => `<option ${t === d.type ? 'selected' : ''}>${l}</option>`)
        .join(
          '',
        )}</select></label><label class="field"><span>Thẻ</span><input value="${d.tags.join(', ')}" ${w ? '' : 'disabled'}></label><label class="field"><span>Hiệu lực từ</span><input type="date" value="${d.effectiveFrom}" ${w ? '' : 'disabled'}></label></div>
      <div class="grid cols-2"><label class="field"><span>Markdown</span><textarea style="min-height:360px;font-family:ui-monospace,Menlo,Consolas,monospace;font-size:13px" ${w ? '' : 'disabled'} data-input="mdPreview">${esc(md)}</textarea></label><div><span class="small" style="display:block;font-weight:500;margin-bottom:6px;color:var(--ink-2)">Xem trước</span><div class="md-preview" id="md-preview">${renderMd(md)}</div></div></div>
      <div class="panel" style="margin-top:24px"><div class="ph"><h3>Lịch sử phiên bản</h3></div><div class="table-wrap"><table class="tbl"><tr><th>Phiên bản</th><th>Người đăng</th><th>Thời gian</th></tr><tr><td>v3 <span class="badge ok">Đang dùng</span></td><td>Hoàng Nam</td><td class="muted">${dmy(d.updatedAt)} 10:00</td></tr><tr><td>v2</td><td>Hoàng Nam</td><td class="muted">01/07/2026 09:00</td></tr></table></div></div>
      ${note('Ngày hiệu lực ở tương lai thì chưa được truy xuất cho tới ngày đó.')}`;
  });
  function renderMd(md) {
    return esc(md)
      .split('\n\n')
      .map((blk) =>
        blk
          .replace(/^## (.*)$/gm, '<h3>$1</h3>')
          .replace(/^# (.*)$/gm, '<h2>$1</h2>')
          .replace(/^(?!<h)(.+)$/gm, '<p>$1</p>'),
      )
      .join('');
  }
  input('mdPreview', (el) => {
    document.getElementById('md-preview').innerHTML = renderMd(el.value);
  });
  act('kbSet', (d) => {
    const doc = PK.S.kbDocs.find((x) => x.slug === d.slug);
    if (!doc) {
      toast('Đã lưu tài liệu mới ở dạng nháp');
      return PK.go('/admin/knowledge');
    }
    doc.status = d.s;
    doc.updatedAt = PK.now().slice(0, 10);
    audit('knowledge.' + d.s, 'doc:' + doc.slug, '');
    toast(
      d.s === 'published'
        ? 'Đã đăng. Trợ lý dùng phiên bản mới trong 1 phút.'
        : 'Đã cập nhật tài liệu',
    );
    PK.render();
  });
  act('kbImport', () => {
    modal(`<h2>Nhập tài liệu</h2><p class="muted">DOCX hoặc PDF có lớp chữ, tối đa 10 MB. Kết quả luôn là bản nháp để bạn kiểm tra.</p>
      <div class="empty" style="padding:32px">${icon('book', 28)}<b style="margin-top:12px">Kéo thả file vào đây</b>hoặc bấm để chọn file</div>
      <div class="proto-note" style="display:block"><b>Giả lập:</b><div class="row tight" style="margin-top:8px"><button class="btn sm" data-act="kbImportOk">Nhập "Chinh-sach-tra-gop.docx"</button><button class="btn sm secondary" data-act="kbImportScan">Nhập PDF scan</button></div></div>`);
  });
  act('kbImportOk', () => {
    PK.S.kbDocs.unshift({
      slug: 'chinh-sach-tra-gop',
      type: 'policy',
      title: 'Chính sách trả góp',
      status: 'draft',
      tags: ['trả góp'],
      updatedAt: PK.now().slice(0, 10),
      effectiveFrom: '2026-11-01',
      body: [
        ['Điều kiện', 'Áp dụng cho đơn từ 3.000.000 đ qua thẻ tín dụng.'],
        ['Kỳ hạn', '3, 6 hoặc 12 tháng.'],
      ],
    });
    closeModal();
    PK.go('/admin/knowledge/chinh-sach-tra-gop');
  });
  act('kbImportScan', () =>
    toast('File PDF không có lớp chữ (bản scan). Hãy dùng file gốc vì hệ thống không hỗ trợ OCR.'),
  );
  adminRoute('/admin/knowledge-test', 'knowledge:read', (_, q) => {
    const question = q.q || 'đổi trả trong bao lâu';
    const hits = [
      [
        'Chính sách đổi trả › Thời hạn đổi trả',
        'Khách được đổi trả trong 7 ngày kể từ ngày nhận hàng nếu sản phẩm lỗi do nhà sản xuất...',
        0.82,
      ],
      [
        'Chính sách đổi trả › Điều kiện đổi trả',
        'Sản phẩm còn đủ phụ kiện, hộp và hóa đơn...',
        0.71,
      ],
      [
        'Quy trình xử lý khiếu nại › Bước 2 (nội bộ)',
        'Đề xuất đổi mới nếu lỗi trong 7 ngày...',
        0.58,
      ],
      [
        'Chính sách bảo hành › Thời gian bảo hành',
        'Theo thời gian của hãng ghi trên trang sản phẩm...',
        0.31,
      ],
    ];
    return `${crumbs([['Kho tri thức', '#/admin/knowledge'], ['Thử truy xuất']])}${head('Thử truy xuất', 'Xem trợ lý sẽ tìm được đoạn nào cho một câu hỏi')}
      <form data-form="kbTest" class="filters-bar"><input name="q" value="${esc(question)}" style="min-width:420px" aria-label="Câu hỏi"><button class="btn sm">Truy xuất</button></form>
      <div class="table-wrap"><table class="tbl"><tr><th>Mục</th><th>Đoạn văn</th><th class="num">Độ khớp</th></tr>${hits.map(([h, t, s]) => `<tr style="${s < 0.35 ? 'opacity:.5' : ''}"><td><b>${h}</b></td><td class="small">${t}</td><td class="num">${s.toLocaleString('vi-VN')}${s < 0.35 ? '<div class="xs muted">dưới ngưỡng 0,35</div>' : ''}</td></tr>`).join('')}</table></div>
      ${note('Đoạn dưới 0,35 bị loại. Không còn đoạn nào thì trợ lý trả lời "không có thông tin". Đoạn nội bộ chỉ dùng cho bản nháp CSKH.')}`;
  });
  form('kbTest', (data) => PK.go('/admin/knowledge-test?q=' + encodeURIComponent(data.q)));

  // ================= personalized-content: nội dung AI =================
  const AC_ST = {
    active: '<span class="badge ok">Đang hiện</span>',
    pending_approval: '<span class="badge warn">Chờ duyệt</span>',
    hidden: '<span class="badge gray">Đã ẩn</span>',
    failed: '<span class="badge sale">Lỗi kiểm tra</span>',
    expired: '<span class="badge gray">Hết hạn</span>',
  };
  const AC_KIND = {
    banner: 'Banner',
    review_summary: 'Tóm tắt đánh giá',
    highlights: 'Điểm nổi bật',
  };
  adminRoute('/admin/ai-content', 'products:read', (_, q) => {
    const list = PK.S.aiContents.filter(
      (c) => (!q.kind || c.kind === q.kind) && (!q.status || c.status === q.status),
    );
    const w = PK.can('products:write');
    return `${head('Nội dung AI', 'Banner, tóm tắt đánh giá và điểm nổi bật do AI tạo', `<a class="btn secondary" href="#/admin/ai-content/static">Banner tĩnh</a>`)}
      ${PK.S.budgetPct >= 100 ? `<div class="alert warn" style="margin-bottom:16px">${icon('alert', 18)}<span>Ngân sách AI đã chạm 100%: không tạo nội dung mới, nội dung đang có vẫn hiển thị.</span></div>` : ''}
      <div class="row tight" style="margin-bottom:16px">${[
        ['', 'Tất cả'],
        ['banner', 'Banner'],
        ['review_summary', 'Tóm tắt đánh giá'],
        ['highlights', 'Điểm nổi bật'],
      ]
        .map(
          ([k, l]) =>
            `<a class="chip ${(q.kind || '') === k ? 'on' : ''}" href="#/admin/ai-content${k ? '?kind=' + k : ''}">${l}</a>`,
        )
        .join('')}</div>
      <div class="table-wrap"><table class="tbl"><tr><th>Nội dung</th><th>Loại</th><th>Đối tượng</th><th>Persona</th><th>Trạng thái</th><th></th></tr>
      ${list
        .map(
          (
            c,
          ) => `<tr><td style="max-width:320px">${c.title ? `<b>${esc(c.title)}</b><div class="small muted">${esc(c.subtitle) || '-'}</div>` : `<span class="muted">Tạo ${dmy(c.generatedAt)}</span>`}${c.failReason ? `<div class="small" style="color:var(--danger);margin-top:4px">${esc(c.failReason)}</div>` : ''}<div class="xs subtle" style="margin-top:4px">${c.promptVersion}</div></td><td>${AC_KIND[c.kind]}</td><td class="small">${esc(c.target)}</td><td><span class="badge">${c.persona}</span></td><td>${AC_ST[c.status]}</td>
        <td>${w ? `<div class="row tight" style="flex-wrap:nowrap">${c.status === 'pending_approval' ? `<button class="btn sm" data-act="acSet" data-id="${c.id}" data-s="active">Duyệt</button>` : ''}${c.status === 'active' ? `<button class="btn sm secondary" data-act="acSet" data-id="${c.id}" data-s="hidden">Ẩn</button>` : ''}<button class="btn sm ghost" data-act="acRegen" data-id="${c.id}">Tạo lại</button></div>` : ''}</td></tr>`,
        )
        .join('')}</table></div>
      ${note('Banner do job hằng tuần tạo, phải được duyệt mới hiện (chờ quá 7 ngày thì hết hạn). Duyệt "Góc chiến game" rồi bật cá nhân hóa ở cửa hàng để thấy banner "Dành riêng cho bạn". Kiểm tra chống bịa: mọi con số phải có trong thông số, cấm "tốt nhất", "số 1", "rẻ nhất"; AI không viết giá.')}`;
  });
  act('acSet', (d) => {
    const c = PK.S.aiContents.find((x) => x.id === d.id);
    audit('ai_content.' + d.s, 'content:' + c.id, `${c.status} → ${d.s}`);
    c.status = d.s;
    toast(d.s === 'active' ? 'Đã duyệt nội dung' : 'Đã ẩn nội dung');
    PK.render();
  });
  act('acRegen', (d) => {
    if (PK.S.budgetPct >= 100 || PK.S.aiDown)
      return toast('Chưa tạo lại được: ngân sách AI đã hết hoặc AI đang gặp sự cố');
    const c = PK.S.aiContents.find((x) => x.id === d.id);
    c.generatedAt = PK.now().slice(0, 10);
    c.status = c.kind === 'banner' ? 'pending_approval' : 'active';
    c.failReason = '';
    toast('Đã xếp hàng tạo lại nội dung');
    PK.render();
  });
  adminRoute(
    '/admin/ai-content/static',
    'products:read',
    () => `${crumbs([['Nội dung AI', '#/admin/ai-content'], ['Banner tĩnh']])}${head('Banner tĩnh', 'Dùng khi không có banner AI phù hợp', `<button class="btn" data-act="toastMsg" data-msg="Mở form thêm banner tĩnh">${icon('plus', 16)} Thêm banner</button>`)}
      <div class="table-wrap"><table class="tbl"><tr><th>Tiêu đề</th><th>Liên kết</th><th class="num">Vị trí</th><th>Hiển thị</th></tr><tr><td><b>RTX 4060, giảm đến 4 triệu</b></td><td class="muted">/c/laptop-gaming</td><td class="num">1</td><td class="muted">01/10 đến 31/10/2026</td></tr><tr><td><b>Yên tĩnh trên mọi chuyến bay</b></td><td class="muted">/search?q=tai nghe chống ồn</td><td class="num">2</td><td class="muted">01/10 đến 15/11/2026</td></tr></table></div>`,
  );

  // ================= recommendations =================
  adminRoute('/admin/recommendations', 'reports:read', () => {
    const S = PK.S;
    const e = S.experiment;
    const canEdit = PK.can('ai-settings:manage');
    const rows = [
      ['Dành cho bạn', 'control', '2,1%', '0,8%'],
      ['Dành cho bạn', 'algo', '3,4%', '1,2%'],
      ['Dành cho bạn', 'algo_llm', '4,0%', '1,4%'],
      ['Sản phẩm tương tự', 'algo', '5,2%', '1,9%'],
      ['Giỏ hàng', 'algo', '3,1%', '2,3%'],
    ];
    return `${head('Gợi ý sản phẩm', 'Thí nghiệm A/B, cặp danh mục mua kèm và hiệu quả')}
      <div class="grid cols-2" style="align-items:start;gap:24px">
        <div class="panel"><div class="ph"><h3>Chia nhóm thí nghiệm</h3></div><div class="pb">
          <form data-form="experiment" class="stack">${[
            ['control', 'Đối chứng (Bán chạy)'],
            ['algo', 'Thuật toán'],
            ['llm', 'Thuật toán và AI xếp hạng'],
          ]
            .map(
              ([k, l]) =>
                `<label class="field"><span class="row between">${l}<b>${e[k]}%</b></span><input type="range" min="0" max="100" name="${k}" value="${e[k]}" ${canEdit ? '' : 'disabled'}></label>`,
            )
            .join('')}
          ${canEdit ? '<div><button class="btn sm">Lưu chia nhóm</button></div>' : '<p class="small muted">Cần quyền ai-settings:manage để chỉnh.</p>'}</form>
          <p class="xs muted" style="margin:12px 0 0">Đặt đối chứng 0% để tắt thí nghiệm. Tổng ba nhóm phải bằng 100%.</p></div></div>
        <div class="panel"><div class="ph"><h3>Cặp danh mục mua kèm</h3></div><div class="table-wrap"><table class="tbl"><tr><th>Danh mục</th><th>Mua kèm</th><th class="num">Trọng số</th><th>Mua lại</th></tr>${S.complements.map((c) => `<tr><td>${c.from}</td><td>${c.to}</td><td class="num">${c.weight.toLocaleString('vi-VN')}</td><td>${c.repurchasable ? `<span class="perm-yes">${icon('check', 16)}</span>` : ''}</td></tr>`).join('')}</table></div></div>
      </div>
      <div class="panel" style="margin-top:24px"><div class="ph"><h3>Hiệu quả 30 ngày</h3></div><div class="table-wrap"><table class="tbl"><tr><th>Vị trí</th><th>Nhóm</th><th class="num">CTR</th><th class="num">Chuyển đổi</th></tr>${rows.map((r) => `<tr><td>${r[0]}</td><td><code>${r[1]}</code></td><td class="num">${r[2]}</td><td class="num">${r[3]}</td></tr>`).join('')}</table></div>
        <div class="pb" style="padding-top:0"><p class="xs muted" style="margin:0">Giai đoạn 17/09 đến 18/09 bị loại khỏi so sánh do ngân sách hết (nhóm algo_llm chạy như algo).</p></div></div>`;
  });
  form('experiment', (data) => {
    const v = { control: Number(data.control), algo: Number(data.algo), llm: Number(data.llm) };
    if (v.control + v.algo + v.llm !== 100)
      return toast('Tổng ba nhóm phải bằng 100% (hiện tại ' + (v.control + v.algo + v.llm) + '%)');
    PK.S.experiment = v;
    audit('recommendations.experiment', 'settings', JSON.stringify(v));
    toast('Đã lưu chia nhóm');
    PK.render();
  });

  // ================= smart-search: thống kê tìm kiếm =================
  adminRoute('/admin/search', 'reports:read', () => {
    const si = D.searchInsights;
    return `${head('Tìm kiếm', '30 ngày gần nhất', PK.can('products:write') ? `<button class="btn secondary" data-act="toastMsg" data-msg="Đã xếp hàng lập chỉ mục lại 27 sản phẩm">${icon('refresh', 16)} Lập chỉ mục lại</button>` : '')}
      <div class="grid cols-4" style="margin-bottom:24px">${kpi('Lượt tìm', '8.412', '+6%')}${kpi('Không có kết quả', '3,1%', 'giảm 0,5 điểm')}${kpi('CTR từ kết quả', '11,8%')}${kpi('Câu cần AI hiểu', '22%')}</div>
      <div class="grid cols-2" style="gap:24px;align-items:start"><div class="panel"><div class="ph"><h3>Tìm nhiều nhất</h3></div><div class="table-wrap"><table class="tbl"><tr><th>Câu tìm</th><th class="num">Lượt</th><th class="num">CTR</th></tr>${si.top.map((r) => `<tr><td>${r[0]}</td><td class="num">${r[1]}</td><td class="num">${r[2]}</td></tr>`).join('')}</table></div></div>
      <div class="panel"><div class="ph"><h3>Không có kết quả</h3></div><div class="table-wrap"><table class="tbl"><tr><th>Câu tìm</th><th class="num">Lượt</th></tr>${si.zero.map((r) => `<tr><td>${r[0]}</td><td class="num">${r[1]}</td></tr>`).join('')}</table></div><div class="pb" style="padding-top:0"><p class="xs muted" style="margin:0">Gợi ý nhập thêm hàng hoặc bổ sung từ đồng nghĩa.</p></div></div></div>`;
  });

  // ================= admin-console: báo cáo =================
  adminRoute('/admin/reports', 'reports:read', (_, q) => {
    const tab = q.tab || 'revenue';
    const range = `<select aria-label="Khoảng thời gian" style="width:auto;height:36px;padding:0 12px">${['Hôm nay', '7 ngày', '30 ngày', 'Tháng này', 'Tùy chọn'].map((r) => `<option ${r === '30 ngày' ? 'selected' : ''}>${r}</option>`).join('')}</select><select aria-label="Chu kỳ" style="width:auto;height:36px;padding:0 12px"><option>Theo ngày</option><option>Theo tuần</option><option>Theo tháng</option></select><button class="btn secondary" data-act="exportReport">Xuất CSV</button>`;
    const tabs = `<div class="tabs">${[
      ['revenue', 'Doanh thu và đơn hàng'],
      ['ai', 'Hiệu quả AI'],
      ['support', 'CSKH'],
    ]
      .map(
        ([k, l]) => `<a href="#/admin/reports?tab=${k}" class="${tab === k ? 'on' : ''}">${l}</a>`,
      )
      .join('')}</div>`;
    let body = '';
    if (tab === 'revenue') {
      const rd = D.revenueDaily;
      const sum = rd.reduce((s, x) => s + x.revenue, 0);
      const cnt = rd.reduce((s, x) => s + x.orders, 0);
      body = `<div class="grid cols-4" style="margin-bottom:24px">${kpi('Doanh thu ghi nhận', money(sum), '+9% so với kỳ trước')}${kpi('Số đơn', cnt.toLocaleString('vi-VN'), '+4%')}${kpi('Giá trị trung bình đơn', money(Math.round(sum / cnt)))}${kpi('Tỉ lệ hủy', '6,4%', '+0,3 điểm', true)}</div>
        <div class="panel"><div class="ph"><h3>Doanh thu theo ngày</h3></div><div class="pb">${barChart(
          rd.map((x) => x.revenue),
          rd.map((x) => String(x.day)),
          { fmt: money, label: 'Doanh thu theo ngày' },
        )}
        <details style="margin-top:12px"><summary class="small" style="cursor:pointer">Xem bảng dữ liệu</summary><div class="table-wrap" style="margin-top:8px"><table class="tbl compact small"><tr><th>Ngày</th><th class="num">Doanh thu</th><th class="num">Số đơn</th></tr>${rd
          .slice(0, 10)
          .map(
            (x) =>
              `<tr><td>${x.day}/09</td><td class="num">${money(x.revenue)}</td><td class="num">${x.orders}</td></tr>`,
          )
          .join('')}</table></div></details></div></div>
        <div class="grid cols-3" style="margin-top:24px">${kpi('Hủy: khách / shop / quá hạn', '2,9% · 1,1% · 2,4%')}${kpi('VietQR / COD', '64% · 36%')}${kpi('Phí ship shop chịu', money(4820000))}</div>`;
    }
    if (tab === 'ai') {
      const groups = [
        ['control', 1840, '2,1%', '0,8%', money(41200)],
        ['algo', 8210, '3,4%', '1,2%', money(58900)],
        ['algo_llm', 8190, '4,0%', '1,4%', money(64100)],
      ];
      body = `<div class="panel"><div class="ph"><h3>Gợi ý sản phẩm: so sánh nhóm</h3></div><div class="table-wrap"><table class="tbl"><tr><th>Nhóm</th><th class="num">Số khách</th><th class="num">CTR</th><th class="num">Chuyển đổi</th><th class="num">Doanh thu mỗi khách</th></tr>${groups
        .map(
          (g) =>
            `<tr><td><code>${g[0]}</code></td>${g
              .slice(1)
              .map(
                (x) =>
                  `<td class="num">${typeof x === 'number' ? x.toLocaleString('vi-VN') : x}</td>`,
              )
              .join('')}</tr>`,
        )
        .join('')}</table></div>
          <div class="pb" style="padding-top:12px"><div class="alert">${icon('info', 18)}<span>algo_llm so với algo: chuyển đổi +0,2 điểm, khoảng tin cậy 95% [−0,1; +0,5]. <b>Chưa đủ dữ liệu để kết luận.</b></span></div></div></div>
        <div class="grid cols-2" style="margin-top:24px">${kpi('Tìm kiếm', '8.412 lượt · CTR 11,8%', '3,1% không có kết quả')}${kpi('Trợ lý AI', '1.230 hội thoại · 84% thích', 'Chuyển đổi từ chat 3,6%')}</div>
        <div class="panel" style="margin-top:24px"><div class="ph"><h3>Chi phí AI</h3><a class="link small" href="#/admin/ai">Chi tiết ${icon('arrowRight', 14)}</a></div><div class="pb">Đã dùng <b>${PK.S.budgetPct}%</b> ngân sách tháng · 0,021 USD cho mỗi đơn được quy cho AI</div></div>`;
    }
    if (tab === 'support')
      body = `<div class="grid cols-4">${kpi('Chuyển cho nhân viên', '128')}${kpi('Trong giờ / ngoài giờ', '106 / 22')}${kpi('Phản hồi đầu p50', '48 giây')}${kpi('CSAT', '4,6 / 5')}</div>`;
    return `${head('Báo cáo', '', range)}${tabs}${body}${note('Mỗi biểu đồ có bảng dữ liệu kèm theo. Xuất cần quyền reports:export (sales xem được nhưng không xuất được), tối đa 50.000 dòng.')}`;
  });
  act('exportReport', () => {
    if (!PK.can('reports:export')) return toast('Bạn cần quyền reports:export để xuất dữ liệu');
    audit('report.export', 'report', 'CSV 30 ngày');
    toast('Đã tạo file CSV');
  });

  // ================= ai-gateway: chi phí và cấu hình =================
  adminRoute('/admin/ai', 'ai-usage:read', (_, q) => {
    const S = PK.S;
    const tab = q.tab || 'usage';
    const canEdit = PK.can('ai-settings:manage');
    const tabs = `<div class="tabs"><a href="#/admin/ai" class="${tab === 'usage' ? 'on' : ''}">Chi phí và sử dụng</a><a href="#/admin/ai?tab=settings" class="${tab === 'settings' ? 'on' : ''}">Cấu hình</a></div>`;
    if (tab === 'settings') {
      return `${head('Chi phí AI')}${tabs}
        <div class="grid" style="grid-template-columns:480px 1fr;gap:24px;align-items:start">
        <form data-form="aiSettings" class="panel"><div class="ph"><h3>Ngân sách và hạn mức</h3></div><div class="pb stack">
          <label class="field"><span>Ngân sách tháng (USD)</span><input name="budget" type="number" step="1" value="${S.aiSettings.budget}" ${canEdit ? '' : 'disabled'}></label>
          <label class="field"><span>Tin nhắn mỗi ngày, khách đã đăng nhập</span><input name="userLimit" type="number" value="${S.aiSettings.userLimit}" ${canEdit ? '' : 'disabled'}></label>
          <label class="field"><span>Tin nhắn mỗi ngày, khách vãng lai</span><input name="guestLimit" type="number" value="${S.aiSettings.guestLimit}" ${canEdit ? '' : 'disabled'}></label>
          ${canEdit ? '<div><button class="btn">Lưu thay đổi</button></div>' : `<div class="alert">${icon('lock', 16)}<span>Bạn chỉ có quyền xem.</span></div>`}</div></form>
        <div class="panel"><div class="ph"><h3>Khi vượt ngân sách</h3></div><div class="table-wrap"><table class="tbl"><tr><th>Mức đã dùng</th><th>Hệ thống làm gì</th></tr><tr><td>Dưới 80%</td><td>Bình thường</td></tr><tr><td>80% đến dưới 100%</td><td>Cảnh báo trên trang tổng quan</td></tr><tr><td>Từ 100%</td><td>Dừng nội dung cá nhân hóa, xếp hạng gợi ý, bản nháp CSKH</td></tr><tr><td>Từ 120%</td><td>Dừng cả trợ lý chat, tìm kiếm bằng AI, lập chỉ mục</td></tr></table></div></div></div>`;
    }
    const daily = D.aiDaily;
    const spent = (S.aiSettings.budget * S.budgetPct) / 100;
    return `${head('Chi phí AI', 'Tháng 10/2026')}${tabs}
      ${S.aiDown ? `<div class="alert err" style="margin-bottom:16px">${icon('alert', 18)}<span>Kết nối DeepSeek đang tạm ngắt sau 5 lỗi liên tiếp. Hệ thống tự thử lại sau 60 giây.</span></div>` : ''}
      <div class="grid cols-4" style="margin-bottom:24px">
        <div class="kpi"><div class="l">Đã dùng tháng này</div><div class="v">${spent.toFixed(2)} USD</div><div class="bar" style="margin-top:10px"><i style="width:${Math.min(S.budgetPct, 100)}%;background:${S.budgetPct >= 100 ? 'var(--danger)' : S.budgetPct >= 80 ? '#d08700' : 'var(--ink)'}"></i></div><div class="xs muted" style="margin-top:6px">${S.budgetPct}% của ${S.aiSettings.budget} USD</div></div>
        ${kpi('Lượt gọi', '13.870')}${kpi('Tỉ lệ lỗi', '0,6%')}${kpi('Độ trễ p95', '2,9 giây')}</div>
      <div class="panel"><div class="ph"><h3>Chi phí theo ngày (USD)</h3><span class="row tight xs muted"><span style="width:10px;height:10px;background:var(--danger);border-radius:2px;display:inline-block"></span> Ngày lỗi cao</span></div><div class="pb">${barChart(
        daily.map((x) => x.cost),
        daily.map((x) => String(x.day)),
        {
          highlight: (i) => daily[i].errors > 30,
          fmt: (v) => v + ' USD',
          label: 'Chi phí AI theo ngày',
        },
      )}</div></div>
      <div class="panel" style="margin-top:24px"><div class="ph"><h3>Theo tính năng</h3></div><div class="table-wrap"><table class="tbl"><tr><th>Tính năng</th><th class="num">Chi phí (USD)</th><th class="num">Lượt gọi</th><th class="num">Tỉ lệ lỗi</th><th class="num">p95 (giây)</th></tr>${D.aiByFeature.map((r) => `<tr><td><code>${r[0]}</code></td><td class="num">${r[1].toLocaleString('vi-VN')}</td><td class="num">${r[2].toLocaleString('vi-VN')}</td><td class="num">${r[3].toLocaleString('vi-VN')}%</td><td class="num">${r[4].toLocaleString('vi-VN')}</td></tr>`).join('')}</table></div></div>
      ${note('Log không chứa nội dung prompt, chỉ có token, chi phí, độ trễ, trạng thái ok/error/blocked.')}`;
  });
  form('aiSettings', (data) => {
    const v = {
      budget: Number(data.budget),
      userLimit: Number(data.userLimit),
      guestLimit: Number(data.guestLimit),
    };
    if (Object.values(v).some((x) => !Number.isFinite(x) || x < 0))
      return toast('Giá trị không hợp lệ');
    audit('ai.settings_update', 'ai_settings', `budget ${PK.S.aiSettings.budget} → ${v.budget}`);
    PK.S.aiSettings = v;
    toast('Đã lưu, áp dụng ngay cho lần gọi tiếp theo');
    PK.render();
  });

  // ================= identity: khách hàng =================
  adminRoute('/admin/customers', 'customers:read', (_, q) => {
    const kw = PK.norm(q.q || '');
    const list = PK.S.users.filter(
      (u) => u.kind === 'customer' && (!kw || PK.norm(u.name + u.email + u.phone).includes(kw)),
    );
    const canManage = PK.can('customers:manage');
    return `${head('Khách hàng')}<form data-form="custSearch" class="filters-bar"><input name="q" value="${esc(q.q || '')}" placeholder="Tìm theo tên, email, số điện thoại" style="min-width:320px" aria-label="Tìm khách hàng"><button class="btn sm secondary">Tìm</button></form>
      <div class="table-wrap"><table class="tbl"><tr><th>Khách hàng</th><th>Số điện thoại</th><th>Ngày tạo</th><th>Trạng thái</th><th></th></tr>
      ${list
        .map(
          (
            u,
          ) => `<tr><td><div class="row tight" style="flex-wrap:nowrap"><span class="avatar light" style="width:32px;height:32px;font-size:12px">${PK.initials(u.name)}</span><span><b>${esc(u.name)}</b><div class="xs muted">${esc(u.email)}${u.verified ? '' : ' · chưa xác thực email'}</div></span></div></td><td>${esc(u.phone)}</td><td class="muted">${dmy(u.createdAt)}</td><td>${u.lockedAt ? '<span class="badge sale">Đã khóa</span>' : '<span class="badge ok">Hoạt động</span>'}</td>
        <td>${canManage ? `<button class="btn sm secondary" data-act="custLock" data-id="${u.id}">${u.lockedAt ? 'Mở khóa' : 'Khóa'}</button>` : ''}</td></tr>`,
        )
        .join('')}</table></div>
      ${note('Khóa khách thì mọi phiên của khách mất hiệu lực ngay. Sales và CSKH xem được nhưng không khóa được (403).')}`;
  });
  form('custSearch', (data) => PK.go('/admin/customers?q=' + encodeURIComponent(data.q)));
  act('custLock', (d) => {
    const u = PK.S.users.find((x) => x.id === d.id);
    u.lockedAt = u.lockedAt ? null : PK.now();
    audit(u.lockedAt ? 'customer.lock' : 'customer.unlock', 'user:' + u.id, '');
    toast(u.lockedAt ? 'Đã khóa tài khoản và đăng xuất mọi phiên' : 'Đã mở khóa tài khoản');
    PK.render();
  });

  // ================= identity: nhân viên =================
  const roleName = (code) => PK.S.roles.find((r) => r.code === code)?.name || code;
  adminRoute('/admin/staff', 'staff:read', () => {
    const staff = PK.S.users.filter((u) => u.kind === 'staff');
    const m = PK.can('staff:manage');
    return `${head('Nhân viên', `${staff.length} tài khoản`, m ? `<button class="btn" data-act="invite">${icon('plus', 16)} Mời nhân viên</button>` : '')}
      <div class="table-wrap"><table class="tbl"><tr><th>Nhân viên</th><th>Role</th><th>Trạng thái</th><th></th></tr>
      ${staff
        .map(
          (
            u,
          ) => `<tr><td><div class="row tight" style="flex-wrap:nowrap"><span class="avatar ${u.lockedAt ? 'light' : ''}" style="width:32px;height:32px;font-size:12px">${PK.initials(u.name)}</span><span><b>${esc(u.name)}</b>${u.id === PK.me().id ? ' <span class="badge">Bạn</span>' : ''}<div class="xs muted">${esc(u.email)}</div></span></div></td><td>${u.roles.map((r) => `<span class="badge">${roleName(r)}</span>`).join(' ') || '<span class="muted">Chưa có</span>'}</td><td>${u.pending ? '<span class="badge warn">Chờ kích hoạt</span>' : u.lockedAt ? '<span class="badge sale">Đã khóa</span>' : '<span class="badge ok">Hoạt động</span>'}</td>
        <td>${m && u.id !== PK.me().id ? `<div class="row tight" style="flex-wrap:nowrap;justify-content:flex-end"><button class="btn sm secondary" data-act="assignRoles" data-id="${u.id}">Gán role</button><button class="btn sm ghost" data-act="staffLock" data-id="${u.id}">${u.lockedAt ? 'Mở khóa' : 'Khóa'}</button></div>` : ''}</td></tr>`,
        )
        .join('')}</table></div>
      ${note('Quy tắc an toàn: không khóa hoặc gỡ quyền admin cuối cùng, không tự sửa role của mình, không gán "Khách hàng" cùng role nhân viên. Khóa hoặc gỡ hết role thì mọi phiên bị xóa ngay.')}`;
  });
  act('invite', () => {
    modal(`<h2>Mời nhân viên</h2><p class="muted">Người được mời nhận email đặt mật khẩu, link có hiệu lực 72 giờ.</p><form data-form="invite" class="stack"><label class="field"><span>Email công việc</span><input name="email" type="email" placeholder="hoa@pickory.vn"></label><label class="field"><span>Họ và tên</span><input name="name" placeholder="Nguyễn Hoa"></label>
      <div><span class="small" style="font-weight:500;color:var(--ink-2)">Role</span>${PK.S.roles
        .filter((r) => r.code !== 'customer')
        .map(
          (r) =>
            `<label class="check" style="padding:6px 0"><input type="checkbox" name="roles" value="${r.code}" ${r.code === 'sales' ? 'checked' : ''}> ${r.name}</label>`,
        )
        .join('')}</div>
      <div class="row" style="justify-content:flex-end"><button type="button" class="btn secondary" data-act="closeModal">Hủy</button><button class="btn">Gửi lời mời</button></div></form>`);
  });
  form('invite', (data) => {
    const roles = [].concat(data.roles || []);
    if (!/.+@.+\..+/.test(data.email)) return toast('Email không hợp lệ');
    if (PK.S.users.some((u) => u.kind === 'customer' && u.email === data.email))
      return toast('Email này đang là tài khoản khách hàng');
    if (!roles.length) return toast('Chọn ít nhất 1 role');
    const id = 's' + Date.now();
    PK.S.users.push({
      id,
      kind: 'staff',
      name: data.name || data.email,
      email: data.email,
      roles,
      lockedAt: null,
      pending: true,
      createdAt: PK.now().slice(0, 10),
    });
    audit('staff.invite', 'user:' + id, 'roles: ' + roles.join(', '));
    modal(
      `<h2>Đã gửi lời mời</h2><p>Lời mời đã được gửi tới <b>${esc(data.email)}</b>.</p>${note('Xem trước email: <a href="#/reset?invite=1" data-act="closeModal">pickory.vn/reset-password?t=…</a> (hạn 72 giờ, dùng 1 lần)')}<div class="row" style="justify-content:flex-end"><button class="btn" data-act="closeModal">Xong</button></div>`,
    );
    PK.render();
  });
  const activeAdmins = () =>
    PK.S.users.filter((u) => u.kind === 'staff' && u.roles.includes('admin') && !u.lockedAt);
  act('assignRoles', (d) => {
    const u = PK.S.users.find((x) => x.id === d.id);
    if (u.id === PK.me().id) return toast('Bạn không thể tự sửa role của chính mình');
    modal(`<h2>Gán role cho ${esc(u.name)}</h2><p class="muted">Thay đổi có hiệu lực ngay ở lần thao tác tiếp theo của nhân viên.</p><form data-form="assignRoles" data-id="${u.id}" class="stack">${PK.S.roles
      .filter((r) => r.code !== 'customer')
      .map(
        (r) =>
          `<label class="check" style="padding:4px 0"><input type="checkbox" name="roles" value="${r.code}" ${u.roles.includes(r.code) ? 'checked' : ''}> ${r.name}</label>`,
      )
      .join('')}
      <div class="row" style="justify-content:flex-end"><button type="button" class="btn secondary" data-act="closeModal">Hủy</button><button class="btn">Lưu</button></div></form>`);
  });
  form('assignRoles', (data, f) => {
    const u = PK.S.users.find((x) => x.id === f.dataset.id);
    const roles = [].concat(data.roles || []);
    if (u.roles.includes('admin') && !roles.includes('admin') && activeAdmins().length <= 1)
      return toast('Không thể gỡ quyền của admin cuối cùng');
    audit(
      'role.assign',
      'user:' + u.id,
      `${u.roles.join(', ')} → ${roles.join(', ') || '(trống)'}`,
    );
    u.roles = roles;
    closeModal();
    toast(roles.length ? 'Đã cập nhật role' : 'Đã gỡ hết role và đăng xuất mọi phiên');
    PK.render();
  });
  act('staffLock', (d) => {
    const u = PK.S.users.find((x) => x.id === d.id);
    if (!u.lockedAt && u.roles.includes('admin') && activeAdmins().length <= 1)
      return toast('Không thể khóa admin cuối cùng');
    u.lockedAt = u.lockedAt ? null : PK.now();
    audit(u.lockedAt ? 'staff.lock' : 'staff.unlock', 'user:' + u.id, '');
    toast(u.lockedAt ? 'Đã khóa và đăng xuất mọi phiên' : 'Đã mở khóa');
    PK.render();
  });

  // ================= identity: role và quyền =================
  adminRoute('/admin/roles', 'roles:manage', () => {
    const S = PK.S;
    const roles = S.roles.filter((r) => r.code !== 'customer');
    const has = (r, p) => r.perms === 'ALL' || r.perms.includes(p);
    return `${head('Role và quyền', 'Admin luôn có mọi quyền, kể cả quyền thêm sau này', `<button class="btn" data-act="newRole">${icon('plus', 16)} Tạo role</button>`)}
      <div class="table-wrap" style="margin-bottom:32px"><table class="tbl"><tr><th>Role</th><th>Loại</th><th class="num">Người giữ</th><th></th></tr>
      ${S.roles
        .map(
          (
            r,
          ) => `<tr><td><b>${r.name}</b><div class="xs muted"><code>${r.code}</code></div></td><td>${r.isSystem ? `<span class="badge dark">${icon('lock', 12)} Hệ thống</span>` : '<span class="badge">Tùy chỉnh</span>'}</td><td class="num">${S.users.filter((u) => u.roles.includes(r.code)).length}</td>
        <td>${r.isSystem ? '<span class="xs muted">Không sửa, không xóa</span>' : `<div class="row tight" style="flex-wrap:nowrap;justify-content:flex-end"><a class="btn sm secondary" href="#/admin/roles/${r.code}">Sửa quyền</a><button class="btn sm ghost" data-act="deleteRole" data-code="${r.code}">Xóa</button></div>`}</td></tr>`,
        )
        .join('')}</table></div>
      <h2>Ma trận quyền</h2><div class="table-wrap"><table class="tbl perm-grid"><tr><th>Quyền</th>${roles.map((r) => `<th>${r.name}</th>`).join('')}</tr>
      ${D.permissionGroups.map((g) => `<tr class="grp"><td colspan="${roles.length + 1}">${g.label}</td></tr>` + g.perms.map((p) => `<tr><td><code>${p}</code></td>${roles.map((r) => `<td>${has(r, p) ? `<span class="perm-yes" aria-label="Có">${icon('check', 16)}</span>` : `<span class="perm-no" aria-label="Không">${icon('minus', 16)}</span>`}</td>`).join('')}</tr>`).join('')).join('')}</table></div>`;
  });
  adminRoute('/admin/roles/:code', 'roles:manage', (p) => {
    const r = PK.S.roles.find((x) => x.code === p.code);
    if (!r || r.isSystem)
      return `<div class="empty"><b>Role hệ thống không chỉnh sửa được</b><a href="#/admin/roles">Quay lại</a></div>`;
    return `${crumbs([['Role và quyền', '#/admin/roles'], [r.name]])}${head(r.name, 'Chọn quyền cho role này')}
      <form data-form="saveRole" data-code="${r.code}" class="stack"><label class="field" style="max-width:360px"><span>Tên role</span><input name="name" value="${esc(r.name)}"></label>
      <div class="grid cols-2">${D.permissionGroups.map((g) => `<div class="panel"><div class="ph"><h3>${g.label}</h3></div><div class="pb" style="padding-top:12px">${g.perms.map((pm) => `<label class="check small" style="padding:5px 0"><input type="checkbox" name="perms" value="${pm}" ${r.perms.includes(pm) ? 'checked' : ''}> <code>${pm}</code></label>`).join('')}</div></div>`).join('')}</div>
      <div class="row"><button class="btn">Lưu thay đổi</button><a class="btn secondary" href="#/admin/roles">Hủy</a></div></form>`;
  });
  form('saveRole', (data, f) => {
    const r = PK.S.roles.find((x) => x.code === f.dataset.code);
    const perms = [].concat(data.perms || []);
    const added = perms.filter((p) => !r.perms.includes(p));
    const removed = r.perms.filter((p) => !perms.includes(p));
    audit(
      'role.update',
      'role:' + r.code,
      [...added.map((p) => '+ ' + p), ...removed.map((p) => '− ' + p)].join(', '),
    );
    r.perms = perms;
    r.name = data.name;
    toast('Đã lưu. Nhân viên giữ role này thấy thay đổi ngay.');
    PK.go('/admin/roles');
  });
  act('newRole', () => {
    modal(
      `<h2>Tạo role</h2><form data-form="newRole" class="stack"><label class="field"><span>Tên role</span><input name="name" placeholder="Ví dụ: Quản lý kho miền Bắc"></label><label class="field"><span>Mã role</span><input name="code" placeholder="quan-ly-kho-mien-bac"></label><div class="row" style="justify-content:flex-end"><button type="button" class="btn secondary" data-act="closeModal">Hủy</button><button class="btn">Tạo và chọn quyền</button></div></form>`,
    );
  });
  form('newRole', (data) => {
    if (!data.code || !data.name) return toast('Nhập tên và mã role');
    if (PK.S.roles.some((r) => r.code === data.code)) return toast('Mã role đã tồn tại');
    PK.S.roles.push({ code: data.code, name: data.name, isSystem: false, perms: [] });
    audit('role.create', 'role:' + data.code, '');
    closeModal();
    PK.go('/admin/roles/' + data.code);
  });
  act('deleteRole', (d) => {
    const holders = PK.S.users.filter((u) => u.roles.includes(d.code)).length;
    if (holders) return toast(`Không xóa được: còn ${holders} nhân viên giữ role này`);
    PK.S.roles = PK.S.roles.filter((r) => r.code !== d.code);
    audit('role.delete', 'role:' + d.code, '');
    PK.render();
  });

  // ================= identity: nhật ký thao tác =================
  adminRoute('/admin/audit', 'audit-logs:read', (_, q) => {
    const list = PK.S.audit.filter(
      (a) => (!q.actor || a.actor === q.actor) && (!q.action || a.action.startsWith(q.action)),
    );
    const actors = [...new Set(PK.S.audit.map((a) => a.actor))];
    const actions = [...new Set(PK.S.audit.map((a) => a.action.split('.')[0]))];
    return `${head('Nhật ký thao tác', 'Chỉ ghi thêm, không sửa hay xóa được')}
      <div class="filters-bar"><select data-change="auditFilter" data-key="actor" aria-label="Người thực hiện"><option value="">Mọi người</option>${actors.map((a) => `<option ${q.actor === a ? 'selected' : ''}>${a}</option>`).join('')}</select>
        <select data-change="auditFilter" data-key="action" aria-label="Hành động"><option value="">Mọi hành động</option>${actions.map((a) => `<option ${q.action === a ? 'selected' : ''}>${a}</option>`).join('')}</select><input type="date" aria-label="Từ ngày"></div>
      <div class="table-wrap"><table class="tbl"><tr><th>Thời gian</th><th>Người thực hiện</th><th>Hành động</th><th>Đối tượng</th><th>Thay đổi</th></tr>${list.map((a) => `<tr><td class="muted">${dmy(a.at)}</td><td>${esc(a.actor)}</td><td><code>${a.action}</code></td><td>${esc(a.target)}</td><td class="small">${esc(a.diff) || '-'}</td></tr>`).join('')}</table></div>
      ${note('Trường mật khẩu và token bị loại khỏi before/after. Các thao tác bạn làm trong prototype cũng được ghi ở đây.')}`;
  });
  change('auditFilter', (el) => {
    const { query } = PK.parseHash();
    query[el.dataset.key] = el.value;
    location.hash =
      '/admin/audit?' + new URLSearchParams(Object.entries(query).filter(([, v]) => v)).toString();
  });

  // ================= admin-console: hub cấu hình =================
  adminRoute('/admin/settings', null, () => {
    const items = [
      [
        'Bán hàng',
        'Ngưỡng miễn phí ship, hạn mức COD, tài khoản nhận tiền',
        '#/admin/settings/commerce',
        'commerce-settings:manage',
        'card',
      ],
      [
        'Vận chuyển',
        'Địa chỉ lấy hàng, tài khoản GHN, dịch vụ mặc định',
        '#/admin/settings/shipping',
        'shipping-settings:manage',
        'truck',
      ],
      [
        'CSKH',
        'Giờ làm việc, ngày nghỉ, nhắc chờ, tự đóng phiên',
        '#/admin/settings/support',
        'support-settings:manage',
        'headset',
      ],
      [
        'AI',
        'Ngân sách tháng, hạn mức tin nhắn',
        '#/admin/ai?tab=settings',
        'ai-settings:manage',
        'cpu',
      ],
      [
        'Gợi ý sản phẩm',
        'Chia nhóm thí nghiệm, cặp mua kèm',
        '#/admin/recommendations',
        'ai-settings:manage',
        'wand',
      ],
    ].filter((x) => PK.can(x[3]));
    return `${head('Cấu hình')}<div class="grid cols-3">${items.map(([t, d, h, , ic]) => `<a class="todo" href="${h}"><div class="top">${icon(ic, 20)}${icon('arrowRight', 16)}</div><div><b>${t}</b><div class="small muted" style="margin-top:4px">${d}</div></div></a>`).join('') || '<div class="empty"><b>Bạn chưa có quyền cấu hình</b></div>'}</div>`;
  });
  const settingsPage = (title, body, formName) =>
    `${crumbs([['Cấu hình', '#/admin/settings'], [title]])}${head(title)}<form data-form="${formName}" class="panel" style="max-width:640px"><div class="pb stack">${body}<div><button class="btn">Lưu thay đổi</button></div></div></form>`;
  adminRoute('/admin/settings/commerce', 'commerce-settings:manage', () => {
    const c = PK.S.commerceSettings;
    return settingsPage(
      'Bán hàng',
      `<div class="grid cols-2"><label class="field"><span>Miễn phí ship cho đơn từ (đ)</span><input name="freeShip" type="number" value="${c.freeShip}"></label><label class="field"><span>Hạn mức COD tối đa (đ)</span><input name="codMax" type="number" value="${c.codMax}"></label>
      <label class="field"><span>Số tài khoản nhận tiền</span><input name="account" value="${c.account}"></label><label class="field"><span>Ngân hàng</span><input name="bank" value="${c.bank}"></label></div>`,
      'commerceSettings',
    );
  });
  form('commerceSettings', (data) => {
    Object.assign(PK.S.commerceSettings, {
      freeShip: Number(data.freeShip),
      codMax: Number(data.codMax),
      account: data.account,
      bank: data.bank,
    });
    audit('commerce.settings_update', 'commerce_settings', '');
    toast('Đã lưu cấu hình bán hàng');
  });
  adminRoute('/admin/settings/shipping', 'shipping-settings:manage', () => {
    const s = PK.S.shippingSettings;
    return settingsPage(
      'Vận chuyển',
      `<div class="grid cols-2"><label class="field"><span>Tên điểm lấy hàng</span><input value="${esc(s.name)}"></label><label class="field"><span>Số điện thoại</span><input value="${esc(s.phone)}"></label></div><label class="field"><span>Địa chỉ lấy hàng</span><input value="${esc(s.address)}"></label>
      <div class="grid cols-2"><label class="field"><span>GHN ShopId</span><input value="${s.shopId}"></label><label class="field"><span>Dịch vụ mặc định</span><select><option>${s.service}</option><option>Nhanh</option></select></label></div><p class="small muted" style="margin:0">Khách được xem hàng, không được thử. Phí thực tế chênh với phí báo giá thì shop chịu phần chênh.</p>`,
      'shippingSettings',
    );
  });
  form('shippingSettings', () => toast('Đã lưu cấu hình vận chuyển'));
  adminRoute('/admin/settings/support', 'support-settings:manage', () => {
    const s = PK.S.supportSettings;
    return settingsPage(
      'CSKH',
      `<div class="grid cols-2"><label class="field"><span>Bắt đầu</span><input type="time" value="${s.start}"></label><label class="field"><span>Kết thúc</span><input type="time" value="${s.end}"></label></div>
      <label class="field"><span>Ngày nghỉ</span><input value="${esc(s.holidays)}"></label>
      <div class="grid cols-2"><label class="field"><span>Nhắc khi khách chờ quá (phút)</span><input type="number" value="${s.nudge}"></label><label class="field"><span>Tự đóng phiên không hoạt động (phút)</span><input type="number" value="${s.autoClose}"></label></div><p class="small muted" style="margin:0">Giờ theo múi Asia/Ho_Chi_Minh.</p>`,
      'supportSettings',
    );
  });
  form('supportSettings', () => toast('Đã lưu cấu hình CSKH'));
})();
