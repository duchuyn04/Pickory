import React, { useEffect, useRef, useState } from 'react';
import type { Editor } from '@tiptap/core';

export interface DescriptionToolbarProps {
  editor: Editor | null;
  disabled?: boolean;
}

export function DescriptionToolbar({ editor, disabled = false }: DescriptionToolbarProps) {
  // Theo dõi số phiên bản để cập nhật giao diện khi con trỏ di chuyển.
  const [, setRevision] = useState(0);

  // Vị trí nút đang giữ tabIndex 0 theo mẫu ARIA Toolbar.
  const [focusedIndex, setFocusedIndex] = useState(0);

  // Mảng tham chiếu tới từng phần tử nút để chuyển tiêu điểm bàn phím.
  const buttonRefs = useRef<(HTMLButtonElement | null)[]>([]);

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

  const buttonCount = 8;

  // Điều hướng tiêu điểm giữa các nút bằng phím mũi tên theo chuẩn WAI-ARIA.
  const handleKeyDown = (e: React.KeyboardEvent, index: number) => {
    let nextIndex: number;
    if (e.key === 'ArrowRight') {
      e.preventDefault();
      nextIndex = (index + 1) % buttonCount;
    } else if (e.key === 'ArrowLeft') {
      e.preventDefault();
      nextIndex = (index - 1 + buttonCount) % buttonCount;
    } else if (e.key === 'Home') {
      e.preventDefault();
      nextIndex = 0;
    } else if (e.key === 'End') {
      e.preventDefault();
      nextIndex = buttonCount - 1;
    } else {
      return;
    }

    setFocusedIndex(nextIndex);
    buttonRefs.current[nextIndex]?.focus();
  };

  // Kiểu dáng cho nút định dạng có trạng thái bật và tắt.
  const getFormatButtonStyle = (active: boolean): React.CSSProperties => {
    const isButtonDisabled = disabled;
    return {
      padding: 'var(--space-1) var(--space-3)',
      borderRadius: 'var(--radius-control)',
      border: active ? '1px solid var(--color-brand)' : '1px solid var(--color-border)',
      backgroundColor: active ? 'var(--color-brand-soft)' : 'var(--color-surface)',
      color: active ? 'var(--color-brand)' : 'var(--color-ink)',
      fontSize: 'var(--text-sm)',
      fontFamily: 'var(--font-sans)',
      fontWeight: active ? 600 : 500,
      cursor: isButtonDisabled ? 'not-allowed' : 'pointer',
      opacity: isButtonDisabled ? 0.5 : 1,
      transition: 'background-color 0.15s ease, border-color 0.15s ease',
    };
  };

  // Kiểu dáng cho nút hành động đơn lẻ không lưu trạng thái bật.
  const getActionButtonStyle = (): React.CSSProperties => {
    const isButtonDisabled = disabled;
    return {
      padding: 'var(--space-1) var(--space-3)',
      borderRadius: 'var(--radius-control)',
      border: '1px solid var(--color-border)',
      backgroundColor: 'var(--color-surface)',
      color: 'var(--color-ink)',
      fontSize: 'var(--text-sm)',
      fontFamily: 'var(--font-sans)',
      fontWeight: 500,
      cursor: isButtonDisabled ? 'not-allowed' : 'pointer',
      opacity: isButtonDisabled ? 0.5 : 1,
      transition: 'background-color 0.15s ease, border-color 0.15s ease',
    };
  };

  return (
    <div
      role="toolbar"
      aria-label="Thanh công cụ định dạng văn bản"
      style={{
        display: 'flex',
        flexWrap: 'wrap',
        alignItems: 'center',
        gap: 'var(--space-2)',
        padding: 'var(--space-2)',
        borderBottom: '1px solid var(--color-border)',
        backgroundColor: 'var(--color-surface-muted)',
      }}
    >
      <button
        ref={(el) => {
          buttonRefs.current[0] = el;
        }}
        type="button"
        tabIndex={focusedIndex === 0 ? 0 : -1}
        onFocus={() => setFocusedIndex(0)}
        onKeyDown={(e) => handleKeyDown(e, 0)}
        onClick={() => editor.chain().focus().setParagraph().run()}
        disabled={disabled}
        aria-pressed={editor.isActive('paragraph')}
        aria-label="Đoạn văn"
        style={getFormatButtonStyle(editor.isActive('paragraph'))}
      >
        Đoạn văn
      </button>

      <button
        ref={(el) => {
          buttonRefs.current[1] = el;
        }}
        type="button"
        tabIndex={focusedIndex === 1 ? 0 : -1}
        onFocus={() => setFocusedIndex(1)}
        onKeyDown={(e) => handleKeyDown(e, 1)}
        onClick={() => editor.chain().focus().toggleHeading({ level: 1 }).run()}
        disabled={disabled}
        aria-pressed={editor.isActive('heading', { level: 1 })}
        aria-label="Tiêu đề 1"
        style={getFormatButtonStyle(editor.isActive('heading', { level: 1 }))}
      >
        Tiêu đề 1
      </button>

      <button
        ref={(el) => {
          buttonRefs.current[2] = el;
        }}
        type="button"
        tabIndex={focusedIndex === 2 ? 0 : -1}
        onFocus={() => setFocusedIndex(2)}
        onKeyDown={(e) => handleKeyDown(e, 2)}
        onClick={() => editor.chain().focus().toggleHeading({ level: 2 }).run()}
        disabled={disabled}
        aria-pressed={editor.isActive('heading', { level: 2 })}
        aria-label="Tiêu đề 2"
        style={getFormatButtonStyle(editor.isActive('heading', { level: 2 }))}
      >
        Tiêu đề 2
      </button>

      <button
        ref={(el) => {
          buttonRefs.current[3] = el;
        }}
        type="button"
        tabIndex={focusedIndex === 3 ? 0 : -1}
        onFocus={() => setFocusedIndex(3)}
        onKeyDown={(e) => handleKeyDown(e, 3)}
        onClick={() => editor.chain().focus().toggleHeading({ level: 3 }).run()}
        disabled={disabled}
        aria-pressed={editor.isActive('heading', { level: 3 })}
        aria-label="Tiêu đề 3"
        style={getFormatButtonStyle(editor.isActive('heading', { level: 3 }))}
      >
        Tiêu đề 3
      </button>

      <button
        ref={(el) => {
          buttonRefs.current[4] = el;
        }}
        type="button"
        tabIndex={focusedIndex === 4 ? 0 : -1}
        onFocus={() => setFocusedIndex(4)}
        onKeyDown={(e) => handleKeyDown(e, 4)}
        onClick={() => editor.chain().focus().toggleBold().run()}
        disabled={disabled}
        aria-pressed={editor.isActive('bold')}
        aria-label="Đậm"
        style={getFormatButtonStyle(editor.isActive('bold'))}
      >
        Đậm
      </button>

      <button
        ref={(el) => {
          buttonRefs.current[5] = el;
        }}
        type="button"
        tabIndex={focusedIndex === 5 ? 0 : -1}
        onFocus={() => setFocusedIndex(5)}
        onKeyDown={(e) => handleKeyDown(e, 5)}
        onClick={() => editor.chain().focus().toggleItalic().run()}
        disabled={disabled}
        aria-pressed={editor.isActive('italic')}
        aria-label="Nghiêng"
        style={getFormatButtonStyle(editor.isActive('italic'))}
      >
        Nghiêng
      </button>

      <button
        ref={(el) => {
          buttonRefs.current[6] = el;
        }}
        type="button"
        tabIndex={focusedIndex === 6 ? 0 : -1}
        onFocus={() => setFocusedIndex(6)}
        onKeyDown={(e) => handleKeyDown(e, 6)}
        onClick={() => editor.chain().focus().undo().run()}
        disabled={disabled}
        aria-label="Hoàn tác"
        style={getActionButtonStyle()}
      >
        Hoàn tác
      </button>

      <button
        ref={(el) => {
          buttonRefs.current[7] = el;
        }}
        type="button"
        tabIndex={focusedIndex === 7 ? 0 : -1}
        onFocus={() => setFocusedIndex(7)}
        onKeyDown={(e) => handleKeyDown(e, 7)}
        onClick={() => editor.chain().focus().redo().run()}
        disabled={disabled}
        aria-label="Làm lại"
        style={getActionButtonStyle()}
      >
        Làm lại
      </button>
    </div>
  );
}
