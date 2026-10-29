import { lazy, Suspense } from 'react';
import { FallbackFeePage } from './features/shipping/admin/fallback-fee-page';
import { ShippingSettingsPage } from './features/shipping/admin/shipping-settings-page';
import { CommerceSettingsPage } from './features/commerce/admin/commerce-settings-page';
import { BrandsPage } from './features/catalog/admin/brands-page';
import { CategoriesPage } from './features/catalog/admin/categories-page';
import { CategoryPage } from './features/catalog/customer/category-page';
import { ComparePage } from './features/catalog/customer/compare-page';
import { ProductPage } from './features/catalog/customer/product-page';
import { Navigate, Outlet, Route, Routes, useLocation } from 'react-router-dom';
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
import { SupportDeskPage } from './features/support-desk/admin/support-desk-page';
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
// Recharts chỉ tải khi nhân viên mở trang báo cáo, không làm nặng bundle của khung quản trị.
const ReportsDashboardPage = lazy(() =>
  import('./features/admin-console/reports/reports-dashboard-page').then((module) => ({
    default: module.ReportsDashboardPage,
  })),
);
const RevenueBrandsReportPage = lazy(() =>
  import('./features/admin-console/reports/revenue-brands-page').then((module) => ({
    default: module.RevenueBrandsReportPage,
  })),
);
const RevenuePeriodsReportPage = lazy(() =>
  import('./features/admin-console/reports/revenue-periods-page').then((module) => ({
    default: module.RevenuePeriodsReportPage,
  })),
);
const OrdersPaymentsReportPage = lazy(() =>
  import('./features/admin-console/reports/orders-payments-page').then((module) => ({
    default: module.OrdersPaymentsReportPage,
  })),
);
const OrdersPeriodsReportPage = lazy(() =>
  import('./features/admin-console/reports/orders-periods-page').then((module) => ({
    default: module.OrdersPeriodsReportPage,
  })),
);
const CancellationsReasonsReportPage = lazy(() =>
  import('./features/admin-console/reports/cancellations-reasons-page').then((module) => ({
    default: module.CancellationsReasonsReportPage,
  })),
);
const CancellationsPeriodsReportPage = lazy(() =>
  import('./features/admin-console/reports/cancellations-periods-page').then((module) => ({
    default: module.CancellationsPeriodsReportPage,
  })),
);
const ProductsTopReportPage = lazy(() =>
  import('./features/admin-console/reports/products-top-page').then((module) => ({
    default: module.ProductsTopReportPage,
  })),
);
const ProductsPeriodsReportPage = lazy(() =>
  import('./features/admin-console/reports/products-periods-page').then((module) => ({
    default: module.ProductsPeriodsReportPage,
  })),
);

// Đường dẫn gốc của mỗi báo cáo chưa có trang riêng. Chuyển về trang biểu đồ tròn và giữ tham số tháng.
function ReportRedirect({ to }: { to: string }) {
  const { search } = useLocation();
  const next = new URLSearchParams(search);
  // Tham số của trang biểu đồ cột không dùng ở trang biểu đồ tròn.
  for (const name of ['range', 'granularity', 'brand']) next.delete(name);
  const query = next.toString();
  return <Navigate to={`${to}${query ? `?${query}` : ''}`} replace />;
}

function CustomerLayout() {
  // Header thuộc layout để trang chủ và mọi luồng tài khoản cùng phản ánh phiên hiện tại.
  return (
    <>
      <Header />
      <Outlet />
    </>
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
          <Route path="categories" element={<CategoriesPage />} />
          <Route path="brands" element={<BrandsPage />} />
          <Route path="conversations" element={<SupportDeskPage />} />
          <Route path="shipping/settings" element={<ShippingSettingsPage />} />
          <Route path="shipping/fallback-fee" element={<FallbackFeePage />} />
          <Route path="commerce/settings" element={<CommerceSettingsPage />} />
          <Route
            path="reports"
            element={
              <Suspense fallback={<p role="status">Đang tải báo cáo...</p>}>
                <ReportsDashboardPage />
              </Suspense>
            }
          />
          <Route
            path="reports/revenue"
            element={<ReportRedirect to="/admin/reports/revenue/brands" />}
          />
          <Route
            path="reports/orders"
            element={<ReportRedirect to="/admin/reports/orders/payments" />}
          />
          <Route
            path="reports/cancellations"
            element={<ReportRedirect to="/admin/reports/cancellations/reasons" />}
          />
          <Route
            path="reports/products"
            element={<ReportRedirect to="/admin/reports/products/top" />}
          />
          <Route
            path="reports/revenue/brands"
            element={
              <Suspense fallback={<p role="status">Đang tải báo cáo...</p>}>
                <RevenueBrandsReportPage />
              </Suspense>
            }
          />
          <Route
            path="reports/revenue/periods"
            element={
              <Suspense fallback={<p role="status">Đang tải báo cáo...</p>}>
                <RevenuePeriodsReportPage />
              </Suspense>
            }
          />
          <Route
            path="reports/orders/payments"
            element={
              <Suspense fallback={<p role="status">Đang tải báo cáo...</p>}>
                <OrdersPaymentsReportPage />
              </Suspense>
            }
          />
          <Route
            path="reports/orders/periods"
            element={
              <Suspense fallback={<p role="status">Đang tải báo cáo...</p>}>
                <OrdersPeriodsReportPage />
              </Suspense>
            }
          />
          <Route
            path="reports/cancellations/reasons"
            element={
              <Suspense fallback={<p role="status">Đang tải báo cáo...</p>}>
                <CancellationsReasonsReportPage />
              </Suspense>
            }
          />
          <Route
            path="reports/cancellations/periods"
            element={
              <Suspense fallback={<p role="status">Đang tải báo cáo...</p>}>
                <CancellationsPeriodsReportPage />
              </Suspense>
            }
          />
          <Route
            path="reports/products/top"
            element={
              <Suspense fallback={<p role="status">Đang tải báo cáo...</p>}>
                <ProductsTopReportPage />
              </Suspense>
            }
          />
          <Route
            path="reports/products/periods"
            element={
              <Suspense fallback={<p role="status">Đang tải báo cáo...</p>}>
                <ProductsPeriodsReportPage />
              </Suspense>
            }
          />
        </Route>
      </Routes>
      <ConsentBanner />
    </>
  );
}
