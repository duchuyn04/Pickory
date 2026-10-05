import { createDeepSeek } from '@ai-sdk/deepseek';
import {
  asSchema,
  generateText,
  isStepCount,
  jsonSchema,
  Output,
  streamText,
  tool,
  type JSONSchema7,
  type OutputInterface,
  type Tool,
} from 'ai';
import type { LanguageModelUsage } from 'ai';
import { ProviderError } from '../errors';
import type { Config } from '../../../shared/config';
import { redactPII } from '../../../shared/privacy';
import type {
  ChatChunk,
  ChatMessage,
  ChatProvider,
  ChatResult,
  ProviderChatRequest,
  ToolCall,
  ToolDefinition,
} from '../types';
import { mapProviderError } from './error-mapper';

// Tùy chọn cấu hình cho DeepSeek provider hỗ trợ truyền hàm fetch tùy biến cho kiểm thử.
export type DeepSeekProviderOptions = {
  fetch?: typeof fetch;
};

// Định nghĩa đặc tả xuất JSON thô để gửi cờ response_format JSON mà không ép SDK parse schema.
// Kế thừa các hàm xử lý văn bản từ Output.text() để giữ nguyên dữ liệu thô cho gateway xác thực Zod.
const rawJsonOutput: OutputInterface<string, string, never> = {
  ...Output.text(),
  name: 'json',
  responseFormat: Promise.resolve({ type: 'json' as const }),
};

// Chuyển đổi danh sách tin nhắn từ Pickory sang định dạng ModelMessage của AI SDK 7.
function convertMessages(messages: ChatMessage[]) {
  return messages.map((msg) => {
    // Tin nhắn hệ thống đóng vai trò chỉ dẫn ngữ cảnh cấp cao cho model.
    if (msg.role === 'system') {
      return { role: 'system' as const, content: msg.content };
    }
    // Tin nhắn từ người dùng yêu cầu phản hồi từ trợ lý ảo.
    if (msg.role === 'user') {
      return { role: 'user' as const, content: msg.content };
    }
    // Tin nhắn từ trợ lý: có thể chứa văn bản thuần hoặc danh sách lệnh gọi tool đã phát sinh.
    if (msg.role === 'assistant') {
      if (msg.toolCalls && msg.toolCalls.length > 0) {
        const parts: Array<
          | { type: 'text'; text: string }
          | { type: 'tool-call'; toolCallId: string; toolName: string; input: unknown }
        > = [];
        if (msg.content) {
          parts.push({ type: 'text', text: msg.content });
        }
        for (const tc of msg.toolCalls) {
          parts.push({
            type: 'tool-call',
            toolCallId: tc.id,
            toolName: tc.name,
            input: tc.input,
          });
        }
        return { role: 'assistant' as const, content: parts };
      }
      return { role: 'assistant' as const, content: msg.content };
    }
    // Tin nhắn kết quả thực thi tool cần định dạng tool-result kèm mã định danh lời gọi.
    return {
      role: 'tool' as const,
      content: [
        {
          type: 'tool-result' as const,
          toolCallId: msg.toolCallId,
          toolName: msg.toolName,
          output: {
            type: 'text' as const,
            value: msg.content,
          },
        },
      ],
    };
  });
}

// Đệ quy làm sạch PII trong các giá trị chuỗi của cây JSON Schema, duy trì nguyên vẹn các từ khóa cấu trúc.
function redactSchemaNode(node: unknown): unknown {
  if (typeof node === 'string') {
    return redactPII(node);
  }
  if (Array.isArray(node)) {
    return node.map((item) => redactSchemaNode(item));
  }
  if (node && typeof node === 'object') {
    const result: Record<string, unknown> = {};
    for (const [key, value] of Object.entries(node)) {
      // Giữ nguyên các từ khóa cấu trúc chuẩn của JSON Schema để tránh phá vỡ giao thức của model.
      if (
        key === 'type' ||
        key === 'format' ||
        key === '$schema' ||
        key === '$ref' ||
        key === 'additionalProperties'
      ) {
        result[key] = value;
      } else if (typeof value === 'string') {
        // Làm sạch PII trong các trường nội dung như description, title, default hoặc example.
        result[key] = redactPII(value);
      } else {
        result[key] = redactSchemaNode(value);
      }
    }
    return result;
  }
  return node;
}

// Chuyển đổi định dạng công cụ từ Pickory sang SDK tool, loại bỏ hàm execute để gateway tự điều khiển.
function convertTools(tools?: ToolDefinition[]): Record<string, Tool> | undefined {
  if (!tools || tools.length === 0) {
    return undefined;
  }
  const toolRecord: Record<string, Tool> = {};
  for (const item of tools) {
    // Ẩn PII trong mô tả tool để bảo vệ dữ liệu trước khi gửi sang model ngoại vi.
    const sanitizedDescription = redactPII(item.description);

    // Chuyển Zod schema sang JSON Schema và làm sạch PII đệ quy trong các trường mô tả hoặc giá trị mẫu.
    const rawSchema = asSchema(item.parameters);
    const resolvedJsonSchema =
      typeof rawSchema.jsonSchema === 'function'
        ? (rawSchema.jsonSchema as () => unknown)()
        : rawSchema.jsonSchema;
    const sanitizedJsonSchema = redactSchemaNode(resolvedJsonSchema) as JSONSchema7;

    // Sử dụng jsonSchema với hàm validate luôn chấp nhận để không từ chối input lỗi trước khi gateway xử lý;
    // gateway tự chịu trách nhiệm parse Zod gốc và gửi phản hồi lỗi lại cho LLM.
    const permissiveToolSchema = jsonSchema(sanitizedJsonSchema, {
      validate: (val) => ({ success: true, value: val }),
    });

    // Bỏ hàm execute để ranh giới gateway nắm quyền thực thi tool theo từng bước.
    toolRecord[item.name] = tool({
      description: sanitizedDescription,
      inputSchema: permissiveToolSchema,
    });
  }
  return toolRecord;
}

// Khởi tạo adapter DeepSeek kết nối qua Vercel AI SDK tuân thủ hợp đồng ChatProvider.
export function createDeepSeekProvider(
  config: Config,
  options: DeepSeekProviderOptions = {},
): ChatProvider {
  // Yêu cầu bắt buộc cấu hình tên model cụ thể; không dùng model mặc định ngầm khi chưa có chỉ định.
  const modelName = config.LLM_MODEL.trim();
  if (!modelName) {
    throw new ProviderError(400, false);
  }
  // Khởi tạo DeepSeek provider của AI SDK với khóa và URL cấu hình linh hoạt.
  const deepseek = createDeepSeek({
    apiKey: config.LLM_API_KEY,
    baseURL: config.LLM_BASE_URL,
    fetch: options.fetch,
  });

  // Tạo thể hiện language model chat tương ứng với mã định danh được cấu hình.
  const languageModel = deepseek.chat(modelName);

  return {
    name: 'deepseek',
    model: modelName,

    async chat(req: ProviderChatRequest, signal: AbortSignal): Promise<ChatResult> {
      // Chuyển đổi tin nhắn và công cụ sang cấu trúc chuẩn của SDK trước khi gọi API.
      const sdkMessages = convertMessages(req.messages);
      const sdkTools = convertTools(req.tools);
      // Khi bật json mode, kích hoạt định dạng JSON để model trả về đối tượng cấu trúc.
      const outputSpec = req.json ? rawJsonOutput : undefined;

      try {
        // Thực hiện sinh văn bản đúng một bước, tắt retry nội bộ của SDK để gateway quản lý.
        const result = await generateText({
          model: languageModel,
          messages: sdkMessages,
          tools: sdkTools,
          abortSignal: signal,
          maxRetries: 0,
          telemetry: { isEnabled: false, recordInputs: false, recordOutputs: false },
          allowSystemInMessages: true,
          stopWhen: isStepCount(1),
          // Tắt tính năng suy nghĩ nhiều bước (thinking) để không làm mất reasoning_content giữa các lượt gọi.
          providerOptions: {
            deepseek: {
              thinking: {
                type: 'disabled',
              },
            },
          },
          ...(req.maxTokens !== undefined ? { maxOutputTokens: req.maxTokens } : {}),
          ...(req.temperature !== undefined ? { temperature: req.temperature } : {}),
          ...(outputSpec ? { output: outputSpec } : {}),
        });

        // Trích xuất các lời gọi tool nếu model yêu cầu gọi công cụ trong bước hiện tại.
        let extractedToolCalls: ToolCall[] | undefined;
        if (result.toolCalls && result.toolCalls.length > 0) {
          extractedToolCalls = [];
          for (const call of result.toolCalls) {
            extractedToolCalls.push({
              id: call.toolCallId,
              name: call.toolName,
              input: call.input,
            });
          }
        }

        // Chuẩn hóa token usage trích xuất từ phản hồi của model bao gồm cả cache hit.
        const usageData = result.usage;
        return {
          text: result.text ?? '',
          usage: {
            inputTokens: usageData?.inputTokens ?? 0,
            outputTokens: usageData?.outputTokens ?? 0,
            cachedTokens: usageData?.inputTokenDetails?.cacheReadTokens ?? 0,
          },
          ...(extractedToolCalls ? { toolCalls: extractedToolCalls } : {}),
        };
      } catch (error) {
        // Ánh xạ mọi ngoại lệ thành ProviderError an toàn để không lộ khóa hoặc thông tin nhạy cảm.
        throw mapProviderError(error);
      }
    },

    async *stream(req: ProviderChatRequest, signal: AbortSignal): AsyncIterable<ChatChunk> {
      // Chuẩn bị tin nhắn, công cụ và cấu hình định dạng xuất trước khi mở kết nối stream.
      const sdkMessages = convertMessages(req.messages);
      const sdkTools = convertTools(req.tools);
      const outputSpec = req.json ? rawJsonOutput : undefined;

      let capturedCallbackError: unknown;
      let streamResult;

      try {
        // Mở luồng stream từ model với số lần retry bằng 0 để bộ bọc Reliability kiểm soát.
        streamResult = streamText({
          model: languageModel,
          messages: sdkMessages,
          tools: sdkTools,
          abortSignal: signal,
          maxRetries: 0,
          telemetry: { isEnabled: false, recordInputs: false, recordOutputs: false },
          allowSystemInMessages: true,
          stopWhen: isStepCount(1),
          // Tắt suy nghĩ nhiều bước để hỗ trợ temperature tùy chỉnh và vòng lặp công cụ chuẩn mực.
          providerOptions: {
            deepseek: {
              thinking: {
                type: 'disabled',
              },
            },
          },
          ...(req.maxTokens !== undefined ? { maxOutputTokens: req.maxTokens } : {}),
          ...(req.temperature !== undefined ? { temperature: req.temperature } : {}),
          ...(outputSpec ? { output: outputSpec } : {}),
          onError(event) {
            // Ghi nhận lỗi phát sinh từ callback của SDK để không bỏ sót ngoại lệ ngầm.
            capturedCallbackError = event.error;
          },
        });
      } catch (error) {
        // Bắt lỗi đồng bộ xảy ra ngay khi khởi tạo luồng stream.
        throw mapProviderError(error);
      }

      let finalUsage: LanguageModelUsage | undefined;
      const streamedToolCalls: ToolCall[] = [];

      try {
        // Lặp qua từng phần tử của luồng stream để phát text delta ra ngoài theo thời gian thực.
        for await (const part of streamResult.stream) {
          if (part.type === 'text-delta') {
            yield { type: 'text', text: part.text };
          } else if (part.type === 'tool-call') {
            streamedToolCalls.push({
              id: part.toolCallId,
              name: part.toolName,
              input: part.input,
            });
          } else if (part.type === 'finish-step') {
            if (part.usage) {
              finalUsage = part.usage;
            }
          } else if (part.type === 'finish') {
            if (part.totalUsage) {
              finalUsage = part.totalUsage;
            }
          } else if (part.type === 'error') {
            // Ném lỗi ngay khi luồng stream phát sự kiện error từ máy chủ.
            throw mapProviderError(part.error);
          }
        }

        // Kiểm tra lỗi bắt giữ từ callback nếu luồng kết thúc mà không kịp ném ngoại lệ.
        if (capturedCallbackError) {
          throw mapProviderError(capturedCallbackError);
        }

        // Đọc dự phòng thông tin token usage nếu các sự kiện finish không mang đủ dữ liệu.
        if (!finalUsage) {
          try {
            finalUsage = await streamResult.usage;
          } catch (usageError) {
            // Không nuốt lỗi đọc usage; ném lỗi để gateway không ghi nhận log 0 token miễn phí sai lệch.
            throw mapProviderError(usageError);
          }
        }

        // Bắt buộc phải có dữ liệu usage hợp lệ; nếu thiếu hoàn toàn phải báo lỗi nhà cung cấp.
        if (
          !finalUsage ||
          (finalUsage.inputTokens === undefined && finalUsage.outputTokens === undefined)
        ) {
          throw new ProviderError(undefined, false);
        }

        // Phát chunk hoàn tất chứa tổng lượng token tiêu thụ và danh sách tool calls nếu có.
        yield {
          type: 'finish',
          usage: {
            inputTokens: finalUsage?.inputTokens ?? 0,
            outputTokens: finalUsage?.outputTokens ?? 0,
            cachedTokens: finalUsage?.inputTokenDetails?.cacheReadTokens ?? 0,
          },
          ...(streamedToolCalls.length > 0 ? { toolCalls: streamedToolCalls } : {}),
        };
      } catch (error) {
        // Ánh xạ lỗi stream sang ProviderError để bộ bọc độ tin cậy xử lý retry hoặc mở mạch.
        throw mapProviderError(error);
      }
    },
  };
}
