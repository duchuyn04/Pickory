import { createHash } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import { db } from '../../../../src/shared/db';
import {
  AiBudgetExceededError,
  createAiGateway,
  FakeChatProvider,
  FakeEmbeddingProvider,
} from '../../../../src/modules/ai-gateway';
import { AI_SETTINGS_SINGLETON_ID } from '../../../../src/modules/ai-gateway/settings';

describe('AI Gateway và Database Integration', () => {
  it('ghi nhật ký sử dụng vào PostgreSQL khi gọi chat thành công', async () => {
    const fakeChat = new FakeChatProvider();
    const gateway = createAiGateway({
      chatProvider: fakeChat,
      store: db,
    });

    const actorKey = 'user:customer_database_test';
    const result = await gateway.chat(
      { messages: [{ role: 'user', content: 'Tìm laptop Dell 15 triệu' }] },
      { feature: 'shopping-assistant.chat', actorKey },
    );

    // Xác nhận nội dung phản hồi cụ thể từ fake provider.
    expect(result.text).toBe('Xin chào từ fake AI');

    // Tự tính toán hash SHA-256 độc lập để kiểm chứng khách quan, không dùng lại hàm helper của module.
    const expectedDirectHash = createHash('sha256').update(actorKey).digest('hex');

    const logRecord = await db.aiUsageLog.findFirst({
      where: { actorKeyHash: expectedDirectHash },
      orderBy: { createdAt: 'desc' },
    });

    expect(logRecord).not.toBeNull();
    expect(logRecord?.feature).toBe('shopping-assistant.chat');
    expect(logRecord?.status).toBe('ok');
    expect(logRecord?.provider).toBe('fake');
    expect(logRecord?.model).toBe('fake-chat');
    expect(logRecord?.costUsd).toBe(0);

    // Tuyệt đối không có trường lưu trữ nội dung prompt hoặc câu trả lời trong nhật ký.
    const rawRecord = logRecord as unknown as Record<string, unknown>;
    expect(rawRecord.prompt).toBeUndefined();
    expect(rawRecord.content).toBeUndefined();
    expect(rawRecord.messages).toBeUndefined();
  });

  it('ghi nhật ký trạng thái blocked khi bị chặn bởi hạn mức người dùng', async () => {
    const fakeChat = new FakeChatProvider();
    const gateway = createAiGateway({
      chatProvider: fakeChat,
      store: db,
    });

    const anonKey = 'anon:spammer_blocked_db_test';
    const expectedAnonHash = createHash('sha256').update(anonKey).digest('hex');

    // Gọi liên tiếp 15 lần để dùng hết sạch hạn mức 15 lượt của khách vãng lai.
    for (let i = 0; i < 15; i += 1) {
      await gateway.chat(
        { messages: [{ role: 'user', content: `Tin nhắn ${i}` }] },
        { feature: 'shopping-assistant.chat', actorKey: anonKey },
      );
    }

    // Xác nhận đúng 15 dòng nhật ký thành công đã được lưu.
    const okLogsCount = await db.aiUsageLog.count({
      where: { actorKeyHash: expectedAnonHash, status: 'ok' },
    });
    expect(okLogsCount).toBe(15);

    // Lần thứ 16 bắt buộc phải bị từ chối với AiBudgetExceededError.
    await expect(
      gateway.chat(
        { messages: [{ role: 'user', content: 'Tin nhắn thứ 16' }] },
        { feature: 'shopping-assistant.chat', actorKey: anonKey },
      ),
    ).rejects.toBeInstanceOf(AiBudgetExceededError);

    // Kiểm tra chính xác đúng 1 dòng nhật ký bị chặn (status = blocked) với chi phí bằng 0.
    const blockedLogsCount = await db.aiUsageLog.count({
      where: { actorKeyHash: expectedAnonHash, status: 'blocked' },
    });
    expect(blockedLogsCount).toBe(1);

    const blockedLog = await db.aiUsageLog.findFirst({
      where: { actorKeyHash: expectedAnonHash, status: 'blocked' },
    });
    expect(blockedLog?.errorCode).toBe('RATE_LIMIT_EXCEEDED');
    expect(blockedLog?.costUsd).toBe(0);
  });

  it('lưu bộ nhớ đệm embedding vào PostgreSQL và tái sử dụng cho các lần gọi sau', async () => {
    const fakeEmbedding = new FakeEmbeddingProvider();
    const gateway = createAiGateway({
      embeddingProvider: fakeEmbedding,
      store: db,
    });

    const targetText = 'Điện thoại Samsung Galaxy S24 Ultra chính hãng';

    // Lần gọi 1: dữ liệu chưa có trong cache -> provider phải được gọi và vector được lưu vào DB.
    const vectors1 = await gateway.embed([targetText], {
      feature: 'smart-search.index',
      actorKey: 'user:admin_catalog',
    });

    expect(vectors1).toHaveLength(1);
    expect(vectors1[0]).toHaveLength(1536);

    // Kiểm tra đúng 1 bản ghi cache đã được tạo trong bảng ai_embedding_cache.
    const cacheCountAfterFirstCall = await db.aiEmbeddingCache.count();
    expect(cacheCountAfterFirstCall).toBe(1);

    fakeEmbedding.clearRecorded();

    // Lần gọi 2: cùng nội dung text -> đọc trực tiếp từ DB, provider không nhận bất kỳ request nào.
    const vectors2 = await gateway.embed([targetText], {
      feature: 'smart-search.index',
      actorKey: 'user:admin_catalog',
    });

    expect(vectors2).toHaveLength(1);
    expect(vectors2[0]).toEqual(vectors1[0]);
    // Khẳng định chắc chắn provider không bị gọi lại lần nào.
    expect(fakeEmbedding.recordedRequests).toHaveLength(0);

    // Số lượng bản ghi cache trong DB vẫn chính xác là 1, không tạo thêm bản ghi trùng lặp.
    const cacheCountAfterSecondCall = await db.aiEmbeddingCache.count();
    expect(cacheCountAfterSecondCall).toBe(1);

    // Bản ghi log cho lần gọi trúng cache hoàn toàn phải có costUsd = 0.
    const cachedUsageLog = await db.aiUsageLog.findFirst({
      where: { feature: 'smart-search.index' },
      orderBy: { createdAt: 'desc' },
    });
    expect(cachedUsageLog?.costUsd).toBe(0);
    expect(cachedUsageLog?.status).toBe('ok');
  });

  it('ngân sách bằng 0 chặn lời gọi chat mới nhưng vẫn cho phép đọc embedding đã có trong cache', async () => {
    // Đặt ngân sách bằng 0 USD trong cơ sở dữ liệu.
    await db.aiSetting.upsert({
      where: { id: AI_SETTINGS_SINGLETON_ID },
      create: {
        id: AI_SETTINGS_SINGLETON_ID,
        monthlyBudgetUsd: 0,
        chatDailyLimitUser: 50,
        chatDailyLimitAnon: 15,
      },
      update: {
        monthlyBudgetUsd: 0,
      },
    });

    const fakeChat = new FakeChatProvider();
    const fakeEmbedding = new FakeEmbeddingProvider();
    const gateway = createAiGateway({
      chatProvider: fakeChat,
      embeddingProvider: fakeEmbedding,
      store: db,
    });

    // Lời gọi chat phải bị chặn lập tức do ngân sách bằng 0.
    await expect(
      gateway.chat(
        { messages: [{ role: 'user', content: 'Thử nghiệm chat khi ngân sách 0' }] },
        { feature: 'shopping-assistant.chat', actorKey: 'user:member_zero_budget' },
      ),
    ).rejects.toBeInstanceOf(AiBudgetExceededError);

    // Chuẩn bị sẵn một bản ghi cache trong bảng ai_embedding_cache.
    const cachedText = 'Văn bản đã nạp sẵn trong cache';
    const cleanHashKey = createHash('sha256').update(`fake-embedding\0${cachedText}`).digest('hex');

    const dummyVector = Array.from({ length: 1536 }, () => 0.123);
    await db.aiEmbeddingCache.upsert({
      where: { key: cleanHashKey },
      create: {
        key: cleanHashKey,
        model: 'fake-embedding',
        embedding: dummyVector,
      },
      update: {},
    });

    // Lời gọi embed trúng cache 100% không phát sinh chi phí nên vẫn được phép chạy thành công dù ngân sách là 0.
    const cachedEmbedResult = await gateway.embed([cachedText], {
      feature: 'smart-search.index',
      actorKey: 'user:member_zero_budget',
    });

    expect(cachedEmbedResult).toHaveLength(1);
    expect(cachedEmbedResult[0]).toEqual(dummyVector);
  });
});
