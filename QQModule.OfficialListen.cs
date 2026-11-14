using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Suran.QQ;

/// <summary>
/// 官方通道的「群监听 / 缓存与防抖 / 自动关闭」——语义照搬 QChat，只是把 long 群号换成 group_openid。
///
/// 三种注入策略（配置「群消息·全量注入策略」）：
///   at    —— 只有 @ 机器人（或命中唤醒词）才注入；其余只记 msg_id，不刷上下文
///   all   —— 每条群消息都立刻注入
///   listen—— 唤醒词 / @ / 概率激活后开始监听，消息先缓存，按间隔成批推送；
///            回复后（可选）或长时间没动静自动关闭
/// </summary>
public partial class QQModule
{
    sealed class OfficialGroupState
    {
        public string GroupId = "";
        public string Head = "";
        public bool Listening;
        public DateTime LastFlush;
        public DateTime LastUse;
        public readonly List<string> Buffer = new();
        public readonly Queue<string> PreMessage = new();
    }

    sealed class OfficialPrivateState
    {
        public string Head = "";
        public DateTime LastFlush;
        public readonly List<string> Buffer = new();
    }

    readonly Dictionary<string, OfficialGroupState> officialGroups = new();
    readonly Dictionary<string, OfficialPrivateState> officialPrivates = new();
    /// <summary>网关线程在写、设置页每秒在读，所有状态读写都走这把锁，避免渲染时撞上「集合已修改」。</summary>
    readonly object officialGate = new();

    /// <summary>设置页「群监听状态」一行数据。群 ID 是 group_openid，显示名从通讯录里取。</summary>
    public sealed record QQGroupListenSnapshot(
        string GroupId,
        string Name,
        bool Listening,
        IReadOnlyList<string> Buffer,
        IReadOnlyList<string> PreBuffer,
        double NextFlushSeconds,
        double AutoCloseSeconds,
        DateTime LastActiveAt);

    /// <summary>当前是否处于「群监听」策略（不是的话设置页的开关不会生效）。</summary>
    public bool OfficialListenEnabled => GroupMode() == "listen";

    /// <summary>正在监听的群数量。</summary>
    public int OfficialListeningCount
    {
        get
        {
            lock (officialGate)
            {
                return officialGroups.Values.Count(state => state.Listening);
            }
        }
    }

    /// <summary>给设置页用的快照：监听中的排前面，其余按群 ID。</summary>
    public IReadOnlyList<QQGroupListenSnapshot> OfficialGroupSnapshots()
    {
        List<QQGroupListenSnapshot> list = new();
        if (Configuration == null)
        {
            return list;
        }

        lock (officialGate)
        {
            // 除了内存里的群，再并上通讯录里记过的群名：
            // 插件重载会把 officialGroups 清空，只靠收到的消息就要等下一个群消息才显示得出来。
            HashSet<string> ids = new(officialGroups.Keys, StringComparer.Ordinal);
            if (identities != null)
            {
                foreach (KeyValuePair<string, string> pair in identities.GroupNamesSnapshot())
                {
                    ids.Add(pair.Key);
                }
            }

            DateTime now = DateTime.Now;
            foreach (string groupId in ids)
            {
                string name = identities == null ? "" : identities.GroupName(groupId);
                if (officialGroups.TryGetValue(groupId, out OfficialGroupState state))
                {
                    double flushLeft = Configuration.GroupFlushSeconds - (now - state.LastFlush).TotalSeconds;
                    double closeLeft = Configuration.GroupAutoCloseMinutes - (now - state.LastUse).TotalMinutes;
                    list.Add(new QQGroupListenSnapshot(groupId, name, state.Listening,
                        state.Buffer.ToList(), state.PreMessage.ToList(), Math.Max(0, flushLeft), Math.Max(0, closeLeft),
                        state.LastUse));
                }
                else
                {
                    list.Add(new QQGroupListenSnapshot(groupId, name, false,
                        Array.Empty<string>(), Array.Empty<string>(), 0, 0, DateTime.MinValue));
                }
            }
        }

        return list
            .OrderByDescending(snapshot => snapshot.Listening)
            .ThenBy(snapshot => snapshot.GroupId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>设置页手动开启/关闭某个群的监听。</summary>
    public bool SetOfficialGroupListening(string groupId, bool listening)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return false;
        }
        if (listening)
        {
            lock (officialGate)
            {
                ActivateOfficialGroup(GetOfficialGroup(groupId));
            }
            DetailLog("设置页手动开启群监听：" + groupId);
            return true;
        }
        lock (officialGate)
        {
            OfficialGroupState state = GetOfficialGroup(groupId);
            state.Listening = false;
            state.Buffer.Clear();
            // 背景缓存保留：下次激活时模型还能知道之前在聊什么
        }
        DetailLog("设置页手动关闭群监听：" + groupId);
        return true;
    }

    /// <summary>设置页「立即推送」：把该群攒着的消息马上推给模型。</summary>
    public bool FlushOfficialGroupNow(string groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return false;
        }
        lock (officialGate)
        {
            if (officialGroups.TryGetValue(groupId, out OfficialGroupState state) == false)
            {
                return false;
            }
            FlushOfficialGroup(state);
        }
        return true;
    }

    static string[] SplitList(string raw) =>
        (raw ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    string GroupMode() => (Configuration.GroupMessageMode ?? "at").Trim().ToLowerInvariant();

    /// <summary>注入时追加的群聊/私聊约束句：和 QChat 的 AppendGroupChatPrompt 一样，留空就不加。</summary>
    string OfficialGroupSuffix =>
        string.IsNullOrWhiteSpace(Configuration.GroupChatPrompt) ? "" : "\n" + Configuration.GroupChatPrompt.Trim();

    string OfficialPrivateSuffix =>
        string.IsNullOrWhiteSpace(Configuration.PrivateChatPrompt) ? "" : "\n" + Configuration.PrivateChatPrompt.Trim();

    /// <summary>官方群消息入口：按配置决定立刻注入、丢弃，还是进缓存。</summary>
    void OfficialGroupInbound(string eventName, string groupId, string head, string line, bool mentioned, string rawBody)
    {
        if (SplitList(Configuration.GroupIgnoredGroups).Contains(groupId, StringComparer.OrdinalIgnoreCase))
        {
            DetailLog("群 " + groupId + " 在忽略名单里，整条丢弃");
            return;
        }

        bool atEvent = eventName == "GROUP_AT_MESSAGE_CREATE";
        bool awakened = atEvent || mentioned
                       || SplitList(Configuration.GroupWakeWords).Any(word =>
                           word.Length > 0 && rawBody.Contains(word, StringComparison.OrdinalIgnoreCase));

        lock (officialGate)
        {
            // 不管哪种策略都登记一下，设置页才能列出「共几个群」
            OfficialGroupState state = GetOfficialGroup(groupId);
            state.Head = head;

            string mode = GroupMode();
            if (mode == "all")
            {
                interactor.Chat(head + "\n" + line + OfficialGroupSuffix);
                return;
            }

            if (mode != "listen")
            {
                // at：没被唤醒只记 msg_id（上游已记），不注入
                if (awakened == false)
                {
                    DetailLog("全量群消息没 @ 机器人，只记 msg_id 不注入：" + eventName);
                    return;
                }
                interactor.Chat(head + "\n" + line + OfficialGroupSuffix);
                return;
            }

            if (state.Listening == false && awakened)
            {
                ActivateOfficialGroup(state);
            }

            int chance = Math.Clamp(Configuration.GroupProactiveChancePercent, 0, 100);
            bool accept = state.Listening || (chance > 0 && Random.Shared.Next(100) < chance);
            if (accept == false)
            {
                // 没激活也留一点背景，激活时一起带上
                state.PreMessage.Enqueue(line);
                int cap = Math.Max(1, Configuration.GroupPreBufferSize);
                while (state.PreMessage.Count > cap)
                {
                    state.PreMessage.Dequeue();
                }
                return;
            }

            if (state.Listening == false)
            {
                ActivateOfficialGroup(state);
            }
            if (state.LastFlush == default)
            {
                state.LastFlush = DateTime.Now;
            }

            state.Buffer.Add(line);
            state.LastUse = DateTime.Now;
            if (Configuration.GroupDebounce)
            {
                state.LastFlush = DateTime.Now;   // 防抖：每来一条就重新计时
            }

            int max = Configuration.GroupMaxBufferMessages;
            if (max > 0 && state.Buffer.Count >= max)
            {
                FlushOfficialGroup(state);
            }
            else if (awakened && Configuration.GroupPushOnWakeUp)
            {
                FlushOfficialGroup(state);
            }
        }
    }

    /// <summary>官方私聊入口：配了防抖就先攒，否则立即注入。</summary>
    void OfficialPrivateInbound(string head, string line)
    {
        int debounce = Math.Max(0, Configuration.PrivateDebounceSeconds);
        if (debounce == 0)
        {
            interactor.Chat(head + "\n" + line + OfficialPrivateSuffix);
            return;
        }

        lock (officialGate)
        {
            string key = head;
            if (officialPrivates.TryGetValue(key, out OfficialPrivateState state) == false)
            {
                state = new OfficialPrivateState { Head = head };
                officialPrivates[key] = state;
            }
            state.Buffer.Add(line);
            state.LastFlush = DateTime.Now;
        }
    }

    /// <summary>每个 tick 检查：到点推送、到点自动关闭。</summary>
    void OfficialListenTick()
    {
        if (Configuration == null)
        {
            return;
        }

        DateTime now = DateTime.Now;
        int flushSeconds = Math.Max(1, Configuration.GroupFlushSeconds);
        int closeMinutes = Configuration.GroupAutoCloseMinutes;
        // 防抖改成 0 时也要把攒着的私聊立刻推掉，否则那些消息会永远卡在缓存里
        int privateDebounce = Math.Max(0, Configuration.PrivateDebounceSeconds);

        lock (officialGate)
        {
            foreach (OfficialGroupState state in officialGroups.Values.ToList())
            {
                if (state.Buffer.Count > 0 && (now - state.LastFlush).TotalSeconds >= flushSeconds)
                {
                    FlushOfficialGroup(state);
                }

                if (state.Listening && closeMinutes > 0 && (now - state.LastUse).TotalMinutes >= closeMinutes)
                {
                    state.Listening = false;
                    state.Buffer.Clear();
                    // 背景缓存保留：下次唤醒时仍能带出刚才的聊天
                    DetailLog("群监听空闲超时，自动关闭：" + state.Head);
                }
            }

            foreach (OfficialPrivateState state in officialPrivates.Values.ToList())
            {
                if (state.Buffer.Count == 0 || (now - state.LastFlush).TotalSeconds < privateDebounce)
                {
                    continue;
                }
                StringBuilder text = new();
                text.AppendLine(state.Head);
                foreach (string item in state.Buffer)
                {
                    text.AppendLine(item);
                }
                state.Buffer.Clear();
                state.LastFlush = now;
                interactor.Chat(text.ToString().TrimEnd() + OfficialPrivateSuffix);
            }
        }
    }

    /// <summary>往这个官方群发过消息之后：按配置关闭监听，或刷新使用时间。</summary>
    void OfficialAfterReply(bool isGroup, string targetId)
    {
        if (isGroup == false)
        {
            return;
        }
        lock (officialGate)
        {
            if (officialGroups.TryGetValue(targetId, out OfficialGroupState state) == false)
            {
                return;
            }
            if (Configuration.GroupCloseAfterReply)
            {
                state.Listening = false;
                state.Buffer.Clear();
            }
            else
            {
                state.LastUse = DateTime.Now;
            }
        }
    }

    OfficialGroupState GetOfficialGroup(string groupId)
    {
        if (officialGroups.TryGetValue(groupId, out OfficialGroupState state) == false)
        {
            state = new OfficialGroupState { GroupId = groupId };
            officialGroups[groupId] = state;
        }
        return state;
    }

    static void ActivateOfficialGroup(OfficialGroupState state)
    {
        state.Listening = true;
        state.LastUse = DateTime.Now;
        state.LastFlush = DateTime.Now - TimeSpan.FromSeconds(Random.Shared.Next(1, 12));
    }

    void FlushOfficialGroup(OfficialGroupState state)
    {
        state.LastFlush = DateTime.Now;
        if (state.Buffer.Count == 0 && state.PreMessage.Count == 0)
        {
            return;
        }

        StringBuilder text = new();
        text.AppendLine(state.Head);
        while (state.PreMessage.Count > 0)
        {
            text.AppendLine(state.PreMessage.Dequeue());
        }
        foreach (string item in state.Buffer)
        {
            text.AppendLine(item);
        }
        state.Buffer.Clear();
        state.LastUse = DateTime.Now;

        interactor.Chat(text.ToString().TrimEnd() + OfficialGroupSuffix);

        if (Configuration.GroupCloseAfterReply)
        {
            state.Listening = false;
        }
    }
}
