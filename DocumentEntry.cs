using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Suran.QQ;

/// <summary>
/// 挂一个与插件同名的入口标签（&lt;QQModule/&gt;）：模型输出它时才把整份工具文档推给模型，
/// 上下文里已有这份文档就跳过。配合 DocumentMode.None 使用——框架的 Implicit 会在每次函数调用后
/// 自动补一份整档文档。
/// </summary>
static class DocumentEntry
{
    /// <summary>登记入口标签。poke 用来把文档推给模型（一般是 Interactor.Poke），随 cancellationToken 注销。</summary>
    public static void Attach(XmlFunctionCaller caller, XmlHandler source, Action<string> poke, CancellationToken cancellationToken)
    {
        string tag = XmlFunctionCaller.GetDocumentTag(source);
        XmlHandler entry = new(source.Name + "_Trigger")
        {
            Functions =
            {
                new XmlFunction
                {
                    Name = source.Name,
                    Description = "读取本插件完整工具文档",
                    Invoker = (_, _) =>
                    {
                        bool seen = caller.ChatBot.ChatHistory.Any(m => m.Role == AuthorRole.User && (m.Content?.Contains(tag) ?? false));
                        if (!seen)
                        {
                            poke(Text(source));
                        }
                        return Task.CompletedTask;
                    }
                }
            }
        };
        caller.HandlerTable.Register(entry);
        cancellationToken.Register(() => caller.HandlerTable.Unregister(entry));
    }

    /// <summary>整份文档，排版与框架的常驻文档一致（同一个 [工具文档(名字)] 头）。</summary>
    static string Text(XmlHandler handler)
    {
        StringBuilder text = new();
        text.AppendLine(XmlFunctionCaller.GetDocumentTag(handler));
        text.AppendLine(handler.Description);
        text.AppendLine("#### 提供函数");
        text.AppendLine(handler.FunctionDocument());
        if (!string.IsNullOrEmpty(handler.Explanation))
        {
            text.AppendLine("#### 详细说明");
            text.AppendLine("```");
            text.AppendLine(handler.Explanation);
            text.AppendLine("```");
        }
        return text.ToString().Trim();
    }
}
