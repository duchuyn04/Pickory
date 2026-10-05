// Danh mục quyền quản trị của module catalog theo quy định phân quyền.
export const catalogPermissions = ['products:read', 'products:write', 'inventory:write'] as const;

// Xuất các hàm lắng nghe và phát sự kiện nghiệp vụ của catalog.
export {
  onProductChanged,
  onStockChanged,
  emitAfterCommit,
  createEventCollector,
  type TransactionEventCollector,
} from './data/events';

// Xuất router phục vụ việc gắn vào ứng dụng Express chính.
export { catalogRouter } from './routes';
