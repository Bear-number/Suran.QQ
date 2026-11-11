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

    // ============================================================
    // 协议端（OneBot v11）：与官方 OpenAPI 并行的第二条通道
    // ============================================================

    [DisplayName("协议端·启用")]
    [Description("开：启动 OneBot v11 通道（连反连地址、收群/私聊消息、注册 QChat 等工具并注入提示词）；关：整条协议端通道完全不启动，官方机器人通道照常")]
    public bool OneBotEnabled { get; set; } = false;

    [DisplayName("协议端·机器人 QQ 号")]
    [Description("协议端登录的机器人 QQ 号。用于识别有人 @ 自己、拦截发给自己；填 0 会让唤醒判断与自检退化")]
    public long BotId { get; set; }

    [DisplayName("协议端·主人 QQ 号")]
    [Description("主人的 QQ 号：私聊消息会直接注入会话，且被视为最高优先级；填 0 会影响功能使用与 AI 的理解能力")]
    public long OwnerId { get; set; }

    [DisplayName("协议端·反连地址")]
    [Description("OneBot v11 的 WebSocket 反连地址，例如 ws://127.0.0.1:3001")]
    public string Url { get; set; } = "ws://127.0.0.1:3001";

    [DisplayName("协议端·访问令牌")]
    [Description("OneBot WebSocket 的 Authorization Bearer Token；协议端没设 token 就留空")]
    public string Token { get; set; } = "";

    [DisplayName("协议端·自动重连间隔(秒)")]
    [Description("断线后每隔多少秒尝试重连；填 0 关闭自动重连，默认 60 秒")]
    public int AutoReconnectSeconds { get; set; } = 60;

    [DisplayName("协议端·总提示词")]
    [Description("注入给 AI 的协议端聊天规则，会随功能说明常驻提示词一起进入上下文")]
    public string AppendDocumentPrompt { get; set; } =
        "注意！如果回复QQ消息，必须保持极简的文本（0-20字）来保证自然感。同时群聊消息要选择性忽略，避免刷屏。此外注意分清语境，群聊环境人声嘈杂，不要回复与自己无关的内容，回复时请使用CQ码功能指定回复人。";

    [DisplayName("协议端·私聊附加提示词")]
    [Description("收到私聊消息时追加在消息末尾的提示词，用于约束回复长度与语气")]
    public string AppendPrivateChatPrompt { get; set; } = "(回复请保持1-20字)";

    [DisplayName("协议端·群聊附加提示词")]
    [Description("收到群聊消息时追加在消息末尾的提示词，用于约束回复长度与场合")]
    public string AppendGroupChatPrompt { get; set; } = "(回复请保持1-20字。注意分清群聊场合，不要随便插话，避免刷屏)";

    [DisplayName("协议端·忽略群")]
    [Description("完全屏蔽消息的群号，不会收到这些群的任何信息；多个用英文逗号分隔")]
    public string IgnoredGroup { get; set; } = "";

    [DisplayName("协议端·唤醒词")]
    [Description("原始群消息中出现即开启该群监听的词，多个用英文逗号分隔；@ 机器人也算唤醒")]
    public string WakingWords { get; set; } = "";

    [DisplayName("协议端·概率激活(0~100)")]
    [Description("收到未激活群的消息时自动开启监听的概率，按百分比填写：0=从不，100=每条都开")]
    public float ProactiveChatProbability { get; set; }

    [DisplayName("协议端·预缓存条数")]
    [Description("群聊未激活时也始终缓存的最近消息条数，激活后会一并发送，让 AI 感知聊天背景")]
    public int PerBufferSize { get; set; } = 5;

    [DisplayName("协议端·最大缓存条数")]
    [Description("群聊激活状态下最多暂存多少条消息，超出会立即推送；-1 表示不限制")]
    public int MaxBufferMessages { get; set; } = -1;

    [DisplayName("协议端·推送间隔(秒)")]
    [Description("群消息暂存多久后自动推送，默认 12 秒")]
    public float FlushInterval { get; set; } = 12f;

    [DisplayName("协议端·消息防抖")]
    [Description("开：每收到一条新消息就重置推送倒计时，等对方说完再一起推送")]
    public bool DebounceEnabled { get; set; }

    [DisplayName("协议端·唤醒立即推送")]
    [Description("开：遇到满足唤醒条件的群消息时，立即推送当前缓存，不等倒计时")]
    public bool PushOnWakeUp { get; set; }

    [DisplayName("协议端·回复后关闭监听")]
    [Description("开：AI 回复群消息后立即关闭该群的消息监听")]
    public bool CloseGroupAfterReply { get; set; }

    [DisplayName("协议端·自动关闭(分钟)")]
    [Description("群监听长时间没有新消息触发时自动关闭的分钟数，默认 4 分钟")]
    public float AutoCloseMinutes { get; set; } = 4f;

    [DisplayName("协议端·私聊防抖(秒)")]
    [Description("私聊消息连续收到时等待多少秒再一起推送，0 表示立即推送")]
    public float PrivateDebounceTime { get; set; }
}
