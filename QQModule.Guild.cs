using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Alife.Function.FunctionCaller;

namespace Suran.QQ;

/// <summary>
/// 频道侧接口的专用封装：成员、身份组、子频道权限、API 权限、公告、精华/置顶、
/// 日程、音频、论坛帖子、消息列表、表态。
///
/// 文档索引（autogen）里没有这些老接口，但平台一直能用；这里按 botgo / botpy 的路径逐个包上，
/// 参数多的接口留一个 JSON 透传口，避免为了一个新字段就发一次插件版本。
/// 真碰上没包的，还有 QQApi 兜底。
/// </summary>
public partial class QQModule
{
    // ── 公共小工具 ──────────────────────────────────────────────

    async Task<string> InvokeAsync(string method, string path, JsonObject? body = null)
    {
        JsonElement result = method switch
        {
            "GET" => await RequireRest().GetAsync(path, DestroyCancellationToken),
            "POST" => await RequireRest().PostAsync(path, body, DestroyCancellationToken),
            "PUT" => await RequireRest().PutAsync(path, body, DestroyCancellationToken),
            "PATCH" => await RequireRest().PatchAsync(path, body, DestroyCancellationToken),
            "DELETE" => await RequireRest().DeleteAsync(path, DestroyCancellationToken),
            _ => throw new Exception("不支持的方法：" + method),
        };
        return result.ValueKind == JsonValueKind.Undefined ? "" : result.GetRawText();
    }

    /// <summary>把接口返回原样推回去：这些管理接口的字段又多又杂，转述反而丢信息。</summary>
    void PokeJson(string title, string json, int max = 3500)
    {
        if (json.Length > max)
        {
            json = json[..max] + "…";
        }
        interactor.Poke(title + (json.Length > 0 ? "：\n" + json : "（无返回体）"));
    }

    JsonObject ParseBody(string json, string name)
    {
        string raw = (json ?? "").Trim();
        if (raw.Length == 0)
        {
            return new JsonObject();
        }
        if (JsonNode.Parse(raw) is not JsonObject parsed)
        {
            throw new Exception(name + " 要是一个 JSON 对象");
        }
        return parsed;
    }

    static string Query(params (string Key, string Value)[] pairs)
    {
        List<string> parts = [];
        foreach ((string key, string value) in pairs)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(key + "=" + Uri.EscapeDataString(value.Trim()));
            }
        }
        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }

    // ── 频道成员 ───────────────────────────────────────────────

    [XmlFunction(FunctionMode.OneShot)]
    [Description("拉频道成员列表（分页）。after 传上一页最后一个成员的 user_id 翻页，limit 默认 100、最大 1000。50 QPS")]
    public async Task QQGuildMembers(string guildId, string after = "", int limit = 100)
    {
        string guild = Uri.EscapeDataString(Need(guildId, "guildId"));
        string json = await InvokeAsync("GET",
            "/guilds/" + guild + "/members" + Query(("after", after), ("limit", Math.Clamp(limit, 1, 1000).ToString())));
        PokeJson("频道 " + guildId.Trim() + " 的成员", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查频道里单个成员（昵称、加入时间、身份组、机器人标记）。50 QPS")]
    public async Task QQGuildMember(string guildId, string userId)
    {
        string json = await InvokeAsync("GET",
            "/guilds/" + Uri.EscapeDataString(Need(guildId, "guildId"))
            + "/members/" + Uri.EscapeDataString(Need(userId, "userId")));
        PokeJson("成员 " + userId.Trim(), json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("把成员踢出频道；addBlacklist=true 会顺手拉黑，不清记录就只是移除。50 QPS")]
    public async Task QQGuildKickMember(string guildId, string userId, bool addBlacklist = false)
    {
        string json = await InvokeAsync("DELETE",
            "/guilds/" + Uri.EscapeDataString(Need(guildId, "guildId"))
            + "/members/" + Uri.EscapeDataString(Need(userId, "userId"))
            + Query(("add_blacklist", addBlacklist ? "true" : "")));
        PokeJson("踢出成员 " + userId.Trim(), json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("给成员加/卸身份组。op=add 加，op=del 卸。50 QPS")]
    public async Task QQGuildMemberRole(string guildId, string userId, string roleId, string op = "add")
    {
        bool add = op.Trim().ToLowerInvariant() != "del";
        string json = await InvokeAsync(add ? "PUT" : "DELETE",
            "/guilds/" + Uri.EscapeDataString(Need(guildId, "guildId"))
            + "/members/" + Uri.EscapeDataString(Need(userId, "userId"))
            + "/roles/" + Uri.EscapeDataString(Need(roleId, "roleId")));
        PokeJson((add ? "已加身份组 " : "已卸身份组 ") + roleId.Trim() + "（成员 " + userId.Trim() + "）", json);
    }

    // ── 身份组 ─────────────────────────────────────────────────

    [XmlFunction(FunctionMode.OneShot)]
    [Description("拉频道身份组列表（每个组的 id/名称/颜色/权限位/是否可提及）。50 QPS")]
    public async Task QQGuildRoles(string guildId)
    {
        string json = await InvokeAsync("GET",
            "/guilds/" + Uri.EscapeDataString(Need(guildId, "guildId")) + "/roles");
        PokeJson("频道 " + guildId.Trim() + " 的身份组", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("新建频道身份组。name 必填；color 是十进制 RGB（0~16777215），hoist=是否单独分组显示，mentionable=是否可 @。"
        + "权限位、频道权限等复杂字段用 extraJson 透传（如 {\"permissions\":\"...\"}）。50 QPS")]
    public async Task QQCreateGuildRole(string guildId, string name, int color = 0, bool hoist = false,
        bool mentionable = false, string extraJson = "")
    {
        JsonObject payload = ParseBody(extraJson, "extraJson");
        payload["name"] = Need(name, "name");
        if (color > 0)
        {
            payload["color"] = color;
        }
        payload["hoist"] = hoist;
        payload["mentionable"] = mentionable;

        string json = await InvokeAsync("POST",
            "/guilds/" + Uri.EscapeDataString(Need(guildId, "guildId")) + "/roles", payload);
        PokeJson("已创建身份组", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("修改身份组。只传要改的字段：name / color / hoist（\"true\"/\"false\"）/ mentionable（\"true\"/\"false\"），"
        + "其它（权限位等）用 extraJson 透传。50 QPS")]
    public async Task QQUpdateGuildRole(string guildId, string roleId, string name = "", int color = -1,
        string hoist = "", string mentionable = "", string extraJson = "")
    {
        JsonObject payload = ParseBody(extraJson, "extraJson");
        if (name.Trim().Length > 0)
        {
            payload["name"] = name.Trim();
        }
        if (color >= 0)
        {
            payload["color"] = color;
        }
        if (hoist.Trim().Length > 0)
        {
            payload["hoist"] = bool.Parse(hoist.Trim());
        }
        if (mentionable.Trim().Length > 0)
        {
            payload["mentionable"] = bool.Parse(mentionable.Trim());
        }
        if (payload.Count == 0)
        {
            throw new Exception("什么都没要改（name/color/hoist/mentionable/extraJson 至少给一个）");
        }

        string json = await InvokeAsync("PATCH",
            "/guilds/" + Uri.EscapeDataString(Need(guildId, "guildId"))
            + "/roles/" + Uri.EscapeDataString(Need(roleId, "roleId")), payload);
        PokeJson("身份组 " + roleId.Trim() + " 已修改", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("删除频道身份组（不可撤销）。50 QPS")]
    public async Task QQDeleteGuildRole(string guildId, string roleId)
    {
        string json = await InvokeAsync("DELETE",
            "/guilds/" + Uri.EscapeDataString(Need(guildId, "guildId"))
            + "/roles/" + Uri.EscapeDataString(Need(roleId, "roleId")));
        PokeJson("身份组 " + roleId.Trim() + " 已删除", json);
    }

    // ── 子频道权限 / API 权限 ──────────────────────────────────

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查某个成员或身份组在子频道里的权限位。targetId 传 user_id 或 role_id 都行。50 QPS")]
    public async Task QQChannelPermission(string channelId, string targetId)
    {
        string json = await InvokeAsync("GET",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId"))
            + "/permissions/" + Uri.EscapeDataString(Need(targetId, "targetId")));
        PokeJson("子频道 " + channelId.Trim() + " 的权限", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("改成员/身份组在子频道里的权限。addPermissions 与 removePermissions 是文档里的权限字符串（如 \"1\"、\"4\"，多个用逗号）。50 QPS")]
    public async Task QQUpdateChannelPermission(string channelId, string targetId,
        string addPermissions = "", string removePermissions = "")
    {
        JsonObject payload = new();
        if (addPermissions.Trim().Length > 0)
        {
            payload["add"] = addPermissions.Trim();
        }
        if (removePermissions.Trim().Length > 0)
        {
            payload["remove"] = removePermissions.Trim();
        }
        if (payload.Count == 0)
        {
            throw new Exception("addPermissions 与 removePermissions 至少给一个");
        }

        string json = await InvokeAsync("PUT",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId"))
            + "/permissions/" + Uri.EscapeDataString(Need(targetId, "targetId")), payload);
        PokeJson("子频道 " + channelId.Trim() + " 的权限已更新", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查机器人当前在这个频道拿到了哪些 API 接口权限。50 QPS")]
    public async Task QQGuildApiPermissions(string guildId)
    {
        string json = await InvokeAsync("GET",
            "/guilds/" + Uri.EscapeDataString(Need(guildId, "guildId")) + "/api_permission");
        PokeJson("频道 " + guildId.Trim() + " 的 API 权限", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("在子频道里发一条「申请接口权限」的授权卡片。apiPath/apiMethod 写要申请的那个接口，如 GET /guilds/{guild_id}；"
        + "desc 写给用户看的理由。每天每频道最多 3 条。50 QPS")]
    public async Task QQRequestApiPermission(string guildId, string channelId, string apiPath, string apiMethod, string desc)
    {
        JsonObject payload = new()
        {
            ["channel_id"] = Need(channelId, "channelId"),
            ["api_identify"] = new JsonObject
            {
                ["path"] = Need(apiPath, "apiPath"),
                ["method"] = (apiMethod ?? "GET").Trim().ToUpperInvariant(),
            },
            ["desc"] = (desc ?? "").Trim(),
        };

        string json = await InvokeAsync("POST",
            "/guilds/" + Uri.EscapeDataString(Need(guildId, "guildId")) + "/api_permission/demand", payload);
        PokeJson("已发送授权链接", json);
    }

    // ── 公告 / 精华 / 置顶 ─────────────────────────────────────

    [XmlFunction(FunctionMode.OneShot)]
    [Description("给频道发公告。用 messageId 把已有消息转成公告（announcesType=0 普通，1 推荐）；"
        + "也可以用 channelId+content 直接发一条新公告（content 为文本，extraJson 可补 image 等字段）。50 QPS")]
    public async Task QQGuildAnnounce(string guildId, string messageId = "", int announcesType = 0,
        string channelId = "", string content = "", string extraJson = "")
    {
        JsonObject payload = ParseBody(extraJson, "extraJson");
        if (messageId.Trim().Length > 0)
        {
            payload["message_id"] = messageId.Trim();
        }
        else if (channelId.Trim().Length > 0 && content.Trim().Length > 0)
        {
            payload["channel_id"] = channelId.Trim();
            payload["message"] = new JsonObject { ["content"] = content.Trim() };
        }
        else
        {
            throw new Exception("要么给 messageId，要么同时给 channelId + content");
        }
        payload["announces_type"] = announcesType;

        string json = await InvokeAsync("POST",
            "/guilds/" + Uri.EscapeDataString(Need(guildId, "guildId")) + "/announces", payload);
        PokeJson("公告已发布", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("删除频道公告（按公告的消息 id）。50 QPS")]
    public async Task QQDeleteGuildAnnounce(string guildId, string messageId)
    {
        string json = await InvokeAsync("DELETE",
            "/guilds/" + Uri.EscapeDataString(Need(guildId, "guildId"))
            + "/announces/" + Uri.EscapeDataString(Need(messageId, "messageId")));
        PokeJson("公告已删除", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查子频道的精华/置顶消息列表。50 QPS")]
    public async Task QQChannelPins(string channelId)
    {
        string json = await InvokeAsync("GET",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId")) + "/pins");
        PokeJson("子频道 " + channelId.Trim() + " 的精华消息", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("把消息加进精华/置顶，或移出。op=add 加，op=del 移出。50 QPS")]
    public async Task QQChannelPin(string channelId, string messageId, string op = "add")
    {
        bool add = op.Trim().ToLowerInvariant() != "del";
        string json = await InvokeAsync(add ? "PUT" : "DELETE",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId"))
            + "/pins/" + Uri.EscapeDataString(Need(messageId, "messageId")));
        PokeJson((add ? "已设为精华：" : "已移出精华：") + messageId.Trim(), json);
    }

    // ── 日程 ───────────────────────────────────────────────────

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查子频道的日程列表，since 传秒级时间戳只取之后的。50 QPS")]
    public async Task QQChannelSchedules(string channelId, string since = "")
    {
        string json = await InvokeAsync("GET",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId")) + "/schedules" + Query(("since", since)));
        PokeJson("子频道 " + channelId.Trim() + " 的日程", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("在子频道建日程。start/end 是秒级时间戳字符串；jumpChannelId 是点进去跳转的子频道；"
        + "复杂字段（remind_type、attendees 等）用 extraJson 透传。50 QPS")]
    public async Task QQCreateSchedule(string channelId, string name, string start, string end,
        string jumpChannelId = "", string extraJson = "")
    {
        JsonObject payload = ParseBody(extraJson, "extraJson");
        payload["name"] = Need(name, "name");
        payload["start_timestamp"] = Need(start, "start");
        payload["end_timestamp"] = Need(end, "end");
        if (jumpChannelId.Trim().Length > 0)
        {
            payload["jump_channel_id"] = jumpChannelId.Trim();
        }

        string json = await InvokeAsync("POST",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId")) + "/schedules", payload);
        PokeJson("日程已创建", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("修改日程，只传要改的字段，统一用 JSON 透传（如 {\"name\":\"新名字\",\"start_timestamp\":\"1730000000\"}）。50 QPS")]
    public async Task QQUpdateSchedule(string channelId, string scheduleId, string json)
    {
        JsonObject payload = ParseBody(json, "json");
        if (payload.Count == 0)
        {
            throw new Exception("json 里至少给一个要改的字段");
        }

        string result = await InvokeAsync("PATCH",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId"))
            + "/schedules/" + Uri.EscapeDataString(Need(scheduleId, "scheduleId")), payload);
        PokeJson("日程 " + scheduleId.Trim() + " 已修改", result);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("删除日程。50 QPS")]
    public async Task QQDeleteSchedule(string channelId, string scheduleId)
    {
        string json = await InvokeAsync("DELETE",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId"))
            + "/schedules/" + Uri.EscapeDataString(Need(scheduleId, "scheduleId")));
        PokeJson("日程 " + scheduleId.Trim() + " 已删除", json);
    }

    // ── 音频 ───────────────────────────────────────────────────

    [XmlFunction(FunctionMode.OneShot)]
    [Description("上麦（机器人进入子频道语音）或下麦。op=on 上麦，op=off 下麦。50 QPS")]
    public async Task QQChannelMic(string channelId, string op = "on")
    {
        bool on = op.Trim().ToLowerInvariant() != "off";
        string json = await InvokeAsync(on ? "PUT" : "DELETE",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId")) + "/mic");
        PokeJson(on ? "已上麦" : "已下麦", json);
    }

    // ── 论坛帖子 ───────────────────────────────────────────────

    [XmlFunction(FunctionMode.OneShot)]
    [Description("拉子频道的帖子列表（论坛子频道用）。50 QPS")]
    public async Task QQChannelThreads(string channelId)
    {
        string json = await InvokeAsync("GET",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId")) + "/threads");
        PokeJson("子频道 " + channelId.Trim() + " 的帖子", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查单个帖子详情。50 QPS")]
    public async Task QQChannelThread(string channelId, string threadId)
    {
        string json = await InvokeAsync("GET",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId"))
            + "/threads/" + Uri.EscapeDataString(Need(threadId, "threadId")));
        PokeJson("帖子 " + threadId.Trim(), json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("修改帖子（标题/内容/格式），字段统一 JSON 透传，如 {\"title\":\"新标题\"}。50 QPS")]
    public async Task QQUpdateThread(string channelId, string threadId, string json)
    {
        JsonObject payload = ParseBody(json, "json");
        string result = await InvokeAsync("PUT",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId"))
            + "/threads/" + Uri.EscapeDataString(Need(threadId, "threadId")), payload);
        PokeJson("帖子 " + threadId.Trim() + " 已修改", result);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("删除帖子。50 QPS")]
    public async Task QQDeleteThread(string channelId, string threadId)
    {
        string json = await InvokeAsync("DELETE",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId"))
            + "/threads/" + Uri.EscapeDataString(Need(threadId, "threadId")));
        PokeJson("帖子 " + threadId.Trim() + " 已删除", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("拉帖子的回复列表。50 QPS")]
    public async Task QQThreadPosts(string channelId, string threadId)
    {
        string json = await InvokeAsync("GET",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId"))
            + "/threads/" + Uri.EscapeDataString(Need(threadId, "threadId")) + "/posts");
        PokeJson("帖子 " + threadId.Trim() + " 的回复", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("给帖子发回复，字段统一 JSON 透传，如 {\"content\":\"写点什么\"}。50 QPS")]
    public async Task QQCreateThreadPost(string channelId, string threadId, string json)
    {
        JsonObject payload = ParseBody(json, "json");
        if (payload.Count == 0)
        {
            throw new Exception("json 里至少给 content");
        }
        string result = await InvokeAsync("POST",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId"))
            + "/threads/" + Uri.EscapeDataString(Need(threadId, "threadId")) + "/posts", payload);
        PokeJson("已回复帖子", result);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("删除帖子回复。50 QPS")]
    public async Task QQDeleteThreadPost(string channelId, string threadId, string postId)
    {
        string json = await InvokeAsync("DELETE",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId"))
            + "/threads/" + Uri.EscapeDataString(Need(threadId, "threadId"))
            + "/posts/" + Uri.EscapeDataString(Need(postId, "postId")));
        PokeJson("回复 " + postId.Trim() + " 已删除", json);
    }

    // ── 频道消息 / 表态 ────────────────────────────────────────

    [XmlFunction(FunctionMode.OneShot)]
    [Description("拉子频道的历史消息（limit 默认 20、最大 100；before/after 传消息 id 翻页）。50 QPS")]
    public async Task QQChannelMessages(string channelId, int limit = 20, string before = "", string after = "")
    {
        string json = await InvokeAsync("GET",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId"))
            + "/messages" + Query(("limit", Math.Clamp(limit, 1, 100).ToString()), ("before", before), ("after", after)));
        PokeJson("子频道 " + channelId.Trim() + " 的消息", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("给频道消息贴表情（表态）或取消。emojiType 一般填 1（emoji 系统表情 id，如 4 是点赞），"
        + "自定义表情传 emojiType=2 且 emojiId 填表情 id。op=add 贴，op=del 取消。50 QPS")]
    public async Task QQChannelReaction(string channelId, string messageId, string emojiId,
        string emojiType = "1", string op = "add")
    {
        bool add = op.Trim().ToLowerInvariant() != "del";
        string json = await InvokeAsync(add ? "PUT" : "DELETE",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId"))
            + "/messages/" + Uri.EscapeDataString(Need(messageId, "messageId"))
            + "/reactions/" + Uri.EscapeDataString((emojiType ?? "1").Trim())
            + "/" + Uri.EscapeDataString(Need(emojiId, "emojiId")));
        PokeJson(add ? "已贴表情" : "已取消表情", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查这条消息上贴过某个表情的用户列表（分页 cookie 从控件外传不方便，这里只取第一页）。50 QPS")]
    public async Task QQChannelReactionUsers(string channelId, string messageId, string emojiId,
        string emojiType = "1")
    {
        string json = await InvokeAsync("GET",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId"))
            + "/messages/" + Uri.EscapeDataString(Need(messageId, "messageId"))
            + "/reactions/" + Uri.EscapeDataString((emojiType ?? "1").Trim())
            + "/" + Uri.EscapeDataString(Need(emojiId, "emojiId")));
        PokeJson("表态用户", json);
    }

    // ── 频道消息发送 / 撤回（老接口，文档 autogen 索引里没有） ──────────

    [XmlFunction(FunctionMode.OneShot)]
    [Description("往子频道发一条文本消息。可选 imageUrl 传图片地址（内容 + 图片一起发），quoteMessageId 引用某条消息。"
        + "频道消息没有群/单聊的被动回复窗口，也不支持 markdown 与按钮")]
    public async Task QQChannelSendText(string channelId, string text, string imageUrl = "", string quoteMessageId = "")
    {
        string content = (text ?? "").Trim();
        string image = (imageUrl ?? "").Trim();
        if (content.Length == 0 && image.Length == 0)
        {
            throw new Exception("text 与 imageUrl 至少给一个");
        }
        JsonObject body = new();
        if (content.Length > 0)
        {
            body["content"] = content;
        }
        if (image.Length > 0)
        {
            body["image"] = image;
        }
        if (string.IsNullOrWhiteSpace(quoteMessageId) == false)
        {
            body["message_reference"] = new JsonObject
            {
                ["message_id"] = quoteMessageId.Trim(),
                ["ignore_get_message_error"] = true
            };
        }
        string json = await InvokeAsync("POST",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId")) + "/messages", body);
        PokeJson("频道消息已发出", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("往子频道发一条 embed 图文卡片（频道专属）。embedJson 是官方 embed 对象，例如 "
        + "{\"title\":\"标题\",\"prompt\":\"摘要\",\"thumbnail\":{\"url\":\"https://...\"},"
        + "\"fields\":[{\"name\":\"字段\",\"value\":\"内容\"}]}")]
    public async Task QQChannelSendEmbed(string channelId, string embedJson)
    {
        JsonObject embed = ParseBody(embedJson, "embedJson");
        if (embed.Count == 0)
        {
            throw new Exception("embedJson 不能是空对象");
        }
        JsonObject body = new() { ["embed"] = embed };
        string json = await InvokeAsync("POST",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId")) + "/messages", body);
        PokeJson("embed 卡片已发出", json);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("撤回子频道里的一条消息（只能撤机器人自己发的）。")]
    public async Task QQChannelRecall(string channelId, string messageId)
    {
        string json = await InvokeAsync("DELETE",
            "/channels/" + Uri.EscapeDataString(Need(channelId, "channelId"))
            + "/messages/" + Uri.EscapeDataString(Need(messageId, "messageId")));
        PokeJson("已撤回频道消息 " + messageId.Trim(), json);
    }
}
