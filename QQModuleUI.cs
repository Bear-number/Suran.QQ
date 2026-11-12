using System;
using System.Collections.Generic;
using System.Linq;
using Alife.Framework;
using AntDesign;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Suran.QQ;

/// <summary>
/// QQ 官方机器人的设置页。
///
/// 版式对齐 EmotionTTS 那一页（gs- 皮肤）：粉色 hero + 左侧色条分区 + 统一控件，
/// 只是把手写的配置项按「凭据 / 事件 / 收发 / 网络 / 语音 / 运行」重新分了一次组——
/// 反射自动分组会把 22 项平铺成一张长表，凭据和事件位混在一起很难找。
/// </summary>
public partial class QQModuleUI : ModuleUIBase<QQModule, QQConfig>, IDisposable
{
    System.Timers.Timer? listenTimer;

    // 官方通道记住下拉框选中的群（group_openid）
    string? selectedOfficialGroup;

    protected override void OnInitialized()
    {
        // 群监听的倒计时/缓存条数要实时变，1 秒刷一次（和 QChat 那页一样）
        listenTimer = new System.Timers.Timer(1000);
        listenTimer.Elapsed += (_, _) =>
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
        listenTimer.Start();
    }

    public void Dispose()
    {
        if (listenTimer != null)
        {
            listenTimer.Stop();
            listenTimer.Dispose();
            listenTimer = null;
        }
    }

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

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        if (Configuration == null)
        {
            b.AddContent(0, "Configuration NULL");
            return;
        }

        int i = 0;
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "gs-root");

        InjectStyles(b, ref i);

        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "gs-title");
        b.AddContent(i++, "QQ 接入");
        b.CloseElement();

        BuildHero(b, ref i);

        SectionPanel(b, ref i, "凭据与连接", () =>
        {
            AddOfficialConnectionRow(b, ref i);

            AddInput(b, ref i, "AppID", Configuration.AppId, v => Configuration.AppId = v,
                "q.qq.com 后台「开发管理」里的数字 ID");
            AddHint(b, ref i, "只存在本地配置里，插件不会把它写进日志或回显到会话。");

            AddInput(b, ref i, "AppSecret", Configuration.AppSecret, v => Configuration.AppSecret = v,
                "与 AppID 配对的机器人密钥", password: true);
            AddHint(b, ref i, "等同机器人密码，别外传、别提交到仓库。");

            AddInput(b, ref i, "API 根地址", Configuration.ApiBaseUrl, v => Configuration.ApiBaseUrl = v,
                "留空 = https://api.bot.qq.com；沙盒可填 https://sandbox.api.sgroup.qq.com");
            AddHint(b, ref i, "一般留空。要连沙盒环境才改这里。");

            AddInput(b, ref i, "数据目录", Configuration.DataFolder, v => Configuration.DataFolder = v,
                "相对 Alife Storage，默认 QQOfficial");
            AddHint(b, ref i, "放已见消息 id 台账和各会话的被动回复剩余额度；不存 token 与密钥。");
        });

        SectionPanel(b, ref i, "事件订阅", () =>
        {
            AddSwitch(b, ref i, "群与私聊消息", Configuration.IntentGroupAndPrivate,
                v => Configuration.IntentGroupAndPrivate = v);
            AddHint(b, ref i, "C2C_MESSAGE_CREATE / GROUP_AT_MESSAGE_CREATE / GROUP_MESSAGE_CREATE 等。关掉就什么都收不到。");

            AddSwitch(b, ref i, "群成员变化与加群申请", Configuration.IntentGroupMember,
                v => Configuration.IntentGroupMember = v);
            AddHint(b, ref i, "官方标注是白名单能力；没开权限时连接会被平台以关闭码 4014 断掉，那时把这一项关掉。");

            AddSwitch(b, ref i, "按钮互动", Configuration.IntentInteraction,
                v => Configuration.IntentInteraction = v);
            AddHint(b, ref i, "INTERACTION_CREATE：用户点消息按钮或快捷菜单。要用键盘按钮才需要开。");

            AddSwitch(b, ref i, "消息审核结果", Configuration.IntentMessageAudit,
                v => Configuration.IntentMessageAudit = v);
            AddHint(b, ref i, "MESSAGE_AUDIT_PASS / MESSAGE_AUDIT_REJECT。");

            AddSwitch(b, ref i, "频道与子频道变化", Configuration.IntentGuilds,
                v => Configuration.IntentGuilds = v);
            AddHint(b, ref i, "GUILDS 位。只有玩 QQ 频道才需要，默认关。");
        });

        SectionPanel(b, ref i, "收发开关", () =>
        {
            AddSwitch(b, ref i, "注入会话", Configuration.InjectInboundMessages,
                v => Configuration.InjectInboundMessages = v);
            AddHint(b, ref i, "开：QQ 收到的消息以「好友/群里发来的」形式进 Alife 会话，由主 Agent 决定怎么回。"
                + "关：只保留发送面，纯当外发通道。");

            AddLabeledSelect(b, ref i, Configuration.GroupMessageMode,
                v => Configuration.GroupMessageMode = v, GroupModeOptions);
            AddHint(b, ref i, "手机 QQ 里开了「获取群内全部消息」后：选「仅 @」时，没 @ 机器人的闲聊只记可回复的 msg_id、"
                + "不进上下文（之后仍能被动回复）；选「全部」则每条群消息都进会话，容易刷屏。");

            AddTextArea(b, ref i, "群聊附加提示词", Configuration.GroupChatPrompt,
                v => Configuration.GroupChatPrompt = v, "注入群消息时末尾追加的约束句，留空不追加");
            AddHint(b, ref i, "每条（或每批）群消息末尾都会带上这句，用来提醒模型这是群聊、控制长度、别乱插话。");

            AddTextArea(b, ref i, "私聊附加提示词", Configuration.PrivateChatPrompt,
                v => Configuration.PrivateChatPrompt = v, "注入私聊消息时末尾追加的约束句，留空不追加");
            AddHint(b, ref i, "私聊消息末尾追加的约束句，同样留空就不加。");

            AddNumberInput(b, ref i, "读文件内容上限（字）", Configuration.InboundFileMaxChars,
                v => Configuration.InboundFileMaxChars = v, 0, 20000);
            AddHint(b, ref i, "收到的文本类附件（txt/log/json/md/csv 等）读进上下文的最大字数，默认 4000；0 = 只给链接不读内容。");

            AddLabeledSelect(b, ref i, Configuration.AtMarkupStyle,
                v => Configuration.AtMarkupStyle = v, AtStyleOptions);
            AddHint(b, ref i, "发送 @ 时用的标记形态。部分环境会把 new 形态当纯文本，默认 legacy 更稳。");

            AddSwitch(b, ref i, "含 @ 强制 markdown", Configuration.AtForceMarkdown,
                v => Configuration.AtForceMarkdown = v);
            AddHint(b, ref i, "开：正文里出现 @ 标记就自动按 markdown 发；关：可能显示成原始标记。");

            AddLabeledSelect(b, ref i, Configuration.MediaConflictPrefer,
                v => Configuration.MediaConflictPrefer = v, ConflictOptions);
            AddHint(b, ref i, "markdown 与引用只能留一个：默认留 markdown 丢引用；选 reference 时，非必须场景退回纯文本发引用。");

            AddSwitch(b, ref i, "允许发送消息", Configuration.EnableSend,
                v => Configuration.EnableSend = v);
            AddHint(b, ref i, "关掉后所有发送类工具不可用，收信与注入不受影响。");

            AddSwitch(b, ref i, "允许主动消息", Configuration.AllowProactiveMessage,
                v => Configuration.AllowProactiveMessage = v);
            AddHint(b, ref i, "被动回复有平台窗口（群 5 分钟 5 次、单聊 60 分钟 4 次）；窗口外的主动消息要走这条，且受配额限制。"
                + "默认关：不在会话里主动插话。");

            AddSwitch(b, ref i, "功能文档常驻", Configuration.DocumentAlwaysResident,
                v => Configuration.DocumentAlwaysResident = v);
            AddHint(b, ref i, "开：工具说明常驻系统提示词；关：隐式加载省 token，但模型得先读入口文档。"
                + "QQ 收发是高频基础能力，默认开。改完要重载插件。");
        });

        SectionPanel(b, ref i, "按钮", () =>
        {
            AddSwitch(b, ref i, "回调按钮改指令", Configuration.KeyboardCallbackToCommand,
                v => Configuration.KeyboardCallbackToCommand = v);
            AddHint(b, ref i, "开：把 action.type=1（回调）自动换成 type=2 + enter，平台推不了互动事件时按钮也能点一下就发。");

            AddSwitch(b, ref i, "按钮限次策略", Configuration.ButtonLimitEnabled,
                v => Configuration.ButtonLimitEnabled = v);
            AddHint(b, ref i, "开：按按钮 JSON 里的 once / ttl / limit 拦截超额、过期、重复点击；没写的用下面两个默认值。");

            AddSwitch(b, ref i, "追踪前缀", Configuration.ButtonPolicyToken,
                v => Configuration.ButtonPolicyToken = v);
            AddHint(b, ref i, "开：给指令按钮的 data 自动加 #qb# 前缀，点回来后剥掉。防止用户正常发一句和 data 一样的文字被误判成点击。");

            AddSwitch(b, ref i, "默认每人一次", Configuration.ButtonDefaultOnce,
                v => Configuration.ButtonDefaultOnce = v);
            AddHint(b, ref i, "按钮没写 once / limit 时，默认每人只能点一次。");

            AddNumberInput(b, ref i, "默认有效期（秒）", Configuration.ButtonDefaultTtlSeconds,
                v => Configuration.ButtonDefaultTtlSeconds = v, 0, 86400);
            AddHint(b, ref i, "按钮没写 ttl 时的有效期，0 = 不限。");

            AddNumberInput(b, ref i, "账本上限", Configuration.ButtonMaxEntries,
                v => Configuration.ButtonMaxEntries = v, 50, 5000);
            AddHint(b, ref i, "内存里最多记多少个按钮，超出先清过期的再清最旧的。重启后账本清空。");

            AddSwitch(b, ref i, "拦截时提示模型", Configuration.ButtonNotifyOnLimit,
                v => Configuration.ButtonNotifyOnLimit = v);
            AddHint(b, ref i, "开：点击被拦下时在会话里提示模型（不打扰用户）。");

            AddSwitch(b, ref i, "拦截时提示用户", Configuration.ButtonNotifyUserOnLimit,
                v => Configuration.ButtonNotifyUserOnLimit = v);
            AddHint(b, ref i, "开：点击被拦下时直接给对方回一句固定文案（占一次被动回复额度）。");

            AddInput(b, ref i, "用户提示文案", Configuration.ButtonLimitNotifyText,
                v => Configuration.ButtonLimitNotifyText = v, "这个按钮已经用过啦");

            AddSwitch(b, ref i, "账本持久化", Configuration.ButtonLedgerPersist,
                v => Configuration.ButtonLedgerPersist = v);
            AddHint(b, ref i, "开：限次账本落盘，重启后「每人一次/限次」仍然成立，默认开。");
        });

        SectionPanel(b, ref i, "群监听（官方通道）", () =>
        {
            AddListenStatus(b, ref i);

            AddHint(b, ref i, "「群消息·全量注入策略」选「群监听」时生效：唤醒词 / @ / 概率任一命中就开始听这个群，"
                + "消息先缓存、按间隔成批推送；回复后或长时间没动静自动关闭。");

            AddInput(b, ref i, "唤醒词（逗号分隔）", Configuration.GroupWakeWords,
                v => Configuration.GroupWakeWords = v, "例如：机器人,在吗,小苏");
            AddHint(b, ref i, "原始群消息里出现这些词就激活监听；@ 机器人同样会激活。留空 = 只靠 @ 和概率。");

            AddInput(b, ref i, "忽略群（逗号分隔）", Configuration.GroupIgnoredGroups,
                v => Configuration.GroupIgnoredGroups = v, "填 group_openid，多个用逗号分隔");
            AddHint(b, ref i, "这些群的消息完全不处理：不注入、不缓存、不激活。");

            AddNumberInput(b, ref i, "概率激活（%）", Configuration.GroupProactiveChancePercent,
                v => Configuration.GroupProactiveChancePercent = v, 0, 100);
            AddHint(b, ref i, "没被唤醒时，每条群消息有多大比例直接开始监听。0 = 只靠 @ 和唤醒词。");

            AddNumberInput(b, ref i, "缓存推送间隔（秒）", Configuration.GroupFlushSeconds,
                v => Configuration.GroupFlushSeconds = v, 1, 120);
            AddHint(b, ref i, "监听中把消息攒这么久再成批推给模型，默认 12 秒。");

            AddSwitch(b, ref i, "防抖", Configuration.GroupDebounce,
                v => Configuration.GroupDebounce = v);
            AddHint(b, ref i, "开：每来一条新消息就重新计时，等聊天停下来再推；关：从激活开始按固定间隔推。");

            AddNumberInput(b, ref i, "最大缓存条数", Configuration.GroupMaxBufferMessages,
                v => Configuration.GroupMaxBufferMessages = v, -1, 1000);
            AddHint(b, ref i, "监听中缓存超过这个数就立刻推送，防刷屏；-1 = 不限。");

            AddNumberInput(b, ref i, "背景缓存条数", Configuration.GroupPreBufferSize,
                v => Configuration.GroupPreBufferSize = v, 1, 50);
            AddHint(b, ref i, "没监听时也悄悄留最近几条消息，激活时一起带上，让模型知道刚才在聊什么。");

            AddSwitch(b, ref i, "唤醒立即推送", Configuration.GroupPushOnWakeUp,
                v => Configuration.GroupPushOnWakeUp = v);
            AddHint(b, ref i, "开：一命中唤醒词就立刻推缓存，不等间隔；关：等下一个推送点。");

            AddSwitch(b, ref i, "回复后关闭监听", Configuration.GroupCloseAfterReply,
                v => Configuration.GroupCloseAfterReply = v);
            AddHint(b, ref i, "开：往这个群发过消息后就退出监听，等下个唤醒词；关：一直听到自动关闭时间到。");

            AddNumberInput(b, ref i, "自动关闭（分钟）", Configuration.GroupAutoCloseMinutes,
                v => Configuration.GroupAutoCloseMinutes = v, 0, 240);
            AddHint(b, ref i, "监听中这么久没再被唤醒/使用就自动退出，默认 4 分钟；0 = 不自动关。");

            AddNumberInput(b, ref i, "私聊防抖（秒）", Configuration.PrivateDebounceSeconds,
                v => Configuration.PrivateDebounceSeconds = v, 0, 300);
            AddHint(b, ref i, "大于 0 时，单个私聊的消息先缓存，停这么久再一起推；0 = 每条立即推。");

            AddSubGroup(b, ref i, "快速预设");
            AddHint(b, ref i, "两套常用组合，会把「全量注入策略」一并切到「群监听」；改完仍需点下方保存。");
            AddOfficialPresetRow(b, ref i);
        });

        SectionPanel(b, ref i, "网络与重连", () =>
        {
            AddInput(b, ref i, "HTTP 代理", Configuration.ProxyUrl, v => Configuration.ProxyUrl = v,
                "如 http://127.0.0.1:7890，留空直连");
            AddHint(b, ref i, "api.bot.qq.com 和 wss 网关连不上时，先看这一项。");

            AddNumberInput(b, ref i, "单次请求超时（秒）", Configuration.RequestTimeoutSeconds,
                v => Configuration.RequestTimeoutSeconds = v, 5, 120);
            AddHint(b, ref i, "REST 单次请求的超时，默认 20。");

            AddNumberInput(b, ref i, "断线重连间隔（秒）", Configuration.ReconnectSeconds,
                v => Configuration.ReconnectSeconds = v, 1, 60);
            AddHint(b, ref i, "心跳间隔由网关下发，这一项只管连不上时的重试节奏，默认 3 秒。");
        });

        SectionPanel(b, ref i, "语音条", () =>
        {
            AddVoiceCheckRow(b, ref i);

            AddInput(b, ref i, "silk 编码器（python.exe）", Configuration.SilkEncoderPath, v => Configuration.SilkEncoderPath = v,
                @"留空 = 自动用运行时 python");
            AddHint(b, ref i, "官方语音条要 silk，而 ffmpeg 只能解码不能编码，所以需要一个装了 pilk 的 python。"
                + "留空时自动用宿主运行时里的 python + 插件自带 tools/to_silk.py。");

            AddInput(b, ref i, "编码器参数", Configuration.SilkEncoderArgs, v => Configuration.SilkEncoderArgs = v,
                @"占位符 {in}、{out}，如 ""D:\...\to_silk.py"" {in} {out}");
            AddHint(b, ref i, "插件会把 {in} 换成 pcm、{out} 换成 silk，再原样传给上面那个解释器。留空则用自动参数。");

            AddNumberInput(b, ref i, "采样率（Hz）", Configuration.VoicePcmRate,
                v => Configuration.VoicePcmRate = v, 8000, 48000);
            AddHint(b, ref i, "转 silk 前 ffmpeg 的 pcm 采样率，默认 24000。手配编码器时要和它自己的参数对齐，自动方案会自动带上。");

            AddSwitch(b, ref i, "响度归一化", Configuration.VoiceLoudnessNormalize,
                v => Configuration.VoiceLoudnessNormalize = v);
            AddHint(b, ref i, "开：转码前跑 loudnorm（-16 LUFS），TTS 音量忽大忽小时更稳；关：保持原始音量。");

            AddSwitch(b, ref i, "放行非 silk 直传", Configuration.AllowNonSilkVoice,
                v => Configuration.AllowNonSilkVoice = v);
            AddHint(b, ref i, "开：没有编码器时直接把 mp3/wav 当文件发出去。关：宁可报错也不降级。");

            AddNumberInput(b, ref i, "最长秒数", Configuration.VoiceMaxSeconds,
                v => Configuration.VoiceMaxSeconds = v, 10, 3600);
            AddHint(b, ref i, "转码前截断超过这个长度的音频，默认 300 秒。");
        });

        SectionPanel(b, ref i, "运行细节", () =>
        {
            AddNumberInput(b, ref i, "输入状态帧数上限", Configuration.TypingMaxFrames,
                v => Configuration.TypingMaxFrames = v, 0, 20);
            AddHint(b, ref i, "每个会话一次对话里最多发几帧「正在输入」（msg_type=6 也占被动回复额度），默认 3；0 = 不发。");

            AddNumberInput(b, ref i, "事件去重保留（分钟）", Configuration.DedupTtlMinutes,
                v => Configuration.DedupTtlMinutes = v, 1, 1440);
            AddHint(b, ref i, "同一 msg_id 官方可能重复推送，这段时间内见过的 id 直接吞掉，默认 30 分钟。");

            AddSwitch(b, ref i, "详细日志", Configuration.DetailLog,
                v => Configuration.DetailLog = v);
            AddHint(b, ref i, "记录请求与事件的时间、错误码。不会记录 AppSecret 与 access_token。");

            AddSwitch(b, ref i, "注入引用内容", Configuration.InjectQuotedContent,
                v => Configuration.InjectQuotedContent = v);
            AddHint(b, ref i, "开：有人引用回复时，把被引用那条的作者与正文（截断 300 字）一起注入。");

            AddSwitch(b, ref i, "高风险群管理工具", Configuration.EnableSensitiveTools,
                v => Configuration.EnableSensitiveTools = v);
            AddHint(b, ref i, "开：允许禁言/全员禁言/踢人/黑名单/加群审批/频道踢人/改频道权限；默认关，调用会直接报错。");

            AddSubGroup(b, ref i, "输入状态");
            AddNumberInput(b, ref i, "延时（秒）", Configuration.TypingDelaySeconds,
                v => Configuration.TypingDelaySeconds = v, 0, 30);
            AddHint(b, ref i, "收到私聊后隔多久才显示「正在输入」，默认 2 秒；0 = 立刻。");

            AddNumberInput(b, ref i, "防抖（秒）", Configuration.TypingDebounceSeconds,
                v => Configuration.TypingDebounceSeconds = v, 0, 120);
            AddHint(b, ref i, "同一个人刚显示过状态后，这么久内不再重复显示，默认 3 秒；0 = 不防抖。");

            AddSwitch(b, ref i, "主动也显示", Configuration.TypingAllowProactive,
                v => Configuration.TypingAllowProactive = v);
            AddHint(b, ref i, "开：没有被动 msg_id 时也发一帧主动状态（占主动额度）。");

            AddNumberInput(b, ref i, "素材配额提醒阈值", Configuration.MarkdownImageWarnAt,
                v => Configuration.MarkdownImageWarnAt = v, 0, 100);
            AddHint(b, ref i, "markdown 素材达到这个数量就在上传后提醒，平台上限 100，默认 90；0 = 不提醒。");

            AddLabeledSelect(b, ref i, Configuration.AnimatedImageMode,
                v => Configuration.AnimatedImageMode = v, AnimatedOptions);
            AddHint(b, ref i, "GIF/WebP 的发送档位：auto 逐级降级；static 跳过原图；file 原图按文件发（保动画）。");

            AddNumberInput(b, ref i, "双副本宽限（秒）", Configuration.DoubleCopyGraceSeconds,
                v => Configuration.DoubleCopyGraceSeconds = v, 0, 5);
            AddHint(b, ref i, "全量群消息与 @ 副本同时到达时，全量副本等这么久再处理，发现 @ 副本已到就丢弃，默认 0。");
        });

        b.CloseElement();
    }

    // ============================================================
    // 顶部 hero
    // ============================================================

    static void BuildHero(RenderTreeBuilder b, ref int i)
    {
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "gs-hero");
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "gs-hero-title");
        b.AddContent(i++, "官方 OpenAPI");
        b.CloseElement();
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "gs-hero-sub");
        b.AddContent(i++, "自己取 access_token 并续期、连 WebSocket 网关收事件，把 QQ 侧消息按会话形状注入 Alife，"
            + "回复由模型调用发送工具发出：文本、markdown、富媒体、按钮回调、群管理与频道管理都在这里。");
        b.CloseElement();
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "gs-hero-chain");
        HeroNode(b, ref i, "填凭据");
        HeroArrow(b, ref i);
        HeroNode(b, ref i, "连网关");
        HeroArrow(b, ref i);
        HeroNode(b, ref i, "收发消息");
        b.CloseElement();
        b.CloseElement();
    }

    static void HeroNode(RenderTreeBuilder b, ref int i, string text)
    {
        b.OpenElement(i++, "span");
        b.AddAttribute(i++, "class", "gs-node");
        b.AddContent(i++, text);
        b.CloseElement();
    }

    static void HeroArrow(RenderTreeBuilder b, ref int i)
    {
        b.OpenElement(i++, "span");
        b.AddAttribute(i++, "class", "gs-arrow");
        b.AddContent(i++, "→");
        b.CloseElement();
    }

    // ============================================================
    // 控件（与语音页同一套）
    // ============================================================

    void SectionPanel(RenderTreeBuilder b, ref int seq, string title, Action render)
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

    /// <summary>设置页那一行监听状态（官方通道与协议端共用同一套渲染）。</summary>
    sealed record ListenRowView(string Id, string Name, bool Listening,
        IReadOnlyList<string> Buffer, IReadOnlyList<string> PreBuffer,
        double NextFlushSeconds, double AutoCloseSeconds, DateTime LastActiveAt,
        Action<bool> Toggle, Action Flush);

    bool voiceChecking;
    string voiceCheckResult = "";

    /// <summary>语音链路自检：ffmpeg / 编码器 / pilk 一次跑完，结果显示在按钮下面。</summary>
    void AddVoiceCheckRow(RenderTreeBuilder b, ref int seq)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-listen-bar");
        AddActionButton(b, ref seq, voiceChecking ? "检测中…" : "检测语音链路", CheckVoiceChain, voiceChecking);
        b.CloseElement();
        if (voiceCheckResult.Length > 0)
        {
            b.OpenElement(seq++, "pre");
            b.AddAttribute(seq++, "class", "gs-buffer");
            b.AddAttribute(seq++, "style", "white-space:pre-wrap;max-height:180px;margin:8px 0;");
            b.AddContent(seq++, voiceCheckResult);
            b.CloseElement();
        }
    }

    async void CheckVoiceChain()
    {
        QQModule? module = ResolveModule();
        if (module == null)
        {
            voiceCheckResult = "模块实例还没起来";
            StateHasChanged();
            return;
        }
        voiceChecking = true;
        voiceCheckResult = "";
        StateHasChanged();
        try
        {
            voiceCheckResult = await module.VoiceChainCheckAsync();
        }
        catch (Exception ex)
        {
            voiceCheckResult = "检测失败：" + ex.Message;
        }
        finally
        {
            voiceChecking = false;
            StateHasChanged();
        }
    }

    /// <summary>宿主没把 Module 传进来时（角色没起/刚重载），退一步从活动容器里找同一个模块实例。</summary>
    QQModule? ResolveModule()
    {
        if (Module != null)
        {
            return Module;
        }
        return ChatActivity?.Container?.Instances.OfType<QQModule>().FirstOrDefault();
    }

    /// <summary>
    /// 官方通道的群监听运行时状态：几个群在听、每个群缓存多少、还有多久推送 / 自动关闭，
    /// 每个群带「开启 / 关闭」「立即推送」。数据来自 QQModule 里的 officialGroups + 通讯录里的群名。
    /// </summary>
    void AddListenStatus(RenderTreeBuilder b, ref int seq)
    {
        QQModule? module = ResolveModule();
        if (module == null)
        {
            AddModuleOffline(b, ref seq);
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
            module.OfficialListenEnabled, selectedOfficialGroup, v => selectedOfficialGroup = v);
    }

    void AddListenBlock(RenderTreeBuilder b, ref int seq, int listeningCount, List<ListenRowView> rows,
        string warn, string emptyHint, bool interactive, string? selectedId, Action<string?> select)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-listen");

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-listen-bar");
        b.OpenElement(seq++, "span");
        b.AddAttribute(seq++, "class", "gs-listen-count");
        b.AddContent(seq++, listeningCount + " 个监听中 / 共 " + rows.Count + " 个群");
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
            b.CloseElement();
            return;
        }

        // 群多了要能选：下拉框里一个群一行，下面只显示选中那个群的详情
        ListenRowView current = rows.FirstOrDefault(row => row.Id == selectedId) ?? rows[0];
        if (current.Id != selectedId)
        {
            select(current.Id);
        }

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-field");
        b.OpenComponent<Select<string, string>>(seq++);
        b.AddAttribute(seq++, "Value", current.Id);
        b.AddAttribute(seq++, "ShowSearch", true);
        b.AddAttribute(seq++, "Placeholder", "选择群");
        b.AddAttribute(seq++, "Style", "width:100%;");
        b.AddAttribute(seq++, "ValueChanged", EventCallback.Factory.Create<string>(this, v => select(v)));
        b.AddAttribute(seq++, "ChildContent", (RenderFragment)(childBuilder =>
        {
            int c = 0;
            foreach (ListenRowView row in rows)
            {
                childBuilder.OpenComponent<SelectOption<string, string>>(c++);
                childBuilder.AddAttribute(c++, "Value", row.Id);
                childBuilder.AddAttribute(c++, "Label", ListenOptionLabel(row));
                childBuilder.CloseComponent();
            }
        }));
        b.CloseComponent();
        b.CloseElement();

        AddListenRow(b, ref seq, current, interactive);

        b.CloseElement();
    }

    static string ListenOptionLabel(ListenRowView row)
    {
        string mark = row.Listening ? "● " : "○ ";
        return row.Name.Length > 0 ? mark + row.Name + "（" + row.Id + "）" : mark + row.Id;
    }

    /// <summary>模块实例还没起来时的占位，避免整块状态区什么都不显示。</summary>
    void AddModuleOffline(RenderTreeBuilder b, ref int seq)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-listen");
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-hint2");
        b.AddContent(seq++, "模块实例还没起来（角色未加载或插件刚重载），群状态暂时读不到。");
        b.CloseElement();
        b.CloseElement();
    }

    void AddListenRow(RenderTreeBuilder b, ref int seq, ListenRowView row, bool interactive)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-listen-row");

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-listen-name-wrap");
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-listen-name");
        b.AddAttribute(seq++, "title", row.Name.Length > 0 ? row.Name : row.Id);
        b.AddContent(seq++, row.Name.Length > 0 ? row.Name : row.Id);
        b.CloseElement();
        if (row.Name.Length > 0)
        {
            // 群名和群号都要能看见：群名在上，群号/openid 在下
            b.OpenElement(seq++, "div");
            b.AddAttribute(seq++, "class", "gs-listen-id");
            b.AddAttribute(seq++, "title", row.Id);
            b.AddContent(seq++, row.Id);
            b.CloseElement();
        }
        b.CloseElement();

        b.OpenElement(seq++, "span");
        b.AddAttribute(seq++, "class", row.Listening ? "gs-listen-tag" : "gs-listen-tag off");
        b.AddContent(seq++, row.Listening ? "监听中" : "休眠");
        b.CloseElement();

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-listen-meta");
        b.AddContent(seq++, row.Listening
            ? "缓存 " + row.Buffer.Count + " 条" + (row.Buffer.Count > 0 ? " · 推送 " + Math.Ceiling(row.NextFlushSeconds) + "s" : "")
              + (row.AutoCloseSeconds > 0 ? " · 自动关 " + Math.Ceiling(row.AutoCloseSeconds) + "min" : "")
            : row.PreBuffer.Count > 0 ? "背景 " + row.PreBuffer.Count + " 条" : "无缓存");
        b.CloseElement();

        b.OpenElement(seq++, "button");
        b.AddAttribute(seq++, "class", "gs-btn");
        b.AddAttribute(seq++, "type", "button");
        if (interactive == false)
        {
            b.AddAttribute(seq++, "disabled", true);
        }
        b.AddAttribute(seq++, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this,
            _ => row.Toggle(!row.Listening)));
        b.AddContent(seq++, row.Listening ? "关闭" : "开启");
        b.CloseElement();

        b.OpenElement(seq++, "button");
        b.AddAttribute(seq++, "class", "gs-btn");
        b.AddAttribute(seq++, "type", "button");
        if (row.Buffer.Count + row.PreBuffer.Count == 0)
        {
            b.AddAttribute(seq++, "disabled", true);
        }
        b.AddAttribute(seq++, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, _ => row.Flush()));
        b.AddContent(seq++, "立即推送");
        b.CloseElement();

        b.CloseElement();

        // ---- 选中群的缓存预览（对齐 QChat 的详情卡）----
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-listen-meta2");
        b.AddContent(seq++, "最后活动："
            + (row.LastActiveAt == DateTime.MinValue ? "—" : row.LastActiveAt.ToString("HH:mm:ss")));
        b.CloseElement();

        if (row.PreBuffer.Count > 0)
        {
            AddBufferBox(b, ref seq, "激活前缓冲 (" + row.PreBuffer.Count + ")", row.PreBuffer, false);
        }
        AddBufferBox(b, ref seq, "消息缓冲 (" + row.Buffer.Count + ")", row.Buffer, true);
    }

    /// <summary>缓存消息预览框：超出高度滚动，空的时候给一句话。</summary>
    void AddBufferBox(RenderTreeBuilder b, ref int seq, string label, IReadOnlyList<string> messages, bool showEmpty)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-buffer-label");
        b.AddContent(seq++, label);
        b.CloseElement();

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-buffer");
        if (messages.Count == 0)
        {
            if (showEmpty)
            {
                b.OpenElement(seq++, "div");
                b.AddAttribute(seq++, "class", "gs-buffer-empty");
                b.AddContent(seq++, "暂无缓存消息");
                b.CloseElement();
            }
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

    /// <summary>官方网关状态（网关按「断线重连间隔」自己重连，这里只显示，不打断它）。</summary>
    void AddOfficialConnectionRow(RenderTreeBuilder b, ref int seq)
    {
        QQModule? module = ResolveModule();
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

    /// <summary>官方通道的快速预设。</summary>
    void AddOfficialPresetRow(RenderTreeBuilder b, ref int seq)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-listen-bar");
        AddActionButton(b, ref seq, "开关监听式", ApplyOfficialDesktopPetPreset);
        AddActionButton(b, ref seq, "随机触发式", ApplyOfficialGroupChatPreset);
        b.CloseElement();
        AddHint(b, ref seq, "开关监听式：只在唤醒词 / @ 命中时开始听，其余时间只留背景缓存，适合桌面宠物。");
        AddHint(b, ref seq, "随机触发式：小概率自动听 + 小缓存 + 回复后关闭，适合全天挂机的冒泡机器人。");
    }

    void ApplyOfficialDesktopPetPreset()
    {
        Configuration.GroupMessageMode = "listen";
        Configuration.GroupDebounce = false;
        Configuration.GroupFlushSeconds = 15;
        Configuration.GroupMaxBufferMessages = -1;
        Configuration.GroupPreBufferSize = 5;
        Configuration.GroupProactiveChancePercent = 0;
        Configuration.GroupCloseAfterReply = false;
        StateHasChanged();
    }

    void ApplyOfficialGroupChatPreset()
    {
        Configuration.GroupMessageMode = "listen";
        Configuration.GroupDebounce = true;
        Configuration.GroupFlushSeconds = 3;
        Configuration.GroupMaxBufferMessages = 5;
        Configuration.GroupPreBufferSize = 3;
        Configuration.GroupProactiveChancePercent = 5;
        Configuration.GroupCloseAfterReply = true;
        StateHasChanged();
    }

    void AddActionButton(RenderTreeBuilder b, ref int seq, string text, Action onClick, bool disabled = false)
    {
        b.OpenElement(seq++, "button");
        b.AddAttribute(seq++, "class", "gs-btn");
        b.AddAttribute(seq++, "type", "button");
        if (disabled)
        {
            b.AddAttribute(seq++, "disabled", true);
        }
        b.AddAttribute(seq++, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, _ => onClick()));
        b.AddContent(seq++, text);
        b.CloseElement();
    }

    void AddLabel(RenderTreeBuilder b, ref int seq, string text)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-label");
        b.AddContent(seq++, text);
        b.CloseElement();
    }

    void AddHint(RenderTreeBuilder b, ref int seq, string text)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-hint2");
        b.AddContent(seq++, text);
        b.CloseElement();
    }

    void AddInput(RenderTreeBuilder b, ref int seq, string label, string value, Action<string> setter,
        string placeholder = "请输入", bool password = false)
    {
        AddLabel(b, ref seq, label);
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-field");
        b.OpenComponent<Input<string>>(seq++);
        b.AddAttribute(seq++, "Value", value);
        b.AddAttribute(seq++, "Placeholder", placeholder);
        if (password)
        {
            // AntDesign 的 Type 是枚举不是字符串，传 "password" 会在运行时炸
            b.AddAttribute(seq++, "Type", InputType.Password);
        }
        b.AddAttribute(seq++, "ValueChanged", EventCallback.Factory.Create<string>(this, setter));
        b.CloseComponent();
        b.CloseElement();
    }

    void AddNumberInput(RenderTreeBuilder b, ref int seq, string label, int value, Action<int> setter, int min, int max)
    {
        AddLabel(b, ref seq, label);
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-field");
        b.OpenComponent<InputNumber<int>>(seq++);
        b.AddAttribute(seq++, "Value", value);
        b.AddAttribute(seq++, "Min", min);
        b.AddAttribute(seq++, "Max", max);
        b.AddAttribute(seq++, "Style", "width:100%;");
        b.AddAttribute(seq++, "ValueChanged", EventCallback.Factory.Create<int>(this, setter));
        b.CloseComponent();
        b.CloseElement();
    }

    void AddNumberInputF(RenderTreeBuilder b, ref int seq, string label, float value, Action<float> setter,
        double min, double max, double step)
    {
        AddLabel(b, ref seq, label);
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-field");
        b.OpenComponent<InputNumber<double>>(seq++);
        b.AddAttribute(seq++, "Value", (double)value);
        b.AddAttribute(seq++, "Min", min);
        b.AddAttribute(seq++, "Max", max);
        b.AddAttribute(seq++, "Step", step);
        b.AddAttribute(seq++, "Style", "width:100%;");
        b.AddAttribute(seq++, "ValueChanged", EventCallback.Factory.Create<double>(this, v => setter((float)v)));
        b.CloseComponent();
        b.CloseElement();
    }

    void AddSubGroup(RenderTreeBuilder b, ref int seq, string title)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-subhead");
        b.AddContent(seq++, title);
        b.CloseElement();
    }

    void AddSwitch(RenderTreeBuilder b, ref int seq, string label, bool value, Action<bool> setter)
    {
        AddLabel(b, ref seq, label);
        b.OpenComponent<Switch>(seq++);
        b.AddAttribute(seq++, "Checked", value);
        b.AddAttribute(seq++, "CheckedChildren", "开");
        b.AddAttribute(seq++, "UnCheckedChildren", "关");
        b.AddAttribute(seq++, "CheckedChanged", EventCallback.Factory.Create<bool>(this, setter));
        b.CloseComponent();
    }

    void AddTextArea(RenderTreeBuilder b, ref int seq, string label, string value, Action<string> setter,
        string placeholder = "")
    {
        AddLabel(b, ref seq, label);
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-field");
        b.OpenElement(seq++, "textarea");
        b.AddAttribute(seq++, "value", value);
        b.AddAttribute(seq++, "placeholder", placeholder);
        b.AddAttribute(seq++, "rows", "3");
        b.AddAttribute(seq++, "style", "width:100%;box-sizing:border-box;border-radius:10px;border:1px solid #f7d6e6;"
            + "background:#fffafd;padding:8px 11px;font-size:12px;color:#3d2a33;font-family:inherit;line-height:1.6;resize:vertical;");
        b.AddAttribute(seq++, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this,
            e => setter(e.Value?.ToString() ?? "")));
        b.AddContent(seq++, value);
        b.CloseElement();
        b.CloseElement();
    }

    void AddLabeledSelect(RenderTreeBuilder b, ref int seq, string value, Action<string> setter,
        List<(string Value, string Label, bool Enabled)> options)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-field");
        b.OpenComponent<Select<string, string>>(seq++);
        b.AddAttribute(seq++, "Value", value);
        b.AddAttribute(seq++, "Style", "width:100%;");
        b.AddAttribute(seq++, "ValueChanged", EventCallback.Factory.Create<string>(this, setter));
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

    // ============================================================
    // 样式（整段沿用语音页的粉色 gs- 皮肤）
    // ============================================================

    static void InjectStyles(RenderTreeBuilder b, ref int seq)
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
