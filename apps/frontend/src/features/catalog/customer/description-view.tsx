import type { ReactNode } from 'react';
import {
  readDescriptionBlocks,
  type DescriptionBlock,
  type DescriptionRun,
} from './description-blocks';

function renderRun(run: DescriptionRun, index: number): ReactNode {
  // Chữ luôn đi qua React dưới dạng chuỗi, nên React thoát ký tự HTML và không tạo thẻ từ nội dung.
  let content: ReactNode = run.text;
  if (run.italic) content = <em>{content}</em>;
  if (run.bold) content = <strong>{content}</strong>;
  return <span key={index}>{content}</span>;
}

function renderBlock(block: DescriptionBlock, index: number): ReactNode {
  const children = block.runs.map(renderRun);
  if (block.kind === 'paragraph') return <p key={index}>{children}</p>;

  // Trang đã có h1 là tên sản phẩm. Vì vậy tiêu đề cấp 1, 2, 3 của mô tả thành h2, h3, h4.
  if (block.level === 1) {
    return (
      <h2 key={index} className="description-heading">
        {children}
      </h2>
    );
  }
  if (block.level === 2) return <h3 key={index}>{children}</h3>;
  return <h4 key={index}>{children}</h4>;
}

export function DescriptionView({ description }: { description: unknown }) {
  const blocks = readDescriptionBlocks(description);
  // Mô tả không có khối hợp lệ thì ẩn cả mục, để trang không có tiêu đề "Mô tả" đứng trơ trọi.
  if (blocks.length === 0) return null;

  return (
    <section className="product-info-section" aria-labelledby="product-description-title">
      <h2 id="product-description-title" className="product-info-title">
        Mô tả sản phẩm
      </h2>
      <div className="description-body">{blocks.map(renderBlock)}</div>
    </section>
  );
}
