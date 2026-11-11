using System;
using System.Collections.Generic;
using System.Globalization;

namespace Suran.QQ;

/// <summary>网关 IDENTIFY 用的 intents 位与可读名称。位值取自官方文档事件订阅页，
/// 群里/私聊消息是 1&lt;&lt;25，群成员变化 1&lt;&lt;24（白名单能力），互动 1&lt;&lt;26，消息审核 1&lt;&lt;27。</summary>
static class QQIntents
{
    public const long groupAndPrivate = 1L << 25;
    public const long guilds = 1L << 0;
    public const long groupMember = 1L << 24;
    public const long interaction = 1L << 26;
    public const long messageAudit = 1L << 27;

    public static long FromConfig(QQConfig config)
    {
        long intents = 0;
        if (config.IntentGroupAndPrivate)
        {
            intents |= groupAndPrivate;
        }
        if (config.IntentGuilds)
        {
            intents |= guilds;
        }
        if (config.IntentGroupMember)
        {
            intents |= groupMember;
        }
        if (config.IntentInteraction)
        {
            intents |= interaction;
        }
        if (config.IntentMessageAudit)
        {
            intents |= messageAudit;
        }
        return intents;
    }

    public static IEnumerable<string> Describe(long intents)
    {
        List<string> names = new();
        if ((intents & groupAndPrivate) != 0)
        {
            names.Add("群与私聊消息(1<<25)");
        }
        if ((intents & guilds) != 0)
        {
            names.Add("频道与子频道变化(1<<0)");
        }
        if ((intents & groupMember) != 0)
        {
            names.Add("群成员变化(1<<24，白名单)");
        }
        if ((intents & interaction) != 0)
        {
            names.Add("按钮互动(1<<26)");
        }
        if ((intents & messageAudit) != 0)
        {
            names.Add("消息审核(1<<27)");
        }
        return names;
    }

    public static string Text(long intents) =>
        intents.ToString(CultureInfo.InvariantCulture) + "（" + string.Join("、", Describe(intents)) + "）";

    /// <summary>关闭码 4013/4014 是 intents 的问题（格式不对 / 有位没权限），话术要直接说该关哪一位</summary>
    public static string IntentHint(long intents) =>
        "当前订阅位：" + Text(intents) + "。把没权限的那几位在插件配置里关掉再存，网关对无权限的位是直接关连接、不会回可读错误。";
}
