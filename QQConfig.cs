using System.ComponentModel;

namespace Suran.QQ;

public class QQConfig
{
    [DisplayName("运行·功能文档常驻")]
    [Description("开：功能说明常驻系统提示词，模型随时看得到工具；关：隐式加载省 token，但模型要先读入口文档。QQ 收发是高频基础能力，默认开")]
    public bool DocumentAlwaysResident { get; set; } = true;

    [DisplayName("凭据·AppID")]
    [Description("QQ 开放平台机器人的 AppID（q.qq.com 后台「开发管理」里那串数字 ID）。只进本地配置，插件不会把它打进日志或回显")]
    public string AppId { get; set; } = "";

    [DisplayName("凭据·AppSecret")]
    [Description("与 AppID 配对的机器人密钥。")]
    public string AppSecret { get; set; } = "";

    [DisplayName("接口·API 根地址")]
    [Description("API 根地址，留空用默认值。")]
    public string ApiBaseUrl { get; set; } = "";

    [DisplayName("事件·群与私聊消息(1<<25)")]
    [Description("群与私聊消息事件位，关掉就收不到消息。")]
    public bool IntentGroupAndPrivate { get; set; } = true;

    [DisplayName("事件·群成员变化(1<<24)")]
    [Description("群成员变化与加群申请事件位，关掉可避免权限不足断连。")]
    public bool IntentGroupMember { get; set; } = false;

    [DisplayName("事件·按钮互动(1<<26)")]
    [Description("INTERACTION_CREATE：用户点了消息按钮或快捷菜单。要用按钮键盘就开它")]
    public bool IntentInteraction { get; set; } = false;

    [DisplayName("事件·消息审核(1<<27)")]
    [Description("MESSAGE_AUDIT_PASS / MESSAGE_AUDIT_REJECT，内容过审结果")]
    public bool IntentMessageAudit { get; set; } = false;

    [DisplayName("事件·频道与子频道变化(1<<0)")]
    [Description("QQ 频道事件位，只有用频道才需要，默认关。")]
    public bool IntentGuilds { get; set; } = false;

    [DisplayName("开关·注入会话")]
    [Description("开启后，QQ 侧收到的消息会以「像好友/群里发来的消息」的形式进入 Alife 会话，由主 Agent 决定要不要回、回什么。关掉就只留发送面，纯当外发通道用")]
    public bool InjectInboundMessages { get; set; } = true;

    [DisplayName("群消息·全量注入策略")]
    [Description("仅 @ 时注入（默认）：没 @ 机器人的群消息只记可回复的 msg_id、不进会话；全部注入：每条群消息都进会话（容易刷屏）；群监听：唤醒词/概率激活，消息先缓存、按间隔成批推送，回复后或长时间没动静自动关闭")]
    public string GroupMessageMode { get; set; } = "at";

    // ── 官方通道：群监听 / 缓存与防抖 / 自动关闭（对应 QChat 的三个功能）──

    [DisplayName("群监听·唤醒词")]
    [Description("原始群消息里出现这些词就激活该群的监听，逗号分隔；@ 机器人同样会激活。群监听模式下生效")]
    public string GroupWakeWords { get; set; } = "";

    [DisplayName("群监听·忽略群")]
    [Description("完全不理这些群的消息，逗号分隔（填 group_openid）。不注入、不缓存、不激活")]
    public string GroupIgnoredGroups { get; set; } = "";

    [DisplayName("群监听·概率激活")]
    [Description("未激活时，每条群消息有多大概率直接开始监听。0~100，默认 0（只靠 @ 和唤醒词）")]
    public int GroupProactiveChancePercent { get; set; } = 0;

    [DisplayName("群监听·缓存推送间隔(秒)")]
    [Description("监听中收到的群消息先攒着，隔这么多秒成批推给模型，默认 12 秒")]
    public int GroupFlushSeconds { get; set; } = 12;

    [DisplayName("群监听·防抖")]
    [Description("开：每收到一条新消息就把推送倒计时往后推，等聊天停下来再推；关：按固定间隔推")]
    public bool GroupDebounce { get; set; } = false;

    [DisplayName("群监听·最大缓存条数")]
    [Description("监听中的消息缓存超过这个数量就立刻推送，防刷屏；-1 = 不限，默认 -1")]
    public int GroupMaxBufferMessages { get; set; } = -1;

    [DisplayName("群监听·背景缓存条数")]
    [Description("未监听时也悄悄留最近几条消息，激活时一并带上，让模型知道刚才在聊什么，默认 5")]
    public int GroupPreBufferSize { get; set; } = 5;

    [DisplayName("群监听·唤醒立即推送")]
    [Description("开：一命中唤醒词就立刻把缓存推出去，不等间隔；关：等下一个推送点")]
    public bool GroupPushOnWakeUp { get; set; } = false;

    [DisplayName("群监听·回复后关闭")]
    [Description("开：模型往这个群发过消息后就退出监听，等下个唤醒词；关：一直听到自动关闭时间到")]
    public bool GroupCloseAfterReply { get; set; } = false;

    [DisplayName("群监听·自动关闭(分钟)")]
    [Description("监听中这么久没有再被唤醒/使用，就自动退出监听，默认 4 分钟；0 = 不自动关")]
    public int GroupAutoCloseMinutes { get; set; } = 4;

    [DisplayName("私聊·防抖(秒)")]
    [Description("大于 0 时，单个私聊的消息先缓存，停这么久再一起推；0 = 每条立即推，默认 0")]
    public int PrivateDebounceSeconds { get; set; } = 0;

    [DisplayName("官方·群聊附加提示词")]
    [Description("注入官方群消息时，在末尾追加这一句，提醒模型这是群聊、控制长度。留空不追加")]
    public string GroupChatPrompt { get; set; } = "(回复请保持1-20字。注意分清群聊场合，不要随便插话，避免刷屏)";

    [DisplayName("官方·私聊附加提示词")]
    [Description("注入官方私聊消息时，在末尾追加这一句，控制长度。留空不追加")]
    public string PrivateChatPrompt { get; set; } = "(回复请保持1-20字)";

    [DisplayName("开关·发送消息")]
    [Description("关闭后所有发送类工具不可用；收信与注入不受影响")]
    public bool EnableSend { get; set; } = true;

    [DisplayName("回传·主动消息")]
    [Description("是否在会话里主动发消息，默认关。")]
    public bool AllowProactiveMessage { get; set; } = false;

    [DisplayName("网络·HTTP 代理")]
    [Description("HTTP 代理地址，留空直连。")]
    public string ProxyUrl { get; set; } = "";

    [DisplayName("网络·单次请求超时(秒)")]
    [Description("REST 单次请求超时，默认 20")]
    public int RequestTimeoutSeconds { get; set; } = 20;

    [DisplayName("网络·心跳与重连")]
    [Description("断线重连的重试间隔，默认 3 秒。")]
    public int ReconnectSeconds { get; set; } = 3;

    [DisplayName("语音·silk 编码器")]
    [Description("语音条编码用的 python.exe 完整路径。")]
    public string SilkEncoderPath { get; set; } = "";

    [DisplayName("语音·编码器参数")]
    [Description("与上面配对，占位符 {in}、{out}。例：`convert {in} {out}`")]
    public string SilkEncoderArgs { get; set; } = "";

    [DisplayName("语音·放行非 silk 直传")]
    [Description("是否跳过转码直接上传音频文件，默认关。")]
    public bool AllowNonSilkVoice { get; set; } = false;

    [DisplayName("语音·最长秒数")]
    [Description("转码时截断过长的音频，默认 300 秒（5 分钟）")]
    public int VoiceMaxSeconds { get; set; } = 300;

    [DisplayName("语音·采样率")]
    [Description("转 silk 前 ffmpeg 的 pcm 采样率，默认 24000。手配编码器时这一项必须与编码器参数一致，自动方案会自动带上")]
    public int VoicePcmRate { get; set; } = 24000;

    [DisplayName("语音·响度归一化")]
    [Description("开：转码前跑 loudnorm（-16 LUFS），TTS 音量忽大忽小时更稳；关：保持原始音量，默认关")]
    public bool VoiceLoudnessNormalize { get; set; } = false;

    [DisplayName("提示·输入状态帧数上限")]
    [Description("每个会话一次对话里最多发几帧「正在输入」（msg_type=6 也占被动回复额度），默认 3；填 0=不发")]
    public int TypingMaxFrames { get; set; } = 3;

    [DisplayName("去重·事件保留分钟数")]
    [Description("同一 msg_id 官方可能重复推送，这段时间内见过的 id 直接吞掉，默认 30 分钟")]
    public int DedupTtlMinutes { get; set; } = 30;

    [DisplayName("数据目录")]
    [Description("插件数据目录，相对 Alife Storage。")]
    public string DataFolder { get; set; } = "QQOfficial";

    [DisplayName("调试·详细日志")]
    [Description("记录请求与事件的时间与错误码。**不会**记录 AppSecret 与 access_token")]
    public bool DetailLog { get; set; } = false;

    [DisplayName("按钮·回调按钮改指令")]
    [Description("开：把 action.type=1（回调）自动换成 type=2 + enter，平台推不了互动事件时按钮也能点一下就发")]
    public bool KeyboardCallbackToCommand { get; set; }

    [DisplayName("按钮·限次策略")]
    [Description("开：按按钮策略拦截超额/过期/重复点击。策略取按钮 JSON 里的 once / ttl / limit，缺省用下面两个默认值")]
    public bool ButtonLimitEnabled { get; set; }

    [DisplayName("按钮·追踪前缀")]
    [Description("开：给指令按钮的 data 加 #qb# 前缀，点回来后自动剥掉。防止用户正常发一句和按钮 data 一样的文字被当成点击，默认开")]
    public bool ButtonPolicyToken { get; set; } = true;

    [DisplayName("按钮·默认每人一次")]
    [Description("按钮没写 once / limit 时，默认每人只能点一次")]
    public bool ButtonDefaultOnce { get; set; }

    [DisplayName("按钮·默认有效期(秒)")]
    [Description("按钮没写 ttl 时的有效期，0 = 不限")]
    public int ButtonDefaultTtlSeconds { get; set; }

    [DisplayName("按钮·账本上限")]
    [Description("内存里最多记多少个按钮，超出先清最旧的，默认 500")]
    public int ButtonMaxEntries { get; set; } = 500;

    [DisplayName("按钮·拦截时提示模型")]
    [Description("开：点击被策略拦下时在会话里提示模型（不打扰用户），默认开")]
    public bool ButtonNotifyOnLimit { get; set; } = true;

    [DisplayName("按钮·拦截时提示用户")]
    [Description("开：点击被拦下时直接给对方回一句提示（占用一次被动回复额度），默认关")]
    public bool ButtonNotifyUserOnLimit { get; set; }

    [DisplayName("按钮·用户提示文案")]
    [Description("上面那项打开后发给用户的固定文案")]
    public string ButtonLimitNotifyText { get; set; } = "这个按钮已经用过啦";

    [DisplayName("收信·读取文件内容上限(字)")]
    [Description("收到的文本类附件（txt/log/json/md/csv 等）读进上下文的最大字数，默认 4000；0 = 不读")]
    public int InboundFileMaxChars { get; set; } = 4000;

    [DisplayName("收信·语音本地识别")]
    [Description("开：把收到的语音下载下来，交给本机听觉模型（SenseVoice 等）转成文字补进上下文。没装听觉模型时自动跳过，默认关")]
    public bool InboundSpeechRecognition { get; set; } = false;

    [DisplayName("收信·图片自动描述")]
    [Description("开：把收到的图片交给视觉模型描述后写进上下文。每张图都是一次视觉推理（在线模型还计费），默认关")]
    public bool InboundImageCaption { get; set; } = false;

    [DisplayName("收信·图片描述提示词")]
    [Description("发给视觉模型的问题，留空用默认的精简描述")]
    public string InboundImagePrompt { get; set; } = "";

    [DisplayName("收信·感官结果字数上限")]
    [Description("本地语音识别文本、图片描述写进上下文时各自最多保留多少字，默认 300")]
    public int InboundSenseMaxChars { get; set; } = 300;

    [DisplayName("输入状态·延时(秒)")]
    [Description("收到私聊后隔多久才显示「正在输入」，默认 2 秒；0 = 立刻")]
    public int TypingDelaySeconds { get; set; } = 2;

    [DisplayName("输入状态·防抖(秒)")]
    [Description("同一个人刚显示过状态后，这么多秒内不再重复显示，默认 3 秒；0 = 不防抖")]
    public int TypingDebounceSeconds { get; set; } = 3;

    [DisplayName("输入状态·主动也显示")]
    [Description("开：没有被动 msg_id 时也发一帧主动状态（占主动额度），默认开")]
    public bool TypingAllowProactive { get; set; } = true;

    [DisplayName("素材·配额提醒阈值")]
    [Description("markdown 素材图达到这个数量就在上传后提醒，平台上限 100，默认 90；0 = 不提醒")]
    public int MarkdownImageWarnAt { get; set; } = 90;

    [DisplayName("发送·@ 标记形态")]
    [Description("legacy = <@openid>（平台自己下发用的那种，默认）；new = <qqbot-at-user id=\"…\" />（官方文档推荐，但部分环境会当纯文本）")]
    public string AtMarkupStyle { get; set; } = "legacy";

    [DisplayName("发送·含 @ 强制 markdown")]
    [Description("开：正文里出现 @ 标记就自动按 markdown 发（纯文本里 @ 只会显示成字符），默认开")]
    public bool AtForceMarkdown { get; set; } = true;

    [DisplayName("发送·引用与 markdown 冲突")]
    [Description("markdown = 优先 markdown，自动丢掉引用（默认）；reference = 优先引用，非必须场景退回纯文本")]
    public string MediaConflictPrefer { get; set; } = "markdown";

    [DisplayName("发送·动图处理")]
    [Description("auto = 原图 → APNG → 静态 PNG → 文件（默认）；static = 跳过原图直接转档；file = 原图按文件发")]
    public string AnimatedImageMode { get; set; } = "auto";

    [DisplayName("收信·双副本宽限(秒)")]
    [Description("全量群消息与 @ 副本同时到达时，全量副本等这么多秒再处理，发现 @ 副本已到就丢弃，默认 0（不等待）")]
    public int DoubleCopyGraceSeconds { get; set; }

    [DisplayName("按钮·账本持久化")]
    [Description("开：按钮限次账本落盘，重启后「每人一次/限次」仍然生效，默认开")]
    public bool ButtonLedgerPersist { get; set; } = true;

    [DisplayName("收信·注入引用内容")]
    [Description("开：有人引用回复时，把被引用那条的作者与正文一起注入（截断 300 字），默认开")]
    public bool InjectQuotedContent { get; set; } = true;

    [DisplayName("工具·高风险群管理")]
    [Description("开：允许禁言/全员禁言/踢人/黑名单/加群审批/频道踢人/改频道权限这些高风险工具；默认关，调用会直接报错")]
    public bool EnableSensitiveTools { get; set; }
}
