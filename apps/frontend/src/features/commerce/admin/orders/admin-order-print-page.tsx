import { useQuery } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { ApiError, apiRequest } from '../../../../shared/api';
import { ErrorAlert } from '../../../../shared/error-alert';
import { getErrorMessage } from '../../../../shared/error-message';
import { ForbiddenNotice } from '../../../../shared/forbidden-notice';
import { formatVietnamDateTime } from '../../../../shared/vietnam-time';
import { checkPermission, useActor } from '../../../identity/hooks/use-actor';
import { InvoiceSheet } from './invoice-sheet';
import { buildOrderInvoicesApiPath, parseInvoiceOrderCodes } from './order-invoice-codes';
import { orderInvoicesResponseSchema, type OrderInvoicesResponse } from './order-invoices-schema';
import './invoice-print.css';

type OrderInvoicesData = OrderInvoicesResponse['data'];

// Tải dữ liệu in hóa đơn và kiểm định dạng theo hợp đồng trước khi vẽ trang in.
async function fetchOrderInvoices(codes: string[]): Promise<OrderInvoicesData> {
  const data = await apiRequest<unknown>(buildOrderInvoicesApiPath(codes));
  // `apiRequest` đã tách lớp `data`, nên bọc lại để khớp schema gốc dùng chung với backend.
  const parsed = orderInvoicesResponseSchema.parse({ data });
  return parsed.data;
}

// Chờ giải mã toàn bộ ảnh logo trước khi in để logo không bị mất trên trang giấy đầu.
async function decodeAllLogos(container: HTMLElement | null): Promise<void> {
  if (!container) {
    return;
  }
  const images = Array.from(container.querySelectorAll('img'));
  await Promise.all(
    images.map((image) =>
      // Môi trường kiểm thử không có `decode` thì coi như ảnh đã sẵn sàng; ảnh hỏng vẫn cho in.
      typeof image.decode === 'function'
        ? image.decode().catch(() => undefined)
        : Promise.resolve(),
    ),
  );
}

// Chờ font chữ sẵn sàng trước khi gọi hộp thoại in để tránh in nhầm font dự phòng.
async function waitForFontsReady(): Promise<void> {
  if (typeof document !== 'undefined' && document.fonts?.ready) {
    await document.fonts.ready.catch(() => undefined);
  }
}

// Thanh công cụ luôn hiện nút Đóng để nhân viên thoát tab ngay cả khi trang gặp lỗi.
function PrintToolbar({ canPrint }: { canPrint: boolean }) {
  return (
    <div className="invoice-print-toolbar">
      <div className="invoice-print-toolbar-inner">
        {canPrint && (
          <button type="button" className="btn btn-primary" onClick={() => window.print()}>
            In
          </button>
        )}
        <button type="button" className="btn" onClick={() => window.close()}>
          Đóng
        </button>
      </div>
    </div>
  );
}

// Trang in hóa đơn nằm ngoài khung quản trị để không mang menu bên vào bản in.
export function AdminOrderPrintPage() {
  const { search } = useLocation();
  const { data: actor, isLoading: isActorLoading } = useActor();
  // Tính giờ in một lần lúc dựng trang để mọi hóa đơn trong cùng lượt in mang chung thời điểm.
  const [printedAtText] = useState(() => formatVietnamDateTime(new Date().toISOString()));
  const sheetsRef = useRef<HTMLDivElement>(null);
  const hasPrintedRef = useRef(false);
  const codes = parseInvoiceOrderCodes(search);
  const isStaff = actor?.kind === 'staff';
  const canReadOrders = isStaff && checkPermission(actor, 'orders:read');

  const { data, isLoading, error } = useQuery<OrderInvoicesData>({
    queryKey: ['admin-order-invoices', codes],
    queryFn: () => fetchOrderInvoices(codes),
    // Chỉ gọi API khi đã xác nhận nhân viên có quyền đọc đơn và có ít nhất một mã.
    enabled: canReadOrders && codes.length > 0,
    // Luôn lấy dữ liệu mới khi mở trang và xóa cache ngay khi rời trang để không giữ dữ liệu khách.
    staleTime: 0,
    gcTime: 0,
    // Không tự tải lại khi cửa sổ lấy lại focus để tránh lỗi mạng làm mất hóa đơn vừa mở in.
    refetchOnWindowFocus: false,
    // Tắt thử lại tự động cho lỗi máy chủ và lỗi mạng để nhân viên thấy ngay thông báo lỗi.
    retry: false,
  });

  useEffect(() => {
    if (!data || data.invoices.length === 0) {
      return;
    }
    const previousTitle = document.title;
    // Đặt tiêu đề trang khớp mã đơn để trình duyệt gợi ý tên tệp PDF rõ ràng khi lưu.
    document.title =
      data.invoices.length === 1 ? `Hóa đơn ${data.invoices[0]!.code}` : 'In hóa đơn';
    return () => {
      document.title = previousTitle;
    };
  }, [data]);

  useEffect(() => {
    // Chỉ tự động mở hộp thoại in khi đã có dữ liệu hóa đơn và chưa từng mở trước đó.
    if (!data || data.invoices.length === 0 || hasPrintedRef.current) {
      return;
    }

    let cancelled = false;

    async function printWhenLogosReady() {
      await Promise.all([decodeAllLogos(sheetsRef.current), waitForFontsReady()]);
      // Hủy lệnh in nếu trang bị đóng hoặc StrictMode chạy lại trước khi giải mã ảnh xong.
      if (cancelled || hasPrintedRef.current) {
        return;
      }
      hasPrintedRef.current = true;
      window.print();
    }

    void printWhenLogosReady();

    return () => {
      // Đánh dấu lượt chạy hiện tại đã hủy để không mở hộp thoại in sau khi rời trang.
      cancelled = true;
    };
  }, [data]);

  // Chờ xác thực xong mới chuyển hướng để tránh đẩy nhầm nhân viên đã đăng nhập.
  if (isActorLoading) {
    return (
      <div className="invoice-print-page">
        <div className="invoice-print-state">
          <p role="status" className="empty-state">
            Đang nạp dữ liệu...
          </p>
        </div>
      </div>
    );
  }

  // Khách vãng lai và tài khoản khách hàng không được xem trang in nội bộ.
  if (!actor || !isStaff) {
    return <Navigate to="/admin/login" replace />;
  }

  // Kiểm tra quyền ở giao diện giúp ẩn nút in; backend vẫn tự chặn request thật.
  if (!canReadOrders) {
    return (
      <div className="invoice-print-page">
        <PrintToolbar canPrint={false} />
        <div className="invoice-print-state">
          <ForbiddenNotice permission="orders:read" subject="hóa đơn" />
        </div>
      </div>
    );
  }

  // Chặn danh sách mã rỗng trước khi gọi API để tránh gửi request thừa lên máy chủ.
  if (codes.length === 0) {
    return (
      <div className="invoice-print-page">
        <PrintToolbar canPrint={false} />
        <div className="invoice-print-state">
          <ErrorAlert message="Chọn ít nhất 1 đơn để in." />
        </div>
      </div>
    );
  }

  // Hiện trạng thái đang tải trong lúc chờ API trả dữ liệu, tránh nháy sang màn báo lỗi.
  if (isLoading) {
    return (
      <div className="invoice-print-page">
        <PrintToolbar canPrint={false} />
        <div className="invoice-print-state">
          <p role="status" className="empty-state">
            Đang nạp hóa đơn...
          </p>
        </div>
      </div>
    );
  }

  // Phản hồi 403 từ API vẫn hiện thông báo thiếu quyền giống lúc kiểm tra ở client.
  if (error instanceof ApiError && error.status === 403) {
    return (
      <div className="invoice-print-page">
        <PrintToolbar canPrint={false} />
        <div className="invoice-print-state">
          <ForbiddenNotice permission="orders:read" subject="hóa đơn" />
        </div>
      </div>
    );
  }

  // Lỗi 404 liệt kê từng mã đơn không tồn tại trong `details` để nhân viên kiểm tra lại.
  if (error instanceof ApiError && error.status === 404) {
    const missingItems =
      error.details?.map((detail) => detail.message).filter((text) => text.length > 0) ?? [];
    return (
      <div className="invoice-print-page">
        <PrintToolbar canPrint={false} />
        <div className="invoice-print-state">
          <ErrorAlert message={error.message} items={missingItems} />
        </div>
      </div>
    );
  }

  // Dùng thông báo an toàn cho lỗi mạng, lỗi máy chủ hoặc response sai cấu trúc.
  if (error || !data) {
    return (
      <div className="invoice-print-page">
        <PrintToolbar canPrint={false} />
        <div className="invoice-print-state">
          <ErrorAlert message={getErrorMessage(error, 'Tải dữ liệu hóa đơn thất bại.')} />
        </div>
      </div>
    );
  }

  // Chặn trang trống có nút In khi server trả về mảng hóa đơn rỗng.
  if (data.invoices.length === 0) {
    return (
      <div className="invoice-print-page">
        <PrintToolbar canPrint={false} />
        <div className="invoice-print-state">
          <ErrorAlert message="Không có hóa đơn để in." />
        </div>
      </div>
    );
  }

  return (
    <div className="invoice-print-page">
      <PrintToolbar canPrint={true} />
      <div className="invoice-print-sheets" ref={sheetsRef}>
        {data.invoices.map((invoice) => (
          <InvoiceSheet
            key={invoice.code}
            shop={data.shop}
            invoice={invoice}
            printedAtText={printedAtText}
          />
        ))}
      </div>
    </div>
  );
}
