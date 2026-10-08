import { lazy, Suspense } from 'react';
import { AiSettingsPage } from './features/ai-gateway/admin/ai-settings-page';
import { BrandsPage } from './features/catalog/admin/brands-page';
import { CategoriesPage } from './features/catalog/admin/categories-page';
import { CategoryPage } from './features/catalog/customer/category-page';
import { InventoryPage } from './features/catalog/admin/inventory-page';
import { SkuInventoryPage } from './features/catalog/admin/sku-inventory-page';
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
// Tách trình soạn mô tả khỏi bundle trang khách, chỉ tải khi nhân viên mở trang sản phẩm.
const ProductsPage = lazy(() =>
  import('./features/catalog/admin/products-page').then((module) => ({
    default: module.ProductsPage,
  })),
);
const ProductFormPage = lazy(() =>
  import('./features/catalog/admin/product-form').then((module) => ({
    default: module.ProductFormPage,
  })),
);

function CustomerLayout() {
  // Header thuộc layout để trang chủ và mọi luồng tài khoản cùng phản ánh phiên hiện tại.
  return (
    <>
      <Header />
      <Outlet />
    </>
  );
}

// Trang giữ chỗ cho module chưa làm, để mục menu đã có route không mở ra trang trắng.
function AdminPlaceholderPage({ title }: { title: string }) {
  return (
    <div className="card">
      <h2 className="admin-placeholder-title">{title}</h2>
      <p className="muted">Chức năng đang được phát triển trong module kế tiếp.</p>
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
          <Route path="/c/:slug" element={<CategoryPage />} />
        </Route>
        <Route path="/admin/login" element={<StaffLoginPage />} />
        <Route path="/admin" element={<AdminLayout />}>
          <Route index element={<AdminDashboardPage />} />
          <Route path="roles" element={<RolesPage />} />
          <Route path="staff" element={<StaffPage />} />
          <Route path="customers" element={<CustomersPage />} />
          <Route path="audit-logs" element={<AuditLogPage />} />
          <Route path="orders" element={<AdminPlaceholderPage title="Quản lý đơn hàng" />} />
          <Route
            path="products"
            element={
              <Suspense fallback={<p role="status">Đang tải sản phẩm...</p>}>
                <ProductsPage />
              </Suspense>
            }
          />
          <Route
            path="products/new"
            element={
              <Suspense fallback={<p role="status">Đang tải biểu mẫu...</p>}>
                <ProductFormPage />
              </Suspense>
            }
          />
          <Route
            path="products/:id"
            element={
              <Suspense fallback={<p role="status">Đang tải biểu mẫu...</p>}>
                <ProductFormPage />
              </Suspense>
            }
          />
          <Route path="inventory" element={<InventoryPage />} />
          <Route path="products/:productId/skus/:skuId/inventory" element={<SkuInventoryPage />} />
          <Route path="categories" element={<CategoriesPage />} />
          <Route path="brands" element={<BrandsPage />} />
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
