import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { stylesDirectory } from './design-tokens';

// DS-02 đến DS-05 dùng các lớp này thay cho `style={{`. Thiếu một lớp thì màn mới mất kiểu.
const requiredSelectors = [
  '.btn',
  '.btn-primary',
  '.btn-outline',
  '.btn-ghost',
  '.btn-danger',
  '.btn-sm',
  '.btn-block',
  '.field',
  '.input-sm',
  '.form-grid',
  '.form-grid-2',
  '.form-grid-3',
  '.field-full',
  '.form-error',
  '.hint',
  '.badge',
  '.badge-ok',
  '.badge-warn',
  '.badge-bad',
  '.badge-info',
  '.card',
  '.card-head',
  '.card-flush',
  '.table-wrap',
  '.tbl',
  '.sr',
  '.muted',
  '.small',
  '.mono',
  '.icon',
  '.page',
  '.page-head',
  '.page-head-text',
  '.head-actions',
  '.row-actions',
  '.stack',
  '.stack-sm',
  '.alert',
  '.alert-list',
  '.empty-state',
  '.cell-sub',
  '.wrap-anywhere',
  '.check',
  '.filters-row',
  '.pager',
  '.icon-btn',
  '.dialog-backdrop',
  '.dialog',
  '.dialog-lg',
  '.dialog-head',
  '.dialog-body',
  '.dialog-foot',
  '.logo-mark',
  '.forbidden-title',
  '.role-picker',
  '.link-btn',
  '.alert-ok',
  '.alert-warn',
  '.kpis',
  '.kpi',
  '.kpi-value',
  '.kpi-ok',
  '.kpi-bad',
];

const componentsCss = readFileSync(`${stylesDirectory}components.css`, 'utf8');

describe('components.css', () => {
  it.each(requiredSelectors)('khai báo lớp %s', (selector) => {
    // Tên lớp phải đứng riêng, không khớp nhầm phần đầu của lớp dài hơn như `.btn-primary`.
    const escapedSelector = selector.replace('.', '\\.');
    expect(componentsCss).toMatch(new RegExp(`${escapedSelector}(?![a-z0-9-])[^{]*\\{`));
  });

  it('nút bị khóa có kiểu riêng để người dùng biết không bấm được', () => {
    expect(componentsCss).toMatch(/\.btn(:disabled|\[disabled\])/);
  });

  it('ô nhập có `aria-invalid` hiện viền màu lỗi', () => {
    expect(componentsCss).toMatch(/\[aria-invalid='true'\][^{]*\{[^}]*--color-danger/);
  });
});
