import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { apiRequest, ApiError } from '../../../shared/api';
import type { AttributeDefinition } from '../../../shared/catalog/attributes';
import { type DescriptionDoc } from '../../../shared/catalog/description-schema';
import { validateSpecs } from '../../../shared/catalog/validate-specs';
import { useCan } from '../../identity/hooks/use-actor';
import { DescriptionEditor } from './description-editor';
import { toCatalogErrorMessage } from './catalog-errors';
import {
  buildVariantCombinations,
  getLeafCategoryAttributes,
  mergeProductVariants,
  toProductVariantRequest,
  toCombinationKey,
  type ProductOption,
  type ProductVariant,
} from './product-logic';
import { ProductOptionsEditor } from './product-options-editor';
import { SpecFields } from './spec-fields';
import { VariantTable } from './variant-table';
import './products.css';

export interface ProductCategory {
  id: string;
  parentId: string | null;
  name: string;
  attributes: AttributeDefinition[];
}
export interface ProductBrand {
  id: string;
  name: string;
}
export interface ProductDetail {
  id: string;
  categoryId: string;
  brandId: string;
  name: string;
  shortDesc: string;
  description: DescriptionDoc;
  specs: Record<string, unknown>;
  options: ProductOption[];
  variants: ProductVariant[];
  warrantyMonths: number;
  status: string;
  minPrice: number | null;
}

const emptyDescription: DescriptionDoc = { type: 'doc', content: [{ type: 'paragraph' }] };

interface ProductFormViewProps {
  categories: ProductCategory[];
  brands: ProductBrand[];
  canWrite: boolean;
  product?: ProductDetail;
  onSave: (payload: Record<string, unknown>) => Promise<unknown>;
}

export function ProductFormView({
  categories,
  brands,
  canWrite,
  product,
  onSave,
}: ProductFormViewProps) {
  const [categoryId, setCategoryId] = useState(product?.categoryId ?? '');
  const [brandId, setBrandId] = useState(product?.brandId ?? '');
  const [name, setName] = useState(product?.name ?? '');
  const [shortDesc, setShortDesc] = useState(product?.shortDesc ?? '');
  const [warrantyMonths, setWarrantyMonths] = useState(product?.warrantyMonths ?? 0);
  const [description, setDescription] = useState<DescriptionDoc>(
    product?.description ?? emptyDescription,
  );
  const [specs, setSpecs] = useState<Record<string, unknown>>(product?.specs ?? {});
  const [options, setOptions] = useState<ProductOption[]>(product?.options ?? []);
  // API không bảo đảm thứ tự SKU, nên ghép lại theo tổ hợp để giá luôn ở cùng dòng sau khi tải lại.
  const [variants, setVariants] = useState<ProductVariant[]>(() =>
    mergeProductVariants(buildVariantCombinations(product?.options ?? []), product?.variants),
  );
  const [specErrors, setSpecErrors] = useState<Record<string, string>>({});
  const [priceErrors, setPriceErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  const attributes = categoryId ? getLeafCategoryAttributes(categoryId, categories) : [];
  // Sản phẩm chỉ gắn vào danh mục lá, không thể chọn danh mục chứa danh mục con.
  const leafCategories = categories.filter(
    (category) => !categories.some((child) => child.parentId === category.id),
  );
  // Quyền products:write và trạng thái lưu cùng khóa mọi ô để tránh ghi ngoài quyền hoặc gửi trùng.
  const disabled = !canWrite || isSaving;

  function updateOptions(nextOptions: ProductOption[]) {
    setOptions(nextOptions);
    // Bản nháp mới thay lỗi gửi trước đó; chỉ lần gửi kế tiếp mới báo lỗi trục chưa hoàn chỉnh.
    setFormError(null);
    // Ưu tiên giá đang soạn. Giữ SKU đã lưu nếu người dùng bỏ rồi thêm lại một giá trị.
    try {
      setVariants(
        mergeProductVariants(buildVariantCombinations(nextOptions), [
          ...variants,
          ...(product?.variants ?? []),
        ]),
      );
    } catch {
      // Trục đang nhập chưa thể sinh tổ hợp. Giữ bảng cũ đến khi người dùng nhập đủ giá trị.
      return;
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    // Giao diện có thể đổi quyền khi đang mở form, nên chặn gửi ngay tại điểm lưu.
    if (!canWrite || isSaving) return;
    setFormError(null);
    // Thẩm định bộ thông số trước HTTP để lỗi luôn gắn đúng ô đang hiển thị.
    const validation = validateSpecs(specs, attributes);
    if (!validation.valid) {
      setSpecErrors(
        Object.fromEntries(validation.errors.map((error) => [error.key, error.message])),
      );
      return;
    }
    setSpecErrors({});
    // Thiếu định danh hoặc tên sẽ khiến API từ chối toàn bộ sản phẩm.
    if (!categoryId || !brandId || !name.trim()) {
      setFormError('Nhập danh mục, thương hiệu và tên sản phẩm.');
      return;
    }
    // Sinh tổ hợp trước khi gửi để giới hạn số SKU và phát hiện trục không hợp lệ.
    let combinations: Record<string, string>[];
    try {
      combinations = buildVariantCombinations(options);
    } catch (error) {
      setFormError(toCatalogErrorMessage(error, 'Tùy chọn không hợp lệ.'));
      return;
    }
    // Gửi các trục đã chuẩn hóa để API lưu đúng tên tổ hợp đang hiển thị trên bảng.
    const normalizedOptions = options.map((option) => ({
      name: option.name.trim(),
      values: option.values.map((value) => value.trim()),
    }));
    const currentVariants = mergeProductVariants(combinations, variants);
    // Gắn lỗi theo tổ hợp để người dùng nhận đúng ô cần sửa khi nhiều SKU cùng hiển thị.
    const invalidPrices = Object.fromEntries(
      currentVariants
        .filter((variant) => Number(variant.price) <= 0)
        .map((variant) => [toCombinationKey(variant.optionValues), 'Nhập giá bán lớn hơn 0.']),
    );
    setPriceErrors(invalidPrices);
    // Không gửi nếu còn giá sai vì từng ô đã hiển thị lỗi riêng.
    if (Object.keys(invalidPrices).length > 0) return;
    // Khóa form trong lúc chờ máy chủ để tránh hai lần ghi cùng một bản nháp.
    setIsSaving(true);
    try {
      // Tồn kho chỉ đọc, API chỉ nhận giá và thông tin đóng gói.
      await onSave({
        categoryId,
        brandId,
        name: name.trim(),
        shortDesc,
        description,
        specs,
        options: normalizedOptions,
        warrantyMonths,
        variants: currentVariants.map(toProductVariantRequest),
      });
    } catch (error) {
      // Lỗi thông số trả về key từ API phải nằm ngay dưới ô, không xóa bản nháp.
      if (error instanceof ApiError && error.code === 'SPECS_INVALID' && error.details) {
        setSpecErrors(
          Object.fromEntries(error.details.map((detail) => [detail.key, detail.message])),
        );
      } else {
        setFormError(toCatalogErrorMessage(error, 'Không lưu được sản phẩm.'));
      }
    } finally {
      // Mở lại form sau khi yêu cầu hoàn tất, kể cả khi API trả lỗi.
      setIsSaving(false);
    }
  }

  return (
    <form className="product-form" onSubmit={submit}>
      <div className="product-page-heading">
        <div>
          <h1>{product ? 'Chi tiết sản phẩm' : 'Tạo sản phẩm'}</h1>
          <p>
            {canWrite ? 'Thông tin và biến thể của sản phẩm' : 'Bạn chỉ có quyền xem sản phẩm.'}
          </p>
        </div>
        <Link to="/admin/products">Về danh sách</Link>
      </div>
      {formError && (
        <p role="alert" className="product-alert">
          {formError}
        </p>
      )}
      <section className="product-section">
        <h2>Thông tin cơ bản</h2>
        <div className="product-fields">
          <div className="product-field">
            <label htmlFor="product-name">Tên sản phẩm</label>
            <input
              id="product-name"
              name="name"
              value={name}
              disabled={disabled}
              onChange={(event) => setName(event.target.value)}
            />
          </div>
          <div className="product-field">
            <label htmlFor="product-category">Danh mục lá</label>
            <select
              id="product-category"
              name="categoryId"
              value={categoryId}
              disabled={disabled}
              onChange={(event) => {
                const nextCategoryId = event.target.value;
                // Chỉ giữ thông số có trong danh mục mới, vì thuộc tính riêng của lá cũ không còn hợp lệ.
                const allowedKeys = new Set(
                  getLeafCategoryAttributes(nextCategoryId, categories).map(
                    (attribute) => attribute.key,
                  ),
                );
                setCategoryId(nextCategoryId);
                setSpecs((current) =>
                  Object.fromEntries(
                    Object.entries(current).filter(([key]) => allowedKeys.has(key)),
                  ),
                );
                // Lỗi của danh mục cũ không còn tương ứng với ô đang hiển thị.
                setSpecErrors({});
              }}
            >
              <option value="">Chọn danh mục</option>
              {leafCategories.map((category) => (
                <option value={category.id} key={category.id}>
                  {category.name}
                </option>
              ))}
            </select>
          </div>
          <div className="product-field">
            <label htmlFor="product-brand">Thương hiệu</label>
            <select
              id="product-brand"
              name="brandId"
              value={brandId}
              disabled={disabled}
              onChange={(event) => setBrandId(event.target.value)}
            >
              <option value="">Chọn thương hiệu</option>
              {brands.map((brand) => (
                <option value={brand.id} key={brand.id}>
                  {brand.name}
                </option>
              ))}
            </select>
          </div>
          <div className="product-field">
            <label htmlFor="product-warranty">Bảo hành (tháng)</label>
            <input
              id="product-warranty"
              name="warrantyMonths"
              type="number"
              min="0"
              value={warrantyMonths}
              disabled={disabled}
              onChange={(event) => setWarrantyMonths(Number(event.target.value))}
            />
          </div>
          <div className="product-field product-field-wide">
            <label htmlFor="product-short-desc">Mô tả ngắn</label>
            <textarea
              id="product-short-desc"
              name="shortDesc"
              value={shortDesc}
              disabled={disabled}
              onChange={(event) => setShortDesc(event.target.value)}
            />
          </div>
        </div>
      </section>
      <section className="product-section">
        <h2>Thông số kỹ thuật</h2>
        <SpecFields
          attributes={attributes}
          values={specs}
          errors={specErrors}
          disabled={disabled}
          onChange={(key, value) => {
            setSpecs((current) => {
              const next = { ...current };
              if (value === undefined) delete next[key];
              else next[key] = value;
              return next;
            });
            setSpecErrors((current) => {
              const next = { ...current };
              delete next[key];
              return next;
            });
          }}
        />
      </section>
      <section className="product-section">
        <h2>Mô tả chi tiết</h2>
        <DescriptionEditor
          value={description}
          onChange={setDescription}
          disabled={disabled}
          onValidationError={(message) => {
            if (message) setFormError(message);
          }}
        />
      </section>
      <ProductOptionsEditor options={options} disabled={disabled} onChange={updateOptions} />
      <VariantTable
        variants={variants}
        disabled={disabled}
        priceErrors={priceErrors}
        onChange={setVariants}
        onPriceChange={(optionValues) => {
          // Xóa lỗi của tổ hợp vừa sửa, giữ nguyên lỗi ở các tổ hợp còn lại.
          const combinationKey = toCombinationKey(optionValues);
          setPriceErrors((current) => {
            const next = { ...current };
            delete next[combinationKey];
            return next;
          });
        }}
      />
      {/* Chỉ quyền products:write mới được hiển thị thao tác lưu. */}
      {canWrite && (
        <div className="product-actions">
          <button className="product-button-primary" disabled={isSaving} type="submit">
            {isSaving ? 'Đang lưu...' : 'Lưu sản phẩm'}
          </button>
        </div>
      )}
    </form>
  );
}

export function ProductFormPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canWrite = useCan('products:write');
  const categoriesQuery = useQuery<ProductCategory[]>({
    queryKey: ['admin-categories'],
    queryFn: () => apiRequest('/api/admin/categories'),
  });
  const brandsQuery = useQuery<ProductBrand[]>({
    queryKey: ['admin-brands'],
    queryFn: () => apiRequest('/api/brands'),
  });
  const productQuery = useQuery<ProductDetail>({
    queryKey: ['admin-product', id],
    queryFn: () => apiRequest(`/api/admin/products/${id}`),
    enabled: !!id,
    // Hủy cache khi rời trang để lần mở sau chỉ khởi tạo form từ dữ liệu mới của server.
    gcTime: 0,
  });
  if (categoriesQuery.isPending || brandsQuery.isPending || (id && productQuery.isPending))
    return <p role="status">Đang tải sản phẩm...</p>;
  if (categoriesQuery.isError || brandsQuery.isError || productQuery.isError)
    return (
      <p role="alert">
        {toCatalogErrorMessage(
          categoriesQuery.error || brandsQuery.error || productQuery.error,
          'Không tải được dữ liệu sản phẩm.',
        )}
      </p>
    );

  async function save(payload: Record<string, unknown>) {
    // Dùng PATCH khi sửa để giữ định danh, POST khi tạo để máy chủ cấp định danh mới.
    const saved = await apiRequest<ProductDetail>(
      id ? `/api/admin/products/${id}` : '/api/admin/products',
      { method: id ? 'PATCH' : 'POST', body: JSON.stringify(payload) },
    );
    // Phản hồi ghi thành công thay thế bản chi tiết cũ trước khi người dùng mở lại form.
    queryClient.setQueryData(['admin-product', saved.id], saved);
    // Danh sách phải tải lại vì giá và trạng thái hiển thị có thể đã thay đổi.
    await queryClient.invalidateQueries({ queryKey: ['admin-products'] });
    // Thay lịch sử trang tạo để nút quay lại không mở lại bản nháp đã lưu.
    navigate(`/admin/products/${saved.id}`, { replace: true });
  }

  return (
    <ProductFormView
      key={id ?? 'new'}
      categories={categoriesQuery.data ?? []}
      brands={brandsQuery.data ?? []}
      product={productQuery.data}
      canWrite={canWrite}
      onSave={save}
    />
  );
}
