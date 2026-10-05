import { describe, expect, it } from 'vitest';
import { db } from '../../../../src/shared/db';
import {
  AI_SETTINGS_SINGLETON_ID,
  getAiSettings,
  updateAiSettings,
} from '../../../../src/modules/ai-gateway/settings';

describe('AI Settings và Audit Log Integration', () => {
  it('khởi tạo cài đặt mặc định khi chưa có bản ghi trong bảng ai_settings', async () => {
    // Đọc cài đặt ban đầu khi bảng đang trống.
    const settings = await getAiSettings(db);

    expect(settings).toBeDefined();
    expect(settings.monthlyBudgetUsd).toBeGreaterThanOrEqual(0);
    expect(settings.chatDailyLimitUser).toBeGreaterThan(0);
    expect(settings.chatDailyLimitAnon).toBeGreaterThan(0);

    // Kiểm tra bản ghi singleton đã được tạo trong cơ sở dữ liệu.
    const row = await db.aiSetting.findUnique({
      where: { id: AI_SETTINGS_SINGLETON_ID },
    });
    expect(row).not.toBeNull();
    expect(row?.monthlyBudgetUsd).toBe(settings.monthlyBudgetUsd);
  });

  it('cập nhật cài đặt thành công kèm tạo đúng một dòng audit log nguyên tử', async () => {
    const actorId = 'test-admin-actor-001';
    const newSettings = {
      monthlyBudgetUsd: 150,
      chatDailyLimitUser: 80,
      chatDailyLimitAnon: 20,
    };

    const initialAuditCount = await db.auditLog.count({
      where: { action: 'ai_settings.update' },
    });

    // Thực hiện cập nhật cài đặt mới.
    const updated = await updateAiSettings(newSettings, actorId, db);

    expect(updated).toEqual(newSettings);

    // Kiểm tra bảng ai_settings đã lưu giá trị mới.
    const savedRow = await db.aiSetting.findUnique({
      where: { id: AI_SETTINGS_SINGLETON_ID },
    });
    expect(savedRow?.monthlyBudgetUsd).toBe(150);
    expect(savedRow?.chatDailyLimitUser).toBe(80);
    expect(savedRow?.chatDailyLimitAnon).toBe(20);

    // Kiểm tra bảng audit_logs đã được thêm đúng một dòng nhật ký kiểm toán.
    const finalAuditCount = await db.auditLog.count({
      where: { action: 'ai_settings.update' },
    });
    expect(finalAuditCount).toBe(initialAuditCount + 1);

    const latestAudit = await db.auditLog.findFirst({
      where: { action: 'ai_settings.update', actorId },
      orderBy: { createdAt: 'desc' },
    });
    expect(latestAudit).not.toBeNull();
    expect(latestAudit?.targetType).toBe('ai_setting');
    expect(latestAudit?.targetId).toBe(AI_SETTINGS_SINGLETON_ID);
    expect(latestAudit?.after).toMatchObject({
      monthlyBudgetUsd: 150,
      chatDailyLimitUser: 80,
      chatDailyLimitAnon: 20,
    });
  });

  it('từ chối giá trị cài đặt âm và không tạo audit log rác', async () => {
    const actorId = 'test-admin-actor-002';
    const invalidSettings = {
      monthlyBudgetUsd: -10,
      chatDailyLimitUser: 50,
      chatDailyLimitAnon: 15,
    };

    const initialAuditCount = await db.auditLog.count({
      where: { action: 'ai_settings.update' },
    });

    await expect(updateAiSettings(invalidSettings, actorId, db)).rejects.toThrow(
      'ngân sách phải không âm',
    );

    // Số dòng audit log không được tăng lên khi có lỗi đầu vào.
    const finalAuditCount = await db.auditLog.count({
      where: { action: 'ai_settings.update' },
    });
    expect(finalAuditCount).toBe(initialAuditCount);
  });
});
