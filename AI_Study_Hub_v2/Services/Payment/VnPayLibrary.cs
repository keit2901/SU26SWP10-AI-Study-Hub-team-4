using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using AI_Study_Hub_v2.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace AI_Study_Hub_v2.Services.Payment;

/// <summary>
/// Static utility for VNPay HMAC-SHA512 hashing, URL building, and signature verification.
/// </summary>
public static class VnPayLibrary
{
    private sealed class VnPayComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            return CompareInfo.GetCompareInfo("en-US")
                .Compare(x, y, CompareOptions.Ordinal);
        }
    }

    private static readonly VnPayComparer VnComp = new();

    /// <summary>
    /// Builds the full VNPay sandbox payment URL with signed parameters.
    /// </summary>
    public static string BuildPaymentUrl(
        VnPaySettings settings,
        string txnRef,
        long amountVnd,
        string orderInfo,
        string ipAddress,
        DateTimeOffset createDate)
    {
        var expireDate = createDate.AddMinutes(settings.ExpireMinutes);

        var vnpParams = new SortedDictionary<string, string>(VnComp)
        {
            { "vnp_Version", settings.Version },
            { "vnp_Command", settings.Command },
            { "vnp_TmnCode", settings.TmnCode },
            { "vnp_Amount", (amountVnd * 100).ToString(CultureInfo.InvariantCulture) },
            { "vnp_CurrCode", settings.CurrCode },
            { "vnp_TxnRef", txnRef },
            { "vnp_OrderInfo", orderInfo },
            { "vnp_OrderType", settings.OrderType },
            { "vnp_Locale", settings.Locale },
            { "vnp_ReturnUrl", settings.ReturnUrl },
            { "vnp_IpAddr", ipAddress },
            { "vnp_CreateDate", createDate.ToString("yyyyMMddHHmmss") },
            { "vnp_ExpireDate", expireDate.ToString("yyyyMMddHHmmss") },
        };

        // Remove empty/null values
        var keysToRemove = vnpParams.Where(kv => string.IsNullOrEmpty(kv.Value)).Select(kv => kv.Key).ToList();
        foreach (var key in keysToRemove) vnpParams.Remove(key);

        var queryString = BuildQueryString(vnpParams);
        var hash = BuildSecureHash(vnpParams, settings.HashSecret);
        return $"{settings.BaseUrl}?{queryString}&vnp_SecureHash={hash}";
    }

    /// <summary>
    /// Verifies the VNPay secure hash from a query collection.
    /// </summary>
    public static bool VerifyHash(IQueryCollection query, string hashSecret)
    {
        if (!query.TryGetValue("vnp_SecureHash", out var receivedHash) || StringValues.IsNullOrEmpty(receivedHash))
            return false;

        var parsed = ParseQueryParams(query);
        var computed = BuildSecureHash(parsed, hashSecret);
        return string.Equals(computed, receivedHash.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Computes HMAC-SHA512 over sorted, URL-encoded key=value pairs, returns lowercase hex.
    /// </summary>
    public static string BuildSecureHash(SortedDictionary<string, string> parameters, string hashSecret)
    {
        var queryString = BuildQueryString(parameters);
        return HmacSHA512(hashSecret, queryString);
    }

    /// <summary>
    /// Extracts vnp_* params from query collection into a sorted dictionary, excluding hash fields.
    /// </summary>
    public static SortedDictionary<string, string> ParseQueryParams(IQueryCollection query)
    {
        var parsed = new SortedDictionary<string, string>(VnComp);
        foreach (var key in query.Keys)
        {
            if (!key.StartsWith("vnp_", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(key, "vnp_SecureHash", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(key, "vnp_SecureHashType", StringComparison.OrdinalIgnoreCase)) continue;
            parsed[key] = query[key].ToString();
        }
        return parsed;
    }

    private static string BuildQueryString(SortedDictionary<string, string> parameters)
    {
        var sb = new StringBuilder();
        foreach (var kv in parameters)
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(WebUtility.UrlEncode(kv.Key));
            sb.Append('=');
            sb.Append(WebUtility.UrlEncode(kv.Value));
        }
        return sb.ToString();
    }

    private static string HmacSHA512(string key, string data)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        using var hmac = new HMACSHA512(keyBytes);
        var hashBytes = hmac.ComputeHash(dataBytes);
        var sb = new StringBuilder(hashBytes.Length * 2);
        foreach (var b in hashBytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
