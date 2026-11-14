using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Suran.QQ;

/// <summary>
/// access_token 的取用与续期。规则照官方文档（获取访问凭证页）：生命周期默认 7200 秒；有效期内重复请求返回**同一个值**、
/// 不会刷新；只有进入到期前 60 秒内再取才会换新 token，而旧 token 在这 60 秒内仍然有效——所以提前量必须明显大于 60 秒，
/// 这里取 180 秒。失败是响应体里的 code，且 HTTP 仍回 200。token 与 AppSecret 都不写日志、不进返回值。
/// </summary>
public class QQAccessToken(QQConfig config, Action<string> logDetail)
{
    public const string DefaultApiBaseUrl = "https://api.bot.qq.com";
    public static readonly TimeSpan RenewSafetyWindow = TimeSpan.FromSeconds(180);

    const string tokenPath = "/app/getAppAccessToken";
    const string tokenUrlHelp = "确认 AppID/AppSecret 与 q.qq.com 后台「开发管理」里的一致；机器人被封禁或已删除会报 100007";

    string currentToken = "";
    DateTime expireAtUtc = DateTime.MinValue;
    readonly SemaphoreSlim tokenLock = new(1, 1);

    public static string ResolveBaseUrl(QQConfig config)
    {
        string configured = config.ApiBaseUrl.Trim().TrimEnd('/');
        return configured.Length > 0 ? configured : DefaultApiBaseUrl;
    }

    public bool IsUsable => currentToken.Length > 0 && DateTime.UtcNow < expireAtUtc;

    public int RemainingSeconds => IsUsable == false ? 0 : Math.Max(0, (int)(expireAtUtc - DateTime.UtcNow).TotalSeconds);

    public Task<string> RequireAsync(CancellationToken cancelToken) => AcquireAsync(false, cancelToken);

    /// <summary>被平台以鉴权失败踢回来时（如 630002、401）强制换一枚新的</summary>
    public Task<string> ForceRefreshAsync(CancellationToken cancelToken) => AcquireAsync(true, cancelToken);

    async Task<string> AcquireAsync(bool force, CancellationToken cancelToken)
    {
        await tokenLock.WaitAsync(cancelToken);
        try
        {
            if (force == false && IsUsable)
            {
                return currentToken;
            }
            (currentToken, long seconds) = await RequestAsync(cancelToken);
            expireAtUtc = DateTime.UtcNow.AddSeconds(seconds);
            logDetail?.Invoke($"access_token 已更新，有效期 {seconds.ToString(CultureInfo.InvariantCulture)} 秒（提前 {RenewSafetyWindow.TotalSeconds.ToString(CultureInfo.InvariantCulture)} 秒续期）");
            return currentToken;
        }
        finally
        {
            tokenLock.Release();
        }
    }

    async Task<(string Token, long Seconds)> RequestAsync(CancellationToken cancelToken)
    {
        string payload = new JsonObject
        {
            ["appId"] = config.AppId.Trim(),
            ["clientSecret"] = config.AppSecret.Trim()
        }.ToJsonString();
        HttpClient client = QQRestClient.NewClient(config); // 共享实例，绝不能 using 释放
        using HttpRequestMessage request = new(HttpMethod.Post, ResolveBaseUrl(config) + tokenPath)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        using HttpResponseMessage response = await client.SendAsync(request, cancelToken);
        string body = await response.Content.ReadAsStringAsync(cancelToken);
        JsonElement parsed;
        try
        {
            parsed = JsonDocument.Parse(body).RootElement.Clone();
        }
        catch (JsonException formatError)
        {
            throw new Exception($"取 access_token 拿到的不是 JSON（HTTP {(int)response.StatusCode}）：{QQRestClient.Shorten(body)}｜{formatError.Message}");
        }
        if (QQApiException.TryReadFailure(parsed, out int code, out string message, out string traceId))
        {
            logDetail?.Invoke($"取 access_token 失败 code={code.ToString(CultureInfo.InvariantCulture)} trace={traceId}");
            throw new QQApiException($"取 access_token（{tokenPath}）", code, message.Length > 0 ? message : "无 message", traceId);
        }
        string token = QQJson.Text(parsed, "access_token");
        if (token.Length == 0)
        {
            throw new Exception($"取 access_token 没返回 access_token 字段（HTTP {(int)response.StatusCode}）：{QQRestClient.Shorten(body)}。{tokenUrlHelp}");
        }
        if (QQJson.TryInteger(parsed, "expires_in", out long seconds) == false || seconds <= 0)
        {
            seconds = 7200;
        }
        return (token, Math.Max(120, seconds - (long)RenewSafetyWindow.TotalSeconds));
    }
}
