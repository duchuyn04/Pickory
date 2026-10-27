import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { apiRequest, ApiError } from '../../../shared/api';
import type { AttributeDefinition } from '../../../shared/catalog/attributes';
import { type DescriptionDoc } from '../../../shared/catalog/description-schema';
import { validateSpecs } from '../../../shared/catalog/validate-specs';
import { useCan } from '../../identity/hooks/use-actor';
import { DescriptionEditor } from './description-editor';
import { ImageManager, type ProductImageDraft } from './image-manager';
import {
  MAX_IMAGES_PER_PRODUCT,
  uploadImageFiles,
  uploadToCloudinary,
  type UploadSignature,
} from './image-upload';
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
import { ProductStatusPanel } from './product-status-panel';
import type { ProductStatus } from './product-status';
import { SpecFields } from './spec-fields';
import { VariantTable } from './variant-table';
import { ErrorAlert } from '../../../shared/error-alert';
import './products.css';

export interface ProductCategory {
  id: string;
  parentId: string | null;
  name: string;
  attributes: AttributeDefinition[];
}
// Ảnh trong phản hồi chi tiết sản phẩm, đã sắp theo `position`.
export interface ProductImageDetail {
  id: string;
  publicId: string;
  alt: string;
  skuId: string | null;
  position: number;
  url: string;
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
  images: ProductImageDetail[];
  // Có giá trị khi sản phẩm đang bị xóa mềm và cần khôi phục trước khi sửa.
  deletedAt?: string | null;
}

const emptyDescription: DescriptionDoc = { type: 'doc', content: [{ type: 'paragraph' }] };

interface DeleteProductResult {
  outcome: 'hard_deleted' | 'soft_deleted';
  product: ProductDetail | null;
}

interface ProductFormViewProps {
  categories: ProductCategory[];
  brands: ProductBrand[];
  canWrite: boolean;
  product?: ProductDetail;
  onSave: (payload: Record<string, unknown>) => Promise<unknown>;
  // Chỉ trang sửa dùng ba hàm này. Trang tạo mới không có khối trạng thái nên không gọi.
  onChangeStatus: (target: ProductStatus) => Promise<void>;
  onDelete: () => Promise<unknown>;
  onRestore: () => Promise<void>;
}

export function ProductFormView({
  categories,
  brands,
  canWrite,
  product,
  onSave,
  onChangeStatus,
  onDelete,
  onRestore,
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
  const [images, setImages] = useState<ProductImageDraft[]>(() =>
    [...(product?.images ?? [])]
      .sort((first, second) => first.position - second.position)
      .map(({ publicId, alt, skuId, url }) => ({ publicId, alt, skuId, url })),
  );
  // Lỗi alt theo `publicId`, để lỗi đi theo ảnh khi admin đổi thứ tự.
  const [imageAltErrors, setImageAltErrors] = useState<Record<string, string>>({});
  const [uploadErrors, setUploadErrors] = useState<string[]>([]);
  const [isUploading, setIsUploading] = useState(false);
  const [specErrors, setSpecErrors] = useState<Record<string, string>>({});
  const [priceErrors, setPriceErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  // Ảnh chụp toàn bộ ô nhập. So ảnh chụp hiện tại với ảnh chụp lúc tải hoặc lúc lưu để biết còn thay đổi chưa lưu.
  const formSnapshot = JSON.stringify({
    categoryId,
    brandId,
    name,
    shortDesc,
    warrantyMonths,
    description,
    specs,
    options,
    variants,
    images,
  });
  const savedSnapshotRef = useRef(formSnapshot);
  const hasUnsavedChanges = formSnapshot !== savedSnapshotRef.current;

  const attributes = categoryId ? getLeafCategoryAttributes(categoryId, categories) : [];
  // Sản phẩm chỉ gắn vào danh mục lá, không thể chọn danh mục chứa danh mục con.
  const leafCategories = categories.filter(
    (category) => !categories.some((child) => child.parentId === category.id),
  );
  // Quyền ghi, thao tác lưu và trạng thái xóa mềm cùng khóa mọi ô để tránh ghi ngoài ý muốn.
  const disabled = !canWrite || isSaving || Boolean(product?.deletedAt);

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

  async function addImageFiles(files: File[]) {
    setIsUploading(true);
    setUploadErrors([]);
    try {
      // File sai bị chặn trước khi xin chữ ký. Chỉ tải số ảnh còn trống trong giới hạn của sản phẩm.
      const result = await uploadImageFiles(
        files,
        {
          requestSignature: () =>
            apiRequest<UploadSignature>('/api/admin/uploads/signature', { method: 'POST' }),
          uploadFile: (file, signature) => uploadToCloudinary(file, signature),
        },
        MAX_IMAGES_PER_PRODUCT - images.length,
      );
      // Ảnh mới thêm vào cuối danh sách với alt trống, để admin nhập mô tả trước khi lưu.
      setImages((current) => [
        ...current,
        ...result.uploaded.map(({ publicId, url }) => ({ publicId, url, alt: '', skuId: null })),
      ]);
      setUploadErrors(result.errors);
    } catch (error) {
      // Lỗi xin chữ ký (mất phiên, thiếu quyền) không làm mất ảnh đã có trong form.
      setUploadErrors([toCatalogErrorMessage(error, 'Không lấy được chữ ký tải ảnh.')]);
    } finally {
      setIsUploading(false);
    }
  }

  async function saveProduct(): Promise<boolean> {
    // Giao diện có thể đổi quyền khi đang mở form, nên chặn gửi ngay tại điểm lưu.
    if (!canWrite || isSaving || product?.deletedAt) return false;
    setFormError(null);
    // Thẩm định bộ thông số trước HTTP để lỗi luôn gắn đúng ô đang hiển thị.
    const validation = validateSpecs(specs, attributes);
    if (!validation.valid) {
      setSpecErrors(
        Object.fromEntries(validation.errors.map((error) => [error.key, error.message])),
      );
      return false;
    }
    setSpecErrors({});
    // Thiếu định danh hoặc tên sẽ khiến API từ chối toàn bộ sản phẩm.
    if (!categoryId || !brandId || !name.trim()) {
      setFormError('Nhập danh mục, thương hiệu và tên sản phẩm.');
      return false;
    }
    // Sinh tổ hợp trước khi gửi để giới hạn số SKU và phát hiện trục không hợp lệ.
    let combinations: Record<string, string>[];
    try {
      combinations = buildVariantCombinations(options);
    } catch (error) {
      setFormError(toCatalogErrorMessage(error, 'Tùy chọn không hợp lệ.'));
      return false;
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
    // Alt bắt buộc cho người dùng trình đọc màn hình. Lỗi hiện ngay dưới ô alt của đúng ảnh.
    const missingAlts = Object.fromEntries(
      images
        .filter((image) => image.alt.trim().length === 0)
        .map((image) => [image.publicId, 'Nhập mô tả ảnh (alt).']),
    );
    setImageAltErrors(missingAlts);
    // Không gửi nếu còn giá sai hoặc ảnh thiếu alt vì từng ô đã hiển thị lỗi riêng.
    if (Object.keys(invalidPrices).length > 0 || Object.keys(missingAlts).length > 0) return false;
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
        // Thứ tự trong danh sách là thứ tự hiển thị. Backend không nhận URL ảnh thu nhỏ.
        images: images.map(({ publicId, alt, skuId }) => ({ publicId, alt: alt.trim(), skuId })),
      });
      // Lưu xong, dữ liệu trên máy chủ khớp với dữ liệu trong form. Vì vậy các nút trạng thái được mở lại.
      savedSnapshotRef.current = formSnapshot;
      return true;
    } catch (error) {
      // Lỗi thông số trả về key từ API phải nằm ngay dưới ô, không xóa bản nháp.
      if (error instanceof ApiError && error.code === 'SPECS_INVALID' && error.details) {
        setSpecErrors(
          Object.fromEntries(error.details.map((detail) => [detail.key, detail.message])),
        );
      } else {
        setFormError(toCatalogErrorMessage(error, 'Không lưu được sản phẩm.'));
      }
      return false;
    } finally {
      // Mở lại form sau khi yêu cầu hoàn tất, kể cả khi API trả lỗi.
      setIsSaving(false);
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    await saveProduct();
  }

  return (
    <form className="page product-form" onSubmit={submit}>
      <div className="page-head">
        <div className="page-head-text">
          <h1>{product ? 'Chi tiết sản phẩm' : 'Tạo sản phẩm'}</h1>
          <p>
            {canWrite ? 'Thông tin và biến thể của sản phẩm' : 'Bạn chỉ có quyền xem sản phẩm.'}
          </p>
        </div>
        <Link className="btn" to="/admin/products">
          Về danh sách
        </Link>
      </div>
      {formError && <ErrorAlert message={formError} />}
      {/* Sản phẩm mới luôn là bản nháp và chưa có định danh, nên chỉ trang sửa có khối trạng thái. */}
      {product && (
        <ProductStatusPanel
          productName={product.name}
          status={product.status}
          deletedAt={product.deletedAt ?? null}
          canWrite={canWrite}
          hasUnsavedChanges={hasUnsavedChanges}
          onSaveChanges={saveProduct}
          onChangeStatus={onChangeStatus}
          onDelete={onDelete}
          onRestore={onRestore}
        />
      )}
      <section className="card product-section">
        <h2>Thông tin cơ bản</h2>
        <div className="form-grid form-grid-2">
          <div className="field">
            <label htmlFor="product-name">Tên sản phẩm</label>
            <input
              id="product-name"
              name="name"
              value={name}
              disabled={disabled}
              onChange={(event) => setName(event.target.value)}
            />
          </div>
          <div className="field">
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
          <div className="field">
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
          <div className="field">
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
          <div className="field field-full">
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
      <section className="card product-section">
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
      <section className="card product-section">
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
      <ImageManager
        images={images}
        // Chỉ biến thể đã lưu mới có định danh để gắn ảnh. Biến thể mới gắn được sau lần lưu đầu.
        variants={variants
          .filter((variant) => variant.id)
          .map((variant) => ({
            id: variant.id!,
            label: Object.values(variant.optionValues).join(' / ') || 'Mặc định',
          }))}
        disabled={disabled}
        isUploading={isUploading}
        uploadErrors={uploadErrors}
        altErrors={imageAltErrors}
        onChange={(nextImages) => {
          setImages(nextImages);
          // Bỏ lỗi của ảnh đã có alt, giữ lỗi của ảnh vẫn còn trống.
          setImageAltErrors((current) =>
            Object.fromEntries(
              Object.entries(current).filter(([publicId]) =>
                nextImages.some((image) => image.publicId === publicId && !image.alt.trim()),
              ),
            ),
          );
        }}
        onAddFiles={addImageFiles}
      />
      <ProductOptionsEditor options={options} disabled={disabled} onChange={updateOptions} />
      <VariantTable
        productId={product?.id}
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
        <div className="head-actions product-actions">
          <button className="btn btn-primary" disabled={disabled} type="submit">
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
  // Giữ đường về danh sách khi một truy vấn thất bại để quản trị viên không kẹt ở trang sửa.
  if (categoriesQuery.isError || brandsQuery.isError || productQuery.isError)
    return (
      <div className="page">
        <ErrorAlert
          message={toCatalogErrorMessage(
            categoriesQuery.error || brandsQuery.error || productQuery.error,
            'Không tải được dữ liệu sản phẩm.',
          )}
        />
        {/* Giữ liên kết theo bề rộng nội dung, vì `.page` kéo dãn phần tử con. */}
        <div className="row-actions">
          <Link className="btn" to="/admin/products">
            Về danh sách
          </Link>
        </div>
      </div>
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

  async function changeStatus(target: ProductStatus) {
    const updated = await apiRequest<ProductDetail>(`/api/admin/products/${id}/status`, {
      method: 'PUT',
      body: JSON.stringify({ status: target }),
    });
    // Ghi phản hồi vào cache để nhãn trạng thái đổi ngay, không chờ lần tải lại.
    queryClient.setQueryData(['admin-product', id], updated);
    // Danh sách hiện nhãn trạng thái của sản phẩm nên phải tải lại.
    await queryClient.invalidateQueries({ queryKey: ['admin-products'] });
  }

  async function deleteProduct() {
    const result = await apiRequest<DeleteProductResult>(`/api/admin/products/${id}`, {
      method: 'DELETE',
    });
    // Danh sách phải tải lại vì sản phẩm có thể đã đổi sang trạng thái xóa mềm.
    await queryClient.invalidateQueries({ queryKey: ['admin-products'] });
    if (result.outcome === 'soft_deleted' && result.product) {
      // Sản phẩm vẫn còn để khôi phục, nên cập nhật tại chỗ thay vì rời trang.
      queryClient.setQueryData(['admin-product', id], result.product);
      return;
    }
    // Xóa cứng làm sản phẩm mất hẳn, nên bỏ cache chi tiết trước khi về danh sách.
    queryClient.removeQueries({ queryKey: ['admin-product', id] });
    navigate('/admin/products');
  }

  async function restoreProduct() {
    const restored = await apiRequest<ProductDetail>(`/api/admin/products/${id}/restoration`, {
      method: 'POST',
    });
    // Ghi sản phẩm đã khôi phục vào cache để form mở khóa ngay.
    queryClient.setQueryData(['admin-product', id], restored);
    await queryClient.invalidateQueries({ queryKey: ['admin-products'] });
  }

  return (
    <ProductFormView
      key={id ?? 'new'}
      categories={categoriesQuery.data ?? []}
      brands={brandsQuery.data ?? []}
      product={productQuery.data}
      canWrite={canWrite}
      onSave={save}
      onChangeStatus={changeStatus}
      onDelete={deleteProduct}
      onRestore={restoreProduct}
    />
  );
}
