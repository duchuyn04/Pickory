import React, { useEffect } from 'react';
import type { Editor } from '@tiptap/core';
import { Bold } from '@tiptap/extension-bold';
import { Document } from '@tiptap/extension-document';
import { Heading } from '@tiptap/extension-heading';
import { History } from '@tiptap/extension-history';
import { Italic } from '@tiptap/extension-italic';
import { Paragraph } from '@tiptap/extension-paragraph';
import { Text } from '@tiptap/extension-text';
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
}

export function DescriptionEditor({
  value,
  onChange,
  disabled = false,
  label = 'Nội dung mô tả chi tiết sản phẩm',
  onEditorReady,
}: DescriptionEditorProps) {
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
      History,
    ],
    // Kích hoạt vẽ lại component khi có giao dịch biên tập nội dung.
    shouldRerenderOnTransaction: true,
    content: value,
    editable: !disabled,
    editorProps: {
      attributes: {
        role: 'textbox',
        'aria-label': label,
        class: 'min-h-[160px] p-3 text-sm text-gray-900 focus:outline-none max-w-full',
      },
    },
    onUpdate: ({ editor: currentEditor }) => {
      // Chỉ phát sự kiện khi tài liệu JSON vượt qua schema whitelist của CAT-13.
      if (onChange) {
        const json = currentEditor.getJSON();
        const parsed = descriptionSchema.safeParse(json);
        if (parsed.success) {
          onChange(parsed.data);
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

  // Đồng bộ lại nội dung khi prop value bên ngoài thay đổi.
  useEffect(() => {
    if (editor && value) {
      const currentJson = editor.getJSON();
      if (JSON.stringify(currentJson) !== JSON.stringify(value)) {
        editor.commands.setContent(value);
      }
    }
  }, [editor, value]);

  return (
    <div className="w-full max-w-full overflow-hidden rounded-md border border-gray-300 bg-white focus-within:border-blue-500 focus-within:ring-1 focus-within:ring-blue-500">
      <DescriptionToolbar editor={editor} />
      <EditorContent editor={editor} />
    </div>
  );
}
