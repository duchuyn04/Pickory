# Implementation Plan: identity + ai-gateway

## Overview

Xây hai module nền đầu tiên của Pickory theo `spec/CAPABILITY-MAP.md`: `identity` (danh tính, phiên, RBAC, audit log, đồng ý thu thập dữ liệu) và `ai-gateway` (cửa duy nhất gọi DeepSeek và OpenAI, có retry, circuit breaker, ngân sách, cache embedding, chặn PII, log chi phí). Repo hiện chỉ có spec, chưa có code, nên plan bắt đầu bằng phần nền chung (monorepo, Express, Prisma, test DB, lint ranh giới module). Sau phần nền, hai module chạy song song theo hai track và gặp nhau ở API back-office của `ai-gateway`, vì API này cần `requirePermission` và `recordAudit` của `identity`.

Spec gốc: `spec/SPEC.md`, `spec/SPEC-identity.md`, `spec/SPEC-ai-gateway.md`, `docs/adr/0001-ai-frameworks.md`.
Danh sách task chi tiết (tiêu chí nghiệm thu, cách kiểm tra, file dự kiến): `tasks/todo.md`.

## Đồ thị phụ thuộc

```
T01 workspace + tooling
 ├── T02 API skeleton (errors, logger, config, health)
 │    ├── T03 DB + Prisma + test DB
 │    │    ├── [Track A: identity]
 │    │    │    ID-01 danh mục quyền + RBAC schema + seed
 │    │    │     └── ID-02 actor khách vãng lai + Origin + /api/me
 │    │    │          └── ID-03 đăng nhập nhân viên + phiên
 │    │    │               └── ID-04 requirePermission + recordAudit + audit-logs ──┐
 │    │    │                    ├── ID-05 đăng ký/đăng nhập khách + UserLinkedEvent  │
 │    │    │                    │    ├── ID-06 chống dò mật khẩu                     │
 │    │    │                    │    ├── ID-07 email token (xác thực, quên mật khẩu) │
 │    │    │                    │    │    ├── ID-09 Google OAuth                     │
 │    │    │                    │    │    ├── ID-10 hồ sơ, đổi mật khẩu, xóa TK      │
 │    │    │                    │    │    └── ID-12 mời nhân viên                    │
 │    │    │                    │    └── ID-08 consent                               │
 │    │    │                    ├── ID-11 quản lý role                              │
 │    │    │                    │    └── ID-13 gán role, khóa nhân viên             │
 │    │    │                    └── ID-14 tra cứu, khóa khách                       │
 │    │    └── [Track B: ai-gateway]                                               │
 │    │         AI-00 spike DeepSeek + AI SDK (cần API key)                        │
 │    │         AI-01 redactPII (chỉ cần T02)                                      │
 │    │         AI-02 hợp đồng + fake provider + usage log                         │
 │    │          └── AI-03 timeout, retry, circuit breaker                         │
 │    │               └── AI-04 adapter DeepSeek (chat, stream)  ← AI-00           │
 │    │                    ├── AI-05 ngân sách + hạn mức                           │
 │    │                    │    ├── AI-06 chatJson                                 │
 │    │                    │    ├── AI-07 vòng lặp tool nhiều bước                 │
 │    │                    │    └── AI-09 API back-office AI  ←────────────────────┘
 │    │                    └── AI-08 embed + cache + adapter OpenAI
 │    └── T05 ESLint ranh giới module
 └── T04 web skeleton → UI-01..UI-08 (mỗi UI task đi ngay sau backend slice của nó)
                       → E2E-01
```

## Architecture Decisions

1. **Danh mục quyền do `identity` giữ trong MVP** (`modules/identity/permissions.ts`, nhóm theo module, kiểu `Permission` là union literal để `requirePermission('orders:update-status')` được kiểm tra lúc compile). Spec nói mỗi module có `permissions.ts` riêng và `identity` gom lại, nhưng nếu `identity` import file của module khác thì phụ thuộc bị ngược chiều (mọi module import `identity`). Cách làm: `identity` giữ toàn bộ danh mục ban đầu; module khác (ví dụ `ai-gateway`) re-export phần của mình từ catalog này (`aiPermissions`). Đây là điểm lệch spec, cần duyệt (Open Question 2).
2. **`admin` có mọi quyền một cách ngầm định**: actor có role `admin` nhận toàn bộ danh mục trong code, không dựa vào dòng `role_permissions`. Quyền thêm sau này admin có ngay, không cần seed lại.
3. **Lát cắt dọc ở mức backend, UI đi ngay sau.** Mỗi task backend đi trọn một đường schema → repository → service → route → integration test, kiểm tra được bằng Supertest. Task UI tương ứng đặt ngay sau, không dồn UI xuống cuối. Lý do: gộp cả API lẫn UI vào một task sẽ vượt 5 file.
4. **Migration tuần tự giữa hai track.** Task nào tạo migration thì trước khi merge phải rebase lên `main` rồi chạy lại `pnpm db:migrate`. Không sửa migration đã merge. Nếu bản Prisma khóa lúc khởi tạo hỗ trợ schema nhiều file (GA từ Prisma 6.7), mỗi module có file riêng: `prisma/schema/identity.prisma`, `prisma/schema/ai-gateway.prisma`. T03 đã xác nhận: Prisma 7.10.0 hỗ trợ `prisma/schema/` nhiều file (`prisma.config.ts` trỏ `schema: 'prisma/schema'`), nên mỗi module một file `.prisma`.
5. **Gửi email qua port `EmailSender`.** Test dùng bản in-memory. Dev dùng bản "file outbox" ghi email ra `apps/api/.dev-outbox/` (thư mục bị gitignore), để Playwright đọc được link mời và link đặt lại mật khẩu. Provider thật làm sau khi chốt dịch vụ email (Open Question của `SPEC.md`).
6. **Bộ đếm trong bộ nhớ** cho chống dò mật khẩu (`identity`) và hạn mức chat theo người (`ai-gateway`), vì MVP chạy 1 instance. Spec `ai-gateway` đã ghi rõ ràng buộc này.
7. **Event bus trong process**: `apps/api/src/shared/events.ts` là emitter có kiểu. `identity` phát `UserLinkedEvent` và `UserDeletedEvent`. Handler lỗi chỉ ghi log, không làm hỏng luồng chính.
8. **Định nghĩa dòng `ai_usage_logs`**: 1 dòng cho mỗi bước LLM (đã gồm các lần retry của bước đó). Lời gọi không có tool là 1 bước, nên đúng 1 dòng. Vòng lặp tool 3 bước ghi 3 dòng. Lời gọi không phát sinh request ra ngoài (bị chặn ngân sách, circuit đang mở, embed trúng cache toàn bộ) vẫn ghi đúng 1 dòng, với `status` là `blocked`, `error` hoặc `ok` và `costUsd = 0`. Cách hiểu này gỡ mâu thuẫn giữa hai tiêu chí trong spec.
9. **Bậc ưu tiên ngân sách** (ghi trong code `ai-gateway/budget.ts`):

   | Bậc | Feature | Dừng ở |
   |---|---|---|
   | thấp | `personalized-content.*`, `recommendations.rerank`, `support-desk.draft-reply` | 100% |
   | cao | `shopping-assistant.*`, `knowledge-base.query`, `knowledge-base.index`, `smart-search.*` | 120% |

   Tháng và ngày tính theo giờ `Asia/Ho_Chi_Minh`. Hạn mức chat theo người chỉ áp cho `shopping-assistant.chat`. Mỗi lần gọi `chat` hoặc `chatStream` tính là 1 tin, kể cả khi vòng lặp tool chạy nhiều bước.
10. **`actorKey` có tiền tố** `user:<id>` hoặc `anon:<id>` để gateway phân biệt hạn mức 50 và 15 mà không đổi interface `CallContext` (Open Question 4).
11. **Cache embedding lưu `Float[]`**, không dùng kiểu `vector`, vì bảng cache không cần tìm kiếm tương đồng. Cột `vector(1536)` để dành cho `smart-search`. T03 vẫn bật extension `pgvector`. Key cache là SHA-256 của `model + "\0" + text đã qua redactPII`, tức chính text được gửi đi.
12. **Retry chỉ nằm ở gateway**: adapter AI SDK đặt `maxRetries: 0`. Vòng lặp tool cần một chỗ móc vào giữa các bước (kiểm tra ngân sách, redactPII kết quả tool, ghi log). AI-00 xác minh hook `prepareStep`/`onStepFinish` của bản AI SDK được khóa. Nếu không có hook phù hợp, gateway tự điều khiển từng bước (mỗi bước gọi 1 lần `generateText` một bước).
13. **Tên package**: `@pickory/api`, `@pickory/web`, `@pickory/shared`. Lệnh test tập trung: `pnpm --filter @pickory/api test -- <đường dẫn>`.
14. **Khung back-office tối thiểu** (layout + menu theo quyền) làm trong UI-05 vì `identity` cần trang admin ngay. Sau này `admin-console` thay khung này bằng bản đầy đủ.

## Task List

Chi tiết từng task ở `tasks/todo.md`. Hai track A và B chạy song song được sau Checkpoint 0.

### Phase 0: Nền tảng chung
- [x] T01: Khởi tạo pnpm workspace và tooling
- [x] T02: API skeleton (errors, logger, config, health)
- [x] T03: PostgreSQL, Prisma và test DB
- [ ] T04: Web skeleton
- [ ] T05: ESLint chặn import sai ranh giới module

### Checkpoint 0: Nền tảng
- [ ] `pnpm lint && pnpm typecheck && pnpm test && pnpm build` sạch; `pnpm dev` chạy cả web và api

### Phase 1: Lõi hai module (song song)
- Track A: [ ] ID-01 · [ ] ID-02 · [ ] ID-03 · [ ] ID-04 · [ ] UI-05
- Track B: [ ] AI-00 · [ ] AI-01 · [ ] AI-02 · [ ] AI-03

### Checkpoint 1: Lõi
- [ ] Module khác đã dùng được `requirePermission`/`recordAudit`; gateway gọi `chat` qua fake provider có retry, circuit breaker, log

### Phase 2: Khách hàng (A) và provider thật (B)
- Track A: [ ] ID-05 · [ ] UI-01 · [ ] ID-06 · [ ] ID-07 · [ ] UI-02 · [ ] ID-08 · [ ] UI-03
- Track B: [ ] AI-04 · [ ] AI-05 · [ ] AI-06

### Checkpoint 2
- [ ] Khách đăng ký, đăng nhập, quên mật khẩu, chọn consent qua UI; gateway gọi DeepSeek thật (live test) và chặn theo ngân sách

### Phase 3: Tài khoản khách (A) và hoàn thiện gateway (B)
- Track A: [ ] ID-09 · [ ] ID-10 · [ ] UI-04
- Track B: [ ] AI-07 · [ ] AI-08 · [ ] AI-09

### Checkpoint 3
- [ ] Mọi tiêu chí trong `SPEC-ai-gateway.md` đạt; luồng tài khoản khách đầy đủ

### Phase 4: Back-office identity
- [ ] ID-11 · [ ] UI-06 · [ ] ID-12 · [ ] ID-13 · [ ] UI-07 · [ ] ID-14 · [ ] UI-08

### Phase 5: E2E và nghiệm thu
- [ ] E2E-01: Playwright + hai luồng E2E của `identity`

### Checkpoint cuối
- [ ] Mọi tiêu chí trong bảng Traceability đạt, trừ các mục ghi "hoãn"
- [ ] Coverage `service.ts` và file logic của hai module ≥ 80%
- [ ] Contract test thật pass với `AI_LIVE_TESTS=1`

## Traceability: Success Criteria → Task

**`SPEC-identity.md`**

| Tiêu chí | Task |
|---|---|
| 4 role sau seed, quyền đúng bảng mặc định | ID-01 |
| Admin tạo role, tick quyền, gán; có hiệu lực ở request tiếp theo | ID-11, ID-13 |
| Nhân viên giữ `sales` + `support` có hợp quyền | ID-01 (unit), ID-13 (integration) |
| Test ma trận quyền | ID-04 (khung test theo dữ liệu), ID-11 đến ID-14 thêm dòng; **một phần**: chỉ phủ endpoint của `identity` và `ai-gateway`, module sau tự thêm dòng |
| Không xóa/khóa admin cuối; không tự sửa role; không xóa role đang dùng | ID-11, ID-13 |
| Khóa hoặc gỡ hết role thì phiên mất hiệu lực ngay | ID-13 |
| Mỗi thao tác phân quyền có đúng 1 dòng audit log | ID-04, ID-11, ID-13 |
| Đăng ký, đăng nhập email và Google; Argon2id | ID-03, ID-05, ID-09 |
| Sai 5 lần trong 15 phút thì lần 6 bị 429 | ID-06 |
| Link đặt lại 30 phút, link mời 72 giờ, không dùng lại được | ID-07, ID-12 |
| Đúng một `UserLinkedEvent`; chưa chọn thì `personalization = false` | ID-05, ID-02, ID-08 |
| `Origin` lạ bị 403 | ID-02 |
| Xóa tài khoản khách thì không đăng nhập lại được, email gốc mất | ID-10 |
| Coverage `service.ts` ≥ 80% | mọi task ID-*, kiểm ở Checkpoint cuối |
| E2E: khách đăng ký → đăng xuất → đăng nhập | E2E-01 |
| E2E: admin mời `sales` → đặt mật khẩu → đăng nhập chỉ thấy menu đơn hàng | E2E-01, **một phần**: kiểm menu không có mục ngoài quyền; phần "menu đơn hàng" hoãn tới `commerce`/`admin-console` |

**`SPEC-ai-gateway.md`**

| Tiêu chí | Task |
|---|---|
| ESLint chặn import `ai`, `@ai-sdk/*`, `openai`, `@langchain/*` sai chỗ | T05 |
| Vòng lặp tool 3 bước ghi 3 dòng log, dừng ở `maxSteps` | AI-07 |
| Đổi `LLM_MODEL`/`LLM_BASE_URL` không sửa code | AI-04 |
| Circuit mở: ném `AiUnavailableError` < 50 ms, không gọi ra ngoài | AI-03, AI-04 |
| `embed` 2 lần cùng text chỉ 1 request | AI-08 |
| 100% ngân sách chặn `personalized-content.*`, chat vẫn chạy | AI-05 |
| Khách vãng lai tin thứ 16 bị `AiBudgetExceededError` | AI-05 |
| `0912 345 678`, `a@b.vn` thành `[đã ẩn]` trước khi gửi | AI-01, AI-02 |
| `chatJson` đúng schema hoặc `AiInvalidOutputError` sau đúng 2 lần | AI-06 |
| Mọi lần gọi đúng 1 dòng log, không có nội dung prompt | AI-02, AI-05, AI-08 (theo Decision 8) |
| `PUT /api/admin/ai/settings` hiệu lực ngay, có audit, `sales` nhận 403 | AI-09 |
| Contract test thật pass với `AI_LIVE_TESTS=1` | AI-04, AI-06, AI-07, AI-08, chạy lại ở Checkpoint cuối |
| Coverage ≥ 80% | mọi task AI-*, kiểm ở Checkpoint cuối |

## Lịch 6 tuần (6 sprint 1 tuần), giữ toàn bộ phạm vi

Giả định: 2 người fullstack, mỗi người 2 đến 3 giờ mỗi ngày (khoảng 25 giờ mỗi sprint cho cả nhóm), code có AI hỗ trợ nên mỗi task tốn khoảng 4 giờ của người (viết yêu cầu, review, chạy kiểm tra, sửa). Quy đổi XS/S = 2 điểm, M = 5 điểm, tổng 154 điểm, cần khoảng 26 điểm mỗi sprint. Con số này là giả định, Sprint 1 là phép thử đầu tiên. Không cắt phạm vi.

| Sprint | Điểm | Task | Checkpoint cuối sprint |
|---|---|---|---|
| 1 | 23 | T01 đến T05, AI-00, AI-01 | Checkpoint 0 |
| 2 | 27 | ID-01 đến ID-04, AI-02, AI-03 | |
| 3 | 26 | UI-05, ID-05, UI-01, ID-06, AI-04 đến AI-06 | Checkpoint 1 |
| 4 | 24 | ID-07, UI-02, ID-08, UI-03, AI-07, AI-08 | Checkpoint 2 |
| 5 | 27 | ID-09, ID-10, UI-04, ID-11, UI-06, AI-09 | Checkpoint 3 |
| 6 | 27 | ID-12, ID-13, UI-07, ID-14, UI-08, E2E-01 | Checkpoint cuối |

Thứ tự trong sprint vẫn theo phụ thuộc (ví dụ UI-07 trước E2E-01 trong Sprint 6). Khi chia, đã kiểm tra mỗi task nằm sau các task nó phụ thuộc. Sprint 1 là phần chung của cả hai người. Với sprint 1 tuần, các buổi planning, review và retro nên ngắn tương ứng (Scrum Guide cho phép rút ngắn theo độ dài sprint).

Cách chia việc cho 2 người:
- Phase 0 (T01 đến T05): cả hai làm chung trong tuần đầu, vì các task nối tiếp nhau và là nền cho mọi thứ sau.
- Sau Checkpoint 0: một người giữ Track A (`identity`), người kia giữ Track B (`ai-gateway`). Track B nhẹ hơn (41 điểm so với 89 điểm), nên người làm Track B nhận thêm các task UI và task `identity` độc lập (UI-06, UI-07, ID-14, UI-08) khi xong AI-09.
- Việc chờ bên ngoài cần chốt ngay tuần 1: API key DeepSeek và OpenAI (cho AI-00), duyệt dependency và Open Questions 1 và 2, chọn dịch vụ email.
- Ai tạo migration thì báo người kia rebase trước khi merge (Decision 4).

Sprint 1 nhẹ nhất (23 điểm) vì phải dựng nền tảng và chờ API key. Từ Sprint 2 trở đi, mỗi sprint 24 đến 27 điểm, không có đệm. Theo dõi velocity ngay cuối Sprint 1 và Sprint 2 để phát hiện sớm nếu trượt.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Model DeepSeek chưa xác định, có thể không hỗ trợ JSON mode hoặc tool calling qua AI SDK | High | AI-00 là task đầu của track B; AI-04, AI-06, AI-07 phụ thuộc kết quả spike |
| AI SDK đổi tên API (`maxSteps` → `stopWhen`, `generateObject` bị thay) so với lúc viết spec | Med | AI-00 kiểm tra trên bản sẽ khóa; `ChatRequest.maxSteps` của Pickory giữ nguyên, chỉ adapter thay đổi |
| Không móc được kiểm tra ngân sách và redactPII vào giữa các bước tool của AI SDK | High | AI-00 xác minh hook; phương án dự phòng là gateway tự chạy từng bước |
| Xung đột migration giữa hai track | Med | Decision 4: tuần tự, rebase rồi tạo lại migration, schema nhiều file |
| `redactPII` ẩn nhầm giá tiền VND (ví dụ `25990000`) làm hỏng prompt so sánh giá | Med | Tiêu chí riêng trong AI-01: giá VND thường gặp không bị ẩn |
| Chưa chọn dịch vụ email làm kẹt luồng mời nhân viên và quên mật khẩu | Med | Port `EmailSender` + file outbox cho dev (Decision 5) |
| Ma trận quyền và E2E menu không đạt trọn vẹn được khi các module sau chưa có | Low | Ghi rõ "một phần" trong Traceability; khung test theo dữ liệu để module sau thêm dòng |
| Tiền tố `actorKey` là quy ước ngầm, module gọi có thể quên | Low | Gateway kiểm tra định dạng, sai thì ném lỗi lập trình (fail fast trong test) |

## Open Questions (cần trả lời trước hoặc trong lúc build)

1. **Duyệt dependency ngoài tech stack** (thuộc mục "Ask first"):
   - Băm mật khẩu Argon2id: `@node-rs/argon2` (đề xuất, không cần build native) hoặc `argon2`.
   - Đọc cookie: `cookie-parser`.
   - Logger: `pino` + `pino-http`.
   - Mock HTTP cho test adapter AI và Google OAuth: `msw`.
   - Google OAuth: `arctic` (PKCE + state) và `jose` (kiểm tra id_token), hoặc tự viết bằng `fetch` + `jose`.
   - Chạy TypeScript cho dev và CLI `admin:create`: `tsx`.
2. **Danh mục quyền do `identity` giữ** (Decision 1) thay vì mỗi module một `permissions.ts`. Đồng ý lệch spec như vậy không? Nếu đồng ý thì cập nhật `SPEC-identity.md` trong ID-01.
3. **API key DeepSeek và OpenAI** cho AI-00 và các live test. Không có key thì AI-00 bị chặn; AI-01 đến AI-03 vẫn làm được.
4. **Quy ước `actorKey`** `user:<id>`/`anon:<id>` (Decision 10), hay thêm trường `actorKind` vào `CallContext` (đổi interface public)?
5. **Xác nhận lại khi xóa tài khoản Google** (không có mật khẩu): đề xuất yêu cầu đăng nhập Google trong vòng 5 phút gần nhất, ghi `authenticatedAt` trên phiên.
6. Các câu hỏi mở của spec giữ mặc định: không bắt xác thực email trước khi đặt đơn, chưa có TOTP, chưa có export dữ liệu cá nhân, bảng quyền mặc định của `sales`/`support` giữ như spec.
