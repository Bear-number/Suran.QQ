using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Suran.QQ;

/// <summary>
/// 发送侧的几个"平台不报错但效果不对"的坑，集中放在这里，都是纯函数好单测：
/// ① @ 人：官方群消息里 @ 的形态是富文本标记 <code>&lt;@openid&gt;</code>，而带 @ 的正文必须走 markdown（msg_type=2），
///    纯文本里写这个标记只会显示成一串字符；新式 <code>&lt;qqbot-at-user id=…/&gt;</code> 实测在群里被当纯文本显示，所以仍用 legacy 形态。
/// ② 引用回复：message_reference 与 markdown 互斥，同时给必须说清楚，不能悄悄丢一个。
/// ③ 语音条：官方 file_type=3 只认 silk，别的格式会掉成文件卡片；ffmpeg 只能解码不能编码，所以没配编码器就只能明确拒绝。
/// ④ 「正在输入」帧：msg_type=6 也占被动回复额度，不限帧会把 4~5 次窗口吃光。
/// </summary>
static class QQSendPolish
{
    public const int markdownType = 2;
    public const int textType = 0;

    static readonly System.Text.RegularExpressions.Regex atMarker = new(@"<@([0-9A-Za-z]{8,})>|<qqbot-at-user\s+id=""([0-9A-Za-z]{8,})""\s*/?>");

    /// <summary>正文里已经出现过 @ 标记（平台自己的形态或新式标签）就必须按 markdown 发</summary>
    public static bool HasAtMarker(string text) => atMarker.IsMatch(text ?? "");

    /// <summary>把 openid 列表拼成 @ 标记追加到正文尾部；style=new 用新式标签，其余走 legacy；重复与正文里已有的都不再拼</summary>
    public static string AppendAtMarkers(string text, List<string> openids, string style = "legacy")
    {
        string body = text ?? "";
        bool useNew = string.Equals(style, "new", StringComparison.OrdinalIgnoreCase);
        List<string> missing = openids.Distinct()
            .Where(openid => body.Contains("<@" + openid, StringComparison.Ordinal) == false
                             && body.Contains("<qqbot-at-user id=\"" + openid, StringComparison.Ordinal) == false)
            .ToList();
        if (missing.Count == 0)
        {
            return body;
        }
        string markers = string.Join(" ", missing.Select(openid => useNew
            ? "<qqbot-at-user id=\"" + openid + "\" />"
            : "<@" + openid + ">"));
        return body + " " + markers;
    }

    /// <summary>@ 目标是昵称不是 openid 时报错——昵称谁都能改，拿它当身份会把人 @ 错</summary>
    public static string UnknownAtTargets(List<string> openids, QQIdentityStore store)
    {
        List<string> suspicious = openids.Where(openid => openid.Length is < 16 or > 64).ToList();
        return suspicious.Count == 0 ? "" : "这些不像 openid（官方是 32 位十六进制串）：" + string.Join("、", suspicious)
            + "。要 @ 人先拿 QQFindPerson 查出 openid，别用昵称——昵称任何人都能改成一样的";
    }

    /// <summary>markdown 与引用回复互斥：两个都给就拒绝，让调用方选一个，不悄悄丢内容</summary>
    public static string ReferenceProblem(bool markdown, string quoteMessageId) =>
        markdown == false || quoteMessageId.Trim().Length == 0
            ? ""
            : "引用回复（message_reference）与 markdown 互斥，官方只会认一个：要么去掉 markdown 走纯文本+引用，要么保留 markdown 放弃引用";

    /// <summary>语音要不要转、能不能转：三态分开，别让"没装编码器"变成默默发个文件</summary>
    public static string VoiceProblem(int fileType, string localPath, string encoderPath, bool allowRawUpload = false)
    {
        if (fileType != 3)
        {
            return "";
        }
        string extension = Path.GetExtension(localPath).ToLowerInvariant();
        if (extension is ".silk" or ".slk" or ".sil")
        {
            return "";
        }
        if (encoderPath.Trim().Length == 0)
        {
            if (allowRawUpload)
            {
                return "";
            }
            return "官方 file_type=3 只认 silk，" + extension + " 传上去会掉成文件卡片而不是语音条；（想拿真接口验一次就先打开「语音·放行非 silk 直传」）"
                + "ffmpeg 只能解码 silk 不能编码，要发语音条请在配「语音·silk 编码器」（给一个装了 pilk 的 python.exe 与本插件 tools\\to_silk.py），或先用外部命令转出 .silk";
        }
        return "";
    }

    /// <summary>编码器参数里的 {in}/{out} 占位；缺 {out} 时拒绝，免得把参数丢给编码器乱写文件</summary>
    public static bool EncoderArgsUsable(string encoderArgs) =>
        encoderArgs.Contains("{in}", StringComparison.Ordinal) && encoderArgs.Contains("{out}", StringComparison.Ordinal);

    /// <summary>帧预算：返回这一帧能不能发（能发就计数 +1）</summary>
    public static bool TakeTypingFrame(Dictionary<string, int> used, string conversationKey, int limit)
    {
        if (limit <= 0)
        {
            return false;
        }
        int now = used.TryGetValue(conversationKey, out int count) ? count : 0;
        if (now >= limit)
        {
            return false;
        }
        used[conversationKey] = now + 1;
        return true;
    }

    public static string DescribeFrames(int used, int limit) =>
        "本会话已用 " + used.ToString(CultureInfo.InvariantCulture) + "/" + limit.ToString(CultureInfo.InvariantCulture) + " 帧输入状态提示";
}
