using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Alife.Framework;
using AntDesign;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Suran.QQ;

/// <summary>
/// 角色工作台里的「QQ官方」页（[CharacterMenu]）。
///
/// 和设置页的分工跟 BreezeTTS 一致：设置页只放凭据和两个总开关，
/// 这一页放**全部设置**，并按分区拆成独立页签（连接 / 收信 / 发送 / 群监听 / 语音），
/// 外加只有在这里才看得到的运行时状态：网关连接、每个群的监听缓存与倒计时、语音链路自检。
///
/// 配置改的是草稿，底部点「保存设置」才落盘；误改可以「撤销改动」。
/// </summary>
[CharacterMenu("QQ官方", "message", 10)]
public sealed class QQMenuPage : CharacterMenu, IDisposable
{
    [Inject]
    public ConfigurationSystem Configurations { get; set; } = null!;

    System.Timers.Timer timer;
    string tab = "status";
    string charKey = "";
    QQConfig draft;
    QQConfig baseline;
    QQConfigFields fields;
    string notice = "";
    string expandedGroup = "";
    string voiceResult = "";
    bool voiceChecking;

    bool Dirty => draft != null && baseline != null
        && JsonSerializer.Serialize(draft) != JsonSerializer.Serialize(baseline);

    protected override void OnInitialized()
    {
        fields = new QQConfigFields(this, () => StateHasChanged(), ResolveModule);

        // 群监听的倒计时/缓存条数要实时变，1 秒刷一次
        timer = new System.Timers.Timer(1000);
        timer.Elapsed += (_, _) =>
        {
            try
            {
                _ = InvokeAsync(StateHasChanged);
            }
            catch
            {
                // 页面已经关掉，忽略
            }
        };
        timer.Start();
    }

    public void Dispose()
    {
        if (timer != null)
        {
            timer.Stop();
            timer.Dispose();
            timer = null;
        }
    }

    protected override void OnParametersSet()
    {
        string key = Character?.StorageKey ?? "";
        if (draft != null && key == charKey)
            return;
        charKey = key;

        // 和 BreezeTTS 一样以角色配置为准；读不到再退回运行中实例的配置
        QQConfig source = null;
        if (Configurations != null && key.Length > 0)
        {
            try
            {
                source = Configurations.GetConfiguration(typeof(QQModule), key) as QQConfig;
            }
            catch
            {
                source = null;
            }
        }
        source ??= ResolveModule()?.Configuration;
        if (source == null)
            return;
        draft = source.Copy();
        baseline = source.Copy();
    }

    /// <summary>宿主没把 Module 传进来时（角色没起/刚重载），退一步从活动容器里找同一个模块实例。</summary>
    QQModule ResolveModule() => ChatActivity?.Container?.Instances.OfType<QQModule>().FirstOrDefault();

    async Task Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            notice = "操作失败：" + ex.Message;
        }
        finally
        {
            StateHasChanged();
        }
    }

    Task SaveConfig() => Run(async () =>
    {
        if (draft == null)
        {
            notice = "配置还没就绪，重载角色后再试";
            return;
        }
        QQConfig committed = draft.Copy();
        if (Configurations != null && Character != null)
            Configurations.SetConfiguration(typeof(QQModule), committed, Character.StorageKey);
        QQModule module = ResolveModule();
        if (module != null)
            module.Configuration = committed;
        draft = committed.Copy();
        baseline = committed.Copy();
        notice = "设置已保存";
        await Task.CompletedTask;
    });

    void UndoConfig()
    {
        if (baseline == null)
            return;
        draft = baseline.Copy();
        notice = "未保存的改动已撤销";
    }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        int i = 0;
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "gs-root qq-root");
        QQConfigFields.Styles(b, ref i);
        PageStyles(b, ref i);

        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "gs-hero qq-hero");
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "gs-hero-title");
        b.AddContent(i++, "QQ官方 工作台");
        b.CloseElement();
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "gs-hero-sub");
        b.AddContent(i++, "状态、连接、收信、发送、群监听、语音都在这几个页签里；设置页签改完点底部「保存设置」生效。");
        b.CloseElement();
        b.CloseElement();

        Tabs(b, ref i);

        if (notice.Length > 0)
        {
            b.OpenElement(i++, "div");
            b.AddAttribute(i++, "class", "qq-notice");
            b.AddContent(i++, notice);
            b.CloseElement();
        }

        if (tab == "status")
        {
            QQModule module = ResolveModule();
            QQConfigFields.Panel(b, ref i, "网关", () =>
            {
                QQConfigFields.GatewayStatus(b, ref i, module);
            });
            QQConfigFields.Panel(b, ref i, "群消息接收状态", () =>
            {
                AddListenStatus(b, ref i);
            });
            QQConfigFields.Panel(b, ref i, "语音链路", () =>
            {
                AddVoiceCheckRow(b, ref i);
            });
            b.CloseElement();
            return;
        }

        if (draft == null)
        {
            b.OpenElement(i++, "div");
            b.AddAttribute(i++, "class", "gs-hint2");
            b.AddContent(i++, "配置还没就绪，重载角色后再进这一页。");
            b.CloseElement();
        }
        else
        {
            switch (tab)
            {
                case "connection":
                    fields.Connection(b, ref i, draft);
                    fields.Owner(b, ref i, draft);
                    fields.Network(b, ref i, draft);
                    fields.Advanced(b, ref i, draft);
                    break;
                case "inbound":
                    fields.Events(b, ref i, draft);
                    fields.Inbound(b, ref i, draft);
                    fields.Senses(b, ref i, draft);
                    break;
                case "send":
                    fields.Send(b, ref i, draft);
                    fields.Buttons(b, ref i, draft);
                    break;
                case "listen":
                    fields.GroupListen(b, ref i, draft);
                    break;
                case "voice":
                    fields.Voice(b, ref i, draft);
                    break;
            }
        }
        SaveBar(b, ref i);

        b.CloseElement();
    }

    void Tabs(RenderTreeBuilder b, ref int i)
    {
        (string Key, string Label)[] tabs =
        {
            ("status", "状态"),
            ("connection", "连接"),
            ("inbound", "收信"),
            ("send", "发送"),
            ("listen", "群监听"),
            ("voice", "语音"),
        };
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "qq-tabs");
        foreach ((string key, string label) in tabs)
        {
            b.OpenElement(i++, "button");
            b.AddAttribute(i++, "type", "button");
            b.AddAttribute(i++, "class", "qq-tab" + (tab == key ? " qq-tab-on" : ""));
            b.AddAttribute(i++, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () =>
            {
                tab = key;
                notice = "";
                StateHasChanged();
            }));
            b.AddContent(i++, label);
            b.CloseElement();
        }
        if (tab != "status" && Dirty)
        {
            b.OpenElement(i++, "span");
            b.AddAttribute(i++, "class", "qq-dirty");
            b.AddContent(i++, "● 未保存");
            b.CloseElement();
        }
        b.CloseElement();
    }

    void SaveBar(RenderTreeBuilder b, ref int i)
    {
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "qq-savebar");
        b.OpenElement(i++, "span");
        b.AddAttribute(i++, "class", "qq-savebar-note");
        b.AddContent(i++, Dirty ? "有改动还没保存。" : "当前设置已保存。");
        b.CloseElement();
        b.OpenElement(i++, "button");
        b.AddAttribute(i++, "type", "button");
        b.AddAttribute(i++, "class", "gs-btn");
        b.AddAttribute(i++, "disabled", draft == null);
        b.AddAttribute(i++, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, async () => await SaveConfig()));
        b.AddContent(i++, "保存设置");
        b.CloseElement();
        b.OpenElement(i++, "button");
        b.AddAttribute(i++, "type", "button");
        b.AddAttribute(i++, "class", "gs-btn");
        b.AddAttribute(i++, "disabled", !Dirty);
        b.AddAttribute(i++, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () =>
        {
            UndoConfig();
            StateHasChanged();
        }));
        b.AddContent(i++, "撤销改动");
        b.CloseElement();
        b.CloseElement();
    }

    // ── 运行时状态（原设置页的状态区，搬到工作台）─────────────────

    sealed record ListenRowView(string Id, string Name, bool Listening,
        IReadOnlyList<string> Buffer, IReadOnlyList<string> PreBuffer,
        double NextFlushSeconds, double AutoCloseSeconds, DateTime LastActiveAt,
        Action<bool> Toggle, Action Flush);

    void AddListenStatus(RenderTreeBuilder b, ref int seq)
    {
        QQModule module = ResolveModule();
        if (module == null)
        {
            b.OpenElement(seq++, "div");
            b.AddAttribute(seq++, "class", "gs-hint2");
            b.AddContent(seq++, "模块实例还没起来（角色未加载或插件刚重载），群状态暂时读不到。");
            b.CloseElement();
            return;
        }

        List<ListenRowView> rows = new();
        foreach (QQModule.QQGroupListenSnapshot group in module.OfficialGroupSnapshots())
        {
            QQModule.QQGroupListenSnapshot snapshot = group;
            rows.Add(new ListenRowView(snapshot.GroupId, snapshot.Name, snapshot.Listening,
                snapshot.Buffer, snapshot.PreBuffer, snapshot.NextFlushSeconds, snapshot.AutoCloseSeconds,
                snapshot.LastActiveAt,
                on => module.SetOfficialGroupListening(snapshot.GroupId, on),
                () => module.FlushOfficialGroupNow(snapshot.GroupId)));
        }

        AddListenBlock(b, ref seq, module.OfficialListeningCount, rows,
            module.OfficialListenEnabled ? "" : "当前策略不是「群监听」，下面开关不生效",
            "还没有群发过消息。机器人收到某个群的任意消息后，这个群就会出现在这里。",
            module.OfficialListenEnabled);
    }

    void AddListenBlock(RenderTreeBuilder b, ref int seq, int listeningCount, List<ListenRowView> rows,
        string warn, string emptyHint, bool interactive)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-listen-bar");
        b.OpenElement(seq++, "span");
        b.AddAttribute(seq++, "class", "gs-listen-count");
        b.AddContent(seq++, listeningCount + " 个监听中");
        b.CloseElement();
        b.OpenElement(seq++, "span");
        b.AddAttribute(seq++, "class", "gs-listen-meta");
        b.AddContent(seq++, "共 " + rows.Count + " 个群　每秒刷新");
        b.CloseElement();
        if (warn.Length > 0)
        {
            b.OpenElement(seq++, "span");
            b.AddAttribute(seq++, "class", "gs-listen-warn");
            b.AddContent(seq++, warn);
            b.CloseElement();
        }
        b.CloseElement();

        if (rows.Count == 0)
        {
            b.OpenElement(seq++, "div");
            b.AddAttribute(seq++, "class", "gs-hint2");
            b.AddContent(seq++, emptyHint);
            b.CloseElement();
            return;
        }

        foreach (ListenRowView row in rows.OrderByDescending(row => row.Listening).ThenBy(row => row.Id))
            AddListenCard(b, ref seq, row, interactive);

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-hint2");
        b.AddContent(seq++, "这里的开关只对本次运行有效：停掉后这个群的消息只留背景缓存，"
            + "等下次被 @ 或唤醒词命中再开始收。");
        b.CloseElement();
    }

    /// <summary>一个群一张卡：状态、名字与 ID、活动/缓存摘要、三个操作，缓冲点「看缓冲」才展开。</summary>
    void AddListenCard(RenderTreeBuilder b, ref int seq, ListenRowView row, bool interactive)
    {
        bool expanded = expandedGroup == row.Id;
        string last = row.LastActiveAt == DateTime.MinValue ? "还没用过" : row.LastActiveAt.ToString("HH:mm:ss");

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-listen");

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-listen-bar");

        b.OpenElement(seq++, "span");
        b.AddAttribute(seq++, "class", row.Listening ? "gs-listen-tag" : "gs-listen-tag off");
        b.AddContent(seq++, row.Listening ? "监听中" : "待机");
        b.CloseElement();

        b.OpenElement(seq++, "span");
        b.AddAttribute(seq++, "class", "gs-listen-name-wrap");
        b.OpenElement(seq++, "span");
        b.AddAttribute(seq++, "class", "gs-listen-name");
        b.AddAttribute(seq++, "title", row.Name.Length > 0 ? row.Name : row.Id);
        b.AddContent(seq++, row.Name.Length > 0 ? row.Name : row.Id);
        b.CloseElement();
        b.OpenElement(seq++, "span");
        b.AddAttribute(seq++, "class", "gs-listen-id");
        b.AddAttribute(seq++, "title", row.Id);
        b.AddContent(seq++, row.Id);
        b.CloseElement();
        b.CloseElement();

        StringBuilder meta = new();
        meta.Append("最后活动 ").Append(last)
            .Append("　待推 ").Append(row.Buffer.Count)
            .Append(" 条 · 背景 ").Append(row.PreBuffer.Count).Append(" 条");
        if (row.Listening && row.Buffer.Count > 0)
            meta.Append(" · 推送 ").Append(Math.Ceiling(row.NextFlushSeconds)).Append("s");
        if (row.Listening && row.AutoCloseSeconds > 0)
            meta.Append(" · 自动关 ").Append(Math.Ceiling(row.AutoCloseSeconds)).Append("min");

        b.OpenElement(seq++, "span");
        b.AddAttribute(seq++, "class", "gs-listen-meta");
        b.AddContent(seq++, meta.ToString());
        b.CloseElement();

        AddListenButton(b, ref seq, row.Listening ? "停止监听" : "开始监听",
            () => row.Toggle(!row.Listening), !interactive);
        AddListenButton(b, ref seq, "立即推送", row.Flush, row.Buffer.Count + row.PreBuffer.Count == 0);
        AddListenButton(b, ref seq, expanded ? "收起" : "看缓冲", () =>
        {
            expandedGroup = expanded ? "" : row.Id;
            StateHasChanged();
        }, false);

        b.CloseElement();

        if (expanded)
        {
            AddBufferBox(b, ref seq, "背景缓存（激活时一并带上）", row.PreBuffer);
            AddBufferBox(b, ref seq, "待推送", row.Buffer);
        }

        b.CloseElement();
    }

    void AddListenButton(RenderTreeBuilder b, ref int seq, string text, Action onClick, bool disabled)
    {
        b.OpenElement(seq++, "button");
        b.AddAttribute(seq++, "class", "gs-btn");
        b.AddAttribute(seq++, "type", "button");
        if (disabled)
            b.AddAttribute(seq++, "disabled", true);
        b.AddAttribute(seq++, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, _ => onClick()));
        b.AddContent(seq++, text);
        b.CloseElement();
    }

    void AddBufferBox(RenderTreeBuilder b, ref int seq, string label, IReadOnlyList<string> messages)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-buffer-label");
        b.AddContent(seq++, label);
        b.CloseElement();

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-buffer");
        if (messages.Count == 0)
        {
            b.OpenElement(seq++, "div");
            b.AddAttribute(seq++, "class", "gs-buffer-empty");
            b.AddContent(seq++, "暂无消息");
            b.CloseElement();
        }
        else
        {
            foreach (string message in messages)
            {
                b.OpenElement(seq++, "div");
                b.AddAttribute(seq++, "class", "gs-buffer-item");
                b.AddContent(seq++, message);
                b.CloseElement();
            }
        }
        b.CloseElement();
    }

    /// <summary>语音链路自检：ffmpeg / 编码器 / pilk 一次跑完，结果显示在按钮下面。</summary>
    void AddVoiceCheckRow(RenderTreeBuilder b, ref int seq)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-listen-bar");
        b.OpenElement(seq++, "button");
        b.AddAttribute(seq++, "class", "gs-btn");
        b.AddAttribute(seq++, "type", "button");
        if (voiceChecking)
            b.AddAttribute(seq++, "disabled", true);
        b.AddAttribute(seq++, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, _ => CheckVoiceChain()));
        b.AddContent(seq++, voiceChecking ? "检测中…" : "检测语音链路");
        b.CloseElement();
        b.CloseElement();
        if (voiceResult.Length > 0)
        {
            b.OpenElement(seq++, "pre");
            b.AddAttribute(seq++, "class", "gs-buffer");
            b.AddAttribute(seq++, "style", "white-space:pre-wrap;max-height:180px;margin:8px 0;");
            b.AddContent(seq++, voiceResult);
            b.CloseElement();
        }
    }

    async void CheckVoiceChain()
    {
        QQModule module = ResolveModule();
        if (module == null)
        {
            voiceResult = "模块实例还没起来";
            StateHasChanged();
            return;
        }
        voiceChecking = true;
        voiceResult = "";
        StateHasChanged();
        try
        {
            voiceResult = await module.VoiceChainCheckAsync();
        }
        catch (Exception ex)
        {
            voiceResult = "检测失败：" + ex.Message;
        }
        finally
        {
            voiceChecking = false;
            StateHasChanged();
        }
    }

    static void PageStyles(RenderTreeBuilder b, ref int seq)
    {
        b.OpenElement(seq++, "style");
        b.AddContent(seq++, """
            /* 工作台是整页：去掉设置页那种卡片外观，自己铺满并滚动 */
            .qq-root{
                max-width:none;width:100%;height:100%;min-height:100%;box-sizing:border-box;
                overflow-x:hidden;overflow-y:auto;overscroll-behavior:contain;scrollbar-gutter:stable;
                border:none;border-radius:0;box-shadow:none;background:transparent;
            }
            .qq-tabs{
                display:flex;align-items:center;gap:6px;flex-wrap:wrap;
                margin:0 0 14px;padding-bottom:10px;border-bottom:1px solid #f7e0ec;
            }
            .qq-tab{
                font-size:12.5px;padding:3px 14px;border-radius:999px;cursor:pointer;line-height:1.9;
                border:1px solid #f7e0ec;background:#fff;color:#666;font-weight:600;font-family:inherit;
                transition:background .15s ease,border-color .15s ease,color .15s ease;
            }
            .qq-tab:hover{border-color:#eb2f96;background:#fff0f7;color:#eb2f96;}
            .qq-tab-on,.qq-tab-on:hover{
                background:linear-gradient(120deg,#eb2f96 0%,#f759ab 100%);
                border-color:transparent;color:#fff;
            }
            .qq-dirty{
                margin-left:auto;font-size:11px;font-weight:600;color:#c2410c;
                padding:1px 9px;border-radius:999px;background:#fff4e5;border:1px solid #f5d9ae;
            }
            .qq-notice{
                font-size:12px;padding:8px 12px;margin:0 0 12px;border-radius:10px;
                background:#fffafd;border:1px solid #f7e0ec;color:#434343;
            }
            .qq-savebar{
                display:flex;align-items:center;gap:8px;flex-wrap:wrap;
                margin:20px 0 0;padding:12px 14px;border-radius:12px;
                background:#fffafd;border:1px solid #f7e0ec;
            }
            .qq-savebar-note{margin-right:auto;font-size:12px;opacity:.65;}
            .qq-savebar .gs-btn{font-size:12px;padding:4px 16px;font-weight:600;}
            @media (prefers-color-scheme:dark){
                .qq-tabs{border-color:#33232c;}
                .qq-tab{background:#1e2127;border-color:#33232c;color:#aeb4bd;}
                .qq-tab:hover{background:#2a1622;border-color:#8a3a63;color:#e8ccdd;}
                .qq-dirty{background:#3a2a12;border-color:#6b4a18;color:#f0b46a;}
                .qq-notice,.qq-savebar{background:#1e2127;border-color:#33232c;color:#c9cdd4;}
            }
            """);
        b.CloseElement();
    }
}
