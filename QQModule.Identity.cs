using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Alife.Function.FunctionCaller;

namespace Suran.QQ;

/// <summary>
/// 身份相关的工具：看通讯录、人工绑 QQ 号、从群成员接口一次性回填、按昵称找人（要 @ 谁但只有昵称时用它拿 openid）。
/// 官方接口本身给不了 QQ 号，所以这里只承认三种来源：人工绑定、内邀字段 `union_user_account`、
/// 以及和另一条 QQ 通道按**唯一昵称**对撞——重名一律不猜。
/// </summary>
public partial class QQModule
{
    const int learnMembersMaxPages = 10;

    QQIdentityStore RequireIdentities()
    {
        if (identities == null)
        {
            throw new Exception("通讯录还没初始化（插件的 OnStart 还没跑到，或宿主刚热重载）");
        }
        return identities;
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("看身份通讯录：openid→昵称/QQ号/备注名，以及缓存的群名。单聊事件官方不给昵称，这里记过才显示得出名字")]
    public async Task QQIdentityShow(int limit = 20)
    {
        interactor.Poke(RequireIdentities().Describe(Math.Clamp(limit, 1, 200)));
        await Task.CompletedTask;
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("给某个 openid 绑 QQ 号或备注名（官方接口拿不到 QQ 号，只能人工给、等内邀字段，或与另一条 QQ 通道对撞）。"
        + "传空串的那一项保持不动；改完立刻落盘")]
    public async Task QQBindIdentity(string openid, string qqNumber = "", string remark = "")
    {
        QQIdentityStore store = RequireIdentities();
        string target = Need(openid, "openid");
        if (qqNumber.Trim().Length == 0 && remark.Trim().Length == 0)
        {
            throw new Exception("qqNumber 与 remark 至少给一个（都给空等于没要改）");
        }
        store.Bind(target, qqNumber, remark);
        store.SaveIfNeeded();
        interactor.Poke("已绑定 " + target + "：" + store.DisplayName(target)
            + "\n之后收他消息就会显示成这个样子（备注名优先，其次昵称／QQ 号）");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("用群成员接口一次性回填通讯录（群里所有人的 member_openid + 入群昵称）与群名。"
        + "每页最多 30 条、自己翻页；这接口是内邀/白名单，没权限会回 11253 并如实报出来")]
    public async Task QQLearnGroupMembers(string groupOpenid, int maxPages = learnMembersMaxPages)
    {
        QQIdentityStore store = RequireIdentities();
        string group = Need(groupOpenid, "groupOpenid");
        int learned = 0;
        string cursor = "";
        for (int page = 0; page < Math.Clamp(maxPages, 1, learnMembersMaxPages); page++)
        {
            JsonElement body = await RequireRest().GetAsync(groupsPath + Uri.EscapeDataString(group) + "/members?cursor=" + Uri.EscapeDataString(cursor), DestroyCancellationToken);
            foreach (JsonElement member in QQJson.Array(body, "members"))
            {
                if (store.RememberNickname(QQJson.Text(member, "member_openid"), QQJson.Text(member, "username")))
                {
                    learned++;
                }
            }
            cursor = QQJson.Text(body, "next_cursor");
            if (cursor.Length == 0)
            {
                break;
            }
        }
        try
        {
            JsonElement info = await RequireRest().GetAsync(groupsPath + Uri.EscapeDataString(group) + "/info", DestroyCancellationToken);
            store.RememberGroupName(group, QQJson.Text(info, "group_name"));
        }
        catch (QQApiException nameError)
        {
            DetailLog("群名没回填上：" + nameError.Message);
        }
        store.SaveIfNeeded();
        interactor.Poke("群 " + group + " 回填完成：新学/更新昵称 " + learned.ToString(CultureInfo.InvariantCulture)
            + " 个，通讯录现有 " + store.PeopleCount.ToString(CultureInfo.InvariantCulture) + " 人"
            + (store.GroupName(group).Length > 0 ? "，群名「" + store.GroupName(group) + "」" : "（群名没取到，多半是白名单没开）"));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("按昵称或备注名找人，拿他的 openid——要在群里 @ 他得用 openid，不能只写名字。重名会把候选都列出来让你确认，不替你猜")]
    public async Task QQFindPerson(string keyword)
    {
        QQIdentityStore store = RequireIdentities();
        string want = keyword.Trim();
        if (want.Length == 0)
        {
            throw new Exception("keyword 不能为空");
        }
        List<string> lines = new() { "通讯录里与「" + want + "」相关的身份：" };
        List<string> hits = store.Search(want);
        if (hits.Count == 0)
        {
            lines.Add("（没找到。可以先跑 QQLearnGroupMembers 把群里的人回填进来，或用 QQBindIdentity 手工记一条）");
        }
        foreach (string hit in hits)
        {
            lines.Add("- " + hit);
        }
        interactor.Poke(string.Join("\n", lines));
        await Task.CompletedTask;
    }
}
