import type {
  ChatChunk,
  ChatProvider,
  ChatResult,
  EmbeddingProvider,
  ProviderChatRequest,
  TokenUsage,
} from './types';

export type ScriptedChatAction =
  | { type: 'result'; result: ChatResult }
  | { type: 'error'; error: Error }
  | { type: 'handler'; handle: (req: ProviderChatRequest) => Promise<ChatResult> | ChatResult };

export type ScriptedStreamAction =
  { type: 'chunks'; chunks: ChatChunk[] } | { type: 'error'; error: Error };

// Provider chat giả lập phục vụ unit test và môi trường dev không có API key.
export class FakeChatProvider implements ChatProvider {
  readonly name: string;
  readonly model: string;
  readonly recordedRequests: ProviderChatRequest[] = [];
  private readonly scriptedActions: ScriptedChatAction[] = [];
  private readonly scriptedStreams: ScriptedStreamAction[] = [];

  constructor(options: { name?: string; model?: string } = {}) {
    this.name = options.name ?? 'fake';
    this.model = options.model ?? 'fake-chat';
  }

  // Thêm phản hồi hoặc lỗi vào hàng đợi để kịch bản test diễn ra có thứ tự.
  enqueueResponse(result: ChatResult): void {
    this.scriptedActions.push({ type: 'result', result });
  }

  // Ném lỗi giả lập để kiểm tra cơ chế retry hoặc circuit breaker.
  enqueueError(error: Error): void {
    this.scriptedActions.push({ type: 'error', error });
  }

  // Xử lý động theo từng request cụ thể trong các bài test phức tạp.
  enqueueHandler(handle: (req: ProviderChatRequest) => Promise<ChatResult> | ChatResult): void {
    this.scriptedActions.push({ type: 'handler', handle });
  }

  // Thêm chuỗi chunks phục vụ kiểm thử luồng stream.
  enqueueStreamChunks(chunks: ChatChunk[]): void {
    this.scriptedStreams.push({ type: 'chunks', chunks });
  }

  // Gây lỗi giữa hoặc đầu luồng stream.
  enqueueStreamError(error: Error): void {
    this.scriptedStreams.push({ type: 'error', error });
  }

  // Xóa danh sách request đã ghi để cô lập giữa các bước kiểm thử.
  clearRecorded(): void {
    this.recordedRequests.length = 0;
  }

  // Thực hiện chat giả lập: ghi lại bản sao request và trả kết quả theo kịch bản.
  async chat(req: ProviderChatRequest): Promise<ChatResult> {
    // Sao chép sâu đối tượng request để ngăn thay đổi ngoại vi sau khi gọi hàm.
    this.recordedRequests.push(JSON.parse(JSON.stringify(req)));

    // Lấy hành động được lập trình trước nếu có trong hàng đợi.
    const scripted = this.scriptedActions.shift();
    if (scripted) {
      if (scripted.type === 'error') {
        // Ném lỗi giả lập lập tức theo kịch bản kiểm thử.
        throw scripted.error;
      }
      if (scripted.type === 'handler') {
        // Thực thi hàm tính toán kết quả động.
        return await scripted.handle(req);
      }
      return scripted.result;
    }

    // Trả lời mặc định an toàn cho môi trường thử nghiệm khi không có kịch bản riêng.
    return {
      text: 'Xin chào từ fake AI',
      usage: { inputTokens: 10, outputTokens: 5, cachedTokens: 0 },
    };
  }

  // Phát luồng chunk giả lập: hỗ trợ kiểm thử text chunk và finish chunk.
  async *stream(req: ProviderChatRequest): AsyncIterable<ChatChunk> {
    // Lưu lại request để assert nội dung prompt và thông tin tool.
    this.recordedRequests.push(JSON.parse(JSON.stringify(req)));

    const scripted = this.scriptedStreams.shift();
    if (scripted) {
      if (scripted.type === 'error') {
        // Gây lỗi stream theo kịch bản.
        throw scripted.error;
      }
      for (const chunk of scripted.chunks) {
        // Trả từng chunk theo đúng thứ tự được lập trình trước.
        yield chunk;
      }
      return;
    }

    // Luồng mặc định phát ra 2 chunk chữ và 1 chunk hoàn thành.
    yield { type: 'text', text: 'Xin chào ' };
    yield { type: 'text', text: 'từ fake AI' };
    yield {
      type: 'finish',
      usage: { inputTokens: 10, outputTokens: 5, cachedTokens: 0 },
    };
  }
}

// Provider embedding giả lập với model fake-embedding để không ghi đè vector giả vào cache của model thật.
export class FakeEmbeddingProvider implements EmbeddingProvider {
  readonly name: string;
  readonly model: string;
  readonly recordedRequests: string[][] = [];
  private readonly scriptedActions: Array<
    | { type: 'result'; result: { vectors: number[][]; usage: TokenUsage } }
    | { type: 'error'; error: Error }
  > = [];

  constructor(options: { name?: string; model?: string } = {}) {
    this.name = options.name ?? 'fake';
    // Đặt mặc định là fake-embedding để tách biệt hoàn toàn không gian cache với text-embedding-3-small thật.
    this.model = options.model ?? 'fake-embedding';
  }

  // Lập trình trước phản hồi embedding.
  enqueueResponse(result: { vectors: number[][]; usage: TokenUsage }): void {
    this.scriptedActions.push({ type: 'result', result });
  }

  // Lập trình trước lỗi embedding.
  enqueueError(error: Error): void {
    this.scriptedActions.push({ type: 'error', error });
  }

  // Xóa danh sách request đã ghi lại.
  clearRecorded(): void {
    this.recordedRequests.length = 0;
  }

  // Thực hiện embedding: trả về vector 1536 phần tử tất định cho mỗi chuỗi text đầu vào.
  async embed(texts: string[]): Promise<{ vectors: number[][]; usage: TokenUsage }> {
    // Ghi lại mảng chuỗi nhận được để kiểm tra tính năng redact PII trước khi gọi provider.
    this.recordedRequests.push([...texts]);

    const scripted = this.scriptedActions.shift();
    if (scripted) {
      if (scripted.type === 'error') {
        // Ném lỗi giả lập theo kịch bản.
        throw scripted.error;
      }
      return scripted.result;
    }

    // Sinh vector giả lập có đúng 1536 chiều, loại bỏ phụ thuộc vào textIndex để cùng text luôn sinh cùng vector.
    const vectors = texts.map((text) => {
      const baseValue = (text.length % 100) / 1000;
      return Array.from({ length: 1536 }, (_, dimIndex) => {
        // Chỉ phụ thuộc vào nội dung chuỗi và chỉ số chiều để đảm bảo tính tất định tuyệt đối.
        return Number((baseValue + (dimIndex % 10) * 0.01).toFixed(6));
      });
    });

    // Tính lượng token xấp xỉ 1 token cho mỗi 4 ký tự đầu vào.
    const totalTokens = texts.reduce(
      (sum, item) => sum + Math.max(1, Math.ceil(item.length / 4)),
      0,
    );

    return {
      vectors,
      usage: {
        inputTokens: totalTokens,
        outputTokens: 0,
        cachedTokens: 0,
      },
    };
  }
}
