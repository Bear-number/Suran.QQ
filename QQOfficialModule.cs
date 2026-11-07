using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Function.FunctionCaller;
using Alife.Framework;
using Microsoft.Extensions.Logging;

namespace Alife.QQOfficial;

[Module("QQ官方机器人接入",
    "接 QQ 开放平台的官方机器人 OpenAPI：自己取 access_token 并续期、连 WebSocket 网关收事件、把 QQ 侧消息按会话形状注入 Alife（回信仍由模型调用本插件的发送工具发出），并提供文本/markdown/富媒体发送、撤回、群管理与按钮回调等工具",
    defaultCategory: "苏染的工具/QQ与群管理")]
public partial class QQOfficialModule(
    XmlFunctionCaller functionCaller,
    ILogger<QQOfficialModule> logger,
    Interactor<QQOfficialModule> interactor
) : ChatBehaviour, IConfigurable<QQOfficialConfig>
{
    public QQOfficialConfig Configuration { get; set; } = null!;

    const string usersPath = "/v2/users/";
    const string groupsPath = "/v2/groups/";
    const string interactionPath = "/interactions/";
    const int passiveReplyGroupSeconds = 300;
    const int passiveReplyPrivateSeconds = 3600;
    const int passiveReplyGroupTimes = 5;
    const int passiveReplyPrivateTimes = 4;

    // 传输层跨热重载共用一份：网关连接、token、以及「该会话最近一条可被动回复的消息」
    static QQAccessToken? accessToken;
    static QQRestClient? rest;
    static QQGatewayClient? gateway;
    static string botOpenId = "";
    static string botResolveError = "";
    QQIdentityStore? identities;
    DateTime lastIdentitySaved = DateTime.MinValue;
    static string gatewayStateText = "还没起连接";
    static readonly Dictionary<string, string> lastInboundMessageId = new();
    static readonly Dictionary<string, (DateTime ExpireAtUtc, int Times)> replyBudget = new();
    static readonly Dictionary<string, DateTime> seenEvents = new();
    static readonly Dictionary<string, int> typingFrames = new();
    static readonly Dictionary<string, int> seqByMessageId = new();
    static readonly Dictionary<string, string> pendingInteractions = new();
    static readonly object transportGate = new();

    protected override Task OnAwake()
    {
        XmlHandler handler = new(this)
        {
            Description = "QQ 官方机器人（q.qq.com 那套 OpenAPI）接入：收 QQ 群/私聊消息进会话、把回复与富媒体发回 QQ、群管理与按钮回调。",
            Explanation = "这是 QQ 官方机器人的 HTTP + WebSocket 接口，目标 ID 全是 32 位十六进制 openid，不是 QQ 号也不是群号。"
                + "注入进会话的消息首行形如 [QQ官方群聊消息(group_openid)] 或 [QQ官方私聊消息(user_openid)]，下一行是 [openid(昵称)]:正文。"
                + "要回这条消息就用 QQSend（会自动带上可回复的 msg_id），直接发新内容用 QQSendText/QQSendMedia。"
                + "被动回复有平台窗口（群 5 分钟最多 5 次、单聊 60 分钟最多 4 次），过期或超次数会如实报错，那时只能走主动消息且需要配置里放开。"
                + "连不上、收不到事件先跑 QQOfficialEnv。"
        };
        functionCaller.RegisterHandler(handler, cancellationToken: DestroyCancellationToken); // 不挂隐式：默认 Explicit，文档直接进「当前可用功能」，不再留 <name/> 入口等模型自己去翻
        return Task.CompletedTask;
    }

    /// <summary>连接与通讯录都放在 OnStart 起，不放 OnAwake：那时平台还没把 Configuration 注进来，读 AppId 会拿到 null（宿主所有插件都是这个时机）</summary>
    protected override Task OnStart()
    {
        identities = new QQIdentityStore(
            Path.Combine(AlifePath.StorageFolderPath, Configuration.DataFolder.Trim()), DetailLog);
        identities.Load();
        identities.LearnQqFromPeers();
        StartTransport();
        return Task.CompletedTask;
    }

    /// <summary>落盘走这里：消息路径上只标脏，不碰磁盘</summary>
    protected override Task OnUpdate()
    {
        if (identities == null || (DateTime.Now - lastIdentitySaved).TotalSeconds < 30)
        {
            return Task.CompletedTask;
        }
        lastIdentitySaved = DateTime.Now;
        identities.SaveIfNeeded();
        return Task.CompletedTask;
    }

    protected override Task OnDestroy()
    {
        identities?.SaveIfNeeded();
        logger.LogInformation("[QQOfficial] 插件卸载，网关随销毁令牌停止");
        return Task.CompletedTask;
    }

    // ============================================================
    // 传输层启动与配置校验
    // ============================================================

    void StartTransport()
    {
        if (TryConfigure(out string configureError) == false)
        {
            gatewayStateText = "没起来：" + configureError;
            logger.LogInformation("[QQOfficial] 不启动网关：{Error}", configureError);
            return;
        }
        gateway?.Start(DestroyCancellationToken);
        _ = Task.Run(() => ResolveSelfAsync(), DestroyCancellationToken);
    }

    bool TryConfigure(out string error)
    {
        if (Configuration.AppId.Trim().Length == 0 || Configuration.AppSecret.Trim().Length == 0)
        {
            error = "配置里没有 AppID/AppSecret";
            return false;
        }
        if (QQIntents.FromConfig(Configuration) == 0)
        {
            error = "intents 一位都没开，网关会连上就断；至少要开「群与私聊消息(1<<25)」";
            return false;
        }
        lock (transportGate)
        {
            accessToken ??= new QQAccessToken(Configuration, DetailLog);
            rest ??= new QQRestClient(Configuration, accessToken, DetailLog);
            gateway ??= new QQGatewayClient(Configuration, accessToken, HandleEventAsync, DetailLog);
        }
        error = "";
        return true;
    }

    /// <summary>取机器人自己的 openid：官方会把机器人发出去的消息再推回来，没有它挡不住回声</summary>
    async Task ResolveSelfAsync()
    {
        for (int attempt = 0; attempt < 3 && botOpenId.Length == 0; attempt++)
        {
            try
            {
                JsonElement self = await RequireRest().GetAsync("/users/@me", DestroyCancellationToken);
                botOpenId = QQJson.Text(self, "id");
                DetailLog("机器人 openid 已取到");
                return;
            }
            catch (Exception selfError)
            {
                botResolveError = selfError.Message;
                await Task.Delay(TimeSpan.FromSeconds(5), DestroyCancellationToken);
            }
        }
    }

    QQRestClient RequireRest()
    {
        if (TryConfigure(out string configureError) == false || rest == null)
        {
            throw new Exception("官方机器人还没配置好：" + configureError);
        }
        return rest;
    }

    // ============================================================
    // 网关事件 → 会话注入
    // ============================================================

    async Task HandleEventAsync(string eventName, JsonElement data)
    {
        if (IsDuplicate(eventName, data))
        {
            DetailLog("同一事件重复推送，已吞掉：" + eventName);
            return;
        }
        switch (eventName)
        {
            case "C2C_MESSAGE_CREATE":
            case "GROUP_AT_MESSAGE_CREATE":
            case "GROUP_MESSAGE_CREATE":
                await InjectMessageAsync(eventName, data);
                break;
            case "INTERACTION_CREATE":
                await HandleInteractionAsync(data);
                break;
            case "READY":
            case "RESUMED":
                break;
            case "FRIEND_ADD":
            case "FRIEND_DEL":
            case "C2C_MSG_RECEIVE":
            case "C2C_MSG_REJECT":
            case "GROUP_ADD_ROBOT":
            case "GROUP_DEL_ROBOT":
            case "GROUP_MSG_RECEIVE":
            case "GROUP_MSG_REJECT":
            case "GROUP_MEMBER_ADD":
            case "GROUP_MEMBER_REMOVE":
            case "GROUP_JOIN_REQUEST":
                interactor.Poke(DescribeGroupEvent(eventName, data)
                    + (QQJson.Text(data, "verify_info").Length > 0 ? "\n验证信息：" + QQRestClient.Shorten(data.ToString()) : ""));
                break;
            case "GUILD_CREATE":
            case "GUILD_UPDATE":
            case "GUILD_DELETE":
            case "CHANNEL_CREATE":
            case "CHANNEL_UPDATE":
            case "CHANNEL_DELETE":
                interactor.Poke(DescribeGuildEvent(eventName, data));
                break;
            case "MESSAGE_AUDIT_PASS":
            case "MESSAGE_AUDIT_REJECT":
                interactor.Poke("[QQ官方审核结果 " + eventName + "] " + QQRestClient.Shorten(data.ToString()));
                break;
            case "SUBSCRIBE_MESSAGE_STATUS":
                interactor.Poke("[QQ官方订阅消息状态] " + QQRestClient.Shorten(data.ToString()));
                break;
            default:
                DetailLog("没处理的事件类型：" + eventName);
                break;
        }
    }

    bool IsDuplicate(string eventName, JsonElement data)
    {
        string id = QQJson.Text(data, "id");
        if (id.Length == 0)
        {
            return false;
        }
        DateTime now = DateTime.UtcNow;
        foreach (string stale in new List<string>(seenEvents.Keys))
        {
            if (seenEvents[stale] < now.AddMinutes(-Math.Clamp(Configuration.DedupTtlMinutes, 1, 24 * 60)))
            {
                seenEvents.Remove(stale);
            }
        }
        if (seenEvents.ContainsKey(eventName + "#" + id))
        {
            return true;
        }
        seenEvents[eventName + "#" + id] = now;
        return false;
    }

    async Task InjectMessageAsync(string eventName, JsonElement data)
    {
        JsonElement author = data.TryGetProperty("author", out JsonElement authorElement) && authorElement.ValueKind == JsonValueKind.Object
            ? authorElement
            : JsonDocument.Parse("{}").RootElement.Clone();
        string senderId = QQJson.Text(author, "user_openid");
        if (senderId.Length == 0)
        {
            senderId = QQJson.Text(author, "member_openid");
        }
        if (senderId.Length == 0)
        {
            senderId = QQJson.Text(author, "id");
        }
        bool isGroup = eventName != "C2C_MESSAGE_CREATE";
        string targetId = isGroup ? QQJson.Text(data, "group_openid") : senderId;
        // 官方会把机器人自己发出去的消息再推回来的情形没有约定，按 openid 硬挡回声；昵称谁都能改，不能当身份
        if (senderId.Length > 0 && senderId == botOpenId)
        {
            return;
        }
        string messageId = QQJson.Text(data, "id");
        if (messageId.Length > 0 && targetId.Length > 0)
        {
            lastInboundMessageId[ConversationKey(isGroup, targetId)] = messageId;
        }
        string body = QQJson.Text(data, "content");
        if (QQJson.Integer(data, "message_type") == 3)
        {
            body = "[结构化卡片，正文取不到内容] " + body;
        }
        LearnPeople(data);
        identities?.RememberNickname(senderId, QQJson.Text(author, "username"));
        if (isGroup)
        {
            identities?.RememberGroupName(targetId, QQJson.Text(data, "group_name"));
        }
        if (Configuration.InjectInboundMessages == false)
        {
            DetailLog("注入开关是关的，这条只记了可回复的 msg_id：" + eventName);
            return;
        }
        string display = identities == null ? "昵称未知（通讯录没起来）" : identities.DisplayName(senderId);
        string groupName = isGroup && identities != null ? identities.GroupName(targetId) : "";
        string attachments = DescribeAttachments(data);
        string speaker = "[" + senderId + "(" + display + ")]";
        string head = isGroup
            ? "[QQ官方群聊消息(" + targetId + (groupName.Length > 0 ? "," + groupName : "") + ")]"
            : "[QQ官方私聊消息(" + targetId + ")]";
        interactor.Chat(head + "\n" + speaker + ":" + body + attachments);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 顺手学人：mentions 里每个都带 id+username；引用消息（官方 message_type=103）里
    /// msg_elements[].author 是完整 User 对象——这是私聊唯一能学到对方昵称的第二条路
    /// （私聊事件本身的 author.username 是空串）。
    /// </summary>
    void LearnPeople(JsonElement data)
    {
        if (identities == null)
        {
            return;
        }
        foreach (JsonElement mention in QQJson.Array(data, "mentions"))
        {
            identities.RememberNickname(QQJson.Text(mention, "id"), QQJson.Text(mention, "username"));
        }
        foreach (JsonElement element in QQJson.Array(data, "msg_elements"))
        {
            if (element.TryGetProperty("author", out JsonElement author) == false || author.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            string openid = QQJson.Text(author, "user_openid");
            if (openid.Length == 0)
            {
                openid = QQJson.Text(author, "member_openid");
            }
            if (openid.Length == 0)
            {
                openid = QQJson.Text(author, "id");
            }
            identities.RememberNickname(openid, QQJson.Text(author, "username"));
            identities.Bind(openid, QQJson.Text(author, "union_user_account"));
        }
    }

    static string DescribeAttachments(JsonElement data)
    {
        List<string> lines = new();
        foreach (JsonElement item in QQJson.Array(data, "attachments"))
        {
            string contentType = QQJson.Text(item, "content_type");
            string label = contentType switch
            {
                "voice" => "语音",
                "image/jpeg" or "image/png" or "image/gif" => "图片",
                "video/mp4" => "视频",
                "file" => "文件",
                _ => contentType.Length > 0 ? contentType : "附件"
            };
            lines.Add("\n[附件:" + label + "] " + QQJson.Text(item, "url")
                + (QQJson.Text(item, "asr_refer_text").Length > 0 ? "（平台ASR文本：" + QQJson.Text(item, "asr_refer_text") + "）" : ""));
        }
        return string.Join("", lines);
    }

    /// <summary>频道体系的事件字段与群不一样：id 是频道或子频道 ID、频道名在 name、子频道还带 guild_id/type</summary>
    static string DescribeGuildEvent(string eventName, JsonElement data) =>
        "[QQ官方频道事件 " + eventName + "] id=" + (QQJson.Text(data, "id").Length > 0 ? QQJson.Text(data, "id") : "-")
        + " 名称=" + (QQJson.Text(data, "name").Length > 0 ? QQJson.Text(data, "name") : "-")
        + (QQJson.Text(data, "guild_id").Length > 0 ? " 所属频道=" + QQJson.Text(data, "guild_id") : "")
        + (QQJson.Has(data, "type") ? " 类型=" + QQJson.Integer(data, "type").ToString(CultureInfo.InvariantCulture) : "")
        + (QQJson.Text(data, "op_user_id").Length > 0 ? " 操作人=" + QQJson.Text(data, "op_user_id") : "");

    static string DescribeGroupEvent(string eventName, JsonElement data)
    {
        string group = QQJson.Text(data, "group_openid");
        string actor = QQJson.Text(data, "op_member_openid");
        if (actor.Length == 0)
        {
            actor = QQJson.Text(data, "member_openid");
        }
        if (group.Length == 0)
        {
            // C2C_MSG_RECEIVE / C2C_MSG_REJECT 这类单聊事件给的是 openid，没有 group_openid
            group = QQJson.Text(data, "openid");
        }
        return "[QQ官方群事件 " + eventName + "] 目标=" + (group.Length > 0 ? group : "-")
            + " 操作人=" + (actor.Length > 0 ? actor : "-")
            + " 时间=" + QQJson.ReadTimestamp(data);
    }

    async Task HandleInteractionAsync(JsonElement data)
    {
        string interactionId = QQJson.Text(data, "id");
        int type = (int)QQJson.Integer(data, "type", -1);
        bool handled = type == 11 || type == 12;
        string buttonId = "";
        string buttonData = "";
        if (data.TryGetProperty("data", out JsonElement payload) && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("resolved", out JsonElement resolved) && resolved.ValueKind == JsonValueKind.Object)
        {
            buttonId = QQJson.Text(resolved, "button_id");
            buttonData = QQJson.Text(resolved, "button_data");
        }
        if (interactionId.Length > 0 && (type == 11 || type == 12))
        {
            pendingInteractions[interactionId] = type.ToString(CultureInfo.InvariantCulture);
            try
            {
                // 平台等不了模型：先替它回执 0（成功），否则客户端会一直 loading 到超时
                await RequireRest().PutAsync(interactionPath + interactionId, new JsonObject { ["code"] = 0 }, DestroyCancellationToken);
            }
            catch (Exception ackError)
            {
                DetailLog("互动回执失败：" + ackError.Message);
            }
        }
        interactor.Poke("[QQ官方按钮回调] type=" + type.ToString(CultureInfo.InvariantCulture) + "（" + InteractionTypeName(type) + "）"
            + " group_openid=" + QQJson.Text(data, "group_openid") + " user_openid=" + QQJson.Text(data, "user_openid")
            + " button_id=" + buttonId + (buttonData.Length > 0 ? " button_data=" + buttonData : "")
            + " interaction_id=" + interactionId
            + (handled == false ? "（这一类不需要回执）" : "（已自动回执成功，不需要再调 QQAnswerInteraction）"));
        await Task.CompletedTask;
    }

    static string InteractionTypeName(int type) => type switch
    {
        11 => "消息按钮，必须回执",
        12 => "单聊快捷菜单，必须回执",
        13 => "消息反馈",
        14 => "清空会话",
        15 => "进出故事集",
        16 => "切换模型",
        18 => "用户授权",
        19 => "群授权",
        20 => "群授权状态变更",
        _ => "未列出的类型"
    };

    // ============================================================
    // 发送
    // ============================================================

    static string ConversationKey(bool isGroup, string targetId) => (isGroup ? "g:" : "p:") + targetId;

    string TargetPath(bool isGroup, string targetId) => (isGroup ? groupsPath : usersPath) + Uri.EscapeDataString(targetId.Trim());

    async Task<JsonElement> SendMessageAsync(bool isGroup, string targetId, JsonObject payload, string operation)
    {
        string messageId = payload.TryGetPropertyValue("msg_id", out JsonNode? replyTo) ? replyTo?.GetValue<string>() ?? "" : "";
        int limitSeconds = isGroup ? passiveReplyGroupSeconds : passiveReplyPrivateSeconds;
        int limitTimes = isGroup ? passiveReplyGroupTimes : passiveReplyPrivateTimes;
        if (messageId.Length == 0 && Configuration.AllowProactiveMessage == false)
        {
            throw new Exception("这条没有可被动回复的 msg_id，属于主动下发；平台的主动消息有配额（群侧未认证 30/qpm、每群每天 1000 条），"
                + "要发就把配置「回传·主动消息」打开，或者等对方先说话");
        }
        if (messageId.Length > 0 && ReplyBudgetExceeded(messageId, limitSeconds, limitTimes))
        {
            throw new Exception("这条消息的被动回复额度已用完（" + (isGroup ? "群 5 分钟内最多 5 次" : "单聊 60 分钟内最多 4 次") + "），"
                + "平台会回 40034128；要接着说就得走主动消息或等对方再发一条");
        }
        if (messageId.Length > 0 && payload.ContainsKey("msg_seq") == false)
        {
            // 平台按 (msg_id, msg_seq) 去重：不递增 seq，同一条消息的第二句会被判「消息被去重」40054005（真接口踩过）
            payload["msg_seq"] = NextMessageSeq(messageId);
        }
        JsonElement result = await RequireRest().PostAsync(TargetPath(isGroup, targetId) + "/messages", payload, DestroyCancellationToken);
        if (messageId.Length > 0)
        {
            RecordReply(messageId, limitSeconds);
        }
        DetailLog(operation + " 成功：" + QQJson.Text(result, "id"));
        return result;
    }

    /// <summary>同一个 msg_id 的每一句都要换新的 msg_seq，从 1 开始单调递增</summary>
    static int NextMessageSeq(string messageId)
    {
        int next = (seqByMessageId.TryGetValue(messageId, out int used) ? used : 0) + 1;
        seqByMessageId[messageId] = next;
        return next;
    }

    bool ReplyBudgetExceeded(string messageId, int limitSeconds, int limitTimes)
    {
        if (replyBudget.TryGetValue(messageId, out (DateTime ExpireAtUtc, int Times) used) == false)
        {
            return false;
        }
        return used.ExpireAtUtc < DateTime.UtcNow ? false : used.Times >= limitTimes;
    }

    void RecordReply(string messageId, int limitSeconds)
    {
        DateTime now = DateTime.UtcNow;
        int times = replyBudget.TryGetValue(messageId, out (DateTime ExpireAtUtc, int Times) used) && used.ExpireAtUtc > now ? used.Times : 0;
        DateTime expireAt = used.ExpireAtUtc > now ? used.ExpireAtUtc : now.AddSeconds(limitSeconds);
        replyBudget[messageId] = (expireAt, times + 1);
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("把当前这轮的回复正文发到 QQ（收信注入后的默认出口）。targetType=group 传 group_openid，private 传 user_openid")]
    public async Task QQSend(XmlExecutorContext ctx, string targetId, string targetType = "group")
    {
        if (ctx.CallMode != CallMode.Closing)
        {
            return;
        }
        string text = ctx.FullContent.Trim();
        if (text.Length == 0)
        {
            return;
        }
        bool isGroup = IsGroupTarget(targetType, out string targetError);
        if (targetError.Length > 0)
        {
            interactor.Poke("[QQ官方机器人] " + targetError);
            return;
        }
        if (Configuration.EnableSend == false)
        {
            interactor.Poke("[QQ官方机器人] 发送开关是关的，这条没发出去");
            return;
        }
        JsonObject payload = BuildTextPayload(text, ConversationKey(isGroup, targetId.Trim()), isGroup);
        try
        {
            await SendMessageAsync(isGroup, targetId.Trim(), payload, "QQSend");
        }
        catch (Exception sendError)
        {
            interactor.Poke("[QQ官方机器人发送失败] " + sendError.Message);
        }
    }

    JsonObject BuildTextPayload(string text, string conversationKey, bool isGroup)
    {
        JsonObject payload = new();
        if (QQSendPolish.HasAtMarker(text))
        {
            // 正文里有 @ 标记就必须按 markdown 发，纯文本下平台只把它当普通字符显示出来
            payload["msg_type"] = QQSendPolish.markdownType;
            payload["markdown"] = new JsonObject { ["content"] = text };
        }
        else
        {
            payload["msg_type"] = QQSendPolish.textType;
            payload["content"] = text;
        }
        int limitSeconds = isGroup ? passiveReplyGroupSeconds : passiveReplyPrivateSeconds;
        int limitTimes = isGroup ? passiveReplyGroupTimes : passiveReplyPrivateTimes;
        if (lastInboundMessageId.TryGetValue(conversationKey, out string? messageId) && messageId.Length > 0
            && ReplyBudgetExceeded(messageId, limitSeconds, limitTimes) == false)
        {
            payload["msg_id"] = messageId;
        }
        return payload;
    }

    bool IsGroupTarget(string targetType, out string error)
    {
        string want = targetType.Trim().ToLowerInvariant();
        error = "";
        if (want is "group" or "群" or "群聊" or "group_openid")
        {
            return true;
        }
        if (want is "private" or "c2c" or "user" or "单聊" or "私聊" or "user_openid")
        {
            return false;
        }
        error = "targetType 只认 group 或 private，你给的是「" + targetType + "」";
        return false;
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("主动往某个 QQ 会话发一条文本/markdown（不带上下文，走的是当前会话最近一条消息的被动回复额度；没有额度就按平台规则报错）。markdown 带按钮时把 keyboardJson 一起给")]
    public async Task QQSendText(string targetId, string text, string targetType = "group", string markdown = "", string keyboardJson = "",
        string atOpenids = "", string quoteMessageId = "")
    {
        bool isGroup = IsGroupTarget(targetType, out string targetError);
        if (targetError.Length > 0)
        {
            throw new Exception(targetError);
        }
        if (Configuration.EnableSend == false)
        {
            throw new Exception("配置里「开关·发送消息」是关的");
        }
        List<string> at = SplitIds(atOpenids);
        string atProblem = QQSendPolish.UnknownAtTargets(at, RequireIdentities());
        if (atProblem.Length > 0)
        {
            throw new Exception(atProblem);
        }
        string body = markdown.Trim().Length > 0 ? markdown.Trim() : text.Trim();
        if (at.Count > 0)
        {
            body = QQSendPolish.AppendAtMarkers(body, at);
        }
        if (body.Length == 0)
        {
            throw new Exception("正文是空的");
        }
        bool wantsMarkdown = markdown.Trim().Length > 0 || at.Count > 0 || keyboardJson.Trim().Length > 0 || QQSendPolish.HasAtMarker(body);
        if (keyboardJson.Trim().Length > 0 && markdown.Trim().Length == 0 && at.Count == 0)
        {
            throw new Exception("带 keyboardJson 就必须同时给 markdown 正文（只有 markdown 消息支持按钮）");
        }
        string referenceProblem = QQSendPolish.ReferenceProblem(wantsMarkdown, quoteMessageId);
        if (referenceProblem.Length > 0)
        {
            throw new Exception(referenceProblem);
        }
        JsonObject payload = new();
        if (wantsMarkdown)
        {
            payload["msg_type"] = QQSendPolish.markdownType;
            payload["markdown"] = new JsonObject { ["content"] = body };
            if (keyboardJson.Trim().Length > 0)
            {
                string keyboardError = KeyboardProblem(keyboardJson);
                if (keyboardError.Length > 0)
                {
                    throw new Exception("键盘结构不合法：" + keyboardError);
                }
                payload["keyboard"] = JsonNode.Parse(keyboardJson.Trim());
            }
        }
        else
        {
            payload["msg_type"] = QQSendPolish.textType;
            payload["content"] = body;
        }
        if (quoteMessageId.Trim().Length > 0)
        {
            payload["message_reference"] = new JsonObject { ["message_id"] = quoteMessageId.Trim() };
        }
        string key = ConversationKey(isGroup, targetId.Trim());
        if (lastInboundMessageId.TryGetValue(key, out string? recent) && recent.Length > 0)
        {
            payload["msg_id"] = recent;
        }
        JsonElement result = await SendMessageAsync(isGroup, targetId.Trim(), payload, "QQSendText");
        interactor.Poke("[QQ官方机器人] 已发出" + (wantsMarkdown ? "（markdown" + (at.Count > 0 ? "，带 " + at.Count.ToString(CultureInfo.InvariantCulture) + " 个 @" : "") + "）" : "（纯文本）")
            + "：" + QQJson.Text(result, "id") + "\n" + QQRestClient.Shorten(body));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("发结构化卡片（ark，msg_type=3）。这类型**要先达到准入条件并向平台运营申请权限**，没权限平台会直接拒。"
        + "kvJson 形如 [{\"key\":\"#TITLE#\",\"value\":\"标题\"}]，数组型变量用 obj 嵌套：[{\"key\":\"#LIST#\",\"obj\":[{\"obj_kv\":[{\"key\":\"name\",\"value\":\"a\"}]}]}]")]
    public async Task QQSendArk(string targetId, int templateId, string kvJson, string targetType = "group")
    {
        bool isGroup = IsGroupTarget(targetType, out string targetError);
        if (targetError.Length > 0)
        {
            throw new Exception(targetError);
        }
        string problem = ArkProblem(templateId, kvJson);
        if (problem.Length > 0)
        {
            throw new Exception("ark 结构不合法：" + problem);
        }
        if (Configuration.EnableSend == false)
        {
            throw new Exception("配置里「开关·发送消息」是关的");
        }
        JsonNode? parsed = JsonNode.Parse(kvJson.Trim());
        JsonObject payload = new()
        {
            ["msg_type"] = 3,
            ["content"] = "",
            ["ark"] = new JsonObject { ["template_id"] = templateId, ["kv"] = parsed }
        };
        string target = targetId.Trim();
        string key = ConversationKey(isGroup, target);
        if (lastInboundMessageId.TryGetValue(key, out string? recent) && recent.Length > 0)
        {
            payload["msg_id"] = recent;
        }
        JsonElement result = await SendMessageAsync(isGroup, target, payload, "QQSendArk");
        interactor.Poke("结构化卡片已发出（模板 " + templateId.ToString(CultureInfo.InvariantCulture) + "）：" + QQJson.Text(result, "id"));
    }

    /// <summary>模板 ID 与 kv 的形状平台文档写死了：template_id 只有 23/24/37 三个可选，kv 每项要么 value 要么 obj</summary>
    static string ArkProblem(int templateId, string kvJson)
    {
        if (templateId is not (23 or 24 or 37))
        {
            return "template_id 官方文档只列了 23 / 24 / 37，你给的是 " + templateId.ToString(CultureInfo.InvariantCulture);
        }
        if (kvJson.Trim().Length == 0)
        {
            return "kvJson 是空的";
        }
        if (JsonNode.Parse(kvJson.Trim()) is not JsonArray kv)
        {
            return "kvJson 要是一个数组";
        }
        foreach (JsonNode? raw in kv)
        {
            if (raw is not JsonObject entry)
            {
                return "kv 每一项都要是对象";
            }
            if ((entry["key"]?.ToString() ?? "").Length == 0)
            {
                return "kv 每一项都要有 key（形如 \"#TITLE#\"）";
            }
            if (entry.ContainsKey("value") == false && entry.ContainsKey("obj") == false)
            {
                return "kv 每一项要有 value 或 obj（数组型变量才用 obj）";
            }
        }
        return "";
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("上传本地文件并发到 QQ。mediaType：image 图片 / video 视频 / voice 语音(官方只认 silk、mp3、wav、ogg；m4a 只能当 file 发) / file 文件。"
        + "给 url 走平台转存、给 localPath 由插件按大小自动选整文件或分片上传（>6MB 走分片）。平台对 base64 那条路线标过「暂未支持」，这里两条都留着，未实机验证")]
    public async Task QQSendMedia(string targetId, string mediaType, string localPath = "", string url = "", string targetType = "group")
    {
        bool isGroup = IsGroupTarget(targetType, out string targetError);
        if (targetError.Length > 0)
        {
            throw new Exception(targetError);
        }
        int fileType = mediaType.Trim().ToLowerInvariant() switch
        {
            "image" or "图片" => 1,
            "video" or "视频" => 2,
            "voice" or "语音" => 3,
            "file" or "文件" => 4,
            _ => throw new Exception("mediaType 只认 image/video/voice/file")
        };
        string target = targetId.Trim();
        string fileInfo = await UploadMediaAsync(isGroup, target, fileType, localPath, url);
        JsonObject payload = new()
        {
            ["msg_type"] = 7,
            ["media"] = new JsonObject { ["file_info"] = fileInfo }
        };
        string key = ConversationKey(isGroup, target);
        if (lastInboundMessageId.TryGetValue(key, out string? recent) && recent.Length > 0)
        {
            payload["msg_id"] = recent;
        }
        JsonElement result = await SendMessageAsync(isGroup, target, payload, "QQSendMedia");
        interactor.Poke("[官方机器人] " + mediaType + " 已发出：" + QQJson.Text(result, "id"));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("单聊打字机效果（流式，C2C 专用，50 QPS）。第一段不传 streamMsgId，之后每段都要带上上一次返回的 id；"
        + "inputState=1 生成中、10 结束；index 从 0 递增；mode=append 追加、replace 用全量正文覆盖")]
    public async Task QQStreamText(string userOpenid, string text, string streamMsgId = "", int index = 0, int inputState = 1, string mode = "append", string msgId = "", string eventId = "")
    {
        string user = Need(userOpenid, "userOpenid");
        JsonObject payload = new()
        {
            ["content_type"] = "text",
            ["content_raw"] = text,
            ["input_mode"] = mode.Trim().Length > 0 ? mode.Trim().ToLowerInvariant() : "append",
            ["input_state"] = inputState == 10 ? 10 : 1,
            ["index"] = Math.Max(0, index)
        };
        if (streamMsgId.Trim().Length > 0)
        {
            payload["stream_msg_id"] = streamMsgId.Trim();
        }
        if (msgId.Trim().Length > 0)
        {
            payload["msg_id"] = msgId.Trim();
        }
        if (eventId.Trim().Length > 0)
        {
            payload["event_id"] = eventId.Trim();
        }
        if (msgId.Trim().Length == 0 && eventId.Trim().Length == 0)
        {
            throw new Exception("流式消息也要挂在一次对话上：msg_id 与 event_id 至少给一个（超窗口平台会拒）");
        }
        JsonElement result = await RequireRest().PostAsync(usersPath + Uri.EscapeDataString(user) + "/stream_messages", payload, DestroyCancellationToken);
        interactor.Poke("流式分片 " + index.ToString(CultureInfo.InvariantCulture) + " 已发出（input_state " + inputState.ToString(CultureInfo.InvariantCulture) + "）"
            + "\nstream_msg_id=" + QQJson.Text(result, "id")
            + "，剩余长度 " + QQJson.Integer(result, "remain_msg_len").ToString(CultureInfo.InvariantCulture)
            + (index == 0 && QQJson.Text(result, "id").Length > 0 ? "\n下一段把这个 id 填进 streamMsgId" : ""));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("给对方一个「正在输入」状态提示（C2C 专用，msg_type=6）。seconds 是提示持续秒数。"
        + "注意这一帧也占被动回复额度，所以每个会话有帧数上限（配置「提示·输入状态帧数上限」，默认 3，填 0 等于不发）")]
    public async Task QQTypingNotify(string userOpenid, int seconds = 3)
    {
        string user = Need(userOpenid, "userOpenid");
        string key = ConversationKey(false, user);
        if (QQSendPolish.TakeTypingFrame(typingFrames, key, Configuration.TypingMaxFrames) == false)
        {
            interactor.Poke("这次不发输入状态提示了——" + QQSendPolish.DescribeFrames(
                typingFrames.TryGetValue(key, out int used) ? used : 0, Configuration.TypingMaxFrames)
                + "（msg_type=6 也占被动额度，用完就回不了话）");
            return;
        }
        await RequireRest().PostAsync(usersPath + Uri.EscapeDataString(user) + "/messages", new JsonObject
        {
            ["msg_type"] = 6,
            ["input_notify"] = new JsonObject { ["input_type"] = 1, ["input_second"] = Math.Clamp(seconds, 1, 10) }
        }, DestroyCancellationToken);
        interactor.Poke("已发出输入状态提示 " + Math.Clamp(seconds, 1, 10).ToString(CultureInfo.InvariantCulture) + " 秒，"
            + QQSendPolish.DescribeFrames(typingFrames[key], Configuration.TypingMaxFrames));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("撤回一条本机器人发出去的消息。messageId 给发送回来的 id；显示态 qqo-xxx 那种要先换成真实 id，否则平台报 40061001")]
    public async Task QQRecall(string targetId, string messageId, string targetType = "group")
    {
        bool isGroup = IsGroupTarget(targetType, out string targetError);
        if (targetError.Length > 0)
        {
            throw new Exception(targetError);
        }
        await RequireRest().DeleteAsync(TargetPath(isGroup, targetId.Trim()) + "/messages/" + Uri.EscapeDataString(messageId.Trim()), DestroyCancellationToken);
        interactor.Poke("[QQ官方机器人] 已撤回 " + messageId.Trim() + "（官方撤回窗口只有 2 分钟）");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("回应一次按钮回调（type 11/12）。平台侧其实要求 3 秒内回，插件收到就先替它回了，这个工具只在自动回执失败时用")]
    public async Task QQAnswerInteraction(string interactionId, int code = 0)
    {
        if (pendingInteractions.TryGetValue(interactionId.Trim(), out string? type) == false)
        {
            throw new Exception("没在这批事件里见过 interaction_id " + interactionId.Trim() + "；官方规定同一个 id 只能回应一次，多半已经自动回了");
        }
        await RequireRest().PutAsync(interactionPath + interactionId.Trim(), new JsonObject { ["code"] = code }, DestroyCancellationToken);
        pendingInteractions.Remove(interactionId.Trim());
        interactor.Poke("[QQ官方机器人] 互动 " + interactionId.Trim() + "（type " + type + "）已回执 code=" + code.ToString(CultureInfo.InvariantCulture));
    }

    // ============================================================
    // 自检
    // ============================================================

    [XmlFunction(FunctionMode.OneShot)]
    [Description("接入自检：AppID 是否配了、access_token 还能用多久、网关连到哪一步（心跳间隔、session、关闭码）、订阅位、机器人自身 openid、接口连通性、当前会话语义")]
    public async Task QQOfficialEnv()
    {
        List<string> lines = new()
        {
            "AppID：" + (Configuration.AppId.Trim().Length > 0 ? "已配（" + Configuration.AppId.Trim().Length + " 位）" : "没配，收发都不可用"),
            "AppSecret：" + (Configuration.AppSecret.Trim().Length > 0 ? "已配（只进本地配置，不回显）" : "没配"),
            "API 根地址：" + QQAccessToken.ResolveBaseUrl(Configuration) + (Configuration.ProxyUrl.Trim().Length > 0 ? "，HTTP 代理 " + Configuration.ProxyUrl.Trim() : "，直连"),
            "订阅位：" + QQIntents.Text(QQIntents.FromConfig(Configuration))
        };
        if (TryConfigure(out string configureError) == false)
        {
            lines.Add("❌ 传输层：" + configureError);
            interactor.Poke(string.Join("\n", lines));
            return;
        }
        lines.Add("token：" + (accessToken!.IsUsable
            ? "可用，剩 " + accessToken!.RemainingSeconds.ToString(CultureInfo.InvariantCulture) + " 秒（提前 180 秒续期）"
            : "还没取过"));
        lines.Add(gateway!.Describe());
        try
        {
            JsonElement self = await RequireRest().GetAsync("/users/@me", DestroyCancellationToken);
            botOpenId = QQJson.Text(self, "id");
            botResolveError = "";
            lines.Add("机器人自身：openid " + (botOpenId.Length > 0 ? botOpenId[..Math.Min(8, botOpenId.Length)] + "…（" + botOpenId.Length + " 位）" : "没拿到")
                + "，名称 " + QQJson.Text(self, "username") + "，挡回声就按这个 openid 认");
        }
        catch (Exception selfError)
        {
            lines.Add("❌ GET /users/@me：" + selfError.Message);
        }
        if (botOpenId.Length == 0 && botResolveError.Length > 0)
        {
            lines.Add("启动时取自身 openid 也没成（" + botResolveError + "）——这期间机器人自己发出去的消息可能被当成别人发的再吃进会话");
        }
        lines.Add("开关：发送" + (Configuration.EnableSend ? "开" : "关") + "，主动消息" + (Configuration.AllowProactiveMessage ? "开" : "关")
            + "，注入会话" + (Configuration.InjectInboundMessages ? "开" : "关") + "，详细日志" + (Configuration.DetailLog ? "开" : "关"));
        lines.Add("会话台账：" + lastInboundMessageId.Count.ToString(CultureInfo.InvariantCulture) + " 个会话有可被动回复的 msg_id，"
            + "已见事件 " + seenEvents.Count.ToString(CultureInfo.InvariantCulture) + " 条，互动待回执 " + pendingInteractions.Count.ToString(CultureInfo.InvariantCulture) + " 条");
        interactor.Poke(string.Join("\n", lines));
    }

    void DetailLog(string message)
    {
        if (Configuration.DetailLog)
        {
            logger.LogInformation("[QQOfficial] {Message}", message);
        }
    }
}
