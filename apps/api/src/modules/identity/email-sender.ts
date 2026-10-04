import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { loadConfig } from '../../shared/config';
import { logger } from '../../shared/logger';

export interface EmailMessage {
  to: string;
  subject: string;
  text: string;
  html?: string;
  metadata?: Record<string, unknown>;
}

export interface EmailSender {
  send(message: EmailMessage): Promise<void>;
}

export class InMemoryEmailSender implements EmailSender {
  public readonly sentMessages: EmailMessage[] = [];

  async send(message: EmailMessage): Promise<void> {
    this.sentMessages.push(message);
  }

  clear(): void {
    this.sentMessages.length = 0;
  }
}

export class DevOutboxEmailSender implements EmailSender {
  private readonly outboxDir: string;

  constructor(outboxDir?: string) {
    this.outboxDir = outboxDir ?? join(process.cwd(), '.dev-outbox');
  }

  async send(message: EmailMessage): Promise<void> {
    try {
      // Đảm bảo thư mục lưu trữ tồn tại trước khi ghi file thư vào đĩa.
      await mkdir(this.outboxDir, { recursive: true });
      // Chuẩn hóa tên file theo mốc thời gian và email để tránh ký tự đặc biệt gây lỗi hệ thống tệp.
      const filename = `${Date.now()}-${message.to.replace(/[^a-zA-Z0-9]/g, '_')}.json`;
      const filePath = join(this.outboxDir, filename);
      await writeFile(filePath, JSON.stringify(message, null, 2), 'utf-8');
      logger.info(
        { to: message.to, subject: message.subject, filePath },
        'Email lưu vào outbox dev',
      );
    } catch (err) {
      logger.error({ err, to: message.to }, 'Ghi email vào outbox dev thất bại');
    }
  }
}

let currentEmailSender: EmailSender | null = null;

export function getEmailSender(): EmailSender {
  if (currentEmailSender) {
    return currentEmailSender;
  }
  const config = loadConfig();
  // Môi trường test lưu email trong bộ nhớ để tiện kiểm tra assertions, môi trường khác ghi ra thư mục outbox để dev kiểm tra nội dung.
  if (config.NODE_ENV === 'test') {
    currentEmailSender = new InMemoryEmailSender();
  } else {
    currentEmailSender = new DevOutboxEmailSender();
  }
  return currentEmailSender;
}

/**
 * Cho phép ghi đè đối tượng gửi email để phục vụ kiểm thử đơn vị hoặc tích hợp.
 */
export function setEmailSender(sender: EmailSender): void {
  currentEmailSender = sender;
}
