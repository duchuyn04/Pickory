import { afterEach, describe, expect, it, vi } from 'vitest';
import { ZodError } from 'zod';
import { runOrderMutation } from '../../../../../../src/features/commerce/admin/orders/admin-orders-api';
import { fetchMyReview } from '../../../../../../src/features/commerce/customer/reviews/reviews-api';
import { ApiError } from '../../../../../../src/shared/api';
import type { ContractExample } from '../../../exports-printing/contract-fixtures';
import { findCommerceExample, listCommerceExamples } from '../../contract-fixtures';
import { exampleData, exampleError, exampleFulfillmentRequest } from '../../fixture-response';

afterEach(() => {
  vi.unstubAllGlobals();
});

const FIRST_SUCCESS_NAME = 'start manual shipping';

// Giả `fetch` trả một response JSON. Mock ghi lại lần gọi để test kiểm phương thức, đường dẫn, body, header.
function stubFetch(status: number, body: unknown) {
  const fetchMock = vi.fn<typeof fetch>(async () => Response.json(body, { status }));
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

// Request đầu tiên mà code gửi đi. Không có request thì test hỏng ngay thay vì kiểm trên giá trị rỗng.
function sentRequest(fetchMock: ReturnType<typeof stubFetch>) {
  const call = fetchMock.mock.calls.at(0);
  if (call === undefined) throw new Error('Code không gọi fetch.');
  const [input, init] = call;
  if (init === undefined) throw new Error('Request không có init.');
  return { path: String(input), init };
}

// Mã đơn nằm ở đoạn thứ tư của đường dẫn ví dụ: `/api/admin/orders/{code}/fulfillment`.
function orderCodeOf(example: ContractExample): string {
  const code = example.path?.split('/').at(4);
  if (code === undefined) throw new Error(`Ví dụ "${example.name}" không có mã đơn trong path.`);
  return code;
}

const successExamples = listCommerceExamples('manual-fulfillment.json').filter(
  (example) => example.status === 200,
);

describe('runOrderMutation nhánh fulfillment', () => {
  it.each(successExamples.map((example) => [example.name, example] as const))(
    'gửi PUT đúng đường dẫn, body và trả chi tiết đơn của ví dụ "%s"',
    async (_name, example) => {
      const fetchMock = stubFetch(200, example.response);
      const request = exampleFulfillmentRequest(example);

      const detail = await runOrderMutation(orderCodeOf(example), {
        kind: 'fulfillment',
        ...request,
      });

      const { path, init } = sentRequest(fetchMock);
      expect(path).toBe(example.path);
      expect(init.method).toBe('PUT');
      expect(JSON.parse(String(init.body))).toEqual(example.request);
      expect(new Headers(init.headers).get('Content-Type')).toBe('application/json');
      expect(detail.status).toBe(exampleData(example).status);
    },
  );

  it('mã hóa ký tự đặc biệt của mã đơn trong đường dẫn', async () => {
    const example = findCommerceExample('manual-fulfillment.json', FIRST_SUCCESS_NAME);
    const fetchMock = stubFetch(200, example.response);

    await runOrderMutation('PK 1/2', {
      kind: 'fulfillment',
      toStatus: 'shipping',
      reason: 'Lý do',
    });

    expect(sentRequest(fetchMock).path).toBe('/api/admin/orders/PK%201%2F2/fulfillment');
  });

  it('không cắt khoảng trắng của reason', async () => {
    const example = findCommerceExample('manual-fulfillment.json', FIRST_SUCCESS_NAME);
    const fetchMock = stubFetch(200, example.response);

    await runOrderMutation('PKFULTESTQ', {
      kind: 'fulfillment',
      toStatus: 'shipping',
      reason: '  lý do  ',
    });

    expect(JSON.parse(String(sentRequest(fetchMock).init.body)).reason).toBe('  lý do  ');
  });

  it('từ chối bằng ZodError khi response thiếu khóa bắt buộc', async () => {
    const example = findCommerceExample('manual-fulfillment.json', FIRST_SUCCESS_NAME);
    const data = structuredClone(exampleData(example));
    delete data.status;
    stubFetch(200, { data });

    await expect(
      runOrderMutation('PKFULTESTQ', { kind: 'fulfillment', toStatus: 'shipping', reason: 'x' }),
    ).rejects.toBeInstanceOf(ZodError);
  });

  it.each(['invalid transition', 'invalid reason'])(
    'chuyển lỗi của ví dụ "%s" thành ApiError mang câu lỗi của fixture',
    async (name) => {
      const example = findCommerceExample('manual-fulfillment.json', name);
      stubFetch(example.status, example.response);

      const error = await runOrderMutation('PKFULTESTQ', {
        kind: 'fulfillment',
        ...exampleFulfillmentRequest(example),
      }).catch((caught: unknown) => caught);

      expect(error).toBeInstanceOf(ApiError);
      const fixtureError = exampleError(example);
      // Lỗi 400 hiện câu của từng khóa, các lỗi khác hiện câu chung của fixture.
      const expectedMessage = fixtureError.details?.at(0)?.message ?? fixtureError.message;
      expect((error as ApiError).message).toBe(expectedMessage);
      expect((error as ApiError).status).toBe(example.status);
    },
  );
});

describe('fetchMyReview', () => {
  it.each(['order shipping', 'order delivered'])(
    'trả đúng giá trị của ví dụ "%s"',
    async (name) => {
      const example = findCommerceExample('review-eligibility.json', name);
      const fetchMock = stubFetch(200, example.response);

      const status = await fetchMyReview('8d6707de-2c7f-4b0f-a2ea-3cf82f90a001');

      expect(sentRequest(fetchMock).path).toBe(example.path);
      expect(status).toEqual(exampleData(example));
    },
  );

  it('từ chối bằng ZodError khi response sai hình dạng', async () => {
    stubFetch(200, { data: { eligible: 'yes', reason: null, review: null } });

    await expect(fetchMyReview('p-1')).rejects.toBeInstanceOf(ZodError);
  });
});
