import { Router } from 'express';
import { accountRouter } from './account';
import { adminAuditRouter } from './admin-audit';
import { adminCustomersRouter } from './admin-customers';
import { adminRolesRouter } from './admin-roles';
import { adminStaffRouter } from './admin-staff';
import { authRouter } from './auth';

// Gộp các nhóm route của identity thành một router để app.ts chỉ cần gắn một chỗ.
// Mỗi route trong các file con: validate đầu vào bằng zod, gọi hàm nghiệp vụ, trả kết quả;
// lỗi chuyển hết sang next() để errorHandler xử lý thống nhất.
export const identityRouter = Router();

// Route của khách và route chung (đăng nhập, tài khoản).
identityRouter.use(authRouter);
identityRouter.use(accountRouter);
// Route back-office; mỗi route tự kiểm tra quyền bằng requirePermission.
identityRouter.use(adminStaffRouter);
identityRouter.use(adminRolesRouter);
identityRouter.use(adminCustomersRouter);
identityRouter.use(adminAuditRouter);
