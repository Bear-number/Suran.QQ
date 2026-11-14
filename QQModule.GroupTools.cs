using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging;

namespace Suran.QQ;

/// <summary>
/// 群聊管理与按钮键盘那一半工具。接口形状全部按官方文档当前版本抄（每页/每批的上限、QPM、错误码都写在说明里）：
/// 这些能力大多标着「白名单/内邀」，没权限时平台回 11253——本文件的做法是**如实把 11253 报回去**，
/// 不猜默认值、不假装成功。
/// </summary>
public partial class QQModule
{
    const int groupMembersPerPage = 30;
    const int groupMembersMaxPages = 10;
    const int muteBatchLimit = 20;
    const int removeBatchLimit = 20;
    const int joinRequestMaxLimit = 50;
    const int muteMaxDays = 30;
    const int keyboardMaxRows = 5;
    const int keyboardMaxColumns = 5;
    const int keyboardDataMaxChars = 100;

    /// <summary>高风险群管理工具的总开关：默认关，调用直接报错，避免模型手滑踢人/禁言。</summary>
    void RequireSensitiveTools()
    {
        if (Configuration.EnableSensitiveTools == false)
        {
            throw new Exception("高风险群管理工具已在配置里关闭（「工具·高风险群管理」），确实要用请先打开它");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查群基本信息（群名、简介、分类、标签、成员数）。30 QPM，白名单能力，没权限回 11253 会原样报出")]
    public async Task QQGroupInfo(string groupOpenid)
    {
        JsonElement info = await RequireRest().GetAsync(groupsPath + Uri.EscapeDataString(Need(groupOpenid, "groupOpenid")) + "/info", DestroyCancellationToken);
        List<string> tags = QQJson.Array(info, "group_tags").Select(tag => QQJson.Text(tag, "")).Where(text => text.Length > 0).ToList();
        interactor.Poke("群 " + QQJson.Text(info, "group_openid") + "：" + QQJson.Text(info, "group_name")
            + "\n成员 " + QQJson.Integer(info, "group_member_num").ToString(CultureInfo.InvariantCulture) + " 人"
            + "，分类 " + QQJson.Text(info, "group_class_text", "（无）")
            + "\n简介：" + QQJson.Text(info, "group_finger_memo", "（无）")
            + (tags.Count > 0 ? "\n标签：" + string.Join("、", tags) : ""));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("列群成员（member_openid、昵称、角色、入群时间、是否机器人）。接口每页最多 30 条，本工具按 next_cursor 自己翻页到 limit 或末页")]
    public async Task QQGroupMembers(string groupOpenid, int limit = 30, string role = "")
    {
        string group = Need(groupOpenid, "groupOpenid");
        int want = Math.Clamp(limit, 1, groupMembersPerPage * groupMembersMaxPages);
        string wantRole = role.Trim().ToLowerInvariant();
        List<JsonElement> members = new();
        string cursor = "";
        for (int page = 0; page < groupMembersMaxPages && members.Count < want; page++)
        {
            string path = groupsPath + Uri.EscapeDataString(group) + "/members?cursor=" + Uri.EscapeDataString(cursor);
            JsonElement body = await RequireRest().GetAsync(path, DestroyCancellationToken);
            foreach (JsonElement member in QQJson.Array(body, "members"))
            {
                members.Add(member);
            }
            cursor = QQJson.Text(body, "next_cursor");
            if (cursor.Length == 0)
            {
                break;
            }
        }
        List<string> lines = new();
        IEnumerable<JsonElement> shown = members;
        if (wantRole.Length > 0)
        {
            shown = members.Where(member => QQJson.Text(member, "member_role").Equals(wantRole, StringComparison.OrdinalIgnoreCase));
        }
        List<JsonElement> rows = shown.Take(want).ToList();
        lines.Add("群 " + group + " 成员：抓到 " + members.Count.ToString(CultureInfo.InvariantCulture) + " 条"
            + (wantRole.Length > 0 ? "，筛选 " + wantRole + " 后 " + rows.Count.ToString(CultureInfo.InvariantCulture) + " 条" : "")
            + "（下一页游标 " + (cursor.Length > 0 ? "还有" : "已到末页") + "）");
        foreach (JsonElement member in rows)
        {
            lines.Add("- " + QQJson.Text(member, "username", "（无昵称）") + " | " + QQJson.Text(member, "member_openid")
                + " | " + QQJson.Text(member, "member_role", "?")
                + (QQJson.Flag(member, "bot") ? " | 机器人" : "")
                + " | 入群 " + QQJson.Text(member, "joined_at", "?"));
        }
        interactor.Poke(string.Join("\n", lines));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("设置/解除群成员禁言。op：add 增加禁言、update 改到期时间、del 解除。memberOpenids 逗号或空格分隔，单次最多 20 人；"
        + "只能操作普通成员（群主、管理员、机器人会被拒）；minutes 最长 30 天；60 QPM")]
    public async Task QQGroupMuteMembers(string groupOpenid, string memberOpenids, int minutes = 60, string op = "add")
    {
        RequireSensitiveTools();
        string action = op.Trim().ToLowerInvariant();
        if (action is not ("add" or "update" or "del"))
        {
            throw new Exception("op 只认 add/update/del，你给的是「" + op + "」");
        }
        List<string> members = SplitIds(memberOpenids);
        if (members.Count == 0)
        {
            throw new Exception("memberOpenids 是空的");
        }
        if (members.Count > muteBatchLimit)
        {
            throw new Exception("单次最多禁言 " + muteBatchLimit.ToString(CultureInfo.InvariantCulture) + " 人，你给了 " + members.Count.ToString(CultureInfo.InvariantCulture) + " 个，分批来");
        }
        string expireAt = action == "del" ? "" : MuteExpireAt(minutes);
        JsonArray items = new();
        foreach (string member in members)
        {
            JsonObject item = new() { ["op"] = action, ["member_openid"] = member };
            item["mute_expire_at"] = expireAt;
            items.Add(item);
        }
        await RequireRest().PostAsync(groupsPath + Uri.EscapeDataString(Need(groupOpenid, "groupOpenid")) + "/restrict_chat_setting",
            new JsonObject { ["members"] = items }, DestroyCancellationToken);
        interactor.Poke("已对 " + members.Count.ToString(CultureInfo.InvariantCulture) + " 人执行禁言 " + action
            + (action == "del" ? "（立即解除）" : "，到期 " + expireAt));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查群禁言设置：全员禁言模式（none/always/schedule）与当前正在禁言的成员。30 QPM")]
    public async Task QQGroupMuteSetting(string groupOpenid)
    {
        RequireSensitiveTools();
        JsonElement body = await RequireRest().GetAsync(groupsPath + Uri.EscapeDataString(Need(groupOpenid, "groupOpenid")) + "/restrict_chat_setting", DestroyCancellationToken);
        string mode = "none";
        if (body.TryGetProperty("global_rule", out JsonElement global) && global.ValueKind == JsonValueKind.Object)
        {
            mode = QQJson.Text(global, "mode", "none");
        }
        List<JsonElement> muted = QQJson.Array(body, "members").ToList();
        List<string> lines = new()
        {
            "群 " + Need(groupOpenid, "groupOpenid") + " 禁言：全员模式 " + mode + "，当前禁言中 " + muted.Count.ToString(CultureInfo.InvariantCulture) + " 人（不含已过期）"
        };
        foreach (JsonElement member in muted.Take(20))
        {
            lines.Add("- " + QQJson.Text(member, "member_openid") + " 到 " + QQJson.Text(member, "mute_expire_at", "?"));
        }
        if (muted.Count > 20)
        {
            lines.Add("… 只列前 20 条");
        }
        interactor.Poke(string.Join("\n", lines));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("拉入群申请列表（默认 20 条，最多 50）。返回里的 join_request_id 与 member_openid 是审批要用的两个参数。30 QPM")]
    public async Task QQGroupJoinRequests(string groupOpenid, int limit = 20)
    {
        int want = Math.Clamp(limit, 1, joinRequestMaxLimit);
        string path = groupsPath + Uri.EscapeDataString(Need(groupOpenid, "groupOpenid")) + "/join_request_list?limit="
            + want.ToString(CultureInfo.InvariantCulture);
        JsonElement body = await RequireRest().GetAsync(path, DestroyCancellationToken);
        List<JsonElement> requests = QQJson.Array(body, "list").ToList();
        List<string> lines = new()
        {
            "群 " + Need(groupOpenid, "groupOpenid") + " 入群申请 " + requests.Count.ToString(CultureInfo.InvariantCulture) + " 条"
                + (QQJson.Text(body, "next_cursor").Length > 0 ? "（还有下一页）" : "")
        };
        foreach (JsonElement request in requests)
        {
            lines.Add("- " + QQJson.Text(request, "username", "（无昵称）") + " | member_openid=" + QQJson.Text(request, "member_openid")
                + " | " + QQJson.Text(request, "apply_source", "?") + " | 申请于 " + QQJson.Text(request, "apply_at", "?")
                + (QQJson.Text(request, "risk_tips").Length > 0 ? " | 风险提示：" + QQJson.Text(request, "risk_tips") : "")
                + (QQJson.Text(request, "join_request_id").Length > 0 ? "\n  join_request_id=" + QQJson.Text(request, "join_request_id") : ""));
        }
        interactor.Poke(string.Join("\n", lines));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("审批一条入群申请：approve 通过、decline 拒绝（可带拒绝理由、可同时拉黑）。机器人得是群管理员。60 QPM")]
    public async Task QQGroupApproveJoinRequest(string groupOpenid, string memberOpenid, string joinRequestId, bool approve = true, string rejectReason = "", bool addToBlacklist = false)
    {
        RequireSensitiveTools();
        string op = approve ? "approve" : "decline";
        JsonObject payload = new() { ["op"] = op };
        if (joinRequestId.Trim().Length > 0)
        {
            payload["join_request_id"] = joinRequestId.Trim();
        }
        if (approve == false && rejectReason.Trim().Length > 0)
        {
            payload["reject_reason"] = rejectReason.Trim();
        }
        if (approve == false && addToBlacklist)
        {
            payload["add_to_member_blacklist"] = true;
        }
        await RequireRest().PostAsync(groupsPath + Uri.EscapeDataString(Need(groupOpenid, "groupOpenid"))
            + "/approval_join_request/" + Uri.EscapeDataString(Need(memberOpenid, "memberOpenid")), payload, DestroyCancellationToken);
        interactor.Poke("入群申请已" + (approve ? "通过" : "拒绝") + "：" + memberOpenid.Trim() + (addToBlacklist && approve == false ? "（并加入群黑名单）" : ""));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("批量移除群成员，单次最多 20 人，可同时加入黑名单。文档标着「内邀接入中」，没权限会回 11253 并原样报出。30 QPM")]
    public async Task QQGroupRemoveMembers(string groupOpenid, string memberOpenids, bool addToBlacklist = false)
    {
        RequireSensitiveTools();
        List<string> members = SplitIds(memberOpenids);
        if (members.Count == 0)
        {
            throw new Exception("memberOpenids 是空的");
        }
        if (members.Count > removeBatchLimit)
        {
            throw new Exception("单次最多移除 " + removeBatchLimit.ToString(CultureInfo.InvariantCulture) + " 人，你给了 " + members.Count.ToString(CultureInfo.InvariantCulture) + " 个");
        }
        JsonArray ids = new();
        foreach (string member in members)
        {
            ids.Add(JsonValue.Create(member));
        }
        JsonObject payload = new() { ["member_openids"] = ids };
        if (addToBlacklist)
        {
            payload["add_to_member_blacklist"] = true;
        }
        JsonElement result = await RequireRest().PostAsync(groupsPath + Uri.EscapeDataString(Need(groupOpenid, "groupOpenid")) + "/batch_remove_members",
            payload, DestroyCancellationToken);
        List<string> failed = QQJson.Array(result, "add_to_member_blacklist_fail_openids").Select(item => QQJson.Text(item, "")).ToList();
        interactor.Poke("移除结果：" + QQJson.Text(result, "remove_members_result", "（接口没回结论）")
            + (failed.Count > 0 ? "\n拉黑失败 " + failed.Count.ToString(CultureInfo.InvariantCulture) + " 个：" + string.Join("、", failed) : ""));
    }

    static string MuteExpireAt(int minutes)
    {
        int capped = Math.Clamp(minutes, 1, muteMaxDays * 24 * 60);
        return DateTime.Now.AddMinutes(capped).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    static List<string> SplitIds(string raw) =>
        (raw ?? "").Split(new[] { ',', '，', '、', ';', '；', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(item => item.Trim()).Where(item => item.Length > 0).Distinct().ToList();

    static string Need(string value, string name)
    {
        if (value.Trim().Length == 0)
        {
            throw new Exception(name + " 不能为空（官方给的是 32 位十六进制 openid，不是群里那串数字）");
        }
        return value.Trim();
    }

    /// <summary>单个按钮的点击策略（来自按钮 JSON 的 once / ttl / limit）。</summary>
    sealed class ButtonRule
    {
        public string Data = "";
        public bool Once;
        public int Limit;
        public int TtlSeconds;
    }

    /// <summary>
    /// 指令按钮的追踪前缀：发出去时给 data 加上，用户点回来后先按前缀认领、再把前缀剥掉。
    /// 这样"用户正常发一句和 data 一样的文字"不会被误判成点按钮。
    /// </summary>
    const string buttonToken = "#qb#";

    static string WithButtonToken(string data) =>
        data.StartsWith(buttonToken, StringComparison.Ordinal) ? data : buttonToken + data;

    static string StripButtonToken(string text) =>
        text.StartsWith(buttonToken, StringComparison.Ordinal) ? text[buttonToken.Length..] : text;

    /// <summary>一次发送的键盘结构与按钮策略。</summary>
    sealed class KeyboardPlan
    {
        public JsonObject Keyboard = new();
        public List<ButtonRule> Rules = new();
    }

    /// <summary>
    /// 把模型常见的几种偷懒写法补成平台要的 keyboard 结构，省得每次都卡在格式上：
    ///   {"rows":[{"buttons":[{...}]}]}  —— 标准写法，只补齐缺失的 label / visited_label / style / type / data
    ///   [{"label":"A","data":"a"}, ...] —— 自动包成一行
    ///   [[{...}],[{...}]]               —— 每项一行
    /// 返回 null 时 problem 说明哪里不对。硬限制按平台：≤5 行 × 每行 ≤5 列、style 0/1、action.data ≤100 字符。
    /// type=2 指令按钮默认补 enter=true（官方默认 false 只会把内容插进输入框，用户会以为按钮坏了）。
    /// </summary>
    KeyboardPlan BuildKeyboard(string keyboardJson, out string problem)
    {
        problem = "";
        JsonNode parsed;
        try
        {
            parsed = JsonNode.Parse(keyboardJson);
        }
        catch (JsonException formatError)
        {
            problem = "键盘 JSON 解析失败：" + formatError.Message;
            return null;
        }

        JsonArray rows;
        if (parsed is JsonObject keyboard && keyboard["content"] is JsonObject content && content["rows"] is JsonArray contentRows)
        {
            // 官方原样结构 keyboard.content.rows
            rows = contentRows;
        }
        else if (parsed is JsonObject flatKeyboard && flatKeyboard["rows"] is JsonArray existingRows)
        {
            rows = existingRows;
        }
        else if (parsed is JsonArray array)
        {
            rows = new JsonArray();
            if (array.Count > 0 && array[0] is JsonArray)
            {
                foreach (JsonNode rowNode in array)
                {
                    if (rowNode is not JsonArray rowArray)
                    {
                        problem = "按行给按钮时，每一项都要是数组";
                        return null;
                    }
                    rows.Add(new JsonObject { ["buttons"] = rowArray.DeepClone() });
                }
            }
            else if (array.Count > 0)
            {
                rows.Add(new JsonObject { ["buttons"] = array.DeepClone() });
            }
        }
        else
        {
            problem = "键盘要是一个对象，形如 {\"rows\":[{\"buttons\":[{...}]}]}，或者直接给按钮数组 […]";
            return null;
        }

        if (rows.Count == 0)
        {
            problem = "键盘里一个按钮都没有";
            return null;
        }
        if (rows.Count > keyboardMaxRows)
        {
            problem = "rows 最多 " + keyboardMaxRows.ToString(CultureInfo.InvariantCulture) + " 行，给了 " + rows.Count.ToString(CultureInfo.InvariantCulture) + " 行";
            return null;
        }

        KeyboardPlan plan = new();
        JsonArray normalizedRows = new();
        int buttonIndex = 0;
        foreach (JsonNode rowNode in rows)
        {
            if (rowNode is not JsonObject row || row["buttons"] is not JsonArray buttons)
            {
                problem = "每一行都要有 buttons 数组";
                return null;
            }
            if (buttons.Count == 0)
            {
                problem = "有一行的 buttons 是空的";
                return null;
            }
            if (buttons.Count > keyboardMaxColumns)
            {
                problem = "每行最多 " + keyboardMaxColumns.ToString(CultureInfo.InvariantCulture) + " 个按钮，给了 " + buttons.Count.ToString(CultureInfo.InvariantCulture) + " 个";
                return null;
            }

            JsonArray normalizedButtons = new();
            foreach (JsonNode buttonNode in buttons)
            {
                if (buttonNode is not JsonObject button)
                {
                    problem = "按钮本身要是一个对象";
                    return null;
                }
                JsonObject normalized = NormalizeButton(button, out problem, out ButtonRule rule);
                if (normalized == null)
                {
                    return null;
                }
                // 平台要求每个按钮有唯一 id，没给就按顺序补
                string buttonId = NodeText(button, "id");
                normalized["id"] = buttonId.Length > 0
                    ? buttonId
                    : (++buttonIndex).ToString(CultureInfo.InvariantCulture);
                normalizedButtons.Add(normalized);
                if (rule != null && rule.Data.Length > 0)
                {
                    plan.Rules.Add(rule);
                }
            }
            normalizedRows.Add(new JsonObject { ["buttons"] = normalizedButtons });
        }

        plan.Keyboard["content"] = new JsonObject { ["rows"] = normalizedRows };
        return plan;
    }

    /// <summary>单个按钮：标准写法原样保留并补默认值；简化写法（label/data/style/type）拼成 render_data + action。</summary>
    JsonObject NormalizeButton(JsonObject button, out string problem, out ButtonRule rule)
    {
        problem = "";
        rule = null;
        JsonObject render = button["render_data"] as JsonObject;
        JsonObject action = button["action"] as JsonObject;

        string label = render != null ? NodeText(render, "label") : "";
        if (label.Length == 0)
        {
            label = NodeText(button, "label");
        }
        if (label.Length == 0)
        {
            label = NodeText(button, "text");
        }
        if (label.Length == 0)
        {
            label = NodeText(button, "name");
        }
        if (label.Length == 0)
        {
            problem = "按钮要有文字（label）";
            return null;
        }

        int style = 0;
        if (render != null && int.TryParse(NodeText(render, "style"), out int renderStyle))
        {
            style = renderStyle;
        }
        else if (int.TryParse(NodeText(button, "style"), out int flatStyle))
        {
            style = flatStyle;
        }
        if (style is not (0 or 1))
        {
            problem = "render_data.style 只认 0 和 1，给了 " + style.ToString(CultureInfo.InvariantCulture);
            return null;
        }

        int actionType = 2;   // 默认「指令」：点一下把 data 当消息发回来
        if (action != null && int.TryParse(NodeText(action, "type"), out int parsedType))
        {
            actionType = parsedType;
        }
        else if (int.TryParse(NodeText(button, "type"), out int flatType))
        {
            actionType = flatType;
        }
        if (actionType is not (0 or 1 or 2))
        {
            problem = "action.type 只认 0（跳转）/ 1（回调）/ 2（指令），给了 " + actionType.ToString(CultureInfo.InvariantCulture);
            return null;
        }
        // 回调按钮降级：平台推不到互动事件时，改成指令按钮，点一下就发
        if (actionType == 1 && Configuration.KeyboardCallbackToCommand)
        {
            actionType = 2;
        }

        string data = action != null ? NodeText(action, "data") : "";
        if (data.Length == 0)
        {
            data = NodeText(button, "data");
        }
        if (data.Length == 0)
        {
            data = NodeText(button, "url");
        }
        if (data.Length == 0)
        {
            data = label;   // 至少让点击有内容回来，不卡在必填项
        }
        if (actionType == 0 && data.StartsWith("http", StringComparison.OrdinalIgnoreCase) == false)
        {
            problem = "action.type=0 是跳转链接，data 必须是 http(s) 地址";
            return null;
        }
        // 指令按钮点下去会以 data 作为一条普通消息回来：加追踪前缀，避免和正常聊天撞车。
        // 只有开了「按钮限次策略」才需要辨识点击，否则前缀只会把用户输入框里的文字弄脏
        if (actionType == 2 && Configuration.ButtonPolicyToken && Configuration.ButtonLimitEnabled)
        {
            data = WithButtonToken(data);
        }
        if (data.Length > keyboardDataMaxChars)
        {
            problem = "action.data 最长 " + keyboardDataMaxChars.ToString(CultureInfo.InvariantCulture) + " 字符（含自动加的追踪前缀），给了 " + data.Length.ToString(CultureInfo.InvariantCulture);
            return null;
        }

        // 指令按钮默认「点一下就发」；显式写了 enter 就听它的
        bool enter = actionType == 2;
        if (action != null && bool.TryParse(NodeText(action, "enter"), out bool explicitEnter))
        {
            enter = explicitEnter;
        }

        rule = new ButtonRule
        {
            Data = data,
            Once = NodeBool(button, "once"),
            Limit = NodeInt(button, "limit"),
            TtlSeconds = NodeInt(button, "ttl")
        };

        string visited = render != null ? NodeText(render, "visited_label") : "";
        string unsupportTips = action != null ? NodeText(action, "unsupport_tips") : "";
        JsonNode permission = action?["permission"]?.DeepClone();
        return new JsonObject
        {
            ["render_data"] = new JsonObject
            {
                ["label"] = label,
                ["visited_label"] = visited.Length > 0 ? visited : label,
                ["style"] = style
            },
            ["action"] = new JsonObject
            {
                ["type"] = actionType,
                // 没写权限就按「所有人可点」，否则部分客户端会当成不可用按钮
                ["permission"] = permission ?? new JsonObject { ["type"] = 2 },
                ["data"] = data,
                ["unsupport_tips"] = unsupportTips.Length > 0 ? unsupportTips : "当前版本不支持这个按钮，请升级 QQ",
                ["enter"] = enter
            }
        };
    }

    // ── 按钮点击账本（限次/限人/截止）────────────────────────────

    sealed class ButtonLedgerEntry
    {
        public DateTime ExpireAt = DateTime.MaxValue;
        public int Limit;
        public readonly Dictionary<string, int> Used = new(StringComparer.OrdinalIgnoreCase);
    }

    static readonly Dictionary<string, ButtonLedgerEntry> buttonLedger = new(StringComparer.Ordinal);
    static bool buttonLedgerDirty;

    string ButtonLedgerPath => Path.Combine(AlifePath.StorageFolderPath, Configuration.DataFolder.Trim(), "button-ledger.json");

    /// <summary>启动时恢复账本：重启后「每人一次 / 限次」仍然成立；过期的条目直接丢。</summary>
    void LoadButtonLedger()
    {
        if (Configuration.ButtonLedgerPersist == false || File.Exists(ButtonLedgerPath) == false)
        {
            return;
        }
        try
        {
            if (JsonNode.Parse(File.ReadAllText(ButtonLedgerPath)) is not JsonObject root)
            {
                return;
            }
            DateTime now = DateTime.Now;
            foreach (KeyValuePair<string, JsonNode> pair in root)
            {
                if (pair.Value is not JsonObject entry)
                {
                    continue;
                }
                long expireMs = (long)(entry["expire"]?.GetValue<long>() ?? 0);
                DateTime expire = expireMs > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(expireMs).LocalDateTime
                    : DateTime.MaxValue;
                if (expire < now)
                {
                    continue;
                }
                ButtonLedgerEntry restored = new()
                {
                    ExpireAt = expire,
                    Limit = (int)(entry["limit"]?.GetValue<int>() ?? 0)
                };
                if (entry["used"] is JsonObject used)
                {
                    foreach (KeyValuePair<string, JsonNode> item in used)
                    {
                        if (item.Value != null)
                        {
                            restored.Used[item.Key] = item.Value.GetValue<int>();
                        }
                    }
                }
                buttonLedger[pair.Key] = restored;
            }
            DetailLog("按钮账本已恢复 " + buttonLedger.Count.ToString(CultureInfo.InvariantCulture) + " 条");
        }
        catch (Exception ex)
        {
            DetailLog("读按钮账本失败：" + ex.Message);
        }
    }

    /// <summary>落盘账本。写失败只记日志，不影响点击判定。</summary>
    void SaveButtonLedger()
    {
        if (Configuration.ButtonLedgerPersist == false)
        {
            buttonLedgerDirty = false;
            return;
        }
        try
        {
            JsonObject root = new();
            foreach (KeyValuePair<string, ButtonLedgerEntry> pair in buttonLedger)
            {
                JsonObject used = new();
                foreach (KeyValuePair<string, int> item in pair.Value.Used)
                {
                    used[item.Key] = item.Value;
                }
                root[pair.Key] = new JsonObject
                {
                    ["expire"] = pair.Value.ExpireAt == DateTime.MaxValue
                        ? 0
                        : new DateTimeOffset(pair.Value.ExpireAt).ToUnixTimeMilliseconds(),
                    ["limit"] = pair.Value.Limit,
                    ["used"] = used
                };
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ButtonLedgerPath)!);
            File.WriteAllText(ButtonLedgerPath, root.ToJsonString(), new UTF8Encoding(false));
            buttonLedgerDirty = false;
        }
        catch (Exception ex)
        {
            DetailLog("写按钮账本失败：" + ex.Message);
        }
    }

    /// <summary>发完键盘后登记每个按钮的策略；同一会话同一个 data 重发会重置计数（等于「新消息上的新按钮」）。</summary>
    void RegisterButtons(bool isGroup, string targetId, KeyboardPlan plan)
    {
        if (Configuration.ButtonLimitEnabled == false || plan.Rules.Count == 0)
        {
            return;
        }
        string conversation = ConversationKey(isGroup, targetId);
        DateTime now = DateTime.Now;
        int defaultTtl = Math.Max(0, Configuration.ButtonDefaultTtlSeconds);
        foreach (ButtonRule rule in plan.Rules)
        {
            int limit = rule.Limit > 0
                ? rule.Limit
                : (rule.Once || Configuration.ButtonDefaultOnce ? 1 : 0);
            int ttl = rule.TtlSeconds > 0 ? rule.TtlSeconds : defaultTtl;
            buttonLedger[conversation + "|" + rule.Data] = new ButtonLedgerEntry
            {
                ExpireAt = ttl > 0 ? now.AddSeconds(ttl) : DateTime.MaxValue,
                Limit = limit
            };
        }
        PruneButtonLedger();
        buttonLedgerDirty = true;
    }

    /// <summary>入站消息像不像某个按钮被点；返回 true 表示这次点击超出策略、应当拦下。</summary>
    async Task<bool> ButtonClickBlocked(bool isGroup, string targetId, string senderId, string content, string messageId)
    {
        if (Configuration.ButtonLimitEnabled == false)
        {
            return false;
        }
        string text = (content ?? "").Trim();
        if (text.Length == 0)
        {
            return false;
        }
        string conversation = ConversationKey(isGroup, targetId);
        foreach (string key in buttonLedger.Keys.ToList())
        {
            int split = key.IndexOf('|');
            if (split < 0 || string.Equals(key[..split], conversation, StringComparison.Ordinal) == false)
            {
                continue;
            }
            string data = key[(split + 1)..];
            if (data.Length == 0)
            {
                continue;
            }
            // 指令按钮点下去的内容通常是 data 本身，偶尔前面带 "@机器人 "
            if (string.Equals(text, data, StringComparison.Ordinal) == false
                && text.EndsWith(" " + data, StringComparison.Ordinal) == false)
            {
                continue;
            }

            ButtonLedgerEntry entry = buttonLedger[key];
            string reason;
            if (DateTime.Now > entry.ExpireAt)
            {
                reason = "按钮已过期";
            }
            else if (entry.Limit > 0)
            {
                entry.Used.TryGetValue(senderId, out int used);
                if (used >= entry.Limit)
                {
                    reason = "该用户已经点过 " + used.ToString(CultureInfo.InvariantCulture) + " 次（上限 "
                        + entry.Limit.ToString(CultureInfo.InvariantCulture) + "）";
                }
                else
                {
                    entry.Used[senderId] = used + 1;
                    buttonLedgerDirty = true;
                    return false;
                }
            }
            else
            {
                return false;
            }

            DetailLog("按钮点击被策略拦下：" + reason + "｜data=" + data + "｜user=" + senderId);
            if (Configuration.ButtonNotifyOnLimit)
            {
                interactor.Poke("[QQ按钮] " + (senderId.Length > 0 ? senderId + " 的点击" : "有人的点击")
                    + "被策略拦下：" + reason + "（data=" + data + "），这条没有转给模型。");
            }
            if (Configuration.ButtonNotifyUserOnLimit && Configuration.ButtonLimitNotifyText.Trim().Length > 0)
            {
                await SendButtonLimitNoticeAsync(isGroup, targetId, messageId, Configuration.ButtonLimitNotifyText.Trim());
            }
            return true;
        }
        return false;
    }

    /// <summary>拦截提示发给用户：优先当这条点击的被动回复发；没有 msg_id 时按平台规则可能被拒，失败只记日志。</summary>
    async Task SendButtonLimitNoticeAsync(bool isGroup, string targetId, string messageId, string text)
    {
        try
        {
            JsonObject payload = new()
            {
                ["msg_type"] = QQSendPolish.textType,
                ["content"] = text
            };
            if (messageId.Length > 0)
            {
                payload["msg_id"] = messageId;
                payload["msg_seq"] = NextMessageSeq(messageId);
            }
            await RequireRest().PostAsync(TargetPath(isGroup, targetId) + "/messages", payload, DestroyCancellationToken);
        }
        catch (Exception ex)
        {
            DetailLog("按钮拦截提示发送失败：" + ex.Message);
        }
    }

    void PruneButtonLedger()
    {
        int max = Math.Max(50, Configuration.ButtonMaxEntries);
        if (buttonLedger.Count <= max)
        {
            return;
        }
        DateTime now = DateTime.Now;
        foreach (string stale in buttonLedger.Where(pair => pair.Value.ExpireAt < now).Select(pair => pair.Key).ToList())
        {
            buttonLedger.Remove(stale);
        }
        if (buttonLedger.Count <= max)
        {
            return;
        }
        foreach (string oldest in buttonLedger.OrderBy(pair => pair.Value.ExpireAt).Take(buttonLedger.Count - max).Select(pair => pair.Key).ToList())
        {
            buttonLedger.Remove(oldest);
        }
    }

    /// <summary>从 JsonNode 里取字符串字段（JsonValue 才转，对象/数组一律当空）。</summary>
    static string NodeText(JsonNode node, string name)
    {
        if (node is JsonObject obj && obj[name] is JsonValue value)
        {
            return value.ToString();
        }
        return "";
    }

    static int NodeInt(JsonNode node, string name)
    {
        return int.TryParse(NodeText(node, name), out int value) ? value : 0;
    }

    static bool NodeBool(JsonNode node, string name)
    {
        string raw = NodeText(node, name);
        return raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1";
    }
}
