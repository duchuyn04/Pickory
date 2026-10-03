/* Back-office: admin-console (khung, menu theo quyền, báo cáo), identity (nhân viên, role, khách, nhật ký),
   catalog, commerce, shipping, support-desk, knowledge-base, personalized-content, recommendations,
   smart-search, ai-gateway. */
/* eslint-disable */
(function () {
  const PK = window.PK;
  const D = PK.D;
  const {
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
    `<div class="card" style="max-width:520px"><h1>403</h1><p>Bạn không có quyền <code>${perm}</code> để xem trang này.</p>${note('Backend vẫn kiểm tra quyền bằng requirePermission, menu ẩn chỉ để tiện dùng.')}</div>`;
  const gate = (perm, fn) => (params, q) => (PK.can(perm) ? fn(params, q) : forbidden(perm));
  // Chưa đăng nhập nhân viên thì không chạy trang, layout sẽ hiện form đăng nhập
  const adminRoute = (path, perm, fn) => {
    const page = perm ? gate(perm, fn) : fn;
    route(path, (params, q) => (PK.isStaff() ? page(params, q) : ''), 'admin');
  };

  // ================= admin-console: khung back-office =================
  const MENU = [
    { sec: 'Vận hành' },
    { label: 'Tổng quan', path: '/admin', perm: null },
    {
      label: 'Đơn hàng',
      path: '/admin/orders',
      perm: 'orders:read',
      badge: () => PK.S.orders.filter((o) => o.status === 'pending_confirmation').length,
    },
    {
      label: 'Đối soát thanh toán',
      path: '/admin/payments',
      perm: 'payments:read',
      badge: () => PK.S.sepayTx.filter((t) => t.match === 'unmatched').length,
    },
    {
      label: 'CSKH',
      path: '/admin/support',
      perm: 'conversations:read',
      badge: () =>
        PK.S.convs.filter((c) => c.status === 'waiting_human').length +
        PK.S.callbacks.filter((c) => c.status === 'pending').length,
    },
    {
      label: 'Kiểm duyệt review',
      path: '/admin/reviews',
      perm: 'reviews:moderate',
      badge: () => PK.S.reviews.filter((r) => r.reports >= 3 && r.status === 'visible').length,
    },
    { sec: 'Sản phẩm' },
    { label: 'Sản phẩm', path: '/admin/products', perm: 'products:read' },
    { label: 'Danh mục và thuộc tính', path: '/admin/categories', perm: 'products:write' },
    { label: 'Thương hiệu', path: '/admin/brands', perm: 'products:write' },
    { label: 'Tồn kho', path: '/admin/inventory', perm: 'inventory:write' },
    { sec: 'AI và nội dung' },
    { label: 'Kho tri thức', path: '/admin/knowledge', perm: 'knowledge:read' },
    {
      label: 'Nội dung AI',
      path: '/admin/ai-content',
      perm: 'products:read',
      badge: () => PK.S.aiContents.filter((c) => c.status === 'pending_approval').length,
    },
    { label: 'Gợi ý sản phẩm', path: '/admin/recommendations', perm: 'reports:read' },
    { label: 'Tìm kiếm', path: '/admin/search', perm: 'reports:read' },
    { label: 'Chi phí AI', path: '/admin/ai', perm: 'ai-usage:read' },
    { sec: 'Báo cáo' },
    { label: 'Báo cáo', path: '/admin/reports', perm: 'reports:read' },
    { sec: 'Hệ thống' },
    { label: 'Khách hàng', path: '/admin/customers', perm: 'customers:read' },
    { label: 'Nhân viên', path: '/admin/staff', perm: 'staff:read' },
    { label: 'Role và quyền', path: '/admin/roles', perm: 'roles:manage' },
    { label: 'Nhật ký thao tác', path: '/admin/audit', perm: 'audit-logs:read' },
    {
      label: 'Cấu hình',
      path: '/admin/settings',
      perm: null,
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
        return `<a href="#${m.path}" class="${on ? 'on' : ''}">${m.label}${b ? `<span class="cnt">${b}</span>` : ''}</a>`;
      })
      .join('');
    const handlesChat = PK.can('conversations:reply');
    return `<div class="admin">
      <aside><div class="who"><b>pick<span style="color:var(--color-ai)">ory</span></b> <span class="small muted">back-office</span>
        <div style="margin-top:12px"><b>${esc(u.name)}</b><div class="small muted">${u.roles.map((r) => S.roles.find((x) => x.code === r).name).join(', ')}</div></div>
        ${handlesChat ? `<label class="switch small" style="margin-top:8px"><input type="checkbox" data-change="presence" ${S.presence === 'available' ? 'checked' : ''}> ${S.presence === 'available' ? 'Sẵn sàng nhận chat' : 'Tạm vắng'}</label>` : ''}
        <div style="margin-top:8px"><a href="#" class="small" data-act="staffLogout">Đăng xuất</a> · <a href="#/" class="small">Xem cửa hàng</a></div></div>
        <nav>${navHtml}</nav>
        <div class="proto-note">Menu lọc theo quyền của <b>${esc(u.name)}</b>. Đổi vai trò ở thanh trên cùng để so sánh admin, sales, CSKH.</div>
      </aside>
      <div class="content">${html}</div></div>`;
  };
  function staffLogin() {
    return `<div style="max-width:420px;margin:64px auto" class="card"><h1>Đăng nhập back-office</h1>
      <form data-form="staffLogin" class="stack"><label class="field"><span>Email nhân viên</span><input name="email" type="email" value="nam.admin@pickory.vn"></label>
      <label class="field"><span>Mật khẩu</span><input name="password" type="password" placeholder="12345678"></label><button class="btn block">Đăng nhập</button></form>
      <p class="small muted" style="margin-top:12px">Back-office không có đăng nhập Google. Phiên nhân viên 12 giờ, không gia hạn.</p>
      <div class="proto-note"><b>Đăng nhập nhanh:</b><div class="row" style="margin-top:6px">
        <button class="btn sm" data-act="quickStaff" data-role="admin">Admin</button><button class="btn sm secondary" data-act="quickStaff" data-role="sales">Sales</button><button class="btn sm secondary" data-act="quickStaff" data-role="support">CSKH</button></div></div></div>`;
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
    return false;
  });
  change('presence', (el) => {
    PK.S.presence = el.checked ? 'available' : 'away';
    toast(el.checked ? 'Sẵn sàng nhận chat' : 'Tạm vắng: không nhận chat mới');
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
      ]);
      cards.push([
        'Đơn COD chưa nhận tiền',
        S.orders.filter(
          (o) => o.method === 'cod' && o.status === 'delivered' && o.payment === 'unpaid',
        ).length,
        '#/admin/payments?tab=cod',
      ]);
    }
    if (PK.can('payments:read'))
      cards.push([
        'Giao dịch SePay chưa khớp',
        S.sepayTx.filter((t) => t.match === 'unmatched').length,
        '#/admin/payments',
      ]);
    if (PK.can('products:read'))
      cards.push(['SKU sắp hết hàng', lowStock().length, '#/admin/inventory']);
    if (PK.can('conversations:read')) {
      cards.push([
        'Hội thoại đang chờ',
        S.convs.filter((c) => c.status === 'waiting_human').length,
        '#/admin/support',
      ]);
      cards.push([
        'Yêu cầu gọi lại',
        S.callbacks.filter((c) => c.status === 'pending').length,
        '#/admin/support?tab=callbacks',
      ]);
    }
    if (PK.can('products:write'))
      cards.push([
        'Banner AI chờ duyệt',
        S.aiContents.filter((c) => c.status === 'pending_approval').length,
        '#/admin/ai-content',
      ]);
    if (PK.can('ai-usage:read'))
      cards.push(['Ngân sách AI tháng đã dùng', S.budgetPct + '%', '#/admin/ai']);
    return `<div class="page-head"><h1>Chào ${esc(PK.me().name)}</h1><span class="muted small">Badge tự làm mới mỗi 60 giây</span></div>
      ${S.budgetPct >= 80 && PK.can('ai-usage:read') ? `<div class="alert warn" style="margin-bottom:16px">Ngân sách AI tháng đã dùng ${S.budgetPct}%. ${S.budgetPct >= 100 ? 'Các tính năng ưu tiên thấp (nội dung cá nhân hóa, xếp hạng gợi ý, bản nháp CSKH) đã tạm dừng.' : 'Sắp chạm ngưỡng 100%.'}</div>` : ''}
      <div class="grid cols-3">${cards.map(([l, v, h]) => `<a class="todo" href="${h}"><span>${l}</span><span class="v">${v}</span></a>`).join('')}</div>
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
    return `<div class="page-head"><h1>Đơn hàng</h1><div class="row">
        <select style="width:auto" data-change="orderFlag"><option value="">Mọi cờ</option>${Object.entries(
          FLAG_LABEL,
        )
          .map(([k, l]) => `<option value="${k}" ${q.flag === k ? 'selected' : ''}>${l}</option>`)
          .join('')}</select>
        <button class="btn secondary" data-act="exportOrders">Xuất Excel</button></div></div>
      <div class="tabs">${TABS.map(([k, l]) => `<a href="#/admin/orders${k ? '?tab=' + k : ''}" class="${tab === k ? 'on' : ''}">${l} <span class="muted small">${S.orders.filter((o) => !k || o.status === k).length}</span></a>`).join('')}</div>
      <div class="table-wrap"><table class="tbl"><tr><th>Mã</th><th>Ngày</th><th>Khách</th><th class="num">Tổng</th><th>PT</th><th>Trạng thái</th><th>Thanh toán</th><th>Cờ</th></tr>
      ${list.map((o) => `<tr class="click" data-act="nav" data-href="#/admin/orders/${o.code}"><td><b>${o.code}</b></td><td>${o.createdAt}</td><td>${esc(o.customer)}${o.userId ? '' : ' <span class="badge gray">Khách vãng lai</span>'}</td><td class="num">${money(PK.orderTotal(o))}</td><td>${o.method === 'cod' ? 'COD' : 'SePay'}</td><td>${PK.orderBadge(o.status)}</td><td>${PK.payBadge(o.payment)}</td><td>${o.flags.map((f) => `<span class="badge warn">${FLAG_LABEL[f]}</span>`).join(' ')}</td></tr>`).join('') || '<tr><td colspan="8" class="muted">Không có đơn.</td></tr>'}</table></div>`;
  });
  change('orderFlag', (el) => {
    const { query } = PK.parseHash();
    query.flag = el.value;
    location.hash =
      '/admin/orders?' + new URLSearchParams(Object.entries(query).filter(([, v]) => v)).toString();
  });
  act('exportOrders', () => {
    if (!PK.can('reports:export')) return toast('Cần quyền reports:export để xuất (403)');
    audit('report.export', 'orders', 'Xuất ' + PK.S.orders.length + ' đơn (tối đa 50.000 dòng)');
    toast('Đã tạo file don-hang.xlsx (ghi nhật ký thao tác)');
  });

  adminRoute('/admin/orders/:code', 'orders:read', (p) => {
    const o = PK.order(p.code);
    if (!o) return '<h1>Không tìm thấy đơn</h1>';
    const btns = [];
    if (o.status === 'pending_confirmation' && PK.can('orders:update-status'))
      btns.push(
        `<button class="btn" data-act="orderConfirm" data-code="${o.code}">Xác nhận</button>`,
      );
    if (o.status === 'confirmed' && PK.can('orders:fulfill'))
      btns.push(
        `<button class="btn" data-act="shipCreate" data-code="${o.code}">Tạo vận đơn</button>`,
      );
    if (o.status === 'shipping' && PK.can('orders:fulfill')) {
      btns.push(
        `<button class="btn secondary" data-act="shipCancel" data-code="${o.code}">Hủy vận đơn</button>`,
      );
      btns.push(
        `<button class="btn secondary" data-act="ghnDelivered" data-code="${o.code}">Giả lập GHN: đã giao</button>`,
      );
    }
    if (
      ['pending_payment', 'pending_confirmation', 'confirmed'].includes(o.status) &&
      PK.can('orders:cancel')
    )
      btns.push(
        `<button class="btn danger" data-act="orderCancel" data-code="${o.code}">Hủy đơn</button>`,
      );
    if (o.status === 'shipping' && PK.can('orders:cancel'))
      btns.push(
        `<button class="btn danger disabled" data-act="toastMsg" data-msg="Hủy vận đơn trước rồi mới hủy đơn">Hủy đơn</button>`,
      );
    if (
      o.status === 'delivered' &&
      o.method === 'cod' &&
      o.payment === 'unpaid' &&
      PK.can('payments:reconcile')
    )
      btns.push(
        `<button class="btn" data-act="codReceived" data-code="${o.code}">Đã nhận COD</button>`,
      );
    if (o.payment === 'refund_pending' && PK.can('refunds:manage'))
      btns.push(
        `<button class="btn" data-act="refunded" data-code="${o.code}">Đã hoàn tiền</button>`,
      );
    if (o.flags.includes('invoice_requested') && PK.can('orders:update-status'))
      btns.push(
        `<button class="btn secondary" data-act="invoiced" data-code="${o.code}">Đã xuất hóa đơn</button>`,
      );
    return `<div class="breadcrumb"><a href="#/admin/orders">Đơn hàng</a> / ${o.code}</div>
      <div class="page-head"><h1>${o.code} ${PK.orderBadge(o.status)} ${PK.payBadge(o.payment)}</h1><div class="row">${btns.join('') || '<span class="muted small">Không có thao tác phù hợp với quyền của bạn</span>'}</div></div>
      ${o.flags.length ? `<div class="row" style="margin-bottom:12px">${o.flags.map((f) => `<span class="badge warn">${FLAG_LABEL[f]}</span>`).join('')}</div>` : ''}
      <div class="grid cols-2">
        <div class="card stack"><h3>Sản phẩm</h3>${o.items.map((i) => `<div class="row between"><span>${i.qty} × ${esc(i.name)} <span class="muted small">${i.sku}</span></span><b>${money(i.price * i.qty)}</b></div>`).join('')}
          <div class="row between small"><span>Phí ship ${money(o.shippingFee)}${o.shippingDiscount ? ', shop chịu ' + money(o.shippingDiscount) : ''}</span><span></span></div>
          <div class="row between"><b>Tổng</b><b>${money(PK.orderTotal(o))}</b></div>
          ${o.vat ? `<div class="card muted-bg small"><b>Hóa đơn VAT</b><br>${esc(o.vat.company)}<br>MST ${esc(o.vat.taxCode)}<br>${esc(o.vat.address)}<br>${esc(o.vat.email)}</div>` : ''}</div>
        <div class="card stack"><h3>Khách và giao hàng</h3><p class="small">${esc(o.customer)} · ${esc(o.phone)} · ${esc(o.email)}<br>${esc(o.address)}</p>
          ${o.tracking ? `<p>GHN <b>${o.tracking}</b> · <span class="badge">${PK.SHIP_STATUS[o.shipStatus]}</span></p><table class="tbl small"><tr><th>Thời gian</th><th>Trạng thái</th><th>GHN gốc</th></tr>${(o.shipEvents || []).map((e) => `<tr><td>${e[0]}</td><td>${PK.SHIP_STATUS[e[1]]}</td><td class="muted">${e[2]}</td></tr>`).join('')}</table>` : '<p class="muted small">Chưa có vận đơn.</p>'}</div>
      </div>
      <div class="card" style="margin-top:16px"><h3>Lịch sử trạng thái</h3><table class="tbl"><tr><th>Thời gian</th><th>Từ</th><th>Sang</th><th>Người thực hiện, lý do</th></tr>
        ${o.history.map((h) => `<tr><td>${h[0]}</td><td>${h[1] ? PK.ORDER_STATUS[h[1]] : '-'}</td><td>${PK.ORDER_STATUS[h[2]]}</td><td>${esc(h[3])}</td></tr>`).join('')}</table></div>
      ${note('Nút hiện theo quyền và trạng thái. Chuyển trạng thái sai thứ tự trả 409. Mỗi thao tác ghi 1 dòng nhật ký.')}`;
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
    modal(`<h2>Tạo vận đơn GHN</h2><form data-form="shipCreate" data-code="${o.code}" class="stack">
      <div class="grid cols-2"><label class="field"><span>Khối lượng (g)</span><input name="w" value="${2600 * o.items.length}"></label><label class="field"><span>Dài × rộng × cao (cm)</span><input name="dim" value="40 × 30 × 8"></label>
      <label class="field"><span>Tiền thu hộ COD</span><input value="${o.method === 'cod' && o.payment === 'unpaid' ? money(PK.orderTotal(o)) : '0 đ (đã thanh toán SePay)'}" disabled></label><label class="field"><span>Giá trị khai giá</span><input value="${money(PK.orderTotal(o))}" disabled></label></div>
      <p class="small muted">Cho xem hàng, không cho thử. Nếu lỗi mạng: kiểm tra GHN theo mã đơn trước khi thử lại để tránh tạo trùng.</p>
      <div class="row"><button class="btn">Tạo vận đơn</button><button type="button" class="btn secondary" data-act="closeModal">Hủy</button></div></form>`);
  });
  form('shipCreate', (_, f) => {
    const o = PK.order(f.dataset.code);
    o.tracking = 'GHN' + Math.random().toString(36).slice(2, 9).toUpperCase();
    o.shipStatus = 'pending_pickup';
    o.shipEvents = [[PK.now(), 'pending_pickup', 'ready_to_pick']];
    setStatus(o, 'shipping', 'tạo vận đơn ' + o.tracking);
    closeModal();
    toast('Đã tạo vận đơn ' + o.tracking + ', phí 32.000 đ');
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
    toast('Webhook GHN: đã giao hàng');
    PK.render();
  });
  act('orderCancel', (d) => {
    modal(`<h2>Hủy đơn ${d.code}</h2><form data-form="orderCancel" data-code="${d.code}" class="stack"><label class="field"><span>Lý do (bắt buộc)</span><select name="reason"><option>Shop hủy: hết hàng</option><option>Khách yêu cầu hủy qua hotline</option><option>Không liên lạc được khách</option></select></label>
      <div class="row"><button class="btn danger">Hủy đơn</button><button type="button" class="btn secondary" data-act="closeModal">Đóng</button></div></form>`);
  });
  form('orderCancel', (data, f) => {
    const o = PK.order(f.dataset.code);
    setStatus(o, 'cancelled', data.reason);
    o.cancelReason = data.reason.startsWith('Khách') ? 'khách hủy' : 'shop hủy';
    if (o.payment === 'paid') o.payment = 'refund_pending';
    closeModal();
    toast('Đã hủy đơn, hàng được trả về kho');
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
    const code = prompt('Mã giao dịch hoàn tiền', 'FT' + Date.now().toString().slice(-8));
    if (!code) return;
    const o = PK.order(d.code);
    o.payment = 'refunded';
    audit('payment.refund', 'order:' + o.code, 'refund_pending → refunded (' + code + ')');
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
    return `<div class="page-head"><h1>Đối soát thanh toán</h1></div>
      <div class="tabs"><a href="#/admin/payments" class="${tab === 'sepay' ? 'on' : ''}">Giao dịch SePay</a><a href="#/admin/payments?tab=cod" class="${tab === 'cod' ? 'on' : ''}">COD chưa nhận tiền (${cod.length})</a></div>
      ${
        tab === 'sepay'
          ? `<table class="tbl"><tr><th>Mã GD</th><th class="num">Số tiền</th><th>Nội dung</th><th>Nhận lúc</th><th>Trạng thái</th><th>Đơn</th><th></th></tr>
        ${S.sepayTx
          .map(
            (
              t,
            ) => `<tr><td>${t.id}</td><td class="num">${money(t.amount)}</td><td>${esc(t.content)}${t.note ? `<div class="small muted">${esc(t.note)}</div>` : ''}</td><td>${t.receivedAt}</td><td>${MATCH[t.match]}</td><td>${t.order ? `<a href="#/admin/orders/${t.order}">${t.order}</a>` : '-'}</td>
          <td>${t.match === 'unmatched' && PK.can('payments:reconcile') ? `<div class="row"><button class="btn sm" data-act="txMatch" data-id="${t.id}">Khớp vào đơn</button><button class="btn sm ghost" data-act="txIgnore" data-id="${t.id}">Không thuộc đơn nào</button></div>` : ''}</td></tr>`,
          )
          .join('')}</table>
        ${note('Webhook SePay tự khớp theo mã đơn trong nội dung. Không có mã thì vào "Chưa khớp" để nhân viên khớp tay. Tiền về sau khi đơn quá hạn: hệ thống thử giữ lại hàng, được thì khôi phục đơn, không thì chuyển "Chờ hoàn tiền".')}`
          : `<table class="tbl"><tr><th>Đơn</th><th>Khách</th><th class="num">Tiền thu hộ</th><th>Giao lúc</th><th></th></tr>${cod.map((o) => `<tr><td><a href="#/admin/orders/${o.code}">${o.code}</a></td><td>${esc(o.customer)}</td><td class="num">${money(PK.orderTotal(o))}</td><td>${o.history[o.history.length - 1][0]}</td><td>${PK.can('payments:reconcile') ? `<button class="btn sm" data-act="codReceived" data-code="${o.code}">Đã nhận COD</button>` : ''}</td></tr>`).join('') || '<tr><td colspan="5" class="muted">Không còn đơn COD chờ tiền.</td></tr>'}</table>`
      }`;
  });
  act('txMatch', (d) => {
    const cands = PK.S.orders.filter(
      (o) => ['pending_payment', 'cancelled'].includes(o.status) && o.payment !== 'paid',
    );
    modal(`<h2>Khớp giao dịch ${d.id}</h2><form data-form="txMatch" data-id="${d.id}" class="stack"><label class="field"><span>Chọn đơn</span><select name="order">${cands.map((o) => `<option value="${o.code}">${o.code} · ${esc(o.customer)} · ${money(PK.orderTotal(o))} · ${PK.ORDER_STATUS[o.status]}</option>`).join('')}</select></label>
      <div class="row"><button class="btn">Khớp</button><button type="button" class="btn secondary" data-act="closeModal">Hủy</button></div></form>`);
  });
  form('txMatch', (data, f) => {
    const t = PK.S.sepayTx.find((x) => x.id === f.dataset.id);
    const o = PK.order(data.order);
    t.match = 'matched';
    t.order = o.code;
    if (o.status === 'cancelled') {
      o.payment = 'refund_pending';
      t.note = 'Đơn đã hủy, hết hàng để giữ lại: chuyển chờ hoàn tiền';
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
    hidden: '<span class="badge gray">Ẩn</span>',
    discontinued: '<span class="badge warn">Ngừng KD</span>',
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
    return `<div class="page-head"><h1>Sản phẩm</h1>${PK.can('products:write') ? '<a class="btn" href="#/admin/products/new">Thêm sản phẩm</a>' : '<span class="badge gray">Chỉ xem</span>'}</div>
      <form data-form="productSearch" class="row" style="margin-bottom:12px"><input name="q" value="${esc(q.q || '')}" placeholder="Tìm theo tên hoặc SKU" style="max-width:280px">
        <select name="status" style="width:auto"><option value="">Mọi trạng thái</option>${Object.keys(
          P_STATUS,
        )
          .map((s) => `<option value="${s}" ${q.status === s ? 'selected' : ''}>${s}</option>`)
          .join('')}</select>
        <label class="row small"><input type="checkbox" name="low" ${q.low ? 'checked' : ''}> Sắp hết hàng</label><button class="btn secondary sm">Lọc</button></form>
      <table class="tbl"><tr><th></th><th>Tên</th><th>Danh mục</th><th>Hãng</th><th>Trạng thái</th><th class="num">Giá từ</th><th class="num">Tồn</th></tr>
      ${list.map((p) => `<tr class="click" data-act="nav" data-href="#/admin/products/${p.id}"><td>${productImg(p, 36)}</td><td><b>${esc(p.name)}</b></td><td>${PK.cat(p.cat).name}</td><td>${p.brand}</td><td>${P_STATUS[p.status]}</td><td class="num">${money(p.price)}</td><td class="num">${totalStock(p) <= 5 ? `<span class="badge warn">${totalStock(p)}</span>` : totalStock(p)}</td></tr>`).join('')}</table>`;
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
    return `<div class="breadcrumb"><a href="#/admin/products">Sản phẩm</a> / ${isNew ? 'Thêm mới' : esc(prod.name)}</div>
      <div class="page-head"><h1>${isNew ? 'Thêm sản phẩm' : esc(prod.name)} ${P_STATUS[prod.status]}</h1>
        ${ro ? '<span class="badge gray">Chỉ xem (thiếu products:write)</span>' : `<div class="row"><button class="btn secondary" data-act="pStatus" data-id="${prod.id}" data-s="draft">Lưu nháp</button><button class="btn" data-act="pStatus" data-id="${prod.id}" data-s="active">Đăng bán</button><button class="btn secondary" data-act="pStatus" data-id="${prod.id}" data-s="hidden">Ẩn</button><button class="btn secondary" data-act="pStatus" data-id="${prod.id}" data-s="discontinued">Ngừng KD</button><button class="btn danger" data-act="pDelete" data-id="${prod.id}">Xóa</button></div>`}</div>
      <div class="grid" style="grid-template-columns:2fr 1fr;align-items:start">
        <div class="stack">
          <div class="card stack"><h3>Thông tin chung</h3>
            <div class="grid cols-2"><label class="field"><span>Tên *</span><input value="${esc(prod.name)}" ${dis}></label><label class="field"><span>Slug</span><input value="${esc(prod.slug)}" ${dis}></label>
            <label class="field"><span>Thương hiệu</span><select ${dis}>${D.brands.map((b) => `<option ${b === prod.brand ? 'selected' : ''}>${b}</option>`).join('')}</select></label>
            <label class="field"><span>Danh mục (chỉ danh mục lá)</span><select ${dis}>${D.categories
              .filter((x) => !D.categories.some((y) => y.parent === x.id))
              .map((x) => `<option ${x.id === prod.cat ? 'selected' : ''}>${x.name}</option>`)
              .join('')}</select></label>
            <label class="field"><span>Bảo hành (tháng)</span><input value="${prod.warranty}" ${dis}></label></div>
            <label class="field"><span>Mô tả ngắn</span><input value="${esc(prod.short)}" ${dis}></label>
            <label class="field"><span>Mô tả chi tiết (Markdown)</span><textarea ${dis}>## Điểm nổi bật\n- ${esc(prod.short)}</textarea></label></div>
          <div class="card stack"><h3>Thông số kỹ thuật</h3><p class="small muted">Trường sinh theo thuộc tính của danh mục ${c.name}. Trường bắt buộc thiếu hoặc sai kiểu thì không lưu được.</p>
            <div class="grid cols-2">${attrs.map((a) => `<label class="field"><span>${a.label}${a.unit ? ' (' + a.unit + ')' : ''} · ${a.type}${a.filterable ? ' · lọc được' : ''}</span><input value="${esc(prod.specs[a.key] ?? '')}" ${dis}></label>`).join('') || '<p class="muted small">Danh mục chưa có thuộc tính.</p>'}</div></div>
          <div class="card stack"><h3>Biến thể</h3><p class="small muted">Tối đa 3 trục tùy chọn. Mỗi biến thể là 1 SKU có giá, tồn, kích thước riêng.</p>
            <div class="table-wrap"><table class="tbl small"><tr><th>SKU</th><th>Tùy chọn</th><th class="num">Giá</th><th class="num">Giá gạch</th><th class="num">Tồn</th><th>KL (g)</th><th>D×R×C (cm)</th><th>Bật</th></tr>
            ${variants.map((v) => `<tr><td>${v.sku}</td><td>${Object.values(v.opts).join(', ') || '-'}</td><td class="num">${money(v.price)}</td><td class="num">${v.compareAt ? money(v.compareAt) : '-'}</td><td class="num">${v.stock}</td><td>2500</td><td>36×26×3</td><td><input type="checkbox" checked ${dis}></td></tr>`).join('')}</table></div></div>
        </div>
        <div class="stack"><div class="card stack"><h3>Ảnh</h3><div class="row">${[1, 2, 3].map(() => `<div style="width:72px;height:72px;background:var(--color-surface-muted);border-radius:10px;display:flex;align-items:center;justify-content:center">${isNew ? '' : productImg(prod, 56)}</div>`).join('')}</div>
            <button class="btn secondary sm" ${dis} data-act="toastMsg" data-msg="Chọn ảnh JPG/PNG/WebP ≤ 5MB, tối đa 10 ảnh">Tải ảnh lên</button><p class="small muted">Kéo để sắp xếp, gắn ảnh với màu, nhập alt text.</p></div>
          ${note('Đăng bán cần ít nhất 1 ảnh và 1 biến thể bật có giá, khối lượng, kích thước. Giá gạch phải lớn hơn giá bán. Chỉ xóa hẳn được sản phẩm chưa từng có đơn.')}</div>
      </div>`;
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
    toast('Đã chuyển trạng thái: ' + d.s);
    PK.render();
  });
  act('pDelete', (d) => {
    const hasOrder = PK.S.orders.some((o) => o.items.some((i) => i.pid === d.id));
    toast(
      hasOrder ? 'Không xóa được: sản phẩm đã có đơn. Hãy chọn "Ngừng KD".' : 'Đã xóa sản phẩm',
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
          `<a href="#/admin/categories?c=${c.id}" style="display:block;padding:4px 0;${sel === c.id ? 'font-weight:700' : ''}">${c.name}</a>${D.categories
            .filter((x) => x.parent === c.id)
            .map((x) => `<div class="small muted" style="padding:2px 0 2px 16px">${x.name}</div>`)
            .join('')}`,
      )
      .join('');
    return `<div class="page-head"><h1>Danh mục và thuộc tính</h1><button class="btn" data-act="toastMsg" data-msg="Mở form tạo danh mục (tối đa 2 cấp)">Thêm danh mục</button></div>
      <div class="grid" style="grid-template-columns:260px 1fr;align-items:start"><div class="card">${tree}</div>
      <div class="card"><div class="row between"><h3>Thuộc tính của ${PK.cat(sel).name}</h3><button class="btn sm secondary" data-act="toastMsg" data-msg="Thêm thuộc tính mới">Thêm thuộc tính</button></div>
        <table class="tbl"><tr><th>Key</th><th>Nhãn</th><th>Nhóm</th><th>Kiểu</th><th>Đơn vị</th><th>Bắt buộc</th><th>Lọc được</th></tr>
        ${attrs.map((a) => `<tr><td><code>${a.key}</code></td><td>${a.label}</td><td>${a.group}</td><td>${a.type}</td><td>${a.unit || '-'}</td><td>${a.key === 'cpu' ? '✓' : ''}</td><td>${a.filterable ? '✓' : ''}</td></tr>`).join('') || '<tr><td colspan="7" class="muted">Chưa có thuộc tính. Chọn Laptop, Điện thoại hoặc Tai nghe để xem mẫu.</td></tr>'}</table>
        ${note('Không xóa hoặc đổi kiểu thuộc tính đã có sản phẩm dùng. Thuộc tính "lọc được" sinh bộ lọc kèm số đếm ở trang danh mục.')}</div></div>`;
  });
  adminRoute(
    '/admin/brands',
    'products:write',
    () => `<div class="page-head"><h1>Thương hiệu</h1><button class="btn" data-act="toastMsg" data-msg="Mở form thêm thương hiệu">Thêm thương hiệu</button></div>
      <table class="tbl"><tr><th>Logo</th><th>Tên</th><th>Slug</th><th class="num">Sản phẩm</th></tr>${D.brands.map((b) => `<tr><td><span class="badge dark">${b[0]}</span></td><td>${b}</td><td>${PK.norm(b).replace(/\s+/g, '-')}</td><td class="num">${PK.S.products.filter((p) => p.brand === b).length}</td></tr>`).join('')}</table>`,
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
      manual: 'Điều chỉnh tay',
      order_reserve: 'Giữ cho đơn',
      order_release: 'Trả từ đơn',
      return: 'Khách trả hàng',
    };
    return `<div class="page-head"><h1>Tồn kho</h1><button class="btn" data-act="stockAdjust">Điều chỉnh tồn</button></div>
      <div class="grid cols-2" style="align-items:start">
        <div class="card"><h3>Sắp hết hàng (≤ 5)</h3><table class="tbl"><tr><th>SKU</th><th>Sản phẩm</th><th class="num">Tồn</th></tr>${lowStock()
          .map(
            (x) =>
              `<tr><td>${x.v.sku}</td><td>${esc(x.p.name)}</td><td class="num"><span class="badge ${x.v.stock ? 'warn' : 'sale'}">${x.v.stock}</span></td></tr>`,
          )
          .join('')}</table></div>
        <div class="card"><h3>Lịch sử xuất nhập</h3><table class="tbl small"><tr><th>Thời gian</th><th>SKU</th><th class="num">+/−</th><th>Loại</th><th>Đơn hoặc người</th><th>Lý do</th></tr>${S.stockMoves.map((m) => `<tr><td>${m.at}</td><td>${m.sku}</td><td class="num">${m.delta > 0 ? '+' : ''}${m.delta}</td><td>${TYPE[m.type]}</td><td>${esc(m.ref)}</td><td>${esc(m.reason)}</td></tr>`).join('')}</table></div>
      </div>`;
  });
  act('stockAdjust', () => {
    const skus = PK.S.products.flatMap((p) =>
      (p.variants || [{ sku: p.slug.toUpperCase() }]).map((v) => v.sku),
    );
    modal(`<h2>Điều chỉnh tồn kho</h2><form data-form="stockAdjust" class="stack"><label class="field"><span>SKU</span><select name="sku">${skus.map((s) => `<option>${s}</option>`).join('')}</select></label>
      <div class="grid cols-2"><label class="field"><span>Số lượng (+ nhập, − xuất)</span><input name="delta" type="number" value="5"></label><label class="field"><span>Loại</span><select name="type"><option value="manual">Điều chỉnh tay</option><option value="return">Khách trả hàng</option></select></label></div>
      <label class="field"><span>Lý do (bắt buộc)</span><input name="reason" placeholder="Nhập lô hàng tháng 10"></label>
      <div class="row"><button class="btn">Lưu</button><button type="button" class="btn secondary" data-act="closeModal">Hủy</button></div></form>`);
  });
  form('stockAdjust', (data) => {
    if (!String(data.reason).trim()) return toast('Cần nhập lý do điều chỉnh');
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

  // ================= commerce: kiểm duyệt review =================
  adminRoute('/admin/reviews', 'reviews:moderate', () => {
    const list = [...PK.S.reviews].sort((a, b) => b.reports - a.reports);
    return `<div class="page-head"><h1>Kiểm duyệt review</h1></div><table class="tbl"><tr><th>Sản phẩm</th><th>Người viết</th><th>Sao</th><th>Nội dung</th><th class="num">Báo cáo</th><th>Trạng thái</th><th></th></tr>
      ${list
        .map(
          (
            r,
          ) => `<tr><td>${esc(PK.prod(r.pid).name)}</td><td>${esc(r.user)}</td><td>${r.rating}★</td><td style="max-width:320px">${esc(r.body)}</td><td class="num">${r.reports >= 3 ? `<span class="badge sale">${r.reports}</span>` : r.reports}</td><td>${r.status === 'visible' ? '<span class="badge ok">Hiện</span>' : '<span class="badge gray">Đã ẩn</span>'}</td>
        <td><button class="btn sm secondary" data-act="reviewToggle" data-id="${r.id}">${r.status === 'visible' ? 'Ẩn' : 'Hiện'}</button></td></tr>`,
        )
        .join('')}</table>
      ${note('Review bị báo cáo từ 3 lần nổi lên đầu. Ẩn hoặc hiện đều cần lý do, điểm trung bình sản phẩm được tính lại.')}`;
  });
  act('reviewToggle', (d) => {
    const reason = prompt('Lý do (bắt buộc)', 'Spam, chứa thông tin liên hệ');
    if (!reason) return;
    const r = PK.S.reviews.find((x) => x.id === d.id);
    r.status = r.status === 'visible' ? 'hidden' : 'visible';
    audit('review.' + (r.status === 'hidden' ? 'hide' : 'show'), 'review:' + r.id, reason);
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
    const head = `<div class="page-head"><h1>CSKH</h1><div class="row"><a class="btn secondary sm" href="#/admin/support/canned">Câu trả lời mẫu</a><a class="btn secondary sm" href="#/admin/support/metrics">Chỉ số CSKH</a></div></div>
      <div class="tabs"><a href="#/admin/support" class="${tab === 'waiting' ? 'on' : ''}">Đang chờ (${lists.waiting.length})</a><a href="#/admin/support?tab=mine" class="${tab === 'mine' ? 'on' : ''}">Của tôi (${lists.mine.length})</a><a href="#/admin/support?tab=callbacks" class="${tab === 'callbacks' ? 'on' : ''}">Gọi lại (${S.callbacks.filter((c) => c.status === 'pending').length})</a></div>`;
    if (tab === 'callbacks') {
      const ST = {
        pending: '<span class="badge warn">Chờ gọi</span>',
        done: '<span class="badge ok">Đã gọi</span>',
        unreachable: '<span class="badge gray">Không liên lạc được</span>',
      };
      return (
        head +
        `<table class="tbl"><tr><th>SĐT</th><th>Ghi chú</th><th>Hội thoại</th><th>Tạo lúc</th><th>Trạng thái</th><th></th></tr>${S.callbacks.map((c) => `<tr><td>${c.phone}</td><td>${esc(c.note)}</td><td>${c.conv}</td><td>${c.createdAt}</td><td>${ST[c.status]}</td><td>${c.status === 'pending' ? `<div class="row"><button class="btn sm" data-act="cbSet" data-id="${c.id}" data-s="done">Đã gọi</button><button class="btn sm ghost" data-act="cbSet" data-id="${c.id}" data-s="unreachable">Không liên lạc được</button></div>` : ''}</td></tr>`).join('')}</table>${note('SĐT tự xóa sau 30 ngày và không bao giờ gửi sang AI. Khách để lại tối đa 3 yêu cầu mỗi ngày.')}`
      );
    }
    const list = lists[tab];
    const cur = S.convs.find((c) => c.id === q.c) || list[0];
    const queue =
      list
        .map(
          (c) =>
            `<div class="qitem ${cur && cur.id === c.id ? 'on' : ''}" data-act="nav" data-href="#/admin/support?tab=${tab}&c=${c.id}"><div class="row between"><b class="small">${esc(c.customer)}</b><span class="small muted">${c.status === 'waiting_human' ? 'chờ ' + c.waitingSince + ' phút' : ''}</span></div><div class="small muted">${esc(c.reason)}</div>${c.waitingSince >= S.supportSettings.nudge ? '<span class="badge sale">Chờ quá 10 phút</span>' : ''}</div>`,
        )
        .join('') || '<div class="qitem muted small">Không có hội thoại.</div>';
    let center = '<div style="padding:24px" class="muted">Chọn một hội thoại.</div>';
    let right = '';
    if (cur) {
      const mine = cur.status === 'human' && cur.assignedTo === meId;
      const canReply = mine && PK.can('conversations:reply');
      const draftOk = !PK.S.aiDown && PK.S.budgetPct < 100;
      center = `<div style="padding:12px;border-bottom:1px solid var(--color-border)" class="row between"><b>${esc(cur.customer)}</b><div class="row">
          ${cur.status === 'waiting_human' && PK.can('conversations:reply') ? `<button class="btn sm" data-act="claim" data-id="${cur.id}">Nhận</button>` : ''}
          ${mine ? `<button class="btn sm secondary" data-act="transfer" data-id="${cur.id}">Chuyển</button><button class="btn sm danger" data-act="endSession" data-id="${cur.id}">Kết thúc</button>` : ''}</div></div>
        <div class="chat-body" style="background:#fff">${cur.messages.map(([role, text]) => `<div class="msg ${role === 'assistant' ? 'staff' : role}" style="${role === 'assistant' ? 'background:var(--color-ai-soft)' : ''}"><div class="who">${{ user: 'Khách', assistant: 'Trợ lý AI', staff: 'Nhân viên', system: '' }[role]}</div>${esc(text)}</div>`).join('')}</div>
        ${
          canReply
            ? `<form data-form="staffReply" data-id="${cur.id}" style="padding:12px;border-top:1px solid var(--color-border)" class="stack">
            <textarea name="text" id="composer" placeholder="Nhập trả lời. Gõ / để chọn câu mẫu." data-input="slash" style="min-height:70px"></textarea>
            <div id="slash" class="hidden"></div>
            <div class="row"><button type="button" class="btn sm secondary" data-act="draft" data-id="${cur.id}" ${draftOk ? '' : 'disabled'}>${draftOk ? 'Gợi ý trả lời' : 'Tạm không khả dụng'}</button><span class="spacer"></span><button class="btn sm">Gửi</button></div>
            <div id="draft-src" class="small muted"></div></form>`
            : `<div style="padding:12px" class="small muted">${cur.status === 'waiting_human' ? 'Nhấn "Nhận" để trả lời. Ai nhận trước thì người sau thấy "Đã có người nhận".' : 'Chỉ nhân viên đã nhận hội thoại mới gửi được (403).'}</div>`
        }`;
      const cust = cur.userId ? S.users.find((u) => u.id === cur.userId) : null;
      const orders = cur.userId ? S.orders.filter((o) => o.userId === cur.userId) : [];
      right = `<div style="padding:12px" class="stack"><h3>Khách hàng</h3>${cust ? `<p class="small"><b>${esc(cust.name)}</b><br>${esc(cust.email)}</p>` : '<p class="small muted">Khách vãng lai (theo anonymousId)</p>'}
        <div><b class="small">Đơn gần đây</b>${orders.map((o) => `<div class="small"><a href="#/admin/orders/${o.code}">${o.code}</a> ${PK.orderBadge(o.status)}</div>`).join('') || '<div class="small muted">Không có</div>'}</div>
        <div><b class="small">Bot đã gợi ý</b>${(cur.suggested || []).map((id) => `<div class="small">${esc(PK.prod(id).name)}</div>`).join('') || '<div class="small muted">Không có</div>'}</div>
        <div><b class="small">Đang xem trang</b><div class="small">${esc(cur.page)}</div></div>
        <p class="small muted">Không hiển thị hồ sơ sở thích của khách.</p></div>`;
    }
    return (
      head +
      `${S.presence === 'away' ? '<div class="alert warn" style="margin-bottom:12px">Bạn đang Tạm vắng nên không nhận chat mới.</div>' : ''}
      <div class="desk"><div class="col" style="overflow-y:auto">${queue}</div><div class="col">${center}</div><div class="col">${right}</div></div>
      ${note('Thử luồng đầy đủ: ở cửa hàng mở chat, bấm "Gặp nhân viên" (hội thoại "(bạn)" xuất hiện ở đây) → đổi vai trò CSKH → Nhận → Gợi ý trả lời → Gửi → đổi lại vai trò khách để thấy tin nhắn → Kết thúc để khách chấm CSAT.')}`
    );
  });
  act('claim', (d) => {
    const c = PK.S.convs.find((x) => x.id === d.id);
    if (c.assignedTo) return toast('Đã có người nhận');
    c.status = 'human';
    c.assignedTo = PK.me().id;
    c.messages.push(['system', PK.me().name + ' đã nhận hội thoại']);
    if (c.id === 'c-live') {
      PK.S.chat.status = 'human';
      PK.S.chat.messages.push({ role: 'system', text: 'Lan - CSKH đã tham gia hội thoại.' });
    }
    audit('conversation.claim', 'conversation:' + c.id, '');
    PK.go('/admin/support?tab=mine&c=' + c.id);
  });
  act('draft', () => {
    const el = document.getElementById('composer');
    el.value =
      'Chào bạn, Pickory hỗ trợ đổi trả trong 7 ngày nếu sản phẩm lỗi do nhà sản xuất. Bạn cho em xin mã đơn để kiểm tra giúp nhé.';
    document.getElementById('draft-src').innerHTML =
      'Nguồn bản nháp: Chính sách đổi trả > Thời hạn đổi trả · Quy trình xử lý khiếu nại (nội bộ). AI không tự gửi, bạn sửa rồi bấm Gửi.';
    el.focus();
  });
  input('slash', (el) => {
    const box = document.getElementById('slash');
    if (!el.value.startsWith('/')) return box.classList.add('hidden');
    box.className = 'card small';
    box.innerHTML =
      PK.S.canned
        .filter((c) => c.shortcut.startsWith(el.value.split(' ')[0]))
        .map(
          (c) =>
            `<div style="padding:4px;cursor:pointer" data-act="useCanned" data-body="${esc(c.body)}"><b>${c.shortcut}</b> ${esc(c.body)}</div>`,
        )
        .join('') || 'Không có câu mẫu khớp';
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
        staffName: PK.me().name.replace(' CSKH', '') + ' - CSKH',
      });
    PK.render();
  });
  act('transfer', (d) => {
    const others = PK.S.users.filter(
      (u) =>
        u.kind === 'staff' &&
        u.id !== PK.me().id &&
        u.roles.some((r) => r === 'support') &&
        !u.lockedAt,
    );
    modal(`<h2>Chuyển hội thoại</h2><form data-form="transfer" data-id="${d.id}" class="stack"><label class="field"><span>Đồng nghiệp đang sẵn sàng</span><select name="to">${others.map((u) => `<option value="${u.id}">${esc(u.name)}</option>`).join('')}</select></label>
      <label class="field"><span>Ghi chú nội bộ</span><textarea name="note" placeholder="Khách hỏi hoàn tiền, đã kiểm tra giao dịch"></textarea></label><div class="row"><button class="btn">Chuyển</button><button type="button" class="btn secondary" data-act="closeModal">Hủy</button></div></form>`);
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
        text: 'Phiên hỗ trợ đã kết thúc. Bạn chấm điểm giúp Pickory nhé:',
        form: 'csat',
      });
    }
    audit('conversation.close', 'conversation:' + c.id, '');
    toast('Đã kết thúc phiên, khách nhận form CSAT');
    PK.go('/admin/support');
  });
  act('cbSet', (d) => {
    PK.S.callbacks.find((c) => c.id === d.id).status = d.s;
    PK.render();
  });
  adminRoute(
    '/admin/support/canned',
    'conversations:read',
    () => `<div class="breadcrumb"><a href="#/admin/support">CSKH</a> / Câu trả lời mẫu</div><div class="page-head"><h1>Câu trả lời mẫu</h1><button class="btn" data-act="toastMsg" data-msg="Mở form thêm câu mẫu">Thêm câu mẫu</button></div>
      <table class="tbl"><tr><th>Phím tắt</th><th>Nội dung</th><th>Phạm vi</th></tr>${PK.S.canned.map((c) => `<tr><td><code>${c.shortcut}</code></td><td>${esc(c.body)}</td><td>${c.scope}</td></tr>`).join('')}</table>${note('Biến hỗ trợ: {ten_khach}, {ma_don}. Gõ / trong ô trả lời để chọn.')}`,
  );
  adminRoute('/admin/support/metrics', 'conversations:read', () => {
    const k = [
      ['Chuyển cho người', '128'],
      ['Trong giờ', '83%'],
      ['Phản hồi đầu p50 / p90', '48 giây / 3 phút 10'],
      ['Xử lý p50 / p90', '6 / 19 phút'],
      ['CSAT', '4,6 (92 lượt)'],
      ['Gọi lại hoàn thành', '27 / 31'],
      ['Tỉ lệ dùng bản nháp AI', '61%'],
    ];
    return `<div class="breadcrumb"><a href="#/admin/support">CSKH</a> / Chỉ số</div><h1>Chỉ số CSKH 30 ngày</h1><div class="grid cols-4">${k.map(([l, v]) => `<div class="kpi"><div class="l">${l}</div><div class="v">${v}</div></div>`).join('')}</div>`;
  });

  // ================= knowledge-base =================
  const KB_ST = {
    draft: '<span class="badge gray">Nháp</span>',
    published: '<span class="badge ok">Đã đăng</span>',
    archived: '<span class="badge gray">Lưu trữ</span>',
  };
  adminRoute('/admin/knowledge', 'knowledge:read', () => {
    const w = PK.can('knowledge:write');
    return `<div class="page-head"><h1>Kho tri thức</h1><div class="row"><a class="btn secondary" href="#/admin/knowledge-test">Thử truy xuất</a>${w ? '<button class="btn secondary" data-act="kbImport">Nhập DOCX/PDF</button><a class="btn" href="#/admin/knowledge/new">Viết tài liệu</a>' : ''}</div></div>
      ${PK.S.aiDown ? '<div class="alert err" style="margin-bottom:12px">Lập chỉ mục phiên bản mới của "Chính sách đổi trả" thất bại (lỗi embedding). Phiên bản cũ vẫn đang được dùng. <button class="btn sm" data-act="toastMsg" data-msg="Đã xếp hàng lập chỉ mục lại">Thử lại</button></div>' : ''}
      <table class="tbl"><tr><th>Tiêu đề</th><th>Slug</th><th>Loại</th><th>Trạng thái</th><th>Thẻ</th><th>Hiệu lực</th><th>Cập nhật</th></tr>
      ${PK.S.kbDocs.map((d) => `<tr class="click" data-act="nav" data-href="#/admin/knowledge/${d.slug}"><td><b>${esc(d.title)}</b></td><td class="small">${d.slug}</td><td>${d.type === 'internal' ? '<span class="badge dark">internal</span>' : d.type}</td><td>${KB_ST[d.status]}</td><td>${d.tags.join(', ')}</td><td>${d.effectiveFrom}</td><td>${d.updatedAt}</td></tr>`).join('')}</table>
      ${note('Tài liệu internal chỉ nhân viên và bản nháp AI của CSKH dùng, chatbot khách không bao giờ thấy. Đăng bản mới thì chatbot dùng trong 1 phút, không có lúc trống nội dung.')}`;
  });
  adminRoute('/admin/knowledge/:slug', 'knowledge:read', (p) => {
    const d = PK.S.kbDocs.find((x) => x.slug === p.slug) || {
      slug: '',
      title: '',
      type: 'faq',
      status: 'draft',
      tags: [],
      effectiveFrom: '',
      body: [['Tiêu đề mục', 'Nội dung']],
    };
    const w = PK.can('knowledge:write');
    const md = '# ' + d.title + '\n\n' + d.body.map(([h, t]) => `## ${h}\n${t}`).join('\n\n');
    return `<div class="breadcrumb"><a href="#/admin/knowledge">Kho tri thức</a> / ${esc(d.title || 'Tài liệu mới')}</div>
      <div class="page-head"><h1>${esc(d.title || 'Tài liệu mới')} ${KB_ST[d.status]}</h1>${w ? `<div class="row"><button class="btn secondary" data-act="kbSet" data-slug="${d.slug}" data-s="draft">Lưu nháp</button><button class="btn" data-act="kbSet" data-slug="${d.slug}" data-s="published">Đăng</button><button class="btn secondary" data-act="kbSet" data-slug="${d.slug}" data-s="draft">Gỡ đăng</button><button class="btn ghost" data-act="kbSet" data-slug="${d.slug}" data-s="archived">Lưu trữ</button></div>` : '<span class="badge gray">Chỉ xem</span>'}</div>
      <div class="grid cols-4" style="margin-bottom:12px"><label class="field"><span>Slug</span><input value="${d.slug}" ${w ? '' : 'disabled'}></label><label class="field"><span>Loại</span><select ${w ? '' : 'disabled'}>${['policy', 'faq', 'guide', 'internal'].map((t) => `<option ${t === d.type ? 'selected' : ''}>${t}</option>`).join('')}</select></label><label class="field"><span>Thẻ</span><input value="${d.tags.join(', ')}" ${w ? '' : 'disabled'}></label><label class="field"><span>Hiệu lực từ</span><input type="date" value="${d.effectiveFrom}" ${w ? '' : 'disabled'}></label></div>
      <div class="grid cols-2"><textarea style="min-height:320px;font-family:monospace" ${w ? '' : 'disabled'} data-input="mdPreview">${esc(md)}</textarea><div class="md-preview" id="md-preview">${renderMd(md)}</div></div>
      <div class="card" style="margin-top:16px"><h3>Lịch sử phiên bản</h3><table class="tbl small"><tr><th>Phiên bản</th><th>Người đăng</th><th>Thời gian</th></tr><tr><td>v3 (đang dùng)</td><td>Hoàng Nam</td><td>${d.updatedAt} 10:00</td></tr><tr><td>v2</td><td>Hoàng Nam</td><td>2026-07-01 09:00</td></tr></table></div>
      ${note('Ngày hiệu lực ở tương lai thì chưa được truy xuất cho tới ngày đó.')}`;
  });
  function renderMd(md) {
    return esc(md)
      .replace(/^## (.*)$/gm, '<h3>$1</h3>')
      .replace(/^# (.*)$/gm, '<h2>$1</h2>')
      .replace(/\n\n/g, '<br>');
  }
  input('mdPreview', (el) => {
    document.getElementById('md-preview').innerHTML = renderMd(el.value);
  });
  act('kbSet', (d) => {
    const doc = PK.S.kbDocs.find((x) => x.slug === d.slug);
    if (!doc) {
      toast('Đã lưu tài liệu mới dạng nháp');
      return PK.go('/admin/knowledge');
    }
    doc.status = d.s;
    doc.updatedAt = PK.now().slice(0, 10);
    audit('knowledge.' + d.s, 'doc:' + doc.slug, '');
    toast(
      d.s === 'published'
        ? 'Đã đăng. Đang lập chỉ mục phiên bản mới, chatbot dùng trong 1 phút.'
        : 'Đã cập nhật trạng thái',
    );
    PK.render();
  });
  act('kbImport', () => {
    modal(`<h2>Nhập tài liệu</h2><p>Chọn DOCX hoặc PDF có lớp chữ, tối đa 10 MB. Kết quả luôn là bản nháp, hiển thị Markdown cạnh bản gốc.</p>
      <div class="card muted-bg" style="text-align:center;padding:32px">Kéo thả file vào đây</div>
      <div class="row" style="margin-top:12px"><button class="btn" data-act="kbImportOk">Giả lập: nhập "Chinh-sach-tra-gop.docx"</button><button class="btn secondary" data-act="kbImportScan">Giả lập: PDF scan</button></div>`);
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
    toast('PDF không có lớp chữ (bản scan). Hệ thống không hỗ trợ OCR, hãy dùng file gốc.'),
  );
  adminRoute('/admin/knowledge-test', 'knowledge:read', (_, q) => {
    const question = q.q || 'đổi trả trong bao lâu';
    const hits = [
      [
        'Chính sách đổi trả > Thời hạn đổi trả',
        'Khách được đổi trả trong 7 ngày kể từ ngày nhận hàng nếu sản phẩm lỗi do nhà sản xuất...',
        0.82,
      ],
      [
        'Chính sách đổi trả > Điều kiện đổi trả',
        'Sản phẩm còn đủ phụ kiện, hộp và hóa đơn...',
        0.71,
      ],
      [
        'Quy trình xử lý khiếu nại > Bước 2 (internal)',
        'Đề xuất đổi mới nếu lỗi trong 7 ngày...',
        0.58,
      ],
      [
        'Chính sách bảo hành > Thời gian bảo hành',
        'Theo thời gian của hãng ghi trên trang sản phẩm...',
        0.31,
      ],
    ];
    return `<div class="breadcrumb"><a href="#/admin/knowledge">Kho tri thức</a> / Thử truy xuất</div><h1>Thử truy xuất</h1>
      <form data-form="kbTest" class="row" style="margin-bottom:16px"><input name="q" value="${esc(question)}" style="max-width:420px"><button class="btn">Truy xuất</button></form>
      <table class="tbl"><tr><th>Mục (headingPath)</th><th>Đoạn</th><th class="num">Điểm</th></tr>${hits.map(([h, t, s]) => `<tr style="${s < 0.35 ? 'opacity:.45' : ''}"><td><b>${h}</b></td><td>${t}</td><td class="num">${s}${s < 0.35 ? ' <span class="badge gray">bị loại</span>' : ''}</td></tr>`).join('')}</table>
      ${note('Kết quả dưới 0,35 bị loại. Không còn đoạn nào thì chatbot trả lời "không có thông tin". Đoạn internal chỉ dùng cho bản nháp CSKH.')}`;
  });
  form('kbTest', (data) => PK.go('/admin/knowledge-test?q=' + encodeURIComponent(data.q)));

  // ================= personalized-content: nội dung AI =================
  const AC_ST = {
    active: '<span class="badge ok">active</span>',
    pending_approval: '<span class="badge warn">chờ duyệt</span>',
    hidden: '<span class="badge gray">hidden</span>',
    failed: '<span class="badge sale">failed</span>',
    expired: '<span class="badge gray">expired</span>',
  };
  adminRoute('/admin/ai-content', 'products:read', (_, q) => {
    const list = PK.S.aiContents.filter(
      (c) => (!q.kind || c.kind === q.kind) && (!q.status || c.status === q.status),
    );
    const w = PK.can('products:write');
    return `<div class="page-head"><h1>Nội dung AI</h1><a class="btn secondary" href="#/admin/ai-content/static">Banner tĩnh</a></div>
      ${PK.S.budgetPct >= 100 ? '<div class="alert warn" style="margin-bottom:12px">Ngân sách AI đã chạm 100%: không tạo nội dung mới, nội dung đang có vẫn hiển thị.</div>' : ''}
      <div class="row" style="margin-bottom:12px">${['', 'banner', 'review_summary', 'highlights'].map((k) => `<a class="chip" style="${(q.kind || '') === k ? 'border-color:var(--color-ink);font-weight:700' : ''}" href="#/admin/ai-content${k ? '?kind=' + k : ''}">${k || 'Tất cả'}</a>`).join('')}</div>
      <table class="tbl"><tr><th>Loại</th><th>Đối tượng</th><th>Persona</th><th>Nội dung</th><th>Prompt</th><th>Tạo lúc</th><th>Trạng thái</th><th></th></tr>
      ${list
        .map(
          (
            c,
          ) => `<tr><td>${c.kind}</td><td>${esc(c.target)}</td><td>${c.persona}</td><td style="max-width:280px">${c.title ? `<b>${esc(c.title)}</b><div class="small muted">${esc(c.subtitle)}</div>` : '<span class="muted small">Xem chi tiết</span>'}${c.failReason ? `<div class="small" style="color:var(--color-sale-ink)">${esc(c.failReason)}</div>` : ''}</td><td class="small">${c.promptVersion}</td><td>${c.generatedAt}</td><td>${AC_ST[c.status]}</td>
        <td>${w ? `<div class="row">${c.status === 'pending_approval' ? `<button class="btn sm" data-act="acSet" data-id="${c.id}" data-s="active">Duyệt</button>` : ''}${c.status === 'active' ? `<button class="btn sm secondary" data-act="acSet" data-id="${c.id}" data-s="hidden">Ẩn</button>` : ''}<button class="btn sm ghost" data-act="acRegen" data-id="${c.id}">Tạo lại</button></div>` : ''}</td></tr>`,
        )
        .join('')}</table>
      ${note('Banner do job hằng tuần tạo, phải được duyệt mới hiện (chờ quá 7 ngày thì hết hạn). Duyệt banner "Góc chiến game" rồi bật cá nhân hóa ở cửa hàng để thấy banner có nhãn "Gợi ý bởi AI". Kiểm tra chống bịa: mọi con số phải có trong thông số, cấm "tốt nhất", "số 1", "rẻ nhất", AI không viết giá.')}`;
  });
  act('acSet', (d) => {
    const c = PK.S.aiContents.find((x) => x.id === d.id);
    audit('ai_content.' + d.s, 'content:' + c.id, `${c.status} → ${d.s}`);
    c.status = d.s;
    toast(d.s === 'active' ? 'Đã duyệt, banner hiện cho persona phù hợp' : 'Đã ẩn');
    PK.render();
  });
  act('acRegen', (d) => {
    if (PK.S.budgetPct >= 100 || PK.S.aiDown)
      return toast('Không tạo lại được: ngân sách AI đã hết hoặc AI đang lỗi');
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
    () => `<div class="breadcrumb"><a href="#/admin/ai-content">Nội dung AI</a> / Banner tĩnh</div><div class="page-head"><h1>Banner tĩnh</h1><button class="btn" data-act="toastMsg" data-msg="Mở form thêm banner tĩnh">Thêm banner</button></div>
      <table class="tbl"><tr><th>Tiêu đề</th><th>Link</th><th>Vị trí</th><th>Từ</th><th>Đến</th></tr><tr><td>Laptop gaming RTX 4060 giảm đến 4 triệu</td><td>/c/laptop-gaming</td><td>1</td><td>2026-10-01</td><td>2026-10-31</td></tr><tr><td>Tai nghe chống ồn cho mọi chuyến đi</td><td>/search?q=tai nghe chống ồn</td><td>2</td><td>2026-10-01</td><td>2026-11-15</td></tr></table>${note('Banner tĩnh là phương án dự phòng khi không có banner AI cho persona.')}`,
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
    return `<div class="page-head"><h1>Gợi ý sản phẩm</h1></div>
      <div class="grid cols-2" style="align-items:start">
        <div class="card stack"><h3>Thí nghiệm A/B</h3>
          <form data-form="experiment" class="stack">${[
            ['control', 'Nhóm đối chứng (Bán chạy)'],
            ['algo', 'Thuật toán'],
            ['llm', 'Thuật toán + AI xếp hạng'],
          ]
            .map(
              ([k, l]) =>
                `<label class="field"><span>${l}: <b>${e[k]}%</b></span><input type="range" min="0" max="100" name="${k}" value="${e[k]}" ${canEdit ? '' : 'disabled'}></label>`,
            )
            .join('')}
          ${canEdit ? '<button class="btn sm">Lưu chia nhóm</button>' : '<span class="small muted">Cần ai-settings:manage để sửa</span>'}</form>
          <p class="small muted">Đặt đối chứng 0% là tắt thí nghiệm. Tổng phải bằng 100%.</p></div>
        <div class="card"><h3>Cặp danh mục mua kèm</h3><table class="tbl"><tr><th>Danh mục</th><th>Mua kèm</th><th class="num">Trọng số</th><th>Mua lại được</th></tr>${S.complements.map((c) => `<tr><td>${c.from}</td><td>${c.to}</td><td class="num">${c.weight}</td><td>${c.repurchasable ? '✓' : ''}</td></tr>`).join('')}</table></div>
      </div>
      <div class="card" style="margin-top:16px"><h3>Hiệu quả theo vị trí và nhóm (30 ngày)</h3><table class="tbl"><tr><th>Vị trí</th><th>Nhóm</th><th class="num">CTR</th><th class="num">Chuyển đổi</th></tr>${rows.map((r) => `<tr><td>${r[0]}</td><td>${r[1]}</td><td class="num">${r[2]}</td><td class="num">${r[3]}</td></tr>`).join('')}</table>
        <p class="small muted">Giai đoạn 17/09 đến 18/09 bị đánh dấu do ngân sách hết (algo_llm chạy như algo), không tính vào so sánh.</p></div>`;
  });
  form('experiment', (data) => {
    const v = { control: Number(data.control), algo: Number(data.algo), llm: Number(data.llm) };
    if (v.control + v.algo + v.llm !== 100)
      return toast('Tổng ba nhóm phải bằng 100% (hiện ' + (v.control + v.algo + v.llm) + '%)');
    PK.S.experiment = v;
    audit('recommendations.experiment', 'settings', JSON.stringify(v));
    toast('Đã lưu chia nhóm');
    PK.render();
  });

  // ================= smart-search: thống kê tìm kiếm =================
  adminRoute('/admin/search', 'reports:read', () => {
    const si = D.searchInsights;
    return `<div class="page-head"><h1>Tìm kiếm</h1>${PK.can('products:write') ? '<button class="btn secondary" data-act="toastMsg" data-msg="Đã xếp hàng lập chỉ mục lại 27 sản phẩm (chạy nền)">Lập chỉ mục lại</button>' : ''}</div>
      <div class="grid cols-4" style="margin-bottom:16px">${[
        ['Lượt tìm 30 ngày', '8.412'],
        ['Không có kết quả', '3,1%'],
        ['CTR từ kết quả', '11,8%'],
        ['Câu phải gọi AI', '22%'],
      ]
        .map(
          ([l, v]) => `<div class="kpi"><div class="l">${l}</div><div class="v">${v}</div></div>`,
        )
        .join('')}</div>
      <div class="grid cols-2"><div class="card"><h3>Câu tìm nhiều nhất</h3><table class="tbl"><tr><th>Câu tìm</th><th class="num">Lượt</th><th class="num">CTR</th></tr>${si.top.map((r) => `<tr><td>${r[0]}</td><td class="num">${r[1]}</td><td class="num">${r[2]}</td></tr>`).join('')}</table></div>
      <div class="card"><h3>Không có kết quả</h3><table class="tbl"><tr><th>Câu tìm</th><th class="num">Lượt</th></tr>${si.zero.map((r) => `<tr><td>${r[0]}</td><td class="num">${r[1]}</td></tr>`).join('')}</table><p class="small muted">Gợi ý nhập thêm hàng hoặc thêm từ đồng nghĩa.</p></div></div>`;
  });

  // ================= admin-console: báo cáo =================
  adminRoute('/admin/reports', 'reports:read', (_, q) => {
    const tab = q.tab || 'revenue';
    const range = `<div class="row"><select style="width:auto">${['Hôm nay', '7 ngày', '30 ngày', 'Tháng này', 'Tùy chọn'].map((r) => `<option ${r === '30 ngày' ? 'selected' : ''}>${r}</option>`).join('')}</select><select style="width:auto"><option>Theo ngày</option><option>Theo tuần</option><option>Theo tháng</option></select><button class="btn secondary sm" data-act="exportReport">Xuất CSV</button></div>`;
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
      body = `<div class="grid cols-4" style="margin-bottom:16px">${[
        ['Doanh thu ghi nhận', money(sum)],
        ['Số đơn', rd.reduce((s, x) => s + x.orders, 0)],
        ['Giá trị TB đơn', money(Math.round(sum / rd.reduce((s, x) => s + x.orders, 0)))],
        ['Tỉ lệ hủy', '6,4%'],
      ]
        .map(
          ([l, v]) =>
            `<div class="kpi"><div class="l">${l}</div><div class="v" style="font-size:20px">${v}</div></div>`,
        )
        .join('')}</div>
        <div class="card"><h3>Doanh thu theo ngày</h3>${barChart(
          rd.map((x) => x.revenue),
          rd.map((x) => String(x.day)),
          { fmt: money },
        )}
        <details><summary class="small">Bảng dữ liệu</summary><table class="tbl small">${rd
          .slice(0, 10)
          .map(
            (x) =>
              `<tr><td>Ngày ${x.day}</td><td class="num">${money(x.revenue)}</td><td class="num">${x.orders} đơn</td></tr>`,
          )
          .join('')}</table></details></div>
        <div class="grid cols-3" style="margin-top:16px"><div class="card"><h3>Hủy đơn</h3><p>Khách hủy 2,9% · Shop hủy 1,1% · Quá hạn 2,4%</p></div><div class="card"><h3>Thanh toán</h3><p>SePay 64% · COD 36%</p></div><div class="card"><h3>Phí ship shop chịu</h3><p>${money(4820000)}</p></div></div>`;
    }
    if (tab === 'ai') {
      const groups = [
        ['control', 1840, '2,1%', '0,8%', money(41200)],
        ['algo', 8210, '3,4%', '1,2%', money(58900)],
        ['algo_llm', 8190, '4,0%', '1,4%', money(64100)],
      ];
      body = `<div class="card"><h3>Gợi ý: so sánh nhóm</h3><table class="tbl"><tr><th>Nhóm</th><th class="num">Số khách</th><th class="num">CTR</th><th class="num">Chuyển đổi</th><th class="num">Doanh thu / khách</th></tr>${groups
        .map(
          (g) =>
            `<tr><td>${g[0]}</td>${g
              .slice(1)
              .map((x) => `<td class="num">${x}</td>`)
              .join('')}</tr>`,
        )
        .join('')}</table>
          <p class="small">algo_llm so với algo: chuyển đổi +0,2 điểm, khoảng tin cậy 95% [−0,1; +0,5] → <b>Chưa đủ dữ liệu để kết luận</b>.</p></div>
        <div class="grid cols-2" style="margin-top:16px"><div class="card"><h3>Tìm kiếm</h3><p>8.412 lượt · không kết quả 3,1% · CTR 11,8% · 22% câu gọi AI</p></div><div class="card"><h3>Chatbot</h3><p>1.230 hội thoại · thích 84% · chuyển CSKH 10% · cờ grounding 1,8% · chuyển đổi từ chat 3,6%</p></div></div>
        <div class="card" style="margin-top:16px"><h3>Chi phí AI</h3><p>${PK.S.budgetPct}% ngân sách tháng · 0,021 USD cho mỗi đơn được quy cho AI. <a href="#/admin/ai">Xem chi tiết</a></p></div>`;
    }
    if (tab === 'support')
      body = `<div class="grid cols-4">${[
        ['Chuyển cho người', '128'],
        ['Trong giờ / ngoài giờ', '106 / 22'],
        ['Phản hồi đầu p50', '48 giây'],
        ['CSAT', '4,6'],
      ]
        .map(
          ([l, v]) => `<div class="kpi"><div class="l">${l}</div><div class="v">${v}</div></div>`,
        )
        .join('')}</div>`;
    return `<div class="page-head"><h1>Báo cáo</h1>${range}</div>${tabs}${body}${note('Mỗi biểu đồ có bảng dữ liệu bên dưới. Xuất cần quyền reports:export (sales xem được nhưng không xuất được), tối đa 50.000 dòng.')}`;
  });
  act('exportReport', () => {
    if (!PK.can('reports:export')) return toast('Cần quyền reports:export (403)');
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
      return `<div class="page-head"><h1>AI</h1></div>${tabs}
        <form data-form="aiSettings" class="card stack" style="max-width:520px">
          <label class="field"><span>Ngân sách tháng (USD)</span><input name="budget" type="number" step="1" value="${S.aiSettings.budget}" ${canEdit ? '' : 'disabled'}></label>
          <label class="field"><span>Hạn mức chat mỗi ngày: người đã đăng nhập</span><input name="userLimit" type="number" value="${S.aiSettings.userLimit}" ${canEdit ? '' : 'disabled'}></label>
          <label class="field"><span>Hạn mức chat mỗi ngày: khách vãng lai</span><input name="guestLimit" type="number" value="${S.aiSettings.guestLimit}" ${canEdit ? '' : 'disabled'}></label>
          ${canEdit ? '<button class="btn">Lưu</button>' : '<div class="alert">Bạn chỉ có quyền xem (thiếu ai-settings:manage). Gọi PUT sẽ nhận 403.</div>'}
        </form>
        <div class="card" style="margin-top:16px;max-width:720px"><h3>Bậc ưu tiên khi vượt ngân sách</h3><table class="tbl"><tr><th>Mức dùng</th><th>Hành vi</th></tr><tr><td>&lt; 80%</td><td>Bình thường</td></tr><tr><td>80% đến &lt; 100%</td><td>Cảnh báo trên dashboard</td></tr><tr><td>≥ 100%</td><td>Dừng personalized-content.*, recommendations.rerank, support-desk.draft-reply</td></tr><tr><td>≥ 120%</td><td>Dừng cả chat, tìm kiếm, embedding</td></tr></table></div>`;
    }
    const daily = D.aiDaily;
    const spent = (S.aiSettings.budget * S.budgetPct) / 100;
    return `<div class="page-head"><h1>AI</h1></div>${tabs}
      ${S.budgetPct >= 80 ? `<div class="alert warn" style="margin-bottom:12px">Đã dùng ${S.budgetPct}% ngân sách tháng (${spent.toFixed(2)} / ${S.aiSettings.budget} USD).</div>` : ''}
      ${S.aiDown ? '<div class="alert err" style="margin-bottom:12px">Circuit breaker DeepSeek đang mở (5 lỗi liên tiếp). Lời gọi bị từ chối ngay trong 60 giây.</div>' : ''}
      <div class="grid cols-4" style="margin-bottom:16px">${[
        ['Chi phí tháng', spent.toFixed(2) + ' USD'],
        ['Lượt gọi', '13.870'],
        ['Tỉ lệ lỗi', '0,6%'],
        ['Độ trễ p95', '2,9 giây'],
      ]
        .map(
          ([l, v]) => `<div class="kpi"><div class="l">${l}</div><div class="v">${v}</div></div>`,
        )
        .join('')}</div>
      <div class="card"><h3>Chi phí theo ngày (USD)</h3>${barChart(
        daily.map((x) => x.cost),
        daily.map((x) => String(x.day)),
        { highlight: (i) => daily[i].errors > 30, fmt: (v) => v + ' USD' },
      )}<p class="small muted">Cột đỏ: ngày có tỉ lệ lỗi cao (ngày 17).</p></div>
      <div class="card" style="margin-top:16px"><h3>Theo tính năng</h3><table class="tbl"><tr><th>Feature</th><th class="num">Chi phí (USD)</th><th class="num">Lượt gọi</th><th class="num">Tỉ lệ lỗi</th><th class="num">p95 (giây)</th></tr>${D.aiByFeature.map((r) => `<tr><td><code>${r[0]}</code></td><td class="num">${r[1]}</td><td class="num">${r[2]}</td><td class="num">${r[3]}%</td><td class="num">${r[4]}</td></tr>`).join('')}</table>
        <p class="small muted">Log không chứa nội dung prompt, chỉ có token, chi phí, độ trễ, trạng thái ok/error/blocked. Chi phí TB mỗi hội thoại: 0,008 USD.</p></div>`;
  });
  form('aiSettings', (data) => {
    const v = {
      budget: Number(data.budget),
      userLimit: Number(data.userLimit),
      guestLimit: Number(data.guestLimit),
    };
    if (Object.values(v).some((x) => !Number.isFinite(x) || x < 0))
      return toast('Giá trị không hợp lệ (400)');
    audit('ai.settings_update', 'ai_settings', `budget ${PK.S.aiSettings.budget} → ${v.budget}`);
    PK.S.aiSettings = v;
    toast('Đã lưu, có hiệu lực ở lần gọi kế tiếp');
    PK.render();
  });

  // ================= identity: khách hàng =================
  adminRoute('/admin/customers', 'customers:read', (_, q) => {
    const kw = PK.norm(q.q || '');
    const list = PK.S.users.filter(
      (u) => u.kind === 'customer' && (!kw || PK.norm(u.name + u.email + u.phone).includes(kw)),
    );
    const canManage = PK.can('customers:manage');
    return `<div class="page-head"><h1>Khách hàng</h1></div><form data-form="custSearch" class="row" style="margin-bottom:12px"><input name="q" value="${esc(q.q || '')}" placeholder="Tìm theo tên, email, SĐT" style="max-width:320px"><button class="btn secondary sm">Tìm</button></form>
      <table class="tbl"><tr><th>Tên</th><th>Email</th><th>SĐT</th><th>Ngày tạo</th><th>Trạng thái</th><th></th></tr>
      ${list
        .map(
          (
            u,
          ) => `<tr><td>${esc(u.name)}</td><td>${esc(u.email)} ${u.verified ? '' : '<span class="badge gray">Chưa xác thực</span>'}</td><td>${esc(u.phone)}</td><td>${u.createdAt}</td><td>${u.lockedAt ? '<span class="badge sale">Đã khóa</span>' : '<span class="badge ok">Hoạt động</span>'}</td>
        <td>${canManage ? `<button class="btn sm secondary" data-act="custLock" data-id="${u.id}">${u.lockedAt ? 'Mở khóa' : 'Khóa'}</button>` : '<span class="small muted">Chỉ xem</span>'}</td></tr>`,
        )
        .join('')}</table>
      ${note('Không bao giờ trả passwordHash. Khóa khách thì mọi phiên của khách mất hiệu lực ngay. Sales và CSKH xem được nhưng khóa bị 403.')}`;
  });
  form('custSearch', (data) => PK.go('/admin/customers?q=' + encodeURIComponent(data.q)));
  act('custLock', (d) => {
    const u = PK.S.users.find((x) => x.id === d.id);
    u.lockedAt = u.lockedAt ? null : PK.now();
    audit(u.lockedAt ? 'customer.lock' : 'customer.unlock', 'user:' + u.id, '');
    toast(u.lockedAt ? 'Đã khóa, mọi phiên của khách bị xóa' : 'Đã mở khóa');
    PK.render();
  });

  // ================= identity: nhân viên =================
  const roleName = (code) => PK.S.roles.find((r) => r.code === code)?.name || code;
  adminRoute('/admin/staff', 'staff:read', () => {
    const staff = PK.S.users.filter((u) => u.kind === 'staff');
    const m = PK.can('staff:manage');
    return `<div class="page-head"><h1>Nhân viên</h1>${m ? '<button class="btn" data-act="invite">Mời nhân viên</button>' : ''}</div>
      <table class="tbl"><tr><th>Tên</th><th>Email</th><th>Role</th><th>Trạng thái</th><th></th></tr>
      ${staff
        .map(
          (
            u,
          ) => `<tr><td><b>${esc(u.name)}</b>${u.id === PK.me().id ? ' <span class="badge">Bạn</span>' : ''}</td><td>${esc(u.email)}</td><td>${u.roles.map((r) => `<span class="badge">${roleName(r)}</span>`).join(' ')}</td><td>${u.pending ? '<span class="badge warn">Chờ đặt mật khẩu</span>' : u.lockedAt ? '<span class="badge sale">Đã khóa</span>' : '<span class="badge ok">Hoạt động</span>'}</td>
        <td>${m ? `<div class="row"><button class="btn sm secondary" data-act="assignRoles" data-id="${u.id}">Gán role</button><button class="btn sm ghost" data-act="staffLock" data-id="${u.id}">${u.lockedAt ? 'Mở khóa' : 'Khóa'}</button></div>` : ''}</td></tr>`,
        )
        .join('')}</table>
      ${note('Quy tắc an toàn: không khóa hoặc gỡ quyền admin cuối cùng, không tự sửa role của mình, không gán "Khách hàng" cùng role nhân viên. Khóa hoặc gỡ hết role thì mọi phiên bị xóa ngay. Mỗi thao tác ghi 1 dòng nhật ký.')}`;
  });
  act('invite', () => {
    modal(`<h2>Mời nhân viên</h2><form data-form="invite" class="stack"><label class="field"><span>Email</span><input name="email" type="email" placeholder="hoa.sales@pickory.vn"></label><label class="field"><span>Tên</span><input name="name" placeholder="Nguyễn Hoa"></label>
      <div><span class="small muted">Role</span>${PK.S.roles
        .filter((r) => r.code !== 'customer')
        .map(
          (r) =>
            `<label class="row"><input type="checkbox" name="roles" value="${r.code}" ${r.code === 'sales' ? 'checked' : ''}> ${r.name}</label>`,
        )
        .join('')}</div>
      <div class="row"><button class="btn">Gửi lời mời</button><button type="button" class="btn secondary" data-act="closeModal">Hủy</button></div></form>`);
  });
  form('invite', (data) => {
    const roles = [].concat(data.roles || []);
    if (!/.+@.+\..+/.test(data.email)) return toast('Email không hợp lệ');
    if (PK.S.users.some((u) => u.kind === 'customer' && u.email === data.email))
      return toast('Email này đang là tài khoản khách, không mời được');
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
      `<h2>Đã gửi lời mời</h2><div class="card muted-bg"><div class="small muted">Xem trước email gửi tới ${esc(data.email)}</div><b>Bạn được mời tham gia back-office Pickory</b><p class="small">Đặt mật khẩu trong 72 giờ (dùng 1 lần): <a href="#/reset?invite=1" data-act="closeModal">pickory.vn/reset-password?t=…</a></p></div><button class="btn" style="margin-top:12px" data-act="closeModal">Đóng</button>`,
    );
    PK.render();
  });
  const activeAdmins = () =>
    PK.S.users.filter((u) => u.kind === 'staff' && u.roles.includes('admin') && !u.lockedAt);
  act('assignRoles', (d) => {
    const u = PK.S.users.find((x) => x.id === d.id);
    if (u.id === PK.me().id) return toast('Không được tự sửa role của chính mình');
    modal(`<h2>Gán role cho ${esc(u.name)}</h2><form data-form="assignRoles" data-id="${u.id}" class="stack">${PK.S.roles
      .filter((r) => r.code !== 'customer')
      .map(
        (r) =>
          `<label class="row"><input type="checkbox" name="roles" value="${r.code}" ${u.roles.includes(r.code) ? 'checked' : ''}> ${r.name}</label>`,
      )
      .join('')}
      <p class="small muted">Có hiệu lực ở request tiếp theo của nhân viên. Gỡ hết role thì mọi phiên bị xóa.</p><div class="row"><button class="btn">Lưu</button><button type="button" class="btn secondary" data-act="closeModal">Hủy</button></div></form>`);
  });
  form('assignRoles', (data, f) => {
    const u = PK.S.users.find((x) => x.id === f.dataset.id);
    const roles = [].concat(data.roles || []);
    if (u.roles.includes('admin') && !roles.includes('admin') && activeAdmins().length <= 1)
      return toast('Không gỡ được quyền admin cuối cùng');
    audit(
      'role.assign',
      'user:' + u.id,
      `${u.roles.join(', ')} → ${roles.join(', ') || '(trống)'}`,
    );
    u.roles = roles;
    closeModal();
    toast(roles.length ? 'Đã cập nhật role' : 'Đã gỡ hết role, mọi phiên bị xóa');
    PK.render();
  });
  act('staffLock', (d) => {
    const u = PK.S.users.find((x) => x.id === d.id);
    if (u.id === PK.me().id) return toast('Không tự khóa chính mình');
    if (!u.lockedAt && u.roles.includes('admin') && activeAdmins().length <= 1)
      return toast('Không khóa được admin cuối cùng');
    u.lockedAt = u.lockedAt ? null : PK.now();
    audit(u.lockedAt ? 'staff.lock' : 'staff.unlock', 'user:' + u.id, '');
    toast(u.lockedAt ? 'Đã khóa, phiên cũ nhận 401 ở request tiếp theo' : 'Đã mở khóa');
    PK.render();
  });

  // ================= identity: role và quyền =================
  adminRoute('/admin/roles', 'roles:manage', () => {
    const S = PK.S;
    const roles = S.roles.filter((r) => r.code !== 'customer');
    const has = (r, p) => r.perms === 'ALL' || r.perms.includes(p);
    return `<div class="page-head"><h1>Role và quyền</h1><button class="btn" data-act="newRole">Tạo role</button></div>
      <table class="tbl" style="margin-bottom:24px"><tr><th>Role</th><th>Mã</th><th>Loại</th><th class="num">Người giữ</th><th></th></tr>
      ${S.roles
        .map(
          (
            r,
          ) => `<tr><td><b>${r.name}</b></td><td><code>${r.code}</code></td><td>${r.isSystem ? '<span class="badge dark">Hệ thống 🔒</span>' : '<span class="badge">Tùy chỉnh</span>'}</td><td class="num">${S.users.filter((u) => u.roles.includes(r.code)).length}</td>
        <td>${r.isSystem ? '<span class="small muted">Không sửa, không xóa</span>' : `<div class="row"><a class="btn sm secondary" href="#/admin/roles/${r.code}">Sửa quyền</a><button class="btn sm ghost" data-act="deleteRole" data-code="${r.code}">Xóa</button></div>`}</td></tr>`,
        )
        .join('')}</table>
      <h2>Ma trận quyền</h2><div class="table-wrap"><table class="tbl perm-grid"><tr><th>Quyền</th>${roles.map((r) => `<th>${r.name}</th>`).join('')}</tr>
      ${D.permissionGroups.map((g) => `<tr class="grp"><td colspan="${roles.length + 1}">${g.label}</td></tr>` + g.perms.map((p) => `<tr><td><code>${p}</code></td>${roles.map((r) => `<td>${has(r, p) ? '<span style="color:var(--color-success)">✓</span>' : '<span class="muted">−</span>'}</td>`).join('')}</tr>`).join('')).join('')}</table></div>
      ${note('Admin có mọi quyền ngầm định, kể cả quyền thêm sau này. Danh mục quyền do module identity giữ (đã duyệt lệch spec).')}`;
  });
  adminRoute('/admin/roles/:code', 'roles:manage', (p) => {
    const r = PK.S.roles.find((x) => x.code === p.code);
    if (!r || r.isSystem) return '<h1>Role hệ thống không sửa được</h1>';
    return `<div class="breadcrumb"><a href="#/admin/roles">Role và quyền</a> / ${r.name}</div><h1>${r.name}</h1>
      <form data-form="saveRole" data-code="${r.code}" class="stack"><label class="field" style="max-width:360px"><span>Tên role</span><input name="name" value="${esc(r.name)}"></label>
      <div class="grid cols-2">${D.permissionGroups.map((g) => `<div class="card"><b>${g.label}</b>${g.perms.map((pm) => `<label class="row small" style="padding:3px 0"><input type="checkbox" name="perms" value="${pm}" ${r.perms.includes(pm) ? 'checked' : ''}> <code>${pm}</code></label>`).join('')}</div>`).join('')}</div>
      <div class="row"><button class="btn">Lưu</button><a class="btn secondary" href="#/admin/roles">Hủy</a></div></form>`;
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
    toast('Đã lưu. Nhân viên giữ role thấy thay đổi ngay ở request kế tiếp.');
    PK.go('/admin/roles');
  });
  act('newRole', () => {
    modal(
      `<h2>Tạo role</h2><form data-form="newRole" class="stack"><label class="field"><span>Tên role</span><input name="name" value="Quản lý kho 2"></label><label class="field"><span>Mã</span><input name="code" value="quan-ly-kho-2"></label><div class="row"><button class="btn">Tạo và chọn quyền</button><button type="button" class="btn secondary" data-act="closeModal">Hủy</button></div></form>`,
    );
  });
  form('newRole', (data) => {
    if (PK.S.roles.some((r) => r.code === data.code)) return toast('Mã role đã tồn tại');
    PK.S.roles.push({ code: data.code, name: data.name, isSystem: false, perms: [] });
    audit('role.create', 'role:' + data.code, '');
    closeModal();
    PK.go('/admin/roles/' + data.code);
  });
  act('deleteRole', (d) => {
    const holders = PK.S.users.filter((u) => u.roles.includes(d.code)).length;
    if (holders) return toast(`Không xóa được: còn ${holders} người giữ role này`);
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
    return `<div class="page-head"><h1>Nhật ký thao tác</h1></div>
      <div class="row" style="margin-bottom:12px"><select style="width:auto" data-change="auditFilter" data-key="actor"><option value="">Mọi người</option>${actors.map((a) => `<option ${q.actor === a ? 'selected' : ''}>${a}</option>`).join('')}</select>
        <select style="width:auto" data-change="auditFilter" data-key="action"><option value="">Mọi hành động</option>${actions.map((a) => `<option ${q.action === a ? 'selected' : ''}>${a}</option>`).join('')}</select><input type="date" style="width:auto"></div>
      <table class="tbl"><tr><th>Thời gian</th><th>Người</th><th>Hành động</th><th>Đối tượng</th><th>Thay đổi</th></tr>${list.map((a) => `<tr><td>${a.at}</td><td>${esc(a.actor)}</td><td><code>${a.action}</code></td><td>${esc(a.target)}</td><td class="small">${esc(a.diff)}</td></tr>`).join('')}</table>
      ${note('Chỉ thêm, không có thao tác sửa hay xóa. Trường mật khẩu và token bị loại khỏi before/after. Các thao tác bạn làm trong prototype cũng được ghi ở đây.')}`;
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
        'Ngưỡng miễn phí ship, hạn mức COD, tài khoản SePay',
        '#/admin/settings/commerce',
        'commerce-settings:manage',
      ],
      [
        'Vận chuyển',
        'Địa chỉ lấy hàng, GHN ShopId, dịch vụ mặc định',
        '#/admin/settings/shipping',
        'shipping-settings:manage',
      ],
      [
        'CSKH',
        'Giờ làm việc, ngày nghỉ, nhắc chờ, tự đóng phiên',
        '#/admin/settings/support',
        'support-settings:manage',
      ],
      ['AI', 'Ngân sách tháng, hạn mức chat', '#/admin/ai?tab=settings', 'ai-settings:manage'],
      [
        'Gợi ý',
        'Chia nhóm thí nghiệm, cặp mua kèm',
        '#/admin/recommendations',
        'ai-settings:manage',
      ],
    ].filter((x) => PK.can(x[3]));
    return `<h1>Cấu hình</h1><div class="grid cols-3">${items.map(([t, d, h]) => `<a class="card" href="${h}" style="text-decoration:none"><b>${t}</b><p class="small muted" style="margin:6px 0 0">${d}</p></a>`).join('') || '<p class="muted">Bạn không có quyền cấu hình nào.</p>'}</div>`;
  });
  adminRoute('/admin/settings/commerce', 'commerce-settings:manage', () => {
    const c = PK.S.commerceSettings;
    return `<div class="breadcrumb"><a href="#/admin/settings">Cấu hình</a> / Bán hàng</div><h1>Cấu hình bán hàng</h1><form data-form="commerceSettings" class="card stack" style="max-width:520px">
      <label class="field"><span>Miễn phí ship cho đơn từ (đ)</span><input name="freeShip" type="number" value="${c.freeShip}"></label><label class="field"><span>Hạn mức COD tối đa (đ)</span><input name="codMax" type="number" value="${c.codMax}"></label>
      <label class="field"><span>Số tài khoản SePay</span><input name="account" value="${c.account}"></label><label class="field"><span>Ngân hàng</span><input name="bank" value="${c.bank}"></label><button class="btn">Lưu</button></form>`;
  });
  form('commerceSettings', (data) => {
    Object.assign(PK.S.commerceSettings, {
      freeShip: Number(data.freeShip),
      codMax: Number(data.codMax),
      account: data.account,
      bank: data.bank,
    });
    audit('commerce.settings_update', 'commerce_settings', '');
    toast('Đã lưu');
  });
  adminRoute('/admin/settings/shipping', 'shipping-settings:manage', () => {
    const s = PK.S.shippingSettings;
    return `<div class="breadcrumb"><a href="#/admin/settings">Cấu hình</a> / Vận chuyển</div><h1>Cấu hình vận chuyển</h1><form data-form="shippingSettings" class="card stack" style="max-width:520px">
      <label class="field"><span>Tên điểm lấy hàng</span><input value="${esc(s.name)}"></label><label class="field"><span>SĐT</span><input value="${esc(s.phone)}"></label><label class="field"><span>Địa chỉ (tỉnh, phường, số nhà)</span><input value="${esc(s.address)}"></label>
      <div class="grid cols-2"><label class="field"><span>GHN ShopId</span><input value="${s.shopId}"></label><label class="field"><span>Dịch vụ mặc định</span><select><option>${s.service}</option><option>Nhanh</option></select></label></div><button class="btn">Lưu</button></form>${note('Chính sách giao: cho xem hàng, không cho thử. Phí thực tế khác phí báo giá thì shop chịu phần chênh.')}`;
  });
  form('shippingSettings', () => toast('Đã lưu'));
  adminRoute('/admin/settings/support', 'support-settings:manage', () => {
    const s = PK.S.supportSettings;
    return `<div class="breadcrumb"><a href="#/admin/settings">Cấu hình</a> / CSKH</div><h1>Cấu hình CSKH</h1><form data-form="supportSettings" class="card stack" style="max-width:520px">
      <div class="grid cols-2"><label class="field"><span>Bắt đầu (Asia/Ho_Chi_Minh)</span><input type="time" value="${s.start}"></label><label class="field"><span>Kết thúc</span><input type="time" value="${s.end}"></label></div>
      <label class="field"><span>Ngày nghỉ</span><input value="${esc(s.holidays)}"></label>
      <div class="grid cols-2"><label class="field"><span>Nhắc khi khách chờ quá (phút)</span><input type="number" value="${s.nudge}"></label><label class="field"><span>Tự đóng phiên không hoạt động (phút)</span><input type="number" value="${s.autoClose}"></label></div><button class="btn">Lưu</button></form>`;
  });
  form('supportSettings', () => toast('Đã lưu'));
})();
