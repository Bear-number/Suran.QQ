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
/// 频道（Guild）、单聊快捷菜单、指令面板、分享链接。这一片与「群/私聊」是两套体系：
/// 频道侧的 id 是普通字符串 ID 而不是 openid，面板的生效场景还分 c2c/group/channel/dm，
/// 其中 channel/dm **只能全局配置**（target_type 只能是 all）。这些接口都还没在本机实机跑过，形状照文档抄。
/// </summary>
public partial class QQModule
{
    const int menuMaxItems = 10;
    const int menuMaxSubItems = 5;
    const int panelMaxItems = 20;
    const int panelTargetMax = 20;
    static readonly string[] panelScopes = ["c2c", "group", "channel", "dm"];

    [XmlFunction(FunctionMode.OneShot)]
    [Description("列出机器人加入的 QQ 频道（guild）：频道 ID、名称、成员数、是否自己建的。注意频道与 QQ 群是两套东西")]
    public async Task QQMyGuilds()
    {
        JsonElement body = await RequireRest().GetAsync("/users/@me/guilds", DestroyCancellationToken);
        List<JsonElement> guilds = QQJson.Array(body, "guilds").ToList();
        List<string> lines = new() { "机器人加入的频道 " + guilds.Count.ToString(CultureInfo.InvariantCulture) + " 个" };
        foreach (JsonElement guild in guilds)
        {
            lines.Add("- " + QQJson.Text(guild, "name", "（无名）") + " | guild_id=" + QQJson.Text(guild, "id")
                + " | 成员 " + QQJson.Integer(guild, "member_count").ToString(CultureInfo.InvariantCulture) + " 人"
                + (QQJson.Flag(guild, "owner") ? " | 我建的" : "")
                + " | 加入于 " + QQJson.Text(guild, "joined_at", "?"));
        }
        interactor.Poke(string.Join("\n", lines));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查单个频道信息或它的子频道列表。withRoles=true 时带上身份组（子频道里的 role_permissions 要有权限才给）")]
    public async Task QQGuildChannels(string guildId, bool withRoles = false)
    {
        string guild = Need(guildId, "guildId");
        if (withRoles)
        {
            JsonElement detail = await RequireRest().GetAsync("/guilds/" + Uri.EscapeDataString(guild), DestroyCancellationToken);
            interactor.Poke("频道 " + QQJson.Text(detail, "id") + "：" + QQJson.Text(detail, "name", "（无名）")
                + "\n成员 " + QQJson.Integer(detail, "member_num").ToString(CultureInfo.InvariantCulture)
                + "，最大 " + QQJson.Integer(detail, "max_member_num").ToString(CultureInfo.InvariantCulture)
                + "\n身份组 " + QQJson.Array(detail, "roles").Count() + " 个");
            return;
        }
        JsonElement body = await RequireRest().GetAsync("/guilds/" + Uri.EscapeDataString(guild) + "/channels", DestroyCancellationToken);
        List<JsonElement> channels = QQJson.Array(body, "channels").ToList();
        List<string> lines = new() { "频道 " + guild + " 的子频道 " + channels.Count.ToString(CultureInfo.InvariantCulture) + " 个" };
        foreach (JsonElement channel in channels)
        {
            lines.Add("- " + QQJson.Text(channel, "name", "（无名）") + " | channel_id=" + QQJson.Text(channel, "id")
                + " | type=" + QQJson.Integer(channel, "type").ToString(CultureInfo.InvariantCulture)
                + "（0文字 2语音 4分组 10005直播 10006应用 10007论坛）"
                + " | parent_id=" + QQJson.Text(channel, "parent_id", "-"));
        }
        interactor.Poke(string.Join("\n", lines));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("在频道里建子频道（name/type/parent_id 等按文档给，type 0=文字 2=语音 4=分组）。50 QPS")]
    public async Task QQCreateGuildChannel(string guildId, string name, int type = 0, string parentId = "")
    {
        JsonObject payload = new() { ["name"] = Need(name, "name"), ["type"] = type };
        if (parentId.Trim().Length > 0)
        {
            payload["parent_id"] = parentId.Trim();
        }
        JsonElement result = await RequireRest().PostAsync("/guilds/" + Uri.EscapeDataString(Need(guildId, "guildId")) + "/channels", payload, DestroyCancellationToken);
        interactor.Poke("子频道已建：" + QQJson.Text(result, "id") + "（" + QQJson.Text(result, "name") + "）");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("改子频道（名称/排序/分组/私密类型/发言权限）或删除子频道。delete=true 就是删，其余字段忽略。50 QPS")]
    public async Task QQUpdateGuildChannel(string channelId, string name = "", int position = 0, bool delete = false)
    {
        string channel = Need(channelId, "channelId");
        if (delete)
        {
            await RequireRest().DeleteAsync("/channels/" + Uri.EscapeDataString(channel), DestroyCancellationToken);
            interactor.Poke("子频道 " + channel + " 已删除");
            return;
        }
        JsonObject payload = new();
        if (name.Trim().Length > 0)
        {
            payload["name"] = name.Trim();
        }
        if (position > 0)
        {
            payload["position"] = position;
        }
        if (payload.Count == 0)
        {
            throw new Exception("name 与 position 至少给一个，或者干脆 delete=true");
        }
        await RequireRest().PatchAsync("/channels/" + Uri.EscapeDataString(channel), payload, DestroyCancellationToken);
        interactor.Poke("子频道 " + channel + " 已修改");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查当前单聊快捷菜单配置（版本号 + 菜单项）。5 QPM 级别，改起来很慢，别连着刷")]
    public async Task QQMenuGet()
    {
        JsonElement body = await RequireRest().GetAsync("/v2/menu", DestroyCancellationToken);
        List<string> lines = new() { "菜单版本 " + QQJson.Integer(body, "version").ToString(CultureInfo.InvariantCulture) };
        if (body.TryGetProperty("menu", out JsonElement menu) && menu.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonElement item in QQJson.Array(menu, "items"))
            {
                lines.Add("- " + QQJson.Text(item, "name", "（无名）") + " | " + QQJson.Text(item, "type", "?")
                    + (QQJson.Array(item, "sub_menu_items").Any() ? " | 子项 " + QQJson.Array(item, "sub_menu_items").Count().ToString(CultureInfo.InvariantCulture) + " 个" : ""));
            }
        }
        else
        {
            lines.Add("（还没配过菜单）");
        }
        interactor.Poke(string.Join("\n", lines));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("整体覆盖单聊快捷菜单。menuJson 形如 {\"items\":[{\"name\":\"查天气\",\"type\":\"send_message\",\"send_message\":\"查天气\"}]}，"
        + "type 可选 switch/send_message/link/menu；items 最多 10 个、子项最多 5 个、按钮名最长 10 字符（汉字算 2）、link 必须 https:// 开头。5 QPM")]
    public async Task QQMenuPut(string menuJson)
    {
        if (JsonNode.Parse(menuJson) is not JsonObject menu)
        {
            throw new Exception("menuJson 要是一个对象，形如 {\"items\":[…]}");
        }
        string problem = MenuProblem(menu);
        if (problem.Length > 0)
        {
            throw new Exception("菜单结构不合法：" + problem);
        }
        JsonElement result = await RequireRest().PutAsync("/v2/menu", new JsonObject { ["menu"] = menu }, DestroyCancellationToken);
        interactor.Poke("菜单已覆盖，新版本号 " + QQJson.Integer(result, "version").ToString(CultureInfo.InvariantCulture) + "（5 QPM，改一次要等一会儿才生效）");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查指令面板列表（按设置时间倒序、游标翻页）。面板是聊天输入框旁边那排指令入口")]
    public async Task QQPanels(string cursor = "")
    {
        JsonElement body = await RequireRest().GetAsync("/v2/panels?cursor=" + Uri.EscapeDataString(cursor.Trim()), DestroyCancellationToken);
        List<JsonElement> records = QQJson.Array(body, "records").ToList();
        List<string> lines = new()
        {
            "指令面板 " + records.Count.ToString(CultureInfo.InvariantCulture) + " 条"
            + (QQJson.Flag(body, "is_end") ? "（已到末页）" : "（还有下一页）")
        };
        foreach (JsonElement record in records)
        {
            lines.Add("- panel_id=" + QQJson.Text(record, "panel_id") + " | " + QQJson.Text(record, "scope", "?") + "/" + QQJson.Text(record, "target_type", "?")
                + " | 元素 " + QQJson.Array(QQJson.Has(record, "panel") ? record.GetProperty("panel") : JsonDocument.Parse("{}").RootElement.Clone(), "items").Count().ToString(CultureInfo.InvariantCulture) + " 个"
                + " | 版本 " + QQJson.Integer(record, "version").ToString(CultureInfo.InvariantCulture)
                + " | 改于 " + QQJson.Text(record, "updated_at", "?"));
        }
        interactor.Poke(string.Join("\n", lines));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("创建指令面板。scope：c2c/group/channel/dm；targetType all 或 specific（只有 c2c、group 能 specific）；"
        + "specific 时给 userOpenids 或 groupOpenids（一次最多 20 个）；panelJson 形如 {\"items\":[…]}，最多 20 个元素。10 QPM")]
    public async Task QQCreatePanel(string scope, string panelJson, string targetType = "all", string userOpenids = "", string groupOpenids = "", string remark = "")
    {
        string scene = scope.Trim().ToLowerInvariant();
        if (!panelScopes.Contains(scene))
        {
            throw new Exception("scope 只认 " + string.Join("/", panelScopes));
        }
        if (JsonNode.Parse(panelJson) is not JsonObject panel)
        {
            throw new Exception("panelJson 要是一个对象，形如 {\"items\":[…]}");
        }
        string problem = PanelProblem(panel);
        if (problem.Length > 0)
        {
            throw new Exception("面板结构不合法：" + problem);
        }
        List<string> users = SplitIds(userOpenids);
        List<string> groups = SplitIds(groupOpenids);
        string range = targetType.Trim().ToLowerInvariant();
        JsonObject payload = new() { ["scope"] = scene, ["panel"] = panel };
        if (range == "specific")
        {
            if (scene is "channel" or "dm")
            {
                throw new Exception(scene + " 场景只支持全局配置，targetType 只能是 all");
            }
            if (users.Count + groups.Count == 0)
            {
                throw new Exception("targetType=specific 时要给 userOpenids 或 groupOpenids");
            }
            if (users.Count > panelTargetMax || groups.Count > panelTargetMax)
            {
                throw new Exception("一次最多关联 " + panelTargetMax.ToString(CultureInfo.InvariantCulture) + " 个用户或群");
            }
            payload["target_type"] = "specific";
            if (users.Count > 0)
            {
                payload["user_openids"] = StringArray(users);
            }
            if (groups.Count > 0)
            {
                payload["group_openids"] = StringArray(groups);
            }
        }
        else
        {
            payload["target_type"] = "all";
        }
        if (remark.Trim().Length > 0)
        {
            payload["remark"] = remark.Trim();
        }
        JsonElement result = await RequireRest().PostAsync("/v2/panels", payload, DestroyCancellationToken);
        interactor.Poke("面板已创建，panel_id=" + QQJson.Text(result, "panel_id") + "（改/删/增删关联对象都要用这个 id）");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("改面板关联对象（op=add/del，userOpenids 或 groupOpenids 一次最多 20 个），或者删除面板（delete=true）。60 QPM")]
    public async Task QQPanelTargets(string panelId, string op = "add", string userOpenids = "", string groupOpenids = "", bool delete = false)
    {
        string id = Need(panelId, "panelId");
        if (delete)
        {
            await RequireRest().DeleteAsync("/v2/panels/" + Uri.EscapeDataString(id), DestroyCancellationToken);
            interactor.Poke("面板 " + id + " 已删除");
            return;
        }
        string action = op.Trim().ToLowerInvariant();
        if (action is not ("add" or "del"))
        {
            throw new Exception("op 只认 add/del");
        }
        List<string> users = SplitIds(userOpenids);
        List<string> groups = SplitIds(groupOpenids);
        if (users.Count + groups.Count == 0)
        {
            throw new Exception("userOpenids 与 groupOpenids 至少给一个");
        }
        JsonObject payload = new() { ["op"] = action };
        if (users.Count > 0)
        {
            payload["user_openids"] = StringArray(users);
        }
        if (groups.Count > 0)
        {
            payload["group_openids"] = StringArray(groups);
        }
        await RequireRest().PutAsync("/v2/panels/" + Uri.EscapeDataString(id) + "/target", payload, DestroyCancellationToken);
        interactor.Poke("面板 " + id + " 关联对象已" + (action == "add" ? "增加 " : "移除 ") + (users.Count + groups.Count).ToString(CultureInfo.InvariantCulture) + " 个");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("生成一条分享链接（可带 callbackData 回传自己的数据，用户点开后会随事件带回来）。50 QPS")]
    public async Task QQGenerateShareLink(string callbackData = "")
    {
        JsonObject payload = new();
        if (callbackData.Trim().Length > 0)
        {
            payload["callback_data"] = callbackData.Trim();
        }
        JsonElement result = await RequireRest().PostAsync("/v2/generate_url_link", payload, DestroyCancellationToken);
        string url = "";
        if (result.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Object)
        {
            url = QQJson.Text(data, "url");
        }
        interactor.Poke("分享链接：" + (url.Length > 0 ? url : "（接口没给 url）" + QQRestClient.Shorten(result.ToString())));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查单个子频道的详情（名称、类型、分组、排序、私密与发言权限）。50 QPS")]
    public async Task QQGuildChannelInfo(string channelId)
    {
        JsonElement channel = await RequireRest().GetAsync("/channels/" + Uri.EscapeDataString(Need(channelId, "channelId")), DestroyCancellationToken);
        interactor.Poke("子频道 " + QQJson.Text(channel, "id") + "：" + QQJson.Text(channel, "name", "（无名）")
            + " | type=" + QQJson.Integer(channel, "type").ToString(CultureInfo.InvariantCulture)
            + " | guild_id=" + QQJson.Text(channel, "guild_id", "-")
            + " | parent_id=" + QQJson.Text(channel, "parent_id", "-")
            + " | 排序 " + QQJson.Integer(channel, "position").ToString(CultureInfo.InvariantCulture));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查单个指令面板的详情（配置内容、版本、时间）。10 QPM")]
    public async Task QQPanelInfo(string panelId)
    {
        JsonElement panel = await RequireRest().GetAsync("/v2/panels/" + Uri.EscapeDataString(Need(panelId, "panelId")), DestroyCancellationToken);
        interactor.Poke("面板 " + QQJson.Text(panel, "panel_id", panelId.Trim()) + "：" + QQJson.Text(panel, "scope", "?") + "/" + QQJson.Text(panel, "target_type", "?")
            + " | 版本 " + QQJson.Integer(panel, "version").ToString(CultureInfo.InvariantCulture)
            + " | 元素 " + (panel.TryGetProperty("panel", out JsonElement content) ? QQJson.Array(content, "items").Count() : 0).ToString(CultureInfo.InvariantCulture) + " 个"
            + " | 改于 " + QQJson.Text(panel, "updated_at", "?")
            + "\n配置：" + QQRestClient.Shorten(panel.TryGetProperty("panel", out JsonElement body) ? body.ToString() : panel.ToString()));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("整体覆盖一个已有面板的配置（panelJson 形如 {\"items\":[…]}，最多 20 个元素）。10 QPM")]
    public async Task QQUpdatePanel(string panelId, string panelJson, string remark = "")
    {
        if (JsonNode.Parse(panelJson) is not JsonObject panel)
        {
            throw new Exception("panelJson 要是一个对象，形如 {\"items\":[…]}");
        }
        string problem = PanelProblem(panel);
        if (problem.Length > 0)
        {
            throw new Exception("面板结构不合法：" + problem);
        }
        JsonObject payload = new() { ["panel"] = panel };
        if (remark.Trim().Length > 0)
        {
            payload["remark"] = remark.Trim();
        }
        await RequireRest().PutAsync("/v2/panels/" + Uri.EscapeDataString(Need(panelId, "panelId")), payload, DestroyCancellationToken);
        interactor.Poke("面板 " + panelId.Trim() + " 已覆盖（新版本要再查 QQPanelInfo 才看得到）");
    }

    static string MenuProblem(JsonObject menu)
    {
        if (menu["items"] is not JsonArray items)
        {
            return "找不到 items 数组";
        }
        if (items.Count > menuMaxItems)
        {
            return "items 最多 " + menuMaxItems.ToString(CultureInfo.InvariantCulture) + " 个，给了 " + items.Count.ToString(CultureInfo.InvariantCulture);
        }
        foreach (JsonNode raw in items)
        {
            if (raw is not JsonObject item)
            {
                return "每个菜单项都要是对象";
            }
            string name = item["name"]?.ToString() ?? "";
            if (NameWeight(name) > 10)
            {
                return "按钮名最长 10 字符（汉字算 2），「" + name + "」是 " + NameWeight(name).ToString(CultureInfo.InvariantCulture);
            }
            if (item["link"]?.ToString() is string link && link.Length > 0 && link.StartsWith("https://") == false)
            {
                return "link 必须以 https:// 开头";
            }
            if (item["sub_menu_items"] is JsonArray subs && subs.Count > menuMaxSubItems)
            {
                return "子菜单最多 " + menuMaxSubItems.ToString(CultureInfo.InvariantCulture) + " 个";
            }
        }
        return "";
    }

    static string PanelProblem(JsonObject panel)
    {
        if (panel["items"] is JsonArray items && items.Count > panelMaxItems)
        {
            return "一个面板最多 " + panelMaxItems.ToString(CultureInfo.InvariantCulture) + " 个元素，给了 " + items.Count.ToString(CultureInfo.InvariantCulture);
        }
        return "";
    }

    static int NameWeight(string name) => name.Sum(character => character > 0x7F ? 2 : 1);
}
