import React, { useEffect, useId, useState } from 'react';
import type { Editor } from '@tiptap/core';
import { Bold } from '@tiptap/extension-bold';
import { Document } from '@tiptap/extension-document';
import { Heading } from '@tiptap/extension-heading';
import { Italic } from '@tiptap/extension-italic';
import { Paragraph } from '@tiptap/extension-paragraph';
import { Text } from '@tiptap/extension-text';
import { UndoRedo } from '@tiptap/extensions';
import { EditorContent, useEditor } from '@tiptap/react';
import { descriptionSchema, type DescriptionDoc } from '@pickory/shared';
import { DescriptionToolbar } from './description-toolbar';

export interface DescriptionEditorProps {
  // Dữ liệu tài liệu JSON đã lưu theo cấu trúc của CAT-13.
  value?: DescriptionDoc;
  // Hàm phản hồi khi người dùng thay đổi nội dung bên trong trình soạn thảo.
  onChange?: (doc: DescriptionDoc) => void;
  // Khóa chế độ chỉnh sửa khi form đang gửi dữ liệu hoặc chỉ đọc.
  disabled?: boolean;
  // Nhãn tiếp cận cho ô nhập liệu phục vụ trình đọc màn hình.
  label?: string;
  // Callback cung cấp tham chiếu editor phục vụ kiểm thử đơn vị.
  onEditorReady?: (editor: Editor) => void;
  // Callback thông báo lỗi xác thực schema cho thành phần cha xử lý.
  onValidationError?: (message: string | null) => void;
}

export function DescriptionEditor({
  value,
  onChange,
  disabled = false,
  label = 'Nội dung mô tả chi tiết sản phẩm',
  onEditorReady,
  onValidationError,
}: DescriptionEditorProps) {
  // Tạo định danh duy nhất cho thông báo lỗi phục vụ thuộc tính aria-describedby.
  const errorId = useId();

  // Lưu thông điệp lỗi khi tài liệu vi phạm ràng buộc schema Zod của CAT-13.
  const [validationError, setValidationError] = useState<string | null>(null);

  // Khởi tạo trình soạn thảo Tiptap với danh sách extension tối thiểu đã phê duyệt.
  const editor = useEditor({
    extensions: [
      Document,
      Paragraph,
      Text,
      // Cấu hình tiêu đề giới hạn nghiêm ngặt ở 3 cấp độ theo đặc tả hệ thống.
      Heading.configure({ levels: [1, 2, 3] }),
      Bold,
      Italic,
      // Dùng extension UndoRedo chính thức từ gói @tiptap/extensions.
      UndoRedo,
    ],
    // Kích hoạt vẽ lại component khi có giao dịch biên tập nội dung.
    shouldRerenderOnTransaction: true,
    content: value,
    editable: !disabled,
    editorProps: {
      attributes: {
        role: 'textbox',
        'aria-multiline': 'true',
        'aria-label': label,
        'aria-invalid': validationError ? 'true' : 'false',
        ...(validationError ? { 'aria-describedby': errorId } : {}),
        style:
          'min-height: 160px; padding: var(--space-3); font-size: var(--text-sm); font-family: var(--font-sans); color: var(--color-ink); outline: none; max-width: 100%; overflow-x: hidden; word-break: break-word;',
      },
    },
    onUpdate: ({ editor: currentEditor }) => {
      const json = currentEditor.getJSON();
      const parseResult = descriptionSchema.safeParse(json);

      if (parseResult.success) {
        // Xóa trạng thái lỗi khi nội dung hợp lệ trở lại.
        setValidationError(null);
        if (onValidationError) {
          onValidationError(null);
        }
        if (onChange) {
          onChange(parseResult.data);
        }
      } else {
        // Lấy thông điệp lỗi tiếng Việt đầu tiên từ kết quả xác thực Zod.
        const errorMessage = parseResult.error.issues[0]?.message || 'Nội dung mô tả không hợp lệ.';
        setValidationError(errorMessage);
        if (onValidationError) {
          onValidationError(errorMessage);
        }
      }
    },
  });

  // Chuyển đối tượng editor ra ngoài khi component đã sẵn sàng.
  useEffect(() => {
    if (editor && onEditorReady) {
      onEditorReady(editor);
    }
  }, [editor, onEditorReady]);

  // Cập nhật trạng thái có thể chỉnh sửa của Tiptap khi prop disabled thay đổi.
  useEffect(() => {
    if (editor) {
      editor.setEditable(!disabled);
    }
  }, [editor, disabled]);

  // Đồng bộ lại nội dung khi prop value bên ngoài thay đổi.
  useEffect(() => {
    if (editor && value) {
      const currentJson = editor.getJSON();
      if (JSON.stringify(currentJson) !== JSON.stringify(value)) {
        editor.commands.setContent(value);
      }
    }
  }, [editor, value]);

  // Cập nhật thuộc tính aria-invalid và aria-describedby trực tiếp trên DOM của editor.
  useEffect(() => {
    if (!editor || editor.isDestroyed) {
      return;
    }

    const dom = editor.view.dom;
    dom.setAttribute('aria-invalid', validationError ? 'true' : 'false');

    if (validationError) {
      dom.setAttribute('aria-describedby', errorId);
    } else {
      dom.removeAttribute('aria-describedby');
    }
  }, [editor, validationError, errorId]);

  return (
    <div style={{ width: '100%', maxWidth: '100%', boxSizing: 'border-box' }}>
      <div
        style={{
          width: '100%',
          maxWidth: '100%',
          boxSizing: 'border-box',
          overflow: 'hidden',
          borderRadius: 'var(--radius-control)',
          border: validationError
            ? '1px solid var(--color-danger)'
            : '1px solid var(--color-border)',
          backgroundColor: disabled ? 'var(--color-surface-muted)' : 'var(--color-surface)',
          fontFamily: 'var(--font-sans)',
        }}
      >
        <DescriptionToolbar editor={editor} disabled={disabled} />
        <EditorContent editor={editor} />
      </div>

      {validationError && (
        <div
          id={errorId}
          role="alert"
          style={{
            marginTop: 'var(--space-1)',
            fontSize: 'var(--text-xs)',
            color: 'var(--color-danger)',
            fontFamily: 'var(--font-sans)',
          }}
        >
          {validationError}
        </div>
      )}
    </div>
  );
}
