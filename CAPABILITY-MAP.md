# Capability Map: Pickory

Hệ thống thương mại điện tử bán đồ điện tử, công nghệ tại Việt Nam, dùng AI (gọi API bên thứ ba) để cá nhân hóa trải nghiệm mua sắm.

## Giả định chung

1. Web app chạy trên trình duyệt desktop và mobile, chưa làm app native.
2. Frontend: React (Vite) + TypeScript. Backend: Node.js + Express + TypeScript. Chung một monorepo.
3. Database: PostgreSQL + Prisma ORM, lưu vector bằng pgvector.
4. Thanh toán: COD và chuyển khoản ngân hàng qua **SePay** (khách quét mã VietQR có mã đơn trong nội dung chuyển khoản, SePay gửi webhook khi tiền về).
5. Vận chuyển: tích hợp **GHN** (tính phí theo địa chỉ và cân nặng, tạo vận đơn, theo dõi trạng thái). Khách trả phí thật, đơn trên ngưỡng được miễn phí.
6. LLM: DeepSeek, dùng cho chat và sinh nội dung. Tên model nằm trong cấu hình, không ghi cứng trong code.
7. Embedding: một API riêng, chọn bên cụ thể trong spec `ai-gateway`.
8. Giao diện tiếng Việt, giá VND, sản phẩm có bảng thông số kỹ thuật theo từng danh mục.
9. Thu thập hành vi người dùng phải có sự đồng ý theo quy định bảo vệ dữ liệu cá nhân hiện hành của Việt Nam (cần rà soát pháp lý, xem Open Questions trong `SPEC.md`). Không gửi thông tin định danh (tên, SĐT, địa chỉ, email) sang API AI.

## Roles

| Role | Mã | Việc chính |
|---|---|---|
| Khách hàng | `customer` | Mua hàng, dùng các tính năng AI cá nhân hóa |
| Quản trị viên | `admin` | Toàn quyền: sản phẩm, nhân viên, role, cấu hình AI, báo cáo |
| Nhân viên bán hàng | `sales` | Xử lý đơn, đối soát thanh toán COD và SePay |
| Nhân viên CSKH | `support` | Nhận hội thoại chatbot chuyển sang, hỗ trợ khách, kiểm duyệt review |

Phân quyền RBAC: quyền khai báo trong code, role lưu trong database, admin tạo role mới và chọn quyền trên giao diện. Một tài khoản nhân viên giữ được nhiều role. Chi tiết trong `SPEC-identity.md`.

## Modules

| Module id | Trách nhiệm | Phụ thuộc |
|---|---|---|
| `identity` | Đăng ký, đăng nhập, phiên làm việc, tài khoản nhân viên, RBAC (role, quyền), audit log, quản lý đồng ý thu thập dữ liệu | không có |
| `ai-gateway` | Adapter gọi DeepSeek và API embedding: retry, timeout, giới hạn tần suất, cache, ghi log chi phí, xử lý khi AI lỗi | không có |
| `catalog` | Sản phẩm, danh mục, thương hiệu, thông số kỹ thuật, ảnh, tồn kho, API CRUD cho admin | identity |
| `shipping` | Danh mục địa chỉ, tính phí, tạo và hủy vận đơn GHN, theo dõi trạng thái giao hàng. Không biết về đơn hàng, chỉ phát sự kiện | identity |
| `commerce` | Giỏ hàng, checkout, đơn hàng, thanh toán COD + SePay (VietQR, webhook đối soát), đánh giá sau khi mua | identity, catalog, shipping |
| `activity-tracking` | Ghi sự kiện (xem, tìm, thêm giỏ, mua), dựng hồ sơ sở thích của từng người dùng, đo CTR và chuyển đổi | identity, catalog, commerce |
| `smart-search` | Index embedding sản phẩm, tìm theo ý nghĩa kết hợp bộ lọc (giá, hãng, thông số) | catalog, ai-gateway, activity-tracking |
| `recommendations` | "Dành cho bạn", sản phẩm tương tự, gợi ý mua kèm trong giỏ | catalog, activity-tracking, smart-search, ai-gateway |
| `personalized-content` | Tóm tắt review, mô tả sản phẩm và banner viết theo sở thích từng người | catalog, commerce, activity-tracking, ai-gateway |
| `knowledge-base` | Kho tri thức: chính sách, FAQ, hướng dẫn chọn mua, tài liệu nội bộ. Soạn Markdown hoặc nhập PDF/DOCX, chia đoạn, embedding, truy xuất hybrid cho RAG, trang Trung tâm hỗ trợ | identity, ai-gateway |
| `shopping-assistant` | Chatbot RAG có tool calling: truy xuất sản phẩm, review, tài liệu, đơn hàng của chính khách; trả lời có trích dẫn, kiểm tra grounding, thẻ sản phẩm thêm giỏ, so sánh, chuyển CSKH | catalog, commerce, activity-tracking, smart-search, recommendations, personalized-content, knowledge-base, ai-gateway |
| `support-desk` | Hàng đợi hội thoại chatbot chuyển cho người thật, nhân viên CSKH nhận và trả lời realtime, giờ làm việc và yêu cầu gọi lại, xem đơn của khách, AI soạn nháp từ kho tri thức, CSAT | identity, commerce, shopping-assistant, knowledge-base, ai-gateway |
| `admin-console` | Khung back-office: menu và trang chủ theo quyền, badge việc cần làm, trang cấu hình tổng hợp. Báo cáo doanh thu, hiệu quả AI (so sánh nhóm đối chứng có kiểm định), CSKH; xuất Excel/CSV. Màn hình nghiệp vụ do từng module cung cấp | identity, catalog, commerce, activity-tracking, smart-search, recommendations, personalized-content, shopping-assistant, support-desk, ai-gateway |

## Thứ tự build

```
identity, ai-gateway  (song song)
  → catalog, shipping, knowledge-base  (song song)
  → commerce
  → activity-tracking
  → smart-search
  → recommendations, personalized-content  (song song)
  → shopping-assistant
  → support-desk
  → admin-console
```

## Nguyên tắc

- Module nguồn chỉ phát sự kiện, module nghe tự đăng ký. Ví dụ `commerce` phát `onOrderPaid`, `activity-tracking` nghe; `commerce` không gọi `activity-tracking`.
- Mọi lời gọi AI đi qua `ai-gateway`. Đổi provider chỉ sửa một module.
- Phần mua bán (`identity`, `catalog`, `commerce`) không phụ thuộc AI. API AI lỗi thì khách vẫn mua hàng bình thường.
- Chatbot theo kiểu RAG: chỉ nêu sản phẩm, giá, chính sách, trạng thái đơn lấy từ kết quả truy xuất của lượt đó, có trích dẫn, không tự bịa.
- Module id cố định, không đổi tên. Spec của mỗi module lưu tại `SPEC-<module-id>.md`.

## Trạng thái spec

| Module | Spec | Trạng thái |
|---|---|---|
| `identity` | `SPEC-identity.md` | đã duyệt |
| `ai-gateway` | `SPEC-ai-gateway.md` | đã duyệt |
| `catalog` | `SPEC-catalog.md` | đã duyệt |
| `shipping` | `SPEC-shipping.md` | đã duyệt |
| `commerce` | `SPEC-commerce.md` | đã duyệt |
| `activity-tracking` | `SPEC-activity-tracking.md` | đã duyệt |
| `smart-search` | `SPEC-smart-search.md` | đã duyệt |
| `recommendations` | `SPEC-recommendations.md` | đã duyệt |
| `personalized-content` | `SPEC-personalized-content.md` | đã duyệt |
| `knowledge-base` | `SPEC-knowledge-base.md` | đã duyệt |
| `shopping-assistant` | `SPEC-shopping-assistant.md` | đã duyệt |
| `support-desk` | `SPEC-support-desk.md` | đã duyệt |
| `admin-console` | `SPEC-admin-console.md` | đã duyệt |
