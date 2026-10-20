// @vitest-environment jsdom
import React, { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
  PermissionGrid,
  type PermissionItem,
} from '../../../../../src/features/identity/components/permission-grid';

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
});

async function renderGrid(permissions: PermissionItem[]) {
  await act(async () =>
    root.render(<PermissionGrid permissions={permissions} selected={[]} onChange={() => {}} />),
  );
}

function groupLabels() {
  return Array.from(container.querySelectorAll('.permission-group-head > span')).map(
    (label) => label.textContent,
  );
}

describe('PermissionGrid', () => {
  it('hiện tên thân thiện cho nhóm quyền còn dùng', async () => {
    await renderGrid([
      { id: 'knowledge:read', group: 'knowledge-base', name: 'Xem tri thức', description: 'Xem' },
    ]);

    expect(groupLabels()).toEqual(['Cơ sở tri thức']);
  });

  it('không còn tên "Cấu hình AI" cho nhóm ai-gateway, nhóm lạ hiện theo mã nhóm', async () => {
    await renderGrid([
      { id: 'legacy:ai', group: 'ai-gateway', name: 'Quyền cũ', description: 'Không còn dùng' },
    ]);

    expect(groupLabels()).toEqual(['ai-gateway']);
    expect(container.textContent).not.toContain('Cấu hình AI');
  });
});
