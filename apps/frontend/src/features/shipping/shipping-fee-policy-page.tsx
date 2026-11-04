import { Link } from 'react-router-dom';
import './shipping-fee-policy-page.css';

// Mức miễn phí mặc định của shop, khớp `CommerceLimits.Defaults` ở backend.
// Khi admin đổi mức miễn phí, sửa cả trang này để khách đọc đúng chính sách.
const FREE_SHIPPING_THRESHOLD_LABEL = '500.000 đ';

// Ba tuyến phí dùng để giải thích cách tính cho khách.
// Trang không nêu đơn vị vận chuyển hay cách hệ thống dự phòng, vì khách chỉ cần hiểu phí của mình.
const FEE_ROUTES = [
  {
    name: 'Nội tỉnh',
    description: 'Cùng tỉnh, thành với kho gửi.',
  },
  {
    name: 'Nội vùng',
    description: 'Khác tỉnh, thành nhưng cùng vùng với kho gửi.',
  },
  {
    name: 'Liên vùng',
    description: 'Khác vùng với kho gửi.',
  },
] as const;

// Danh sách tỉnh, thành theo vùng của biểu phí vận chuyển.
// Sửa bảng này khi biểu phí đổi vùng của một tỉnh, thành.
const REGION_GROUPS = [
  {
    name: 'Miền Bắc',
    provinces: [
      'Hà Nội',
      'Hải Phòng',
      'Bắc Ninh',
      'Cao Bằng',
      'Điện Biên',
      'Hà Tĩnh',
      'Hưng Yên',
      'Lai Châu',
      'Lạng Sơn',
      'Lào Cai',
      'Nghệ An',
      'Ninh Bình',
      'Phú Thọ',
      'Quảng Ninh',
      'Sơn La',
      'Thanh Hóa',
      'Thái Nguyên',
      'Tuyên Quang',
    ],
  },
  {
    name: 'Miền Trung',
    provinces: ['Đà Nẵng', 'Huế', 'Quảng Ngãi', 'Quảng Trị'],
  },
  {
    name: 'Miền Nam',
    provinces: [
      'Hồ Chí Minh',
      'An Giang',
      'Cà Mau',
      'Cần Thơ',
      'Đắk Lắk',
      'Đồng Nai',
      'Đồng Tháp',
      'Gia Lai',
      'Khánh Hòa',
      'Lâm Đồng',
      'Tây Ninh',
      'Vĩnh Long',
    ],
  },
] as const;

export function ShippingFeePolicyPage() {
  return (
    <main className="shipping-policy-page">
      <div className="shipping-policy-container">
        <nav className="shipping-policy-crumbs" aria-label="Đường dẫn">
          <Link to="/">Trang chủ</Link>
          <span aria-hidden="true">/</span>
          <span aria-current="page">Chính sách phí giao hàng</span>
        </nav>

        <article className="card shipping-policy-article">
          <header className="shipping-policy-header">
            <p className="shipping-policy-eyebrow">Chính sách mua hàng</p>
            <h1>Chính sách phí giao hàng</h1>
            <p className="shipping-policy-lead">
              Phí giao hàng phụ thuộc vào nơi nhận, khối lượng và kích thước của kiện hàng. Bạn thấy
              số tiền cụ thể ở bước đặt hàng, sau khi chọn địa chỉ.
            </p>
            <div className="shipping-policy-summary">
              <p>Phí được tính theo tuyến giao và khối lượng tính cước.</p>
              <p>Đơn có tiền hàng từ {FREE_SHIPPING_THRESHOLD_LABEL} được miễn phí giao hàng.</p>
            </div>
          </header>

          <nav className="shipping-policy-toc" aria-label="Mục lục">
            <a href="#cach-tinh">Cách tính phí</a>
            <a href="#khu-vuc">Phí theo khu vực</a>
            <a href="#khoi-luong">Khối lượng tính cước</a>
            <a href="#mien-phi">Miễn phí giao hàng</a>
            <a href="#luu-y">Lưu ý</a>
          </nav>

          <section
            className="shipping-policy-section"
            id="cach-tinh"
            aria-labelledby="cach-tinh-title"
          >
            <h2 id="cach-tinh-title">Cách tính phí</h2>
            <ol>
              <li>Hệ thống so tỉnh, thành nhận hàng với tỉnh, thành của kho gửi để chọn tuyến.</li>
              <li>Hệ thống tính khối lượng tính cước cho một kiện chứa toàn bộ món trong đơn.</li>
              <li>Giá gốc của tuyến đã gồm một mức khối lượng gốc.</li>
              <li>Mỗi 500 g vượt khối lượng gốc được cộng một lần giá bước.</li>
              <li>Nếu tiền hàng đạt mức miễn phí, Pickory trừ toàn bộ phí khỏi đơn.</li>
            </ol>
            <p>
              Phí hiện trong phần tóm tắt đơn hàng sau khi bạn chọn địa chỉ. Hệ thống tính lại phí
              khi địa chỉ hoặc giỏ hàng thay đổi.
            </p>
          </section>

          <section className="shipping-policy-section" id="khu-vuc" aria-labelledby="khu-vuc-title">
            <h2 id="khu-vuc-title">Phí theo khu vực</h2>
            <p>
              Tuyến được xác định bằng cách so tỉnh, thành nhận hàng với tỉnh, thành của kho gửi. Có
              ba tuyến:
            </p>
            <div className="table-wrap shipping-policy-table-wrap">
              <table className="tbl shipping-policy-table">
                <thead>
                  <tr>
                    <th scope="col">Tuyến</th>
                    <th scope="col">Cách xác định</th>
                  </tr>
                </thead>
                <tbody>
                  {FEE_ROUTES.map((route) => (
                    <tr key={route.name}>
                      <th scope="row">{route.name}</th>
                      <td>{route.description}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <h3>Các tỉnh, thành theo vùng</h3>
            <p>
              Bảng dưới chia tỉnh, thành theo ba vùng. Danh sách theo đơn vị hành chính từ ngày
              1/7/2025.
            </p>
            <div className="table-wrap shipping-policy-table-wrap">
              <table className="tbl shipping-policy-table">
                <thead>
                  <tr>
                    <th scope="col">Vùng</th>
                    <th scope="col">Tỉnh, thành</th>
                  </tr>
                </thead>
                <tbody>
                  {REGION_GROUPS.map((region) => (
                    <tr key={region.name}>
                      <th scope="row">
                        <span className="shipping-policy-region-name">{region.name}</span>
                        <span className="shipping-policy-region-count">
                          {region.provinces.length} tỉnh, thành
                        </span>
                      </th>
                      <td>{region.provinces.join(', ')}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <p>
              Cách chia này theo biểu phí vận chuyển, không theo địa lý. Mỗi tuyến có giá gốc và giá
              bước riêng. Số tiền cụ thể do biểu phí tại thời điểm đặt hàng quyết định.
            </p>
          </section>

          <section
            className="shipping-policy-section"
            id="khoi-luong"
            aria-labelledby="khoi-luong-title"
          >
            <h2 id="khoi-luong-title">Khối lượng tính cước</h2>
            <p>
              Đơn vị vận chuyển tính phí theo khối lượng lớn hơn giữa khối lượng thật và khối lượng
              thể tích. Cách tính như sau:
            </p>
            <dl className="shipping-policy-definitions">
              <div className="shipping-policy-definition">
                <dt>Khối lượng thật</dt>
                <dd>Tổng khối lượng của các món, nhân với số lượng.</dd>
              </div>
              <div className="shipping-policy-definition">
                <dt>Khối lượng thể tích</dt>
                <dd>Dài x rộng x cao chia 5. Kích thước tính bằng cm, kết quả tính bằng gam.</dd>
              </div>
              <div className="shipping-policy-definition">
                <dt>Khối lượng tính cước</dt>
                <dd>
                  Số lớn hơn giữa khối lượng thật và khối lượng thể tích, làm tròn lên bội số 500 g.
                </dd>
              </div>
            </dl>

            <p className="shipping-policy-formula">Phí = giá gốc + số bước x giá bước</p>
            <p>
              Số bước là phần khối lượng vượt khối lượng gốc chia 500 g, làm tròn lên. Phần lẻ cũng
              tính một bước.
            </p>

            <div className="shipping-policy-example">
              <h3>Ví dụ</h3>
              <p>Kiện có khối lượng 1.200 g, hộp 30 x 20 x 10 cm.</p>
              <p>Khối lượng thể tích là 30 x 20 x 10 / 5 = 1.200 g.</p>
              <p>Khối lượng tính cước là 1.500 g, vì 1.200 g được làm tròn lên bội số 500 g.</p>
              <p>
                Nếu khối lượng gốc của tuyến là 1.200 g, phần vượt 300 g được tính một bước 500 g.
              </p>
            </div>

            <p>
              Khi đơn có nhiều món, khối lượng và chiều cao cộng dồn. Chiều dài và chiều rộng lấy
              giá trị lớn nhất trong các món.
            </p>
          </section>

          <section
            className="shipping-policy-section"
            id="mien-phi"
            aria-labelledby="mien-phi-title"
          >
            <h2 id="mien-phi-title">Miễn phí giao hàng</h2>
            <p>Mức miễn phí hiện tại là {FREE_SHIPPING_THRESHOLD_LABEL}.</p>
            <p>Pickory miễn phí giao hàng cho đơn có tiền hàng đạt mức này.</p>
            <p>
              Tiền hàng là tổng giá sản phẩm, chưa gồm phí giao hàng. Khi đủ mức, Pickory trừ toàn
              bộ phí của tuyến. Bạn chỉ trả tiền hàng.
            </p>
          </section>

          <section className="shipping-policy-section" id="luu-y" aria-labelledby="luu-y-title">
            <h2 id="luu-y-title">Lưu ý</h2>
            <ul>
              <li>Phí giao hàng không đổi theo phương thức thanh toán.</li>
              <li>Pickory không cộng thêm phí đóng gói.</li>
              <li>
                Nếu số đo thực tế khi gửi khác số đo lúc báo phí, Pickory chịu phần chênh lệch.
              </li>
              <li>
                Nếu đơn vị vận chuyển không nhận địa chỉ, bạn thấy thông báo để chọn địa chỉ khác.
              </li>
            </ul>
          </section>

          <section className="shipping-policy-section" aria-labelledby="kiem-tra-title">
            <h2 id="kiem-tra-title">Kiểm tra phí cho đơn của bạn</h2>
            <p>
              Thêm sản phẩm vào giỏ. Mở trang đặt hàng. Chọn địa chỉ nhận. Phần tóm tắt hiện phí và
              tổng tiền trước khi bạn xác nhận.
            </p>
            <div className="shipping-policy-actions">
              <Link to="/cart" className="btn btn-primary">
                Xem giỏ hàng
              </Link>
            </div>
          </section>
        </article>
      </div>
    </main>
  );
}
