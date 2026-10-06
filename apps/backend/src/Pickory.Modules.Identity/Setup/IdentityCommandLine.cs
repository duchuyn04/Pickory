using System.Collections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.Identity.Users;

namespace Pickory.Modules.Identity.Setup;

// Lệnh quản trị chạy bằng `dotnet run --project Pickory.Api -- <lệnh>`. Lệnh chạy xong thì thoát, không mở web API.
public static class IdentityCommandLine
{
    public const string SeedCommand = "identity:seed";
    public const string CreateAdminCommand = "identity:create-admin";

    // Giá trị mặc định chỉ dùng ngoài production, giống bản Node. Production phải truyền email và mật khẩu riêng.
    private const string DevelopmentAdminEmail = "admin@pickory.local";
    private const string DevelopmentAdminPassword = "Admin@123456";
    private const string DefaultAdminName = "Quản trị viên";

    public static bool IsCommand(IReadOnlyList<string> args)
    {
        return args.Count > 0 && (args[0] == SeedCommand || args[0] == CreateAdminCommand);
    }

    public static AdminOptions ReadAdminOptions(IReadOnlyList<string> args, IReadOnlyDictionary<string, string?> environment, bool isProduction)
    {
        // Tham số dòng lệnh thắng biến môi trường. Biến môi trường thắng giá trị mặc định của môi trường dev.
        var email = ReadArgument(args, "email") ?? ReadEnvironment(environment, "ADMIN_EMAIL") ?? (isProduction ? null : DevelopmentAdminEmail);
        var password = ReadArgument(args, "password") ?? ReadEnvironment(environment, "ADMIN_PASSWORD") ?? (isProduction ? null : DevelopmentAdminPassword);
        var name = ReadArgument(args, "name") ?? ReadEnvironment(environment, "ADMIN_NAME") ?? DefaultAdminName;

        if (email is null)
        {
            throw new InvalidOperationException("Vui lòng cung cấp email admin qua --email hoặc ADMIN_EMAIL");
        }

        // Mật khẩu thiếu thành chuỗi rỗng. `AdminAccountCreator` báo lỗi độ dài với thông báo giống bản Node.
        return new AdminOptions(email, password ?? string.Empty, name);
    }

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        if (args[0] == SeedCommand)
        {
            return await RunSeedAsync(database);
        }

        var isProduction = scope.ServiceProvider.GetRequiredService<IHostEnvironment>().IsProduction();
        return await RunCreateAdminAsync(args, database, isProduction);
    }

    private static async Task<int> RunSeedAsync(IdentityDbContext database)
    {
        try
        {
            await IdentityRoleSeeder.SeedAsync(database, CancellationToken.None);
            Console.WriteLine("Seed role identity thành công.");
            return 0;
        }
        catch (Exception exception)
        {
            // Lệnh chạy ngoài web API, nên lỗi được ghi thẳng ra stderr cho người chạy lệnh đọc.
            Console.Error.WriteLine($"Seed thất bại: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunCreateAdminAsync(IReadOnlyList<string> args, IdentityDbContext database, bool isProduction)
    {
        try
        {
            var options = ReadAdminOptions(args, ReadProcessEnvironment(), isProduction);
            var result = await AdminAccountCreator.CreateAsync(database, options.Email, options.Password, options.Name, CancellationToken.None);
            var normalizedEmail = UserInputNormalizer.NormalizeEmail(options.Email);
            Console.WriteLine(result == AdminCreationResult.Created
                ? $"Tạo admin thành công: {normalizedEmail}"
                : $"Admin với email \"{normalizedEmail}\" đã tồn tại.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Tạo admin thất bại: {exception.Message}");
            return 1;
        }
    }

    // Nhận cả dạng `--email value` và dạng `--email=value`, giống hàm `parseArg` của bản Node.
    private static string? ReadArgument(IReadOnlyList<string> args, string name)
    {
        var flag = $"--{name}";
        for (var index = 0; index < args.Count; index++)
        {
            if (args[index] == flag && index + 1 < args.Count)
            {
                return args[index + 1];
            }

            if (args[index].StartsWith(flag + "=", StringComparison.Ordinal))
            {
                return args[index][(flag.Length + 1)..];
            }
        }

        return null;
    }

    private static string? ReadEnvironment(IReadOnlyDictionary<string, string?> environment, string name)
    {
        return environment.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    private static Dictionary<string, string?> ReadProcessEnvironment()
    {
        return Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value, StringComparer.Ordinal);
    }
}
