using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Suran.QQ;

/// <summary>
/// QQ 官方机器人 OpenAPI（统一 https://api.bot.qq.com，v2 路径）。三条硬规矩按文档来：
/// ① 成功时响应体直接是业务数据，失败才有 err_code，判定只认数字码不看 message；
/// ② 取 token 那个接口例外，失败码在 code 且 HTTP 仍回 200（由 QQAccessToken 处理）；
/// ③ 发送类接口有去重与配额（同 msg_id+msg_seq 重发报 40054005、主动消息超配额报 40034100），
///    所以**只对 GET 重试一次**，POST/DELETE 一律失败就如实抛，不在这里偷偷重发。
/// </summary>
public class QQRestClient(QQConfig config, QQAccessToken accessToken, Action<string>? logDetail)
{
    const string BrowserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";
    const int authCodeAppIdFailed = 630002;

    static readonly Dictionary<string, HttpClient> clients = new();

    /// <summary>按「代理+超时」共用一个 HttpClient（每请求新建会累积端口）。**返回的是共享实例，调用方不要 using 释放**</summary>
    public static HttpClient NewClient(QQConfig config)
    {
        string proxy = config.ProxyUrl.Trim();
        int timeout = Math.Clamp(config.RequestTimeoutSeconds, 5, 120);
        string key = proxy + "@" + timeout.ToString();
        lock (clients)
        {
            if (clients.TryGetValue(key, out HttpClient? cached))
            {
                return cached;
            }
        }
        HttpClientHandler handler = new();
        if (proxy.Length > 0)
        {
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(proxy);
        }
        HttpClient client = new(handler) { Timeout = TimeSpan.FromSeconds(timeout) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserAgent);
        lock (clients)
        {
            clients[key] = client;
        }
        return client;
    }

    public string BaseUrl => QQAccessToken.ResolveBaseUrl(config);

    public Task<JsonElement> GetAsync(string path, CancellationToken cancelToken) =>
        SendAsync(HttpMethod.Get, path, null, allowAuthRetry: true, allowTransportRetry: true, cancelToken);

    public Task<JsonElement> PostAsync(string path, JsonObject? body, CancellationToken cancelToken) =>
        SendAsync(HttpMethod.Post, path, body, allowAuthRetry: true, allowTransportRetry: false, cancelToken);

    public Task<JsonElement> PutAsync(string path, JsonObject? body, CancellationToken cancelToken) =>
        SendAsync(HttpMethod.Put, path, body, allowAuthRetry: true, allowTransportRetry: false, cancelToken);

    /// <summary>文档里「修改子频道」「修改入群审批策略」用的都是 PATCH，别拿 PUT 凑。</summary>
    public Task<JsonElement> PatchAsync(string path, JsonObject? body, CancellationToken cancelToken) =>
        SendAsync(HttpMethod.Patch, path, body, allowAuthRetry: true, allowTransportRetry: false, cancelToken);

    public Task<JsonElement> DeleteAsync(string path, CancellationToken cancelToken) =>
        SendAsync(HttpMethod.Delete, path, null, allowAuthRetry: true, allowTransportRetry: false, cancelToken);

    async Task<JsonElement> SendAsync(HttpMethod method, string path, JsonObject? body, bool allowAuthRetry, bool allowTransportRetry, CancellationToken cancelToken)
    {
        string token = await accessToken.RequireAsync(cancelToken);
        try
        {
            return await RequestAsync(method, path, body, token, cancelToken);
        }
        catch (HttpRequestException networkError) when (allowTransportRetry)
        {
            logDetail?.Invoke($"{method.Method} {path} 网络失败，重试一次：{networkError.Message}");
            return await RequestAsync(method, path, body, token, cancelToken);
        }
        catch (QQApiException authError) when (allowAuthRetry && IsAuthFailure(authError))
        {
            logDetail?.Invoke($"{method.Method} {path} 鉴权失败（{authError.Code.ToString()}），换一枚 token 再试一次");
            string refreshed = await accessToken.ForceRefreshAsync(cancelToken);
            return await RequestAsync(method, path, body, refreshed, cancelToken);
        }
    }

    static bool IsAuthFailure(QQApiException error) => error.Code == authCodeAppIdFailed || error.Code == 401 || error.Code == 403;

    async Task<JsonElement> RequestAsync(HttpMethod method, string path, JsonObject? body, string token, CancellationToken cancelToken)
    {
        using HttpRequestMessage request = new(method, BaseUrl + path);
        request.Headers.TryAddWithoutValidation("Authorization", "QQBot " + token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body != null)
        {
            // StringContent 会自己带上 charset=utf-8，媒体类型只能写 application/json（写成带 charset 的整串会被 .NET 判非法）
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        using HttpResponseMessage response = await NewClient(config).SendAsync(request, cancelToken);
        string text = await response.Content.ReadAsStringAsync(cancelToken);
        JsonElement parsed;
        try
        {
            parsed = JsonDocument.Parse(text.Length > 0 ? text : "null").RootElement.Clone();
        }
        catch (JsonException formatError)
        {
            throw new Exception($"{method.Method} {path} 回的不是 JSON（HTTP {(int)response.StatusCode}）：{Shorten(text)}｜{formatError.Message}");
        }
        // 平台会用 HTTP 400 带业务码（实测去重就是 400 + err_code 40054005），
        // 所以**先读 body 的 err_code**，读不到才退化成 HTTP 状态码，否则真正的原因会被 400 这个壳盖掉
        if (QQApiException.TryReadFailure(parsed, out int code, out string message, out string traceId))
        {
            logDetail?.Invoke($"{method.Method} {path} → {code} {Shorten(message)} trace={traceId}");
            throw new QQApiException($"{method.Method} {path}", code, message, traceId);
        }
        if (response.IsSuccessStatusCode == false)
        {
            throw new Exception($"{method.Method} {path} 回 HTTP {(int)response.StatusCode} {response.ReasonPhrase}，"
                + "响应体里没有 err_code" + (text.Trim().Length > 0 ? "：" + Shorten(text) : "（空体）"));
        }
        logDetail?.Invoke($"{method.Method} {path} → ok（{(int)response.StatusCode}，{text.Length.ToString()} 字节）");
        return parsed;
    }

    public static string Shorten(string text)
    {
        string oneLine = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return oneLine.Length > 200 ? oneLine[..200] + "…" : oneLine;
    }
}
