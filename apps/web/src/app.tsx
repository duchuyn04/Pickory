import { Route, Routes } from 'react-router-dom';
import { HealthPage } from './shared/health-page';

// Bảng route của web. Mỗi module thêm route của mình ở đây khi có trang.
export function App() {
  return (
    <Routes>
      <Route path="/" element={<HealthPage />} />
    </Routes>
  );
}
