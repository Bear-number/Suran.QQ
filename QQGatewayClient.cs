using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Suran.QQ;

/// <summary>
/// QQ 官方机器人的 WebSocket 网关客户端。协议形状照官方文档《事件订阅/网关》：
/// 先取网关地址（<c>GET /gateway/bot</c> 带分片与连接数配额，退回 <c>GET /gateway</c>），连上收 op 10 Hello 拿
/// <c>heartbeat_interval</c>（**毫秒、平台下发，不写死**），发 op 2 IDENTIFY（token 形如 <c>QQBot {access_token}</c>），
/// 之后 op 0 Dispatch 是事件、op 7 要重连、op 9 会话作废、op 11 心跳回执。
/// 关闭码里 4001/4002/4010/4011/4012/4013/4014 属于「重发 IDENTIFY 也没用」那一类（4013/4014 就是 intents 格式或权限问题，
/// 平台不会给可读原因，只会把连接关掉），所以同一码连撞三次就停下并报出该关哪一位，不做无脑重连风暴。
/// </summary>
public class QQGatewayClient(
    QQConfig config,
    QQAccessToken accessToken,
    Func<string, JsonElement, Task> handleEvent,
    Action<string> logDetail)
{
    static readonly int[] fatalCloseCodes = [4001, 4002, 4010, 4011, 4012, 4013, 4014];
    const int maxConsecutiveFatalCloses = 3;
    const int receiveBufferBytes = 1024 * 1024;

    readonly SemaphoreSlim sendLock = new(1, 1);

    long? lastSequence;
    string sessionId = "";
    int heartbeatIntervalMs;
    bool loopStarted;
    int consecutiveFatalCloses;
    bool sessionWasReady;

    public string State { get; private set; } = "还没起连接";

    public bool IsReady => State == "已就绪";

    public int HeartbeatIntervalMs => heartbeatIntervalMs;

    public long Intents => QQIntents.FromConfig(config);

    public string SessionSummary => "session " + (sessionId.Length > 0 ? "有" : "无")
        + "，seq " + (lastSequence == null ? "-" : lastSequence.Value.ToString(CultureInfo.InvariantCulture));

    /// <summary>起一条常驻连接循环。整个生命周期跟着宿主销毁走，销毁后不再重连</summary>
    public void Start(CancellationToken destroyToken)
    {
        if (loopStarted)
        {
            return;
        }
        loopStarted = true;
        _ = Task.Run(() => RunAsync(destroyToken), destroyToken);
    }

    async Task RunAsync(CancellationToken destroyToken)
    {
        while (destroyToken.IsCancellationRequested == false)
        {
            try
            {
                State = "在取网关地址";
                string gatewayUrl = await ResolveGatewayUrlAsync(destroyToken);
                State = "在连网关";
                await ConnectAndListenAsync(gatewayUrl, destroyToken);
                // 只有真的 READY 过才算"这条连接成过"，才把致命关闭的计数清零；
                // 否则每次被关闭后正常返回都会把计数抹掉，永远到不了停止阈值（探针实测踩过）
                if (sessionWasReady)
                {
                    consecutiveFatalCloses = 0;
                }
            }
            catch (OperationCanceledException)
            {
                State = "已随宿主销毁";
                return;
            }
            catch (Exception loopError)
            {
                State = "中断：" + QQRestClient.Shorten(loopError.Message);
                logDetail?.Invoke("网关循环异常：" + loopError.Message);
            }
            if (consecutiveFatalCloses >= maxConsecutiveFatalCloses)
            {
                State += "（已停止重连，要改配置才能恢复）";
                logDetail?.Invoke(State);
                return;
            }
            int wait = Math.Clamp(config.ReconnectSeconds, 1, 300);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(wait), destroyToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    async Task<string> ResolveGatewayUrlAsync(CancellationToken destroyToken)
    {
        // /gateway 与 /gateway/bot 也是 openapi，必须带 Authorization；不带的话平台直接拒，连地址都问不到
        string token;
        try
        {
            token = await accessToken.RequireAsync(destroyToken);
        }
        catch (Exception tokenError)
        {
            throw new Exception("问网关地址之前就得先有 access_token，这一步就没过：" + tokenError.Message
                + "（先确认 AppID/AppSecret、以及 api.bot.qq.com 需不需要在配置里填 HTTP 代理）");
        }
        HttpClient client = QQRestClient.NewClient(config); // 共享实例，用 using 会把别人的连接一起释放掉
        foreach (string path in new[] { "/gateway/bot", "/gateway" })
        {
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, QQAccessToken.ResolveBaseUrl(config) + path);
                request.Headers.TryAddWithoutValidation("Authorization", "QQBot " + token);
                using HttpResponseMessage response = await client.SendAsync(request, destroyToken);
                string text = await response.Content.ReadAsStringAsync(destroyToken);
                if (response.IsSuccessStatusCode == false)
                {
                    logDetail?.Invoke($"取网关地址 {path} 回 HTTP {(int)response.StatusCode}：{QQRestClient.Shorten(text)}");
                    continue;
                }
                JsonElement body = JsonDocument.Parse(text).RootElement.Clone();
                string url = QQJson.Text(body, "url");
                if (url.Length == 0)
                {
                    continue;
                }
                if (body.TryGetProperty("session_start_limit", out JsonElement limit) && limit.ValueKind == JsonValueKind.Object)
                {
                    logDetail?.Invoke($"网关配额：total {QQJson.Integer(limit, "total")}、remaining {QQJson.Integer(limit, "remaining")}、"
                        + "reset_after " + QQJson.Integer(limit, "reset_after") + "ms");
                }
                return url;
            }
            catch (Exception urlError) when (urlError is not OperationCanceledException)
            {
                logDetail?.Invoke($"取网关地址 {path} 失败：{urlError.Message}");
            }
        }
        throw new Exception("两个网关地址接口都没给出 url（" + QQAccessToken.ResolveBaseUrl(config) + "/gateway/bot 与 /gateway）——"
            + "先看 AppID/AppSecret 能不能取到 token，再看要不要在配置里填 HTTP 代理");
    }

    async Task ConnectAndListenAsync(string gatewayUrl, CancellationToken destroyToken)
    {
        using ClientWebSocket connection = new();
        using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(destroyToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            await connection.ConnectAsync(new Uri(gatewayUrl), lifetime.Token);
        }
        catch (Exception connectError)
        {
            throw new Exception("连不上网关 " + gatewayUrl + "：" + QQRestClient.Shorten(connectError.Message));
        }
        lifetime.CancelAfter(Timeout.InfiniteTimeSpan);
        logDetail?.Invoke("WebSocket 已连上 " + gatewayUrl);
        heartbeatIntervalMs = 0;
        sessionWasReady = false;
        Task heartbeatTask = HeartbeatAsync(connection, lifetime.Token);
        try
        {
            await ListenAsync(connection, lifetime.Token);
        }
        finally
        {
            lifetime.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch (Exception)
            {
                // 心跳任务退出原因是连接关闭或宿主销毁，连接层的结论已经在 State 里
            }
        }
    }

    async Task ListenAsync(ClientWebSocket connection, CancellationToken destroyToken)
    {
        byte[] buffer = new byte[receiveBufferBytes];
        while (destroyToken.IsCancellationRequested == false && connection.State == WebSocketState.Open)
        {
            MemoryStream message = new();
            WebSocketReceiveResult received;
            do
            {
                received = await connection.ReceiveAsync(new ArraySegment<byte>(buffer), destroyToken);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    await HandleCloseAsync(connection, received, destroyToken);
                    return;
                }
                message.Write(buffer, 0, received.Count);
            }
            while (received.EndOfMessage == false);

            string text = Encoding.UTF8.GetString(message.ToArray());
            await HandleFrameAsync(text, connection, destroyToken);
        }
    }

    async Task HandleCloseAsync(ClientWebSocket connection, WebSocketReceiveResult received, CancellationToken destroyToken)
    {
        int closeCode = received.CloseStatus == null ? 0 : (int)received.CloseStatus.Value;
        if (Array.IndexOf(fatalCloseCodes, closeCode) >= 0)
        {
            consecutiveFatalCloses++;
            State = $"网关以 {closeCode} 关闭连接（第 {consecutiveFatalCloses.ToString(CultureInfo.InvariantCulture)} 次）";
            string reason = closeCode == 4013 || closeCode == 4014 ? QQIntents.IntentHint(Intents) : "这个码重发 IDENTIFY 也没用，要改配置";
            logDetail?.Invoke(State + "：" + reason);
            if (consecutiveFatalCloses >= maxConsecutiveFatalCloses)
            {
                State += "：" + reason;
            }
            return;
        }
        State = "网关断开（关闭码 " + closeCode.ToString(CultureInfo.InvariantCulture) + "），准备重连";
        logDetail?.Invoke(State + "，会话信息保留：" + SessionSummary);
        try
        {
            await connection.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "reconnect", destroyToken);
        }
        catch (Exception)
        {
            // 服务端已经关掉了，回不过去不影响重连
        }
    }

    async Task HandleFrameAsync(string text, ClientWebSocket connection, CancellationToken destroyToken)
    {
        JsonElement frame;
        try
        {
            frame = JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (JsonException)
        {
            logDetail?.Invoke("网关发来一帧不是 JSON，忽略：" + QQRestClient.Shorten(text));
            return;
        }
        int op = (int)QQJson.Integer(frame, "op", -1);
        bool hasSequence = QQJson.TryInteger(frame, "s", out long sequence);
        string eventName = QQJson.Text(frame, "t");
        switch (op)
        {
            case 10:
                heartbeatIntervalMs = (int)QQJson.Integer(HelloData(frame), "heartbeat_interval", 45000);
                bool canResume = sessionId.Length > 0 && lastSequence != null;
                logDetail?.Invoke("Hello：心跳间隔 " + heartbeatIntervalMs.ToString(CultureInfo.InvariantCulture) + "ms，"
                    + (canResume ? "尝试恢复会话（op 6，不带 intents）" : "开始 IDENTIFY（op 2）"));
                if (canResume)
                {
                    await ResumeAsync(connection, destroyToken);
                }
                else
                {
                    await IdentifyAsync(connection, destroyToken);
                }
                break;
            case 11:
                logDetail?.Invoke("心跳已确认");
                break;
            case 0:
                if (hasSequence)
                {
                    lastSequence = sequence;
                }
                await DispatchAsync(eventName, frame, destroyToken);
                break;
            case 7:
                State = "网关要求重连（op 7）";
                throw new Exception("网关要求重新连接");
            case 9:
                logDetail?.Invoke("会话作废（op 9），清掉 session/seq 重新 IDENTIFY");
                sessionId = "";
                lastSequence = null;
                break;
            default:
                logDetail?.Invoke("未处理的网关 op " + op.ToString(CultureInfo.InvariantCulture) + " t=" + eventName);
                break;
        }
    }

    static JsonElement HelloData(JsonElement frame) =>
        frame.TryGetProperty("d", out JsonElement data) && data.ValueKind == JsonValueKind.Object ? data : frame;

    async Task DispatchAsync(string eventName, JsonElement frame, CancellationToken destroyToken)
    {
        JsonElement data = frame.TryGetProperty("d", out JsonElement payload) ? payload : JsonDocument.Parse("null").RootElement.Clone();
        if (eventName == "READY")
        {
            sessionId = QQJson.Text(data, "session_id");
            sessionWasReady = true;
            consecutiveFatalCloses = 0;
            State = "已就绪";
            logDetail?.Invoke("READY：session " + (sessionId.Length > 0 ? "已拿到" : "空的") + "，订阅位 " + QQIntents.Text(Intents));
            return;
        }
        if (eventName == "RESUMED")
        {
            State = "已就绪";
            sessionWasReady = true;
            consecutiveFatalCloses = 0;
            logDetail?.Invoke("RESUMED：会话恢复成功");
            return;
        }
        if (eventName.Length == 0)
        {
            return;
        }
        logDetail?.Invoke("事件 " + eventName + "，" + SessionSummary);
        await handleEvent(eventName, data);
    }

    async Task IdentifyAsync(ClientWebSocket connection, CancellationToken destroyToken)
    {
        string token = await accessToken.RequireAsync(destroyToken);
        JsonObject identify = new()
        {
            ["op"] = 2,
            ["d"] = new JsonObject
            {
                ["token"] = "QQBot " + token,
                ["intents"] = Intents,
                ["shard"] = new JsonArray(0, 1),
                ["properties"] = new JsonObject
                {
                    ["$os"] = "windows",
                    ["$browser"] = "Suran.QQ",
                    ["$device"] = "Suran.QQ"
                }
            }
        };
        await SendAsync(connection, identify.ToJsonString(), destroyToken);
    }

    async Task ResumeAsync(ClientWebSocket connection, CancellationToken destroyToken)
    {
        string token = await accessToken.RequireAsync(destroyToken);
        JsonObject resume = new()
        {
            ["op"] = 6,
            ["d"] = new JsonObject
            {
                ["token"] = "QQBot " + token,
                ["session_id"] = sessionId,
                ["seq"] = lastSequence
            }
        };
        await SendAsync(connection, resume.ToJsonString(), destroyToken);
    }

    async Task HeartbeatAsync(ClientWebSocket connection, CancellationToken destroyToken)
    {
        while (destroyToken.IsCancellationRequested == false && connection.State == WebSocketState.Open)
        {
            if (heartbeatIntervalMs <= 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), destroyToken);
                continue;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(heartbeatIntervalMs), destroyToken);
            JsonObject beat = new() { ["op"] = 1, ["d"] = lastSequence == null ? null : JsonValue.Create(lastSequence.Value) };
            await SendAsync(connection, beat.ToJsonString(), destroyToken, tolerant: true);
        }
    }

    async Task SendAsync(ClientWebSocket connection, string payload, CancellationToken destroyToken, bool tolerant = false)
    {
        await sendLock.WaitAsync(destroyToken);
        try
        {
            if (connection.State != WebSocketState.Open)
            {
                if (tolerant)
                {
                    return;
                }
                throw new Exception("网关连接已不在 Open 状态（" + connection.State + "）");
            }
            await connection.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload)), WebSocketMessageType.Text, true, destroyToken);
        }
        catch (Exception sendError) when (tolerant && sendError is not OperationCanceledException)
        {
            logDetail?.Invoke("心跳发送失败（马上会被重连接管）：" + sendError.Message);
        }
        finally
        {
            sendLock.Release();
        }
    }

    /// <summary>自检用：把当前订阅位与连接状况写成几行可读文本</summary>
    public string Describe()
    {
        return "订阅位 " + QQIntents.Text(Intents)
            + "\n心跳 " + (heartbeatIntervalMs > 0 ? heartbeatIntervalMs.ToString(CultureInfo.InvariantCulture) + "ms（平台下发）" : "还没拿到 Hello")
            + "\n" + SessionSummary + "\n状态：" + State;
    }
}
