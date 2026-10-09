using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;

namespace Pickory.Shared.Http;

// Đổi `TRUST_PROXY` thành cấu hình đọc `X-Forwarded-For`. Giới hạn tần suất theo IP cần đúng IP của client.
public static class TrustProxyOptions
{
    // Trả null khi không tin proxy nào. API khi đó bỏ qua `X-Forwarded-For` do client tự ghi.
    public static ForwardedHeadersOptions? Create(string trustProxy)
    {
        if (trustProxy == "false")
        {
            return null;
        }

        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor };
        // ASP.NET Core mặc định tin loopback. Xóa mặc định để chỉ tin đúng giá trị của `TRUST_PROXY`.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        // Số hop nghĩa là tin đúng chừng ấy proxy tính từ phía máy chủ, như `trust proxy` số của Express.
        if (int.TryParse(trustProxy, NumberStyles.None, CultureInfo.InvariantCulture, out var hops))
        {
            options.ForwardLimit = hops;
            return options;
        }

        // Danh sách địa chỉ nghĩa là chỉ tin các proxy có trong danh sách, không giới hạn số hop.
        options.ForwardLimit = null;
        foreach (var entry in trustProxy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPAddress.TryParse(entry, out var address))
            {
                options.KnownProxies.Add(address);
                continue;
            }

            if (IPNetwork.TryParse(entry, out var network))
            {
                options.KnownIPNetworks.Add(network);
                continue;
            }

            // Địa chỉ sai phải dừng lúc khởi động. Cấu hình sai lặng lẽ làm giới hạn tần suất đếm nhầm IP.
            throw new InvalidOperationException($"TRUST_PROXY chứa địa chỉ không hợp lệ: '{entry}'.");
        }

        return options;
    }
}
