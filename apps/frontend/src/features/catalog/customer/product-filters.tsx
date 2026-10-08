import { useEffect, useState, type FormEvent } from 'react';
import { FieldError, fieldErrorProps } from '../../../shared/field-error';
import type { AttributeFacet, ProductFacets } from './catalog-api';
import {
  attributeRangeKey,
  setAttributeRange,
  setAttributeValues,
  setInStock,
  setPriceRange,
  toggleAttributeValue,
  toggleBrand,
  type CategoryQuery,
} from './category-query';
import {
  attributeLegend,
  buildAttributeOptions,
  buildBrandOptions,
  formatOptionLabel,
  type FilterOption,
} from './facet-labels';
import { validateRangeInput, type RangeNumberKind } from './range-input';

interface ProductFiltersProps {
  facets: ProductFacets;
  query: CategoryQuery;
  // Tên thương hiệu theo slug, để hiện tên cho thương hiệu đang chọn mà facets không còn.
  brandNames?: Record<string, string>;
  // Nhận trạng thái lọc mới. Trang cha ghi trạng thái này lên URL.
  onChange: (next: CategoryQuery) => void;
}

interface CheckboxGroupProps {
  legend: string;
  idPrefix: string;
  options: FilterOption[];
  selectedValues: string[];
  onToggle: (value: string) => void;
}

function CheckboxGroup({
  legend,
  idPrefix,
  options,
  selectedValues,
  onToggle,
}: CheckboxGroupProps) {
  return (
    <fieldset className="catalog-filter-group">
      <legend>{legend}</legend>
      {options.map((option) => {
        const inputId = `${idPrefix}-${option.value}`;
        return (
          <label key={option.value} htmlFor={inputId} className="check catalog-option">
            <input
              id={inputId}
              type="checkbox"
              checked={selectedValues.includes(option.value)}
              onChange={() => onToggle(option.value)}
            />
            <span>{formatOptionLabel(option)}</span>
          </label>
        );
      })}
    </fieldset>
  );
}

interface RangeGroupProps {
  legend: string;
  idPrefix: string;
  kind: RangeNumberKind;
  initialMin: string;
  initialMax: string;
  minPlaceholder: string;
  maxPlaceholder: string;
  onApply: (minValue: string, maxValue: string) => void;
}

// Hai ô "Từ" và "Đến" chỉ áp khi bấm "Áp dụng" hoặc Enter, để mỗi phím gõ không gọi API.
function RangeGroup({
  legend,
  idPrefix,
  kind,
  initialMin,
  initialMax,
  minPlaceholder,
  maxPlaceholder,
  onApply,
}: RangeGroupProps) {
  const [minValue, setMinValue] = useState(initialMin);
  const [maxValue, setMaxValue] = useState(initialMax);
  const [errors, setErrors] = useState<{ minError: string | null; maxError: string | null }>({
    minError: null,
    maxError: null,
  });
  useEffect(() => {
    // Bộ lọc đổi từ ngoài (nút Back, xóa bộ lọc) thì ô nhập nạp lại giá trị mới. Không dựng lại ô, để focus không mất.
    setMinValue(initialMin);
    setMaxValue(initialMax);
    setErrors({ minError: null, maxError: null });
  }, [initialMin, initialMax]);

  const minId = `${idPrefix}-min`;
  const maxId = `${idPrefix}-max`;

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    // Chặn gửi form theo kiểu trình duyệt, vì trang tự ghi bộ lọc lên URL.
    event.preventDefault();
    const nextErrors = validateRangeInput(minValue.trim(), maxValue.trim(), kind);
    setErrors(nextErrors);
    // Khoảng sai không được áp, vì API sẽ từ chối nó bằng lỗi 400.
    if (nextErrors.minError || nextErrors.maxError) return;
    onApply(minValue, maxValue);
  }

  return (
    <fieldset className="catalog-filter-group">
      <legend>{legend}</legend>
      <form className="catalog-range" onSubmit={handleSubmit} noValidate>
        <div className="field">
          <label htmlFor={minId}>Từ</label>
          <input
            id={minId}
            type="number"
            inputMode={kind === 'integer' ? 'numeric' : 'decimal'}
            step={kind === 'integer' ? 1 : 'any'}
            placeholder={minPlaceholder}
            value={minValue}
            onChange={(event) => setMinValue(event.target.value)}
            {...fieldErrorProps(minId, errors.minError)}
          />
          <FieldError inputId={minId} message={errors.minError} />
        </div>
        <div className="field">
          <label htmlFor={maxId}>Đến</label>
          <input
            id={maxId}
            type="number"
            inputMode={kind === 'integer' ? 'numeric' : 'decimal'}
            step={kind === 'integer' ? 1 : 'any'}
            placeholder={maxPlaceholder}
            value={maxValue}
            onChange={(event) => setMaxValue(event.target.value)}
            {...fieldErrorProps(maxId, errors.maxError)}
          />
          <FieldError inputId={maxId} message={errors.maxError} />
        </div>
        <button type="submit" className="btn btn-sm catalog-range-apply">
          Áp dụng
        </button>
      </form>
    </fieldset>
  );
}

function NumberAttributeGroup({
  facet,
  query,
  onChange,
}: {
  facet: AttributeFacet;
  query: CategoryQuery;
  onChange: (next: CategoryQuery) => void;
}) {
  const currentMin = query.attributes[attributeRangeKey(facet.key, 'min')]?.[0] ?? '';
  const currentMax = query.attributes[attributeRangeKey(facet.key, 'max')]?.[0] ?? '';
  return (
    <RangeGroup
      legend={attributeLegend(facet)}
      idPrefix={`filter-${facet.key}`}
      kind="decimal"
      initialMin={currentMin}
      initialMax={currentMax}
      minPlaceholder={facet.min == null ? '' : String(facet.min)}
      maxPlaceholder={facet.max == null ? '' : String(facet.max)}
      onApply={(minValue, maxValue) =>
        onChange(setAttributeRange(query, facet.key, minValue, maxValue))
      }
    />
  );
}

function BooleanAttributeGroup({
  facet,
  query,
  onChange,
}: {
  facet: AttributeFacet;
  query: CategoryQuery;
  onChange: (next: CategoryQuery) => void;
}) {
  const selectedValue = query.attributes[facet.key]?.[0] ?? '';
  const options = buildAttributeOptions(facet, selectedValue === '' ? [] : [selectedValue]);
  const radioName = `filter-${facet.key}`;
  // Thứ tự "Tất cả", "Có", "Không" để lựa chọn thường dùng nằm trước.
  const choices = [
    { value: '', text: 'Tất cả' },
    ...options
      .toSorted((first, second) => Number(second.value === 'true') - Number(first.value === 'true'))
      .map((option) => ({ value: option.value, text: formatOptionLabel(option) })),
  ];
  return (
    <fieldset className="catalog-filter-group">
      <legend>{attributeLegend(facet)}</legend>
      {choices.map((choice) => {
        const inputId = `${radioName}-${choice.value === '' ? 'all' : choice.value}`;
        return (
          <label key={inputId} htmlFor={inputId} className="check catalog-option">
            <input
              id={inputId}
              type="radio"
              name={radioName}
              checked={selectedValue === choice.value}
              onChange={() =>
                onChange(
                  setAttributeValues(query, facet.key, choice.value === '' ? [] : [choice.value]),
                )
              }
            />
            <span>{choice.text}</span>
          </label>
        );
      })}
    </fieldset>
  );
}

function AttributeGroup({
  facet,
  query,
  onChange,
}: {
  facet: AttributeFacet;
  query: CategoryQuery;
  onChange: (next: CategoryQuery) => void;
}) {
  if (facet.type === 'number') {
    const hasRange = facet.min != null || facet.max != null;
    const hasSelection =
      attributeRangeKey(facet.key, 'min') in query.attributes ||
      attributeRangeKey(facet.key, 'max') in query.attributes;
    // Không còn sản phẩm nào có thuộc tính này thì ô nhập không có ý nghĩa, trừ khi khách đang lọc theo nó.
    if (!hasRange && !hasSelection) return null;
    return <NumberAttributeGroup facet={facet} query={query} onChange={onChange} />;
  }

  if (facet.type === 'boolean') {
    const hasOptions = (facet.values ?? []).length > 0 || facet.key in query.attributes;
    if (!hasOptions) return null;
    return <BooleanAttributeGroup facet={facet} query={query} onChange={onChange} />;
  }

  const options = buildAttributeOptions(facet, query.attributes[facet.key] ?? []);
  if (options.length === 0) return null;
  return (
    <CheckboxGroup
      legend={attributeLegend(facet)}
      idPrefix={`filter-${facet.key}`}
      options={options}
      selectedValues={query.attributes[facet.key] ?? []}
      onToggle={(value) => onChange(toggleAttributeValue(query, facet.key, value))}
    />
  );
}

// Các nhóm bộ lọc sinh từ `facets` của API. Mỗi nhóm là một `fieldset` gốc nên bàn phím dùng được không cần code riêng.
export function ProductFilters({ facets, query, brandNames, onChange }: ProductFiltersProps) {
  const brandOptions = buildBrandOptions(facets.brands, query.brands, brandNames);
  return (
    <div className="catalog-filter-groups">
      <RangeGroup
        legend="Mức giá (đ)"
        idPrefix="filter-price"
        kind="integer"
        initialMin={query.priceMin}
        initialMax={query.priceMax}
        minPlaceholder="0"
        maxPlaceholder=""
        onApply={(minValue, maxValue) => onChange(setPriceRange(query, minValue, maxValue))}
      />

      <fieldset className="catalog-filter-group">
        <legend>Tình trạng</legend>
        <label htmlFor="filter-in-stock" className="check catalog-option">
          <input
            id="filter-in-stock"
            type="checkbox"
            checked={query.inStock}
            onChange={(event) => onChange(setInStock(query, event.target.checked))}
          />
          <span>Chỉ hiện hàng còn</span>
        </label>
      </fieldset>

      {brandOptions.length > 0 && (
        <CheckboxGroup
          legend="Thương hiệu"
          idPrefix="filter-brand"
          options={brandOptions}
          selectedValues={query.brands}
          onToggle={(slug) => onChange(toggleBrand(query, slug))}
        />
      )}

      {facets.attributes.map((facet) => (
        <AttributeGroup key={facet.key} facet={facet} query={query} onChange={onChange} />
      ))}
    </div>
  );
}
