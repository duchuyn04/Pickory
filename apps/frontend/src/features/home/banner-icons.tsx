import type { ReactNode } from 'react';

// Icon thông số trên banner sản phẩm. Nét vẽ theo bộ Lucide, cùng độ dày với icon chung của trang.
const BANNER_ICON_PATHS = {
  phone: (
    <>
      <rect x="6" y="2" width="12" height="20" rx="2.5" />
      <path d="M11 18h2" />
    </>
  ),
  camera: (
    <>
      <path d="M4 7h3l2-3h6l2 3h3a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V8a1 1 0 0 1 1-1z" />
      <circle cx="12" cy="13" r="4" />
    </>
  ),
  chip: (
    <>
      <rect x="5" y="5" width="14" height="14" rx="3" />
      <path d="M9 9h6v6H9z" />
    </>
  ),
  cpu: (
    <>
      <rect x="6" y="6" width="12" height="12" rx="2" />
      <rect x="9.5" y="9.5" width="5" height="5" />
      <path d="M9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3" />
    </>
  ),
  battery: (
    <>
      <rect x="7" y="4" width="10" height="18" rx="2" />
      <path d="M10 2h4" />
      <path d="m12.5 9-2 4h3l-2 4" />
    </>
  ),
  fold: (
    <>
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <path d="M12 4v16" />
    </>
  ),
  shield: (
    <>
      <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
      <path d="m9 12 2 2 4-4" />
    </>
  ),
  touch: (
    <>
      <path d="M12 11v4" />
      <path d="M8 12a4 4 0 0 1 8 0v2a8 8 0 0 1-1 4" />
      <path d="M5 13a7 7 0 0 1 14-2" />
    </>
  ),
  memory: (
    <>
      <rect x="3" y="7" width="18" height="10" rx="1.5" />
      <path d="M7 7v10M11 7v10M15 7v10M5 20v-3M19 20v-3" />
    </>
  ),
  storage: (
    <>
      <ellipse cx="12" cy="6" rx="8" ry="3" />
      <path d="M4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6" />
      <path d="M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3" />
    </>
  ),
  screen: (
    <>
      <rect x="2" y="4" width="20" height="13" rx="2" />
      <path d="M8 21h8M12 17v4" />
    </>
  ),
  weight: (
    <>
      <circle cx="12" cy="6" r="3" />
      <path d="M6.5 9h11l2.5 12H4z" />
    </>
  ),
  noise: <path d="M3 12h2M7 8v8M11 5v14M15 8v8M19 11v2" />,
  clock: (
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v5l3 2" />
    </>
  ),
  bluetooth: <path d="m7 7 10 10-5 5V2l5 5L7 17" />,
  headphones: (
    <>
      <path d="M3 18v-6a9 9 0 0 1 18 0v6" />
      <path d="M21 19a2 2 0 0 1-2 2h-1a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2h3zM3 19a2 2 0 0 0 2 2h1a2 2 0 0 0 2-2v-3a2 2 0 0 0-2-2H3z" />
    </>
  ),
  bolt: <path d="M13 2 4 14h7l-1 8 9-12h-7z" />,
} satisfies Record<string, ReactNode>;

export type BannerIconName = keyof typeof BANNER_ICON_PATHS;

export function BannerIcon({ name }: { name: BannerIconName }) {
  return (
    <svg
      className="banner-icon"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {BANNER_ICON_PATHS[name]}
    </svg>
  );
}
