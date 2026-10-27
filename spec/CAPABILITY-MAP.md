# Capability Map: Pickory

Hệ thống thương mại điện tử bán đồ điện tử, công nghệ tại Việt Nam. Chatbot hỏi đáp là dịch vụ riêng `chatbot-rag`, gắn vào trang khách bằng widget.

## Giả định chung

1. Web app chạy trên trình duyệt desktop và mobile, chưa làm app native.
2. Frontend: React (Vite) + TypeScript. Backend: Node.js + Express + TypeScript. Chung một monorepo.
3. Database: PostgreSQL + Prisma ORM, lưu vector bằng pgvector.
4. Thanh toán: COD và chuyển khoản ngân hàng qua **SePay** (khách quét mã VietQR có mã đơn trong nội dung chuyển khoản, SePay gửi webhook khi tiền về).
5. Vận chuyển: tích hợp **GHN** (tính phí theo địa chỉ và cân nặng, tạo vận đơn, theo dõi trạng thái). Khách trả phí thật, đơn trên ngưỡng được miễn phí.
6. Chatbot: dịch vụ riêng `chatbot-rag` (dự án khác). Pickory nhúng widget và cung cấp API tài liệu công khai.
7. Pickory không gọi API LLM hay embedding. Từ ngày 2026-10-18, `ai-gateway` được gỡ khỏi Pickory.
8. Giao diện tiếng Việt, giá VND, sản phẩm có bảng thông số kỹ thuật theo từng danh mục.
9. Thu thập hành vi người dùng phải có sự đồng ý theo quy định bảo vệ dữ liệu cá nhân hiện hành của Việt Nam (cần rà soát pháp lý, xem Open Questions trong `SPEC.md`). Không gửi thông tin định danh (tên, SĐT, địa chỉ, email) sang API AI.

## Roles

| Role | Mã | Việc chính |
|---|---|---|
| Khách hàng | `customer` | Mua hàng, hỏi chatbot, chat với CSKH |
| Quản trị viên | `admin` | Toàn quyền: sản phẩm, nhân viên, role, cấu hình, báo cáo |
| Nhân viên bán hàng | `sales` | Xử lý đơn, đối soát thanh toán COD và SePay |
| Nhân viên CSKH | `support` | Chat trực tiếp với khách, hỗ trợ khách, kiểm duyệt review |

Phân quyền RBAC: quyền khai báo trong code, role lưu trong database, admin tạo role mới và chọn quyền trên giao diện. Một tài khoản nhân viên giữ được nhiều role. Chi tiết trong `SPEC-identity.md`.

## Modules

| Module id | Trách nhiệm | Phụ thuộc |
|---|---|---|
| `identity` | Đăng ký, đăng nhập, phiên làm việc, tài khoản nhân viên, RBAC (role, quyền), audit log, quản lý đồng ý thu thập dữ liệu | không có |
| `ai-gateway` | Đang gỡ khỏi Pickory từ ngày 2026-10-18, xem `tasks/ai-gateway-removal/`. Trước đây: adapter gọi DeepSeek và API embedding | không có |
| `catalog` | Sản phẩm, danh mục, thương hiệu, thông số kỹ thuật, ảnh, tìm theo từ khóa, API CRUD cho admin. Quản lý số lượng bán được và giữ hàng cho đơn | identity |
| `shipping` | Danh mục địa chỉ, tính phí, tạo và hủy vận đơn GHN, theo dõi trạng thái giao hàng. Không biết về đơn hàng, chỉ phát sự kiện | identity |
| `commerce` | Giỏ hàng, checkout, đơn hàng, thanh toán COD + SePay (VietQR, webhook đối soát), đánh giá sau khi mua | identity, catalog, shipping |
| `activity-tracking` | Đếm tổng lượt xem sản phẩm, thêm giỏ và đặt đơn theo ngày để dựng phễu cho báo cáo. Không lưu dữ liệu theo từng khách | identity, commerce |
| `knowledge-base` | Kho tri thức: chính sách, FAQ, hướng dẫn chọn mua, tài liệu nội bộ. Soạn Markdown hoặc nhập PDF/DOCX, phiên bản, trang Trung tâm hỗ trợ `/help`, API tài liệu công khai cho `chatbot-rag` nạp | identity |
| `shopping-assistant` | Đã bỏ ngày 2026-10-18. Chatbot chuyển sang dự án riêng `chatbot-rag` | không có |
| `chatbot-widget` | Nhúng widget của `chatbot-rag` vào trang khách, cấu hình địa chỉ dịch vụ, giữ trang mua hàng chạy khi dịch vụ chatbot lỗi | identity |
| `support-desk` | Chat realtime giữa khách và nhân viên CSKH, mở từ trang `/support`. Hàng chờ, giờ làm việc và yêu cầu gọi lại, xem đơn của khách, mẫu trả lời nhanh, CSAT | identity, commerce |
| `admin-console` | Khung back-office: menu và trang chủ theo quyền, badge việc cần làm, trang cấu hình tổng hợp. Báo cáo doanh thu, CSKH; xuất Excel/CSV. Màn hình nghiệp vụ do từng module cung cấp | identity, catalog, commerce, activity-tracking, support-desk |

### Thay đổi phạm vi ngày 2026-10-14

Người dùng bỏ ba module `smart-search`, `recommendations` và `personalized-content`. Lý do: phần AI cá nhân hóa chỉ tập trung vào chatbot RAG (`shopping-assistant`).

- Tìm theo từ khóa cơ bản chuyển sang `catalog`. Hệ thống không còn tìm sản phẩm theo ý nghĩa.
- Chatbot tìm sản phẩm qua `ICatalogReader.ListProductsAsync` với bộ lọc do LLM trích từ câu hỏi.
- Chatbot đọc review thật từ `commerce` và tự tóm tắt trong câu trả lời.
- Spec và task của ba module vẫn nằm trong repo để tra cứu lịch sử. Đầu mỗi file có dòng trạng thái "đã bỏ".

### Thay đổi phạm vi ngày 2026-10-18

Người dùng build chatbot RAG ở dự án riêng `chatbot-rag` (`C:\Users\juven\Desktop\chatbot-rag`). Mục tiêu là một chatbot gắn được vào website bất kỳ. Pickory chỉ nhúng widget của chatbot đó.

- `shopping-assistant` bị bỏ. Module mới `chatbot-widget` (mã `CBW`) nhúng widget vào trang khách.
- Chatbot trả lời chính sách, FAQ và hướng dẫn trước. Câu về giá hoặc sản phẩm cụ thể thì chatbot trả fallback kèm link tìm kiếm. Tool tìm sản phẩm qua API công khai của Pickory để giai đoạn sau của `chatbot-rag`.
- `knowledge-base` giữ phần soạn tài liệu và trang `/help`, bỏ chia chunk, embedding và truy xuất. `chatbot-rag` nạp tài liệu từ API công khai của Pickory, nên chỉ có một nguồn tài liệu.
- `support-desk` làm độc lập: khách chat trực tiếp với CSKH từ trang `/support`. Fallback của `chatbot-rag` trỏ tới trang này. Module bỏ phần AI soạn nháp.
- `ai-gateway` bị gỡ khỏi Pickory theo `tasks/ai-gateway-removal/` (mã `AIR`). Pickory không gọi API AI nào.
- `admin-console` bỏ báo cáo hiệu quả AI. `chatbot-rag` có log và số liệu chi phí riêng.
- `activity-tracking` chỉ giữ phần đo chuyển đổi. Hồ sơ sở thích và bản tóm tắt hồ sơ bị bỏ, vì không còn tính năng AI nào đọc.
- Ngày 2026-10-20 `activity-tracking` chỉ còn bộ đếm tổng theo ngày. Module không lưu sự kiện theo khách, nên không cần kiểm tra đồng ý hay xóa lịch sử.
- COM-E2E5 của `commerce` chuyển thành CBW-E2E1: dịch vụ chatbot không gọi được thì khách vẫn mua hàng.

### Thay đổi phạm vi ngày 2026-10-22

Người dùng tách tồn kho ra khỏi `catalog` thành module `inventory`. Spec: `SPEC-inventory.md`. Plan: `tasks/inventory/`.

- Mỗi SKU có ba con số: số trên kệ, số đã giữ, số bán được. Hàng rời kho khi đơn sang `shipping`.
- Module mới có phiếu nhập, nhà cung cấp, kiểm kê, ngưỡng sắp hết theo SKU, giá vốn bình quân và giá trị tồn kho.
- `catalog` giữ cột `skus.stock` làm bản sao số bán được. Trang khách, giỏ hàng và checkout không đổi.
- `commerce` gọi `IInventoryService` thay cho `IStockService` của `catalog`.
- `catalog` và `inventory` tham chiếu project Contracts của nhau. Hai project Contracts không tham chiếu nhau.

## Thứ tự build

```
identity
  → catalog, shipping  (song song)
  → commerce
  → activity-tracking
  → ai-gateway-removal
  → support-desk
  → admin-console
  → knowledge-base
  → chatbot-widget, KB-14 của knowledge-base  (song song, làm cuối)
```

Ngày 2026-10-20 người dùng chốt thứ tự trên:

- `knowledge-base` làm sau `admin-console`, ngay trước phần chatbot.
- `chatbot-widget` và `KB-14` (nguồn tài liệu cho `chatbot-rag`) làm cuối cùng.
- `knowledge-base` nghiệm thu không cần `KB-14`.
- `ai-gateway-removal` vẫn làm sớm. Gỡ code AI cũ trước giúp `support-desk` và `admin-console` xây trên code sạch.
- `support-desk` làm trước nên dựng chân trang. `knowledge-base` thêm link chính sách vào chân trang đó.

Ngày 2026-10-22 người dùng chèn `inventory` vào sau `activity-tracking`, trước `ai-gateway-removal`. Ngày 2026-10-27 người dùng bỏ module này; badge sắp hết vẫn đọc từ `catalog`.

## Nguyên tắc

- Module nguồn chỉ phát sự kiện, module nghe tự đăng ký. Ví dụ `commerce` phát `OrderPlacedEvent`, `activity-tracking` nghe; `commerce` không gọi `activity-tracking`.
- Pickory không gọi API AI. Chatbot là dịch vụ ngoài `chatbot-rag`, gắn qua `chatbot-widget`.
- Phần mua bán (`identity`, `catalog`, `commerce`) không phụ thuộc chatbot. Dịch vụ chatbot lỗi thì khách vẫn mua hàng bình thường.
- API tài liệu công khai cho `chatbot-rag` không bao giờ trả tài liệu loại `internal`. Ai trên mạng cũng hỏi được chatbot.
- Module id cố định, không đổi tên. Bản đồ này, `SPEC.md` và spec của mỗi module (`SPEC-<module-id>.md`) đều nằm trong thư mục `spec/`.

## Trạng thái spec

| Module | Spec | Trạng thái |
|---|---|---|
| `identity` | `SPEC-identity.md` | đã duyệt |
| `ai-gateway` | `SPEC-ai-gateway.md` | đã duyệt |
| `catalog` | `SPEC-catalog.md` | đã duyệt |
| `shipping` | `SPEC-shipping.md` | đã duyệt |
| `commerce` | `SPEC-commerce.md` | đã duyệt |
| `activity-tracking` | `SPEC-activity-tracking.md` | đã duyệt |
| `inventory` | `SPEC-inventory.md` | không triển khai ngày 2026-10-27 |
| `knowledge-base` | `SPEC-knowledge-base.md` | đã duyệt |
| `shopping-assistant` | `SPEC-shopping-assistant.md` | đã bỏ ngày 2026-10-18 |
| `chatbot-widget` | chưa có spec riêng, tiêu chí ở `tasks/chatbot-widget/` | chờ duyệt |
| `support-desk` | `SPEC-support-desk.md` | đã duyệt |
| `admin-console` | `SPEC-admin-console.md` | đã duyệt |

Spec của ba module đã bỏ (`SPEC-smart-search.md`, `SPEC-recommendations.md`, `SPEC-personalized-content.md`) có trạng thái "đã bỏ ngày 2026-10-14" và không còn trong bảng trên.
