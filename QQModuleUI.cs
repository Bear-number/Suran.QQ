using System;
using System.Linq;
using Alife.Framework;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Suran.QQ;

/// <summary>
/// QQ 官方机器人的设置页——和 BreezeTTS 一样「配置页只放简单设置」：
/// 这里只留凭据、连接状态和两个总开关；事件订阅、收信注入、听觉视觉、
/// 发送格式、按钮、群监听、语音条、调试等全部在「角色工作台 → QQ官方」里。
///
/// 字段控件由 <see cref="QQConfigFields"/> 提供，和工作台共用同一份代码。
/// 本页直接改 <c>Configuration</c> 本体，保存交给框架渲染的按钮。
/// </summary>
public partial class QQModuleUI : ModuleUIBase<QQModule, QQConfig>
{
    /// <summary>共享字段渲染器：本页只用它渲染 Simple</summary>
    QQConfigFields fields;

    protected override void OnInitialized()
    {
        fields = new QQConfigFields(this, () => StateHasChanged(), ResolveModule);
    }

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

        QQConfigFields.Styles(b, ref i);

        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "gs-title");
        b.AddContent(i++, "QQ 接入");
        b.CloseElement();

        BuildHero(b, ref i);

        QQConfigFields.Panel(b, ref i, "连接状态", () =>
        {
            QQConfigFields.GatewayStatus(b, ref i, ResolveModule());
        });

        fields.Simple(b, ref i, Configuration);

        QQConfigFields.Panel(b, ref i, "完整功能在「角色工作台 → QQ官方」", () =>
        {
            AddHint(b, ref i, "事件订阅、收信与注入、听觉与视觉、发送与格式、按钮、群监听规则、语音条、运行与调试，"
                + "以及每个群的实时监听状态，都在工作台那一页。");
            AddHint(b, ref i, "那里改的是草稿，底部点「保存设置」才生效，误改可以「撤销改动」。"
                + "本页只留最常用的凭据和两个总开关，改完用页面下方的保存按钮。");
        });

        b.CloseElement();
    }

    /// <summary>宿主没把 Module 传进来时（角色没起/刚重载），退一步从活动容器里找同一个模块实例。</summary>
    QQModule ResolveModule()
    {
        if (Module != null)
        {
            return Module;
        }
        return ChatActivity?.Container?.Instances.OfType<QQModule>().FirstOrDefault();
    }

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

    static void AddHint(RenderTreeBuilder b, ref int seq, string text)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "gs-hint2");
        b.AddContent(seq++, text);
        b.CloseElement();
    }
}
