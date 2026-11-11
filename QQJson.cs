using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Suran.QQ;

/// <summary>官方接口的 JSON 取值：同一字段在不同事件里经常一会儿是数字一会儿是字符串（如 timestamp、code、expires_in），
/// 全部在这里统一收口，别处不再各写一套 switch。</summary>
static class QQJson
{
    public static string Text(JsonElement body, string name, string fallback = "")
    {
        if (body.ValueKind != JsonValueKind.Object || body.TryGetProperty(name, out JsonElement value) == false)
        {
            return fallback;
        }
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? fallback,
            JsonValueKind.Number => value.GetRawText(),
            _ => fallback
        };
    }

    public static bool TryInteger(JsonElement body, string name, out long value)
    {
        value = 0;
        if (body.ValueKind != JsonValueKind.Object || body.TryGetProperty(name, out JsonElement field) == false)
        {
            return false;
        }
        if (field.ValueKind == JsonValueKind.Number && field.TryGetInt64(out long number))
        {
            value = number;
            return true;
        }
        if (field.ValueKind == JsonValueKind.String && long.TryParse(field.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }

    public static long Integer(JsonElement body, string name, long fallback = 0) => TryInteger(body, name, out long value) ? value : fallback;

    public static bool Flag(JsonElement body, string name)
    {
        if (body.ValueKind != JsonValueKind.Object || body.TryGetProperty(name, out JsonElement field) == false)
        {
            return false;
        }
        return field.ValueKind == JsonValueKind.True
            || (field.ValueKind == JsonValueKind.Number && field.GetInt64() != 0)
            || (field.ValueKind == JsonValueKind.String && bool.TryParse(field.GetString(), out bool text) && text);
    }

    public static IEnumerable<JsonElement> Array(JsonElement body, string name)
    {
        if (body.ValueKind != JsonValueKind.Object || body.TryGetProperty(name, out JsonElement array) == false || array.ValueKind != JsonValueKind.Array)
        {
            return System.Array.Empty<JsonElement>();
        }
        List<JsonElement> items = new();
        foreach (JsonElement item in array.EnumerateArray())
        {
            items.Add(item.Clone());
        }
        return items;
    }

    public static bool Has(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out JsonElement value) && value.ValueKind != JsonValueKind.Null;

    /// <summary>官方事件体里的 timestamp：消息事件是 RFC3339 字符串，其他事件是 Unix 秒整数，两种都要能读</summary>
    public static string ReadTimestamp(JsonElement body)
    {
        if (TryInteger(body, "timestamp", out long seconds))
        {
            return System.DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        return Text(body, "timestamp");
    }
}
