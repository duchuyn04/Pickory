import { describe, expect, it } from 'vitest';
import { db } from '../../../../src/shared/db';

describe('ràng buộc cơ sở dữ liệu catalog', () => {
  it('từ chối cập nhật stock thành số âm theo ràng buộc CHECK', async () => {
    // Tạo danh mục mẫu để liên kết khóa ngoại của sản phẩm.
    const category = await db.category.create({
      data: {
        name: 'Laptop',
        slug: 'laptop',
        attributes: [],
      },
    });

    // Tạo thương hiệu mẫu để liên kết khóa ngoại của sản phẩm.
    const brand = await db.brand.create({
      data: {
        name: 'ASUS',
        slug: 'asus',
      },
    });

    // Tạo sản phẩm mẫu để chứa biến thể SKU.
    const product = await db.product.create({
      data: {
        categoryId: category.id,
        brandId: brand.id,
        name: 'ASUS ROG Strix',
        slug: 'asus-rog-strix',
        shortDesc: 'Laptop gaming cao cấp',
        description: { type: 'doc', content: [] },
        specs: { ram_gb: 16 },
        options: [],
        minPrice: 20000000,
        inStock: true,
      },
    });

    // Tạo biến thể hợp lệ với tồn kho ban đầu lớn hơn hoặc bằng 0.
    const sku = await db.sku.create({
      data: {
        productId: product.id,
        code: 'ROG-STRIX-001',
        optionValues: {},
        price: 20000000,
        stock: 5,
      },
    });

    // Thử cập nhật stock thành số âm và kỳ vọng cơ sở dữ liệu từ chối.
    await expect(
      db.sku.update({
        where: { id: sku.id },
        data: { stock: -1 },
      }),
    ).rejects.toThrow();
  });

  it('từ chối hai bản ghi biến động tồn kho trùng orderId, skuId và type', async () => {
    // Tạo dữ liệu danh mục ban đầu cho sản phẩm.
    const category = await db.category.create({
      data: {
        name: 'Điện thoại',
        slug: 'dien-thoai',
        attributes: [],
      },
    });

    // Tạo dữ liệu thương hiệu ban đầu cho sản phẩm.
    const brand = await db.brand.create({
      data: {
        name: 'Apple',
        slug: 'apple',
      },
    });

    // Tạo sản phẩm ban đầu cho biến thể.
    const product = await db.product.create({
      data: {
        categoryId: category.id,
        brandId: brand.id,
        name: 'iPhone 15',
        slug: 'iphone-15',
        shortDesc: 'Điện thoại thông minh',
        description: { type: 'doc', content: [] },
        specs: {},
        options: [],
      },
    });

    // Tạo biến thể SKU để ghi nhận biến động tồn kho.
    const sku = await db.sku.create({
      data: {
        productId: product.id,
        code: 'IPHONE-15-128',
        optionValues: {},
        price: 22000000,
        stock: 10,
      },
    });

    const movementData = {
      skuId: sku.id,
      delta: -1,
      type: 'order_reserve' as const,
      orderId: 'order-test-uuid',
      reason: 'Giữ hàng cho đơn hàng',
    };

    // Tạo bản ghi biến động tồn kho lần đầu thành công.
    await db.inventoryMovement.create({ data: movementData });

    // Tạo bản ghi thứ hai trùng bộ ba khóa và kỳ vọng cơ sở dữ liệu từ chối.
    await expect(db.inventoryMovement.create({ data: movementData })).rejects.toThrow();
  });

  it('xác nhận chỉ mục GIN trên cột specs của bảng products tồn tại', async () => {
    // Tra cứu danh sách chỉ mục trên bảng products để kiểm tra loại chỉ mục GIN.
    const indexRows = await db.$queryRaw<Array<{ indexname: string; indexdef: string }>>`
      SELECT indexname, indexdef
      FROM pg_indexes
      WHERE tablename = 'products' AND indexname = 'products_specs_gin_idx'
    `;

    expect(indexRows).toHaveLength(1);
    expect(indexRows[0]?.indexdef).toContain('USING gin');
  });

  it('xác nhận các chỉ mục bổ sung trên bảng products và skus tồn tại', async () => {
    const productIndexes = await db.$queryRaw<Array<{ indexname: string }>>`
      SELECT indexname
      FROM pg_indexes
      WHERE tablename = 'products' AND indexname IN ('products_minPrice_idx', 'products_createdAt_idx')
    `;
    expect(productIndexes).toHaveLength(2);

    const skuIndexes = await db.$queryRaw<Array<{ indexname: string }>>`
      SELECT indexname
      FROM pg_indexes
      WHERE tablename = 'skus' AND indexname = 'skus_productId_idx'
    `;
    expect(skuIndexes).toHaveLength(1);
  });

  it('từ chối xóa sản phẩm khi SKU của sản phẩm vẫn còn bản ghi biến động tồn kho', async () => {
    // Tạo danh mục mẫu để liên kết khóa ngoại của sản phẩm.
    const category = await db.category.create({
      data: {
        name: 'Màn hình',
        slug: 'man-hinh',
        attributes: [],
      },
    });

    // Tạo thương hiệu mẫu để liên kết khóa ngoại của sản phẩm.
    const brand = await db.brand.create({
      data: {
        name: 'LG',
        slug: 'lg',
      },
    });

    // Tạo sản phẩm mẫu để chứa biến thể SKU.
    const product = await db.product.create({
      data: {
        categoryId: category.id,
        brandId: brand.id,
        name: 'LG UltraGear',
        slug: 'lg-ultragear',
        shortDesc: 'Màn hình gaming',
        description: { type: 'doc', content: [] },
        specs: {},
        options: [],
      },
    });

    // Tạo biến thể SKU thuộc sản phẩm.
    const sku = await db.sku.create({
      data: {
        productId: product.id,
        code: 'LG-27GP850',
        optionValues: {},
        price: 9000000,
        stock: 5,
      },
    });

    // Tạo bản ghi biến động tồn kho nhập kho ban đầu cho SKU.
    await db.inventoryMovement.create({
      data: {
        skuId: sku.id,
        delta: 5,
        type: 'manual',
        reason: 'Nhập kho ban đầu',
      },
    });

    // Thử xóa sản phẩm: Cascade cố xóa Sku nhưng bị chặn bởi FK Restrict từ inventory_movements.
    await expect(
      db.product.delete({
        where: { id: product.id },
      }),
    ).rejects.toThrow();
  });
});
