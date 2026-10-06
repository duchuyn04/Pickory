import { expect, test, type Locator, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';
import { readPasswordSetupLink } from './outbox';

// Đo tràn ngang của cả trang. Ảnh chụp rộng hơn viewport nghĩa là người dùng phải cuộn ngang.
async function expectNoHorizontalPageOverflow(page: Page) {
  const pageWidths = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(pageWidths.scrollWidth).toBeLessThanOrEqual(pageWidths.clientWidth);
}

// Đếm số dòng chữ theo chiều cao và `line-height`. Khối đầu trang ép hẹp làm tiêu đề ngắt từng chữ.
async function expectAtMostTextLines(target: Locator, maxLines: number) {
  const lineCount = await target.evaluate((element) => {
    const style = getComputedStyle(element);
    // Nút có `line-height: normal`. Trình duyệt dựng dòng cao khoảng 1.2 lần `font-size`.
    const declaredLineHeight = Number.parseFloat(style.lineHeight);
    const lineHeight = Number.isNaN(declaredLineHeight)
      ? Number.parseFloat(style.fontSize) * 1.2
      : declaredLineHeight;
    const verticalPadding =
      Number.parseFloat(style.paddingTop) + Number.parseFloat(style.paddingBottom);
    return Math.round((element.getBoundingClientRect().height - verticalPadding) / lineHeight);
  });
  expect(lineCount).toBeLessThanOrEqual(maxLines);
}

test('Luồng UI-C4: quản lý danh mục, bộ thuộc tính và thương hiệu', async ({ page }, testInfo) => {
  test.setTimeout(90000);
  // Ghi nhận nhật ký console và lỗi trang để kiểm tra không có lỗi hoặc cảnh báo nào.
  const consoleIssues: string[] = [];
  page.on('console', (msg) => {
    if (msg.type() === 'error' || msg.type() === 'warning') {
      consoleIssues.push(`[${msg.type()}] ${msg.text()}`);
    }
  });
  page.on('pageerror', (err) => {
    consoleIssues.push(`[pageerror] ${err.message}`);
  });

  await test.step('Admin đăng nhập vào hệ thống', async () => {
    // Đăng nhập bằng tài khoản quản trị viên có sẵn trong môi trường E2E.
    await page.goto('/admin/login');
    const cookieButton = page.getByRole('button', { name: 'Chỉ cần thiết', exact: true });
    if (await cookieButton.isVisible()) {
      await cookieButton.click();
    }
    await page.getByLabel(/^Email/).fill(e2eEnvironment.adminEmail);
    await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.adminPassword);
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
  });

  await test.step('Tạo danh mục gốc và danh mục con có thuộc tính enum', async () => {
    // Điều hướng vào trang quản lý danh mục qua menu thanh bên.
    await page.getByRole('link', { name: 'Danh mục', exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/categories`);

    // Tạo danh mục gốc đầu tiên.
    await page.getByRole('button', { name: '+ Thêm danh mục gốc', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await dialog.getByLabel('Tên danh mục *', { exact: true }).fill('Điện tử E2E');
    await dialog.getByLabel('Thứ tự hiển thị (position)', { exact: true }).fill('1');
    await dialog.getByRole('button', { name: 'Tạo danh mục', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByText('Điện tử E2E')).toBeVisible();

    // Tạo danh mục con có thuộc tính kiểu enum kèm danh sách lựa chọn.
    await page.getByRole('button', { name: 'Thêm con cho Điện tử E2E', exact: true }).click();
    await expect(dialog).toBeVisible();
    await dialog.getByLabel('Tên danh mục *', { exact: true }).fill('Điện thoại E2E');
    await dialog.getByRole('button', { name: '+ Thêm thuộc tính', exact: true }).click();
    await dialog.getByLabel('Khóa (key) *', { exact: true }).fill('color');
    await dialog.getByLabel('Nhãn *', { exact: true }).fill('Màu sắc');
    await dialog.getByLabel('Nhóm *', { exact: true }).fill('Thiết kế');
    await dialog.getByLabel('Kiểu *', { exact: true }).selectOption('enum');
    await dialog.getByLabel(/Danh sách options/).pressSequentially('Đỏ, Xanh, Vàng');
    await dialog.getByRole('button', { name: 'Tạo danh mục', exact: true }).click();
    await expect(dialog).toBeHidden();

    // Tải lại trang để kiểm tra dữ liệu thuộc tính được lưu trữ bền vững.
    await page.reload();
    await expect(page.getByText('Điện thoại E2E')).toBeVisible();
    await expect(page.getByText(/1 thuộc tính riêng/)).toBeVisible();
  });

  await test.step('Tạo thương hiệu mới bằng giao diện quản trị', async () => {
    // Chuyển sang màn hình quản lý thương hiệu qua menu.
    await page.getByRole('link', { name: 'Thương hiệu', exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/brands`);

    // Mở hộp thoại và tạo thương hiệu mới.
    await page.getByRole('button', { name: '+ Thêm thương hiệu', exact: true }).click();
    const brandDialog = page.getByRole('dialog');
    await expect(brandDialog).toBeVisible();
    await brandDialog.getByLabel('Tên thương hiệu *', { exact: true }).fill('VinSmart E2E');
    await brandDialog.getByLabel(/Mã logo Cloudinary/).fill('pickory/brands/vinsmart');
    await brandDialog.getByRole('button', { name: 'Tạo thương hiệu', exact: true }).click();
    await expect(brandDialog).toBeHidden();
    await expect(page.getByText('VinSmart E2E')).toBeVisible();
  });

  await test.step('Tạo sản phẩm sử dụng thuộc tính enum và kiểm tra chặn xóa thuộc tính', async () => {
    // Giao diện tạo sản phẩm thuộc nhiệm vụ UI-C5. Ở đây gọi API trực tiếp để tạo sản phẩm liên kết.
    const categoriesResponse = await page.request.get('/api/admin/categories');
    expect(categoriesResponse.ok()).toBe(true);
    const categoriesJson = await categoriesResponse.json();
    const childCategory = categoriesJson.data.find(
      (cat: { name: string }) => cat.name === 'Điện thoại E2E',
    );
    expect(childCategory).toBeDefined();

    const brandsResponse = await page.request.get('/api/brands');
    expect(brandsResponse.ok()).toBe(true);
    const brandsJson = await brandsResponse.json();
    const brandItem = brandsJson.data.find(
      (brand: { name: string }) => brand.name === 'VinSmart E2E',
    );
    expect(brandItem).toBeDefined();

    // Tạo sản phẩm mẫu có sử dụng thuộc tính color vừa tạo.
    const productResponse = await page.request.post('/api/admin/products', {
      headers: {
        Origin: e2eEnvironment.webOrigin,
      },
      data: {
        categoryId: childCategory.id,
        brandId: brandItem.id,
        name: 'VinSmart Live 4 E2E',
        shortDesc: 'Điện thoại thông minh',
        description: {
          type: 'doc',
          content: [
            {
              type: 'paragraph',
              content: [{ type: 'text', text: 'Mô tả chi tiết sản phẩm thử nghiệm.' }],
            },
          ],
        },
        specs: { color: 'Đỏ' },
        options: [],
        variants: [
          {
            optionValues: {},
            price: 3500000,
            enabled: true,
          },
        ],
      },
    });
    expect(productResponse.status()).toBe(201);

    // Quay lại màn hình danh mục để thử xóa thuộc tính đang được sử dụng.
    await page.getByRole('link', { name: 'Danh mục', exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/categories`);
    await expect(page.getByText('Điện thoại E2E')).toBeVisible();

    await page.getByRole('button', { name: 'Sửa Điện thoại E2E', exact: true }).click();

    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();

    // Bấm nút xóa thuộc tính Màu sắc đang được sản phẩm sử dụng.
    await dialog.getByRole('button', { name: 'Xóa thuộc tính Màu sắc', exact: true }).click();
    await dialog.getByRole('button', { name: 'Lưu thay đổi', exact: true }).click();

    // Hộp thoại báo lỗi ATTRIBUTE_IN_USE hiển thị bằng tiếng Việt trong vùng role alert.
    const alert = dialog.getByRole('alert');
    await expect(alert).toBeVisible();
    await expect(alert).toContainText(
      'Thuộc tính đang được sản phẩm sử dụng nên không thể xóa hoặc đổi kiểu dữ liệu.',
    );

    // Đóng hộp thoại và xác nhận dữ liệu không bị xóa nhầm.
    await dialog.getByRole('button', { name: 'Đóng', exact: true }).click();
    await expect(dialog).toBeHidden();

    // Chuyển sang màn hình thương hiệu và thử xóa thương hiệu VinSmart E2E đang có sản phẩm.
    await page.getByRole('link', { name: 'Thương hiệu', exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/brands`);
    await expect(page.getByText('VinSmart E2E')).toBeVisible();

    page.once('dialog', (confirmDialog) => confirmDialog.accept());
    await page.getByRole('button', { name: 'Xóa VinSmart E2E', exact: true }).click();

    // Kiểm tra lỗi BRAND_HAS_PRODUCTS hiển thị bằng tiếng Việt trong vùng role alert.
    const brandAlert = page.getByRole('alert');
    await expect(brandAlert).toBeVisible();
    await expect(brandAlert).toContainText('Thương hiệu đang có sản phẩm, không thể xóa.');

    // Chụp giao diện danh mục và hộp thoại ở chế độ admin trên thiết bị di động (375px).
    await page.getByRole('link', { name: 'Danh mục', exact: true }).click();
    await page.setViewportSize({ width: 375, height: 812 });
    await expect(
      page.getByRole('button', { name: 'Sửa Điện thoại E2E', exact: true }),
    ).toBeVisible();
    await expectNoHorizontalPageOverflow(page);
    await expectAtMostTextLines(page.getByRole('heading', { level: 1 }), 2);
    await expectAtMostTextLines(
      page.getByRole('button', { name: '+ Thêm danh mục gốc', exact: true }),
      1,
    );
    await page.screenshot({
      path: testInfo.outputPath('admin-categories-mobile.png'),
      fullPage: true,
    });

    await page.getByRole('button', { name: 'Sửa Điện thoại E2E', exact: true }).click();
    await expect(dialog).toBeVisible();

    // Hộp thoại và vùng cuộn bên trong không được rộng hơn khung. Tràn ngang làm ô nhập bị cắt.
    const dialogOverflow = await dialog.evaluate((dialogElement) => {
      const scrollBody = dialogElement.querySelector('form')?.parentElement;
      return [dialogElement, scrollBody].map((element) => ({
        scrollWidth: element?.scrollWidth ?? 0,
        clientWidth: element?.clientWidth ?? 0,
      }));
    });
    for (const widths of dialogOverflow) {
      expect(widths.scrollWidth).toBeLessThanOrEqual(widths.clientWidth);
    }

    // Ô chọn "Kiểu" phải nằm trọn trong hộp thoại để admin đọc được giá trị đã chọn.
    const dialogBox = await dialog.boundingBox();
    const typeSelectBox = await dialog.getByLabel('Kiểu *', { exact: true }).boundingBox();
    expect(dialogBox).not.toBeNull();
    expect(typeSelectBox).not.toBeNull();
    if (dialogBox && typeSelectBox) {
      expect(typeSelectBox.x).toBeGreaterThanOrEqual(dialogBox.x);
      expect(typeSelectBox.x + typeSelectBox.width).toBeLessThanOrEqual(
        dialogBox.x + dialogBox.width,
      );
    }

    // Lưới cũ dồn ô vào cột 40px mà không tràn hộp thoại. Ô hẹp hơn 120px không đọc được giá trị.
    for (const fieldLabel of ['Khóa (key) *', 'Nhãn *', 'Nhóm *', 'Kiểu *', 'Đơn vị']) {
      const fieldBox = await dialog.getByLabel(fieldLabel, { exact: true }).boundingBox();
      expect(fieldBox?.width ?? 0, `Độ rộng ô ${fieldLabel}`).toBeGreaterThanOrEqual(120);
    }

    await page.screenshot({
      path: testInfo.outputPath('admin-categories-mobile-dialog.png'),
      fullPage: true,
    });
    await dialog.getByRole('button', { name: 'Đóng', exact: true }).click();
    await expect(dialog).toBeHidden();

    // Chụp màn hình thương hiệu ở chế độ admin trên thiết bị di động (375px).
    await page.getByRole('link', { name: 'Thương hiệu', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Sửa VinSmart E2E', exact: true })).toBeVisible();
    await expectNoHorizontalPageOverflow(page);
    await expectAtMostTextLines(page.getByRole('heading', { level: 1 }), 2);
    await expectAtMostTextLines(
      page.getByRole('button', { name: '+ Thêm thương hiệu', exact: true }),
      1,
    );
    await page.screenshot({
      path: testInfo.outputPath('admin-brands-mobile.png'),
      fullPage: true,
    });
  });

  await test.step('Mời tài khoản sales riêng và kiểm tra phân quyền xem danh sách', async () => {
    // Mời một tài khoản nhân viên bán hàng mới qua màn hình quản lý nhân viên.
    await page.getByRole('link', { name: 'Nhân viên', exact: true }).click();
    await page.getByRole('button', { name: '+ Mời nhân viên', exact: true }).click();
    const invitationDialog = page.getByRole('dialog', {
      name: 'Mời nhân viên mới',
      exact: true,
    });
    const salesEmail = e2eEnvironment.catalogSalesEmail;
    await invitationDialog.getByLabel(/^Email nhân viên/).fill(salesEmail);
    await invitationDialog.getByLabel(/^Họ và tên/).fill('Sales Catalog E2E');
    await invitationDialog.getByRole('checkbox', { name: /\(sales\)$/ }).check();
    await invitationDialog.getByRole('button', { name: 'Gửi lời mời', exact: true }).click();
    await expect(invitationDialog).toBeHidden();

    // Đăng xuất phiên quản trị viên.
    await page.getByRole('button', { name: 'Đăng xuất', exact: true }).click();

    // Mở liên kết thiết lập mật khẩu từ hòm thư nội bộ.
    const setupLink = await readPasswordSetupLink(salesEmail);
    await page.goto(setupLink);
    await page
      .getByLabel('Mật khẩu mới (tối thiểu 8 ký tự)', { exact: true })
      .fill(e2eEnvironment.accountPassword);
    await page
      .getByLabel('Xác nhận mật khẩu mới', { exact: true })
      .fill(e2eEnvironment.accountPassword);
    await page.getByRole('button', { name: 'Cập nhật mật khẩu', exact: true }).click();
    await page.getByRole('link', { name: 'Đăng nhập trang quản trị', exact: true }).click();

    // Nhân viên sales đăng nhập bằng mật khẩu vừa khởi tạo.
    await page.getByLabel(/^Email/).fill(salesEmail);
    await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.accountPassword);
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();

    // Sales xem được danh mục nhưng không có quyền tạo, sửa, xóa.
    await page.getByRole('link', { name: 'Danh mục', exact: true }).click();
    await expect(page.getByText('Điện thoại E2E')).toBeVisible();
    await expect(page.getByRole('button', { name: '+ Thêm danh mục gốc' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: /Thêm con/ })).toHaveCount(0);
    await expect(page.getByRole('button', { name: /Sửa/ })).toHaveCount(0);
    await expect(page.getByRole('button', { name: /Xóa/ })).toHaveCount(0);

    // Sales xem được thương hiệu nhưng không có các nút thao tác chỉnh sửa.
    await page.getByRole('link', { name: 'Thương hiệu', exact: true }).click();
    await expect(page.getByText('VinSmart E2E')).toBeVisible();
    await expect(page.getByRole('button', { name: '+ Thêm thương hiệu' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: /Sửa/ })).toHaveCount(0);
    await expect(page.getByRole('button', { name: /Xóa/ })).toHaveCount(0);
  });

  await test.step('Chụp ảnh màn hình ở độ phân giải 1280 và 375', async () => {
    // Chụp trang danh mục ở chế độ máy tính để bàn (1280px).
    await page.getByRole('link', { name: 'Danh mục', exact: true }).click();
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.screenshot({
      path: testInfo.outputPath('categories-desktop.png'),
      fullPage: true,
    });

    // Chụp trang danh mục ở chế độ thiết bị di động (375px).
    await page.setViewportSize({ width: 375, height: 812 });
    await page.screenshot({
      path: testInfo.outputPath('categories-mobile.png'),
      fullPage: true,
    });

    // Chụp trang thương hiệu ở chế độ máy tính để bàn (1280px).
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.getByRole('link', { name: 'Thương hiệu', exact: true }).click();
    await page.screenshot({
      path: testInfo.outputPath('brands-desktop.png'),
      fullPage: true,
    });

    // Chụp trang thương hiệu ở chế độ thiết bị di động (375px).
    await page.setViewportSize({ width: 375, height: 812 });
    await page.screenshot({
      path: testInfo.outputPath('brands-mobile.png'),
      fullPage: true,
    });
  });

  // Lọc bỏ lỗi mạng 409 do bài test cố tình gây ra để kiểm tra xử lý lỗi xung đột.
  const unexpectedIssues = consoleIssues.filter(
    (issue) => !issue.includes('409 (Conflict)') && !issue.includes('400 (Bad Request)'),
  );
  expect(unexpectedIssues).toEqual([]);
});
