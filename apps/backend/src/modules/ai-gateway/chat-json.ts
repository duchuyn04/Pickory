import type { ZodIssue, ZodType } from 'zod';
import type { PrismaClient } from '../../generated/prisma/client';
import { redactPII, redactPIIValue } from '../../shared/privacy';
import { assertWithinBudget } from './budget';
import { AiBudgetExceededError, AiInvalidOutputError, getAiErrorCode } from './errors';
import type { Reliability } from './reliability';
import { getAiSettings } from './settings';
import type {
  AiSettings,
  CallContext,
  ChatMessage,
  ChatProvider,
  ChatRequest,
  ChatResult,
  ProviderChatRequest,
} from './types';
import { persistUsageLog } from './usage-log';

// Chuẩn hóa và làm sạch thông báo lỗi từ Zod để tránh rò rỉ dữ liệu cá nhân trong thông báo lỗi.
function sanitizeZodIssues(issues: ZodIssue[]): string {
  // Chỉ lấy đường dẫn trường và mô tả lỗi tổng quát, tuyệt đối không đưa giá trị người dùng nhập vào.
  const descriptions = issues.map((issue) => {
    const fieldPath = issue.path.length > 0 ? issue.path.join('.') : 'root';
    return `${fieldPath}: ${issue.message}`;
  });

  // Chạy thêm redactPII qua chuỗi lỗi để bảo đảm an toàn dữ liệu nhiều lớp.
  return redactPII(descriptions.join('; '));
}

// Làm sạch thông tin PII trong toàn bộ các tin nhắn gửi đi tới provider.
function redactMessages(messages: ChatMessage[]): ChatMessage[] {
  return messages.map((msg) => {
    // Ẩn thông tin nhạy cảm trong nội dung văn bản tin nhắn.
    const cleanContent = redactPII(msg.content);
    if (msg.role === 'assistant' && msg.toolCalls) {
      // Ẩn PII trong dữ liệu gọi công cụ của trợ lý.
      return {
        role: 'assistant',
        content: cleanContent,
        toolCalls: msg.toolCalls.map((tc) => ({
          id: tc.id,
          name: tc.name,
          input: redactPIIValue(tc.input),
        })),
      };
    }
    if (msg.role === 'tool') {
      return {
        role: 'tool',
        toolCallId: msg.toolCallId,
        toolName: msg.toolName,
        content: cleanContent,
      };
    }
    return {
      role: msg.role,
      content: cleanContent,
    };
  });
}

// Thực hiện gọi AI có cấu trúc JSON với tối đa đúng 2 lần thử validate schema.
export async function executeChatJson<T>(params: {
  provider: ChatProvider;
  reliability: Reliability;
  store: PrismaClient;
  settings: AiSettings;
  req: ChatRequest;
  schema: ZodType<T>;
  ctx: CallContext;
  now?: () => number;
}): Promise<T> {
  const currentNow = params.now ?? Date.now;

  // Làm sạch PII của các tin nhắn khởi tạo ban đầu.
  let currentMessages = redactMessages(params.req.messages);

  // Đảm bảo prompt luôn chứa chỉ dẫn hệ thống yêu cầu trả JSON hợp lệ để kích hoạt chế độ cấu trúc của model.
  const hasSystemJsonInstruction = currentMessages.some(
    (m) => m.role === 'system' && m.content.toLowerCase().includes('json'),
  );
  if (!hasSystemJsonInstruction) {
    currentMessages = [
      {
        role: 'system',
        content: 'Respond with a valid JSON object matching the requested schema.',
      },
      ...currentMessages,
    ];
  }

  // Chỉ cho phép đúng 2 lượt gọi model: lần 1 và tối đa 1 lần thử lại khi sai schema.
  for (let attempt = 1; attempt <= 2; attempt += 1) {
    // Nạp lại cấu hình cài đặt mới nhất trước mỗi lượt gọi để cập nhật kịp thời nếu admin vừa đổi ngân sách.
    const stepSettings = await getAiSettings(params.store);

    // Kiểm tra ngân sách trước mỗi bước gọi model để không vượt quá hạn mức cho phép.
    try {
      await assertWithinBudget({
        feature: params.ctx.feature,
        monthlyBudgetUsd: stepSettings.monthlyBudgetUsd,
        store: params.store,
        now: currentNow,
      });
    } catch (budgetError) {
      if (budgetError instanceof AiBudgetExceededError) {
        // Ghi nhận một dòng nhật ký trạng thái blocked khi bị chặn bởi ngân sách theo Decision 8.
        await persistUsageLog(params.store, {
          feature: params.ctx.feature,
          provider: params.provider.name,
          model: params.provider.model,
          promptVersion: params.ctx.promptVersion,
          latencyMs: 0,
          status: 'blocked',
          errorCode: 'BUDGET_EXCEEDED',
          actorKey: params.ctx.actorKey,
          costUsd: 0,
        });
      }
      throw budgetError;
    }

    const requestPayload: ProviderChatRequest = {
      ...params.req,
      messages: currentMessages,
      json: true,
    };

    const startTime = currentNow();
    let chatResult: ChatResult;

    try {
      // Gọi provider qua lớp Reliability để tự động retry lỗi mạng và kích hoạt circuit breaker.
      chatResult = await params.reliability.run(params.provider.name, 30000, async (signal) => {
        return await params.provider.chat(requestPayload, signal);
      });
    } catch (error) {
      // Ghi nhật ký lỗi cho bước gọi thất bại để bảo toàn số liệu đối soát sự cố.
      await persistUsageLog(params.store, {
        feature: params.ctx.feature,
        provider: params.provider.name,
        model: params.provider.model,
        promptVersion: params.ctx.promptVersion,
        usage: { inputTokens: 0, outputTokens: 0, cachedTokens: 0 },
        latencyMs: currentNow() - startTime,
        status: 'error',
        errorCode: getAiErrorCode(error),
        actorKey: params.ctx.actorKey,
      });
      throw error;
    }

    const responseText = chatResult.text;
    const stepUsage = chatResult.usage;
    const latencyMs = currentNow() - startTime;

    // Thử giải mã JSON từ kết quả trả về của model.
    let parsedJson: unknown;
    let isJsonValid = false;
    let failureReason = '';

    try {
      parsedJson = JSON.parse(responseText);
      isJsonValid = true;
    } catch {
      failureReason = 'Phản hồi không phải là chuỗi JSON hợp lệ.';
    }

    // Chỉ đánh dấu thành công sau khi schema hợp lệ, tránh dashboard bỏ sót lỗi đầu ra của model.
    const validationResult = isJsonValid ? params.schema.safeParse(parsedJson) : undefined;
    await persistUsageLog(params.store, {
      feature: params.ctx.feature,
      provider: params.provider.name,
      model: params.provider.model,
      promptVersion: params.ctx.promptVersion,
      usage: stepUsage,
      latencyMs,
      status: validationResult?.success ? 'ok' : 'error',
      errorCode: validationResult?.success ? undefined : 'AI_INVALID_OUTPUT',
      actorKey: params.ctx.actorKey,
    });

    if (validationResult?.success) {
      return validationResult.data;
    }
    if (validationResult) {
      failureReason = `Dữ liệu không khớp schema: ${sanitizeZodIssues(validationResult.error.issues)}`;
    }

    // Nếu đã hết 2 lần thử mà vẫn sai schema thì ném lỗi chuẩn hóa theo hợp đồng.
    if (attempt === 2) {
      throw new AiInvalidOutputError();
    }

    // Chuẩn bị tin nhắn cho lần thử thứ hai: ẩn PII nội dung responseText của assistant trước khi gửi lại.
    currentMessages = [
      ...currentMessages,
      {
        role: 'assistant',
        content: redactPII(responseText),
      },
      {
        role: 'user',
        content: redactPII(
          `Dữ liệu trả về chưa đúng định dạng. Lý do: ${failureReason}. Vui lòng sửa lại và trả về đúng đối tượng JSON hợp lệ theo đúng cấu trúc yêu cầu.`,
        ),
      },
    ];
  }

  // Phương án dự phòng an toàn nếu vòng lặp kết thúc bất thường.
  throw new AiInvalidOutputError();
}
