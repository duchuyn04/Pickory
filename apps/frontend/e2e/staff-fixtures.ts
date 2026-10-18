import { expect, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';
import { readPasswordSetupLink } from './outbox';

// Hàm tạo nhân viên qua giao diện thật: admin gửi lời mời, nhân viên đặt mật khẩu bằng link trong outbox rồi đăng nhập.

export type StaffRoleCode = 'sales' | 'support';

export interface StaffAccount {
  email: string;
  fullName: string;
  roleCode: StaffRoleCode;
}

// Admin mời một nhân viên bằng hộp thoại "Mời nhân viên mới". Hộp thoại chỉ đóng khi API đã nhận lời mời.
export async function inviteStaff(adminPage: Page, staff: StaffAccount) {
  await adminPage.goto('/admin/staff');
  await adminPage.getByRole('button', { name: '+ Mời nhân viên', exact: true }).click();
  const invitation = adminPage.getByRole('dialog', { name: 'Mời nhân viên mới', exact: true });
  await invitation.getByLabel(/^Email nhân viên/).fill(staff.email);
  await invitation.getByLabel(/^Họ và tên/).fill(staff.fullName);
  // Nhãn checkbox chứa mã vai trò trong ngoặc, ví dụ "Nhân viên bán hàng (sales)". Mã trong ngoặc không trùng nhau giữa các vai trò.
  await invitation.getByRole('checkbox', { name: `(${staff.roleCode})` }).check();
  await invitation.getByRole('button', { name: 'Gửi lời mời', exact: true }).click();
  await expect(invitation).toBeHidden();
}

// Nhân viên mở link trong thư mời ở trình duyệt của họ, đặt mật khẩu và đăng nhập trang quản trị.
export async function activateStaffAccount(staffPage: Page, email: string) {
  await staffPage.goto(await readPasswordSetupLink(email));
  await staffPage
    .getByLabel('Mật khẩu mới (tối thiểu 8 ký tự)', { exact: true })
    .fill(e2eEnvironment.accountPassword);
  await staffPage
    .getByLabel('Xác nhận mật khẩu mới', { exact: true })
    .fill(e2eEnvironment.accountPassword);
  await staffPage.getByRole('button', { name: 'Cập nhật mật khẩu', exact: true }).click();
  await staffPage.getByRole('link', { name: 'Đăng nhập trang quản trị', exact: true }).click();
  await staffPage.getByLabel(/^Email/).fill(email);
  await staffPage.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.accountPassword);
  await staffPage.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(staffPage).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
}
