import request from 'supertest';
import { beforeEach, describe, expect, it } from 'vitest';
import { createApp } from '../../../../src/app';
import { db } from '../../../../src/shared/db';
import { hashPassword } from '../../../../src/modules/identity/security/password';
import { seedIdentityRoles } from '../../../../src/modules/identity/data/seed';
import { generateToken, hashToken } from '../../../../src/modules/identity/security/tokens';
import { createAiGateway } from '../../../../src/modules/ai-gateway/service';
import { AiBudgetExceededError } from '../../../../src/modules/ai-gateway/errors';

describe('AI-09: Tích hợp API quản trị AI và ma trận quyền hạn', () => {
  const app = createApp();
  let adminToken: string;
  let adminUserId: string;
  let salesToken: string;

  beforeEach(async () => {
    // Khởi tạo các vai trò chuẩn trong hệ thống để bảo đảm quyền hạn có sẵn.
    await seedIdentityRoles(db);

    // Xóa sạch nhật ký audit, nhật ký sử dụng và cấu hình AI trước mỗi ca kiểm thử.
    await db.auditLog.deleteMany({ where: { targetType: 'ai_setting' } });
    await db.aiUsageLog.deleteMany({});
    await db.aiSetting.deleteMany({});

    // Tạo tài khoản admin với đầy đủ mọi quyền trong hệ thống.
    const adminRole = await db.role.findUniqueOrThrow({ where: { code: 'admin' } });
    const adminUser = await db.user.create({
      data: {
        email: `admin_ai_${Date.now()}@pickory.local`,
        passwordHash: await hashPassword('AdminPass123!'),
        kind: 'staff',
        roles: { create: { roleId: adminRole.id } },
      },
    });
    adminUserId = adminUser.id;

    adminToken = generateToken();
    await db.session.create({
      data: {
        tokenHash: hashToken(adminToken),
        userId: adminUser.id,
        expiresAt: new Date(Date.now() + 12 * 60 * 60 * 1000),
      },
    });

    // Tạo tài khoản nhân viên sales có quyền ai-usage:read nhưng không có ai-settings:manage.
    const salesRole = await db.role.findUniqueOrThrow({ where: { code: 'sales' } });
    const salesUser = await db.user.create({
      data: {
        email: `sales_ai_${Date.now()}@pickory.local`,
        passwordHash: await hashPassword('SalesPass123!'),
        kind: 'staff',
        roles: { create: { roleId: salesRole.id } },
      },
    });

    salesToken = generateToken();
    await db.session.create({
      data: {
        tokenHash: hashToken(salesToken),
        userId: salesUser.id,
        expiresAt: new Date(Date.now() + 12 * 60 * 60 * 1000),
      },
    });
  });

  it('nhân viên sales được phép GET usage và GET settings nhưng bị chặn 403 khi PUT settings', async () => {
    // Sales xem báo cáo sử dụng thành công vì có quyền ai-usage:read.
    const usageRes = await request(app)
      .get('/api/admin/ai/usage')
      .set('Cookie', `pk_session=${salesToken}`);
    expect(usageRes.status).toBe(200);
    expect(usageRes.body.data).toBeDefined();

    // Sales xem cài đặt thành công vì có quyền ai-usage:read.
    const settingsRes = await request(app)
      .get('/api/admin/ai/settings')
      .set('Cookie', `pk_session=${salesToken}`);
    expect(settingsRes.status).toBe(200);
    expect(settingsRes.body.data).toBeDefined();

    // Sales cố gắng chỉnh sửa cấu hình bị từ chối 403 do thiếu quyền ai-settings:manage.
    const putRes = await request(app)
      .put('/api/admin/ai/settings')
      .set('Cookie', `pk_session=${salesToken}`)
      .send({
        monthlyBudgetUsd: 50,
        chatDailyLimitUser: 100,
        chatDailyLimitAnon: 30,
      });
    expect(putRes.status).toBe(403);
  });

  it('admin PUT settings thành công, có hiệu lực ngay ở lần gọi gateway tiếp theo và ghi đúng 1 dòng audit log', async () => {
    // 1. Khởi tạo gateway với cơ sở dữ liệu thật để kiểm tra tác động thời gian thực.
    const gateway = createAiGateway({ store: db });

    // 2. Thiết lập cấu hình ban đầu với ngân sách 1 USD, và tạo một log đã dùng 10 USD (vượt 120% ngân sách).
    await db.aiSetting.upsert({
      where: { id: 'singleton' },
      create: {
        id: 'singleton',
        monthlyBudgetUsd: 1.0,
        chatDailyLimitUser: 50,
        chatDailyLimitAnon: 15,
      },
      update: {
        monthlyBudgetUsd: 1.0,
      },
    });

    await db.aiUsageLog.create({
      data: {
        feature: 'shopping-assistant.chat',
        provider: 'fake',
        model: 'fake',
        latencyMs: 50,
        status: 'ok',
        costUsd: 10.0,
        createdAt: new Date(),
      },
    });

    // 3. Gọi gateway chat: phải bị chặn ngay với lỗi AiBudgetExceededError vì chi phí 10 USD vượt quá 1.2 USD (120% của 1 USD).
    await expect(
      gateway.chat(
        { messages: [{ role: 'user', content: 'Xin chào' }] },
        { feature: 'shopping-assistant.chat', actorKey: 'user:test_budget_actor' },
      ),
    ).rejects.toThrowError(AiBudgetExceededError);

    // 4. Admin gọi PUT /api/admin/ai/settings để nới lỏng ngân sách lên 100 USD.
    const newSettings = {
      monthlyBudgetUsd: 100,
      chatDailyLimitUser: 80,
      chatDailyLimitAnon: 20,
    };

    const putRes = await request(app)
      .put('/api/admin/ai/settings')
      .set('Cookie', `pk_session=${adminToken}`)
      .send(newSettings);

    expect(putRes.status).toBe(200);
    expect(putRes.body.data).toMatchObject(newSettings);

    // 5. Ngay ở lần gọi gateway kế tiếp, hệ thống đọc ngân sách mới (100 USD) và cho phép chat thành công.
    const chatResult = await gateway.chat(
      { messages: [{ role: 'user', content: 'Xin chào lần hai' }] },
      { feature: 'shopping-assistant.chat', actorKey: 'user:test_budget_actor' },
    );
    expect(chatResult.text).toBeDefined();

    // 6. Kiểm tra hệ thống ghi nhận đúng 1 dòng nhật ký kiểm toán với action ai_settings.update và targetId singleton.
    const auditLogs = await db.auditLog.findMany({
      where: {
        action: 'ai_settings.update',
        targetType: 'ai_setting',
        targetId: 'singleton',
      },
    });

    expect(auditLogs).toHaveLength(1);
    expect(auditLogs[0]?.actorId).toBe(adminUserId);
    expect(auditLogs[0]?.after).toMatchObject(newSettings);
  });

  it('trả về 400 khi dữ liệu PUT settings không hợp lệ hoặc chứa trường lạ', async () => {
    // Ngân sách âm bị từ chối.
    const resNegativeBudget = await request(app)
      .put('/api/admin/ai/settings')
      .set('Cookie', `pk_session=${adminToken}`)
      .send({
        monthlyBudgetUsd: -10,
        chatDailyLimitUser: 50,
        chatDailyLimitAnon: 15,
      });
    expect(resNegativeBudget.status).toBe(400);

    // Hạn mức chat người dùng bằng 0 bị từ chối vì phải là số nguyên dương.
    const resZeroLimit = await request(app)
      .put('/api/admin/ai/settings')
      .set('Cookie', `pk_session=${adminToken}`)
      .send({
        monthlyBudgetUsd: 30,
        chatDailyLimitUser: 0,
        chatDailyLimitAnon: 15,
      });
    expect(resZeroLimit.status).toBe(400);

    // Hạn mức không phải số nguyên (số thực) bị từ chối.
    const resFloatLimit = await request(app)
      .put('/api/admin/ai/settings')
      .set('Cookie', `pk_session=${adminToken}`)
      .send({
        monthlyBudgetUsd: 30,
        chatDailyLimitUser: 25.5,
        chatDailyLimitAnon: 15,
      });
    expect(resFloatLimit.status).toBe(400);

    // Gửi kèm trường không xác định bị từ chối theo quy tắc schema strict.
    const resUnknownField = await request(app)
      .put('/api/admin/ai/settings')
      .set('Cookie', `pk_session=${adminToken}`)
      .send({
        monthlyBudgetUsd: 30,
        chatDailyLimitUser: 50,
        chatDailyLimitAnon: 15,
        extraUnauthorizedField: 'malicious',
      });
    expect(resUnknownField.status).toBe(400);

    // Thiếu trường bắt buộc bị từ chối.
    const resMissingField = await request(app)
      .put('/api/admin/ai/settings')
      .set('Cookie', `pk_session=${adminToken}`)
      .send({
        monthlyBudgetUsd: 30,
      });
    expect(resMissingField.status).toBe(400);
  });

  it('trả về 400 khi tham số ngày query GET usage không hợp lệ hoặc from lớn hơn hoặc bằng to', async () => {
    // Chuỗi ngày không đúng chuẩn ISO 8601 bị từ chối.
    const resInvalidIso = await request(app)
      .get('/api/admin/ai/usage?from=khong-phai-ngay&to=2026-10-05T00:00:00.000Z')
      .set('Cookie', `pk_session=${adminToken}`);
    expect(resInvalidIso.status).toBe(400);

    // Mốc from sau mốc to bị từ chối để bảo đảm logic thời gian.
    const resInvertedRange = await request(app)
      .get('/api/admin/ai/usage?from=2026-10-10T00:00:00.000Z&to=2026-10-05T00:00:00.000Z')
      .set('Cookie', `pk_session=${adminToken}`);
    expect(resInvertedRange.status).toBe(400);

    const resQuarter = await request(app)
      .get('/api/admin/ai/usage?from=2026-07-01T00:00:00.000Z&to=2026-10-01T00:00:00.000Z')
      .set('Cookie', `pk_session=${adminToken}`);
    expect(resQuarter.status).toBe(200);
    const resTooWide = await request(app)
      .get('/api/admin/ai/usage?from=2026-07-01T00:00:00.000Z&to=2026-10-01T00:00:00.001Z')
      .set('Cookie', `pk_session=${adminToken}`);
    expect(resTooWide.status).toBe(400);
    expect(resTooWide.body.error.code).toBe('INVALID_DATE_RANGE');
  });
});
