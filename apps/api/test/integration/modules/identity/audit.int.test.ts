import { describe, expect, it } from 'vitest';
import { db } from '../../../../src/shared/db';
import { recordAudit } from '../../../../src/modules/identity/audit';

describe('ID-04 recordAudit integration', () => {
  it('thử ghi audit trong transaction bị rollback thì không còn dòng nào', async () => {
    const initialCount = await db.auditLog.count();

    await expect(
      db.$transaction(async (tx) => {
        await recordAudit(tx, {
          actorId: 'test-actor-1',
          action: 'test.action',
          targetType: 'test',
          targetId: 'target-1',
          before: { count: 0 },
          after: { count: 1 },
        });

        // Cố ý ném lỗi làm rollback transaction
        throw new Error('Cố ý rollback transaction');
      }),
    ).rejects.toThrow('Cố ý rollback transaction');

    const finalCount = await db.auditLog.count();
    expect(finalCount).toBe(initialCount);
  });

  it('ghi audit thành công trong transaction bình thường', async () => {
    const log = await db.$transaction(async (tx) => {
      return recordAudit(tx, {
        actorId: 'test-actor-2',
        action: 'role.assign',
        targetType: 'user',
        targetId: 'user-123',
        before: { roles: ['sales'] },
        after: { roles: ['sales', 'support'], password: 'secret_to_redact' },
      });
    });

    expect(log.id).toBeDefined();
    expect(log.action).toBe('role.assign');
    expect(log.after).toMatchObject({
      roles: ['sales', 'support'],
      password: '[REDACTED]',
    });
  });
});
