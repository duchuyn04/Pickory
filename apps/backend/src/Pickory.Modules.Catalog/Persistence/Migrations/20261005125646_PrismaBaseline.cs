using Microsoft.EntityFrameworkCore.Migrations;

namespace Pickory.Modules.Catalog.Persistence.Migrations;

public partial class PrismaBaseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Dùng `AlterDatabase` để Npgsql nạp lại cache kiểu sau khi tạo enum. Giữ thứ tự nhãn theo schema mốc.
        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:Enum:InventoryMovementType", "manual,order_reserve,order_release,return")
            .Annotation("Npgsql:Enum:ProductStatus", "draft,active,hidden,discontinued");

        // Giữ cấu trúc `brands` theo schema mốc. Khóa `slug` duy nhất ngăn hai thương hiệu dùng cùng đường dẫn.
        migrationBuilder.Sql("""
            CREATE TABLE public.brands (
                id text NOT NULL,
                name text NOT NULL,
                slug text NOT NULL,
                "logoPublicId" text,
                "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
                "updatedAt" timestamp(3) without time zone NOT NULL
            );

            ALTER TABLE ONLY public.brands
                ADD CONSTRAINT brands_pkey PRIMARY KEY (id);

            CREATE UNIQUE INDEX brands_slug_key ON public.brands USING btree (slug);
            """);

        // Giữ `parentId` nullable trong `categories` để danh mục gốc không phải tham chiếu một danh mục khác.
        migrationBuilder.Sql("""
            CREATE TABLE public.categories (
                id text NOT NULL,
                "parentId" text,
                name text NOT NULL,
                slug text NOT NULL,
                "position" integer DEFAULT 0 NOT NULL,
                attributes jsonb NOT NULL,
                "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
                "updatedAt" timestamp(3) without time zone NOT NULL
            );

            ALTER TABLE ONLY public.categories
                ADD CONSTRAINT categories_pkey PRIMARY KEY (id);

            CREATE UNIQUE INDEX categories_slug_key ON public.categories USING btree (slug);
            """);

        // Giữ cấu trúc `image_deletion_jobs` theo schema mốc để giá trị mặc định và index retry không đổi.
        migrationBuilder.Sql("""
            CREATE TABLE public.image_deletion_jobs (
                id text NOT NULL,
                "publicId" text NOT NULL,
                status text DEFAULT 'pending'::text NOT NULL,
                attempts integer DEFAULT 0 NOT NULL,
                "maxAttempts" integer DEFAULT 8 NOT NULL,
                "nextRetryAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
                "lastError" text,
                "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
                "updatedAt" timestamp(3) without time zone NOT NULL
            );

            ALTER TABLE ONLY public.image_deletion_jobs
                ADD CONSTRAINT image_deletion_jobs_pkey PRIMARY KEY (id);

            CREATE INDEX "image_deletion_jobs_status_nextRetryAt_idx" ON public.image_deletion_jobs USING btree (status, "nextRetryAt");
            """);

        // Giữ cấu trúc `products` theo schema mốc. Index dùng `gin` giữ khả năng tìm kiếm thuộc tính trong `specs`.
        migrationBuilder.Sql("""
            CREATE TABLE public.products (
                id text NOT NULL,
                "categoryId" text NOT NULL,
                "brandId" text NOT NULL,
                name text NOT NULL,
                slug text NOT NULL,
                "shortDesc" text NOT NULL,
                description jsonb NOT NULL,
                specs jsonb NOT NULL,
                options jsonb NOT NULL,
                "warrantyMonths" integer DEFAULT 12 NOT NULL,
                status public."ProductStatus" DEFAULT 'draft'::public."ProductStatus" NOT NULL,
                "minPrice" integer,
                "inStock" boolean DEFAULT false NOT NULL,
                "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
                "updatedAt" timestamp(3) without time zone NOT NULL
            );

            ALTER TABLE ONLY public.products
                ADD CONSTRAINT products_pkey PRIMARY KEY (id);

            CREATE INDEX "products_categoryId_status_idx" ON public.products USING btree ("categoryId", status);
            CREATE INDEX "products_createdAt_idx" ON public.products USING btree ("createdAt");
            CREATE INDEX "products_minPrice_idx" ON public.products USING btree ("minPrice");
            CREATE UNIQUE INDEX products_slug_key ON public.products USING btree (slug);
            CREATE INDEX products_specs_gin_idx ON public.products USING gin (specs);
            """);

        // Giữ cấu trúc `skus` theo schema mốc. Ràng buộc `skus_stock_non_negative` ngăn lưu tồn kho âm.
        migrationBuilder.Sql("""
            CREATE TABLE public.skus (
                id text NOT NULL,
                "productId" text NOT NULL,
                code text NOT NULL,
                "optionValues" jsonb NOT NULL,
                price integer NOT NULL,
                "compareAtPrice" integer,
                stock integer DEFAULT 0 NOT NULL,
                enabled boolean DEFAULT true NOT NULL,
                "weightGrams" integer,
                "lengthCm" integer,
                "widthCm" integer,
                "heightCm" integer,
                "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
                "updatedAt" timestamp(3) without time zone NOT NULL,
                CONSTRAINT skus_stock_non_negative CHECK ((stock >= 0))
            );

            ALTER TABLE ONLY public.skus
                ADD CONSTRAINT skus_pkey PRIMARY KEY (id);

            CREATE UNIQUE INDEX skus_code_key ON public.skus USING btree (code);
            CREATE INDEX "skus_productId_idx" ON public.skus USING btree ("productId");
            """);

        // Giữ `skuId` nullable trong `product_images` để ảnh có thể thuộc sản phẩm mà không thuộc biến thể.
        migrationBuilder.Sql("""
            CREATE TABLE public.product_images (
                id text NOT NULL,
                "productId" text NOT NULL,
                "skuId" text,
                "publicId" text NOT NULL,
                alt text NOT NULL,
                "position" integer DEFAULT 0 NOT NULL,
                "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
            );

            ALTER TABLE ONLY public.product_images
                ADD CONSTRAINT product_images_pkey PRIMARY KEY (id);

            CREATE INDEX "product_images_productId_position_idx" ON public.product_images USING btree ("productId", "position");
            """);

        // Giữ khóa `oldSlug` duy nhất của `product_slug_history` để đường dẫn cũ không trỏ tới hai sản phẩm.
        migrationBuilder.Sql("""
            CREATE TABLE public.product_slug_history (
                id text NOT NULL,
                "oldSlug" text NOT NULL,
                "productId" text NOT NULL,
                "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
            );

            ALTER TABLE ONLY public.product_slug_history
                ADD CONSTRAINT product_slug_history_pkey PRIMARY KEY (id);

            CREATE UNIQUE INDEX "product_slug_history_oldSlug_key" ON public.product_slug_history USING btree ("oldSlug");
            CREATE INDEX "product_slug_history_productId_idx" ON public.product_slug_history USING btree ("productId");
            """);

        // Giữ khóa ghép của `inventory_movements` để cùng đơn hàng không ghi trùng loại biến động cho một biến thể.
        migrationBuilder.Sql("""
            CREATE TABLE public.inventory_movements (
                id text NOT NULL,
                "skuId" text NOT NULL,
                delta integer NOT NULL,
                type public."InventoryMovementType" NOT NULL,
                "orderId" text,
                "actorId" text,
                reason text NOT NULL,
                "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
            );

            ALTER TABLE ONLY public.inventory_movements
                ADD CONSTRAINT inventory_movements_pkey PRIMARY KEY (id);

            CREATE UNIQUE INDEX "inventory_movements_orderId_skuId_type_key" ON public.inventory_movements USING btree ("orderId", "skuId", type);
            CREATE INDEX "inventory_movements_skuId_createdAt_idx" ON public.inventory_movements USING btree ("skuId", "createdAt");
            """);

        // Tạo khóa ngoại sau bảng gốc để tham chiếu hợp lệ. Giữ hành vi cập nhật và xóa theo schema mốc.
        migrationBuilder.Sql("""
            ALTER TABLE ONLY public.categories
                ADD CONSTRAINT "categories_parentId_fkey" FOREIGN KEY ("parentId") REFERENCES public.categories(id) ON UPDATE CASCADE ON DELETE RESTRICT;

            ALTER TABLE ONLY public.products
                ADD CONSTRAINT "products_brandId_fkey" FOREIGN KEY ("brandId") REFERENCES public.brands(id) ON UPDATE CASCADE ON DELETE RESTRICT;

            ALTER TABLE ONLY public.products
                ADD CONSTRAINT "products_categoryId_fkey" FOREIGN KEY ("categoryId") REFERENCES public.categories(id) ON UPDATE CASCADE ON DELETE RESTRICT;

            ALTER TABLE ONLY public.skus
                ADD CONSTRAINT "skus_productId_fkey" FOREIGN KEY ("productId") REFERENCES public.products(id) ON UPDATE CASCADE ON DELETE CASCADE;

            ALTER TABLE ONLY public.product_images
                ADD CONSTRAINT "product_images_productId_fkey" FOREIGN KEY ("productId") REFERENCES public.products(id) ON UPDATE CASCADE ON DELETE CASCADE;

            ALTER TABLE ONLY public.product_images
                ADD CONSTRAINT "product_images_skuId_fkey" FOREIGN KEY ("skuId") REFERENCES public.skus(id) ON UPDATE CASCADE ON DELETE SET NULL;

            ALTER TABLE ONLY public.product_slug_history
                ADD CONSTRAINT "product_slug_history_productId_fkey" FOREIGN KEY ("productId") REFERENCES public.products(id) ON UPDATE CASCADE ON DELETE CASCADE;

            ALTER TABLE ONLY public.inventory_movements
                ADD CONSTRAINT "inventory_movements_skuId_fkey" FOREIGN KEY ("skuId") REFERENCES public.skus(id) ON UPDATE CASCADE ON DELETE RESTRICT;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Xóa các bảng catalog theo thứ tự phụ thuộc khóa ngoại từ bảng phụ đến bảng chính.
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS public.inventory_movements;
            DROP TABLE IF EXISTS public.product_images;
            DROP TABLE IF EXISTS public.product_slug_history;
            DROP TABLE IF EXISTS public.skus;
            DROP TABLE IF EXISTS public.products;
            DROP TABLE IF EXISTS public.categories;
            DROP TABLE IF EXISTS public.brands;
            DROP TABLE IF EXISTS public.image_deletion_jobs;
            """);

        // Gỡ enum sau bảng sử dụng để `Down` không thất bại do đối tượng còn phụ thuộc.
        migrationBuilder.Sql("""
            DROP TYPE IF EXISTS public."InventoryMovementType";
            DROP TYPE IF EXISTS public."ProductStatus";
            """);
    }
}
