import type { PrismaClient } from '../../generated/prisma/client';
import { loadConfig } from '../../shared/config';
import { db as defaultDb } from '../../shared/db';
import { AppError } from '../../shared/errors';
import { recordAudit } from '../identity';
import type { AiSettings } from './types';

export const AI_SETTINGS_SINGLETON_ID = 'singleton';

// Lấy cài đặt ngân sách và hạn mức AI hiện tại từ cơ sở dữ liệu.
export async function getAiSettings(store: PrismaClient = defaultDb): Promise<AiSettings> {
  // Tìm bản ghi cấu hình duy nhất đã được lưu trong cơ sở dữ liệu.
  const existing = await store.aiSetting.findUnique({
    where: { id: AI_SETTINGS_SINGLETON_ID },
  });

  if (existing) {
    return {
      monthlyBudgetUsd: existing.monthlyBudgetUsd,
      chatDailyLimitUser: existing.chatDailyLimitUser,
      chatDailyLimitAnon: existing.chatDailyLimitAnon,
    };
  }

  // Khởi tạo giá trị mặc định từ biến môi trường khi hệ thống mới chạy lần đầu chưa có bản ghi.
  const config = loadConfig();

  // Tạo mới bản ghi singleton; để lỗi DB lan truyền tự nhiên thay vì nuốt lỗi làm sai lệch hạn mức thực tế.
  const created = await store.aiSetting.upsert({
    where: { id: AI_SETTINGS_SINGLETON_ID },
    create: {
      id: AI_SETTINGS_SINGLETON_ID,
      monthlyBudgetUsd: config.AI_MONTHLY_BUDGET_USD,
      chatDailyLimitUser: config.AI_CHAT_DAILY_LIMIT_USER,
      chatDailyLimitAnon: config.AI_CHAT_DAILY_LIMIT_ANON,
    },
    update: {},
  });

  return {
    monthlyBudgetUsd: created.monthlyBudgetUsd,
    chatDailyLimitUser: created.chatDailyLimitUser,
    chatDailyLimitAnon: created.chatDailyLimitAnon,
  };
}

// Cập nhật cài đặt AI nguyên tử kèm ghi nhật ký kiểm toán (audit log) của module identity.
export async function updateAiSettings(
  settings: AiSettings,
  actorId: string,
  store: PrismaClient = defaultDb,
): Promise<AiSettings> {
  // Ngăn chặn giá trị ngân sách âm hoặc giá trị không phải số hợp lệ để bảo vệ toàn vẹn tài chính.
  if (
    !Number.isFinite(settings.monthlyBudgetUsd) ||
    settings.monthlyBudgetUsd < 0 ||
    !Number.isInteger(settings.chatDailyLimitUser) ||
    settings.chatDailyLimitUser <= 0 ||
    !Number.isInteger(settings.chatDailyLimitAnon) ||
    settings.chatDailyLimitAnon <= 0
  ) {
    throw new AppError(
      'INVALID_AI_SETTINGS',
      400,
      'Cài đặt AI không hợp lệ: ngân sách phải không âm và hạn mức phải là số nguyên dương.',
    );
  }

  // Thực hiện trong một transaction để cài đặt mới và nhật ký kiểm toán luôn đồng bộ.
  return await store.$transaction(async (tx) => {
    // Đọc trạng thái cũ để lưu thông tin đối soát trước khi sửa đổi.
    const before = await tx.aiSetting.findUnique({
      where: { id: AI_SETTINGS_SINGLETON_ID },
    });

    // Cập nhật hoặc tạo mới bản ghi cài đặt duy nhất.
    const updated = await tx.aiSetting.upsert({
      where: { id: AI_SETTINGS_SINGLETON_ID },
      create: {
        id: AI_SETTINGS_SINGLETON_ID,
        monthlyBudgetUsd: settings.monthlyBudgetUsd,
        chatDailyLimitUser: settings.chatDailyLimitUser,
        chatDailyLimitAnon: settings.chatDailyLimitAnon,
      },
      update: {
        monthlyBudgetUsd: settings.monthlyBudgetUsd,
        chatDailyLimitUser: settings.chatDailyLimitUser,
        chatDailyLimitAnon: settings.chatDailyLimitAnon,
      },
    });

    // Ghi audit log bằng hàm dùng chung của identity theo yêu cầu kiến trúc.
    await recordAudit(tx, {
      actorId,
      action: 'ai_settings.update',
      targetType: 'ai_setting',
      targetId: AI_SETTINGS_SINGLETON_ID,
      before,
      after: updated,
    });

    return {
      monthlyBudgetUsd: updated.monthlyBudgetUsd,
      chatDailyLimitUser: updated.chatDailyLimitUser,
      chatDailyLimitAnon: updated.chatDailyLimitAnon,
    };
  });
}
