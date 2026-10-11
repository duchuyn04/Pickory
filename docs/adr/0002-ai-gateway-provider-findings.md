# ADR 0002: Hợp đồng provider và điều khiển từng bước trong ai-gateway

- Ngày: 2026-10-05
- Trạng thái: Quyết định triển khai đã chọn, xác minh API thật bị chặn do thiếu khóa
- Liên quan: `spec/SPEC-ai-gateway.md`, `tasks/identity-ai-gateway/todo.md` AI-00 đến AI-09

## Nguồn chính thức

- DeepSeek model, giới hạn và giá: https://api-docs.deepseek.com/quick_start/pricing
- DeepSeek JSON mode: https://api-docs.deepseek.com/guides/json_mode
- DeepSeek tool calling: https://api-docs.deepseek.com/guides/tool_calls
- Adapter DeepSeek của AI SDK: https://ai-sdk.dev/providers/ai-sdk-providers/deepseek
- API generateText: https://ai-sdk.dev/docs/reference/ai-sdk-core/generate-text
- OpenAI embedding và giá: https://developers.openai.com/api/docs/models/text-embedding-3-small
- Giá OpenAI các model embedding: https://developers.openai.com/api/docs/pricing

## Phiên bản đã khóa và API đã quan sát

| Package | Phiên bản |
| --- | --- |
| `ai` | `7.0.127` |
| `@ai-sdk/deepseek` | `3.0.58` |
| `@ai-sdk/openai` | `4.0.83` |

Probe trực tiếp package đã cài xác nhận `generateText`, `streamText`, `embedMany`, `generateObject`, `stepCountIs`, `tool` và `jsonSchema` là hàm, `Output` là object. Source của phiên bản đã cài có `prepareStep`, `onStepFinish`, `stopWhen` và `maxOutputTokens`.

Spec giữ `ChatRequest.maxTokens` và `maxSteps` của Pickory. Adapter ánh xạ `maxTokens` sang `maxOutputTokens`; gateway dùng vòng lặp riêng để giới hạn số bước. Module gọi không import kiểu AI SDK.

## Model và giá

Tài liệu DeepSeek hiện ghi ID `deepseek-flash` cho DeepSeek-V4.1-Flash, context 1M token, output tối đa 384K token, có JSON output và tool calling. Đây là thông tin tài liệu, chưa được xác minh bằng tài khoản API của dự án. `LLM_MODEL` không có mặc định: có khóa mà chưa chọn model thì cấu hình bị từ chối.

Giá Flash theo tài liệu, USD trên một triệu token:

| Loại | Peak | Off-peak |
| --- | --- | --- |
| Input cache hit | 0.006 | 0.003 |
| Input cache miss | 0.30 | 0.15 |
| Output | 1.20 | 0.60 |

Gateway dùng giá peak để ước tính chi phí một cách bảo thủ, không cố dự đoán ngày lễ hoặc ưu đãi của provider. Chi phí trong dashboard là ước tính, không phải hóa đơn. Các ID đã có giá gồm `deepseek-flash`, `deepseek-v4-flash`, `deepseek-v4-flash-vision-exp` và `deepseek-v4-pro`; đổi giữa các ID này chỉ cần cấu hình. Pro dùng giá peak input cache hit 0.044, input cache miss 1.32, output 3.96 USD trên một triệu token. Không giữ `deepseek-chat` hay `deepseek-reasoner` đã ngừng hỗ trợ, không đoán ID từ nhãn snapshot. Model chưa có giá bị từ chối trước provider.

OpenAI `text-embedding-3-small` có giá 0.02 và `text-embedding-3-large` có giá 0.13 USD trên một triệu token. Cả hai yêu cầu 1536 chiều để khớp thiết kế pgvector của các module sau; cache hiện dùng `Float[]` vì không tìm kiếm tương đồng. Model fake có namespace cache riêng.

## Quyết định: gateway tự điều khiển từng bước

AI SDK có hook nhiều bước nhưng gateway tự gọi một bước mỗi lần. Cách này giữ kiểm tra ngân sách trước từng request, retry và circuit breaker theo provider, redact kết quả tool trước khi gửi lại, và đúng một usage log mỗi bước trong cùng một nơi. Adapter không thực thi tool và đặt `maxRetries: 0`.

JSON mode dùng response format JSON của provider. Gateway parse và kiểm tra Zod, tối đa hai lần thử validate; adapter không tự thêm vòng retry validate. DeepSeek yêu cầu từ `json` trong prompt. Thông báo sửa kết quả không chứa giá trị riêng tư từ output sai.

Stream phát text thực từ adapter, ghi usage khi kết thúc bước và không retry sau khi đã phát text. Timeout mỗi lần thử chat là 30 giây, embed là 15 giây. Stream có token đầu trong 10 giây, idle tối đa 15 giây khi đang chờ chunk tiếp theo. Client ngừng đọc 30 giây thì abort và release probe, không ghi failure của provider. Không giới hạn tổng thời gian của stream khỏe để tránh cắt câu trả lời dài và mở circuit nhầm. Retry chỉ lỗi 429, 5xx hoặc mạng, tối đa hai lần với backoff 500 rồi 1500 ms và jitter. Năm request lỗi liên tiếp mở mạch 60 giây; chỉ một probe sau cửa sổ đó.

Tắt telemetry và ghi input/output của SDK để nội dung prompt không đi sang hệ thống trace. Chỉ lưu metadata usage, hash actor và mã lỗi an toàn.

## Xác minh cục bộ và review

- SDK adapter, các bước gateway và PostgreSQL đã được chạy, không chỉ kiểm tra type. Smoke ghi 9 dòng cho chat, hai lần JSON, ba bước tool, hai lời gọi embed và một stream; cache chỉ gọi embedding provider một lần, vector fake 1536 chiều; lượt ẩn danh thứ 16 bị chặn.
- SDK DeepSeek thật gọi HTTP server cục bộ trả 500: đúng 5 request, 3 usage log cho hai bước lỗi và một bước bị chặn; bước bị chặn mất 4.88 ms và không gọi HTTP thêm. Request đã redact PII. Đây là kiểm tra SDK/protocol cục bộ, không phải API DeepSeek thật.
- Giao diện `/admin/ai` đã chạy trên desktop và viewport 390 px. Ngân sách lưu qua API và reload giữ giá trị; input rỗng bị chặn, ngân sách 0 yêu cầu xác nhận; lỗi báo cáo không hiện KPI 0 giả; sales có input disabled, không có nút lưu, bộ lọc vẫn hoạt động và PUT thật trả 403.
- Claude Opus đã review qua Herdr. Đã sửa JSON bị hỏng khi redact số tool, stream treo giữ probe, input ngân sách rỗng, mã lỗi usage không phân biệt nguyên nhân, mất lượt chat khi provider lỗi, thiếu log khi production chưa cấu hình, chi phí stream dang dở và KPI/style lỗi. `.env.example` giữ LF.
- Review vòng hai phát hiện timeout tổng 30 giây có thể mở circuit nhầm với câu trả lời dài hoặc client chậm. Đã tách timeout idle provider khỏi timeout consumer; regression dùng fake timer kiểm tra stream khỏe 50 giây và probe được nhả mà không phải chờ thêm cửa sổ mở 60 giây.
- Review vòng ba xác nhận hết High/Medium và phát hiện lease probe cũ có thể nhả probe mới cùng thế hệ. State hiện giữ đúng object lease; release, success và failure của probe cũ đều bị bỏ qua. Regression tái hiện trước sửa và PASS sau sửa.
- Review vòng bốn của Claude qua Herdr xác nhận đã sửa lỗi ownership probe và không còn finding có thể xử lý ở bất kỳ severity nào. Nguy cơ vượt ngân sách khi request đồng thời và thiếu xác minh API thật vẫn là giới hạn được ghi rõ.
- Smoke Node với đồng hồ thật xác nhận stream khỏe 35.06 giây hoàn tất, idle provider bị hủy ở 15.01 giây và consumer giữ probe bị hủy ở 30.00 giây. Khi probe mới đang chạy, consumer cũ nhả lần nữa vẫn không cho request thứ ba vào; probe mới thành công đóng circuit bình thường.
- Embedding single-flight đăng ký trước truy vấn cache để hai snapshot cũ không tạo request trùng; chỉ resolve sau khi upsert thành công. Lỗi đọc/ghi cache, lỗi provider, cache hit và mảng rỗng đều có một log.
- Full suite cuối: 361 test PASS, 4 test live SKIP vì chưa bật cấu hình API thật; coverage toàn repo 89.34% dòng, facade `service.ts` 94.59%. Mỗi file gateway có logic đạt gate ít nhất 80% dòng, adapter thấp nhất 80.43%; không hạ ngưỡng hay loại file để đạt gate. `pnpm lint`, `pnpm typecheck` và `pnpm build` PASS; build frontend còn cảnh báo chunk 668.23 kB.
- p95 và số lượt là số liệu gateway gồm cả blocked/cache, đúng định nghĩa dòng Decision 8, không đổi thành độ trễ provider. HTTP report giới hạn 92 ngày để tránh query toàn bộ lịch sử.
- Ngân sách là ngưỡng trước từng bước dựa trên usage đã ghi, không phải reservation hoặc trần hóa đơn tuyệt đối. Cơ chế reservation cần một quyết định riêng về giới hạn token và request đồng thời, không tự đoán giá hoặc chặn thêm luồng trong lần cutover này.

## Phần chưa thể xác minh

Kiểm tra môi trường chỉ báo sự hiện diện, không xuất giá trị khóa. Hiện thiếu `LLM_API_KEY`, `LLM_MODEL` và `EMBEDDING_API_KEY`. Chưa chạy chat, JSON, tool calling hoặc embedding với API thật. Không đánh dấu AI-00 hay contract test thật là đạt chỉ từ tài liệu hoặc HTTP giả lập.

Sau khi người vận hành cung cấp khóa và chọn model, chạy:

```bash
AI_LIVE_TESTS=1 pnpm vitest run --project integration apps/backend/test/integration/modules/ai-gateway
```

Các test live phát sinh chi phí. Khi bật live nhưng thiếu điều kiện cấu hình, test phải báo lỗi thay vì tự bỏ qua.
