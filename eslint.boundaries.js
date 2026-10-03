// Quy tắc ESLint bảo vệ ranh giới module (xem spec/SPEC.md, mục "Quy tắc ranh giới module").
//
// Ba quy tắc, cùng dùng `no-restricted-imports`:
//   1. `ai`, `@ai-sdk/*`, `openai` chỉ được import trong `modules/ai-gateway/providers/`.
//   2. `@langchain/*` chỉ được import trong `modules/knowledge-base/ingest/`.
//   3. Module chỉ được import module khác qua `modules/<id>/index.ts`, không import
//      thẳng `service.ts`, `repository.ts`... của module khác.
//
// Lưu ý kỹ thuật: ESLint flat config KHÔNG gộp cấu hình của cùng một rule giữa các block,
// block đứng sau ghi đè hoàn toàn block trước. Vì vậy mỗi vùng file phải nhận đủ bộ quy tắc
// áp dụng cho nó, và các vùng ngoại lệ (providers, ingest) đặt SAU CÙNG.

// Id cố định của mọi module, lấy từ spec/CAPABILITY-MAP.md. Thêm module mới thì thêm vào đây.
export const MODULE_IDS = [
  'identity',
  'ai-gateway',
  'catalog',
  'shipping',
  'commerce',
  'activity-tracking',
  'smart-search',
  'recommendations',
  'personalized-content',
  'knowledge-base',
  'shopping-assistant',
  'support-desk',
  'admin-console',
];

const MODULES_DIR = 'apps/api/src/modules';

// Quy tắc 1: SDK AI chỉ dùng trong ai-gateway/providers.
const AI_SDK_BAN = {
  paths: [
    { name: 'ai', message: 'Chỉ import `ai` trong modules/ai-gateway/providers/.' },
    { name: 'openai', message: 'Chỉ import `openai` trong modules/ai-gateway/providers/.' },
  ],
  patterns: [
    {
      group: ['@ai-sdk/*', 'ai/*', 'openai/*'],
      message:
        'SDK AI chỉ được import trong modules/ai-gateway/providers/. Hãy gọi qua ai-gateway.',
    },
  ],
};

// Quy tắc 2: LangChain chỉ dùng trong knowledge-base/ingest.
const LANGCHAIN_BAN = {
  patterns: [
    {
      group: ['@langchain/*'],
      message: 'LangChain chỉ được import trong modules/knowledge-base/ingest/.',
    },
  ],
};

// Quy tắc 3 cho file thuộc module `ownId`: cấm import "chui" vào file bên trong module khác.
// Cho phép `../identity` và `../identity/index`, cấm `../identity/service`.
function crossModulePatterns(ownId) {
  return MODULE_IDS.filter((id) => id !== ownId).map((id) => ({
    group: [`../**/${id}/**`, `!../**/${id}/index`, `!../**/${id}/index.ts`],
    message: `Chỉ import module "${id}" qua modules/${id}/index.ts, không import thẳng file bên trong.`,
  }));
}

// Gộp các phần quy tắc thành một cấu hình `no-restricted-imports`.
function buildRule({ banAi, banLangchain, ownId }) {
  const paths = banAi ? [...AI_SDK_BAN.paths] : [];
  const patterns = [
    ...(banAi ? AI_SDK_BAN.patterns : []),
    ...(banLangchain ? LANGCHAIN_BAN.patterns : []),
    ...(ownId ? crossModulePatterns(ownId) : []),
  ];
  return ['error', { paths, patterns }];
}

export const boundaryConfigs = [
  // Vùng chung: mọi file code (web, api, shared, tools) cấm SDK AI và LangChain.
  {
    files: ['**/*.{ts,tsx}'],
    rules: { 'no-restricted-imports': buildRule({ banAi: true, banLangchain: true }) },
  },
  // Mỗi module: thêm quy tắc không import chui vào module khác.
  ...MODULE_IDS.map((id) => ({
    files: [`${MODULES_DIR}/${id}/**/*.ts`],
    rules: {
      'no-restricted-imports': buildRule({ banAi: true, banLangchain: true, ownId: id }),
    },
  })),
  // Ngoại lệ (đặt sau cùng để ghi đè): được dùng SDK AI trong providers,
  // được dùng LangChain trong ingest. Vẫn giữ quy tắc ranh giới module.
  {
    files: [`${MODULES_DIR}/ai-gateway/providers/**/*.ts`],
    rules: {
      'no-restricted-imports': buildRule({
        banAi: false,
        banLangchain: true,
        ownId: 'ai-gateway',
      }),
    },
  },
  {
    files: [`${MODULES_DIR}/knowledge-base/ingest/**/*.ts`],
    rules: {
      'no-restricted-imports': buildRule({
        banAi: true,
        banLangchain: false,
        ownId: 'knowledge-base',
      }),
    },
  },
];
