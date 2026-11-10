import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, type ReactElement } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { vi } from 'vitest';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';

// Bộ dựng chung cho test màn cấu hình và hộp thoại "Mẫu của tôi": máy chủ giả có trạng thái.

export interface RecordedRequest {
  method: string;
  path: string;
  body: Record<string, unknown> | null;
}

export interface FakeCannedResponse {
  id: string;
  shortcut: string;
  body: string;
  shared: boolean;
  editable: boolean;
}

export interface FakeSettings {
  opensAt: string;
  closesAt: string;
  holidays: string[];
  waitNudgeMinutes: number;
  autoCloseMinutes: number;
  updatedAt: string;
}

export const SETTINGS_PATH = '/api/admin/support/settings';
export const CANNED_PATH = '/api/admin/support/canned-responses';

export const STORED_SETTINGS: FakeSettings = {
  opensAt: '08:00',
  closesAt: '21:00',
  holidays: ['2027-02-07', '2027-02-06'],
  waitNudgeMinutes: 10,
  autoCloseMinutes: 30,
  updatedAt: '2026-11-03T01:00:00Z',
};

export const SHARED_READ_ONLY: FakeCannedResponse = {
  id: 'ca1',
  shortcut: 'camon',
  body: 'Cảm ơn {ten_khach} đã liên hệ Pickory.',
  shared: true,
  editable: false,
};
export const SHARED_EDITABLE: FakeCannedResponse = {
  id: 'ca2',
  shortcut: 'giaohang',
  body: 'Đơn hàng giao trong 3 ngày.',
  shared: true,
  editable: true,
};
export const PERSONAL: FakeCannedResponse = {
  id: 'ca3',
  shortcut: 'kiemdon',
  body: 'Mình kiểm đơn {ma_don} giúp bạn nhé.',
  shared: false,
  editable: true,
};

export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status });
}

export function errorResponse(
  status: number,
  code: string,
  message: string,
  details?: { key: string; message: string }[],
): Response {
  return jsonResponse({ error: { code, message, details } }, status);
}

export interface FakeServer {
  settings: FakeSettings;
  canned: FakeCannedResponse[];
  requests: RecordedRequest[];
  // Hàm trả phản hồi cho `PUT` cấu hình. Không đặt thì lưu thành công như máy chủ thật.
  putSettings?: (body: Record<string, unknown>) => Promise<Response> | Response;
  postCanned?: (body: Record<string, unknown>) => Promise<Response> | Response;
  patchCanned?: (id: string, body: Record<string, unknown>) => Promise<Response> | Response;
  deleteCanned?: (id: string) => Promise<Response> | Response;
  // Lệnh `GET` cấu hình trả lỗi này thay vì dữ liệu.
  settingsGetFailure?: Response;
}

export function createFakeServer(overrides: Partial<FakeServer> = {}): FakeServer {
  return {
    settings: { ...STORED_SETTINGS, holidays: [...STORED_SETTINGS.holidays] },
    canned: [SHARED_READ_ONLY, SHARED_EDITABLE, PERSONAL],
    requests: [],
    ...overrides,
  };
}

let nextCreatedId = 100;

// Gắn `fetch` giả đọc và ghi vào `server`. Mọi request được ghi lại để test kiểm đường dẫn và body.
export function stubFetch(server: FakeServer) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost');
      const method = init?.method ?? 'GET';
      const body: Record<string, unknown> | null =
        typeof init?.body === 'string' ? (JSON.parse(init.body) as Record<string, unknown>) : null;
      server.requests.push({ method, path: url.pathname, body });

      if (url.pathname === SETTINGS_PATH) return handleSettings(server, method, body);
      if (url.pathname.startsWith(CANNED_PATH)) return handleCanned(server, method, url, body);
      return errorResponse(404, 'NOT_FOUND', 'Không có route giả.');
    }),
  );
}

async function handleSettings(
  server: FakeServer,
  method: string,
  body: Record<string, unknown> | null,
): Promise<Response> {
  if (method === 'GET') {
    return server.settingsGetFailure ?? jsonResponse({ data: server.settings });
  }
  if (server.putSettings) return server.putSettings(body ?? {});
  server.settings = {
    ...server.settings,
    ...(body as Omit<FakeSettings, 'updatedAt'>),
    updatedAt: '2026-11-03T03:00:00Z',
  };
  return jsonResponse({ data: server.settings });
}

async function handleCanned(
  server: FakeServer,
  method: string,
  url: URL,
  body: Record<string, unknown> | null,
): Promise<Response> {
  const id = url.pathname.slice(CANNED_PATH.length + 1);
  if (method === 'GET') return jsonResponse({ data: server.canned });
  if (method === 'POST') {
    if (server.postCanned) return server.postCanned(body ?? {});
    const created: FakeCannedResponse = {
      id: `ca${nextCreatedId++}`,
      shortcut: String(body?.shortcut),
      body: String(body?.body),
      shared: body?.shared === true,
      editable: true,
    };
    server.canned = [...server.canned, created];
    return jsonResponse({ data: created }, 201);
  }
  if (method === 'PATCH') {
    if (server.patchCanned) return server.patchCanned(id, body ?? {});
    server.canned = server.canned.map((entry) => (entry.id === id ? { ...entry, ...body } : entry));
    return jsonResponse({ data: server.canned.find((entry) => entry.id === id) });
  }
  if (server.deleteCanned) return server.deleteCanned(id);
  server.canned = server.canned.filter((entry) => entry.id !== id);
  return new Response(null, { status: 204 });
}

export function actorWith(permissions: string[]): ActorData {
  return {
    anonymousId: 'anon',
    userId: 'user-1',
    kind: 'staff',
    roles: ['support'],
    permissions,
    consent: { personalization: false, marketing: false },
  };
}

export interface RenderedView {
  container: HTMLDivElement;
  client: QueryClient;
  unmount: () => Promise<void>;
}

// Cổng chờ: request giả dừng ở `promise` cho tới khi test gọi `open`. Giữ request đang chạy để kiểm trạng thái "đang lưu".
export interface Gate {
  promise: Promise<void>;
  open: () => void;
}

export function createGate(): Gate {
  let open: () => void = () => {};
  const promise = new Promise<void>((resolve) => {
    open = resolve;
  });
  return { promise, open };
}

// Chờ các lời hứa của React Query và effect chạy xong. Hai lượt để lượt đọc dữ liệu kéo theo lượt vẽ lại.
export async function settle() {
  for (let pass = 0; pass < 2; pass++) {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
  }
}

export async function renderView(actor: ActorData, element: ReactElement): Promise<RenderedView> {
  const container = document.createElement('div');
  document.body.append(container);
  const root: Root = createRoot(container);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  client.setQueryData(['actor'], actor);
  await act(async () =>
    root.render(
      <QueryClientProvider client={client}>
        <MemoryRouter>{element}</MemoryRouter>
      </QueryClientProvider>,
    ),
  );
  await settle();
  return {
    container,
    client,
    unmount: async () => {
      await act(async () => root.unmount());
      container.remove();
      client.clear();
    },
  };
}

// Đặt giá trị cho ô có kiểm soát của React: gán bằng setter gốc rồi phát sự kiện `input`.
export async function typeInto(
  root: HTMLElement,
  selector: string,
  text: string,
): Promise<HTMLInputElement | HTMLTextAreaElement> {
  const element = root.querySelector<HTMLInputElement | HTMLTextAreaElement>(selector);
  if (!element) throw new Error(`Không thấy ô ${selector}`);
  const prototype =
    element instanceof HTMLTextAreaElement
      ? HTMLTextAreaElement.prototype
      : HTMLInputElement.prototype;
  const setter = Object.getOwnPropertyDescriptor(prototype, 'value')?.set;
  await act(async () => {
    setter?.call(element, text);
    element.dispatchEvent(new Event('input', { bubbles: true }));
  });
  return element;
}

// Tìm nút theo tên truy cập: `aria-label` nếu có, không thì chữ hiện ra.
export function findButtonByName(root: ParentNode, name: string): HTMLButtonElement | null {
  const buttons = Array.from(root.querySelectorAll<HTMLButtonElement>('button'));
  return (
    buttons.find(
      (button) => (button.getAttribute('aria-label') ?? button.textContent?.trim()) === name,
    ) ?? null
  );
}

export async function clickButton(root: ParentNode, name: string) {
  const button = findButtonByName(root, name);
  if (!button) throw new Error(`Không thấy nút "${name}".`);
  await act(async () => button.click());
  await settle();
}

export function requestsOf(server: FakeServer, method: string, path: string): RecordedRequest[] {
  return server.requests.filter((request) => request.method === method && request.path === path);
}

// Câu lỗi nằm ngay sau ô, nối với ô bằng `aria-describedby`. Trả chữ của câu lỗi đó.
export function errorTextOf(root: ParentNode, inputSelector: string): string | null {
  const input = root.querySelector<HTMLElement>(inputSelector);
  const describedBy = input?.getAttribute('aria-describedby') ?? '';
  for (const id of describedBy.split(' ')) {
    const element = id ? root.querySelector(`[id="${id}"]`) : null;
    if (element?.classList.contains('form-error')) return element.textContent;
  }
  return null;
}
