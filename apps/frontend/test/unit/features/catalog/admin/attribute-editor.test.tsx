// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React, { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  validateAttributeDefinitions,
  type AttributeDefinition,
} from '../../../../../src/shared/catalog/attributes';
import { AttributeEditor } from '../../../../../src/features/catalog/admin/attribute-editor';
import { CategoriesPage } from '../../../../../src/features/catalog/admin/categories-page';

// Giả lập hook kiểm tra quyền để chuyển đổi linh hoạt giữa quyền đọc và quyền ghi.
let mockCanWrite = true;
vi.mock('../../../../../src/features/identity/hooks/use-actor', () => ({
  useCan: () => mockCanWrite,
  useActor: () => ({ data: { kind: 'staff', roles: ['admin'], permissions: [] } }),
}));

let container: HTMLDivElement;
let root: Root;
let queryClient: QueryClient;

beforeEach(() => {
  mockCanWrite = true;
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  });
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
  vi.clearAllMocks();
});

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => {
    setTimeout(resolve, ms);
  });
}

async function waitForCategoryData() {
  for (let i = 0; i < 50; i++) {
    if (!container.textContent?.includes('Đang nạp dữ liệu danh mục...')) {
      return;
    }
    await act(async () => {
      await delay(10);
    });
  }
}

function changeInput(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
  setter?.call(input, value);
  input.dispatchEvent(new Event('input', { bubbles: true }));
  input.dispatchEvent(new Event('change', { bubbles: true }));
}

function changeSelect(select: HTMLSelectElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')?.set;
  setter?.call(select, value);
  select.dispatchEvent(new Event('change', { bubbles: true }));
}

describe('Trình soạn thảo thuộc tính và trang danh mục (UI-C4)', () => {
  it('danh mục con hiện thuộc tính kế thừa ở chế độ chỉ đọc', async () => {
    // Ngăn người dùng sửa nhầm thuộc tính được kế thừa từ danh mục cha.
    const inheritedAttrs: AttributeDefinition[] = [
      {
        key: 'origin',
        label: 'Xuất xứ',
        group: 'Chung',
        type: 'text',
        required: false,
        filterable: true,
      },
    ];

    const ownAttrs: AttributeDefinition[] = [
      {
        key: 'ram_gb',
        label: 'Dung lượng RAM',
        group: 'Cấu hình',
        type: 'number',
        unit: 'GB',
        required: true,
        filterable: true,
      },
    ];

    await act(async () => {
      root.render(
        <StrictMode>
          <AttributeEditor
            attributes={ownAttrs}
            onChange={() => {}}
            inheritedAttributes={inheritedAttrs}
          />
        </StrictMode>,
      );
    });

    // Khối kế thừa hiển thị tiêu đề chỉ đọc rõ ràng.
    expect(container.textContent).toContain('Thuộc tính kế thừa từ danh mục cha (chỉ đọc)');
    expect(container.textContent).toContain('Xuất xứ');
    expect(container.textContent).toContain('origin');

    // Thuộc tính riêng của con có ô nhập liệu để chỉnh sửa.
    const keyInput = container.querySelector('input[value="ram_gb"]') as HTMLInputElement;
    expect(keyInput).not.toBeNull();
    expect(keyInput.disabled).toBe(false);

    // Thuộc tính cha không có ô nhập liệu trong danh sách riêng.
    const originInput = container.querySelector('input[value="origin"]');
    expect(originInput).toBeNull();
  });

  it('thêm thuộc tính enum có options cho danh mục con, body gửi đi đúng', async () => {
    // Đảm bảo dữ liệu enum kèm options được gửi lên máy chủ đầy đủ và chính xác.
    const mockCategories = [
      {
        id: 'cat-root',
        parentId: null,
        name: 'Điện thoại',
        slug: 'dien-thoai',
        position: 1,
        attributes: [],
        createdAt: '2026-10-06T00:00:00Z',
        updatedAt: '2026-10-06T00:00:00Z',
      },
      {
        id: 'cat-child',
        parentId: 'cat-root',
        name: 'Smartphone',
        slug: 'smartphone',
        position: 1,
        attributes: [],
        createdAt: '2026-10-06T00:00:00Z',
        updatedAt: '2026-10-06T00:00:00Z',
      },
    ];

    interface CategoryPayload {
      attributes: AttributeDefinition[];
    }
    let sentBody: CategoryPayload | null = null;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string, init?: RequestInit) => {
        const method = init?.method || 'GET';
        if (url === '/api/admin/categories' && method === 'GET') {
          return {
            ok: true,
            status: 200,
            json: async () => ({ data: mockCategories }),
          } as Response;
        }

        if (url === '/api/admin/categories/cat-child' && method === 'PATCH') {
          sentBody = JSON.parse(init?.body as string) as CategoryPayload;
          return {
            ok: true,
            status: 200,
            json: async () => ({ data: { ...mockCategories[1], ...sentBody } }),
          } as Response;
        }

        return {
          ok: false,
          status: 404,
          json: async () => ({ error: { code: 'NOT_FOUND', message: 'Không tìm thấy' } }),
        } as Response;
      }),
    );

    await act(async () => {
      root.render(
        <StrictMode>
          <QueryClientProvider client={queryClient}>
            <CategoriesPage />
          </QueryClientProvider>
        </StrictMode>,
      );
    });
    await waitForCategoryData();

    // Mở form chỉnh sửa danh mục con Smartphone.
    const editButtons = Array.from(container.querySelectorAll('button')).filter(
      (btn) => btn.textContent?.trim() === 'Sửa',
    );
    expect(editButtons.length).toBeGreaterThan(0);

    await act(async () => {
      editButtons[1]?.click();
    });

    // Bấm nút thêm thuộc tính mới vào danh mục con.
    const addAttrButton = Array.from(container.querySelectorAll('button')).find(
      (btn) => btn.textContent?.trim() === '+ Thêm thuộc tính',
    );
    expect(addAttrButton).toBeDefined();

    await act(async () => {
      addAttrButton?.click();
    });

    // Nhập thông tin thuộc tính kiểu enum có options.
    const keyInput = container.querySelector('input[placeholder="ram_gb"]') as HTMLInputElement;
    const labelInput = container.querySelector(
      'input[placeholder="Dung lượng RAM"]',
    ) as HTMLInputElement;
    const groupInput = container.querySelector('input[placeholder="Cấu hình"]') as HTMLInputElement;
    const typeSelect = container.querySelector('#attr-0-type') as HTMLSelectElement;

    await act(async () => {
      changeInput(keyInput, 'color');
    });
    await act(async () => {
      changeInput(labelInput, 'Màu sắc');
    });
    await act(async () => {
      changeInput(groupInput, 'Thiết kế');
    });
    await act(async () => {
      changeSelect(typeSelect, 'enum');
    });

    const optionsInput = container.querySelector(
      'input[placeholder="8GB, 16GB, 32GB"]',
    ) as HTMLInputElement;
    expect(optionsInput).not.toBeNull();

    await act(async () => {
      changeInput(optionsInput, 'Đỏ, Xanh lá, Vàng');
    });

    // Gửi form lưu thay đổi danh mục con.
    const submitButton = Array.from(container.querySelectorAll('button')).find(
      (btn) => btn.textContent?.trim() === 'Lưu thay đổi',
    );

    await act(async () => {
      submitButton?.click();
    });

    // Kiểm tra payload gửi đi có đúng mảng options đã cắt khoảng trắng.
    const currentPayload = sentBody as CategoryPayload | null;
    expect(currentPayload).not.toBeNull();
    const attrs = currentPayload?.attributes || [];
    expect(attrs[0]?.key).toBe('color');
    expect(attrs[0]?.label).toBe('Màu sắc');
    expect(attrs[0]?.type).toBe('enum');
    expect(attrs[0]?.options).toEqual(['Đỏ', 'Xanh lá', 'Vàng']);
  });

  it('enum không có options bị chặn trước khi gửi, lỗi gắn với ô bằng aria-describedby', async () => {
    // Chặn gửi yêu cầu lên server khi định nghĩa enum thiếu danh sách tùy chọn.
    const mockCategories = [
      {
        id: 'cat-root',
        parentId: null,
        name: 'Điện thoại',
        slug: 'dien-thoai',
        position: 1,
        attributes: [],
        createdAt: '2026-10-06T00:00:00Z',
        updatedAt: '2026-10-06T00:00:00Z',
      },
    ];

    let patchCalled = false;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string, init?: RequestInit) => {
        const method = init?.method || 'GET';
        if (url === '/api/admin/categories' && method === 'GET') {
          return {
            ok: true,
            status: 200,
            json: async () => ({ data: mockCategories }),
          } as Response;
        }

        if (method === 'PATCH') {
          patchCalled = true;
          return { ok: true, status: 200, json: async () => ({ data: {} }) } as Response;
        }

        return { ok: false, status: 500 } as Response;
      }),
    );

    await act(async () => {
      root.render(
        <StrictMode>
          <QueryClientProvider client={queryClient}>
            <CategoriesPage />
          </QueryClientProvider>
        </StrictMode>,
      );
    });

    await waitForCategoryData();
    // Mở hộp thoại sửa danh mục gốc.
    const editButton = Array.from(container.querySelectorAll('button')).find(
      (btn) => btn.textContent?.trim() === 'Sửa',
    );
    await act(async () => {
      editButton?.click();
    });

    // Thêm dòng thuộc tính và chọn kiểu enum nhưng để trống options.
    const addAttrButton = Array.from(container.querySelectorAll('button')).find(
      (btn) => btn.textContent?.trim() === '+ Thêm thuộc tính',
    );
    await act(async () => {
      addAttrButton?.click();
    });

    const keyInput = container.querySelector('input[placeholder="ram_gb"]') as HTMLInputElement;
    const labelInput = container.querySelector(
      'input[placeholder="Dung lượng RAM"]',
    ) as HTMLInputElement;
    const groupInput = container.querySelector('input[placeholder="Cấu hình"]') as HTMLInputElement;
    const typeSelect = container.querySelector('#attr-0-type') as HTMLSelectElement;

    await act(async () => {
      changeInput(keyInput, 'storage_tier');
    });
    await act(async () => {
      changeInput(labelInput, 'Phân khúc bộ nhớ');
    });
    await act(async () => {
      changeInput(groupInput, 'Cấu hình');
    });
    await act(async () => {
      changeSelect(typeSelect, 'enum');
    });

    // Bấm lưu khi chưa nhập options.
    const submitButton = Array.from(container.querySelectorAll('button')).find(
      (btn) => btn.textContent?.trim() === 'Lưu thay đổi',
    );
    await act(async () => {
      submitButton?.click();
    });

    // Yêu cầu mạng không được gửi đi.
    expect(patchCalled).toBe(false);

    // Lỗi hiển thị gắn trực tiếp với ô options bằng thuộc tính aria-describedby.
    const optionsInput = container.querySelector(
      'input[placeholder="8GB, 16GB, 32GB"]',
    ) as HTMLInputElement;
    const describedById = optionsInput.getAttribute('aria-describedby');
    expect(describedById).toBeTruthy();

    const errorElement = container.querySelector(`#${describedById}`);
    expect(errorElement).not.toBeNull();
    expect(errorElement?.getAttribute('role')).toBe('alert');
    expect(errorElement?.textContent).toContain('kiểu enum bắt buộc phải có danh sách options');
  });

  it('API trả 409 ATTRIBUTE_IN_USE: hiện role="alert" tiếng Việt, dữ liệu đang soạn còn nguyên', async () => {
    // Bảo toàn dữ liệu người dùng vừa chỉnh sửa khi server từ chối vì xung đột thuộc tính.
    const mockCategories = [
      {
        id: 'cat-root',
        parentId: null,
        name: 'Máy tính bảng',
        slug: 'may-tinh-bang',
        position: 1,
        attributes: [
          {
            key: 'screen_type',
            label: 'Công nghệ màn hình',
            group: 'Màn hình',
            type: 'text',
            required: false,
            filterable: false,
          },
        ],
        createdAt: '2026-10-06T00:00:00Z',
        updatedAt: '2026-10-06T00:00:00Z',
      },
    ];

    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string, init?: RequestInit) => {
        const method = init?.method || 'GET';
        if (url === '/api/admin/categories' && method === 'GET') {
          return {
            ok: true,
            status: 200,
            json: async () => ({ data: mockCategories }),
          } as Response;
        }

        if (url === '/api/admin/categories/cat-root' && method === 'PATCH') {
          return {
            ok: false,
            status: 409,
            json: async () => ({
              error: {
                code: 'ATTRIBUTE_IN_USE',
                message: 'Thuộc tính đang được sản phẩm sử dụng.',
              },
            }),
          } as Response;
        }

        return { ok: false, status: 500 } as Response;
      }),
    );

    await act(async () => {
      root.render(
        <StrictMode>
          <QueryClientProvider client={queryClient}>
            <CategoriesPage />
          </QueryClientProvider>
        </StrictMode>,
      );
    });

    await waitForCategoryData();
    const editButton = Array.from(container.querySelectorAll('button')).find(
      (btn) => btn.textContent?.trim() === 'Sửa',
    );
    await act(async () => {
      editButton?.click();
    });

    // Thay đổi tên danh mục và xóa thuộc tính đang dùng.
    const nameInput = container.querySelector('#category-name') as HTMLInputElement;
    await act(async () => {
      changeInput(nameInput, 'Máy tính bảng Pro 2026');
    });

    const deleteAttrButton = Array.from(container.querySelectorAll('button')).find(
      (btn) => btn.getAttribute('aria-label') === 'Xóa thuộc tính Công nghệ màn hình',
    );
    await act(async () => {
      deleteAttrButton?.click();
    });

    // Bấm lưu và nhận lỗi 409 từ API.
    const submitButton = Array.from(container.querySelectorAll('button')).find(
      (btn) => btn.textContent?.trim() === 'Lưu thay đổi',
    );
    await act(async () => {
      submitButton?.click();
    });

    // Vùng role alert hiển thị đúng câu thông báo giải thích tiếng Việt.
    const alerts = Array.from(container.querySelectorAll('[role="alert"]'));
    const attributeAlert = alerts.find((alert) =>
      alert.textContent?.includes(
        'Thuộc tính đang được sản phẩm sử dụng nên không thể xóa hoặc đổi kiểu dữ liệu.',
      ),
    );
    expect(attributeAlert).toBeDefined();

    // Dữ liệu người dùng đang nhập vào ô tên được giữ nguyên vẹn.
    expect(nameInput.value).toBe('Máy tính bảng Pro 2026');
  });

  it('không có products:write: không có nút tạo, sửa, xóa', async () => {
    // Ẩn hoàn toàn các nút thao tác ghi khi người dùng chỉ có quyền xem.
    mockCanWrite = false;

    const mockCategories = [
      {
        id: 'cat-root',
        parentId: null,
        name: 'Gia dụng',
        slug: 'gia-dung',
        position: 1,
        attributes: [],
        createdAt: '2026-10-06T00:00:00Z',
        updatedAt: '2026-10-06T00:00:00Z',
      },
      {
        id: 'cat-child',
        parentId: 'cat-root',
        name: 'Nồi cơm điện',
        slug: 'noi-com-dien',
        position: 1,
        attributes: [],
        createdAt: '2026-10-06T00:00:00Z',
        updatedAt: '2026-10-06T00:00:00Z',
      },
    ];

    vi.stubGlobal(
      'fetch',
      vi.fn(async () => ({
        ok: true,
        status: 200,
        json: async () => ({ data: mockCategories }),
      })),
    );

    await act(async () => {
      root.render(
        <StrictMode>
          <QueryClientProvider client={queryClient}>
            <CategoriesPage />
          </QueryClientProvider>
        </StrictMode>,
      );
    });

    await waitForCategoryData();
    // Danh sách vẫn hiển thị tên danh mục cho người dùng xem.
    expect(container.textContent).toContain('Gia dụng');
    expect(container.textContent).toContain('Nồi cơm điện');

    // Không tồn tại bất kỳ nút thao tác ghi nào trên giao diện.
    const allButtons = Array.from(container.querySelectorAll('button')).map((btn) =>
      btn.textContent?.trim(),
    );
    expect(allButtons).not.toContain('+ Thêm danh mục gốc');
    expect(allButtons).not.toContain('+ Thêm con');
    expect(allButtons).not.toContain('Sửa');
    expect(allButtons).not.toContain('Xóa');
  });

  it('gõ từng ký tự vào ô options không làm nuốt dấu phẩy', async () => {
    // Đảm bảo thao tác gõ bàn phím thực tế lưu đúng chuỗi thô kèm dấu phẩy.
    let currentAttrs: AttributeDefinition[] = [
      {
        key: 'color',
        label: 'Màu',
        group: 'Chung',
        type: 'enum',
        options: [],
        required: false,
        filterable: true,
      },
    ];

    function Wrapper() {
      const [attrs, setAttrs] = React.useState(currentAttrs);
      return (
        <AttributeEditor
          attributes={attrs}
          onChange={(next) => {
            currentAttrs = next;
            setAttrs(next);
          }}
        />
      );
    }

    await act(async () => {
      root.render(
        <StrictMode>
          <Wrapper />
        </StrictMode>,
      );
    });

    const optionsInput = container.querySelector('#attr-0-options') as HTMLInputElement;
    expect(optionsInput).not.toBeNull();

    // Giả lập gõ từng ký tự "Đ", "ỏ", ",", " ", "X", "a", "n", "h"
    const textToType = 'Đỏ, Xanh';
    for (let i = 1; i <= textToType.length; i++) {
      const partial = textToType.slice(0, i);
      await act(async () => {
        changeInput(optionsInput, partial);
      });
      // Ô nhập luôn giữ nguyên chuỗi người dùng đã gõ, không bị nuốt dấu phẩy.
      expect(optionsInput.value).toBe(partial);
    }

    // Kết quả mảng options cuối cùng được phân tách chính xác.
    expect(currentAttrs[0]?.options).toEqual(['Đỏ', 'Xanh']);
  });

  it('hai dòng cùng để trống key: lỗi Nhãn chỉ hiện ở dòng sai Nhãn', async () => {
    // Hai dòng cùng key rỗng. Khớp lỗi theo key sẽ gắn lỗi Nhãn của dòng 0 sang dòng 1.
    const twoEmptyKeyAttrs: AttributeDefinition[] = [
      {
        key: '',
        label: '',
        group: 'Nhóm 1',
        type: 'text',
        required: false,
        filterable: false,
      },
      {
        key: '',
        label: 'Tên hợp lệ',
        group: 'Nhóm 2',
        type: 'text',
        required: false,
        filterable: false,
      },
    ];

    const validation = validateAttributeDefinitions(twoEmptyKeyAttrs);
    expect(validation.valid).toBe(false);

    await act(async () => {
      root.render(
        <StrictMode>
          <AttributeEditor
            attributes={twoEmptyKeyAttrs}
            onChange={() => {}}
            errors={validation.errors}
          />
        </StrictMode>,
      );
    });

    // Dòng 0 sai Nhãn nên ô Nhãn trỏ tới thông báo lỗi của chính dòng đó.
    const label0 = container.querySelector('#attr-0-label') as HTMLInputElement;
    expect(label0.getAttribute('aria-describedby')).toBe('attr-error-0-label');
    expect(container.querySelector('#attr-error-0-label')?.textContent).toContain(
      'Nhãn thuộc tính không được để trống.',
    );

    // Dòng 1 có Nhãn hợp lệ nên ô Nhãn không có lỗi nào.
    const label1 = container.querySelector('#attr-1-label') as HTMLInputElement;
    expect(label1.hasAttribute('aria-describedby')).toBe(false);
    expect(container.querySelector('#attr-error-1-label')).toBeNull();
  });

  it('option "Nhãn dán" bị lặp: lỗi hiện dưới ô options, không hiện dưới ô Nhãn', async () => {
    // Thông báo lỗi chứa chữ "Nhãn". Dò chữ trong message sẽ gắn nhầm lỗi vào ô Nhãn.
    const stickerAttrs: AttributeDefinition[] = [
      {
        key: 'accessory',
        label: 'Phụ kiện',
        group: 'Hộp',
        type: 'enum',
        options: ['Nhãn dán', 'Nhãn dán'],
        required: false,
        filterable: false,
      },
    ];

    const validation = validateAttributeDefinitions(stickerAttrs);
    expect(validation.valid).toBe(false);

    await act(async () => {
      root.render(
        <StrictMode>
          <AttributeEditor
            attributes={stickerAttrs}
            onChange={() => {}}
            errors={validation.errors}
          />
        </StrictMode>,
      );
    });

    const optionsInput = container.querySelector('#attr-0-options') as HTMLInputElement;
    expect(optionsInput.getAttribute('aria-describedby')).toBe('attr-error-0-options');
    expect(container.querySelector('#attr-error-0-options')?.textContent).toContain(
      'Tùy chọn "Nhãn dán" của thuộc tính "accessory" bị lặp lại.',
    );

    const labelInput = container.querySelector('#attr-0-label') as HTMLInputElement;
    expect(labelInput.hasAttribute('aria-describedby')).toBe(false);
    expect(container.querySelector('#attr-error-0-label')).toBeNull();
  });

  it('chế độ chỉ đọc khóa ô options', async () => {
    // Ô options không khóa thì người xem vẫn gõ được và chuỗi thô vẫn đổi.
    const enumAttrs: AttributeDefinition[] = [
      {
        key: 'color',
        label: 'Màu',
        group: 'Chung',
        type: 'enum',
        options: ['Đỏ'],
        required: false,
        filterable: false,
      },
    ];

    await act(async () => {
      root.render(
        <StrictMode>
          <AttributeEditor attributes={enumAttrs} onChange={() => {}} readOnly />
        </StrictMode>,
      );
    });

    const optionsInput = container.querySelector('#attr-0-options') as HTMLInputElement;
    expect(optionsInput.disabled).toBe(true);
  });
});
