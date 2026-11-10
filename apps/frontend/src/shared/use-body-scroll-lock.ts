import { useEffect } from 'react';

// Số hộp thoại đang khóa cuộn. Hộp chồng nhau chỉ nhả khóa khi hộp cuối đóng.
let lockCount = 0;
let previousOverflow = '';

// Khóa cuộn trang nền khi hộp thoại mở, để cuộn trong hộp không kéo trang phía sau.
export function useBodyScrollLock(isLocked: boolean) {
  useEffect(() => {
    if (!isLocked) return;
    // Lưu giá trị cũ ở hộp đầu tiên để trả lại đúng khi nhả khóa.
    if (lockCount === 0) previousOverflow = document.body.style.overflow;
    lockCount += 1;
    document.body.style.overflow = 'hidden';
    return () => {
      lockCount -= 1;
      if (lockCount === 0) document.body.style.overflow = previousOverflow;
    };
  }, [isLocked]);
}
