using Microsoft.EntityFrameworkCore.Migrations;

namespace Pickory.Modules.Identity.Persistence.Migrations;

public partial class PrismaBaseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Dùng `AlterDatabase` để Npgsql nạp lại cache kiểu sau khi tạo enum. Giữ thứ tự nhãn theo schema mốc.
        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:Enum:EmailTokenPurpose", "verify,reset,invite")
            .Annotation("Npgsql:Enum:UserKind", "customer,staff");

        // Giữ thứ tự cột của `users` theo schema mốc. Khóa email duy nhất ngăn hai tài khoản dùng cùng email.
        migrationBuilder.Sql("""
            CREATE TABLE public.users (
                id text NOT NULL,
                email text NOT NULL,
                "emailVerified" boolean DEFAULT false NOT NULL,
                "passwordHash" text,
                name text,
                phone text,
                kind public."UserKind" NOT NULL,
                "lockedAt" timestamp(3) without time zone,
                "deletedAt" timestamp(3) without time zone,
                "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
                "updatedAt" timestamp(3) without time zone NOT NULL
            );

            ALTER TABLE ONLY public.users
                ADD CONSTRAINT users_pkey PRIMARY KEY (id);

            CREATE UNIQUE INDEX users_email_key ON public.users USING btree (email);
            """);

        // Giữ cấu trúc `roles` theo schema mốc. Khóa `code` duy nhất ngăn vai trò trùng mã.
        migrationBuilder.Sql("""
            CREATE TABLE public.roles (
                id text NOT NULL,
                code text NOT NULL,
                name text NOT NULL,
                "isSystem" boolean DEFAULT false NOT NULL,
                "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
            );

            ALTER TABLE ONLY public.roles
                ADD CONSTRAINT roles_pkey PRIMARY KEY (id);

            CREATE UNIQUE INDEX roles_code_key ON public.roles USING btree (code);
            """);

        // Giữ khóa ghép của `role_permissions` để một vai trò không có hai bản ghi cho cùng quyền.
        migrationBuilder.Sql("""
            CREATE TABLE public.role_permissions (
                "roleId" text NOT NULL,
                permission text NOT NULL
            );

            ALTER TABLE ONLY public.role_permissions
                ADD CONSTRAINT role_permissions_pkey PRIMARY KEY ("roleId", permission);
            """);

        // Giữ khóa ghép của `user_roles` để một người dùng không được gán trùng cùng vai trò.
        migrationBuilder.Sql("""
            CREATE TABLE public.user_roles (
                "userId" text NOT NULL,
                "roleId" text NOT NULL
            );

            ALTER TABLE ONLY public.user_roles
                ADD CONSTRAINT user_roles_pkey PRIMARY KEY ("userId", "roleId");
            """);

        // Giữ cấu trúc `sessions` theo schema mốc. Khóa `tokenHash` duy nhất ngăn hai phiên dùng cùng token.
        migrationBuilder.Sql("""
            CREATE TABLE public.sessions (
                id text NOT NULL,
                "tokenHash" text NOT NULL,
                "userId" text NOT NULL,
                "expiresAt" timestamp(3) without time zone NOT NULL,
                "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
            );

            ALTER TABLE ONLY public.sessions
                ADD CONSTRAINT sessions_pkey PRIMARY KEY (id);

            CREATE UNIQUE INDEX "sessions_tokenHash_key" ON public.sessions USING btree ("tokenHash");
            CREATE INDEX "sessions_userId_idx" ON public.sessions USING btree ("userId");
            """);

        // Giữ khóa ghép của `oauth_accounts` để tài khoản từ cùng nhà cung cấp không được liên kết hai lần.
        migrationBuilder.Sql("""
            CREATE TABLE public.oauth_accounts (
                id text NOT NULL,
                provider text NOT NULL,
                "providerUserId" text NOT NULL,
                "userId" text NOT NULL,
                "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
            );

            ALTER TABLE ONLY public.oauth_accounts
                ADD CONSTRAINT oauth_accounts_pkey PRIMARY KEY (id);

            CREATE UNIQUE INDEX "oauth_accounts_provider_providerUserId_key" ON public.oauth_accounts USING btree (provider, "providerUserId");
            CREATE INDEX "oauth_accounts_userId_idx" ON public.oauth_accounts USING btree ("userId");
            """);

        // Giữ cấu trúc `email_tokens` theo schema mốc. Khóa `tokenHash` duy nhất ngăn trùng token.
        migrationBuilder.Sql("""
            CREATE TABLE public.email_tokens (
                id text NOT NULL,
                "tokenHash" text NOT NULL,
                "userId" text NOT NULL,
                purpose public."EmailTokenPurpose" NOT NULL,
                "expiresAt" timestamp(3) without time zone NOT NULL,
                "usedAt" timestamp(3) without time zone,
                "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
            );

            ALTER TABLE ONLY public.email_tokens
                ADD CONSTRAINT email_tokens_pkey PRIMARY KEY (id);

            CREATE UNIQUE INDEX "email_tokens_tokenHash_key" ON public.email_tokens USING btree ("tokenHash");
            CREATE INDEX "email_tokens_userId_idx" ON public.email_tokens USING btree ("userId");
            """);

        // Giữ `userId` nullable trong `consent_records` để bản ghi của khách chưa có tài khoản vẫn được lưu.
        migrationBuilder.Sql("""
            CREATE TABLE public.consent_records (
                id text NOT NULL,
                "anonymousId" text NOT NULL,
                "userId" text,
                personalization boolean DEFAULT false NOT NULL,
                marketing boolean DEFAULT false NOT NULL,
                "policyVersion" text NOT NULL,
                "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
            );

            ALTER TABLE ONLY public.consent_records
                ADD CONSTRAINT consent_records_pkey PRIMARY KEY (id);

            CREATE INDEX "consent_records_anonymousId_idx" ON public.consent_records USING btree ("anonymousId");
            CREATE INDEX "consent_records_userId_idx" ON public.consent_records USING btree ("userId");
            """);

        // Giữ cấu trúc `audit_logs` theo schema mốc để cột `jsonb` và các index không đổi.
        migrationBuilder.Sql("""
            CREATE TABLE public.audit_logs (
                id text NOT NULL,
                "actorId" text NOT NULL,
                action text NOT NULL,
                "targetType" text NOT NULL,
                "targetId" text NOT NULL,
                before jsonb,
                after jsonb,
                "createdAt" timestamp(3) without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
            );

            ALTER TABLE ONLY public.audit_logs
                ADD CONSTRAINT audit_logs_pkey PRIMARY KEY (id);

            CREATE INDEX audit_logs_action_idx ON public.audit_logs USING btree (action);
            CREATE INDEX "audit_logs_actorId_idx" ON public.audit_logs USING btree ("actorId");
            CREATE INDEX "audit_logs_createdAt_idx" ON public.audit_logs USING btree ("createdAt");
            """);

        // Tạo khóa ngoại sau bảng gốc để tham chiếu hợp lệ. Giữ hành vi cập nhật và xóa theo schema mốc.
        migrationBuilder.Sql("""
            ALTER TABLE ONLY public.consent_records
                ADD CONSTRAINT "consent_records_userId_fkey" FOREIGN KEY ("userId") REFERENCES public.users(id) ON UPDATE CASCADE ON DELETE SET NULL;

            ALTER TABLE ONLY public.email_tokens
                ADD CONSTRAINT "email_tokens_userId_fkey" FOREIGN KEY ("userId") REFERENCES public.users(id) ON UPDATE CASCADE ON DELETE CASCADE;

            ALTER TABLE ONLY public.oauth_accounts
                ADD CONSTRAINT "oauth_accounts_userId_fkey" FOREIGN KEY ("userId") REFERENCES public.users(id) ON UPDATE CASCADE ON DELETE CASCADE;

            ALTER TABLE ONLY public.role_permissions
                ADD CONSTRAINT "role_permissions_roleId_fkey" FOREIGN KEY ("roleId") REFERENCES public.roles(id) ON UPDATE CASCADE ON DELETE CASCADE;

            ALTER TABLE ONLY public.sessions
                ADD CONSTRAINT "sessions_userId_fkey" FOREIGN KEY ("userId") REFERENCES public.users(id) ON UPDATE CASCADE ON DELETE CASCADE;

            ALTER TABLE ONLY public.user_roles
                ADD CONSTRAINT "user_roles_roleId_fkey" FOREIGN KEY ("roleId") REFERENCES public.roles(id) ON UPDATE CASCADE ON DELETE CASCADE;

            ALTER TABLE ONLY public.user_roles
                ADD CONSTRAINT "user_roles_userId_fkey" FOREIGN KEY ("userId") REFERENCES public.users(id) ON UPDATE CASCADE ON DELETE CASCADE;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Xóa các bảng định danh theo thứ tự phụ thuộc để tránh xung đột khóa ngoại.
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS public.consent_records;
            DROP TABLE IF EXISTS public.email_tokens;
            DROP TABLE IF EXISTS public.oauth_accounts;
            DROP TABLE IF EXISTS public.sessions;
            DROP TABLE IF EXISTS public.user_roles;
            DROP TABLE IF EXISTS public.role_permissions;
            DROP TABLE IF EXISTS public.audit_logs;
            DROP TABLE IF EXISTS public.roles;
            DROP TABLE IF EXISTS public.users;
            """);

        // Gỡ enum sau bảng sử dụng để `Down` không thất bại do đối tượng còn phụ thuộc.
        migrationBuilder.Sql("""
            DROP TYPE IF EXISTS public."EmailTokenPurpose";
            DROP TYPE IF EXISTS public."UserKind";
            """);
    }
}
