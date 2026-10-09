import { Link } from 'react-router-dom';
import { toCombinationKey, type ProductVariant } from './product-logic';

interface VariantTableProps {
  // Có mã sản phẩm thì SKU đã lưu có liên kết tới màn tồn kho. Sản phẩm mới chưa có mã nên không có liên kết.
  productId?: string;
  variants: ProductVariant[];
  disabled: boolean;
  priceErrors: Record<string, string>;
  onChange: (variants: ProductVariant[]) => void;
  onPriceChange: (optionValues: Record<string, string>) => void;
}

type NumericField = 'compareAtPrice' | 'weightGrams' | 'lengthCm' | 'widthCm' | 'heightCm';
const numericColumns: { field: NumericField; title: string }[] = [
  { field: 'compareAtPrice', title: 'Giá niêm yết' },
  { field: 'weightGrams', title: 'Cân nặng (g)' },
  { field: 'lengthCm', title: 'Dài (cm)' },
  { field: 'widthCm', title: 'Rộng (cm)' },
  { field: 'heightCm', title: 'Cao (cm)' },
];

// Bảng dùng cùng khóa tổ hợp với form để gắn lỗi giá đúng SKU.
export function VariantTable({
  productId,
  variants,
  disabled,
  priceErrors,
  onChange,
  onPriceChange,
}: VariantTableProps) {
  // Chỉ sửa dòng đang nhập vì các biến thể khác có giá và mã SKU độc lập.
  function updateVariant(index: number, changes: Partial<ProductVariant>) {
    onChange(
      variants.map((variant, position) =>
        position === index ? { ...variant, ...changes } : variant,
      ),
    );
  }

  return (
    <section className="card product-section">
      <h2>Biến thể</h2>
      <p>Tồn kho chỉ xem. Điều chỉnh tồn kho ở màn tồn kho với lý do và nhật ký.</p>
      <div className="table-wrap product-table-scroll">
        <table className="tbl product-table">
          <thead>
            <tr>
              <th scope="col">Tổ hợp</th>
              <th scope="col">Mã SKU</th>
              <th scope="col">Giá bán</th>
              <th scope="col">Tồn kho</th>
              <th scope="col">Trạng thái</th>
              {numericColumns.map((column) => (
                <th scope="col" key={column.field}>
                  {column.title}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {variants.map((variant, index) => {
              // Khóa tổ hợp nối lỗi giá với đúng biến thể dù thứ tự SKU thay đổi.
              const combinationKey = toCombinationKey(variant.optionValues);
              const combinationName = Object.values(variant.optionValues).join(' / ') || 'Mặc định';
              const priceError = priceErrors[combinationKey];
              const priceErrorId = `variant-price-${index}-error`;
              return (
                <tr key={combinationKey} data-variant-row>
                  <th scope="row">{combinationName}</th>
                  <td>
                    <input
                      aria-label={`Mã SKU ${combinationName}`}
                      name={`variant-code-${index}`}
                      value={variant.code ?? ''}
                      disabled={disabled}
                      onChange={(event) => updateVariant(index, { code: event.target.value })}
                    />
                  </td>
                  <td>
                    {/* Giá bán cần lỗi ngay dưới ô; không để ràng buộc min chặn sự kiện gửi form. */}
                    <input
                      aria-label={`Giá bán ${combinationName}`}
                      name={`variant-price-${index}`}
                      type="number"
                      aria-invalid={priceError ? true : undefined}
                      aria-describedby={priceError ? priceErrorId : undefined}
                      value={variant.price ?? ''}
                      disabled={disabled}
                      onChange={(event) => {
                        // Sửa giá chỉ xóa lỗi của tổ hợp này, không xóa lỗi ở biến thể khác.
                        onPriceChange(variant.optionValues);
                        // Ô giá trống không được lưu thành số 0 để người dùng nhận lỗi thiếu giá.
                        updateVariant(index, {
                          price: event.target.value === '' ? '' : Number(event.target.value),
                        });
                      }}
                    />
                    {priceError && (
                      <span id={priceErrorId} className="form-error">
                        {priceError}
                      </span>
                    )}
                  </td>
                  {/* Tồn kho chỉ đọc vì chỉnh tồn phải qua nghiệp vụ ghi nhận lý do. */}
                  <td>
                    {variant.stock ?? 0}
                    {/* SKU chưa lưu chưa có id từ máy chủ nên chưa có trang tồn kho. */}
                    {productId && variant.id && (
                      <Link
                        className="cell-link"
                        aria-label={`Tồn kho ${combinationName}`}
                        to={`/admin/products/${productId}/skus/${variant.id}/inventory`}
                      >
                        Tồn kho
                      </Link>
                    )}
                  </td>
                  <td>
                    <label className="check">
                      <input
                        type="checkbox"
                        aria-label={`Bật ${combinationName}`}
                        checked={variant.enabled}
                        disabled={disabled}
                        onChange={(event) =>
                          updateVariant(index, { enabled: event.target.checked })
                        }
                      />{' '}
                      Bật
                    </label>
                  </td>
                  {/* Thông tin đóng gói nằm sau thao tác bán để nút bật luôn hiện trên màn 1280 px. */}
                  {numericColumns.map(({ field, title }) => (
                    <td key={field}>
                      <input
                        aria-label={`${title} ${combinationName}`}
                        name={`variant-${field.replace(/[A-Z]/g, (letter) => `-${letter.toLowerCase()}`)}-${index}`}
                        type="number"
                        min={0}
                        value={variant[field] ?? ''}
                        disabled={disabled}
                        onChange={(event) => {
                          // Ô trống phải là null vì API phân biệt thiếu số đo với giá trị 0.
                          updateVariant(index, {
                            [field]: event.target.value === '' ? null : Number(event.target.value),
                          });
                        }}
                      />
                    </td>
                  ))}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </section>
  );
}
