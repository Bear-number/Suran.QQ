using System.ComponentModel;

namespace Alife.QQOfficial;

public class QQOfficialConfig
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
}
