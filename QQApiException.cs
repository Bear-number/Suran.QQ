using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Suran.QQ;

/// <summary>
/// 官方机器人接口的失败。文档原话：「请不要依据 message 来判定一个请求是否失败，message 可能会随时调整」，
/// 所以这里只认数字码——但两套接口看的键不一样：
/// 取 token 的 /app/getAppAccessToken 用 code（且失败时 HTTP 仍是 200），其余 /v2 OpenAPI 用 err_code。
/// </summary>
public class QQApiException(string operation, int code, string message, string traceId) : Exception(Describe(operation, code, message, traceId))
{
    public string Operation { get; } = operation;
    public int Code { get; } = code;
    public string RawMessage { get; } = message;
    public string TraceId { get; } = traceId;

    // 排查指南取自官方文档的码表与本地既有记录；话术要让模型能自己决定下一步，而不是只报一个数字
    static readonly Dictionary<int, string> hints = new()
    {
        [100001] = "取凭证太频繁，退避后再试（access_token 有效期内重复取会返回同一个值，没必要重取）",
        [100007] = "AppID 无效或机器人状态不正常（被封禁/已删除）",
        [100016] = "AppID 或 AppSecret 与开放平台后台不一致，去 q.qq.com 复制重来",
        [10004] = "这个 AppID 对应的机器人不存在",
        [11253] = "该能力是白名单（内邀）权限，你这个机器人没开通——群信息/成员详情/bot_state 这类接口都会撞它",
        [11001] = "接口本身回「不支持的调用」：这个 app 的能力集里没有它（本机 2026-10-24 实测评测频道类 /users/@me/guilds 就这样）。纯群聊机器人别走频道类接口",
        [40011002] = "同上，err_code 形态：该 app 不支持这类调用（频道/子频道类接口对纯群聊机器人就是这个）。要频道能力得在开放平台单独申请",
        [40034005] = "被动回复用的 msg_id 已过期，改成主动发送或重新拿一条消息的 id",
        [40034024] = "msg_id 无效或不属于当前会话（越权）",
        [40034026] = "event_id 已过期，被动回复窗口只在事件后那几分钟内有效",
        [40034027] = "这个事件不支持回复",
        [40034100] = "主动消息配额超了（群侧未认证 30/qpm、单关系 20/qpm、每群每天 1000 条；单聊侧未认证 5/qps 且 30/qpm）",
        [40034105] = "没有主动下发权限（未开「接收所有消息」或不在可主动会话内）",
        [40034128] = "被动回复超时或次数用完：群 5 分钟内最多 5 次、单聊 60 分钟内最多 4 次",
        [40054005] = "同一 msg_id + 同一 msg_seq 被判去重（本插件每条回复会自动递增 msg_seq；若反复出现，检查是不是外部也在用同一个 seq）",
        [40061001] = "消息 id 形态不对：显示态 qqo-xxx 要先反解成真实 id 才能撤回",
        [305007] = "按钮样式不合法，render_data.style 只认 0 和 1",
        [40034029] = "按钮行列超限（最多 5 行 × 5 列，action.data ≤ 100 字符）",
        [630002] = "回应互动回调时 Authorization 头有问题（token 过期或未带）",
        [304080] = "file_info 无效或已过期，重新上传拿新的",
        [850018] = "被禁言，发不出去",
        [850019] = "文件格式不支持",
        [850026] = "平台拉不到这个 URL（外链不可达或需要鉴权）",
        [850027] = "平台拉取超时",
        [850031] = "文件体积超限",
        [40093001] = "分片上传可重试",
        [40093002] = "当天分片上传配额用完",
        [50002] = "流式接口限流，退避后重试并把 index 往前推",
        [40035001] = "素材图片格式不支持（扩展名与 Content-Type 必须是 png/jpeg/gif/webp 且两者一致）",
        [40035002] = "素材图片超过大小上限（默认 10MB）",
        [40035003] = "上传任务不存在、已超过 4 小时过期、或不属于当前机器人——重新 prepare 一次",
        [40035006] = "服务端内部错误（COS 处理、链接生成失败），可稍后重试",
        [40035008] = "要删除的 image_id 不存在"
    };

    static string Describe(string operation, int code, string message, string traceId)
    {
        string hint = hints.TryGetValue(code, out string? text) ? "｜" + text : "";
        string trace = traceId.Length > 0 ? "｜trace " + traceId : "";
        return operation + " 失败：" + code.ToString(CultureInfo.InvariantCulture) + " " + message + hint + trace;
    }

    /// <summary>从响应体里取失败码：先看 err_code（/v2 接口），再看 code（token 接口），都没有就是成功</summary>
    public static bool TryReadFailure(JsonElement body, out int code, out string message, out string traceId)
    {
        code = 0;
        message = "";
        traceId = "";
        if (body.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        long? errCode = ReadNumber(body, "err_code") ?? ReadNumber(body, "code");
        if (errCode == null || errCode.Value == 0)
        {
            return false;
        }
        code = (int)errCode.Value;
        message = ReadString(body, "message") ?? ReadString(body, "msg") ?? "";
        traceId = ReadString(body, "trace_id") ?? "";
        return true;
    }

    static long? ReadNumber(JsonElement body, string name)
    {
        if (body.TryGetProperty(name, out JsonElement value) == false)
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
        {
            return number;
        }
        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : null;
    }

    static string? ReadString(JsonElement body, string name) =>
        body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
