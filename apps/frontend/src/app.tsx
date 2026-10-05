import { AiSettingsPage } from './features/ai-gateway/admin/ai-settings-page';
import { Outlet, Route, Routes } from 'react-router-dom';
import { AccountPage } from './features/identity/customer/account-page';
import { AdminDashboardPage } from './features/identity/admin/dashboard-page';
import { AdminLayout } from './features/identity/admin/admin-layout';
import { AuditLogPage } from './features/identity/admin/audit-log-page';
import { CustomersPage } from './features/identity/admin/customers-page';
import { RolesPage } from './features/identity/admin/roles-page';
import { StaffLoginPage } from './features/identity/admin/login-page';
import { StaffPage } from './features/identity/admin/staff-page';
import { ConsentBanner } from './features/identity/components/consent-banner';
import { ForgotPasswordPage } from './features/identity/customer/forgot-password-page';
import { LoginPage } from './features/identity/customer/login-page';
import { PrivacySettingsPage } from './features/identity/customer/privacy-settings';
import { RegisterPage } from './features/identity/customer/register-page';
import { ResetPasswordPage } from './features/identity/customer/reset-password-page';
import { VerifyEmailPage } from './features/identity/customer/verify-email-page';
import { HealthPage } from './shared/health-page';
import { Header } from './shared/header';

function CustomerLayout() {
  // Header thuộc layout để trang chủ và mọi luồng tài khoản cùng phản ánh phiên hiện tại.
  return (
    <>
      <Header />
      <Outlet />
    </>
  );
}

function AdminPlaceholderPage({ title }: { title: string }) {
  return (
    <div
      style={{
        padding: 'var(--space-6)',
        backgroundColor: 'var(--color-surface)',
        borderRadius: 'var(--radius-card)',
        border: '1px solid var(--color-border)',
      }}
    >
      <h2 style={{ margin: '0 0 var(--space-2) 0', color: 'var(--color-ink)' }}>{title}</h2>
      <p style={{ margin: 0, color: 'var(--color-ink-muted)' }}>
        Chức năng đang được phát triển trong module kế tiếp.
      </p>
    </div>
  );
}

export function App() {
  return (
    <>
      <Routes>
        <Route element={<CustomerLayout />}>
          <Route path="/" element={<HealthPage />} />
          <Route path="/register" element={<RegisterPage />} />
          <Route path="/login" element={<LoginPage />} />
          <Route path="/me" element={<AccountPage />} />
          <Route path="/forgot-password" element={<ForgotPasswordPage />} />
          <Route path="/reset-password" element={<ResetPasswordPage />} />
          <Route path="/verify-email" element={<VerifyEmailPage />} />
          <Route path="/privacy" element={<PrivacySettingsPage />} />
        </Route>
        <Route path="/admin/login" element={<StaffLoginPage />} />
        <Route path="/admin" element={<AdminLayout />}>
          <Route index element={<AdminDashboardPage />} />
          <Route path="roles" element={<RolesPage />} />
          <Route path="staff" element={<StaffPage />} />
          <Route path="customers" element={<CustomersPage />} />
          <Route path="audit-logs" element={<AuditLogPage />} />
          <Route path="orders" element={<AdminPlaceholderPage title="Quản lý đơn hàng" />} />
          <Route path="products" element={<AdminPlaceholderPage title="Quản lý sản phẩm" />} />
          <Route
            path="conversations"
            element={<AdminPlaceholderPage title="Chăm sóc khách hàng" />}
          />
          <Route path="ai" element={<AiSettingsPage />} />
        </Route>
      </Routes>
      <ConsentBanner />
    </>
  );
}
