using System;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Alife.Function.FunctionCaller;

namespace Suran.QQ;

/// <summary>
/// 兜底调用：文档里有、但没单独封装成工具的端点，让模型直接按文档打。
/// 主要覆盖频道那套老接口（身份组、子频道权限、公告、精华、置顶、日程、音频、论坛、表态），
/// 以及后续平台新增、插件还没跟上的接口——不用等插件发版。
/// </summary>
public partial class QQModule
{
    [XmlFunction(FunctionMode.OneShot)]
    [Description("直接调用 QQ 开放平台 OpenAPI 里没有专门封装的端点（例如频道身份组 /guilds/{guild_id}/roles、子频道权限、公告、精华、置顶、日程、音频、论坛帖子、消息表态）。"
        + "method=GET/POST/PUT/PATCH/DELETE，path 填文档里的相对路径（不要带域名），body 传 JSON 对象字符串，GET/DELETE 留空。"
        + "字段和路径照官方文档填，错了平台会回具体 err_code")]
    public async Task QQApi(string method, string path, string body = "")
    {
        string verb = (method ?? "").Trim().ToUpperInvariant();
        string route = (path ?? "").Trim();
        if (route.Length == 0 || route.StartsWith("/") == false || route.Contains("://", StringComparison.Ordinal))
        {
            throw new Exception("path 要是相对路径，例如 /guilds/123456/roles；不带域名、以 / 开头");
        }

        JsonObject payload = null;
        string raw = (body ?? "").Trim();
        if (raw.Length > 0)
        {
            if (JsonNode.Parse(raw) is not JsonObject parsed)
            {
                throw new Exception("body 要是一个 JSON 对象，例如 {\"name\":\"管理员\"}");
            }
            payload = parsed;
        }

        JsonElement result = verb switch
        {
            "GET" => await RequireRest().GetAsync(route, DestroyCancellationToken),
            "POST" => await RequireRest().PostAsync(route, payload, DestroyCancellationToken),
            "PUT" => await RequireRest().PutAsync(route, payload, DestroyCancellationToken),
            "PATCH" => await RequireRest().PatchAsync(route, payload, DestroyCancellationToken),
            "DELETE" => await RequireRest().DeleteAsync(route, DestroyCancellationToken),
            _ => throw new Exception("method 只支持 GET/POST/PUT/PATCH/DELETE"),
        };

        string json = result.ValueKind == JsonValueKind.Undefined ? "" : result.GetRawText();
        if (json.Length > 3000)
        {
            json = json[..3000] + "…";
        }
        interactor.Poke(verb + " " + route + " 成功" + (json.Length > 0 ? "：\n" + json : "（无返回体）"));
    }
}
