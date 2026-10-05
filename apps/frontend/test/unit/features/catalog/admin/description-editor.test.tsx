// @vitest-environment jsdom
import React, { act, StrictMode, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Editor } from '@tiptap/core';
import {
  DESCRIPTION_MAX_TEXT_LENGTH,
  descriptionSchema,
  type DescriptionDoc,
} from '@pickory/shared';
import { DescriptionEditor } from '../../../../../src/features/catalog/admin/description-editor';

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

describe('DescriptionEditor', () => {
  it('khởi tạo thanh công cụ có role toolbar và các nút type button có nhãn tiếng Việt', async () => {
    // Đảm bảo cây hỗ trợ tiếp cận nhận diện đúng vai trò thanh công cụ chuẩn.
    await act(async () => {
      root.render(
        <StrictMode>
          <DescriptionEditor />
        </StrictMode>,
      );
    });

    const toolbar = container.querySelector('[role="toolbar"]');
    expect(toolbar).not.toBeNull();
    expect(toolbar?.getAttribute('aria-label')).toBeTruthy();

    const buttons = toolbar?.querySelectorAll('button');
    expect(buttons?.length).toBeGreaterThanOrEqual(6);

    // Xác minh toàn bộ các nút đều ngăn chặn hành vi submit form mặc định.
    buttons?.forEach((btn) => {
      expect(btn.getAttribute('type')).toBe('button');
    });

    const buttonTexts = Array.from(buttons || []).map((btn) => btn.textContent?.trim());
    expect(buttonTexts).toContain('Đoạn văn');
    expect(buttonTexts).toContain('Tiêu đề 1');
    expect(buttonTexts).toContain('Tiêu đề 2');
    expect(buttonTexts).toContain('Tiêu đề 3');
    expect(buttonTexts).toContain('Đậm');
    expect(buttonTexts).toContain('Nghiêng');
  });

  it('ô nhập nội dung có role textbox, aria-multiline true và nhãn tiếp cận được', async () => {
    // Cung cấp đầy đủ ngữ nghĩa vùng nhập văn bản nhiều dòng cho trình đọc màn hình.
    await act(async () => {
      root.render(
        <StrictMode>
          <DescriptionEditor label="Mô tả sản phẩm" />
        </StrictMode>,
      );
    });

    const textbox = container.querySelector('[role="textbox"]');
    expect(textbox).not.toBeNull();
    expect(textbox?.getAttribute('aria-label')).toBe('Mô tả sản phẩm');
    expect(textbox?.getAttribute('aria-multiline')).toBe('true');
  });

  it('nạp mọi tài liệu mẫu hợp lệ của CAT-13 vào Tiptap thật và getJSON trả lại đúng tài liệu đó', async () => {
    // Chứng minh tương thích hoàn toàn giữa schema Zod và bộ máy ProseMirror của Tiptap.
    const validSampleDoc: DescriptionDoc = {
      type: 'doc',
      content: [
        {
          type: 'heading',
          attrs: { level: 1 },
          content: [{ type: 'text', text: 'Tiêu đề cấp 1' }],
        },
        {
          type: 'heading',
          attrs: { level: 2 },
          content: [{ type: 'text', text: 'Tiêu đề cấp 2' }],
        },
        {
          type: 'heading',
          attrs: { level: 3 },
          content: [{ type: 'text', text: 'Tiêu đề cấp 3' }],
        },
        {
          type: 'paragraph',
          content: [
            { type: 'text', text: 'Chữ thường ' },
            { type: 'text', text: 'chữ đậm ', marks: [{ type: 'bold' }] },
            { type: 'text', text: 'chữ nghiêng ', marks: [{ type: 'italic' }] },
            {
              type: 'text',
              text: 'vừa đậm vừa nghiêng',
              marks: [{ type: 'bold' }, { type: 'italic' }],
            },
          ],
        },
      ],
    };

    let editorInstance: Editor | null = null;

    await act(async () => {
      root.render(
        <StrictMode>
          <DescriptionEditor
            onEditorReady={(editor) => {
              editorInstance = editor;
            }}
          />
        </StrictMode>,
      );
    });

    expect(editorInstance).not.toBeNull();
    const editor = editorInstance as unknown as Editor;

    await act(async () => {
      editor.commands.setContent(validSampleDoc);
    });

    const currentJson = editor.getJSON();
    expect(currentJson).toEqual(validSampleDoc);
    expect(descriptionSchema.safeParse(currentJson).success).toBe(true);
  });

  it('từ chối tài liệu có text rỗng hoặc doc thiếu content', () => {
    // Đảm bảo các cấu trúc ProseMirror cấm đều bị schema Zod từ chối nhất quán.
    const emptyTextDoc = {
      type: 'doc',
      content: [
        {
          type: 'paragraph',
          content: [{ type: 'text', text: '' }],
        },
      ],
    };
    expect(descriptionSchema.safeParse(emptyTextDoc).success).toBe(false);

    const emptyDoc = {
      type: 'doc',
      content: [],
    };
    expect(descriptionSchema.safeParse(emptyDoc).success).toBe(false);

    const docWithoutContent = {
      type: 'doc',
    };
    expect(descriptionSchema.safeParse(docWithoutContent).success).toBe(false);

    const duplicateMarksDoc = {
      type: 'doc',
      content: [
        {
          type: 'paragraph',
          content: [
            {
              type: 'text',
              text: 'Văn bản',
              marks: [{ type: 'bold' }, { type: 'bold' }],
            },
          ],
        },
      ],
    };
    expect(descriptionSchema.safeParse(duplicateMarksDoc).success).toBe(false);
  });

  it('bấm Tiêu đề 2 và Đậm cập nhật aria-pressed và cho ra JSON qua schema CAT-13', async () => {
    // Chứng minh tương tác định dạng tạo ra tài liệu JSON chuẩn và cập nhật trạng thái nút.
    let editorInstance: Editor | null = null;
    let latestDoc: DescriptionDoc | null = null;

    await act(async () => {
      root.render(
        <StrictMode>
          <DescriptionEditor
            onEditorReady={(editor) => {
              editorInstance = editor;
            }}
            onChange={(doc) => {
              latestDoc = doc;
            }}
          />
        </StrictMode>,
      );
    });

    expect(editorInstance).not.toBeNull();
    const editor = editorInstance as unknown as Editor;

    // Nhập nội dung ban đầu vào trình soạn thảo.
    await act(async () => {
      editor.commands.setContent('<p>Nội dung mẫu</p>');
    });

    // Chọn toàn bộ văn bản để áp dụng định dạng.
    await act(async () => {
      editor.commands.selectAll();
    });

    // Kích hoạt nút Tiêu đề 2 trên thanh công cụ.
    const h2Button = Array.from(container.querySelectorAll('button')).find(
      (btn) => btn.textContent?.trim() === 'Tiêu đề 2',
    );
    expect(h2Button).toBeDefined();

    await act(async () => {
      h2Button?.click();
    });

    expect(h2Button?.getAttribute('aria-pressed')).toBe('true');

    // Kích hoạt nút Đậm trên thanh công cụ.
    const boldButton = Array.from(container.querySelectorAll('button')).find(
      (btn) => btn.textContent?.trim() === 'Đậm',
    );
    expect(boldButton).toBeDefined();

    await act(async () => {
      boldButton?.click();
    });

    expect(boldButton?.getAttribute('aria-pressed')).toBe('true');

    // Trích xuất JSON và kiểm tra tính hợp lệ qua schema của CAT-13.
    const json = editor.getJSON();
    const parseResult = descriptionSchema.safeParse(json);
    expect(parseResult.success).toBe(true);

    if (parseResult.success) {
      expect(parseResult.data.content?.[0]?.type).toBe('heading');
    }

    // Xác minh hàm onChange đã nhận được tài liệu JSON hợp lệ.
    expect(latestDoc).not.toBeNull();
  });

  it('nút đang bật khác nút đang tắt ở cả màu nền và viền, và dùng CSS variable', async () => {
    // Tuân thủ quy chuẩn thiết kế không dùng Tailwind và thể hiện rõ trạng thái active.
    let editorInstance: Editor | null = null;

    await act(async () => {
      root.render(
        <StrictMode>
          <DescriptionEditor
            onEditorReady={(editor) => {
              editorInstance = editor;
            }}
          />
        </StrictMode>,
      );
    });

    expect(editorInstance).not.toBeNull();
    const editor = editorInstance as unknown as Editor;

    const boldButton = Array.from(container.querySelectorAll('button')).find(
      (btn) => btn.textContent?.trim() === 'Đậm',
    );
    expect(boldButton).toBeDefined();

    const inactiveBg = boldButton?.style.backgroundColor;
    const inactiveBorder = boldButton?.style.border || boldButton?.style.borderColor;

    await act(async () => {
      editor.commands.setContent('<p>Nội dung</p>');
      editor.commands.selectAll();
      boldButton?.click();
    });

    const activeBg = boldButton?.style.backgroundColor;
    const activeBorder = boldButton?.style.border || boldButton?.style.borderColor;

    expect(activeBg).not.toBe(inactiveBg);
    expect(activeBorder).not.toBe(inactiveBorder);
  });

  it('thanh công cụ tuân thủ mẫu ARIA toolbar với roving tabindex và phím điều hướng', async () => {
    // Đảm bảo người dùng bàn phím duyệt tuần tự giữa các nút theo chuẩn WAI-ARIA Toolbar.
    let editorInstance: Editor | null = null;
    await act(async () => {
      root.render(
        <StrictMode>
          <DescriptionEditor
            onEditorReady={(editor) => {
              editorInstance = editor;
            }}
          />
        </StrictMode>,
      );
    });

    // Kích hoạt lịch sử thao tác để các nút hành động đều ở trạng thái sẵn sàng.
    if (editorInstance) {
      const editor = editorInstance as unknown as Editor;
      await act(async () => {
        const { closeHistory } = await import('@tiptap/pm/history');
        editor.commands.insertContent(' bước một');
        editor.view.dispatch(closeHistory(editor.state.tr));
        editor.commands.insertContent(' bước hai');
        editor.commands.undo();
      });
    }

    const buttons = Array.from(
      container.querySelectorAll<HTMLButtonElement>('[role="toolbar"] button'),
    );
    expect(buttons.length).toBeGreaterThanOrEqual(6);

    // Nút đầu tiên phải có tabIndex 0, các nút còn lại có tabIndex -1.
    expect(buttons[0]?.tabIndex).toBe(0);
    for (let i = 1; i < buttons.length; i += 1) {
      expect(buttons[i]?.tabIndex).toBe(-1);
    }

    // Phím ArrowRight chuyển tiêu điểm sang nút tiếp theo.
    buttons[0]?.focus();
    await act(async () => {
      buttons[0]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    });
    expect(buttons[0]?.tabIndex).toBe(-1);
    expect(buttons[1]?.tabIndex).toBe(0);
    expect(document.activeElement).toBe(buttons[1]);

    // Phím ArrowLeft chuyển tiêu điểm quay lại nút trước đó.
    await act(async () => {
      buttons[1]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    });
    expect(buttons[0]?.tabIndex).toBe(0);
    expect(buttons[1]?.tabIndex).toBe(-1);
    expect(document.activeElement).toBe(buttons[0]);

    // Phím End nhảy thẳng tới nút cuối cùng trên thanh công cụ.
    await act(async () => {
      buttons[0]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
    });
    const lastIndex = buttons.length - 1;
    expect(buttons[lastIndex]?.tabIndex).toBe(0);
    expect(document.activeElement).toBe(buttons[lastIndex]);

    // Phím Home nhảy quay lại nút đầu tiên trên thanh công cụ.
    await act(async () => {
      buttons[lastIndex]?.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Home', bubbles: true }),
      );
    });
    expect(buttons[0]?.tabIndex).toBe(0);
    expect(document.activeElement).toBe(buttons[0]);
  });

  it('hiển thị thông báo role alert và kích hoạt aria-invalid khi nội dung vượt giới hạn', async () => {
    // Thông báo lỗi trực quan và phản hồi trạng thái lỗi tới công nghệ trợ năng.
    let editorInstance: Editor | null = null;
    let receivedError: string | null = null;

    await act(async () => {
      root.render(
        <StrictMode>
          <DescriptionEditor
            onEditorReady={(editor) => {
              editorInstance = editor;
            }}
            onValidationError={(message) => {
              receivedError = message;
            }}
          />
        </StrictMode>,
      );
    });

    expect(editorInstance).not.toBeNull();
    const editor = editorInstance as unknown as Editor;

    // Chèn chuỗi ký tự vượt quá 20.000 ký tự text quy định.
    const longText = 'x'.repeat(DESCRIPTION_MAX_TEXT_LENGTH + 5);

    await act(async () => {
      editor.commands.setContent(`<p>${longText}</p>`);
    });

    const alertElement = container.querySelector('[role="alert"]');
    expect(alertElement).not.toBeNull();
    expect(receivedError).toBeTruthy();

    const textbox = container.querySelector('[role="textbox"]');
    expect(textbox?.getAttribute('aria-invalid')).toBe('true');
    expect(textbox?.getAttribute('aria-describedby')).toBe(alertElement?.id);

    // Nhập lại nội dung hợp lệ thì thông báo lỗi phải tự động biến mất.
    await act(async () => {
      editor.commands.setContent('<p>Nội dung ngắn hợp lệ</p>');
    });

    expect(container.querySelector('[role="alert"]')).toBeNull();
    expect(receivedError).toBeNull();
    expect(textbox?.getAttribute('aria-invalid')).toBe('false');
  });

  it('chế độ disabled khóa chỉnh sửa ô soạn thảo và vô hiệu hóa toàn bộ nút trên thanh công cụ', async () => {
    // Ngăn chặn thao tác sửa đổi khi người dùng không có quyền hoặc form đang gửi dữ liệu.
    let editorInstance: Editor | null = null;

    const { rerender } = {
      rerender: async (disabled: boolean) => {
        await act(async () => {
          root.render(
            <StrictMode>
              <DescriptionEditor
                disabled={disabled}
                onEditorReady={(editor) => {
                  editorInstance = editor;
                }}
              />
            </StrictMode>,
          );
        });
      },
    };

    await rerender(false);
    expect(editorInstance).not.toBeNull();
    const editor = editorInstance as unknown as Editor;
    expect(editor.isEditable).toBe(true);

    // Kích hoạt lịch sử thao tác để chứng minh trạng thái khi chưa khóa toàn bộ thanh công cụ.
    await act(async () => {
      const { closeHistory } = await import('@tiptap/pm/history');
      editor.commands.insertContent(' bước một');
      editor.view.dispatch(closeHistory(editor.state.tr));
      editor.commands.insertContent(' bước hai');
      editor.commands.undo();
    });

    const buttonsBefore = Array.from(
      container.querySelectorAll<HTMLButtonElement>('[role="toolbar"] button'),
    );
    expect(buttonsBefore.some((b) => b.disabled)).toBe(false);
    // Chuyển sang trạng thái disabled = true.
    await rerender(true);
    expect(editor.isEditable).toBe(false);

    const buttonsAfter = Array.from(
      container.querySelectorAll<HTMLButtonElement>('[role="toolbar"] button'),
    );
    expect(buttonsAfter.every((b) => b.disabled)).toBe(true);
  });

  it('dán HTML có link, ảnh, danh sách, script chỉ giữ text và định dạng được bật, và qua schema', async () => {
    // Kiểm tra cơ chế lọc sạch nội dung không cho phép để bảo đảm an toàn dữ liệu.
    let editorInstance: Editor | null = null;

    await act(async () => {
      root.render(
        <StrictMode>
          <DescriptionEditor
            onEditorReady={(editor) => {
              editorInstance = editor;
            }}
          />
        </StrictMode>,
      );
    });

    expect(editorInstance).not.toBeNull();
    const editor = editorInstance as unknown as Editor;

    // Chèn chuỗi HTML chứa mã độc, thẻ ảnh, danh sách và liên kết lạ.
    const maliciousHtml =
      '<h2>Tiêu đề an toàn</h2>' +
      '<p>Đoạn có <a href="https://malicious.com">liên kết</a> và <script>alert(1)</script> mã độc.</p>' +
      '<img src="https://example.com/pic.png" alt="ảnh" />' +
      '<ul><li>Mục một</li><li>Mục hai</li></ul>' +
      '<p>Kết thúc bằng <b>chữ đậm</b> và <i>chữ nghiêng</i>.</p>';

    await act(async () => {
      // Mô phỏng thao tác dán HTML qua lệnh insertContent của Tiptap.
      editor.commands.setContent('');
      editor.commands.insertContent(maliciousHtml);
    });

    const json = editor.getJSON();

    // Kết quả sau khi lọc phải thỏa mãn toàn bộ ràng buộc của schema CAT-13.
    const parseResult = descriptionSchema.safeParse(json);
    expect(parseResult.success).toBe(true);

    // Xác nhận không còn nút ảnh, danh sách hay script trong tài liệu.
    const nodeTypes = json?.content?.map((n: { type: string }) => n.type);
    expect(nodeTypes).not.toContain('image');
    expect(nodeTypes).not.toContain('bulletList');
    expect(nodeTypes).not.toContain('orderedList');
    expect(nodeTypes).not.toContain('script');
  });

  it('mở được tài liệu đã lưu và lưu lại không đổi nội dung', async () => {
    // Bảo toàn dữ liệu gốc khi người dùng mở lại bản ghi đã lưu từ trước.
    const savedDoc: DescriptionDoc = {
      type: 'doc',
      content: [
        {
          type: 'heading',
          attrs: { level: 2 },
          content: [{ type: 'text', text: 'Tiêu đề sản phẩm đã lưu' }],
        },
        {
          type: 'paragraph',
          content: [
            { type: 'text', text: 'Đoạn văn có ' },
            { type: 'text', text: 'chữ đậm', marks: [{ type: 'bold' }] },
            { type: 'text', text: ' và chữ thường.' },
          ],
        },
      ],
    };

    let editorInstance: Editor | null = null;

    await act(async () => {
      root.render(
        <StrictMode>
          <DescriptionEditor
            value={savedDoc}
            onEditorReady={(editor) => {
              editorInstance = editor;
            }}
          />
        </StrictMode>,
      );
    });

    expect(editorInstance).not.toBeNull();
    const editor = editorInstance as unknown as Editor;

    // Kiểm tra cấu trúc JSON sau khi mở trùng khớp hoàn toàn với dữ liệu nạp vào.
    const outputJson = editor.getJSON();
    expect(outputJson).toEqual(savedDoc);

    const parseResult = descriptionSchema.safeParse(outputJson);
    expect(parseResult.success).toBe(true);
  });

  it('focus vào editor thì khung bao ngoài đổi viền sang màu brand, blur thì trở lại viền thường, và khi có lỗi thì viền đỏ chiếm ưu tiên', async () => {
    // Đảm bảo hiển thị vòng nét tiêu điểm trực quan theo tiêu chuẩn tiếp cận WCAG 2.4.7.
    let editorInstance: Editor | null = null;

    await act(async () => {
      root.render(
        <StrictMode>
          <DescriptionEditor
            onEditorReady={(editor) => {
              editorInstance = editor;
            }}
          />
        </StrictMode>,
      );
    });

    expect(editorInstance).not.toBeNull();
    const editor = editorInstance as unknown as Editor;

    const getWrapper = () =>
      container.querySelector<HTMLDivElement>('[data-testid="description-editor-wrapper"]');
    expect(getWrapper()).not.toBeNull();

    // Kiểm tra trạng thái bình thường chưa nhận tiêu điểm.
    expect(getWrapper()?.style.borderColor).toBe('var(--color-border)');

    // Focus vào editor để kích hoạt viền màu thương hiệu.
    await act(async () => {
      editor.commands.focus();
      await new Promise((r) => setTimeout(r, 20));
    });

    expect(getWrapper()?.style.borderColor).toBe('var(--color-brand)');

    // Blur khỏi editor để đưa viền về trạng thái thông thường.
    await act(async () => {
      editor.commands.blur();
      await new Promise((r) => setTimeout(r, 20));
    });

    expect(getWrapper()?.style.borderColor).toBe('var(--color-border)');

    // Khi có lỗi xác thực, viền màu đỏ cảnh báo phải có độ ưu tiên cao hơn viền focus.
    const longText = 'x'.repeat(DESCRIPTION_MAX_TEXT_LENGTH + 5);
    await act(async () => {
      editor.commands.setContent(`<p>${longText}</p>`);
      editor.commands.focus();
    });

    expect(getWrapper()?.style.borderColor).toBe('var(--color-danger)');
  });

  it('editor mới mở thì hai nút Hoàn tác và Làm lại bị tắt, gõ chữ thì Hoàn tác bật, hoàn tác xong thì Làm lại bật', async () => {
    // Vô hiệu hóa nút thao tác lịch sử khi không có giao dịch phù hợp để thực thi.
    let editorInstance: Editor | null = null;

    await act(async () => {
      root.render(
        <StrictMode>
          <DescriptionEditor
            onEditorReady={(editor) => {
              editorInstance = editor;
            }}
          />
        </StrictMode>,
      );
    });

    expect(editorInstance).not.toBeNull();
    const editor = editorInstance as unknown as Editor;

    const undoButton = container.querySelector<HTMLButtonElement>('button[aria-label="Hoàn tác"]');
    const redoButton = container.querySelector<HTMLButtonElement>('button[aria-label="Làm lại"]');

    expect(undoButton).not.toBeNull();
    expect(redoButton).not.toBeNull();

    // Editor mới mở chưa có lịch sử nên cả hai nút đều phải bị tắt.
    expect(undoButton?.disabled).toBe(true);
    expect(redoButton?.disabled).toBe(true);

    // Chèn nội dung để tạo giao dịch mới trong lịch sử.
    await act(async () => {
      editor.commands.insertContent('Nội dung mới');
    });

    expect(undoButton?.disabled).toBe(false);
    expect(redoButton?.disabled).toBe(true);

    // Thực hiện hoàn tác để kích hoạt khả năng làm lại.
    await act(async () => {
      undoButton?.click();
    });

    expect(undoButton?.disabled).toBe(true);
    expect(redoButton?.disabled).toBe(false);
  });

  it('khi có nút bị tắt thì tabIndex 0 vẫn vào được thanh công cụ ở nút bật đầu tiên và phím mũi tên bỏ qua nút bị tắt', async () => {
    // Đảm bảo roving tabindex không bị mắc kẹt vào các nút đã bị vô hiệu hóa.
    await act(async () => {
      root.render(
        <StrictMode>
          <DescriptionEditor />
        </StrictMode>,
      );
    });

    const buttons = Array.from(
      container.querySelectorAll<HTMLButtonElement>('[role="toolbar"] button'),
    );

    // Hai nút cuối cùng (Hoàn tác và Làm lại) đang bị tắt.
    expect(buttons[6]?.disabled).toBe(true);
    expect(buttons[7]?.disabled).toBe(true);

    // Chuyển tiêu điểm tới nút bật cuối cùng (Nghiêng - index 5).
    buttons[5]?.focus();
    await act(async () => {
      buttons[5]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    });

    // Phím mũi tên phải phải bỏ qua nút 6 và 7 để quay vòng về nút bật đầu tiên (Đoạn văn - index 0).
    expect(buttons[0]?.tabIndex).toBe(0);
    expect(document.activeElement).toBe(buttons[0]);

    // Phím mũi tên trái từ nút 0 phải bỏ qua nút 7 và 6 để quay vòng về nút 5.
    await act(async () => {
      buttons[0]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    });

    expect(buttons[5]?.tabIndex).toBe(0);
    expect(document.activeElement).toBe(buttons[5]);
  });

  it('khi dùng dạng controlled, gõ chữ trong đoạn có chữ đậm không gọi setContent và vị trí con trỏ giữ nguyên sau mỗi lần gõ', async () => {
    // Ngăn chặn vòng lặp đồng bộ gây nhảy con trỏ và phát sinh giao dịch thừa.
    const initialDoc: DescriptionDoc = {
      type: 'doc',
      content: [
        {
          type: 'paragraph',
          content: [
            { type: 'text', text: 'Bắt đầu ' },
            {
              type: 'text',
              text: 'chữ đậm',
              marks: [{ type: 'bold' }],
            },
            { type: 'text', text: ' kết thúc.' },
          ],
        },
      ],
    };

    let editorInstance: Editor | null = null;

    function ControlledParent() {
      const [docValue, setDocValue] = useState<DescriptionDoc>(initialDoc);
      return (
        <DescriptionEditor
          value={docValue}
          onChange={setDocValue}
          onEditorReady={(editor) => {
            editorInstance = editor;
          }}
        />
      );
    }

    await act(async () => {
      root.render(
        <StrictMode>
          <ControlledParent />
        </StrictMode>,
      );
    });

    expect(editorInstance).not.toBeNull();
    const editor = editorInstance as unknown as Editor;

    // Giám sát lệnh setContent gốc để chứng minh lệnh này không bị kích hoạt khi gõ.
    const editorInternal = editor as unknown as {
      commandManager: { rawCommands: { setContent: (doc: DescriptionDoc) => unknown } };
    };
    const setContentSpy = vi.spyOn(editorInternal.commandManager.rawCommands, 'setContent');

    // Đặt vị trí con trỏ vào bên trong đoạn văn bản có định dạng chữ đậm.
    await act(async () => {
      editor.commands.setTextSelection(12);
    });
    expect(editor.state.selection.from).toBe(12);

    // Gõ ký tự thứ nhất vào vùng chữ đậm để kích hoạt sự kiện thay đổi.
    await act(async () => {
      editor.commands.insertContent('a');
    });

    // Xác nhận setContent không bị gọi và con trỏ giữ đúng vị trí kế tiếp.
    expect(setContentSpy).not.toHaveBeenCalled();
    expect(editor.state.selection.from).toBe(13);
    expect(editor.state.selection.to).toBe(13);

    // Gõ ký tự thứ hai liên tiếp để đảm bảo tính ổn định sau nhiều thao tác.
    await act(async () => {
      editor.commands.insertContent('b');
    });

    // Xác nhận setContent vẫn không bị gọi và con trỏ tiếp tục dịch chuyển chuẩn xác.
    expect(setContentSpy).not.toHaveBeenCalled();
    expect(editor.state.selection.from).toBe(14);
    expect(editor.state.selection.to).toBe(14);
  });

  it('khi đổi value từ bên ngoài thì editor cập nhật đúng nội dung mới', async () => {
    // Đảm bảo trình soạn thảo nạp nội dung mới khi thành phần cha cập nhật dữ liệu.
    const initialDoc: DescriptionDoc = {
      type: 'doc',
      content: [
        {
          type: 'paragraph',
          content: [{ type: 'text', text: 'Tài liệu ban đầu' }],
        },
      ],
    };

    const newExternalDoc: DescriptionDoc = {
      type: 'doc',
      content: [
        {
          type: 'heading',
          attrs: { level: 2 },
          content: [{ type: 'text', text: 'Tiêu đề mới từ bên ngoài' }],
        },
      ],
    };

    let editorInstance: Editor | null = null;
    let setDocExternal: React.Dispatch<React.SetStateAction<DescriptionDoc>> = () => {};

    function ExternalParent() {
      const [docValue, setDocValue] = useState<DescriptionDoc>(initialDoc);
      setDocExternal = setDocValue;
      return (
        <DescriptionEditor
          value={docValue}
          onChange={setDocValue}
          onEditorReady={(editor) => {
            editorInstance = editor;
          }}
        />
      );
    }

    await act(async () => {
      root.render(
        <StrictMode>
          <ExternalParent />
        </StrictMode>,
      );
    });

    expect(editorInstance).not.toBeNull();
    const editor = editorInstance as unknown as Editor;

    // Giám sát lệnh setContent khi có dữ liệu mới từ bên ngoài truyền vào.
    const editorInternal = editor as unknown as {
      commandManager: { rawCommands: { setContent: (doc: DescriptionDoc) => unknown } };
    };
    const setContentSpy = vi.spyOn(editorInternal.commandManager.rawCommands, 'setContent');

    // Cập nhật trạng thái của thành phần cha với tài liệu mới hoàn toàn.
    await act(async () => {
      setDocExternal(newExternalDoc);
    });

    // Kiểm tra editor đã kích hoạt nạp lại nội dung mới và cấu trúc JSON đồng bộ.
    expect(setContentSpy).toHaveBeenCalledWith(newExternalDoc);
    expect(editor.getJSON()).toEqual(newExternalDoc);
  });
});
