using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Database.SupportDesk;

// SD-02: schema của support-desk đúng cột, enum, chỉ mục unique một phần, khóa ngoại và dòng cấu hình mặc định.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class SupportDeskSchemaTests(PickoryWebApplicationFactory factory)
    : IClassFixture<PickoryWebApplicationFactory>
{
    private const string Timestamp = "timestamp without time zone";

    // Mỗi dòng gồm tên bảng và danh sách `cột:kiểu:nullable`. Cột enum hiện tên kiểu enum, cột mảng hiện `ARRAY_` cùng kiểu phần tử.
    public static TheoryData<string, string[]> ExpectedColumns => new()
    {
        {
            "support_conversations",
            [
                "id:text:NO", "customerUserId:text:YES", "anonymousId:text:NO", "status:SupportConversationStatus:NO",
                $"createdAt:{Timestamp}:NO", $"lastMessageAt:{Timestamp}:NO", $"closedAt:{Timestamp}:YES",
            ]
        },
        {
            "support_messages",
            [
                "id:text:NO", "conversationId:text:NO", "role:SupportMessageRole:NO", "staffId:text:YES",
                "body:text:NO", "action:text:YES", $"createdAt:{Timestamp}:NO",
            ]
        },
        {
            "support_sessions",
            [
                "id:text:NO", "conversationId:text:NO", $"handoffAt:{Timestamp}:NO", "handoffInHours:boolean:NO",
                "customerUserId:text:YES", "anonymousId:text:NO", "staffId:text:YES", $"claimedAt:{Timestamp}:YES",
                $"firstResponseAt:{Timestamp}:YES", $"closedAt:{Timestamp}:YES", "closedBy:text:YES", "csat:integer:YES",
                "transferNote:text:YES", $"nudgedAt:{Timestamp}:YES",
            ]
        },
        {
            "callback_requests",
            [
                "id:text:NO", "conversationId:text:YES", "anonymousId:text:NO", "customerUserId:text:YES",
                "phone:text:YES", "note:text:YES", "status:CallbackRequestStatus:NO", "handledBy:text:YES",
                $"createdAt:{Timestamp}:NO", $"handledAt:{Timestamp}:YES",
            ]
        },
        {
            "canned_responses",
            [
                "id:text:NO", "ownerId:text:YES", "shortcut:text:NO", "body:text:NO", "shared:boolean:NO",
                $"createdAt:{Timestamp}:NO", $"updatedAt:{Timestamp}:NO",
            ]
        },
        {
            "staff_presence",
            ["staffId:text:NO", "status:StaffPresenceStatus:NO", $"lastSeenAt:{Timestamp}:NO"]
        },
        {
            "support_settings",
            [
                "id:text:NO", "opensAt:time without time zone:NO", "closesAt:time without time zone:NO",
                "holidays:ARRAY_date:NO", "waitNudgeMinutes:integer:NO", "autoCloseMinutes:integer:NO",
                $"updatedAt:{Timestamp}:NO",
            ]
        },
    };

    [Theory]
    [MemberData(nameof(ExpectedColumns))]
    public async Task Table_HasExactlyTheAgreedColumnsWithTypesAndNullability(string table, string[] expectedColumns)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SupportDeskDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);

        var columns = await ReadRowsAsync(context, $"""
            SELECT column_name || ':' ||
                   CASE WHEN data_type = 'USER-DEFINED' THEN udt_name
                        WHEN data_type = 'ARRAY' THEN data_type || '_' || ltrim(udt_name, '_')
                        ELSE data_type END || ':' || is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = '{table}'
            """);

        // So sánh theo thứ tự chữ cái cố định, vì thứ tự cột vật lý không phải điều cần kiểm. Cột thừa hay thiếu đều làm test đỏ.
        Assert.Equal(expectedColumns.Order(StringComparer.Ordinal), columns.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Enums_HaveTheAgreedLabelsInDeclaredOrder()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SupportDeskDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);

        var enums = await ReadRowsAsync(context, """
            SELECT type_row.typname || ':' || string_agg(label_row.enumlabel, ',' ORDER BY label_row.enumsortorder)
            FROM pg_type type_row
            JOIN pg_enum label_row ON label_row.enumtypid = type_row.oid
            JOIN pg_namespace schema_row ON schema_row.oid = type_row.typnamespace
            WHERE schema_row.nspname = 'public'
              AND type_row.typname IN ('SupportConversationStatus', 'SupportMessageRole', 'CallbackRequestStatus', 'StaffPresenceStatus')
            GROUP BY type_row.typname
            """);

        // Nhãn chữ thường theo `[PgName]`. Thứ tự nhãn quyết định phép so sánh enum trong PostgreSQL.
        Assert.Equal(
            [
                "CallbackRequestStatus:pending,done,unreachable",
                "StaffPresenceStatus:available,away",
                "SupportConversationStatus:open,closed",
                "SupportMessageRole:customer,staff,system",
            ],
            enums.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Indexes_HaveTheAgreedColumnsAndPartialConditions()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SupportDeskDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);

        var indexes = await ReadRowsAsync(context, """
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename IN ('support_conversations', 'support_messages', 'support_sessions', 'callback_requests')
              AND indexname NOT LIKE '%_pkey'
            """);

        // Điều kiện `WHERE` của hai chỉ mục unique là phần chặn dữ liệu trùng. Bỏ điều kiện thì chỉ mục chặn cả dòng đã đóng.
        Assert.Equal(
            [
                "CREATE INDEX \"callback_requests_status_createdAt_idx\" ON public.callback_requests USING btree (status, \"createdAt\")",
                "CREATE INDEX \"support_messages_conversationId_createdAt_idx\" ON public.support_messages USING btree (\"conversationId\", \"createdAt\")",
                "CREATE INDEX \"support_sessions_closedAt_handoffAt_idx\" ON public.support_sessions USING btree (\"closedAt\", \"handoffAt\")",
                "CREATE UNIQUE INDEX \"support_conversations_anonymousId_open_key\" ON public.support_conversations USING btree (\"anonymousId\") WHERE (status = 'open'::\"SupportConversationStatus\")",
                "CREATE UNIQUE INDEX \"support_sessions_conversationId_open_key\" ON public.support_sessions USING btree (\"conversationId\") WHERE (\"closedAt\" IS NULL)",
            ],
            indexes.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ForeignKeys_PointAtConversationsWithTheAgreedDeleteBehavior()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SupportDeskDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);

        // `c` là CASCADE, `n` là SET NULL. Chỉ xét khóa ngoại của bốn bảng thuộc module.
        var foreignKeys = await ReadRowsAsync(context, """
            SELECT constraint_row.conname || ':' || referenced_table.relname || ':' || constraint_row.confdeltype::text
            FROM pg_constraint constraint_row
            JOIN pg_class owner_table ON owner_table.oid = constraint_row.conrelid
            JOIN pg_class referenced_table ON referenced_table.oid = constraint_row.confrelid
            JOIN pg_namespace schema_row ON schema_row.oid = owner_table.relnamespace
            WHERE constraint_row.contype = 'f' AND schema_row.nspname = 'public'
              AND owner_table.relname IN ('support_messages', 'support_sessions', 'callback_requests')
            """);

        Assert.Equal(
            [
                "callback_requests_conversationId_fkey:support_conversations:n",
                "support_messages_conversationId_fkey:support_conversations:c",
                "support_sessions_conversationId_fkey:support_conversations:c",
            ],
            foreignKeys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Sessions_RejectSecondOpenSessionOfSameConversation()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SupportDeskDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        // Giao dịch giữ dữ liệu thử để rollback. Lỗi unique làm giao dịch hỏng nên không dùng tiếp sau đó.
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var conversation = NewConversation(anonymousId: NewId(), SupportConversationStatus.Open);
            context.Conversations.Add(conversation);
            context.Sessions.Add(NewSession(conversation));
            await context.SaveChangesAsync();

            context.Sessions.Add(NewSession(conversation));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

            var postgresError = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresError.SqlState);
            Assert.Equal("support_sessions_conversationId_open_key", postgresError.ConstraintName);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Sessions_AllowNewOpenSessionAfterThePreviousOneClosed()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SupportDeskDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var conversation = NewConversation(anonymousId: NewId(), SupportConversationStatus.Open);
            var firstSession = NewSession(conversation);
            context.Conversations.Add(conversation);
            context.Sessions.Add(firstSession);
            await context.SaveChangesAsync();

            // Đóng phiên đầu rồi mở phiên mới cho cùng hội thoại. Đây là luồng tin tiếp theo của khách mở phiên chờ mới.
            firstSession.ClosedAt = DateTime.UtcNow;
            firstSession.ClosedBy = "auto";
            // Hai lần lưu riêng, vì EF không bảo đảm thứ tự giữa lệnh cập nhật và lệnh thêm trong cùng một lần lưu.
            await context.SaveChangesAsync();
            context.Sessions.Add(NewSession(conversation));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var sessionCount = await context.Sessions.CountAsync(session => session.ConversationId == conversation.Id);
            Assert.Equal(2, sessionCount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Conversations_RejectSecondOpenConversationOfSameAnonymousId()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SupportDeskDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var anonymousId = NewId();
            context.Conversations.Add(NewConversation(anonymousId, SupportConversationStatus.Open));
            await context.SaveChangesAsync();

            context.Conversations.Add(NewConversation(anonymousId, SupportConversationStatus.Open));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

            var postgresError = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresError.SqlState);
            Assert.Equal("support_conversations_anonymousId_open_key", postgresError.ConstraintName);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Conversations_AllowOpenConversationNextToClosedOnesOfSameAnonymousId()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SupportDeskDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var anonymousId = NewId();
            // Hai hội thoại đã đóng và một hội thoại mở của cùng khách. Chỉ hội thoại mở bị chỉ mục unique chặn trùng.
            context.Conversations.AddRange(
                NewConversation(anonymousId, SupportConversationStatus.Closed),
                NewConversation(anonymousId, SupportConversationStatus.Closed),
                NewConversation(anonymousId, SupportConversationStatus.Open));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var conversationCount = await context.Conversations.CountAsync(conversation => conversation.AnonymousId == anonymousId);
            Assert.Equal(3, conversationCount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Settings_HasExactlyOneRowWithTheDefaultValues()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SupportDeskDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);

        var rows = await context.Settings.AsNoTracking().ToListAsync();

        var row = Assert.Single(rows);
        Assert.Equal("singleton", row.Id);
        Assert.Equal(new TimeOnly(8, 0), row.OpensAt);
        Assert.Equal(new TimeOnly(21, 0), row.ClosesAt);
        Assert.Empty(row.Holidays);
        Assert.Equal(10, row.WaitNudgeMinutes);
        Assert.Equal(30, row.AutoCloseMinutes);
    }

    [Fact]
    public async Task DeletingConversation_DeletesMessagesAndSessionsButKeepsCallbackRequestWithEmptyConversationId()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SupportDeskDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var conversation = NewConversation(anonymousId: NewId(), SupportConversationStatus.Open);
            var callbackRequest = new CallbackRequest
            {
                ConversationId = conversation.Id,
                AnonymousId = conversation.AnonymousId,
                Phone = "0900000000",
                Status = CallbackRequestStatus.Pending,
                CreatedAt = DateTime.UtcNow,
            };
            context.Conversations.Add(conversation);
            context.Messages.Add(new SupportMessage
            {
                ConversationId = conversation.Id,
                Role = SupportMessageRole.Customer,
                Body = "Xin chào",
                CreatedAt = DateTime.UtcNow,
            });
            context.Sessions.Add(NewSession(conversation));
            context.CallbackRequests.Add(callbackRequest);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            // Xóa bằng SQL để chính DB thực hiện cascade. Nếu xóa qua EF, EF có thể tự xóa các dòng con thay cho khóa ngoại.
            await context.Database.ExecuteSqlAsync($"DELETE FROM support_conversations WHERE id = {conversation.Id}");

            var messageCount = await context.Messages.CountAsync(message => message.ConversationId == conversation.Id);
            var sessionCount = await context.Sessions.CountAsync(session => session.ConversationId == conversation.Id);
            var keptCallback = await context.CallbackRequests.AsNoTracking().SingleAsync(request => request.Id == callbackRequest.Id);
            Assert.Equal(0, messageCount);
            Assert.Equal(0, sessionCount);
            Assert.Null(keptCallback.ConversationId);
            Assert.Equal("0900000000", keptCallback.Phone);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    // Mỗi test dùng mã riêng nên không phụ thuộc thứ tự chạy và không đụng dữ liệu của test khác.
    private static string NewId() => Guid.NewGuid().ToString();

    private static SupportConversation NewConversation(string anonymousId, SupportConversationStatus status)
    {
        var now = DateTime.UtcNow;
        return new SupportConversation
        {
            Id = NewId(),
            AnonymousId = anonymousId,
            Status = status,
            CreatedAt = now,
            LastMessageAt = now,
            ClosedAt = status == SupportConversationStatus.Closed ? now : null,
        };
    }

    private static SupportSession NewSession(SupportConversation conversation)
    {
        return new SupportSession
        {
            Id = NewId(),
            ConversationId = conversation.Id,
            AnonymousId = conversation.AnonymousId,
            HandoffAt = DateTime.UtcNow,
            HandoffInHours = true,
        };
    }

    private static async Task<List<string>> ReadRowsAsync(SupportDeskDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var rows = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(reader.GetString(0));
            }

            return rows;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
