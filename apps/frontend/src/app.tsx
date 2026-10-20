import { lazy, Suspense } from 'react';
import { FallbackFeePage } from './features/shipping/admin/fallback-fee-page';
import { ShippingSettingsPage } from './features/shipping/admin/shipping-settings-page';
import { CommerceSettingsPage } from './features/commerce/admin/commerce-settings-page';
import { BrandsPage } from './features/catalog/admin/brands-page';
import { CategoriesPage } from './features/catalog/admin/categories-page';
import { CategoryPage } from './features/catalog/customer/category-page';
import { ComparePage } from './features/catalog/customer/compare-page';
import { ProductPage } from './features/catalog/customer/product-page';
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
import { CartPage } from './features/commerce/customer/cart-page';
import { CheckoutPage } from './features/commerce/customer/checkout/checkout-page';
import { OrderSuccessPage } from './features/commerce/customer/checkout/order-success-page';
import { PaymentPage } from './features/commerce/customer/checkout/payment-page';
import { AdminPaymentsPage } from './features/commerce/admin/payments/admin-payments-page';
import { AdminReviewsPage } from './features/commerce/admin/reviews/admin-reviews-page';
import { AdminOrderDetailPage } from './features/commerce/admin/orders/admin-order-detail-page';
import { AdminOrdersPage } from './features/commerce/admin/orders/admin-orders-page';
import { OrderDetailPage } from './features/commerce/customer/orders/order-detail-page';
import { CustomerOrdersPage } from './features/commerce/customer/orders/customer-orders-page';
import { OrderLookupPage } from './features/commerce/customer/orders/order-lookup-page';
import { HomePage } from './features/home/home-page';
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
          <Route path="/" element={<HomePage />} />
          <Route path="/register" element={<RegisterPage />} />
          <Route path="/login" element={<LoginPage />} />
          <Route path="/me" element={<AccountPage />} />
          <Route path="/forgot-password" element={<ForgotPasswordPage />} />
          <Route path="/reset-password" element={<ResetPasswordPage />} />
          <Route path="/verify-email" element={<VerifyEmailPage />} />
          <Route path="/privacy" element={<PrivacySettingsPage />} />
          <Route path="/c/:slug" element={<CategoryPage />} />
          <Route path="/p/:slug" element={<ProductPage />} />
          <Route path="/compare" element={<ComparePage />} />
          <Route path="/cart" element={<CartPage />} />
          <Route path="/checkout" element={<CheckoutPage />} />
          <Route path="/order-lookup" element={<OrderLookupPage />} />
          <Route path="/orders/:code/payment" element={<PaymentPage />} />
          <Route path="/orders/:code/success" element={<OrderSuccessPage />} />
          <Route path="/orders/:code" element={<OrderDetailPage />} />
          <Route path="/me/orders" element={<CustomerOrdersPage />} />
        </Route>
        <Route path="/admin/login" element={<StaffLoginPage />} />
        <Route path="/admin" element={<AdminLayout />}>
          <Route index element={<AdminDashboardPage />} />
          <Route path="roles" element={<RolesPage />} />
          <Route path="staff" element={<StaffPage />} />
          <Route path="customers" element={<CustomersPage />} />
          <Route path="audit-logs" element={<AuditLogPage />} />
          <Route path="orders" element={<AdminOrdersPage />} />
          <Route path="orders/:code" element={<AdminOrderDetailPage />} />
          <Route path="payments" element={<AdminPaymentsPage />} />
          <Route path="reviews" element={<AdminReviewsPage />} />
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
          <Route path="shipping/settings" element={<ShippingSettingsPage />} />
          <Route path="shipping/fallback-fee" element={<FallbackFeePage />} />
          <Route path="commerce/settings" element={<CommerceSettingsPage />} />
        </Route>
      </Routes>
      <ConsentBanner />
    </>
  );
}
