import { z } from 'zod';
import type { PrismaClient } from '../../generated/prisma/client';
import { redactPII, redactPIIValue } from '../../shared/privacy';
import { assertWithinBudget } from './budget';
import { AiBudgetExceededError, getAiErrorCode } from './errors';
import type { Reliability } from './reliability';
import { getAiSettings } from './settings';
import type {
  AiSettings,
  CallContext,
  ChatChunk,
  ChatMessage,
  ChatProvider,
  ChatRequest,
  ChatResult,
  ProviderChatRequest,
  TokenUsage,
  ToolCall,
  ToolDefinition,
} from './types';
import { persistUsageLog } from './usage-log';

function estimateInterruptedStreamUsage(request: ProviderChatRequest, text: string): TokenUsage {
  const tools = request.tools?.map((tool) => ({
    name: tool.name,
    description: tool.description,
    parameters: z.toJSONSchema(tool.parameters, { io: 'input', unrepresentable: 'any' }),
  }));
  // Thiếu usage của provider thì dùng số byte UTF-8 làm proxy bảo thủ, không giả vờ đây là token đã đối soát.
  return {
    inputTokens: Buffer.byteLength(JSON.stringify({ messages: request.messages, tools }), 'utf8'),
    outputTokens: Buffer.byteLength(text, 'utf8'),
    cachedTokens: 0,
  };
}

// Làm sạch thông tin nhạy cảm khỏi danh sách công cụ được định nghĩa.
function redactToolDefinitions(tools?: ToolDefinition[]): ToolDefinition[] | undefined {
  if (!tools || tools.length === 0) return undefined;
  return tools.map((tool) => ({
    name: tool.name,
    // Ẩn thông tin nhạy cảm có thể xuất hiện trong mô tả công cụ.
    description: redactPII(tool.description),
    parameters: tool.parameters,
    execute: (params) => tool.execute(params),
  }));
}

// Làm sạch thông tin nhạy cảm trong toàn bộ chuỗi tin nhắn của hội thoại.
function redactConversationMessages(messages: ChatMessage[]): ChatMessage[] {
  return messages.map((msg) => {
    const cleanContent = redactPII(msg.content);
    if (msg.role === 'assistant' && msg.toolCalls) {
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

// Thực thi an toàn một lệnh gọi công cụ: bắt lỗi và ẩn dữ liệu nhạy cảm trước khi trả về cho LLM.
async function executeSingleToolCall(toolCall: ToolCall, tools: ToolDefinition[]): Promise<string> {
  const matchedTool = tools.find((t) => t.name === toolCall.name);
  // Thông báo rõ ràng cho LLM khi công cụ yêu cầu không tồn tại trong danh sách đăng ký.
  if (!matchedTool) {
    return redactPII(`Lỗi: Công cụ "${toolCall.name}" không tồn tại.`);
  }

  // Xác thực tham số đầu vào qua schema Zod của công cụ trước khi thực thi.
  const parseResult = matchedTool.parameters.safeParse(toolCall.input);
  if (!parseResult.success) {
    const errorDetails = parseResult.error.issues
      .map((i) => `${i.path.join('.')}: ${i.message}`)
      .join(', ');
    return redactPII(`Lỗi tham số đầu vào cho công cụ "${toolCall.name}": ${errorDetails}`);
  }

  try {
    // Chạy logic thực thi công cụ do module nghiệp vụ cung cấp.
    const rawResult = await matchedTool.execute(parseResult.data);
    // Chuẩn hóa giá trị undefined sang null để JSON.stringify không sinh chuỗi rỗng gây lỗi redactPII.
    const normalizedResult = rawResult === undefined ? null : rawResult;
    const textResult =
      typeof normalizedResult === 'string'
        ? normalizedResult
        : JSON.stringify(redactPIIValue(normalizedResult));
    // Ẩn số điện thoại, email, số thẻ ngân hàng trong kết quả trả về của công cụ.
    return redactPII(textResult);
  } catch {
    // Tuyệt đối không đưa chi tiết exception thô vào prompt vì có thể làm lộ khóa API hoặc token kết nối.
    return redactPII(`Lỗi: Thao tác thực thi công cụ "${toolCall.name}" không thành công.`);
  }
}

// Chạy vòng lặp gọi công cụ nhiều bước cho hàm chat đồng bộ.
export async function executeChatWithToolLoop(params: {
  provider: ChatProvider;
  reliability: Reliability;
  store: PrismaClient;
  settings: AiSettings;
  req: ChatRequest;
  ctx: CallContext;
  now?: () => number;
}): Promise<ChatResult> {
  const currentNow = params.now ?? Date.now;
  const maxSteps = params.req.maxSteps ?? (params.req.tools && params.req.tools.length > 0 ? 5 : 1);
  const tools = params.req.tools ?? [];

  const currentMessages = [...params.req.messages];
  const cleanTools = redactToolDefinitions(params.req.tools);

  const accumulatedUsage: TokenUsage = { inputTokens: 0, outputTokens: 0, cachedTokens: 0 };
  let lastResultText = '';
  let lastToolCalls: ToolCall[] | undefined;

  for (let step = 1; step <= maxSteps; step += 1) {
    // Nạp lại cấu hình cài đặt mới nhất ở mỗi bước để phản ánh tức thì các thay đổi hạn mức từ phía admin.
    const stepSettings = await getAiSettings(params.store);

    // Kiểm tra ngân sách trước từng bước gọi LLM; nếu vượt ngưỡng thì dừng ngay lập tức.
    try {
      await assertWithinBudget({
        feature: params.ctx.feature,
        monthlyBudgetUsd: stepSettings.monthlyBudgetUsd,
        store: params.store,
        now: currentNow,
      });
    } catch (budgetError) {
      if (budgetError instanceof AiBudgetExceededError) {
        // Ghi nhận một dòng nhật ký blocked khi bước bị chặn do ngân sách.
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

    // Làm sạch PII trên toàn bộ tin nhắn tại MỖI vòng lặp để bảo vệ cả dữ liệu do tool và assistant vừa sinh ra.
    const cleanMessagesForStep = redactConversationMessages(currentMessages);

    const requestPayload: ProviderChatRequest = {
      ...params.req,
      messages: cleanMessagesForStep,
      tools: cleanTools,
    };

    const startTime = currentNow();
    let stepResult: ChatResult;

    try {
      // Gọi provider qua lớp reliability quản lý timeout 30 giây và retry tự động.
      stepResult = await params.reliability.run(params.provider.name, 30000, async (signal) => {
        return await params.provider.chat(requestPayload, signal);
      });
    } catch (error) {
      // Ghi nhật ký lỗi cho bước gọi thất bại để bảo đảm đủ dữ liệu đối soát.
      await persistUsageLog(params.store, {
        feature: params.ctx.feature,
        provider: params.provider.name,
        model: params.provider.model,
        promptVersion: params.ctx.promptVersion,
        latencyMs: currentNow() - startTime,
        status: 'error',
        errorCode: getAiErrorCode(error),
        actorKey: params.ctx.actorKey,
      });
      throw error;
    }

    const latencyMs = currentNow() - startTime;
    // Ghi nhận chi phí và token cho bước model thành công.
    await persistUsageLog(params.store, {
      feature: params.ctx.feature,
      provider: params.provider.name,
      model: params.provider.model,
      promptVersion: params.ctx.promptVersion,
      usage: stepResult.usage,
      latencyMs,
      status: 'ok',
      actorKey: params.ctx.actorKey,
    });

    accumulatedUsage.inputTokens += stepResult.usage.inputTokens;
    accumulatedUsage.outputTokens += stepResult.usage.outputTokens;
    accumulatedUsage.cachedTokens += stepResult.usage.cachedTokens;

    lastResultText = stepResult.text;
    lastToolCalls = stepResult.toolCalls;

    // Kết thúc vòng lặp nếu model không yêu cầu gọi công cụ nào hoặc đã chạm số bước tối đa.
    if (!stepResult.toolCalls || stepResult.toolCalls.length === 0 || step === maxSteps) {
      return {
        text: stepResult.text,
        usage: accumulatedUsage,
        toolCalls: stepResult.toolCalls,
      };
    }

    // Đưa kết quả của model vào hội thoại chuẩn bị cho bước tiếp theo.
    currentMessages.push({
      role: 'assistant',
      content: stepResult.text,
      toolCalls: stepResult.toolCalls,
    });

    // Chạy tuần tự các công cụ được yêu cầu và gắn kết quả đã làm sạch PII vào lịch sử tin nhắn.
    for (const toolCall of stepResult.toolCalls) {
      const toolOutput = await executeSingleToolCall(toolCall, tools);
      currentMessages.push({
        role: 'tool',
        toolCallId: toolCall.id,
        toolName: toolCall.name,
        content: toolOutput,
      });
    }
  }

  return {
    text: lastResultText,
    usage: accumulatedUsage,
    toolCalls: lastToolCalls,
  };
}

// Chạy luồng stream thực tế kèm thực thi công cụ nhiều bước: phát chunk ngay khi nhận được.
export async function* executeChatStreamWithToolLoop(params: {
  provider: ChatProvider;
  reliability: Reliability;
  store: PrismaClient;
  settings: AiSettings;
  req: ChatRequest;
  ctx: CallContext;
  now?: () => number;
}): AsyncIterable<ChatChunk> {
  const currentNow = params.now ?? Date.now;
  const maxSteps = params.req.maxSteps ?? (params.req.tools && params.req.tools.length > 0 ? 5 : 1);
  const tools = params.req.tools ?? [];

  const currentMessages = [...params.req.messages];
  const cleanTools = redactToolDefinitions(params.req.tools);

  const accumulatedUsage: TokenUsage = { inputTokens: 0, outputTokens: 0, cachedTokens: 0 };
  let lastToolCalls: ToolCall[] | undefined;

  for (let step = 1; step <= maxSteps; step += 1) {
    const stepSettings = await getAiSettings(params.store);

    // Kiểm tra ngân sách trước từng bước stream; chặn bước nếu chạm giới hạn chi phí.
    try {
      await assertWithinBudget({
        feature: params.ctx.feature,
        monthlyBudgetUsd: stepSettings.monthlyBudgetUsd,
        store: params.store,
        now: currentNow,
      });
    } catch (budgetError) {
      if (budgetError instanceof AiBudgetExceededError) {
        // Ghi nhật ký blocked nếu bước stream bị chặn ngân sách.
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

    // Làm sạch PII trên tin nhắn ở mỗi bước stream.
    const cleanMessagesForStep = redactConversationMessages(currentMessages);

    const requestPayload: ProviderChatRequest = {
      ...params.req,
      messages: cleanMessagesForStep,
      tools: cleanTools,
    };

    const startTime = currentNow();
    let stepUsage: TokenUsage = { inputTokens: 0, outputTokens: 0, cachedTokens: 0 };
    let stepToolCalls: ToolCall[] | undefined;
    let accumulatedStepText = '';
    let stepLogged = false;
    let usageKnown = false;
    let usageEstimated = false;

    try {
      // Mở luồng stream qua lớp reliability với cơ chế retry trước khi phát token đầu tiên.
      const streamIterable = params.reliability.stream(params.provider.name, (signal) => {
        return params.provider.stream(requestPayload, signal);
      });

      for await (const chunk of streamIterable) {
        if (chunk.type === 'text') {
          accumulatedStepText += chunk.text;
          // Phát ngay chunk văn bản ra bên ngoài để đảm bảo độ trễ thấp theo thời gian thực.
          yield chunk;
        } else if (chunk.type === 'finish') {
          stepUsage = chunk.usage;
          usageKnown = true;
          stepToolCalls = chunk.toolCalls;
        }
      }

      const latencyMs = currentNow() - startTime;
      // Ghi nhật ký sử dụng sau khi kết thúc bước stream của model.
      await persistUsageLog(params.store, {
        feature: params.ctx.feature,
        provider: params.provider.name,
        model: params.provider.model,
        promptVersion: params.ctx.promptVersion,
        usage: stepUsage,
        latencyMs,
        status: 'ok',
        actorKey: params.ctx.actorKey,
      });
      stepLogged = true;
    } catch (error) {
      stepLogged = true;
      if (accumulatedStepText && !usageKnown) {
        stepUsage = estimateInterruptedStreamUsage(requestPayload, accumulatedStepText);
        usageEstimated = true;
      }
      // Ghi nhật ký lỗi nếu luồng stream bị đứt gãy giữa chừng.
      await persistUsageLog(params.store, {
        feature: params.ctx.feature,
        provider: params.provider.name,
        model: params.provider.model,
        promptVersion: params.ctx.promptVersion,
        usage: stepUsage,
        latencyMs: currentNow() - startTime,
        status: 'error',
        errorCode: usageEstimated
          ? `STREAM_PARTIAL_ESTIMATED_${getAiErrorCode(error)}`
          : getAiErrorCode(error),
        actorKey: params.ctx.actorKey,
      });
      throw error;
    } finally {
      // Nếu người gọi hủy sớm (ví dụ client ngắt kết nối HTTP) trước khi ghi log hoàn tất, bắt buộc ghi log STREAM_CANCELLED.
      if (!stepLogged) {
        if (accumulatedStepText && !usageKnown) {
          stepUsage = estimateInterruptedStreamUsage(requestPayload, accumulatedStepText);
          usageEstimated = true;
        }
        const latencyMs = currentNow() - startTime;
        await persistUsageLog(params.store, {
          feature: params.ctx.feature,
          provider: params.provider.name,
          model: params.provider.model,
          promptVersion: params.ctx.promptVersion,
          usage: stepUsage,
          latencyMs,
          status: 'error',
          errorCode: usageEstimated ? 'STREAM_CANCELLED_PARTIAL_ESTIMATED' : 'STREAM_CANCELLED',
          actorKey: params.ctx.actorKey,
        });
      }
    }

    accumulatedUsage.inputTokens += stepUsage.inputTokens;
    accumulatedUsage.outputTokens += stepUsage.outputTokens;
    accumulatedUsage.cachedTokens += stepUsage.cachedTokens;
    lastToolCalls = stepToolCalls;

    // Dừng lặp nếu không có lệnh gọi công cụ nào hoặc đã hoàn thành số bước tối đa.
    if (!stepToolCalls || stepToolCalls.length === 0 || step === maxSteps) {
      // Phát chunk kết thúc tổng hợp lượng token tiêu thụ của toàn bộ các bước.
      yield {
        type: 'finish',
        usage: accumulatedUsage,
        toolCalls: stepToolCalls,
      };
      return;
    }

    // Cập nhật lịch sử hội thoại chuẩn bị cho bước gọi tiếp theo.
    currentMessages.push({
      role: 'assistant',
      content: accumulatedStepText,
      toolCalls: stepToolCalls,
    });

    for (const toolCall of stepToolCalls) {
      const toolOutput = await executeSingleToolCall(toolCall, tools);
      currentMessages.push({
        role: 'tool',
        toolCallId: toolCall.id,
        toolName: toolCall.name,
        content: toolOutput,
      });
    }
  }

  // Phát tín hiệu hoàn tất sau khi vượt qua bước cuối cùng.
  yield {
    type: 'finish',
    usage: accumulatedUsage,
    toolCalls: lastToolCalls,
  };
}
