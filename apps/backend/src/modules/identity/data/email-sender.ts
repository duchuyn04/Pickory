import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { loadConfig } from '../../../shared/config';
import { logger } from '../../../shared/logger';

export interface EmailMessage {
  to: string;
  subject: string;
  text: string;
  html?: string;
  // Dữ liệu kèm cho máy đọc (ví dụ token gốc) để test và dev lấy link mà không phải phân tích nội dung thư.
  metadata?: Record<string, unknown>;
}

// Giao diện gửi email; code nghiệp vụ chỉ biết giao diện này nên sau này đổi sang dịch vụ thật không phải sửa chỗ gọi.
export interface EmailSender {
  send(message: EmailMessage): Promise<void>;
}

// Bản dùng trong test: giữ thư trong bộ nhớ để test đọc lại.
export class InMemoryEmailSender implements EmailSender {
  public readonly sentMessages: EmailMessage[] = [];

  async send(message: EmailMessage): Promise<void> {
    this.sentMessages.push(message);
  }

  clear(): void {
    // Gán length về 0 để xóa mảng mà vẫn giữ nguyên tham chiếu mà test đang cầm.
    this.sentMessages.length = 0;
  }
}

// Bản dùng khi dev: ghi thư ra file thay vì gửi thật, để mở file lấy link đặt mật khẩu.
export class DevOutboxEmailSender implements EmailSender {
  private readonly outboxDir: string;

  constructor(outboxDir?: string) {
    this.outboxDir = outboxDir ?? join(process.cwd(), '.dev-outbox');
  }

  async send(message: EmailMessage): Promise<void> {
    try {
      // Tạo thư mục nếu chưa có, vì lần gửi đầu tiên thư mục chưa tồn tại.
      await mkdir(this.outboxDir, { recursive: true });
      // Tên file gồm thời gian để xếp theo thứ tự gửi; ký tự lạ trong email đổi thành gạch dưới để hợp lệ trên mọi hệ điều hành.
      const filename = `${Date.now()}-${message.to.replace(/[^a-zA-Z0-9]/g, '_')}.json`;
      const filePath = join(this.outboxDir, filename);
      await writeFile(filePath, JSON.stringify(message, null, 2), 'utf-8');
      logger.info(
        { to: message.to, subject: message.subject, filePath },
        'Email lưu vào outbox dev',
      );
    } catch (err) {
      // Chỉ ghi log, không ném lỗi: lỗi ghi file dev không được làm hỏng đăng ký hay quên mật khẩu.
      logger.error({ err, to: message.to }, 'Ghi email vào outbox dev thất bại');
    }
  }
}

// Giữ một bản dùng chung để mọi nơi gửi thư qua cùng một sender.
let currentEmailSender: EmailSender | null = null;

export function getEmailSender(): EmailSender {
  if (currentEmailSender) {
    return currentEmailSender;
  }
  const config = loadConfig();
  // Test dùng bản trong bộ nhớ để kiểm tra nội dung thư; các môi trường khác dùng outbox file vì chưa nối dịch vụ gửi thật.
  if (config.NODE_ENV === 'test') {
    currentEmailSender = new InMemoryEmailSender();
  } else {
    currentEmailSender = new DevOutboxEmailSender();
  }
  return currentEmailSender;
}

// Cho test thay sender bằng bản giả hoặc bản có kiểm soát.
export function setEmailSender(sender: EmailSender): void {
  currentEmailSender = sender;
}
