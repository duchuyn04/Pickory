# Spec: ai-gateway

Module id: `ai-gateway` · Phụ thuộc: không có · Quy ước chung: `SPEC.md`

## Objective

Là cửa duy nhất để hệ thống gọi API AI bên ngoài. Các module tính năng (`smart-search`, `recommendations`, `personalized-content`, `shopping-assistant`) chỉ gọi interface của module này, không biết đang dùng provider nào.

**Vì sao cần:**
- Đổi model hoặc provider chỉ sửa cấu hình và adapter, không sửa module tính năng.
- Kiểm soát chi phí tập trung khi chưa biết ngân sách.
- Chặn dữ liệu cá nhân ở một chỗ trước khi rời hệ thống.
- Test được mọi tính năng AI mà không tốn tiền và không phụ thuộc mạng.

**Người dùng của module:** các module khác trong backend, và nhân viên back-office: người có `ai-usage:read` xem chi phí, người có `ai-settings:manage` (mặc định chỉ admin) sửa ngân sách và hạn mức.

## Interface public

```ts
// apps/backend/src/modules/ai-gateway/index.ts
export type AiFeature =
  | 'smart-search.parse-query'
  | 'smart-search.index'
  | 'recommendations.rerank'
  | 'personalized-content.review-summary'
  | 'personalized-content.description'
  | 'personalized-content.banner'
  | 'shopping-assistant.chat'
  | 'shopping-assistant.summarize'
  | 'knowledge-base.index'
  | 'knowledge-base.query'
  | 'support-desk.draft-reply';

export type CallContext = {
  feature: AiFeature;        // để ghi log chi phí và áp hạn mức
  actorKey?: string;         // userId hoặc anonymousId, để giới hạn theo người
  promptVersion?: string;    // ví dụ 'review-summary@3'
};

export interface AiGateway {
  chat(req: ChatRequest, ctx: CallContext): Promise<ChatResult>;
  chatStream(req: ChatRequest, ctx: CallContext): AsyncIterable<ChatChunk>;
  chatJson<T>(req: ChatRequest, schema: ZodType<T>, ctx: CallContext): Promise<T>;
  embed(texts: string[], ctx: CallContext): Promise<number[][]>;
}

export class AiUnavailableError extends AppError {}     // provider lỗi, timeout, circuit mở
export class AiBudgetExceededError extends AppError {}  // vượt ngân sách hoặc hạn mức người dùng
export class AiInvalidOutputError extends AppError {}   // JSON sai schema sau khi đã thử lại

export const aiGateway: AiGateway;
export const getUsageSummary: (range: DateRange) => Promise<UsageSummary>; // cho admin-console
export const aiPermissions = ['ai-usage:read', 'ai-settings:manage'] as const;
```

- `ChatRequest` hỗ trợ `messages`, `tools`, `maxSteps`, `temperature`, `maxTokens`.
- `tools` dùng kiểu `ToolDefinition` của Pickory, không dùng kiểu của AI SDK, để module tính năng không phụ thuộc framework:

```ts
export type ToolDefinition<P> = {
  name: string;
  description: string;
  parameters: ZodType<P>;
  execute: (params: P) => Promise<unknown>;   // module tính năng tự gắn actor qua closure
};
```

- Với `maxSteps > 1`, gateway tự chạy vòng lặp tool nhiều bước (gọi tool, đưa kết quả lại, gọi LLM tiếp). Mỗi bước đều qua kiểm tra ngân sách, `redactPII`, ghi log.
- Module gọi phải tự bắt 3 loại lỗi trên và hiển thị phương án dự phòng. Gateway không tự quyết định giao diện dự phòng.
- Prompt nằm trong module tính năng (`<module>/prompts.ts`), không nằm trong gateway.

## Thiết kế

### Provider

| Việc | Provider | Ghi chú |
|---|---|---|
| Chat, sinh nội dung, tool calling | DeepSeek | Qua Vercel AI SDK, provider `@ai-sdk/deepseek` |
| Embedding | OpenAI `text-embedding-3-small` | Qua Vercel AI SDK, provider `@ai-sdk/openai`. 1536 chiều, khớp cột `vector(1536)` trong pgvector |

Cấu hình qua biến môi trường:

```bash
LLM_BASE_URL=https://api.deepseek.com
LLM_API_KEY=
LLM_MODEL=                     # id model DeepSeek, xác nhận theo tài liệu chính thức
EMBEDDING_API_KEY=
EMBEDDING_MODEL=text-embedding-3-small
AI_MONTHLY_BUDGET_USD=30
AI_CHAT_DAILY_LIMIT_USER=50
AI_CHAT_DAILY_LIMIT_ANON=15
AI_LIVE_TESTS=0
```

Adapter đặt trong `ai-gateway/providers/`, dùng Vercel AI SDK:
- `chat`, `chatStream` dựa trên `generateText`, `streamText`. Vòng lặp tool nhiều bước dùng cơ chế multi-step của AI SDK, giới hạn bằng `maxSteps`.
- `chatJson` dựa trên `generateObject` với Zod schema.
- `embed` dựa trên `embedMany`.
- Usage của từng bước lấy từ callback kết thúc bước của AI SDK để ghi `ai_usage_logs`.
- Tắt retry có sẵn của AI SDK (`maxRetries: 0`). Retry, timeout, circuit breaker do gateway quản lý ở một chỗ.

Có thêm `fake-provider.ts` trả kết quả cố định, dùng trong mọi test và khi chạy dev không có API key.

### Độ tin cậy

- **Timeout:** chat 30 giây; stream phải có token đầu tiên trong 10 giây; embed 15 giây.
- **Retry:** chỉ với lỗi 429, 5xx và lỗi mạng. Tối đa 2 lần, backoff 500 ms rồi 1500 ms có jitter. Không retry lỗi 4xx khác.
- **Circuit breaker** (theo từng provider): 5 lỗi liên tiếp thì mở mạch 60 giây, trong thời gian đó ném `AiUnavailableError` ngay, không gọi ra ngoài. Sau 60 giây cho thử 1 request.
- **`chatJson`:** bật chế độ JSON của provider, parse rồi validate bằng Zod. Sai schema thì thử lại 1 lần kèm thông báo lỗi validate, vẫn sai thì ném `AiInvalidOutputError`.

### Cache

- **Embedding:** cache vĩnh viễn trong bảng `ai_embedding_cache`, key là SHA-256 của `(model, text)`. Text giống nhau không bao giờ bị tính tiền 2 lần.
- **Chat:** gateway không tự cache, vì cùng prompt có thể cần kết quả khác nhau. Module tính năng tự cache kết quả của mình (ví dụ tóm tắt review cache theo sản phẩm và số review).

### Ngân sách và hạn mức

Vì chưa biết ngân sách, mặc định đặt 30 USD/tháng. Giá trị trong biến môi trường chỉ là mặc định ban đầu; ngân sách và hạn mức lưu trong bảng `ai_settings`, người có quyền `ai-settings:manage` sửa trên giao diện, có hiệu lực ngay và được ghi audit log.

| Mức dùng trong tháng | Hành vi |
|---|---|
| < 80% | Bình thường |
| 80% đến < 100% | Ghi log cảnh báo, gắn cờ trên dashboard admin |
| ≥ 100% | Tính năng ưu tiên thấp (`personalized-content.*`, `recommendations.rerank`, `support-desk.draft-reply`) ném `AiBudgetExceededError`. Chat (`shopping-assistant.*`, `knowledge-base.query`), tìm kiếm và embedding vẫn chạy cho tới 120% rồi mới dừng hẳn |

- Hạn mức chat theo người mỗi ngày (theo giờ Việt Nam): 50 tin cho người đã đăng nhập, 15 tin cho khách vãng lai. Vượt thì ném `AiBudgetExceededError` với thông báo dễ hiểu.
- Chi phí ước tính = số token × đơn giá trong file `ai-gateway/pricing.ts`. Đơn giá phải cập nhật tay khi provider đổi giá.
- MVP chạy 1 instance API, nên bộ đếm hạn mức theo người giữ trong bộ nhớ, còn tổng chi phí tháng tính từ bảng log. Khi chạy nhiều instance phải chuyển bộ đếm sang DB hoặc Redis.

### Bảo vệ dữ liệu cá nhân

- Mọi text gửi đi (message, text cần embed) đi qua `redactPII()` (hàm dùng chung ở `apps/backend/src/shared/privacy.ts`), thay SĐT Việt Nam, email và số thẻ/số tài khoản ngân hàng bằng `[đã ẩn]`.
- Module gọi không được đưa tên, địa chỉ hay ID người dùng vào prompt. Hồ sơ sở thích chỉ gửi dạng tóm tắt (ví dụ: "quan tâm laptop gaming, ngân sách 20 đến 30 triệu").
- `redactPII()` là lớp chặn cuối, không thay thế trách nhiệm của module gọi.

### Ghi log

Mỗi lần gọi ghi 1 dòng vào `ai_usage_logs`:

```
id, feature, provider, model, promptVersion, inputTokens, outputTokens,
cachedTokens, costUsd, latencyMs, status (ok|error|blocked), errorCode,
actorKeyHash, createdAt
```

- Không lưu nội dung prompt và câu trả lời. `actorKeyHash` là hash của userId hoặc anonymousId, không lưu ID gốc.
- `getUsageSummary` tổng hợp chi phí theo ngày, theo feature, tỉ lệ lỗi và độ trễ p95 cho `admin-console`.

## API back-office

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/admin/ai/usage` | `ai-usage:read` | Chi phí, số lần gọi, tỉ lệ lỗi, độ trễ theo ngày và theo feature |
| GET | `/api/admin/ai/settings` | `ai-usage:read` | Ngân sách và hạn mức hiện tại |
| PUT | `/api/admin/ai/settings` | `ai-settings:manage` | Sửa ngân sách, hạn mức chat theo người |

## Testing

- Unit: retry và backoff (dùng fake timer), circuit breaker, tính chi phí, ngưỡng ngân sách, hạn mức theo người, `redactPII()` với bộ ví dụ SĐT và email Việt Nam, `chatJson` thử lại khi sai schema.
- Integration: cache embedding đọc ghi đúng trong PostgreSQL; log được ghi cho cả trường hợp thành công, lỗi và bị chặn.
- Adapter test bằng HTTP mock (giả lập 200, 429, 500, timeout).
- Contract test với API thật chỉ chạy khi `AI_LIVE_TESTS=1`: 1 lần chat, 1 lần `chatJson`, 1 lần tool calling, 1 lần embed. Không chạy trong CI mặc định.

## Boundaries (riêng module)

- **Always:** đi qua `redactPII()` trước khi gửi; ghi log cho mọi lần gọi, kể cả lần bị chặn.
- **Ask first:** thêm provider; đổi model mặc định; tăng hạn mức hoặc ngân sách mặc định; lưu nội dung prompt để debug.
- **Never:** để API key xuất hiện trong log hay response; cho frontend gọi thẳng provider; retry vô hạn.

## Success Criteria

- [ ] Module tính năng chỉ cần import từ `ai-gateway/index.ts`; ESLint chặn import `ai`, `@ai-sdk/*`, `openai` ở ngoài `ai-gateway/providers/`, và `@langchain/*` ở ngoài `knowledge-base/ingest/`.
- [ ] Vòng lặp tool 3 bước ghi đúng 3 dòng `ai_usage_logs` và dừng ở `maxSteps`.
- [ ] Đổi `LLM_MODEL` hoặc `LLM_BASE_URL` không cần sửa code.
- [ ] Provider trả 500 liên tục: sau 5 lỗi, các lần gọi tiếp theo trong 60 giây ném `AiUnavailableError` trong dưới 50 ms và không phát sinh request ra ngoài.
- [ ] Gọi `embed` 2 lần cùng một text chỉ phát sinh 1 request tới provider.
- [ ] Đạt 100% ngân sách thì `personalized-content.*` bị chặn, còn `shopping-assistant.chat` vẫn chạy.
- [ ] Khách vãng lai gửi tin chat thứ 16 trong ngày nhận `AiBudgetExceededError`.
- [ ] Text chứa `0912 345 678` hoặc `a@b.vn` được thay bằng `[đã ẩn]` trước khi gửi (kiểm tra bằng fake provider ghi lại request).
- [ ] `chatJson` trả object đúng schema, hoặc ném `AiInvalidOutputError` sau đúng 2 lần thử.
- [ ] Mọi lần gọi có đúng 1 dòng trong `ai_usage_logs`, không có nội dung prompt.
- [ ] Sửa ngân sách qua `PUT /api/admin/ai/settings` có hiệu lực ở lần gọi kế tiếp và tạo 1 dòng audit log; nhân viên `sales` gọi endpoint này nhận 403.
- [ ] Contract test với DeepSeek và OpenAI thật pass khi bật `AI_LIVE_TESTS=1`.
- [ ] Coverage `service.ts` và các file logic ≥ 80%.

## Open Questions

1. Id model chính xác của "DeepSeek v4.1 flash" trên API, giới hạn context, có hỗ trợ JSON mode và tool calling không, đơn giá hiện tại. Cần kiểm tra tài liệu chính thức của DeepSeek trước khi code adapter.
2. Mức ngân sách 30 USD/tháng và hạn mức 50/15 tin chat mỗi ngày có hợp lý không?
3. Có cần provider dự phòng cho chat (ví dụ tự chuyển sang model khác khi DeepSeek lỗi) trong MVP không? Mặc định: không, chỉ hiển thị phương án dự phòng.
