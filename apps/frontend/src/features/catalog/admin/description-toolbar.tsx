import React, { useEffect, useState } from 'react';
import type { Editor } from '@tiptap/core';

export interface DescriptionToolbarProps {
  editor: Editor | null;
}

export function DescriptionToolbar({ editor }: DescriptionToolbarProps) {
  // Theo dõi số phiên bản để kích hoạt vẽ lại thanh công cụ khi con trỏ di chuyển.
  const [, setRevision] = useState(0);

  useEffect(() => {
    if (!editor) {
      return;
    }

    // Cập nhật lại giao diện nút khi định dạng thay đổi hoặc con trỏ nhảy vị trí.
    const handleUpdate = () => {
      setRevision((prev) => prev + 1);
    };

    editor.on('transaction', handleUpdate);
    editor.on('selectionUpdate', handleUpdate);

    return () => {
      editor.off('transaction', handleUpdate);
      editor.off('selectionUpdate', handleUpdate);
    };
  }, [editor]);

  // Tránh render các nút khi phiên bản trình soạn thảo chưa sẵn sàng.
  if (!editor) {
    return null;
  }

  // Lớp CSS nền và viền cho nút định dạng khi được kích hoạt hoặc nhả.
  const getButtonClass = (active: boolean) =>
    `px-2.5 py-1 text-sm rounded border transition-colors ${
      active
        ? 'bg-blue-600 text-white border-blue-600 font-medium'
        : 'bg-white text-gray-700 border-gray-300 hover:bg-gray-100'
    }`;

  // Lớp CSS cho nút hành động không duy trì trạng thái toggle.
  const actionButtonClass =
    'px-2.5 py-1 text-sm rounded border border-gray-300 bg-white text-gray-700 hover:bg-gray-100 disabled:opacity-40 disabled:cursor-not-allowed';

  return (
    <div
      role="toolbar"
      aria-label="Thanh công cụ định dạng văn bản"
      className="flex flex-wrap items-center gap-1.5 border-b border-gray-200 bg-gray-50 p-2"
    >
      <button
        type="button"
        onClick={() => editor.chain().focus().setParagraph().run()}
        aria-pressed={editor.isActive('paragraph')}
        aria-label="Đoạn văn"
        className={getButtonClass(editor.isActive('paragraph'))}
      >
        Đoạn văn
      </button>

      <button
        type="button"
        onClick={() => editor.chain().focus().toggleHeading({ level: 1 }).run()}
        aria-pressed={editor.isActive('heading', { level: 1 })}
        aria-label="Tiêu đề 1"
        className={getButtonClass(editor.isActive('heading', { level: 1 }))}
      >
        Tiêu đề 1
      </button>

      <button
        type="button"
        onClick={() => editor.chain().focus().toggleHeading({ level: 2 }).run()}
        aria-pressed={editor.isActive('heading', { level: 2 })}
        aria-label="Tiêu đề 2"
        className={getButtonClass(editor.isActive('heading', { level: 2 }))}
      >
        Tiêu đề 2
      </button>

      <button
        type="button"
        onClick={() => editor.chain().focus().toggleHeading({ level: 3 }).run()}
        aria-pressed={editor.isActive('heading', { level: 3 })}
        aria-label="Tiêu đề 3"
        className={getButtonClass(editor.isActive('heading', { level: 3 }))}
      >
        Tiêu đề 3
      </button>

      <button
        type="button"
        onClick={() => editor.chain().focus().toggleBold().run()}
        aria-pressed={editor.isActive('bold')}
        aria-label="Đậm"
        className={getButtonClass(editor.isActive('bold'))}
      >
        Đậm
      </button>

      <button
        type="button"
        onClick={() => editor.chain().focus().toggleItalic().run()}
        aria-pressed={editor.isActive('italic')}
        aria-label="Nghiêng"
        className={getButtonClass(editor.isActive('italic'))}
      >
        Nghiêng
      </button>

      <button
        type="button"
        onClick={() => editor.chain().focus().undo().run()}
        disabled={!editor.can().undo()}
        aria-label="Hoàn tác"
        className={actionButtonClass}
      >
        Hoàn tác
      </button>

      <button
        type="button"
        onClick={() => editor.chain().focus().redo().run()}
        disabled={!editor.can().redo()}
        aria-label="Làm lại"
        className={actionButtonClass}
      >
        Làm lại
      </button>
    </div>
  );
}
