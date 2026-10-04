# Spec: Pickory (quy ước chung)

File này chứa phần dùng chung cho mọi module. Danh sách module, phụ thuộc và thứ tự build nằm ở `CAPABILITY-MAP.md`. Spec riêng của từng module nằm ở `SPEC-<module-id>.md`.

## Objective

Pickory là web thương mại điện tử bán đồ điện tử, công nghệ cho khách hàng Việt Nam. Điểm khác biệt là trải nghiệm được cá nhân hóa bằng AI: gợi ý sản phẩm, chatbot tư vấn, tìm kiếm theo ý nghĩa và nội dung viết theo từng người. AI chỉ được tích hợp qua API của bên thứ ba (DeepSeek cho LLM, OpenAI cho embedding), không tự train model.

**Người dùng:**
- **Khách mua hàng**: muốn tìm đúng sản phẩm điện tử phù hợp nhu cầu và ngân sách mà không phải tự đọc hết thông số.
- **Quản trị viên**: quản lý sản phẩm, nhân viên, phân quyền, cấu hình AI và theo dõi AI có thực sự giúp tăng doanh số không.
- **Nhân viên bán hàng**: xử lý đơn, đối soát thanh toán COD và SePay.
- **Nhân viên CSKH**: tiếp nhận hội thoại khi chatbot chuyển cho người thật, hỗ trợ khách, kiểm duyệt review.

Phân quyền theo RBAC, chi tiết trong `SPEC-identity.md`.

**Thành công ở mức sản phẩm:**
- Khách vãng lai và khách có tài khoản đều mua hàng được từ đầu đến cuối (COD hoặc chuyển khoản qua SePay).
- Bốn tính năng AI chạy trên dữ liệu thật và có số đo hiệu quả trên dashboard admin.
- API AI lỗi hoặc hết ngân sách thì tính năng mua bán vẫn chạy bình thường.

## Tech Stack

| Thành phần | Lựa chọn |
|---|---|
| Ngôn ngữ | TypeScript (strict) cho cả frontend và backend |
| Runtime | Node.js LTS hiện hành |
| Package manager | pnpm workspaces (monorepo) |
| Frontend | React + Vite, React Router, TanStack Query |
| Backend | Express |
| Validation | Zod, schema dùng chung giữa frontend và backend |
| Database | PostgreSQL + pgvector, Prisma ORM |
| LLM | DeepSeek, tên model trong biến môi trường |
| AI SDK | Vercel AI SDK (`ai`, `@ai-sdk/deepseek`, `@ai-sdk/openai`), chỉ dùng trong `ai-gateway/providers/` |
| RAG tooling | LangChain.js (`@langchain/textsplitters`, PDF loader của `@langchain/community`), chỉ dùng trong `knowledge-base/ingest/` |
| Embedding | OpenAI `text-embedding-3-small` (1536 chiều) |
| Thanh toán | COD, SePay (VietQR + webhook) |
| Test | Vitest, Supertest, Playwright |
| Lint/format | ESLint + Prettier |
| Môi trường dev | Docker Compose cho PostgreSQL |

Phiên bản cụ thể lấy bản ổn định mới nhất lúc khởi tạo và khóa trong `pnpm-lock.yaml`. Lý do chọn framework AI ghi tại `docs/adr/0001-ai-frameworks.md`.

## Commands

```bash
pnpm install                      # cài dependency
docker compose up -d db           # chạy PostgreSQL + pgvector
pnpm db:migrate                   # prisma migrate dev
pnpm db:seed                      # dữ liệu mẫu + tài khoản admin dev
pnpm dev                          # chạy web (5173) và api (3000) cùng lúc
pnpm test                         # unit + integration (Vitest)
pnpm test --project unit           # chỉ unit, không cần PostgreSQL
pnpm test --project integration    # integration với DB test riêng
pnpm test -- --coverage           # kèm báo cáo coverage
pnpm test:e2e                     # Playwright
pnpm lint                         # ESLint
pnpm typecheck                    # tsc --noEmit cho mọi package
pnpm build                        # build web và api
pnpm admin:create -- --email <email>   # tạo tài khoản admin
```

## Project Structure

```
apps/
  frontend/               → React app (Vite), package @pickory/frontend
    src/features/<module-id>/   → trang, component, hook của từng module
      customer/           → trang dành cho khách (module lớn)
      admin/              → trang back-office (module lớn)
      components/         → component dùng trong nhiều trang của module
      hooks/              → hook của module
    src/shared/                 → component và tiện ích dùng chung
    test/unit/features/<module-id>/ → test hook và logic UI, đối chiếu với src/features/
  backend/                → Express API, package @pickory/backend
    src/modules/<module-id>/
      routes.ts           → khai báo route, chỉ parse request và gọi service
      service.ts          → logic nghiệp vụ
      repository.ts       → truy cập database qua Prisma
      schemas.ts          → Zod schema của request/response
      index.ts            → public interface cho module khác dùng
      (module lớn như identity tách thành thư mục: routes/, services/, security/, data/)
    src/shared/           → error, logger, config, middleware chung
    test/unit/modules/<module-id>/        → *.test.ts, đối chiếu với src/modules/
    test/unit/shared/                     → test tiện ích dùng chung
    test/integration/modules/<module-id>/ → *.int.test.ts với DB test riêng
    test/integration/shared/              → test hạ tầng DB
    test/{setup,global-setup,env,db}.ts    → setup và helper test, không thuộc src/
    prisma/               → schema.prisma, migrations, seed
packages/
  shared/                 → type và Zod schema dùng chung web/api
    src/                  → code dùng chung
    test/unit/            → *.test.ts
tools/
  test/unit/              → test quy tắc tooling
  lint-fixtures/          → file mẫu cho ESLint
e2e/                      → Playwright test
spec/                     → CAPABILITY-MAP.md, SPEC.md (file này), SPEC-<module-id>.md
docs/adr/                 → Architecture Decision Records
```

Test nằm ngoài `src/`; dưới `test/unit/` và `test/integration/`, giữ cấu trúc thư mục tương ứng với code được test. Import trực tiếp từ `src/`, không tạo bản sao hay re-export trung gian. Typecheck bao gồm cả `src/` và `test/`; API build chỉ biên dịch `src/`. Vitest tìm test trong các thư mục này, không tìm test đặt cạnh code nguồn.

**Quy tắc ranh giới module:** module khác chỉ được import từ `modules/<module-id>/index.ts`, không import thẳng `service.ts` hay `repository.ts` của module khác. Mỗi module chỉ ghi vào bảng của chính nó.

## Code Style

```ts
// apps/backend/src/modules/catalog/routes.ts
import { Router } from 'express';
import { requirePermission } from '../identity';
import { createProductSchema } from './schemas';
import * as productService from './service';

export const catalogRouter = Router();

catalogRouter.post('/admin/products', requirePermission('products:write'), async (req, res) => {
  const input = createProductSchema.parse(req.body); // ZodError → middleware trả 400
  const product = await productService.createProduct(input, req.actor);
  res.status(201).json({ data: product });
});
```

```ts
// apps/backend/src/shared/errors.ts
export class AppError extends Error {
  constructor(
    public readonly code: string,      // 'PRODUCT_NOT_FOUND'
    public readonly status: number,    // 404
    message: string,
  ) {
    super(message);
  }
}
```

- File và thư mục: `kebab-case`. Biến, hàm: `camelCase`. Type, class: `PascalCase`. Hằng số: `UPPER_SNAKE_CASE`.
- Bảng database: `snake_case` số nhiều, map qua `@@map` trong Prisma.
- Response thành công: `{ data }`. Response lỗi: `{ error: { code, message } }`, `message` bằng tiếng Việt để hiển thị được cho người dùng.
- Mọi input từ bên ngoài (body, query, params, webhook) đều validate bằng Zod trước khi dùng.
- Tiền lưu dạng số nguyên VND (`Int`), không dùng số thực.
- Thời gian lưu UTC, hiển thị theo `Asia/Ho_Chi_Minh`.
- Không dùng `any`. Không để `console.log` trong code commit, dùng logger chung.

## Testing Strategy

| Cấp | Công cụ | Dùng cho | Vị trí |
|---|---|---|---|
| Unit | Vitest | Logic thuần trong service, hàm tiện ích, parse/validate | `<app hoặc package>/test/unit/**/*.test.{ts,tsx}`; tooling trong `tools/test/unit/` |
| Integration | Vitest + Supertest + PostgreSQL thật (DB test riêng) | API endpoint, truy vấn Prisma, webhook | `<app hoặc package>/test/integration/**/*.int.test.{ts,tsx}` |
| E2E | Playwright | Luồng quan trọng: đăng ký, tìm kiếm, thêm giỏ, checkout, chat | `e2e/` |
| AI contract | Vitest, gọi API thật | Kiểm tra provider thật vẫn trả đúng định dạng | chỉ chạy khi `AI_LIVE_TESTS=1` |

- Test thường không bao giờ gọi API AI hay SePay thật. Dùng fake provider của `ai-gateway` và payload webhook mẫu.
- Coverage tối thiểu 80% dòng cho `service.ts` của mỗi module. Không đặt ngưỡng cho UI component.
- Bug fix phải có test tái hiện lỗi trước khi sửa.

## Boundaries

**Always:**
- Chạy `pnpm lint && pnpm typecheck && pnpm test` trước khi commit.
- Validate mọi input bên ngoài bằng Zod.
- Gọi AI qua `ai-gateway`, không gọi SDK provider trực tiếp từ module khác.
- Xác thực webhook SePay trước khi xử lý, và xử lý idempotent.
- Bảo vệ mọi endpoint back-office bằng `requirePermission`, và ghi `recordAudit` cho thao tác thay đổi dữ liệu của nhân viên.
- Cập nhật spec trước khi đổi hành vi đã ghi trong spec.

**Ask first:**
- Thêm dependency mới.
- Đổi schema database của module đã có dữ liệu thật.
- Đổi interface public (`index.ts`) của một module.
- Thêm quyền mới vào danh mục quyền, hoặc đổi quyền mặc định của role tạo sẵn.
- Đổi provider AI, model, hoặc gửi thêm loại dữ liệu người dùng sang API AI.
- Thay đổi CI/CD hoặc cấu hình deploy.

**Never:**
- Commit secret (API key, mật khẩu DB). Secret chỉ nằm trong `.env`, có `.env.example` mô tả.
- Gửi tên, SĐT, email, địa chỉ của người dùng sang API AI.
- Thu thập hành vi cho cá nhân hóa khi người dùng chưa đồng ý.
- Xóa hoặc skip test đang fail để cho pass.
- Lưu mật khẩu dạng có thể giải mã, hoặc log mật khẩu, token, API key.
- Bật LangSmith tracing, hoặc gửi trace, prompt, hội thoại sang dịch vụ quan sát bên ngoài.

## Success Criteria (toàn dự án)

Từng module có tiêu chí riêng trong spec của module. Toàn dự án coi là xong MVP khi:
- Mọi module trong `CAPABILITY-MAP.md` đạt tiêu chí của spec riêng.
- Mỗi role (`customer`, `admin`, `sales`, `support`) có ít nhất một luồng E2E chạy được từ đầu đến cuối.
- E2E pass cho luồng: khách vãng lai tìm kiếm bằng câu tự nhiên → chat hỏi so sánh → thêm giỏ → đăng ký → checkout SePay → webhook xác nhận đơn.
- Tắt API AI (sai key) thì toàn bộ luồng mua hàng vẫn pass E2E, các khối AI hiển thị phương án dự phòng.

## Open Questions

1. Hạ tầng deploy (VPS, Railway, Render, AWS...) và tên miền.
2. Dịch vụ gửi email (Resend, AWS SES, SMTP riêng...).
3. Rà soát pháp lý về dữ liệu cá nhân: gửi dữ liệu hành vi (đã ẩn danh hóa) sang DeepSeek và OpenAI là chuyển dữ liệu ra nước ngoài. Cần người có chuyên môn xác nhận nghĩa vụ theo quy định bảo vệ dữ liệu cá nhân hiện hành của Việt Nam.
