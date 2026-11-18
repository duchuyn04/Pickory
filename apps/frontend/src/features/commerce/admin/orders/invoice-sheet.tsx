import { formatVndInWords } from '../../../../shared/format-vnd-in-words';
import { formatVietnamDateTime } from '../../../../shared/vietnam-time';
import { formatProductPrice } from '../../../catalog/customer/product-display';
import { formatPaymentMethod, formatPaymentStatus } from '../../order-labels';
import type { OrderInvoice, OrderInvoicesResponse } from './order-invoices-schema';

type InvoiceShop = OrderInvoicesResponse['data']['shop'];

interface InvoiceSheetProps {
  shop: InvoiceShop;
  invoice: OrderInvoice;
  printedAtText: string;
}

// Chuỗi rỗng hoặc chỉ có khoảng trắng coi như chưa nhập để không hiện dòng trống trên giấy.
function readNonEmpty(value: string | null): string | null {
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
}

// Định dạng dòng giảm phí vận chuyển luôn dương hoặc bằng 0 thành chữ rõ dấu trừ khi có giảm.
function formatShippingDiscount(discount: number): string {
  if (discount > 0) {
    return `-${formatProductPrice(discount)}`;
  }
  return formatProductPrice(0);
}

// Một tờ hóa đơn bán hàng hoàn chỉnh cho một đơn hàng, tách riêng để dùng lại giữa màn hình và bản in.
export function InvoiceSheet({ shop, invoice, printedAtText }: InvoiceSheetProps) {
  const shopName = readNonEmpty(shop.name) ?? 'Pickory';
  const shopAddress = readNonEmpty(shop.address);
  const shopPhone = readNonEmpty(shop.phone);
  const shopTaxCode = readNonEmpty(shop.taxCode);
  const isCancelled = invoice.status === 'cancelled';

  return (
    <article
      className="invoice-sheet"
      data-testid={`invoice-${invoice.code}`}
      aria-label={`Hóa đơn ${invoice.code}`}
    >
      {/* Dấu đơn đã hủy đặt trên cùng trước cả logo để người đóng gói nhận ra ngay khi cầm giấy. */}
      {isCancelled && (
        <div className="invoice-cancelled-banner">
          <span className="invoice-cancelled-stamp" data-testid="invoice-cancelled-stamp">
            ĐƠN ĐÃ HỦY
          </span>
        </div>
      )}

      <header className="invoice-header">
        <div className="invoice-brand">
          <img src="/media/brand/pickory-logo.png" alt="Logo Pickory" className="invoice-logo" />
          <div className="invoice-shop-info">
            <strong className="invoice-shop-name">{shopName}</strong>
            {shopAddress && <p className="invoice-shop-line">Địa chỉ: {shopAddress}</p>}
            {shopPhone && <p className="invoice-shop-line">Điện thoại: {shopPhone}</p>}
            {shopTaxCode && <p className="invoice-shop-line">Mã số thuế: {shopTaxCode}</p>}
          </div>
        </div>

        <div className="invoice-title-block">
          <h1 className="invoice-title">HÓA ĐƠN BÁN HÀNG</h1>
          <dl className="invoice-meta">
            <div>
              <dt>Mã đơn:</dt>
              <dd className="mono">{invoice.code}</dd>
            </div>
            <div>
              <dt>Ngày đặt:</dt>
              <dd>{formatVietnamDateTime(invoice.createdAt)}</dd>
            </div>
            <div>
              <dt>Ngày in:</dt>
              <dd data-testid="invoice-printed-at">{printedAtText}</dd>
            </div>
          </dl>
        </div>
      </header>

      <section className="invoice-parties">
        <div className="invoice-party">
          <h2 className="invoice-section-heading">Thông tin khách hàng</h2>
          <p className="invoice-party-line">
            <span>Khách hàng:</span> <strong>{invoice.customer.name}</strong>
          </p>
          <p className="invoice-party-line">
            <span>Số điện thoại:</span> {invoice.customer.phone}
          </p>
          {/* Không in email khách hàng để giữ gọn bản in theo đúng 10 phần của tài liệu đặc tả. */}
          <p className="invoice-party-line">
            <span>Địa chỉ giao:</span> {invoice.shippingAddressText}
          </p>
        </div>

        {/* Khối thuế giá trị gia tăng (VAT) chỉ vẽ khi khách có yêu cầu xuất hóa đơn công ty. */}
        {invoice.vatInvoice !== null && (
          <div className="invoice-party invoice-vat">
            <h2 className="invoice-section-heading">Thông tin xuất hóa đơn VAT</h2>
            <p className="invoice-party-line">
              <span>Tên công ty:</span> <strong>{invoice.vatInvoice.companyName}</strong>
            </p>
            <p className="invoice-party-line">
              <span>Mã số thuế:</span> {invoice.vatInvoice.taxCode}
            </p>
            <p className="invoice-party-line">
              <span>Địa chỉ:</span> {invoice.vatInvoice.address}
            </p>
            <p className="invoice-party-line">
              <span>Email nhận hóa đơn:</span> {invoice.vatInvoice.email}
            </p>
          </div>
        )}
      </section>

      <div className="invoice-table-wrap">
        <table className="invoice-items-table">
          <thead>
            <tr>
              <th scope="col" className="invoice-col-index">
                STT
              </th>
              <th scope="col" className="invoice-col-product">
                Sản phẩm
              </th>
              <th scope="col" className="invoice-col-num">
                Số lượng
              </th>
              <th scope="col" className="invoice-col-num">
                Đơn giá
              </th>
              <th scope="col" className="invoice-col-num">
                Thành tiền
              </th>
            </tr>
          </thead>
          <tbody>
            {invoice.lines.map((line, index) => {
              const optionText = readNonEmpty(line.optionText);
              return (
                <tr key={`${line.skuCode}-${index}`}>
                  {/* Đánh số thứ tự lại từ 1 trên từng tờ hóa đơn thay vì nối tiếp qua các đơn. */}
                  <td className="invoice-col-index">{index + 1}</td>
                  <td className="invoice-col-product">
                    <span className="invoice-line-name">{line.name}</span>
                    <span className="invoice-line-sub">
                      {optionText && (
                        <>
                          <span className="invoice-line-option">{optionText}</span>
                          {/* Chỉ chèn dấu chấm phân cách khi sản phẩm có tên phân loại đi kèm. */}
                          {' · '}
                        </>
                      )}
                      <span className="mono">SKU: {line.skuCode}</span>
                    </span>
                  </td>
                  <td className="invoice-col-num">{line.quantity}</td>
                  <td className="invoice-col-num">{formatProductPrice(line.unitPrice)}</td>
                  <td className="invoice-col-num">{formatProductPrice(line.lineTotal)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {/* Giữ đúng thứ tự đọc 6, 7, 8 của đặc tả trong cây phần tử và dùng lưới để gọn chiều cao. */}
      <section className="invoice-summary-section">
        <dl className="invoice-totals">
          <div>
            <dt>Tạm tính</dt>
            <dd>{formatProductPrice(invoice.subtotal)}</dd>
          </div>
          <div>
            <dt>Phí vận chuyển</dt>
            <dd>{formatProductPrice(invoice.shippingFee)}</dd>
          </div>
          <div>
            <dt>Giảm phí vận chuyển</dt>
            <dd>{formatShippingDiscount(invoice.shippingDiscount)}</dd>
          </div>
          <div className="invoice-totals-grand">
            <dt>Tổng cộng</dt>
            <dd>{formatProductPrice(invoice.total)}</dd>
          </div>
        </dl>

        <p className="invoice-amount-words">
          <span>Tổng cộng bằng chữ:</span> <strong>{formatVndInWords(invoice.total)}</strong>
        </p>

        <div className="invoice-payment-info">
          <p className="invoice-payment-line">
            <span>Phương thức thanh toán:</span> {formatPaymentMethod(invoice.paymentMethod)}
          </p>
          <p className="invoice-payment-line">
            <span>Trạng thái thanh toán:</span> {formatPaymentStatus(invoice.paymentStatus)}
          </p>
        </div>
      </section>

      <section className="invoice-signatures">
        <div className="invoice-signature-box">
          <p className="invoice-signature-role">Người mua hàng</p>
          <p className="invoice-signature-hint">(Ký, ghi rõ họ tên)</p>
        </div>
        <div className="invoice-signature-box">
          <p className="invoice-signature-role">Người bán hàng</p>
          <p className="invoice-signature-hint">(Ký, ghi rõ họ tên)</p>
        </div>
      </section>

      <p className="invoice-footer-note">Hóa đơn này không thay thế hóa đơn giá trị gia tăng.</p>
    </article>
  );
}
