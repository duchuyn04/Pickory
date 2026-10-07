import React, { useEffect, useRef, useState } from 'react';
import type { Editor } from '@tiptap/core';

export interface DescriptionToolbarProps {
  editor: Editor | null;
  disabled?: boolean;
}

// Cấu hình từng nút trên thanh công cụ để tránh lặp mã JSX.
interface ToolbarItemConfig {
  label: string;
  isToggle: boolean;
  execute: (editor: Editor) => void;
  isActive: (editor: Editor) => boolean;
  isDisabled: (editor: Editor, disabled: boolean) => boolean;
}

const TOOLBAR_ITEMS: ToolbarItemConfig[] = [
  {
    label: 'Đoạn văn',
    isToggle: true,
    execute: (editor) => editor.chain().focus().setParagraph().run(),
    isActive: (editor) => editor.isActive('paragraph'),
    isDisabled: (_editor, disabled) => disabled,
  },
  {
    label: 'Tiêu đề 1',
    isToggle: true,
    execute: (editor) => editor.chain().focus().toggleHeading({ level: 1 }).run(),
    isActive: (editor) => editor.isActive('heading', { level: 1 }),
    isDisabled: (_editor, disabled) => disabled,
  },
  {
    label: 'Tiêu đề 2',
    isToggle: true,
    execute: (editor) => editor.chain().focus().toggleHeading({ level: 2 }).run(),
    isActive: (editor) => editor.isActive('heading', { level: 2 }),
    isDisabled: (_editor, disabled) => disabled,
  },
  {
    label: 'Tiêu đề 3',
    isToggle: true,
    execute: (editor) => editor.chain().focus().toggleHeading({ level: 3 }).run(),
    isActive: (editor) => editor.isActive('heading', { level: 3 }),
    isDisabled: (_editor, disabled) => disabled,
  },
  {
    label: 'Đậm',
    isToggle: true,
    execute: (editor) => editor.chain().focus().toggleBold().run(),
    isActive: (editor) => editor.isActive('bold'),
    isDisabled: (_editor, disabled) => disabled,
  },
  {
    label: 'Nghiêng',
    isToggle: true,
    execute: (editor) => editor.chain().focus().toggleItalic().run(),
    isActive: (editor) => editor.isActive('italic'),
    isDisabled: (_editor, disabled) => disabled,
  },
  {
    label: 'Hoàn tác',
    isToggle: false,
    execute: (editor) => editor.chain().focus().undo().run(),
    isActive: () => false,
    isDisabled: (editor, disabled) => disabled || !editor.can().undo(),
  },
  {
    label: 'Làm lại',
    isToggle: false,
    execute: (editor) => editor.chain().focus().redo().run(),
    isActive: () => false,
    isDisabled: (editor, disabled) => disabled || !editor.can().redo(),
  },
];

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

  // Tìm vị trí nút bật tiếp theo theo chiều bước nhảy, bỏ qua nút bị tắt.
  const getNextEnabledIndex = (startIndex: number, step: 1 | -1): number => {
    const total = TOOLBAR_ITEMS.length;
    for (let i = 1; i <= total; i += 1) {
      const candidate = (startIndex + i * step + total * total) % total;
      const item = TOOLBAR_ITEMS[candidate];
      if (item && !item.isDisabled(editor, disabled)) {
        return candidate;
      }
    }
    return startIndex;
  };

  // Tìm vị trí nút bật đầu tiên trên thanh công cụ.
  const getFirstEnabledIndex = (): number => {
    return TOOLBAR_ITEMS.findIndex((item) => !item.isDisabled(editor, disabled));
  };

  // Tìm vị trí nút bật cuối cùng trên thanh công cụ.
  const getLastEnabledIndex = (): number => {
    for (let i = TOOLBAR_ITEMS.length - 1; i >= 0; i -= 1) {
      const item = TOOLBAR_ITEMS[i];
      if (item && !item.isDisabled(editor, disabled)) {
        return i;
      }
    }
    return -1;
  };

  // Xác định nút giữ tabIndex 0: chọn nút đang trỏ nếu bật, hoặc nút bật đầu tiên.
  const firstEnabled = getFirstEnabledIndex();
  const currentItem = TOOLBAR_ITEMS[focusedIndex];
  const activeTabIndex =
    focusedIndex !== -1 && currentItem && !currentItem.isDisabled(editor, disabled)
      ? focusedIndex
      : firstEnabled;

  // Điều hướng tiêu điểm giữa các nút bằng phím mũi tên theo chuẩn WAI-ARIA.
  const handleKeyDown = (e: React.KeyboardEvent, index: number) => {
    let nextIndex: number;
    if (e.key === 'ArrowRight') {
      e.preventDefault();
      nextIndex = getNextEnabledIndex(index, 1);
    } else if (e.key === 'ArrowLeft') {
      e.preventDefault();
      nextIndex = getNextEnabledIndex(index, -1);
    } else if (e.key === 'Home') {
      e.preventDefault();
      nextIndex = getFirstEnabledIndex();
    } else if (e.key === 'End') {
      e.preventDefault();
      nextIndex = getLastEnabledIndex();
    } else {
      return;
    }

    if (nextIndex !== -1) {
      setFocusedIndex(nextIndex);
      buttonRefs.current[nextIndex]?.focus();
    }
  };

  return (
    <div
      role="toolbar"
      aria-label="Thanh công cụ định dạng văn bản"
      className="description-toolbar"
    >
      {TOOLBAR_ITEMS.map((item, index) => {
        const isItemActive = item.isActive(editor);
        const isItemDisabled = item.isDisabled(editor, disabled);
        const isCurrentActiveTab = index === activeTabIndex;

        return (
          <button
            key={item.label}
            ref={(el) => {
              buttonRefs.current[index] = el;
            }}
            type="button"
            tabIndex={isCurrentActiveTab ? 0 : -1}
            onFocus={() => setFocusedIndex(index)}
            onKeyDown={(e) => handleKeyDown(e, index)}
            onClick={() => item.execute(editor)}
            disabled={isItemDisabled}
            aria-pressed={item.isToggle ? isItemActive : undefined}
            aria-label={item.label}
            className="editor-tool"
          >
            {item.label}
          </button>
        );
      })}
    </div>
  );
}
