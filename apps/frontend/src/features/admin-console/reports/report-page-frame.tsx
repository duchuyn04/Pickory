import type { ReactNode } from 'react';
import './reports.css';

interface ReportPageFrameProps {
  title: string;
  description: string;
  // Hàng chọn thời gian dùng chung của trang.
  controls: ReactNode;
  // Các khối biểu đồ của trang.
  children: ReactNode;
}

// Khung chung của bốn trang báo cáo: đầu trang, hàng chọn thời gian, rồi các khối biểu đồ.
export function ReportPageFrame({ title, description, controls, children }: ReportPageFrameProps) {
  return (
    <div className="page report-page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>{title}</h1>
          <p>{description}</p>
        </div>
      </div>
      <section className="card report-controls-card" aria-label="Chọn thời gian báo cáo">
        {controls}
      </section>
      {children}
    </div>
  );
}
