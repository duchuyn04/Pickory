import React, { useEffect, useId, useRef, useState } from 'react';
import type { Editor } from '@tiptap/core';
import { Bold } from '@tiptap/extension-bold';
import { Document } from '@tiptap/extension-document';
import { Heading } from '@tiptap/extension-heading';
import { Italic } from '@tiptap/extension-italic';
import { Paragraph } from '@tiptap/extension-paragraph';
import { Text } from '@tiptap/extension-text';
import { UndoRedo } from '@tiptap/extensions';
import { EditorContent, useEditor } from '@tiptap/react';
import { descriptionSchema, type DescriptionDoc } from '../../../shared/catalog/description-schema';
import { DescriptionToolbar } from './description-toolbar';
import './description-editor.css';

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
  // Báo lỗi xác thực tài liệu để thành phần cha chặn nút lưu hoặc hiển thị cảnh báo.
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

  // Ghi nhớ tài liệu vừa phát qua onChange để tránh kích hoạt đồng bộ lặp lại.
  const lastEmittedDocRef = useRef<DescriptionDoc | null>(null);
  // Trạng thái nhận tiêu điểm của ô soạn thảo để đổi màu viền khung ngoài.
  const [isFocused, setIsFocused] = useState(false);
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
        class: 'description-editor-content',
      },
    },
    onUpdate: ({ editor: currentEditor }) => {
      const json = currentEditor.getJSON();
      const parseResult = descriptionSchema.safeParse(json);

      if (parseResult.success) {
        // Lưu tham chiếu tài liệu hợp lệ vừa phát để bỏ qua lần đồng bộ kế tiếp.
        lastEmittedDocRef.current = parseResult.data;
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
  // Lắng nghe sự kiện focus và blur để cập nhật viền khung bao ngoài.
  useEffect(() => {
    if (!editor || editor.isDestroyed) {
      return;
    }
    const handleFocus = () => {
      setIsFocused(true);
    };

    const handleBlur = () => {
      setIsFocused(false);
    };

    editor.on('focus', handleFocus);
    editor.on('blur', handleBlur);

    return () => {
      editor.off('focus', handleFocus);
      editor.off('blur', handleBlur);
    };
  }, [editor]);

  // Cập nhật trạng thái có thể chỉnh sửa của Tiptap khi prop disabled thay đổi.
  useEffect(() => {
    if (editor) {
      editor.setEditable(!disabled);
    }
  }, [editor, disabled]);

  // Đồng bộ lại nội dung khi prop value bên ngoài thay đổi.
  useEffect(() => {
    if (!editor || !value) {
      return;
    }

    // Bỏ qua đồng bộ khi giá trị truyền vào chính là tài liệu editor vừa phát.
    if (value === lastEmittedDocRef.current) {
      return;
    }

    // Chuẩn hóa cấu trúc tài liệu qua schema trước khi đối chiếu tính tương đương.
    const currentParsed = descriptionSchema.safeParse(editor.getJSON());
    const incomingParsed = descriptionSchema.safeParse(value);

    const isSameContent =
      currentParsed.success &&
      incomingParsed.success &&
      JSON.stringify(currentParsed.data) === JSON.stringify(incomingParsed.data);

    // Chỉ nạp nội dung khi tài liệu thực sự khác biệt từ nguồn bên ngoài.
    if (!isSameContent) {
      editor.commands.setContent(value);
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

  // Lỗi ưu tiên hơn focus, để người dùng thấy viền lỗi cả khi đang gõ trong ô.
  const frameState = validationError ? 'error' : isFocused ? 'focused' : 'idle';

  return (
    <div className="description-editor">
      <div
        data-testid="description-editor-wrapper"
        className="description-editor-frame"
        data-state={frameState}
        data-disabled={disabled}
      >
        <DescriptionToolbar editor={editor} disabled={disabled} />
        <EditorContent editor={editor} />
      </div>

      {validationError && (
        <div id={errorId} role="alert" className="form-error description-editor-error">
          {validationError}
        </div>
      )}
    </div>
  );
}
