# Suran.QQ

接 QQ 开放平台的官方机器人：收发消息、管理群与频道、发卡片、配菜单面板。

## 功能

- 发消息：文本、语音、按钮、ARK 卡片、图片 / 视频 / 文件，可流式分段并显示正在输入
- 撤回消息、应答按钮交互
- 群管理：群资料、成员列表与详情、禁言与全员禁言、踢人、加群审批策略与白名单、群黑名单
- 频道：我的频道、子频道创建与修改、成员与身份组、频道权限、API 权限申请
- 频道公告、置顶、日程、子频道麦克风、帖子增删改查
- 频道消息：发文本或 embed、表情回应、撤回
- 菜单与面板：查询设置菜单、创建面板与配置目标、生成分享链接
- 身份展示与绑定、找人、markdown 图片上传与删除
- 环境自检：检查配置与接口连通性

## 安装

先装依赖：`Alife.Function.Speech`、`Alife.Function.FunctionCaller`。

装法二选一：插件市场里搜 `Suran.QQ`；或把目录放到 `Storage/Plugins/Suran.QQ/`，重启角色生效。

## 配置

在设置页里改，改完重启角色生效。

| 设置项 | 键 | 说明 | 默认 |
| --- | --- | --- | --- |
| 运行·功能文档常驻 | `DocumentAlwaysResident` | 开：功能说明常驻系统提示词，模型随时看得到工具；关：隐式加载省 token，但模型要先读入口文档。QQ 收发是高频基础能力，默认开 | `true` |
| 凭据·AppID | `AppId` | QQ 开放平台机器人的 AppID（q.qq.com 后台「开发管理」里那串数字 ID）。只进本地配置，插件不会把它打进日志或回显 | `""` |
| 凭据·AppSecret | `AppSecret` | 与 AppID 配对的机器人密钥。 | `""` |
| 接口·API 根地址 | `ApiBaseUrl` | API 根地址，留空用默认值。 | `""` |
| 事件·群与私聊消息(1<<25) | `IntentGroupAndPrivate` | 群与私聊消息事件位，关掉就收不到消息。 | `true` |
| 事件·群成员变化(1<<24) | `IntentGroupMember` | 群成员变化与加群申请事件位，关掉可避免权限不足断连。 | `false` |
| 事件·按钮互动(1<<26) | `IntentInteraction` | INTERACTION_CREATE：用户点了消息按钮或快捷菜单。要用按钮键盘就开它 | `false` |
| 事件·消息审核(1<<27) | `IntentMessageAudit` | MESSAGE_AUDIT_PASS / MESSAGE_AUDIT_REJECT，内容过审结果 | `false` |
| 事件·频道与子频道变化(1<<0) | `IntentGuilds` | QQ 频道事件位，只有用频道才需要，默认关。 | `false` |
| 开关·注入会话 | `InjectInboundMessages` | 开启后，QQ 侧收到的消息会以「像好友/群里发来的消息」的形式进入 Alife 会话，由主 Agent 决定要不要回、回什么。关掉就只留发送面，纯当外发通道用 | `true` |
| 群消息·全量注入策略 | `GroupMessageMode` | 仅 @ 时注入（默认）：没 @ 机器人的群消息只记可回复的 msg_id、不进会话；全部注入：每条群消息都进会话（容易刷屏）；群监听：唤醒词/概率激活，消息先缓存、按间隔成批推送，回复后或长时间没动静自动关闭 | `"at"` |
| 群监听·唤醒词 | `GroupWakeWords` | 原始群消息里出现这些词就激活该群的监听，逗号分隔；@ 机器人同样会激活。群监听模式下生效 | `""` |
| 群监听·忽略群 | `GroupIgnoredGroups` | 完全不理这些群的消息，逗号分隔（填 group_openid）。不注入、不缓存、不激活 | `""` |
| 群监听·概率激活 | `GroupProactiveChancePercent` | 未激活时，每条群消息有多大概率直接开始监听。0~100，默认 0（只靠 @ 和唤醒词） | `0` |
| 群监听·缓存推送间隔(秒) | `GroupFlushSeconds` | 监听中收到的群消息先攒着，隔这么多秒成批推给模型，默认 12 秒 | `12` |
| 群监听·防抖 | `GroupDebounce` | 开：每收到一条新消息就把推送倒计时往后推，等聊天停下来再推；关：按固定间隔推 | `false` |
| 群监听·最大缓存条数 | `GroupMaxBufferMessages` | 监听中的消息缓存超过这个数量就立刻推送，防刷屏；-1 = 不限，默认 -1 | `-1` |
| 群监听·背景缓存条数 | `GroupPreBufferSize` | 未监听时也悄悄留最近几条消息，激活时一并带上，让模型知道刚才在聊什么，默认 5 | `5` |
| 群监听·唤醒立即推送 | `GroupPushOnWakeUp` | 开：一命中唤醒词就立刻把缓存推出去，不等间隔；关：等下一个推送点 | `false` |
| 群监听·回复后关闭 | `GroupCloseAfterReply` | 开：模型往这个群发过消息后就退出监听，等下个唤醒词；关：一直听到自动关闭时间到 | `false` |
| 群监听·自动关闭(分钟) | `GroupAutoCloseMinutes` | 监听中这么久没有再被唤醒/使用，就自动退出监听，默认 4 分钟；0 = 不自动关 | `4` |
| 私聊·防抖(秒) | `PrivateDebounceSeconds` | 大于 0 时，单个私聊的消息先缓存，停这么久再一起推；0 = 每条立即推，默认 0 | `0` |
| 官方·群聊附加提示词 | `GroupChatPrompt` | 注入官方群消息时，在末尾追加这一句，提醒模型这是群聊、控制长度。留空不追加 | `"(回复请保持1-20字。注意分清群聊场合，不要随便插话，避免刷屏)"` |
| 官方·私聊附加提示词 | `PrivateChatPrompt` | 注入官方私聊消息时，在末尾追加这一句，控制长度。留空不追加 | `"(回复请保持1-20字)"` |
| 开关·发送消息 | `EnableSend` | 关闭后所有发送类工具不可用；收信与注入不受影响 | `true` |
| 回传·主动消息 | `AllowProactiveMessage` | 是否在会话里主动发消息，默认关。 | `false` |
| 网络·HTTP 代理 | `ProxyUrl` | HTTP 代理地址，留空直连。 | `""` |
| 网络·单次请求超时(秒) | `RequestTimeoutSeconds` | REST 单次请求超时，默认 20 | `20` |
| 网络·心跳与重连 | `ReconnectSeconds` | 断线重连的重试间隔，默认 3 秒。 | `3` |
| 语音·silk 编码器 | `SilkEncoderPath` | 语音条编码用的 python.exe 完整路径。 | `""` |
| 语音·编码器参数 | `SilkEncoderArgs` | 与上面配对，占位符 {in}、{out}。例：`convert {in} {out}` | `""` |
| 语音·放行非 silk 直传 | `AllowNonSilkVoice` | 是否跳过转码直接上传音频文件，默认关。 | `false` |
| 语音·最长秒数 | `VoiceMaxSeconds` | 转码时截断过长的音频，默认 300 秒（5 分钟） | `300` |
| 语音·采样率 | `VoicePcmRate` | 转 silk 前 ffmpeg 的 pcm 采样率，默认 24000。手配编码器时这一项必须与编码器参数一致，自动方案会自动带上 | `24000` |
| 语音·响度归一化 | `VoiceLoudnessNormalize` | 开：转码前跑 loudnorm（-16 LUFS），TTS 音量忽大忽小时更稳；关：保持原始音量，默认关 | `false` |
| 提示·输入状态帧数上限 | `TypingMaxFrames` | 每个会话一次对话里最多发几帧「正在输入」（msg_type=6 也占被动回复额度），默认 3；填 0=不发 | `3` |
| 去重·事件保留分钟数 | `DedupTtlMinutes` | 同一 msg_id 官方可能重复推送，这段时间内见过的 id 直接吞掉，默认 30 分钟 | `30` |
| 数据目录 | `DataFolder` | 插件数据目录，相对 Alife Storage。 | `"QQOfficial"` |
| 调试·详细日志 | `DetailLog` | 记录请求与事件的时间与错误码。**不会**记录 AppSecret 与 access_token | `false` |
| 按钮·回调按钮改指令 | `KeyboardCallbackToCommand` | 开：把 action.type=1（回调）自动换成 type=2 + enter，平台推不了互动事件时按钮也能点一下就发 | `—` |
| 按钮·限次策略 | `ButtonLimitEnabled` | 开：按按钮策略拦截超额/过期/重复点击。策略取按钮 JSON 里的 once / ttl / limit，缺省用下面两个默认值 | `—` |
| 按钮·追踪前缀 | `ButtonPolicyToken` | 开：给指令按钮的 data 加 #qb# 前缀，点回来后自动剥掉。防止用户正常发一句和按钮 data 一样的文字被当成点击，默认开 | `true` |
| 按钮·默认每人一次 | `ButtonDefaultOnce` | 按钮没写 once / limit 时，默认每人只能点一次 | `—` |
| 按钮·默认有效期(秒) | `ButtonDefaultTtlSeconds` | 按钮没写 ttl 时的有效期，0 = 不限 | `—` |
| 按钮·账本上限 | `ButtonMaxEntries` | 内存里最多记多少个按钮，超出先清最旧的，默认 500 | `500` |
| 按钮·拦截时提示模型 | `ButtonNotifyOnLimit` | 开：点击被策略拦下时在会话里提示模型（不打扰用户），默认开 | `true` |
| 按钮·拦截时提示用户 | `ButtonNotifyUserOnLimit` | 开：点击被拦下时直接给对方回一句提示（占用一次被动回复额度），默认关 | `—` |
| 按钮·用户提示文案 | `ButtonLimitNotifyText` | 上面那项打开后发给用户的固定文案 | `"这个按钮已经用过啦"` |
| 收信·读取文件内容上限(字) | `InboundFileMaxChars` | 收到的文本类附件（txt/log/json/md/csv 等）读进上下文的最大字数，默认 4000；0 = 不读 | `4000` |
| 输入状态·延时(秒) | `TypingDelaySeconds` | 收到私聊后隔多久才显示「正在输入」，默认 2 秒；0 = 立刻 | `2` |
| 输入状态·防抖(秒) | `TypingDebounceSeconds` | 同一个人刚显示过状态后，这么多秒内不再重复显示，默认 3 秒；0 = 不防抖 | `3` |
| 输入状态·主动也显示 | `TypingAllowProactive` | 开：没有被动 msg_id 时也发一帧主动状态（占主动额度），默认开 | `true` |
| 素材·配额提醒阈值 | `MarkdownImageWarnAt` | markdown 素材图达到这个数量就在上传后提醒，平台上限 100，默认 90；0 = 不提醒 | `90` |
| 发送·@ 标记形态 | `AtMarkupStyle` | legacy = <@openid>（平台自己下发用的那种，默认）；new = <qqbot-at-user id="…" />（官方文档推荐，但部分环境会当纯文本） | `"legacy"` |
| 发送·含 @ 强制 markdown | `AtForceMarkdown` | 开：正文里出现 @ 标记就自动按 markdown 发（纯文本里 @ 只会显示成字符），默认开 | `true` |
| 发送·引用与 markdown 冲突 | `MediaConflictPrefer` | markdown = 优先 markdown，自动丢掉引用（默认）；reference = 优先引用，非必须场景退回纯文本 | `"markdown"` |
| 发送·动图处理 | `AnimatedImageMode` | auto = 原图 → APNG → 静态 PNG → 文件（默认）；static = 跳过原图直接转档；file = 原图按文件发 | `"auto"` |
| 收信·双副本宽限(秒) | `DoubleCopyGraceSeconds` | 全量群消息与 @ 副本同时到达时，全量副本等这么多秒再处理，发现 @ 副本已到就丢弃，默认 0（不等待） | `—` |
| 按钮·账本持久化 | `ButtonLedgerPersist` | 开：按钮限次账本落盘，重启后「每人一次/限次」仍然生效，默认开 | `true` |
| 收信·注入引用内容 | `InjectQuotedContent` | 开：有人引用回复时，把被引用那条的作者与正文一起注入（截断 300 字），默认开 | `true` |
| 工具·高风险群管理 | `EnableSensitiveTools` | 开：允许禁言/全员禁言/踢人/黑名单/加群审批/频道踢人/改频道权限这些高风险工具；默认关，调用会直接报错 | `—` |
