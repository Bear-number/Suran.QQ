using System;
using System.Collections.Generic;
using AntDesign;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Suran.QQ;

/// <summary>
/// QQ 配置字段的共享渲染器——对应 BreezeTTS 里 BreezeConnectionFields / BreezePlaybackFields 的角色。
///
/// 设置页只调 <see cref="Simple"/>（凭据 + 两个总开关），工作台调
/// <see cref="Events"/> / <see cref="Inbound"/> / … / <see cref="Advanced"/>（全部）。
/// 两边共用同一份控件与文案；页面传进来的 <c>cfg</c> 可以是配置本体，也可以是工作台草稿。
/// </summary>
internal sealed class QQConfigFields
{
    readonly ComponentBase owner;
    readonly Action changed;
    readonly Func<QQModule?>? module;

    public QQConfigFields(ComponentBase owner, Action changed, Func<QQModule?>? module = null)
    {
        this.owner = owner;
        this.changed = changed;
        this.module = module;
    }

    void Refresh() => changed?.Invoke();

    /// <summary>「查看提示词」是否展开。渲染器实例跟着页面走，翻页签不会丢。</summary>
    bool showPrompt;

    static readonly List<(string Value, string Label, bool Enabled)> GroupModeOptions = new()
    {
        ("at", "仅 @ 时注入（推荐）", true),
        ("all", "全部群消息都注入", true),
        ("listen", "群监听（唤醒词/概率激活，缓存后推送）", true),
    };

    static readonly List<(string Value, string Label, bool Enabled)> AtStyleOptions = new()
    {
        ("legacy", "legacy：<@openid>（默认）", true),
        ("new", "new：<qqbot-at-user id=… />", true),
    };

    static readonly List<(string Value, string Label, bool Enabled)> ConflictOptions = new()
    {
        ("markdown", "优先 markdown，自动丢引用（默认）", true),
        ("reference", "优先引用，非必须场景退回纯文本", true),
    };

    static readonly List<(string Value, string Label, bool Enabled)> AnimatedOptions = new()
    {
        ("auto", "auto：原图 → APNG → 静态 PNG → 文件", true),
        ("static", "static：跳过原图直接转档", true),
        ("file", "file：原图按文件发", true),
    };

    // ── 设置页：只留凭据与两个总开关 ────────────────────────────

    public void Simple(RenderTreeBuilder b, ref int start, QQConfig cfg)
    {
        int i = start;
        Connection(b, ref i, cfg);

        Panel(b, ref i, "总开关", () =>
        {
            Switch(b, ref i, "注入会话", cfg.InjectInboundMessages, v => cfg.InjectInboundMessages = v);
            Hint(b, ref i, "开：QQ 消息以「好友/群里发来的」形式进 Alife 会话；关：只当外发通道。");
            Switch(b, ref i, "允许发送消息", cfg.EnableSend, v => cfg.EnableSend = v);
            Hint(b, ref i, "关掉后所有发送类工具不可用，收信与注入不受影响。");
        });
        start = i;
    }

    /// <summary>凭据与连接：配置页的 Simple 和工作台「连接」页签共用这一块。</summary>
    public void Connection(RenderTreeBuilder b, ref int start, QQConfig cfg)
    {
        int i = start;
        Panel(b, ref i, "凭据与连接", () =>
        {
            Input(b, ref i, "AppID", cfg.AppId, v => cfg.AppId = v,
                "q.qq.com 后台「开发管理」里的数字 ID");
            Hint(b, ref i, "只存在本地配置里，插件不会把它写进日志或回显到会话。");

            Input(b, ref i, "AppSecret", cfg.AppSecret, v => cfg.AppSecret = v,
                "与 AppID 配对的机器人密钥", password: true);
            Hint(b, ref i, "等同机器人密码，别外传、别提交到仓库。");

            Input(b, ref i, "API 根地址", cfg.ApiBaseUrl, v => cfg.ApiBaseUrl = v,
                "留空 = https://api.bot.qq.com；沙盒可填 https://sandbox.api.sgroup.qq.com");
            Hint(b, ref i, "一般留空。要连沙盒环境才改这里。");

            Input(b, ref i, "数据目录", cfg.DataFolder, v => cfg.DataFolder = v,
                "相对 Alife Storage，默认 QQOfficial");
            Hint(b, ref i, "放已见消息 id 台账和各会话的被动回复剩余额度；不存 token 与密钥。");
        });
        start = i;
    }

    // ── 工作台：全部设置 ────────────────────────────────────────

    public void Events(RenderTreeBuilder b, ref int start, QQConfig cfg)
    {
        int i = start;
        Panel(b, ref i, "事件订阅", () =>
        {
            Switch(b, ref i, "群与私聊消息", cfg.IntentGroupAndPrivate, v => cfg.IntentGroupAndPrivate = v);
            Hint(b, ref i, "C2C_MESSAGE_CREATE / GROUP_AT_MESSAGE_CREATE / GROUP_MESSAGE_CREATE 等。关掉就什么都收不到。");

            Switch(b, ref i, "群成员变化与加群申请", cfg.IntentGroupMember, v => cfg.IntentGroupMember = v);
            Hint(b, ref i, "官方标注是白名单能力；没开权限时连接会被平台以关闭码 4014 断掉，那时把这一项关掉。");

            Switch(b, ref i, "按钮互动", cfg.IntentInteraction, v => cfg.IntentInteraction = v);
            Hint(b, ref i, "INTERACTION_CREATE：用户点消息按钮或快捷菜单。要用键盘按钮才需要开。");

            Switch(b, ref i, "消息审核结果", cfg.IntentMessageAudit, v => cfg.IntentMessageAudit = v);
            Hint(b, ref i, "MESSAGE_AUDIT_PASS / MESSAGE_AUDIT_REJECT。");

            Switch(b, ref i, "频道与子频道变化", cfg.IntentGuilds, v => cfg.IntentGuilds = v);
            Hint(b, ref i, "GUILDS 位。只有玩 QQ 频道才需要，默认关。");
        });
        start = i;
    }

    public void Inbound(RenderTreeBuilder b, ref int start, QQConfig cfg)
    {
        int i = start;
        Panel(b, ref i, "收信与注入", () =>
        {
            Switch(b, ref i, "注入会话", cfg.InjectInboundMessages, v => cfg.InjectInboundMessages = v);
            Hint(b, ref i, "开：QQ 收到的消息以「好友/群里发来的」形式进 Alife 会话，由主 Agent 决定怎么回。"
                + "关：只保留发送面，纯当外发通道。");

            LabeledSelect(b, ref i, cfg.GroupMessageMode, v => cfg.GroupMessageMode = v, GroupModeOptions);
            Hint(b, ref i, "手机 QQ 里开了「获取群内全部消息」后：选「仅 @」时，没 @ 机器人的闲聊只记可回复的 msg_id、"
                + "不进上下文（之后仍能被动回复）；选「全部」则每条群消息都进会话，容易刷屏。");

            TextArea(b, ref i, "群聊附加提示词", cfg.GroupChatPrompt,
                v => cfg.GroupChatPrompt = v, "注入群消息时末尾追加的约束句，留空不追加");
            Hint(b, ref i, "每条（或每批）群消息末尾都会带上这句，用来提醒模型这是群聊、控制长度、别乱插话。");

            TextArea(b, ref i, "私聊附加提示词", cfg.PrivateChatPrompt,
                v => cfg.PrivateChatPrompt = v, "注入私聊消息时末尾追加的约束句，留空不追加");
            Hint(b, ref i, "私聊消息末尾追加的约束句，同样留空就不加。");

            Number(b, ref i, "读文件内容上限（字）", cfg.InboundFileMaxChars,
                v => cfg.InboundFileMaxChars = v, 0, 20000);
            Hint(b, ref i, "收到的文本类附件（txt/log/json/md/csv 等）读进上下文的最大字数，默认 4000；0 = 只给链接不读内容。");

            Switch(b, ref i, "注入引用内容", cfg.InjectQuotedContent, v => cfg.InjectQuotedContent = v);
            Hint(b, ref i, "开：有人引用回复时，把被引用那条的作者与正文（截断 300 字）一起注入。");

            Number(b, ref i, "私聊防抖（秒）", cfg.PrivateDebounceSeconds,
                v => cfg.PrivateDebounceSeconds = v, 0, 300);
            Hint(b, ref i, "大于 0 时，单个私聊的消息先缓存，停这么久再一起推；0 = 每条立即推。");

            Number(b, ref i, "事件去重保留（分钟）", cfg.DedupTtlMinutes,
                v => cfg.DedupTtlMinutes = v, 1, 1440);
            Hint(b, ref i, "同一 msg_id 官方可能重复推送，这段时间内见过的 id 直接吞掉，默认 30 分钟。");

            Number(b, ref i, "双副本宽限（秒）", cfg.DoubleCopyGraceSeconds,
                v => cfg.DoubleCopyGraceSeconds = v, 0, 5);
            Hint(b, ref i, "全量群消息与 @ 副本同时到达时，全量副本等这么久再处理，发现 @ 副本已到就丢弃，默认 0。");
        });
        start = i;
    }

    public void Senses(RenderTreeBuilder b, ref int start, QQConfig cfg)
    {
        int i = start;
        Panel(b, ref i, "听觉与视觉", () =>
        {
            Switch(b, ref i, "语音本地识别", cfg.InboundSpeechRecognition, v => cfg.InboundSpeechRecognition = v);
            Hint(b, ref i, "开：把收到的语音下载下来，交给本机听觉模型（SenseVoice 等）转成文字补进上下文。"
                + "支持 QQ 的 silk 格式；没装听觉模型或识别失败会自动跳过。默认关。");

            Switch(b, ref i, "视觉自动描述", cfg.InboundImageCaption, v => cfg.InboundImageCaption = v);
            Hint(b, ref i, "开：图片额外让视觉模型描述一行（本地模型或在线 API 都行，取决于启用了哪个视觉插件；视频不额外描述）。"
                + "不管开不开，图片和视频都会带上 URL 与 <AnalyseImage>/<AnalyzeVideo> 提示，模型自己决定要不要看。默认关。");

            TextArea(b, ref i, "图片描述提示词", cfg.InboundImagePrompt,
                v => cfg.InboundImagePrompt = v, "发给视觉模型的问题，留空用默认");
            Hint(b, ref i, "默认要求精简描述人物、文字和场景；图里有需要特别关注的东西时可以改。");

            Number(b, ref i, "感官结果字数上限", cfg.InboundSenseMaxChars,
                v => cfg.InboundSenseMaxChars = v, 50, 2000);
            Hint(b, ref i, "本地语音识别文本、图片描述各自写进上下文的最大字数，默认 300。");
        });
        start = i;
    }

    public void Send(RenderTreeBuilder b, ref int start, QQConfig cfg)
    {
        int i = start;
        Panel(b, ref i, "发送与格式", () =>
        {
            Switch(b, ref i, "允许发送消息", cfg.EnableSend, v => cfg.EnableSend = v);
            Hint(b, ref i, "关掉后所有发送类工具不可用，收信与注入不受影响。");

            Switch(b, ref i, "允许主动消息", cfg.AllowProactiveMessage, v => cfg.AllowProactiveMessage = v);
            Hint(b, ref i, "被动回复有平台窗口（群 5 分钟 5 次、单聊 60 分钟 4 次）；窗口外的主动消息要走这条，且受配额限制。"
                + "默认关：不在会话里主动插话。");

            LabeledSelect(b, ref i, cfg.AtMarkupStyle, v => cfg.AtMarkupStyle = v, AtStyleOptions);
            Hint(b, ref i, "发送 @ 时用的标记形态。部分环境会把 new 形态当纯文本，默认 legacy 更稳。");

            Switch(b, ref i, "含 @ 强制 markdown", cfg.AtForceMarkdown, v => cfg.AtForceMarkdown = v);
            Hint(b, ref i, "开：正文里出现 @ 标记就自动按 markdown 发；关：可能显示成原始标记。");

            LabeledSelect(b, ref i, cfg.MediaConflictPrefer, v => cfg.MediaConflictPrefer = v, ConflictOptions);
            Hint(b, ref i, "markdown 与引用只能留一个：默认留 markdown 丢引用；选 reference 时，非必须场景退回纯文本发引用。");

            Number(b, ref i, "输入状态帧数上限", cfg.TypingMaxFrames, v => cfg.TypingMaxFrames = v, 0, 20);
            Hint(b, ref i, "每个会话一次对话里最多发几帧「正在输入」（msg_type=6 也占被动回复额度），默认 3；0 = 不发。");

            SubGroup(b, ref i, "输入状态");
            Number(b, ref i, "延时（秒）", cfg.TypingDelaySeconds, v => cfg.TypingDelaySeconds = v, 0, 30);
            Hint(b, ref i, "收到私聊后隔多久才显示「正在输入」，默认 2 秒；0 = 立刻。");

            Number(b, ref i, "防抖（秒）", cfg.TypingDebounceSeconds, v => cfg.TypingDebounceSeconds = v, 0, 120);
            Hint(b, ref i, "同一个人刚显示过状态后，这么久内不再重复显示，默认 3 秒；0 = 不防抖。");

            Switch(b, ref i, "主动也显示", cfg.TypingAllowProactive, v => cfg.TypingAllowProactive = v);
            Hint(b, ref i, "开：没有被动 msg_id 时也发一帧主动状态（占主动额度）。");

            LabeledSelect(b, ref i, cfg.AnimatedImageMode, v => cfg.AnimatedImageMode = v, AnimatedOptions);
            Hint(b, ref i, "GIF/WebP 的发送档位：auto 逐级降级；static 跳过原图；file 原图按文件发（保动画）。");

            Number(b, ref i, "素材配额提醒阈值", cfg.MarkdownImageWarnAt, v => cfg.MarkdownImageWarnAt = v, 0, 100);
            Hint(b, ref i, "markdown 素材达到这个数量就在上传后提醒，平台上限 100，默认 90；0 = 不提醒。");
        });
        start = i;
    }

    public void Buttons(RenderTreeBuilder b, ref int start, QQConfig cfg)
    {
        int i = start;
        Panel(b, ref i, "按钮", () =>
        {
            Switch(b, ref i, "回调按钮改指令", cfg.KeyboardCallbackToCommand, v => cfg.KeyboardCallbackToCommand = v);
            Hint(b, ref i, "开：把 action.type=1（回调）自动换成 type=2 + enter，平台推不了互动事件时按钮也能点一下就发。");

            Switch(b, ref i, "按钮限次策略", cfg.ButtonLimitEnabled, v => cfg.ButtonLimitEnabled = v);
            Hint(b, ref i, "开：按按钮 JSON 里的 once / ttl / limit 拦截超额、过期、重复点击；没写的用下面两个默认值。");

            Switch(b, ref i, "追踪前缀", cfg.ButtonPolicyToken, v => cfg.ButtonPolicyToken = v);
            Hint(b, ref i, "开：给指令按钮的 data 自动加 #qb# 前缀，点回来后剥掉，防止用户正常发一句和 data 一样的文字被误判成点击。"
                + "只在开了「按钮限次策略」时才加——群聊里这个前缀会原样出现在用户输入框里。");

            Switch(b, ref i, "默认每人一次", cfg.ButtonDefaultOnce, v => cfg.ButtonDefaultOnce = v);
            Hint(b, ref i, "按钮没写 once / limit 时，默认每人只能点一次。");

            Number(b, ref i, "默认有效期（秒）", cfg.ButtonDefaultTtlSeconds, v => cfg.ButtonDefaultTtlSeconds = v, 0, 86400);
            Hint(b, ref i, "按钮没写 ttl 时的有效期，0 = 不限。");

            Number(b, ref i, "账本上限", cfg.ButtonMaxEntries, v => cfg.ButtonMaxEntries = v, 50, 5000);
            Hint(b, ref i, "内存里最多记多少个按钮，超出先清过期的再清最旧的。重启后账本清空。");

            Switch(b, ref i, "拦截时提示模型", cfg.ButtonNotifyOnLimit, v => cfg.ButtonNotifyOnLimit = v);
            Hint(b, ref i, "开：点击被拦下时在会话里提示模型（不打扰用户）。");

            Switch(b, ref i, "拦截时提示用户", cfg.ButtonNotifyUserOnLimit, v => cfg.ButtonNotifyUserOnLimit = v);
            Hint(b, ref i, "开：点击被拦下时直接给对方回一句固定文案（占一次被动回复额度）。");

            Input(b, ref i, "用户提示文案", cfg.ButtonLimitNotifyText, v => cfg.ButtonLimitNotifyText = v, "这个按钮已经用过啦");

            Switch(b, ref i, "账本持久化", cfg.ButtonLedgerPersist, v => cfg.ButtonLedgerPersist = v);
            Hint(b, ref i, "开：限次账本落盘，重启后「每人一次/限次」仍然成立，默认开。");
        });
        start = i;
    }

    public void GroupListen(RenderTreeBuilder b, ref int start, QQConfig cfg)
    {
        int i = start;
        Panel(b, ref i, "群监听（官方通道）", () =>
        {
            Hint(b, ref i, "每个群的实时状态与监听开关在工作台「状态」页签里；这里只配监听规则。"
                + "「群消息·全量注入策略」选「群监听」时生效：唤醒词 / @ / 概率任一命中就开始听这个群，"
                + "消息先缓存、按间隔成批推送；回复后或长时间没动静自动关闭。");

            Input(b, ref i, "唤醒词（逗号分隔）", cfg.GroupWakeWords, v => cfg.GroupWakeWords = v, "例如：机器人,在吗,小苏");
            Hint(b, ref i, "原始群消息里出现这些词就激活监听；@ 机器人同样会激活。留空 = 只靠 @ 和概率。");

            Input(b, ref i, "忽略群（逗号分隔）", cfg.GroupIgnoredGroups, v => cfg.GroupIgnoredGroups = v,
                "填 group_openid，多个用逗号分隔");
            Hint(b, ref i, "这些群的消息完全不处理：不注入、不缓存、不激活。");

            Number(b, ref i, "概率激活（%）", cfg.GroupProactiveChancePercent,
                v => cfg.GroupProactiveChancePercent = v, 0, 100);
            Hint(b, ref i, "没被唤醒时，每条群消息有多大比例直接开始监听。0 = 只靠 @ 和唤醒词。");

            Number(b, ref i, "缓存推送间隔（秒）", cfg.GroupFlushSeconds, v => cfg.GroupFlushSeconds = v, 1, 120);
            Hint(b, ref i, "监听中把消息攒这么久再成批推给模型，默认 12 秒。");

            Switch(b, ref i, "防抖", cfg.GroupDebounce, v => cfg.GroupDebounce = v);
            Hint(b, ref i, "开：每来一条新消息就重新计时，等聊天停下来再推；关：从激活开始按固定间隔推。");

            Number(b, ref i, "最大缓存条数", cfg.GroupMaxBufferMessages,
                v => cfg.GroupMaxBufferMessages = v, -1, 1000);
            Hint(b, ref i, "监听中缓存超过这个数就立刻推送，防刷屏；-1 = 不限。");

            Number(b, ref i, "背景缓存条数", cfg.GroupPreBufferSize, v => cfg.GroupPreBufferSize = v, 1, 50);
            Hint(b, ref i, "没监听时也悄悄留最近几条消息，激活时一起带上，让模型知道刚才在聊什么。");

            Switch(b, ref i, "唤醒立即推送", cfg.GroupPushOnWakeUp, v => cfg.GroupPushOnWakeUp = v);
            Hint(b, ref i, "开：一命中唤醒词就立刻推缓存，不等间隔；关：等下一个推送点。");

            Switch(b, ref i, "回复后关闭监听", cfg.GroupCloseAfterReply, v => cfg.GroupCloseAfterReply = v);
            Hint(b, ref i, "开：往这个群发过消息后就退出监听，等下个唤醒词；关：一直听到自动关闭时间到。");

            Number(b, ref i, "自动关闭（分钟）", cfg.GroupAutoCloseMinutes,
                v => cfg.GroupAutoCloseMinutes = v, 0, 240);
            Hint(b, ref i, "监听中这么久没再被唤醒/使用就自动退出，默认 4 分钟；0 = 不自动关。");

            SubGroup(b, ref i, "快速预设");
            Hint(b, ref i, "两套常用组合，会把「全量注入策略」一并切到「群监听」；改完仍需点下方保存。");
            b.OpenElement(i++, "div");
            b.AddAttribute(i++, "class", "gs-listen-bar");
            ActionButton(b, ref i, "开关监听式", () =>
            {
                cfg.GroupMessageMode = "listen";
                cfg.GroupDebounce = false;
                cfg.GroupFlushSeconds = 15;
                cfg.GroupMaxBufferMessages = -1;
                cfg.GroupPreBufferSize = 5;
                cfg.GroupProactiveChancePercent = 0;
                cfg.GroupCloseAfterReply = false;
                Refresh();
            });
            ActionButton(b, ref i, "随机触发式", () =>
            {
                cfg.GroupMessageMode = "listen";
                cfg.GroupDebounce = true;
                cfg.GroupFlushSeconds = 3;
                cfg.GroupMaxBufferMessages = 5;
                cfg.GroupPreBufferSize = 3;
                cfg.GroupProactiveChancePercent = 5;
                cfg.GroupCloseAfterReply = true;
                Refresh();
            });
            b.CloseElement();
            Hint(b, ref i, "开关监听式：只在唤醒词 / @ 命中时开始听，其余时间只留背景缓存，适合桌面宠物。");
            Hint(b, ref i, "随机触发式：小概率自动听 + 小缓存 + 回复后关闭，适合全天挂机的冒泡机器人。");
        });
        start = i;
    }

    public void Voice(RenderTreeBuilder b, ref int start, QQConfig cfg)
    {
        int i = start;
        Panel(b, ref i, "语音条", () =>
        {
            Input(b, ref i, "silk 编码器（python.exe）", cfg.SilkEncoderPath, v => cfg.SilkEncoderPath = v,
                "留空 = 自动用运行时 python");
            Hint(b, ref i, "官方语音条要 silk，而 ffmpeg 只能解码不能编码，所以需要一个装了 pilk 的 python。"
                + "留空时自动用宿主运行时里的 python + 插件自带 tools/to_silk.py。");

            Input(b, ref i, "编码器参数", cfg.SilkEncoderArgs, v => cfg.SilkEncoderArgs = v,
                @"占位符 {in}、{out}，如 ""D:\...\to_silk.py"" {in} {out}");
            Hint(b, ref i, "插件会把 {in} 换成 pcm、{out} 换成 silk，再原样传给上面那个解释器。留空则用自动参数。");

            Number(b, ref i, "采样率（Hz）", cfg.VoicePcmRate, v => cfg.VoicePcmRate = v, 8000, 48000);
            Hint(b, ref i, "转 silk 前 ffmpeg 的 pcm 采样率，默认 24000。手配编码器时要和它自己的参数对齐，自动方案会自动带上。");

            Switch(b, ref i, "响度归一化", cfg.VoiceLoudnessNormalize, v => cfg.VoiceLoudnessNormalize = v);
            Hint(b, ref i, "开：转码前跑 loudnorm（-16 LUFS），TTS 音量忽大忽小时更稳；关：保持原始音量。");

            Switch(b, ref i, "放行非 silk 直传", cfg.AllowNonSilkVoice, v => cfg.AllowNonSilkVoice = v);
            Hint(b, ref i, "开：没有编码器时直接把 mp3/wav 当文件发出去。关：宁可报错也不降级。");

            Number(b, ref i, "最长秒数", cfg.VoiceMaxSeconds, v => cfg.VoiceMaxSeconds = v, 10, 3600);
            Hint(b, ref i, "转码前截断超过这个长度的音频，默认 300 秒。");
        });
        start = i;
    }

    public void Advanced(RenderTreeBuilder b, ref int start, QQConfig cfg)
    {
        int i = start;
        Panel(b, ref i, "运行与调试", () =>
        {
            Switch(b, ref i, "功能文档常驻", cfg.DocumentAlwaysResident, v => cfg.DocumentAlwaysResident = v);
            Hint(b, ref i, "关（默认）：完整文档不常驻，模型输出 <QQModule/> 才注入，省 token；"
                + "开：完整文档常驻系统提示词，模型随时看得到全部工具。改完要重载插件。");

            TextArea(b, ref i, "常用功能提示词", cfg.MainPrompt, v => cfg.MainPrompt = v,
                "留空用默认模板：发消息、媒体、语音、按钮、撤回、找人、群管理");
            Hint(b, ref i, "注入主 LLM 系统提示词，只写常用出口；完整文档仍走隐式加载。模板里可以直接写 <QQSend> 这类标签说明。");
            ActionButton(b, ref i, "填入默认模板", () =>
            {
                cfg.MainPrompt = QQModule.DefaultMainPrompt;
                Refresh();
            });
            ActionButton(b, ref i, "恢复默认提示词", () =>
            {
                cfg.MainPrompt = "";
                Refresh();
            });
            ActionButton(b, ref i, showPrompt ? "收起提示词" : "查看提示词", () =>
            {
                showPrompt = !showPrompt;
                Refresh();
            });
            ActionButton(b, ref i, "重新注入", () => module?.Invoke()?.RefreshPrompt());
            Hint(b, ref i, "「填入默认模板」把内置内容放进输入框，方便看着改；「恢复默认提示词」清空即回到内置模板。"
                + "改完保存后点「重新注入」立即生效，不用重载插件。");

            if (showPrompt)
            {
                string custom = (cfg.MainPrompt ?? "").Trim();
                Hint(b, ref i, custom.Length > 0
                    ? "下面是当前填写的模板，保存并点「重新注入」后生效："
                    : "输入框为空，下面是实际生效的内置默认模板：");
                b.OpenElement(i++, "pre");
                b.AddAttribute(i++, "style",
                    "margin:8px 0 0;padding:10px 12px;border-radius:10px;border:1px solid #f7d6e6;"
                    + "background:#fffafd;color:#3d2a33;font:12px/1.7 ui-monospace,Consolas,'Cascadia Mono',monospace;"
                    + "white-space:pre-wrap;word-break:break-word;max-height:320px;overflow:auto;");
                b.AddContent(i++, custom.Length > 0 ? custom : QQModule.DefaultMainPrompt);
                b.CloseElement();
            }

            Switch(b, ref i, "详细日志", cfg.DetailLog, v => cfg.DetailLog = v);
            Hint(b, ref i, "记录请求与事件的时间、错误码。不会记录 AppSecret 与 access_token。");

            Switch(b, ref i, "高风险群管理工具", cfg.EnableSensitiveTools, v => cfg.EnableSensitiveTools = v);
            Hint(b, ref i, "开：允许踢人 / 拉黑 / 频道踢人 / 改频道权限；默认关，调用会直接报错。单人禁言、加群审批和只读查询不受影响。");

            Input(b, ref i, "敏感操作白名单", cfg.SensitiveToolAllowList, v => cfg.SensitiveToolAllowList = v,
                "允许触发上述操作的用户 openid，逗号分隔。留空则只认群主和管理员；频道操作留空会直接拒绝");
        });
        start = i;
    }

    public void Network(RenderTreeBuilder b, ref int start, QQConfig cfg)
    {
        int i = start;
        Panel(b, ref i, "网络与重连", () =>
        {
            Input(b, ref i, "HTTP 代理", cfg.ProxyUrl, v => cfg.ProxyUrl = v,
                "如 http://127.0.0.1:7890，留空直连");
            Hint(b, ref i, "api.bot.qq.com 和 wss 网关连不上时，先看这一项。");

            Number(b, ref i, "单次请求超时（秒）", cfg.RequestTimeoutSeconds,
                v => cfg.RequestTimeoutSeconds = v, 5, 120);
            Hint(b, ref i, "REST 单次请求的超时，默认 20。");

            Number(b, ref i, "断线重连间隔（秒）", cfg.ReconnectSeconds,
                v => cfg.ReconnectSeconds = v, 1, 60);
            Hint(b, ref i, "心跳间隔由网关下发，这一项只管连不上时的重试节奏，默认 3 秒。");
        });
        start = i;
    }

    /// <summary>网关状态行（设置页和工作台共用）。</summary>
    public static void GatewayStatus(RenderTreeBuilder b, ref int seq, QQModule module)
    {
        bool ready = module != null && module.IsOfficialGatewayReady;
        string state = module == null ? "模块实例还没起来，读不到网关状态" : module.OfficialGatewayState;

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-listen-bar");
        b.OpenElement(seq++, "span");
        b.AddAttribute(seq++, "class", ready ? "gs-listen-tag" : "gs-listen-tag off");
        b.AddContent(seq++, ready ? "网关已就绪" : "网关未就绪");
        b.CloseElement();
        b.OpenElement(seq++, "span");
        b.AddAttribute(seq++, "class", "gs-hint2");
        b.AddContent(seq++, state);
        b.CloseElement();
        b.CloseElement();
    }

    // ── 小节与控件 ──────────────────────────────────────────────

    public static void Panel(RenderTreeBuilder b, ref int seq, string title, Action render)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-panel");
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-panel-head");
        b.OpenElement(seq++, "span");
        b.AddAttribute(seq++, "class", "gs-panel-dot");
        b.CloseElement();
        b.AddContent(seq++, title);
        b.CloseElement();
        render();
        b.CloseElement();
    }

    void Label(RenderTreeBuilder b, ref int seq, string text)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-label");
        b.AddContent(seq++, text);
        b.CloseElement();
    }

    static void Hint(RenderTreeBuilder b, ref int seq, string text)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-hint2");
        b.AddContent(seq++, text);
        b.CloseElement();
    }

    void Input(RenderTreeBuilder b, ref int seq, string label, string value, Action<string> setter,
        string placeholder = "请输入", bool password = false)
    {
        Label(b, ref seq, label);
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-field");
        b.OpenComponent<Input<string>>(seq++);
        b.AddAttribute(seq++, "Value", value);
        b.AddAttribute(seq++, "Placeholder", placeholder);
        if (password)
            b.AddAttribute(seq++, "Type", InputType.Password);
        b.AddAttribute(seq++, "ValueChanged", EventCallback.Factory.Create<string>(owner, setter));
        b.CloseComponent();
        b.CloseElement();
    }

    void Number(RenderTreeBuilder b, ref int seq, string label, int value, Action<int> setter, int min, int max)
    {
        Label(b, ref seq, label);
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-field");
        b.OpenComponent<InputNumber<int>>(seq++);
        b.AddAttribute(seq++, "Value", value);
        b.AddAttribute(seq++, "Min", min);
        b.AddAttribute(seq++, "Max", max);
        b.AddAttribute(seq++, "Style", "width:100%;");
        b.AddAttribute(seq++, "ValueChanged", EventCallback.Factory.Create<int>(owner, setter));
        b.CloseComponent();
        b.CloseElement();
    }

    static void SubGroup(RenderTreeBuilder b, ref int seq, string title)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-subhead");
        b.AddContent(seq++, title);
        b.CloseElement();
    }

    void Switch(RenderTreeBuilder b, ref int seq, string label, bool value, Action<bool> setter)
    {
        Label(b, ref seq, label);
        b.OpenComponent<Switch>(seq++);
        b.AddAttribute(seq++, "Checked", value);
        b.AddAttribute(seq++, "CheckedChildren", "开");
        b.AddAttribute(seq++, "UnCheckedChildren", "关");
        b.AddAttribute(seq++, "CheckedChanged", EventCallback.Factory.Create<bool>(owner, setter));
        b.CloseComponent();
    }

    void TextArea(RenderTreeBuilder b, ref int seq, string label, string value, Action<string> setter,
        string placeholder = "")
    {
        Label(b, ref seq, label);
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-field");
        b.OpenElement(seq++, "textarea");
        b.AddAttribute(seq++, "value", value);
        b.AddAttribute(seq++, "placeholder", placeholder);
        b.AddAttribute(seq++, "rows", "3");
        b.AddAttribute(seq++, "style", "width:100%;box-sizing:border-box;border-radius:10px;border:1px solid #f7d6e6;"
            + "background:#fffafd;padding:8px 11px;font-size:12px;color:#3d2a33;font-family:inherit;line-height:1.6;resize:vertical;");
        b.AddAttribute(seq++, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(owner,
            e => setter(e.Value?.ToString() ?? "")));
        b.AddContent(seq++, value);
        b.CloseElement();
        b.CloseElement();
    }

    void LabeledSelect(RenderTreeBuilder b, ref int seq, string value, Action<string> setter,
        List<(string Value, string Label, bool Enabled)> options)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-field");
        b.OpenComponent<Select<string, string>>(seq++);
        b.AddAttribute(seq++, "Value", value);
        b.AddAttribute(seq++, "Style", "width:100%;");
        b.AddAttribute(seq++, "ValueChanged", EventCallback.Factory.Create<string>(owner, setter));
        b.AddAttribute(seq++, "ChildContent", (RenderFragment)(childBuilder =>
        {
            int c = 0;
            foreach ((string optValue, string optLabel, bool enabled) in options)
            {
                childBuilder.OpenComponent<SelectOption<string, string>>(c++);
                childBuilder.AddAttribute(c++, "Value", optValue);
                childBuilder.AddAttribute(c++, "Label", optLabel);
                childBuilder.AddAttribute(c++, "Disabled", !enabled);
                childBuilder.CloseComponent();
            }
        }));
        b.CloseComponent();
        b.CloseElement();
    }

    void ActionButton(RenderTreeBuilder b, ref int seq, string text, Action onClick, bool disabled = false)
    {
        b.OpenElement(seq++, "button");
        b.AddAttribute(seq++, "class", "gs-btn");
        b.AddAttribute(seq++, "type", "button");
        if (disabled)
            b.AddAttribute(seq++, "disabled", true);
        b.AddAttribute(seq++, "onclick", EventCallback.Factory.Create<MouseEventArgs>(owner, _ => onClick()));
        b.AddContent(seq++, text);
        b.CloseElement();
    }

    /// <summary>gs- 皮肤样式：设置页和工作台各输出一次，保证两页观感一致。</summary>
    public static void Styles(RenderTreeBuilder b, ref int seq)
    {
        b.OpenElement(seq++, "style");
        b.AddContent(seq++, """
            .gs-root{
                max-width:720px;padding:20px 24px 28px;
                background:linear-gradient(180deg,#fff0f7 0%,#fff 120px);
                border-radius:14px;border:1px solid #fce4f0;
                box-shadow:0 4px 24px rgba(235,47,150,0.08);
            }
            .gs-title{font-size:20px;font-weight:700;margin-bottom:8px;color:#262626;}
            .gs-hero{
                position:relative;overflow:hidden;
                padding:16px 18px;margin-bottom:14px;border-radius:12px;color:#fff;
                background:linear-gradient(135deg,#eb2f96 0%,#f759ab 42%,#ff85c0 74%,#ffadd2 100%);
                box-shadow:0 8px 28px rgba(235,47,150,0.22);
            }
            .gs-hero::before{
                content:"";position:absolute;top:0;left:-30%;width:30%;height:100%;
                background:linear-gradient(90deg,transparent,rgba(255,255,255,0.18),transparent);
                animation:gs-shine 4.8s linear infinite;
            }
            @keyframes gs-shine{
                0%{transform:translateX(0);}
                100%{transform:translateX(460%);}
            }
            .gs-hero-title{font-size:15px;font-weight:700;position:relative;z-index:1;}
            .gs-hero-sub{font-size:12px;opacity:0.92;margin-top:4px;line-height:1.6;position:relative;z-index:1;}
            .gs-hero-chain{display:flex;align-items:center;gap:6px;flex-wrap:wrap;margin-top:10px;position:relative;z-index:1;}
            .gs-node{padding:3px 10px;border-radius:999px;font-size:11px;font-weight:600;background:rgba(255,255,255,0.16);border:1px solid rgba(255,255,255,0.26);}
            .gs-arrow{font-size:11px;opacity:0.92;}
            .gs-panel{position:relative;margin-bottom:16px;padding:14px 16px 16px 18px;border:1px solid #f7e0ec;border-radius:10px;background:#fffafd;box-shadow:0 2px 8px rgba(235,47,150,0.05);}
            .gs-panel::before{content:"";position:absolute;left:0;top:0;bottom:0;width:4px;background:linear-gradient(180deg,#f759ab,#eb2f96);border-radius:10px 0 0 10px;}
            .gs-panel-head{display:flex;align-items:center;gap:8px;font-size:14px;font-weight:600;color:#434343;margin-bottom:12px;}
            .gs-panel-dot{width:8px;height:8px;border-radius:50%;background:#eb2f96;box-shadow:0 0 0 3px rgba(235,47,150,0.14);}
            .gs-subhead{margin:16px 0 6px;padding-left:8px;border-left:3px solid #ffadd2;font-size:12px;font-weight:700;letter-spacing:1px;color:#c41d7f;}
            .gs-label{font-weight:600;margin:10px 0 4px;font-size:13px;color:#434343;}
            .gs-hint2{font-size:11px;color:#8c8c8c;margin:0 0 10px 2px;line-height:1.5;}
            .gs-field{margin-bottom:2px;}
            .gs-root .ant-input,.gs-root .ant-input-number{width:100%;border-radius:10px !important;border:1px solid #f7d6e6 !important;background:linear-gradient(180deg,#fff 0%,#fffafd 100%) !important;box-shadow:0 1px 4px rgba(235,47,150,0.06),inset 0 1px 0 rgba(255,255,255,0.8) !important;}
            .gs-root .ant-input:hover,.gs-root .ant-input-number:hover{border-color:#ffadd2 !important;box-shadow:0 2px 8px rgba(235,47,150,0.1) !important;}
            .gs-root .ant-input:focus,.gs-root .ant-input-number-focused{border-color:#f759ab !important;box-shadow:0 0 0 3px rgba(247,89,171,0.18),0 2px 8px rgba(235,47,150,0.12) !important;}
            .gs-listen{margin-bottom:10px;padding:10px 12px;border:1px dashed #f7d6e6;border-radius:10px;background:linear-gradient(180deg,#fff 0%,#fff7fb 100%);}
            .gs-listen-bar{display:flex;align-items:center;gap:8px;flex-wrap:wrap;margin-bottom:8px;}
            .gs-listen-count{font-size:12px;font-weight:700;color:#c41d7f;}
            .gs-listen-warn{font-size:11px;color:#d46b08;background:#fff7e6;border:1px solid #ffd591;border-radius:999px;padding:1px 8px;}
            .gs-listen-row{display:flex;align-items:center;gap:8px;flex-wrap:wrap;padding:6px 0;border-top:1px dashed #fbe6f0;}
            .gs-listen-row:first-of-type{border-top:none;}
            .gs-listen-name-wrap{display:flex;flex-direction:column;max-width:200px;min-width:0;}
            .gs-listen-name{font-size:12px;font-weight:600;color:#3d2a33;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
            .gs-listen-id{font-size:10px;color:#ad8fa0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
            .gs-listen-tag{font-size:11px;border-radius:999px;padding:1px 8px;border:1px solid #ffadd2;color:#c41d7f;background:#fff0f7;}
            .gs-listen-tag.off{border-color:#e8e8e8;color:#8c8c8c;background:#fafafa;}
            .gs-listen-meta{font-size:11px;color:#8c8c8c;flex:1;min-width:130px;}
            .gs-btn{font-size:11px;padding:3px 10px;border-radius:999px;border:1px solid #ffadd2;background:linear-gradient(135deg,#fff0f7,#ffe3f1);color:#c41d7f;cursor:pointer;font-family:inherit;}
            .gs-btn:hover{border-color:#f759ab;box-shadow:0 2px 8px rgba(235,47,150,0.16);}
            .gs-btn:disabled{opacity:0.45;cursor:not-allowed;box-shadow:none;}
            .gs-listen-meta2{font-size:11px;color:#8c8c8c;margin:2px 0 6px;}
            .gs-buffer-label{font-size:11px;color:#8c8c8c;margin:0 0 4px;}
            .gs-buffer{max-height:140px;overflow-y:auto;background:#fff7fb;border:1px dashed #f7d6e6;border-radius:8px;padding:6px 8px;font-family:Consolas,monospace;font-size:11px;line-height:1.55;color:#5b4450;word-break:break-word;margin-bottom:8px;}
            .gs-buffer-item{padding:2px 0;border-bottom:1px dashed #fbe6f0;}
            .gs-buffer-item:last-child{border-bottom:none;}
            .gs-buffer-empty{color:#b7a3ae;text-align:center;padding:12px 0;}
            @media (prefers-color-scheme:dark){
                .gs-root{background:linear-gradient(180deg,#2a1622 0%,#16181d 140px);
                    border-color:#3a2532;color:#e7e9ec;box-shadow:0 4px 24px rgba(0,0,0,.35);}
                .gs-title{color:#e7e9ec;}
                .gs-panel{background:#1e2127;border-color:#33232c;box-shadow:none;}
                .gs-panel-head,.gs-label{color:#c9cdd4;}
                .gs-hint2{color:#9aa1ab;}
                .gs-listen{background:#1e2127;border-color:#33232c;}
                .gs-listen-name{color:#e7e9ec;}
                .gs-listen-id{color:#9aa1ab;}
                .gs-listen-row{border-top-color:#33232c;}
                .gs-listen-meta{color:#9aa1ab;}
                .gs-listen-tag{background:#2a1b24;border-color:#5e3348;color:#ffadd2;}
                .gs-listen-tag.off{background:#22262d;border-color:#33373e;color:#8b929c;}
                .gs-btn{background:#2a1b24;border-color:#5e3348;color:#ffadd2;}
                .gs-listen-meta2{color:#9aa1ab;}
                .gs-buffer-label{color:#9aa1ab;}
                .gs-buffer{background:#181b20;border-color:#33232c;color:#c9cdd4;}
                .gs-buffer-item{border-bottom-color:#2a2229;}
                .gs-buffer-empty{color:#6b727c;}
                .gs-root .ant-input,.gs-root .ant-input-number{
                    background:#22262d !important;border-color:#33373e !important;
                    color:#e7e9ec !important;box-shadow:none !important;}
                .gs-root .ant-input:hover,.gs-root .ant-input-number:hover{border-color:#5e3348 !important;}
            }
            """);
        b.CloseElement();
    }
}
