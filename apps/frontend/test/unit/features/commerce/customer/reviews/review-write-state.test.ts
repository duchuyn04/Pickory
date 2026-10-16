import { describe, expect, it } from 'vitest';
import type { ActorData } from '../../../../../../src/features/identity/hooks/use-actor';
import type {
  MyReviewStatus,
  OwnReview,
} from '../../../../../../src/features/commerce/customer/reviews/reviews-api';
import { chooseWriteState } from '../../../../../../src/features/commerce/customer/reviews/review-write-state';

const EDITABLE_UNTIL = '2026-11-01T03:00:00Z';

function buildActor(kind: ActorData['kind']): ActorData {
  return {
    anonymousId: 'anon-1',
    userId: kind === 'guest' ? null : 'user-1',
    kind,
    roles: [],
    permissions: [],
    consent: { personalization: false, marketing: false },
  };
}

function buildOwnReview(overrides: Partial<OwnReview> = {}): OwnReview {
  return {
    id: 'review-1',
    rating: 4,
    body: 'Dùng tốt',
    hidden: false,
    createdAt: '2026-10-02T03:00:00Z',
    updatedAt: '2026-10-02T03:00:00Z',
    editableUntil: EDITABLE_UNTIL,
    ...overrides,
  };
}

const eligible: MyReviewStatus = { eligible: true, reason: null, review: null };
const notPurchased: MyReviewStatus = { eligible: false, reason: 'NOT_PURCHASED', review: null };

function alreadyReviewed(review: OwnReview): MyReviewStatus {
  return { eligible: false, reason: 'ALREADY_REVIEWED', review };
}

const beforeDeadline = new Date('2026-10-20T00:00:00Z');

describe('chooseWriteState', () => {
  it('chưa có actor thì đang tải', () => {
    expect(chooseWriteState({ actor: undefined, mine: undefined, now: beforeDeadline })).toEqual({
      kind: 'loading',
    });
  });

  it('khách vãng lai được mời đăng nhập, dù có dữ liệu /mine thừa', () => {
    const state = chooseWriteState({
      actor: buildActor('guest'),
      mine: eligible,
      now: beforeDeadline,
    });

    expect(state).toEqual({ kind: 'sign-in' });
  });

  it('nhân viên không viết đánh giá', () => {
    const state = chooseWriteState({
      actor: buildActor('staff'),
      mine: eligible,
      now: beforeDeadline,
    });

    expect(state).toEqual({ kind: 'staff' });
  });

  it('khách chưa có dữ liệu /mine thì đang tải', () => {
    const state = chooseWriteState({
      actor: buildActor('customer'),
      mine: undefined,
      now: beforeDeadline,
    });

    expect(state).toEqual({ kind: 'loading' });
  });

  it('khách đủ điều kiện thấy form viết', () => {
    const state = chooseWriteState({
      actor: buildActor('customer'),
      mine: eligible,
      now: beforeDeadline,
    });

    expect(state).toEqual({ kind: 'form' });
  });

  it('khách NOT_PURCHASED thấy lý do, không thấy form', () => {
    const state = chooseWriteState({
      actor: buildActor('customer'),
      mine: notPurchased,
      now: beforeDeadline,
    });

    expect(state).toEqual({ kind: 'not-purchased' });
  });

  it('khách ALREADY_REVIEWED còn hạn sửa thì được sửa', () => {
    const review = buildOwnReview();
    const state = chooseWriteState({
      actor: buildActor('customer'),
      mine: alreadyReviewed(review),
      now: beforeDeadline,
    });

    expect(state).toEqual({ kind: 'own-review', review, canEdit: true });
  });

  it('khách ALREADY_REVIEWED quá hạn sửa thì không được sửa', () => {
    const review = buildOwnReview();
    const state = chooseWriteState({
      actor: buildActor('customer'),
      mine: alreadyReviewed(review),
      now: new Date('2026-11-01T03:00:01Z'),
    });

    expect(state).toEqual({ kind: 'own-review', review, canEdit: false });
  });

  it('đúng mốc editableUntil vẫn sửa được, khớp luật backend chỉ từ chối khi now lớn hơn hạn', () => {
    const review = buildOwnReview();
    const state = chooseWriteState({
      actor: buildActor('customer'),
      mine: alreadyReviewed(review),
      now: new Date(EDITABLE_UNTIL),
    });

    expect(state).toEqual({ kind: 'own-review', review, canEdit: true });
  });

  it('lý do ALREADY_REVIEWED mà thiếu đánh giá thì không mở form', () => {
    const state = chooseWriteState({
      actor: buildActor('customer'),
      mine: { eligible: false, reason: 'ALREADY_REVIEWED', review: null },
      now: beforeDeadline,
    });

    expect(state).toEqual({ kind: 'not-purchased' });
  });
});
