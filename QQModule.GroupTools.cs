using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
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

    /// <summary>
    /// 把模型常见的几种偷懒写法补成平台要的 keyboard 结构，省得每次都卡在格式上：
    ///   {"rows":[{"buttons":[{...}]}]}  —— 标准写法，只补齐缺失的 label / visited_label / style / type / data
    ///   [{"label":"A","data":"a"}, ...] —— 自动包成一行
    ///   [[{...}],[{...}]]               —— 每项一行
    /// 返回 null 时 problem 说明哪里不对。硬限制按平台：≤5 行 × 每行 ≤5 列、style 0/1、action.data ≤100 字符。
    /// </summary>
    static JsonObject? NormalizeKeyboard(string keyboardJson, out string problem)
    {
        problem = "";
        JsonNode? parsed;
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
        if (parsed is JsonObject keyboard && keyboard["rows"] is JsonArray existingRows)
        {
            rows = existingRows;
        }
        else if (parsed is JsonArray array)
        {
            rows = new JsonArray();
            if (array.Count > 0 && array[0] is JsonArray)
            {
                foreach (JsonNode? rowNode in array)
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

        JsonArray normalizedRows = new();
        foreach (JsonNode? rowNode in rows)
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
            foreach (JsonNode? buttonNode in buttons)
            {
                if (buttonNode is not JsonObject button)
                {
                    problem = "按钮本身要是一个对象";
                    return null;
                }
                JsonObject? normalized = NormalizeButton(button, out problem);
                if (normalized == null)
                {
                    return null;
                }
                normalizedButtons.Add(normalized);
            }
            normalizedRows.Add(new JsonObject { ["buttons"] = normalizedButtons });
        }

        return new JsonObject { ["rows"] = normalizedRows };
    }

    /// <summary>单个按钮：标准写法原样保留并补默认值；简化写法（label/data/style/type）拼成 render_data + action。</summary>
    static JsonObject? NormalizeButton(JsonObject button, out string problem)
    {
        problem = "";
        JsonObject? render = button["render_data"] as JsonObject;
        JsonObject? action = button["action"] as JsonObject;

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
        if (data.Length > keyboardDataMaxChars)
        {
            problem = "action.data 最长 " + keyboardDataMaxChars.ToString(CultureInfo.InvariantCulture) + " 字符，给了 " + data.Length.ToString(CultureInfo.InvariantCulture);
            return null;
        }

        string visited = render != null ? NodeText(render, "visited_label") : "";
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
                ["data"] = data,
                ["enter"] = false
            }
        };
    }

    /// <summary>从 JsonNode 里取字符串字段（JsonValue 才转，对象/数组一律当空）。</summary>
    static string NodeText(JsonNode? node, string name)
    {
        if (node is JsonObject obj && obj[name] is JsonValue value)
        {
            return value.ToString();
        }
        return "";
    }
}
