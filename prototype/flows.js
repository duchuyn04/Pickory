/* Bản đồ luồng: điểm vào của prototype, liệt kê luồng của từng module kèm link tới đúng màn hình. */
/* eslint-disable */
(function () {
  const PK = window.PK;
  const { route, act } = PK;

  // [module, mô tả, [[tên luồng, vai trò, [[bước, link]]]]]
  const FLOWS = [
    [
      'identity',
      'Danh tính, phiên, RBAC, audit log, đồng ý thu thập dữ liệu',
      [
        [
          'Khách vãng lai đồng ý dữ liệu',
          'Khách vãng lai',
          [
            ['Banner đồng ý xuất hiện lần đầu', '#/'],
            ['Tùy chỉnh 3 lựa chọn', 'act:openConsent'],
          ],
        ],
        [
          'Đăng ký, đăng nhập, quên mật khẩu',
          'Khách vãng lai',
          [
            ['Đăng ký (lỗi ngay dưới ô, thước độ mạnh mật khẩu)', '#/register'],
            ['Đã gửi email xác thực', '#/verify-sent?email=ban@gmail.com'],
            ['Xác thực email thành công', '#/verify'],
            ['Đăng nhập (sai 5 lần bị khóa 15 phút)', '#/login'],
            ['Đăng nhập bằng Google', '#/login'],
            ['Quên mật khẩu', '#/forgot'],
            ['Đã gửi link đặt lại', '#/forgot-sent?email=ban@gmail.com'],
            ['Đặt lại mật khẩu', '#/reset'],
            ['Đăng nhập back-office (nhân viên)', '#/admin/login'],
          ],
        ],
        [
          'Tài khoản khách',
          'Khách hàng',
          [
            ['Thông tin', '#/account'],
            ['Quyền riêng tư và sở thích', '#/account?tab=privacy'],
            ['Sổ địa chỉ', '#/account?tab=addresses'],
            ['Xóa tài khoản', '#/account?tab=delete'],
          ],
        ],
        [
          'Admin mời nhân viên sales',
          'Admin',
          [
            ['Danh sách nhân viên, Mời', '#/admin/staff'],
            ['Nhân viên đặt mật khẩu (link 72 giờ)', '#/reset?invite=1'],
            ['Đăng nhập back-office', '#/admin/login'],
            ['Đổi vai trò Sales để xem menu bị giới hạn', '#/admin'],
          ],
        ],
        [
          'Admin tạo role "Quản lý kho"',
          'Admin',
          [
            ['Role và ma trận quyền', '#/admin/roles'],
            ['Tick quyền cho role', '#/admin/roles/quan-ly-kho'],
            ['Gán role, khóa nhân viên', '#/admin/staff'],
            ['Nhật ký thao tác', '#/admin/audit'],
          ],
        ],
        ['Tra cứu và khóa khách', 'Admin', [['Khách hàng', '#/admin/customers']]],
      ],
    ],
    [
      'catalog',
      'Sản phẩm, danh mục, thuộc tính, biến thể, tồn kho',
      [
        [
          'Khách duyệt sản phẩm',
          'Mọi vai trò',
          [
            ['Danh mục có bộ lọc và số đếm', '#/c/laptop'],
            ['Trang sản phẩm có biến thể', '#/p/laptop-asus-rog-strix-g16'],
            ['Sản phẩm ngừng kinh doanh', '#/p/laptop-lenovo-ideapad-slim-3'],
            ['So sánh', '#/compare?ids=p1,p2,p3'],
          ],
        ],
        [
          'Admin quản lý sản phẩm',
          'Admin',
          [
            ['Danh sách', '#/admin/products'],
            ['Form sản phẩm, biến thể', '#/admin/products/p1'],
            ['Danh mục và thuộc tính', '#/admin/categories'],
            ['Thương hiệu', '#/admin/brands'],
            ['Tồn kho, điều chỉnh, lịch sử', '#/admin/inventory'],
          ],
        ],
      ],
    ],
    [
      'smart-search',
      'Tìm kiếm ngôn ngữ tự nhiên, gợi ý tức thì',
      [
        [
          'Khách tìm bằng câu tự nhiên',
          'Khách vãng lai',
          [
            [
              '"laptop choi game duoi 25 trieu"',
              '#/search?q=laptop%20choi%20game%20duoi%2025%20trieu',
            ],
            ['Nới tiêu chí khi không có kết quả', '#/search?q=laptop%20gaming%20duoi%2015%20trieu'],
            ['Gõ mã model', '#/search?q=rog%20g16'],
            [
              'Bật "AI lỗi" để xem chế độ dự phòng',
              '#/search?q=tai%20nghe%20chong%20on%20di%20may%20bay',
            ],
          ],
        ],
        [
          'Admin xem thống kê',
          'Admin hoặc Sales',
          [['Câu tìm nhiều, không kết quả, lập chỉ mục lại', '#/admin/search']],
        ],
      ],
    ],
    [
      'recommendations',
      'Dành cho bạn, sản phẩm tương tự, mua kèm, thí nghiệm A/B',
      [
        [
          'Gợi ý theo đồng ý',
          'Khách vãng lai rồi Khách hàng',
          [
            ['Chưa đồng ý: "Bán chạy"', '#/'],
            ['Đồng ý rồi xem laptop gaming', '#/p/laptop-msi-katana-15'],
            ['Về trang chủ thấy "Dành cho bạn"', '#/'],
            ['Tương tự và mua kèm trên trang sản phẩm', '#/p/laptop-asus-rog-strix-g16'],
            ['Gợi ý trong giỏ', '#/cart'],
          ],
        ],
        [
          'Admin chỉnh thí nghiệm',
          'Admin',
          [['Chia nhóm, cặp mua kèm, hiệu quả', '#/admin/recommendations']],
        ],
      ],
    ],
    [
      'personalized-content',
      'Tóm tắt review, điểm nổi bật theo persona, banner AI',
      [
        [
          'Khách xem nội dung AI',
          'Khách hàng',
          [
            [
              '"Khách hàng nói gì" và "Điểm nổi bật" (persona gaming)',
              '#/p/laptop-asus-rog-strix-g16',
            ],
          ],
        ],
        [
          'Admin duyệt banner AI',
          'Admin',
          [
            ['Nội dung AI, duyệt banner "Góc chiến game"', '#/admin/ai-content'],
            ['Trang chủ có banner AI (khi đã đồng ý)', '#/'],
            ['Banner tĩnh dự phòng', '#/admin/ai-content/static'],
          ],
        ],
      ],
    ],
    [
      'commerce + shipping',
      'Giỏ hàng, checkout, SePay, COD, đơn hàng, vận đơn GHN, review',
      [
        [
          'Khách mua bằng SePay',
          'Khách vãng lai',
          [
            ['Thêm vào giỏ', '#/p/laptop-msi-katana-15'],
            ['Giỏ hàng', '#/cart'],
            ['Checkout 1 trang', '#/checkout'],
            ['Trang VietQR, giả lập webhook', 'note:Đặt hàng để mở'],
            ['Đặt hàng thành công, email', 'note:Sau khi thanh toán'],
          ],
        ],
        [
          'Sales xử lý đơn COD',
          'Sales',
          [
            ['Đơn chờ xác nhận', '#/admin/orders?tab=pending_confirmation'],
            ['Xác nhận, tạo vận đơn GHN', '#/admin/orders/PKC2R9GHMX'],
            ['Đã nhận COD', '#/admin/payments?tab=cod'],
          ],
        ],
        ['Đối soát SePay', 'Sales', [['Giao dịch chưa khớp, khớp tay', '#/admin/payments']]],
        [
          'Khách theo dõi đơn, review',
          'Khách hàng',
          [
            ['Đơn của tôi', '#/orders'],
            ['Đơn đang giao có timeline GHN', '#/orders/PK7KX2M9QD'],
            ['Viết review sau khi nhận hàng', '#/p/laptop-asus-rog-strix-g16'],
          ],
        ],
        [
          'Hoàn tiền, kiểm duyệt review',
          'Admin',
          [
            ['Đơn chờ hoàn tiền', '#/admin/orders/PKH6D3FBNC'],
            ['Kiểm duyệt review', '#/admin/reviews'],
            ['Cấu hình bán hàng, vận chuyển', '#/admin/settings'],
          ],
        ],
      ],
    ],
    [
      'activity-tracking',
      'Ghi hành vi khi có đồng ý, hồ sơ sở thích',
      [
        [
          'Khách xem và xóa hồ sơ',
          'Khách hàng',
          [
            ['Bật cá nhân hóa, xem vài sản phẩm', '#/c/laptop-gaming'],
            ['"Đã xem gần đây" ở trang chủ', '#/'],
            ['"Hệ thống hiểu bạn thích gì", Xóa lịch sử', '#/account?tab=privacy'],
          ],
        ],
      ],
    ],
    [
      'knowledge-base',
      'Kho tri thức, trung tâm hỗ trợ, RAG',
      [
        [
          'Khách đọc trung tâm hỗ trợ',
          'Mọi vai trò',
          [
            ['Trung tâm hỗ trợ', '#/help'],
            ['Chính sách đổi trả, mục lục', '#/help/chinh-sach-doi-tra'],
          ],
        ],
        [
          'Admin soạn và đăng',
          'Admin',
          [
            ['Danh sách tài liệu', '#/admin/knowledge'],
            ['Trình soạn Markdown có xem trước', '#/admin/knowledge/chinh-sach-doi-tra'],
            ['Nhập DOCX/PDF', '#/admin/knowledge'],
            ['Thử truy xuất', '#/admin/knowledge-test'],
          ],
        ],
      ],
    ],
    [
      'shopping-assistant',
      'Chatbot RAG có tool calling, thẻ sản phẩm, trích dẫn',
      [
        [
          'Hỏi tư vấn và mua từ chat',
          'Khách vãng lai',
          [
            ['Mở chat, hỏi "laptop chơi game tầm 25 triệu, nhẹ để mang đi học"', 'act:chatDemo'],
            ['Hỏi "so sánh 2 con này"', 'note:Gõ trong chat'],
            ['Thêm vào giỏ từ thẻ sản phẩm', 'note:Nút trên thẻ'],
            ['Hỏi "đổi trả trong bao lâu", bấm [1]', 'note:Gõ trong chat'],
          ],
        ],
        [
          'Hỏi đơn hàng',
          'Khách hàng',
          [['"đơn của tôi đến đâu rồi"', 'note:Gõ trong chat khi là Khách hàng']],
        ],
        [
          'Nhân viên xem lại hội thoại AI',
          'Admin hoặc CSKH',
          [['Tool đã gọi, trích dẫn, cờ grounding, phản hồi của khách', '#/admin/conversations']],
        ],
        [
          'Giới hạn và dự phòng',
          'Khách vãng lai',
          [
            ['15 tin mỗi ngày cho khách, 50 cho người đăng nhập', 'note:Báo khi còn 3 tin'],
            ['Bật "API AI đang lỗi": trợ lý tạm không khả dụng', 'note:Bảng Prototype'],
          ],
        ],
      ],
    ],
    [
      'support-desk',
      'Chuyển chat cho người thật, CSKH trả lời, gọi lại, CSAT',
      [
        [
          'Chuyển cho nhân viên và CSAT',
          'Khách vãng lai rồi CSKH',
          [
            ['Trong chat bấm "Gặp nhân viên"', 'act:chatDemo'],
            ['Đổi vai trò CSKH, Nhận hội thoại "(bạn)"', '#/admin/support'],
            ['Gợi ý trả lời (bản nháp AI), gõ / để chọn câu mẫu, Gửi', '#/admin/support'],
            ['Kết thúc, đổi lại khách để chấm CSAT', 'note:Mở lại chat'],
          ],
        ],
        [
          'Ngoài giờ: để lại SĐT',
          'Khách vãng lai',
          [
            ['Bật "Ngoài giờ CSKH", bấm "Gặp nhân viên"', 'act:chatDemo'],
            ['Tab Gọi lại của CSKH', '#/admin/support?tab=callbacks'],
          ],
        ],
        [
          'Quản lý',
          'Admin',
          [
            ['Câu trả lời mẫu', '#/admin/support/canned'],
            ['Chỉ số CSKH', '#/admin/support/metrics'],
            ['Giờ làm việc', '#/admin/settings/support'],
          ],
        ],
      ],
    ],
    [
      'ai-gateway',
      'Cửa gọi AI duy nhất: ngân sách, hạn mức, log chi phí',
      [
        [
          'Admin theo dõi và cấu hình',
          'Admin',
          [
            ['Chi phí theo ngày và tính năng', '#/admin/ai'],
            ['Ngân sách, hạn mức chat', '#/admin/ai?tab=settings'],
          ],
        ],
        [
          'Vượt ngân sách',
          'Mọi vai trò',
          [
            [
              'Chọn "Ngân sách AI 105%": nội dung AI không tạo mới, bản nháp CSKH tắt, chat vẫn chạy',
              'note:Thanh trên cùng',
            ],
            ['Chọn 125%: chat và tìm kiếm AI cũng dừng', 'note:Thanh trên cùng'],
          ],
        ],
      ],
    ],
    [
      'admin-console',
      'Khung back-office, trang chủ theo role, báo cáo',
      [
        [
          'So sánh menu theo role',
          'Admin, Sales, CSKH',
          [
            ['Trang chủ việc cần làm', '#/admin'],
            ['Báo cáo doanh thu', '#/admin/reports'],
            ['Hiệu quả AI (khoảng tin cậy)', '#/admin/reports?tab=ai'],
            ['Hub cấu hình', '#/admin/settings'],
          ],
        ],
      ],
    ],
  ];

  route('/flows', () => {
    const step = ([label, link]) => {
      if (link.startsWith('act:'))
        return `<li><a href="#" data-act="${link.slice(4)}">${label}</a></li>`;
      if (link.startsWith('note:'))
        return `<li>${label} <span class="muted small">(${link.slice(5)})</span></li>`;
      return `<li><a href="${link}">${label}</a></li>`;
    };
    return `<div style="max-width:760px;margin-bottom:32px"><h1 style="font-size:36px">Bản đồ luồng Pickory</h1>
      <p class="muted" style="font-size:16px">Prototype bấm được với dữ liệu giả lập, phủ 13 module trong <code>spec/</code>. Nút <b>Prototype</b> ở góc trái dưới cho phép đổi vai trò, giả lập AI lỗi, ngoài giờ CSKH, mức ngân sách AI và bật ghi chú cho người review. Mọi thay đổi (đơn, chat, duyệt banner) được nhớ giữa các màn hình.</p></div>
      <h3>Vào với vai trò</h3>
      <div class="grid" style="grid-template-columns:repeat(5,minmax(0,1fr));margin:12px 0 40px">
        ${[
          ['Khách vãng lai', 'guest', '#/'],
          ['Khách hàng Minh Anh', 'customer', '#/'],
          ['Admin', 'admin', '#/admin'],
          ['Sales', 'sales', '#/admin'],
          ['CSKH', 'support', '#/admin/support'],
        ]
          .map(
            ([l, r, h]) =>
              `<a class="todo" href="#" data-act="asRole" data-role="${r}" data-href="${h}"><div class="top">${PK.icon(r === 'guest' || r === 'customer' ? 'store' : r === 'support' ? 'headset' : 'users', 18)}${PK.icon('arrowRight', 16)}</div><b>${l}</b></a>`,
          )
          .join('')}
      </div>
      <div class="grid cols-2 flowmap">${FLOWS.map(
        ([
          mod,
          desc,
          flows,
        ]) => `<div class="card"><h3><code>${mod}</code></h3><p class="small muted">${desc}</p>
        ${flows.map(([name, role, steps]) => `<div style="margin-top:10px"><b class="small">${name}</b> <span class="badge gray">${role}</span><ol class="small">${steps.map(step).join('')}</ol></div>`).join('')}</div>`,
      ).join('')}</div>`;
  });
  act('asRole', (d) => {
    PK.S.role = d.role;
    location.hash = d.href.slice(1);
    PK.render();
    return false;
  });
  act('chatDemo', () => {
    PK.S.chat.open = true;
    PK.S.chat.noticeShown = true;
    if (location.hash !== '#/') location.hash = '/';
    else PK.render();
    return false;
  });
})();
