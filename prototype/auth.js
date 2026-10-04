/* identity: màn hình xác thực cho khách (đăng nhập, đăng ký, quên và đặt lại mật khẩu, xác thực email)
   và đăng nhập back-office cho nhân viên. Bố cục chia đôi: form một bên, panel thương hiệu một bên. */
/* eslint-disable */
(function () {
  const PK = window.PK;
  const { icon, esc, route, act, form, input, note, toast, modal, closeModal, productImg, money } =
    PK;

  // ---------- Khung chia đôi dùng chung ----------
  // Panel bên phải: lợi ích của tài khoản và ảnh sản phẩm, nền cát của thương hiệu
  function customerAside() {
    const picks = ['p1', 'p12', 'p17'].map(PK.prod);
    return `<aside class="auth-aside" aria-hidden="true">
      <div class="auth-collage">${picks.map((p, i) => `<div class="auth-tile t${i}"><div class="pic">${productImg(p, i === 0 ? 150 : 110)}</div><b>${esc(p.name.replace(/^(Laptop|Tai nghe) /, ''))}</b><span>${money(p.price)}</span></div>`).join('')}</div>
      <div class="auth-pitch">
        <h2>Mua đồ công nghệ dễ hơn với tài khoản Pickory</h2>
        <ul>
          <li>${icon('package', 20)}<span><b>Theo dõi đơn hàng</b>Xem trạng thái giao hàng GHN theo thời gian thực</span></li>
          <li>${icon('sparkle', 20)}<span><b>Gợi ý đúng nhu cầu</b>Khi bạn đồng ý, Pickory gợi ý theo sở thích của bạn</span></li>
          <li>${icon('pin', 20)}<span><b>Thanh toán nhanh hơn</b>Lưu tối đa 10 địa chỉ giao hàng</span></li>
          <li>${icon('chat', 20)}<span><b>Trợ lý AI 50 tin mỗi ngày</b>Thay vì 15 tin khi chưa đăng nhập</span></li>
        </ul>
      </div>
    </aside>`;
  }
  function staffAside() {
    return `<aside class="auth-aside staff" aria-hidden="true"><div class="auth-pitch">
      <h2>Back-office Pickory</h2>
      <ul>
        <li>${icon('package', 20)}<span><b>Đơn hàng và đối soát</b>Xác nhận đơn, tạo vận đơn GHN, khớp giao dịch SePay</span></li>
        <li>${icon('headset', 20)}<span><b>CSKH</b>Nhận hội thoại từ trợ lý AI, trả lời bằng bản nháp có nguồn</span></li>
        <li>${icon('chart', 20)}<span><b>Báo cáo</b>Doanh thu, hiệu quả AI và chất lượng CSKH</span></li>
      </ul>
      <p class="small" style="margin-top:32px;opacity:.8">${icon('lock', 14)} Mỗi thao tác thay đổi dữ liệu được ghi vào nhật ký.</p>
    </div></aside>`;
  }
  function shell(content, aside) {
    return `<div class="auth-shell">
      <main class="auth-main" id="main">
        <a class="logo" href="#/" aria-label="Pickory, về trang chủ">pickory<i></i></a>
        <div class="auth-box">${content}</div>
        <p class="xs muted">© 2026 Pickory · <a href="#/help/quyen-rieng-tu"><u>Quyền riêng tư</u></a> · <a href="#/help"><u>Hỗ trợ</u></a></p>
      </main>
      ${aside || customerAside()}
    </div>`;
  }
  PK.layouts.auth = (html) => shell(html);
  PK.authShell = shell;
  PK.staffAside = staffAside;

  // ---------- Thành phần form ----------
  const googleIcon = `<svg width="18" height="18" viewBox="0 0 24 24" aria-hidden="true"><path fill="#4285F4" d="M22.5 12.3c0-.8-.1-1.5-.2-2.2H12v4.2h5.9a5 5 0 0 1-2.2 3.3v2.7h3.6c2-1.9 3.2-4.7 3.2-8Z"/><path fill="#34A853" d="M12 23c3 0 5.5-1 7.3-2.7l-3.6-2.7c-1 .7-2.2 1-3.7 1-2.8 0-5.2-1.9-6.1-4.5H2.2v2.8A11 11 0 0 0 12 23Z"/><path fill="#FBBC05" d="M5.9 14.1a6.6 6.6 0 0 1 0-4.2V7.1H2.2a11 11 0 0 0 0 9.8l3.7-2.8Z"/><path fill="#EA4335" d="M12 5.4c1.6 0 3 .6 4.1 1.6l3.1-3.1A11 11 0 0 0 2.2 7.1l3.7 2.8C6.8 7.3 9.2 5.4 12 5.4Z"/></svg>`;
  const googleBtn = (label) =>
    `<button type="button" class="btn secondary block" data-act="googleLogin">${googleIcon} ${label}</button>`;
  // Ô nhập có nhãn, chỗ hiện lỗi ngay bên dưới (aria-describedby để trình đọc màn hình đọc lỗi)
  const field = (name, label, attrs = '', extra = '') =>
    `<label class="field"><span>${label}</span>${extra.replace('%INPUT%', `<input name="${name}" id="f-${name}" aria-describedby="e-${name}" ${attrs}>`) || `<input name="${name}" id="f-${name}" aria-describedby="e-${name}" ${attrs}>`}<small class="field-error" id="e-${name}" role="alert"></small></label>`;
  // Ô mật khẩu có nút hiện/ẩn
  const pwField = (name, label, autocomplete, hint) =>
    field(
      name,
      label,
      `type="password" autocomplete="${autocomplete}"`,
      `<span class="pw-wrap">%INPUT%<button type="button" class="pw-eye" data-act="togglePw" data-for="f-${name}" aria-label="Hiện mật khẩu">${icon('eye', 18)}</button></span>${hint || ''}`,
    );
  const seg = (on) =>
    `<div class="seg" role="tablist"><a href="#/login" role="tab" aria-selected="${on === 'login'}" class="${on === 'login' ? 'on' : ''}">Đăng nhập</a><a href="#/register" role="tab" aria-selected="${on === 'register'}" class="${on === 'register' ? 'on' : ''}">Tạo tài khoản</a></div>`;

  // Hiện lỗi ngay dưới từng ô, xóa lỗi cũ trước mỗi lần gửi
  function showErrors(f, errs) {
    f.querySelectorAll('.field-error').forEach((e) => (e.textContent = ''));
    f.querySelectorAll('[aria-invalid]').forEach((e) => e.removeAttribute('aria-invalid'));
    const banner = f.querySelector('.form-alert');
    if (banner) banner.remove();
    Object.entries(errs).forEach(([name, msg]) => {
      if (name === '_form') {
        f.insertAdjacentHTML(
          'afterbegin',
          `<div class="alert err form-alert" role="alert">${icon('alert', 18)}<span>${msg}</span></div>`,
        );
        return;
      }
      const el = f.querySelector(`[name="${name}"]`);
      const err = f.querySelector(`#e-${name}`);
      if (el) el.setAttribute('aria-invalid', 'true');
      if (err) err.innerHTML = msg;
    });
    const first = f.querySelector('[aria-invalid="true"]');
    if (first) first.focus();
    return Object.keys(errs).length > 0;
  }
  act('togglePw', (d, el) => {
    const inp = document.getElementById(d.for);
    const show = inp.type === 'password';
    inp.type = show ? 'text' : 'password';
    el.setAttribute('aria-label', show ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');
    el.innerHTML = icon(show ? 'eyeOff' : 'eye', 18);
  });
  // Đo độ mạnh mật khẩu: độ dài, chữ hoa, số, ký tự đặc biệt
  input('pwStrength', (el) => {
    const v = el.value;
    let score = 0;
    if (v.length >= 8) score++;
    if (v.length >= 12) score++;
    if (/[A-Z]/.test(v) && /[a-z]/.test(v)) score++;
    if (/\d/.test(v) && /[^A-Za-z0-9]/.test(v)) score++;
    const meter = document.getElementById('pw-meter');
    if (!meter) return;
    const label =
      v.length === 0
        ? 'Tối thiểu 8 ký tự'
        : v.length < 8
          ? 'Quá ngắn'
          : ['Yếu', 'Trung bình', 'Khá mạnh', 'Mạnh'][Math.min(score, 4) - 1] || 'Yếu';
    meter.dataset.score = v.length < 8 ? 0 : score;
    meter.querySelector('span').textContent = label;
  });
  const meter = `<div class="pw-meter" id="pw-meter" data-score="0"><i></i><i></i><i></i><i></i><span>Tối thiểu 8 ký tự</span></div>`;

  // ================= Đăng nhập =================
  let failCount = 0;
  route(
    '/login',
    (_, q) => `${seg('login')}
      <h1>Chào mừng trở lại</h1><p class="lead">Đăng nhập để theo dõi đơn hàng và nhận gợi ý phù hợp.</p>
      ${q.next === 'checkout' ? `<div class="alert" style="margin-bottom:16px">${icon('bag', 18)}<span>Giỏ hàng của bạn được giữ nguyên sau khi đăng nhập.</span></div>` : ''}
      ${googleBtn('Tiếp tục với Google')}<div class="or">hoặc dùng email</div>
      <form data-form="login" class="stack" novalidate>
        ${field('email', 'Email', 'type="email" autocomplete="email" value="minhanh@example.vn"')}
        ${pwField('password', '<span class="row between" style="margin:0">Mật khẩu <a href="#/forgot" class="small link" style="font-weight:500">Quên mật khẩu?</a></span>', 'current-password')}
        <label class="check small"><input type="checkbox" name="remember" checked> Ghi nhớ đăng nhập trên thiết bị này</label>
        <button class="btn block">Đăng nhập</button>
      </form>
      <p class="small muted center" style="margin-top:20px">Chưa có tài khoản? <a href="#/register" class="link">Tạo tài khoản</a></p>
      ${note('Mật khẩu đúng: 12345678. Sai 5 lần trong 15 phút thì lần 6 bị khóa 15 phút (429), kể cả nhập đúng. Thông báo lỗi không tiết lộ email có tồn tại hay không. Đăng nhập xong, giỏ hàng và lịch sử xem của phiên khách được gộp vào tài khoản.')}`,
    'auth',
  );
  form('login', (data, f) => {
    const errs = {};
    if (!/.+@.+\..+/.test(data.email || '')) errs.email = 'Nhập email hợp lệ, ví dụ ten@gmail.com';
    if (!data.password) errs.password = 'Nhập mật khẩu';
    if (showErrors(f, errs)) return;
    if (failCount >= 5)
      return showErrors(f, {
        _form:
          'Bạn đã nhập sai quá nhiều lần. Vui lòng thử lại sau 15 phút hoặc <a href="#/forgot"><u>đặt lại mật khẩu</u></a>.',
      });
    if (data.password !== '12345678') {
      failCount += 1;
      return showErrors(f, {
        _form: `Email hoặc mật khẩu không đúng.${failCount >= 3 ? ` Còn ${5 - failCount} lần thử trước khi bị khóa tạm thời.` : ''}`,
      });
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
    modal(`<div class="center">${googleIcon}<h2 style="margin-top:12px">Chọn tài khoản</h2><p class="muted">để tiếp tục tới Pickory</p></div>
      <button class="radio-card" style="width:100%;background:none;font:inherit;text-align:left;margin-top:8px" data-act="googlePick"><span class="avatar light">MA</span><span><b>Nguyễn Minh Anh</b><br><span class="small muted">minhanh@example.vn</span></span></button>
      <button class="radio-card" style="width:100%;background:none;font:inherit;text-align:left;margin-top:8px" data-act="googleStaff"><span class="avatar light">NA</span><span><b>Hoàng Nam</b><br><span class="small muted">nam.admin@pickory.vn</span></span></button>
      ${note('Email Google đã xác thực và trùng tài khoản khách thì tự liên kết. Email chưa xác thực thì không tự liên kết. Email thuộc nhân viên bị từ chối (thử dòng thứ hai).')}`);
  });
  act('googlePick', () => {
    closeModal();
    loginCustomer();
  });
  act('googleStaff', () => {
    closeModal();
    toast('Email này thuộc tài khoản nhân viên. Vui lòng đăng nhập tại back-office.');
  });

  // ================= Đăng ký =================
  route(
    '/register',
    () => `${seg('register')}
      <h1>Tạo tài khoản</h1><p class="lead">Chỉ mất 30 giây. Đơn và giỏ hàng hiện tại sẽ được giữ lại.</p>
      ${googleBtn('Đăng ký với Google')}<div class="or">hoặc dùng email</div>
      <form data-form="register" class="stack" novalidate>
        ${field('name', 'Họ và tên', 'autocomplete="name" placeholder="Nguyễn Văn A"')}
        ${field('email', 'Email', 'type="email" autocomplete="email" placeholder="ten@gmail.com"')}
        ${pwField('password', 'Mật khẩu', 'new-password', meter).replace('aria-describedby="e-password"', 'aria-describedby="e-password pw-meter" data-input="pwStrength"')}
        <label class="check small" style="align-items:flex-start"><input type="checkbox" name="personalization" style="margin-top:3px"> <span>Cho phép Pickory dùng lịch sử xem và mua để gợi ý sản phẩm phù hợp. Bạn đổi được bất cứ lúc nào.</span></label>
        <button class="btn block">Tạo tài khoản</button>
        <p class="xs muted center" style="margin:0">Bằng việc tạo tài khoản, bạn đồng ý với <a href="#/help" class="link">Điều khoản</a> và <a href="#/help/quyen-rieng-tu" class="link">Chính sách quyền riêng tư</a>.</p>
      </form>
      <p class="small muted center" style="margin-top:20px">Đã có tài khoản? <a href="#/login" class="link">Đăng nhập</a></p>
      ${note('Thử: để trống tên, mật khẩu dưới 8 ký tự, email minhanh@example.vn (đã có tài khoản) hoặc email @pickory.vn (thuộc nhân viên). Ô đồng ý cá nhân hóa mặc định tắt theo yêu cầu quyền riêng tư.')}`,
    'auth',
  );
  form('register', (data, f) => {
    const errs = {};
    if (!String(data.name || '').trim()) errs.name = 'Nhập họ và tên';
    if (!/.+@.+\..+/.test(data.email || '')) errs.email = 'Nhập email hợp lệ, ví dụ ten@gmail.com';
    else if (/@pickory\.vn$/i.test(data.email))
      errs.email = 'Email này không dùng được cho tài khoản khách hàng';
    else if (PK.S.users.some((u) => u.kind === 'customer' && u.email === data.email))
      errs.email =
        'Email đã có tài khoản. <a href="#/login" class="link">Đăng nhập</a> hoặc <a href="#/forgot" class="link">đặt lại mật khẩu</a>';
    if (String(data.password || '').length < 8) errs.password = 'Mật khẩu cần tối thiểu 8 ký tự';
    if (showErrors(f, errs)) return;
    const S = PK.S;
    S.role = 'customer';
    if (data.personalization) S.consent = { personalization: true, marketing: false, at: PK.now() };
    PK.go('/verify-sent?email=' + encodeURIComponent(data.email));
  });
  route(
    '/verify-sent',
    (_, q) => `<div class="auth-icon">${icon('send', 26)}</div><h1>Kiểm tra hộp thư của bạn</h1>
      <p class="lead">Chúng tôi đã gửi link xác thực tới <b style="color:var(--ink)">${esc(q.email || 'email của bạn')}</b>. Link có hiệu lực trong 24 giờ.</p>
      <a class="btn block" href="#/">Tiếp tục mua sắm</a>
      <p class="small muted center" style="margin-top:16px">Chưa nhận được? <button class="link small" data-act="toastMsg" data-msg="Đã gửi lại email xác thực">Gửi lại</button></p>
      ${note('Bạn đã được đăng nhập ngay sau khi đăng ký (không bắt xác thực email trước khi mua). Xem trước email: <a href="#/verify">pickory.vn/verify-email?t=…</a>')}`,
    'auth',
  );
  route(
    '/verify',
    () =>
      `<div class="auth-icon ok">${icon('check', 28)}</div><h1>Email đã được xác thực</h1><p class="lead">Cảm ơn bạn. Tài khoản Pickory đã sẵn sàng.</p><a class="btn block" href="#/">Bắt đầu mua sắm</a>`,
    'auth',
  );

  // ================= Quên và đặt lại mật khẩu =================
  route(
    '/forgot',
    () => `<a href="#/login" class="link small" style="margin-bottom:24px">${icon('chevronLeft', 16)} Quay lại đăng nhập</a>
      <h1>Quên mật khẩu?</h1><p class="lead">Nhập email đã đăng ký. Chúng tôi sẽ gửi link để bạn đặt mật khẩu mới.</p>
      <form data-form="forgot" class="stack" novalidate>${field('email', 'Email', 'type="email" autocomplete="email"')}<button class="btn block">Gửi link đặt lại</button></form>
      ${note('Luôn trả cùng một thông báo và cùng độ trễ dù email có tồn tại hay không, để không lộ ai có tài khoản.')}`,
    'auth',
  );
  form('forgot', (data, f) => {
    if (showErrors(f, /.+@.+\..+/.test(data.email || '') ? {} : { email: 'Nhập email hợp lệ' }))
      return;
    PK.go('/forgot-sent?email=' + encodeURIComponent(data.email));
  });
  route(
    '/forgot-sent',
    (_, q) => `<div class="auth-icon">${icon('send', 26)}</div><h1>Kiểm tra hộp thư</h1>
      <p class="lead">Nếu <b style="color:var(--ink)">${esc(q.email || '')}</b> có tài khoản Pickory, bạn sẽ nhận được link đặt lại mật khẩu trong vài phút. Link có hiệu lực 30 phút và chỉ dùng được một lần.</p>
      <a class="btn secondary block" href="#/login">Quay lại đăng nhập</a>
      ${note('Xem trước email: <a href="#/reset">pickory.vn/reset-password?t=…</a>')}`,
    'auth',
  );
  route(
    '/reset',
    (_, q) => `<h1>${q.invite ? 'Kích hoạt tài khoản nhân viên' : 'Đặt mật khẩu mới'}</h1>
      <p class="lead">${q.invite ? 'Chào mừng bạn đến với Pickory. Đặt mật khẩu để đăng nhập back-office.' : 'Mật khẩu mới sẽ đăng xuất tất cả thiết bị đang đăng nhập.'}</p>
      <form data-form="reset" class="stack" novalidate>
        ${pwField('password', 'Mật khẩu mới', 'new-password', meter).replace('aria-describedby="e-password"', 'aria-describedby="e-password pw-meter" data-input="pwStrength"')}
        ${pwField('confirm', 'Nhập lại mật khẩu', 'new-password')}
        <button class="btn block">Lưu mật khẩu</button></form>
      ${note(q.invite ? 'Trang dùng chung cho lời mời nhân viên (link hạn 72 giờ, dùng 1 lần).' : 'Link hết hạn hoặc đã dùng sẽ báo "Link không còn hiệu lực" kèm nút gửi lại.')}`,
    'auth',
  );
  form('reset', (data, f) => {
    const errs = {};
    if (String(data.password || '').length < 8) errs.password = 'Mật khẩu cần tối thiểu 8 ký tự';
    else if (data.confirm !== data.password) errs.confirm = 'Hai mật khẩu chưa khớp';
    if (showErrors(f, errs)) return;
    toast('Đã lưu mật khẩu mới');
    const { query } = PK.parseHash();
    PK.go(query.invite ? '/admin/login' : '/login');
  });

  // ================= Đăng nhập back-office (nhân viên) =================
  PK.staffLoginView = () =>
    shell(
      `<div class="badge" style="margin-bottom:16px">${icon('lock', 12)} Dành cho nhân viên</div>
      <h1>Đăng nhập back-office</h1><p class="lead">Dùng email công việc @pickory.vn. Back-office không hỗ trợ đăng nhập Google.</p>
      <form data-form="staffLogin" class="stack" novalidate>
        ${field('email', 'Email công việc', 'type="email" autocomplete="username" value="nam.admin@pickory.vn"')}
        ${pwField('password', 'Mật khẩu', 'current-password')}
        <button class="btn block">Đăng nhập</button></form>
      <p class="xs muted" style="margin-top:16px">Phiên làm việc kéo dài 12 giờ. Quên mật khẩu? Liên hệ quản trị viên để được gửi lại lời mời.</p>
      <div class="proto-note" style="display:block"><b>Đăng nhập nhanh</b> (mật khẩu 12345678)
        <div class="row tight" style="margin-top:8px"><button class="btn sm" data-act="quickStaff" data-role="admin">Admin</button><button class="btn sm secondary" data-act="quickStaff" data-role="sales">Sales</button><button class="btn sm secondary" data-act="quickStaff" data-role="support">CSKH</button></div></div>`,
      staffAside(),
    );
  form('staffLogin', (data, f) => {
    const role = {
      'nam.admin@pickory.vn': 'admin',
      'duc.sales@pickory.vn': 'sales',
      'lan.cskh@pickory.vn': 'support',
    }[data.email];
    if (!role || data.password !== '12345678')
      return showErrors(f, { _form: 'Email hoặc mật khẩu không đúng.' });
    PK.S.role = role;
    PK.go('/admin');
  });
})();
