import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import type { AttributeDefinition } from '../../../../src/shared/catalog/attributes';
import { descriptionSchema } from '../../../../src/shared/catalog/description-schema';
import { validateSpecs } from '../../../../src/shared/catalog/validate-specs';

// Bộ dữ liệu mẫu dùng chung với backend .NET. Hai phía chạy cùng file nên không lệch luật validate.
function readFixture<T>(name: string): T {
  const path = fileURLToPath(
    new URL(`../../../../../../spec/fixtures/catalog/${name}`, import.meta.url),
  );
  return JSON.parse(readFileSync(path, 'utf8')) as T;
}

type SpecFixture = {
  attributes: AttributeDefinition[];
  cases: { name: string; specs: unknown; valid: boolean; errorKeys: string[] }[];
};

type DescriptionFixture = {
  cases: { name: string; valid: boolean; doc: unknown }[];
};

const specFixture = readFixture<SpecFixture>('spec-validation-cases.json');
const descriptionFixture = readFixture<DescriptionFixture>('description-cases.json');

describe('bộ dữ liệu mẫu schema mô tả dùng chung với backend', () => {
  it.each(descriptionFixture.cases)('$name', ({ doc, valid }) => {
    expect(descriptionSchema.safeParse(doc).success).toBe(valid);
  });
});

describe('bộ dữ liệu mẫu validate thông số dùng chung với backend', () => {
  it.each(specFixture.cases)('$name', ({ specs, valid, errorKeys }) => {
    const result = validateSpecs(specs as Record<string, unknown>, specFixture.attributes);

    expect(result.valid).toBe(valid);
    expect(result.errors.map((error) => error.key).sort()).toEqual([...errorKeys].sort());
  });
});
