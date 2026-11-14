using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Suran.QQ;

/// <summary>
/// 收信侧的「感官」：语音转写与图片描述。
///
/// 刻意不引用 Alife.Function.Auditory / Alife.Function.Vision 程序集，也不写进 manifest 依赖：
/// 用接口全名反射，去角色容器里找已经构造好的实例。模块没装、没启用、程序集还没加载，
/// 都只是拿不到实例、静默跳过，插件本身照常加载。
/// </summary>
public partial class QQModule
{
    const string VisionInterfaceType = "Alife.Function.Vision.IVisionModel, Alife.Function.Vision";
    const string AudioProviderType = "Alife.Function.Auditory.IAudioRecognizerProvider, Alife.Function.Auditory";
    const string AudioRecognizerType = "Alife.Function.Auditory.IAudioRecognizer, Alife.Function.Auditory";
    const string AudioDecoderType = "Alife.Function.Auditory.AudioDecoder, Alife.Function.Auditory";
    const string SpeechModelType = "Alife.Function.Speech.ISpeechModel, Alife.Function.Speech";

    /// <summary>按接口全名从角色容器里找现成实例。类型解析不到、模块没启用都返回 null。</summary>
    async Task<object> ResolveSenseServiceAsync(string typeName)
    {
        Type type = Type.GetType(typeName, throwOnError: false);
        if (type == null)
        {
            return null;
        }
        try
        {
            return await ChatActivity.Container.RequireInstance(type);
        }
        catch (Exception ex)
        {
            DetailLog("感官服务不可用：" + typeName + "｜" + Message(ex));
            return null;
        }
    }

    /// <summary>用宿主的语音模型把文字合成音频文件；模型没启用就直接告诉调用方不可用。</summary>
    async Task<string> SynthesizeSpeechAsync(string text)
    {
        object model = await ResolveSenseServiceAsync(SpeechModelType);
        Type modelType = Type.GetType(SpeechModelType, throwOnError: false);
        MethodInfo generate = modelType?.GetMethod("GenerateSpeechFileAsync",
            [typeof(string), typeof(CancellationToken)]);
        if (model == null || generate == null)
        {
            throw new Exception("当前语音合成不可用：宿主没注入语音模型");
        }

        try
        {
            object result = generate.Invoke(model, [text, CancellationToken.None]);
            string file = result is Task<string> task ? await task : null;
            if (string.IsNullOrWhiteSpace(file))
            {
                throw new Exception("语音合成失败");
            }
            return file!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new Exception("语音合成失败：" + ex.InnerException.Message, ex.InnerException);
        }
    }

    /// <summary>下载附件到临时文件。不是 http 链接、下载失败、超过限制都返回 null。</summary>
    async Task<string> DownloadSenseFileAsync(string url, string extension, long maxBytes)
    {
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) == false)
        {
            return null;
        }

        string path = Path.Combine(Path.GetTempPath(), $"qqofficial_sense_{Guid.NewGuid():N}{extension}");
        try
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(30) };
            using HttpResponseMessage response = await client.GetAsync(url, DestroyCancellationToken);
            if (response.IsSuccessStatusCode == false)
            {
                DetailLog("感官附件下载失败：" + (int)response.StatusCode);
                return null;
            }
            if (response.Content.Headers.ContentLength is long declared && declared > maxBytes)
            {
                DetailLog("感官附件过大：" + declared);
                return null;
            }
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(DestroyCancellationToken);
            if (bytes.LongLength > maxBytes)
            {
                DetailLog("感官附件过大：" + bytes.LongLength);
                return null;
            }
            await File.WriteAllBytesAsync(path, bytes, DestroyCancellationToken);
            return path;
        }
        catch (Exception ex)
        {
            DetailLog("感官附件下载失败：" + Message(ex));
            TryDeleteSenseFile(path);
            return null;
        }
    }

    /// <summary>语音转写：下载后交给宿主听觉模型；silk 由 AudioDecoder 负责解码。</summary>
    async Task<string> TryHearVoiceAsync(string url)
    {
        if (Configuration.InboundSpeechRecognition == false)
        {
            return null;
        }

        string path = await DownloadSenseFileAsync(url, ".silk", 20L * 1024 * 1024);
        if (path == null)
        {
            return null;
        }
        try
        {
            return await TranscribeAudioFileAsync(path);
        }
        finally
        {
            TryDeleteSenseFile(path);
        }
    }

    /// <summary>把一个本地音频文件交给宿主听觉模型转写。</summary>
    async Task<string> TranscribeAudioFileAsync(string path)
    {
        object provider = await ResolveSenseServiceAsync(AudioProviderType);
        Type providerType = Type.GetType(AudioProviderType, throwOnError: false);
        Type recognizerType = Type.GetType(AudioRecognizerType, throwOnError: false);
        Type decoderType = Type.GetType(AudioDecoderType, throwOnError: false);
        MethodInfo create = providerType?.GetMethod("CreateAudioRecognizer", Type.EmptyTypes);
        MethodInfo decode = decoderType?.GetMethod("DecodeFileTo16kMonoFloat", [typeof(string)]);
        MethodInfo accept = recognizerType?.GetMethod("AcceptWaveform", [typeof(float[]), typeof(int)]);
        MethodInfo flush = recognizerType?.GetMethod("Flush", Type.EmptyTypes);
        EventInfo recognized = recognizerType?.GetEvent("Recognized");
        if (provider == null || create == null || decode == null || accept == null || flush == null || recognized == null)
        {
            return null;
        }

        try
        {
            float[] samples = await Task.Run(
                () => (float[])decode.Invoke(null, [path])!, DestroyCancellationToken);
            if (samples.Length == 0)
            {
                return null;
            }

            object recognizer = create.Invoke(provider, null);
            if (recognizer == null)
            {
                return null;
            }

            List<string> heard = new();
            void OnRecognized(string text)
            {
                if (text.Trim().Length > 0)
                {
                    heard.Add(text.Trim());
                }
            }

            Action<string> handler = OnRecognized;
            recognized.AddEventHandler(recognizer, handler);
            try
            {
                accept.Invoke(recognizer, [samples, samples.Length]);
                flush.Invoke(recognizer, null);
            }
            finally
            {
                recognized.RemoveEventHandler(recognizer, handler);
                (recognizer as IDisposable)?.Dispose();
            }

            string text = string.Join(" ", heard).Trim();
            return text.Length == 0 ? null : LimitSenseText(text);
        }
        catch (Exception ex)
        {
            DetailLog("语音本地识别失败：" + Message(ex));
            return null;
        }
    }

    /// <summary>图片描述：先下载到临时文件（视觉模型只认本地路径），再问宿主视觉模型。</summary>
    async Task<string> TryDescribeImageAsync(string url, string contentType)
    {
        if (Configuration.InboundImageCaption == false)
        {
            return null;
        }

        string extension = contentType.Contains("png", StringComparison.OrdinalIgnoreCase) ? ".png"
            : contentType.Contains("gif", StringComparison.OrdinalIgnoreCase) ? ".gif"
            : ".jpg";
        string path = await DownloadSenseFileAsync(url, extension, 20L * 1024 * 1024);
        if (path == null)
        {
            return null;
        }

        try
        {
            return await DescribeImageFileAsync(path);
        }
        finally
        {
            TryDeleteSenseFile(path);
        }
    }

    /// <summary>把一个本地图片文件交给宿主视觉模型描述。</summary>
    async Task<string> DescribeImageFileAsync(string path)
    {
        object model = await ResolveSenseServiceAsync(VisionInterfaceType);
        Type modelType = Type.GetType(VisionInterfaceType, throwOnError: false);
        MethodInfo query = modelType?.GetMethod("QueryAsync",
            [typeof(string), typeof(string), typeof(int), typeof(CancellationToken)]);
        if (model == null || query == null)
        {
            return null;
        }

        try
        {
            string prompt = Configuration.InboundImagePrompt.Trim();
            if (prompt.Length == 0)
            {
                prompt = "请精简描述这张图片的内容，重点说清人物、文字和场景，避免输出过多文本。";
            }

            string text = "";
            object result = query.Invoke(model, [path, prompt, 256, DestroyCancellationToken]);
            if (result is Task<string> task)
            {
                text = ((await task) ?? "").Trim();
            }
            // 官方实现把 API 报错也当普通字符串返回（以【开头），别让它进上下文
            if (text.Length == 0)
            {
                return null;
            }
            if (text.StartsWith("【", StringComparison.Ordinal))
            {
                DetailLog("图片视觉描述返回错误：" + text);
                return null;
            }
            return LimitSenseText(text);
        }
        catch (Exception ex)
        {
            DetailLog("图片视觉描述失败：" + Message(ex));
            return null;
        }
    }

    /// <summary>截断感官结果，避免一张图几千字把上下文撑爆。</summary>
    string LimitSenseText(string text)
    {
        int max = Configuration.InboundSenseMaxChars;
        if (max <= 0)
        {
            max = 300;
        }
        return text.Length > max ? text[..max] + "…" : text;
    }

    static string Message(Exception ex) => (ex.InnerException ?? ex).Message;

    static void TryDeleteSenseFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        // [静默] 文件不存在或不可读时跳过
        catch
        {
        }
    }
}
