import type { ActorData } from '../../../identity/hooks/use-actor';
import type { MyReviewStatus, OwnReview } from './reviews-api';

// Trạng thái của khối viết đánh giá. Mỗi trạng thái ứng với một nội dung riêng trên giao diện.
export type WriteState =
  | { kind: 'loading' }
  | { kind: 'sign-in' }
  | { kind: 'staff' }
  | { kind: 'form' }
  | { kind: 'not-purchased' }
  | { kind: 'own-review'; review: OwnReview; canEdit: boolean };

interface ChooseWriteStateInput {
  actor: ActorData | undefined;
  mine: MyReviewStatus | undefined;
  // Giờ hiện tại do nơi gọi truyền vào, để test hạn sửa không phụ thuộc đồng hồ máy.
  now: Date;
}

// Chọn nội dung khối viết đánh giá từ người dùng, trạng thái `/mine` và giờ hiện tại.
export function chooseWriteState({ actor, mine, now }: ChooseWriteStateInput): WriteState {
  // Chưa biết người dùng là ai thì chưa kết luận gì.
  if (actor === undefined) return { kind: 'loading' };
  // Khách vãng lai không có phiên, nên `/mine` không được gọi và không có dữ liệu để xét.
  if (actor.kind === 'guest') return { kind: 'sign-in' };
  // Nhân viên không mua hàng, và `/mine` trả 403 cho họ.
  if (actor.kind === 'staff') return { kind: 'staff' };
  // Khách đã đăng nhập mà `/mine` chưa về thì chờ, để form không nháy lên rồi biến mất.
  if (mine === undefined) return { kind: 'loading' };
  if (mine.eligible) return { kind: 'form' };

  if (mine.reason === 'ALREADY_REVIEWED' && mine.review !== null) {
    // Backend từ chối sửa khi giờ hiện tại lớn hơn hạn. Đúng mốc hạn vẫn sửa được.
    const canEdit = now.getTime() <= new Date(mine.review.editableUntil).getTime();
    return { kind: 'own-review', review: mine.review, canEdit };
  }

  // Còn lại là chưa nhận sản phẩm này. Mã lạ cũng rơi vào đây để không mở form nhầm.
  return { kind: 'not-purchased' };
}
