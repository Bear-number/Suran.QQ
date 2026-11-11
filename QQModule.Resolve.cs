using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Suran.QQ;

/// <summary>
/// 群名 / 昵称补齐：官方事件里 `group_name` 经常是空的，私聊事件连 `author.username` 都是空串。
/// 所以第一次遇到这种消息时，后台补一次对应接口，把结果写进通讯录；下一条消息起就能显示真名。
///
/// 两个接口都可能是白名单（错误码 11253），拿不到就安静降级，不影响收发。
/// 注意：官方 OpenAPI 本身不暴露 QQ 号，能拿到的只有 openid 和 union_user_account，
/// 所以「号码」只能靠人工绑定（QQBindIdentity），不能自动补。
/// </summary>
public partial class QQModule
{
    static readonly TimeSpan ProbeInterval = TimeSpan.FromMinutes(10);
    readonly Dictionary<string, DateTime> groupNameProbeAt = new();
    readonly Dictionary<string, DateTime> memberNameProbeAt = new();
    readonly HashSet<string> probingGroupName = new();
    readonly HashSet<string> probingMemberName = new();

    /// <summary>群名为空时，后台拉一次群信息。</summary>
    void ProbeGroupName(string groupId)
    {
        if (groupId.Length == 0 || identities == null)
        {
            return;
        }
        if (groupNameProbeAt.TryGetValue(groupId, out DateTime last) && DateTime.UtcNow - last < ProbeInterval)
        {
            return;
        }
        if (probingGroupName.Add(groupId) == false)
        {
            return;
        }
        groupNameProbeAt[groupId] = DateTime.UtcNow;

        _ = Task.Run(async () =>
        {
            try
            {
                var info = await RequireRest().GetAsync(
                    "/v2/groups/" + Uri.EscapeDataString(groupId) + "/info", DestroyCancellationToken);
                string name = QQJson.Text(info, "group_name");
                if (name.Length > 0 && identities != null)
                {
                    identities.RememberGroupName(groupId, name);
                    DetailLog("补到群名：" + groupId + " → " + name);
                }
            }
            catch (Exception ex)
            {
                DetailLog("拉群信息失败（可能是白名单接口）：" + groupId + "｜" + ex.Message);
            }
            finally
            {
                probingGroupName.Remove(groupId);
            }
        });
    }

    /// <summary>昵称未知时，后台拉一次该成员的群名片。</summary>
    void ProbeMemberName(string groupId, string memberId)
    {
        if (groupId.Length == 0 || memberId.Length == 0 || identities == null)
        {
            return;
        }
        string key = groupId + "|" + memberId;
        if (memberNameProbeAt.TryGetValue(key, out DateTime last) && DateTime.UtcNow - last < ProbeInterval)
        {
            return;
        }
        if (probingMemberName.Add(key) == false)
        {
            return;
        }
        memberNameProbeAt[key] = DateTime.UtcNow;

        _ = Task.Run(async () =>
        {
            try
            {
                var member = await RequireRest().GetAsync(
                    "/v2/groups/" + Uri.EscapeDataString(groupId) + "/members/" + Uri.EscapeDataString(memberId),
                    DestroyCancellationToken);
                string name = QQJson.Text(member, "username");
                if (name.Length > 0 && identities != null)
                {
                    identities.RememberNickname(memberId, name);
                    DetailLog("补到昵称：" + memberId + " → " + name);
                }
            }
            catch (Exception ex)
            {
                DetailLog("拉成员信息失败（可能是白名单接口）：" + memberId + "｜" + ex.Message);
            }
            finally
            {
                probingMemberName.Remove(key);
            }
        });
    }
}
