using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Alife.Function.FunctionCaller;

namespace Suran.QQ;

/// <summary>
/// 群管理剩下那一半：机器人群内状态、单个成员、群黑名单、入群自动审批策略。
/// 文档把这些逐个标着「白名单」或「内邀接入中」，所以这里的原则是：**平台拒就原样把 11253 报回去**，
/// 不猜默认值、不把失败说成成功。策略接口还有一点容易搞错：创建时 group_openids 与 group_ids **互斥**，
/// 修改时的 group_action 也必须沿用创建时那种形式。
/// </summary>
public partial class QQModule
{
    const int blacklistBatchLimit = 20;
    const int strategyMaxGroups = 100;
    const int whitelistMaxNumbers = 10000;

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查机器人在某个群里的状态：自己的 member_openid、入群时间、角色、是否允许主动推送、收消息设置（all/only_mention/mention_and_context）。"
        + "想知道「能不能主动发消息」就问它，别等 40034105。30 QPM，白名单能力")]
    public async Task QQGroupBotState(string groupOpenid)
    {
        JsonElement state = await RequireRest().GetAsync(groupsPath + Uri.EscapeDataString(Need(groupOpenid, "groupOpenid")) + "/bot_state", DestroyCancellationToken);
        interactor.Poke("机器人在群 " + Need(groupOpenid, "groupOpenid") + " 内："
            + "\nmember_openid=" + QQJson.Text(state, "member_openid") + "，角色 " + QQJson.Text(state, "member_role", "?")
            + "\n可主动推送：" + (QQJson.Has(state, "allow_proactive_msg") ? (QQJson.Flag(state, "allow_proactive_msg") ? "是" : "否") : "接口没给")
            + "，收消息设置：" + QQJson.Text(state, "recv_msg_setting", "（没给）")
            + "\n入群时间：" + QQJson.Text(state, "joined_at", "?")
            + "\n（recv_msg_setting 是后台「接收所有消息」那个开关的现值，mention_and_context 之外的值意味着群里非 @ 消息不会推给我们）");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查单个群成员的信息（昵称、角色、是否机器人、入群时间、union_openid）。30 QPM，文档标着内邀接入中，没权限会回 11253")]
    public async Task QQGroupMemberInfo(string groupOpenid, string memberOpenid)
    {
        JsonElement member = await RequireRest().GetAsync(groupsPath + Uri.EscapeDataString(Need(groupOpenid, "groupOpenid"))
            + "/members/" + Uri.EscapeDataString(Need(memberOpenid, "memberOpenid")), DestroyCancellationToken);
        interactor.Poke("成员 " + QQJson.Text(member, "member_openid") + "：" + QQJson.Text(member, "username", "（无昵称）")
            + " | " + QQJson.Text(member, "member_role", "?") + (QQJson.Flag(member, "bot") ? " | 机器人" : "")
            + " | 入群 " + QQJson.Text(member, "joined_at", "?")
            + (QQJson.Text(member, "union_openid").Length > 0 ? "\nunion_openid " + QQJson.Text(member, "union_openid") : ""));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查群黑名单（游标翻页，每页条数由平台定）。30 QPM，内邀能力")]
    public async Task QQGroupBlacklist(string groupOpenid, string cursor = "")
    {
        string path = groupsPath + Uri.EscapeDataString(Need(groupOpenid, "groupOpenid")) + "/member_blacklist?cursor=" + Uri.EscapeDataString(cursor.Trim());
        JsonElement body = await RequireRest().GetAsync(path, DestroyCancellationToken);
        List<JsonElement> users = QQJson.Array(body, "users").ToList();
        List<string> lines = new() { "群 " + groupOpenid.Trim() + " 黑名单 " + users.Count.ToString(CultureInfo.InvariantCulture) + " 条" + (QQJson.Text(body, "next_cursor").Length > 0 ? "（还有下一页）" : "") };
        foreach (JsonElement user in users)
        {
            lines.Add("- " + QQJson.Text(user, "username", "（无昵称）") + " | " + QQJson.Text(user, "member_openid")
                + " | 拉黑于 " + QQJson.Text(user, "banned_at", "?") + (QQJson.Flag(user, "bot") ? " | 机器人" : ""));
        }
        interactor.Poke(string.Join("\n", lines));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("群黑名单操作：op=add 拉黑、del 移出黑名单。memberOpenids 逗号分隔、单次最多 20 个。"
        + "文档写明：**成员还在群里时无法拉黑**（要先移除）。60 QPM，内邀能力")]
    public async Task QQGroupBlacklistOperate(string groupOpenid, string memberOpenids, string op = "add")
    {
        string action = op.Trim().ToLowerInvariant();
        if (action is not ("add" or "del"))
        {
            throw new Exception("op 只认 add/del，你给的是「" + op + "」");
        }
        List<string> members = SplitIds(memberOpenids);
        if (members.Count == 0)
        {
            throw new Exception("memberOpenids 是空的");
        }
        if (members.Count > blacklistBatchLimit)
        {
            throw new Exception("单次最多操作 " + blacklistBatchLimit.ToString(CultureInfo.InvariantCulture) + " 人，你给了 " + members.Count.ToString(CultureInfo.InvariantCulture) + " 个");
        }
        JsonArray ids = new();
        foreach (string member in members)
        {
            ids.Add(JsonValue.Create(member));
        }
        JsonElement result = await RequireRest().PostAsync(groupsPath + Uri.EscapeDataString(Need(groupOpenid, "groupOpenid")) + "/member_blacklist",
            new JsonObject { ["op"] = action, ["member_openids"] = ids }, DestroyCancellationToken);
        List<string> failed = QQJson.Array(result, "fail_openids").Select(item => QQJson.Text(item, "")).Where(text => text.Length > 0).ToList();
        interactor.Poke("黑名单 " + (action == "add" ? "拉黑" : "移出") + " " + members.Count.ToString(CultureInfo.InvariantCulture) + " 人"
            + (failed.Count > 0 ? "，失败 " + failed.Count.ToString(CultureInfo.InvariantCulture) + " 个：" + string.Join("、", failed) : "，全部成功"));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查入群自动审批策略列表（游标翻页）。能看到策略 ID、关联群、白名单号码数、启用状态与过期时间。60 QPM")]
    public async Task QQJoinApprovalStrategies(string cursor = "")
    {
        JsonElement body = await RequireRest().GetAsync("/v2/groups/join_approval_strategy?cursor=" + Uri.EscapeDataString(cursor.Trim()), DestroyCancellationToken);
        List<JsonElement> strategies = QQJson.Array(body, "strategies").ToList();
        List<string> lines = new() { "入群自动审批策略 " + strategies.Count.ToString(CultureInfo.InvariantCulture) + " 条" + (QQJson.Text(body, "next_cursor").Length > 0 ? "（还有下一页）" : "") };
        foreach (JsonElement strategy in strategies)
        {
            List<string> groups = QQJson.Array(strategy, "group_openids").Select(item => QQJson.Text(item, "")).Where(text => text.Length > 0).ToList();
            lines.Add("- " + QQJson.Text(strategy, "strategy_id") + " | " + QQJson.Text(strategy, "is_enable", "?")
                + " | 白名单 " + QQJson.Integer(strategy, "whitelist_user_count").ToString(CultureInfo.InvariantCulture) + " 个"
                + " | 过期 " + QQJson.Text(strategy, "expire_at", "?")
                + (groups.Count > 0 ? "\n  关联群 " + groups.Count.ToString(CultureInfo.InvariantCulture) + " 个：" + string.Join("、", groups.Take(5)) + (groups.Count > 5 ? "…" : "") : ""));
        }
        interactor.Poke(string.Join("\n", lines));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("创建入群自动审批策略。groupOpenids 与 groupIds **互斥**、各最多 100 个；isEnable on/off（默认 on）；expireAt 不传即一年。60 QPM")]
    public async Task QQCreateJoinApprovalStrategy(string groupOpenids = "", string groupIds = "", string isEnable = "on", string expireAt = "", string remark = "")
    {
        List<string> openids = SplitIds(groupOpenids);
        List<string> numbers = SplitIds(groupIds);
        if (openids.Count > 0 && numbers.Count > 0)
        {
            throw new Exception("groupOpenids 与 groupIds 只能给一个（平台规定互斥，同时给直接报错）");
        }
        if (openids.Count == 0 && numbers.Count == 0)
        {
            throw new Exception("groupOpenids 与 groupIds 至少给一个");
        }
        if (Math.Max(openids.Count, numbers.Count) > strategyMaxGroups)
        {
            throw new Exception("一条策略最多关联 " + strategyMaxGroups.ToString(CultureInfo.InvariantCulture) + " 个群");
        }
        JsonObject payload = new() { ["is_enable"] = isEnable.Trim().Length > 0 ? isEnable.Trim() : "on" };
        payload[openids.Count > 0 ? "group_openids" : "group_ids"] = StringArray(openids.Count > 0 ? openids : numbers);
        if (expireAt.Trim().Length > 0)
        {
            payload["expire_at"] = expireAt.Trim();
        }
        if (remark.Trim().Length > 0)
        {
            payload["remark"] = remark.Trim();
        }
        JsonElement result = await RequireRest().PostAsync("/v2/groups/join_approval_strategy", payload, DestroyCancellationToken);
        interactor.Poke("策略已创建：" + QQJson.Text(result, "strategy_id") + "，启用 " + QQJson.Text(result, "is_enable", "?") + "，过期 " + QQJson.Text(result, "expire_at", "?")
            + "\n（白名单号码与「执行」是另外两个接口：QQJoinApprovalWhitelist、QQExecuteJoinApprovalStrategy）");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("修改入群自动审批策略：启用开关、过期时间、备注，以及关联群的增删（groupActionJson 形如 {\"op\":\"add\",\"group_openids\":[…]}，"
        + "群标识形式必须与创建时一致）。60 QPM")]
    public async Task QQUpdateJoinApprovalStrategy(string strategyId, string isEnable = "", string expireAt = "", string remark = "", string groupActionJson = "")
    {
        JsonObject payload = new();
        if (isEnable.Trim().Length > 0)
        {
            payload["is_enable"] = isEnable.Trim();
        }
        if (expireAt.Trim().Length > 0)
        {
            payload["expire_at"] = expireAt.Trim();
        }
        if (remark.Trim().Length > 0)
        {
            payload["remark"] = remark.Trim();
        }
        if (groupActionJson.Trim().Length > 0)
        {
            if (JsonNode.Parse(groupActionJson.Trim()) is not JsonObject action)
            {
                throw new Exception("groupActionJson 要是一个对象，形如 {\"op\":\"add\",\"group_openids\":[\"…\"]}");
            }
            payload["group_action"] = action;
        }
        if (payload.Count == 0)
        {
            throw new Exception("什么都没要改（isEnable/expireAt/remark/groupActionJson 至少给一个）");
        }
        await RequireRest().PatchAsync("/v2/groups/join_approval_strategy/" + Uri.EscapeDataString(Need(strategyId, "strategyId")), payload, DestroyCancellationToken);
        interactor.Poke("策略 " + strategyId.Trim() + " 已修改");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("删除入群自动审批策略。60 QPM")]
    public async Task QQDeleteJoinApprovalStrategy(string strategyId)
    {
        await RequireRest().DeleteAsync("/v2/groups/join_approval_strategy/" + Uri.EscapeDataString(Need(strategyId, "strategyId")), DestroyCancellationToken);
        interactor.Poke("策略 " + strategyId.Trim() + " 已删除");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("立即执行一次入群自动审批策略（无请求体）。60 QPM")]
    public async Task QQExecuteJoinApprovalStrategy(string strategyId)
    {
        await RequireRest().PostAsync("/v2/groups/join_approval_strategy/" + Uri.EscapeDataString(Need(strategyId, "strategyId")) + "/execute",
            new JsonObject(), DestroyCancellationToken);
        interactor.Poke("策略 " + strategyId.Trim() + " 已触发执行");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("改策略的白名单号码：op=add 新增、del 删除，qqNumbers 逗号分隔的 QQ 号，单次最多 10000 个（平台按字符串处理避免精度问题）。60 QPM")]
    public async Task QQJoinApprovalWhitelist(string strategyId, string qqNumbers, string op = "add")
    {
        string action = op.Trim().ToLowerInvariant();
        if (action is not ("add" or "del"))
        {
            throw new Exception("op 只认 add/del");
        }
        List<string> numbers = SplitIds(qqNumbers);
        if (numbers.Count == 0)
        {
            throw new Exception("qqNumbers 是空的");
        }
        if (numbers.Count > whitelistMaxNumbers)
        {
            throw new Exception("单次最多 " + whitelistMaxNumbers.ToString(CultureInfo.InvariantCulture) + " 个号码");
        }
        JsonElement result = await RequireRest().PostAsync("/v2/groups/join_approval_strategy/" + Uri.EscapeDataString(Need(strategyId, "strategyId")) + "/whitelist_users",
            new JsonObject { ["op"] = action, ["whitelist_users"] = StringArray(numbers) }, DestroyCancellationToken);
        interactor.Poke("白名单已" + (action == "add" ? "新增 " : "移除 ") + numbers.Count.ToString(CultureInfo.InvariantCulture) + " 个号码，策略现有 "
            + QQJson.Integer(result, "whitelist_user_count").ToString(CultureInfo.InvariantCulture) + " 个（估算值），更新于 " + QQJson.Text(result, "updated_at", "?"));
    }

    static JsonArray StringArray(List<string> values)
    {
        JsonArray array = new();
        foreach (string value in values)
        {
            array.Add(JsonValue.Create(value));
        }
        return array;
    }
}
