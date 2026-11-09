import { describe, expect, it } from 'vitest';
import { ApiError } from '../../../src/shared/api';
import { UserFacingError, getErrorMessage } from '../../../src/shared/error-message';

describe('getErrorMessage', () => {
  it('trả câu của ApiError vì API đã viết câu tiếng Việt', () => {
    const error = new ApiError('ORDER_NOT_FOUND', 404, 'Không tìm thấy đơn hàng.');
    expect(getErrorMessage(error, 'Câu dự phòng.')).toBe('Không tìm thấy đơn hàng.');
  });

  it('trả câu của UserFacingError do code giao diện tự ném', () => {
    const error = new UserFacingError('Tên trục tùy chọn không được để trống.');
    expect(getErrorMessage(error, 'Câu dự phòng.')).toBe('Tên trục tùy chọn không được để trống.');
  });

  it('lỗi JS thường thì trả câu dự phòng, không lộ chữ kỹ thuật', () => {
    const error = new TypeError("Cannot read properties of undefined (reading 'items')");
    expect(getErrorMessage(error, 'Không tải được giỏ hàng.')).toBe('Không tải được giỏ hàng.');
  });

  it('giá trị không phải lỗi hoặc câu rỗng thì trả câu dự phòng', () => {
    expect(getErrorMessage('HTTP 500', 'Câu dự phòng.')).toBe('Câu dự phòng.');
    expect(getErrorMessage(null, 'Câu dự phòng.')).toBe('Câu dự phòng.');
    expect(getErrorMessage(new UserFacingError(''), 'Câu dự phòng.')).toBe('Câu dự phòng.');
  });
});
