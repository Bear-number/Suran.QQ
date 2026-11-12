using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Function.FunctionCaller;

namespace Suran.QQ;

/// <summary>
/// 富媒体与素材。官方两条上传路线在这里都实现了，并且**按大小自动选**：
/// 整文件路线（/files 直接给 url 或 base64）与分片路线（upload_prepare → 逐片 PUT 预签名 URL → upload_part_finish → /files 带 upload_id）。
/// 平台标的软限制是图 20MB / 视频 30MB / 语音 20MB / 文件 200MB，**超软限制会被降级成文件类型**，超 200MB 直接报错。
/// 另外单独一条「素材管理」（2026-10-21 新增）：图片传上去拿 qqbot-media:// 引用写进 markdown，单张 ≤10MB、每机器人 100 张、90 天滑动过期。
/// </summary>
public partial class QQModule
{
    const long chunkSoftLimitBytes = 6 * 1024 * 1024;
    const long hardLimitBytes = 200L * 1024 * 1024;
    const long md5HeadBytes = 10002432;
    const long materialImageMaxBytes = 10L * 1024 * 1024;
    /// <summary>pcm 采样率：必须与 tools/to_silk.py 里 pilk 的 pcm_rate 一致，否则语速变调</summary>
    const int pcmRate = 24000;
    const string voiceWorkPrefix = "qqofficial_voice_";
    const string voiceCacheFolder = "qqofficial_voice_cache";
    const string mediaPreparePath = "/upload_prepare";
    const string mediaPartFinishPath = "/upload_part_finish";
    const string materialBase = "/v2/media-images";
    static readonly string[] materialTypes = ["image/png", "image/jpeg", "image/gif", "image/webp"];

    [XmlFunction(FunctionMode.OneShot)]
    [Description("上传一张本地图片当 markdown 素材，返回可以直接写进 markdown 的 qqbot-media:// 引用与 image_id。"
        + "限制：png/jpeg/gif/webp、单张 ≤10MB、每个机器人最多 100 张（用 QQDeleteMarkdownImage 释放）、自最后一次被引用起 90 天内有效。"
        + "markdown 里写法：![说明 #宽px #高px](qqbot-media://image/v1/…)")]
    public async Task QQUploadMarkdownImage(string localPath)
    {
        string path = Need(localPath, "localPath");
        (string imageId, string url) = await UploadMaterialImageAsync(path);
        interactor.Poke("素材已上传：" + imageId
            + "\nmarkdown 引用：" + url
            + await MarkdownQuotaHintAsync());
    }

    /// <summary>上传一张本地图片到 markdown 素材库，返回 image_id 与 qqbot-media:// 引用。</summary>
    internal async Task<(string ImageId, string Url)> UploadMaterialImageAsync(string localPath)
    {
        string path = localPath.Trim();
        FileInfo file = new(path);
        if (file.Exists == false)
        {
            throw new Exception("找不到文件：" + path);
        }
        if (file.Length > materialImageMaxBytes)
        {
            throw new Exception("素材图片最大 " + (materialImageMaxBytes / 1024 / 1024).ToString(CultureInfo.InvariantCulture)
                + "MB，这个文件 " + (file.Length / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + "MB");
        }
        string contentType = GuessImageContentType(file.Extension);
        byte[] bytes = File.ReadAllBytes(path);
        JsonElement prepared = await RequireRest().PostAsync(materialBase + "/prepare", new JsonObject
        {
            ["file_name"] = file.Name,
            ["file_size"] = file.Length,
            ["content_type"] = contentType,
            ["md5"] = HashHex(MD5.Create(), bytes),
            ["sha1"] = HashHex(SHA1.Create(), bytes)
        }, DestroyCancellationToken);
        string uploadId = QQJson.Text(prepared, "upload_id");
        string presigned = QQJson.Text(prepared, "presigned_url");
        if (uploadId.Length == 0 || presigned.Length == 0)
        {
            throw new Exception("prepare 没返回 upload_id/presigned_url：" + QQRestClient.Shorten(prepared.ToString()));
        }
        string etag = await PutToPresignedAsync(presigned, bytes, contentType);
        JsonElement done = await RequireRest().PostAsync(materialBase + "/complete", new JsonObject
        {
            ["upload_id"] = uploadId,
            ["md5"] = etag
        }, DestroyCancellationToken);
        DetailLog("素材图已上传：" + QQJson.Text(done, "image_id")
            + "，有效期 " + (QQJson.Integer(done, "ttl") / 86400.0).ToString("0", CultureInfo.InvariantCulture) + " 天");
        return (QQJson.Text(done, "image_id"), QQJson.Text(done, "url"));
    }

    static readonly Regex markdownLocalImage = new(@"!\[([^\]]*)\]\(([^)\s]+)", RegexOptions.Compiled);

    /// <summary>markdown 里写本地图片路径时：自动上传成素材，并把路径换成 qqbot-media:// 引用。一条消息最多处理 10 张。</summary>
    async Task<string> PublishMarkdownImagesAsync(string markdown)
    {
        if (markdown.Contains("![", StringComparison.Ordinal) == false)
        {
            return markdown;
        }
        string result = markdown;
        int published = 0;
        foreach (Match match in markdownLocalImage.Matches(markdown))
        {
            string target = match.Groups[2].Value.Trim();
            if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("qqbot-media://", StringComparison.OrdinalIgnoreCase)
                || File.Exists(target) == false)
            {
                continue;
            }
            try
            {
                (_, string url) = await UploadMaterialImageAsync(target);
                result = result.Replace(match.Groups[2].Value, url, StringComparison.Ordinal);
                published++;
                if (published >= 10)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                DetailLog("markdown 内嵌图上传失败：" + target + "｜" + ex.Message);
            }
        }
        return result;
    }

    /// <summary>按扩展名猜媒体类型，让 QQSendMedia 可以只给文件路径。</summary>
    static string GuessMediaType(string pathOrUrl)
    {
        string clean = pathOrUrl ?? "";
        int mark = clean.IndexOfAny(['?', '#']);
        if (mark >= 0)
        {
            clean = clean[..mark];
        }
        return Path.GetExtension(clean).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" => "image",
            ".mp4" or ".mov" or ".m4v" or ".avi" or ".mkv" => "video",
            ".silk" or ".slk" or ".sil" or ".mp3" or ".wav" or ".ogg" or ".amr" => "voice",
            _ => "file"
        };
    }

    /// <summary>素材配额提醒：平台上限 100 张，接近阈值时在返回值里带一句，提醒模型去删旧的。</summary>
    async Task<string> MarkdownQuotaHintAsync()
    {
        int warnAt = Configuration.MarkdownImageWarnAt;
        if (warnAt <= 0)
        {
            return "";
        }
        try
        {
            JsonElement body = await RequireRest().GetAsync(materialBase + "?cursor=", DestroyCancellationToken);
            int total = (int)QQJson.Integer(body, "total");
            return total >= warnAt
                ? "\n【注意】素材已有 " + total.ToString(CultureInfo.InvariantCulture) + " 张（上限 100），建议先用 QQMarkdownImages 看一眼并用 QQDeleteMarkdownImage 清理旧的"
                : "";
        }
        catch
        {
            return "";
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("列当前机器人手里有效的素材图片（按上传时间倒序，游标翻页），返回 image_id、qqbot-media:// 引用、过期时间。"
        + "响应里的 preview_url 是短时效的临时预览链接，别存下来当永久地址用")]
    public async Task QQMarkdownImages(string cursor = "")
    {
        JsonElement body = await RequireRest().GetAsync(materialBase + "?cursor=" + Uri.EscapeDataString(cursor.Trim()), DestroyCancellationToken);
        List<JsonElement> images = QQJson.Array(body, "images").ToList();
        List<string> lines = new()
        {
            "素材图片 " + images.Count.ToString(CultureInfo.InvariantCulture) + " 条，平台计总数 "
            + QQJson.Integer(body, "total").ToString(CultureInfo.InvariantCulture) + " 张（上限 100 张）"
            + (QQJson.Text(body, "next_cursor").Length > 0 ? "，还有下一页" : "")
        };
        foreach (JsonElement image in images)
        {
            lines.Add("- " + QQJson.Text(image, "file_name", "（无名）") + " | image_id=" + QQJson.Text(image, "image_id")
                + " | 过期于 " + FromUnix(QQJson.Integer(image, "expires_at"))
                + "\n  " + QQJson.Text(image, "url"));
        }
        interactor.Poke(string.Join("\n", lines));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("删除一张素材图片、释放 100 张配额。删除后引用链接立即失效，但**已经发出去的消息不受影响**（平台在发送时已转存副本）。"
        + "删不存在的 image_id 平台回 40035008")]
    public async Task QQDeleteMarkdownImage(string imageId)
    {
        await RequireRest().DeleteAsync(materialBase + "/" + Uri.EscapeDataString(Need(imageId, "imageId")) + "/delete", DestroyCancellationToken);
        interactor.Poke("素材 " + imageId.Trim() + " 已删除，配额已释放");
    }

    /// <summary>整文件与分片两条路线的分界：小文件走 base64/url，大文件走分片，免得把几十 MB 的 base64 塞进一个请求</summary>
    internal async Task<string> UploadMediaAsync(bool isGroup, string openid, int fileType, string localPath, string url)
    {
        string path = localPath.Trim();
        if (url.Trim().Length > 0)
        {
            JsonElement body = await RequireRest().PostAsync(MediaFilesPath(isGroup, openid), new JsonObject
            {
                ["file_type"] = fileType,
                ["url"] = url.Trim(),
                ["srv_send_msg"] = false
            }, DestroyCancellationToken);
            return RequireFileInfo(body);
        }
        if (path.Length == 0)
        {
            throw new Exception("localPath 与 url 至少给一个");
        }
        FileInfo file = new(path);
        if (file.Exists == false)
        {
            throw new Exception("找不到文件：" + path);
        }
        string voiceProblem = QQSendPolish.VoiceProblem(fileType, path, ResolveSilkEncoderPath(), Configuration.AllowNonSilkVoice);
        if (voiceProblem.Length > 0)
        {
            throw new Exception(voiceProblem);
        }
        if (fileType == 3)
        {
            // 转换产物直接落缓存目录，临时 pcm 由 ConvertToSilkAsync 自己清掉
            file = await ConvertToSilkAsync(file);
        }
        if (file.Length > hardLimitBytes)
        {
            throw new Exception("超过平台硬限制 200MB（" + (file.Length / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + "MB）");
        }
        if (file.Length <= chunkSoftLimitBytes)
        {
            JsonObject simple = new()
            {
                ["file_type"] = fileType,
                // 注意：这里必须读转码后的 file，不是原来的 path，否则 file_type=3 传上去的还是原格式
                ["file_data"] = Convert.ToBase64String(File.ReadAllBytes(file.FullName)),
                ["srv_send_msg"] = false
            };
            // 语音绝不能带 file_name：带了平台会按文件卡片发，语音条就没了
            if (fileType != 3)
            {
                simple["file_name"] = file.Name;
            }
            return RequireFileInfo(await RequireRest().PostAsync(MediaFilesPath(isGroup, openid), simple, DestroyCancellationToken));
        }
        return await UploadInChunksAsync(isGroup, openid, fileType, file);
    }

    string MediaFilesPath(bool isGroup, string openid) => (isGroup ? groupsPath : usersPath) + Uri.EscapeDataString(openid) + "/files";

    /// <summary>分片四步：prepare 拿 upload_id/block_size/预签名 URL → 逐片 PUT → 逐片 part_finish → /files 带 upload_id 合并拿 file_info。
    /// 路径参数名平台写的是 group_id/user_id，但描述明确「群 OpenID/用户 OpenID」，所以传的就是 openid</summary>
    async Task<string> UploadInChunksAsync(bool isGroup, string openid, int fileType, FileInfo file)
    {
        byte[] bytes = File.ReadAllBytes(file.FullName);
        string headHash = HashHex(MD5.Create(), bytes.Take((int)Math.Min(md5HeadBytes, bytes.Length)).ToArray());
        string preparePath = (isGroup ? groupsPath : usersPath) + Uri.EscapeDataString(openid) + mediaPreparePath;
        JsonObject prepare = new()
        {
            ["file_type"] = fileType,
            ["file_size"] = bytes.Length.ToString(CultureInfo.InvariantCulture),
            ["file_name"] = file.Name,
            ["md5"] = HashHex(MD5.Create(), bytes),
            ["sha1"] = HashHex(SHA1.Create(), bytes),
            ["md5_10m"] = headHash
        };
        // 语音条不能带文件名，带了会掉成文件卡片（和小文件分支同一规则）
        if (fileType == 3)
        {
            prepare.Remove("file_name");
        }
        JsonElement prepared = await RequireRest().PostAsync(preparePath, prepare, DestroyCancellationToken);
        string uploadId = QQJson.Text(prepared, "upload_id");
        long blockSize = QQJson.Integer(prepared, "block_size", 5 * 1024 * 1024);
        List<JsonElement> parts = QQJson.Array(prepared, "parts").ToList();
        if (uploadId.Length == 0 || parts.Count == 0)
        {
            throw new Exception("预上传没给 upload_id 或分片列表：" + QQRestClient.Shorten(prepared.ToString()));
        }
        string finishPath = (isGroup ? groupsPath : usersPath) + Uri.EscapeDataString(openid) + mediaPartFinishPath;
        foreach (JsonElement part in parts)
        {
            int index = (int)QQJson.Integer(part, "index");
            string presigned = QQJson.Text(part, "presigned_url");
            if (presigned.Length == 0)
            {
                throw new Exception("分片 " + index.ToString(CultureInfo.InvariantCulture) + " 没有预签名 URL");
            }
            int start = index * (int)blockSize;
            int length = Math.Min((int)blockSize, bytes.Length - start);
            if (length <= 0)
            {
                continue;
            }
            byte[] chunk = bytes.Skip(start).Take(length).ToArray();
            string etag = await PutToPresignedAsync(presigned, chunk, "application/octet-stream");
            await RequireRest().PostAsync(finishPath, new JsonObject
            {
                ["upload_id"] = uploadId,
                ["part_index"] = index,
                ["block_size"] = length.ToString(CultureInfo.InvariantCulture),
                ["md5"] = etag.Length > 0 ? etag : HashHex(MD5.Create(), chunk)
            }, DestroyCancellationToken);
        }
        JsonElement merged = await RequireRest().PostAsync(MediaFilesPath(isGroup, openid), new JsonObject
        {
            ["file_type"] = fileType,
            ["upload_id"] = uploadId,
            ["url"] = "",
            ["srv_send_msg"] = false
        }, DestroyCancellationToken);
        return RequireFileInfo(merged);
    }

    static string RequireFileInfo(JsonElement uploaded)
    {
        string fileInfo = QQJson.Text(uploaded, "file_info");
        if (fileInfo.Length == 0)
        {
            throw new Exception("上传没拿到 file_info：" + QQRestClient.Shorten(uploaded.ToString()));
        }
        return fileInfo;
    }

    /// <summary>直传 COS：Content-Type 必须与申请时声明的一致，否则 COS 回 403；返回头里的 ETag（去引号）就是下一步要的 MD5</summary>
    static async Task<string> PutToPresignedAsync(string presignedUrl, byte[] payload, string contentType)
    {
        using HttpClient client = new();
        using ByteArrayContent content = new(payload);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        using HttpRequestMessage request = new(HttpMethod.Put, presignedUrl) { Content = content };
        using HttpResponseMessage response = await client.SendAsync(request);
        if (response.IsSuccessStatusCode == false)
        {
            throw new Exception("分片直传失败 HTTP " + (int)response.StatusCode + "（Content-Type 与申请时不一致会被 COS 拒 403）");
        }
        return response.Headers.ETag == null ? "" : response.Headers.ETag.ToString().Trim('"');
    }

    static string HashHex(HashAlgorithm algorithm, byte[] payload)
    {
        using (algorithm)
        {
            return string.Concat(algorithm.ComputeHash(payload).Select(item => item.ToString("x2")));
        }
    }

    static string GuessImageContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => throw new Exception("素材图片只收 " + string.Join("/", materialTypes))
    };

    static string FromUnix(long seconds) => seconds <= 0
        ? "?"
        : DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// 语音条要 silk，而 ffmpeg 只能**解码** silk 不能编码，所以链路是：ffmpeg 把任意音频解成 pcm → 编码器出 silk。
    /// 编码器没手配时自动用「宿主运行时里的 python + 插件自带 tools/to_silk.py」；
    /// 转码结果按「内容哈希 + 截断秒数 + 采样率 + 响度」缓存在临时目录，同一段音频重复发不再转。
    /// 临时 pcm/silk 在 finally 里清掉，不留垃圾。
    /// </summary>
    async Task<FileInfo> ConvertToSilkAsync(FileInfo source)
    {
        string encoder = ResolveSilkEncoderPath();
        if (encoder.Length == 0)
        {
            return source;
        }
        bool bundled = Configuration.SilkEncoderPath.Trim().Length == 0;
        string encoderArgs;
        if (bundled)
        {
            encoderArgs = BundledEncoderArgs(PcmRate());
        }
        else
        {
            encoderArgs = Configuration.SilkEncoderArgs.Trim();
            if (QQSendPolish.EncoderArgsUsable(encoderArgs) == false)
            {
                throw new Exception("配置「语音·编码器参数」里必须同时有 {in} 与 {out} 两个占位符");
            }
        }

        int rate = PcmRate();
        int cap = Math.Clamp(Configuration.VoiceMaxSeconds, 5, 3600);
        bool loudness = Configuration.VoiceLoudnessNormalize;

        // 缓存键带上所有会改变输出结果的参数，命中就直接复用
        string cacheKey = HashHex(MD5.Create(), File.ReadAllBytes(source.FullName))
            + "_" + cap.ToString(CultureInfo.InvariantCulture)
            + "_" + rate.ToString(CultureInfo.InvariantCulture)
            + (loudness ? "_loud" : "");
        string cacheDir = Path.Combine(Path.GetTempPath(), voiceCacheFolder);
        string cached = Path.Combine(cacheDir, cacheKey + ".silk");
        if (File.Exists(cached) && new FileInfo(cached).Length > 0)
        {
            DetailLog("语音命中缓存，跳过转码：" + source.Name);
            return new FileInfo(cached);
        }

        string ffmpeg = FindFfmpeg();
        if (ffmpeg.Length == 0)
        {
            throw new Exception("转 silk 需要 ffmpeg 先把音频解成 pcm，但自带运行时与 PATH 上都找不到 ffmpeg"
                + "（本插件清单已把 imageio-ffmpeg 声明成 pip 依赖，让宿主同步一次插件环境就会带上一份）");
        }
        string work = Path.Combine(Path.GetTempPath(), voiceWorkPrefix + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        string pcm = Path.Combine(work, "audio.pcm");
        string silk = Path.Combine(work, Path.GetFileNameWithoutExtension(source.Name) + ".silk");
        string tone = loudness ? " -af loudnorm=I=-16:TP=-1.5:LRA=11" : "";
        (string program, string arguments)[] steps =
        [
            (ffmpeg, "-y -i \"" + source.FullName + "\" -t " + cap.ToString(CultureInfo.InvariantCulture)
                + " -f s16le -ar " + rate.ToString(CultureInfo.InvariantCulture) + " -ac 1" + tone + " \"" + pcm + "\""),
            (encoder, encoderArgs
                .Replace("{in}", "\"" + pcm + "\"", StringComparison.Ordinal)
                .Replace("{out}", "\"" + silk + "\"", StringComparison.Ordinal))
        ];
        try
        {
            foreach ((string program, string arguments) in steps)
            {
                (int exitCode, string error) = await RunToolAsync(program, arguments);
                if (exitCode != 0)
                {
                    throw new Exception(Path.GetFileName(program) + " 转码失败（退出码 " + exitCode.ToString(CultureInfo.InvariantCulture)
                        + "）：" + QQRestClient.Shorten(error));
                }
            }
            FileInfo result = new(silk);
            if (result.Exists == false || result.Length == 0)
            {
                throw new Exception("编码器跑完了但没产出 silk 文件，检查「语音·silk 编码器」的参数写法");
            }
            Directory.CreateDirectory(cacheDir);
            File.Copy(silk, cached, true);
            DetailLog("语音已转 silk：" + result.Length.ToString(CultureInfo.InvariantCulture) + " 字节（"
                + rate.ToString(CultureInfo.InvariantCulture) + "Hz" + (loudness ? "，响度归一化" : "") + "）");
            return new FileInfo(cached);
        }
        finally
        {
            try
            {
                Directory.Delete(work, true);
            }
            catch
            {
                // 临时目录删不掉不影响结果
            }
        }
    }

    int PcmRate() => Configuration.VoicePcmRate <= 0
        ? pcmRate
        : Math.Clamp(Configuration.VoicePcmRate, 8000, 48000);

    /// <summary>没手配编码器时用插件自带的 to_silk.py；返回空串表示自动方案不可用。</summary>
    string ResolveSilkEncoderPath()
    {
        string configured = (Configuration.SilkEncoderPath ?? "").Trim();
        if (configured.Length > 0)
        {
            return configured;
        }
        return BundledSilkScript().Length > 0 ? FindBundledPython() : "";
    }

    /// <summary>自动方案的编码器参数：脚本 + {in} {out} + 采样率（to_silk.py 的第三个参数）。</summary>
    static string BundledEncoderArgs(int rate) =>
        "\"" + BundledSilkScript() + "\" {in} {out} " + rate.ToString(CultureInfo.InvariantCulture);

    static string? bundledSilkScriptCache;

    /// <summary>插件目录里的 tools/to_silk.py；插件改名或换目录时退化为在 Plugins 下扫一遍。</summary>
    static string BundledSilkScript()
    {
        if (bundledSilkScriptCache != null)
        {
            return bundledSilkScriptCache;
        }
        string direct = Path.Combine(AlifePath.StorageFolderPath, "Plugins", "Suran.QQ", "tools", "to_silk.py");
        if (File.Exists(direct))
        {
            bundledSilkScriptCache = direct;
            return direct;
        }
        string plugins = Path.Combine(AlifePath.StorageFolderPath, "Plugins");
        if (Directory.Exists(plugins))
        {
            string[] found = Directory.GetFiles(plugins, "to_silk.py", SearchOption.AllDirectories);
            if (found.Length > 0)
            {
                bundledSilkScriptCache = found[0];
                return found[0];
            }
        }
        bundledSilkScriptCache = "";
        return "";
    }

    /// <summary>宿主运行时里的 python（Python3xx 目录下的 python.exe）。</summary>
    static string FindBundledPython()
    {
        string runtimeRoot = AlifePath.RuntimeFolderPath;
        if (Directory.Exists(runtimeRoot) == false)
        {
            return "";
        }
        foreach (string python in Directory.GetDirectories(runtimeRoot, "Python*"))
        {
            string exe = Path.Combine(python, ExecutableName("python"));
            if (File.Exists(exe))
            {
                return exe;
            }
            string unix = Path.Combine(python, "bin", "python3");
            if (File.Exists(unix))
            {
                return unix;
            }
        }
        return "";
    }

    /// <summary>
    /// 设置页「检测语音链路」：和真正发送走同一套解析逻辑，逐项验证
    /// ffmpeg → 编码器（自动/手配、占位符）→ pilk → 用同样参数跑一次 0.5 秒静音的端到端编码。
    /// 缺哪项直接给修复命令，端到端失败把编码器 stderr 原样带出来。
    /// </summary>
    public async Task<string> VoiceChainCheckAsync()
    {
        List<string> lines = new();

        // ① ffmpeg：不只看文件在不在，真跑一次 -version 确认能执行
        string ffmpeg = FindFfmpeg();
        if (ffmpeg.Length == 0)
        {
            lines.Add("[缺] ffmpeg：运行时与 PATH 都没找到；让宿主同步一次插件环境（清单里有 imageio-ffmpeg），或把 ffmpeg 加进 PATH");
        }
        else
        {
            (int code, string error) = await RunToolAsync(ffmpeg, "-version");
            lines.Add(code == 0
                ? "[OK] ffmpeg：" + ffmpeg
                : "[缺] ffmpeg 找到了但跑不起来：" + ffmpeg + "｜" + QQRestClient.Shorten(error));
        }

        // ② 编码器：自动方案（运行时 python + 自带 to_silk.py）或手配方案
        bool bundled = Configuration.SilkEncoderPath.Trim().Length == 0;
        string encoder = ResolveSilkEncoderPath();
        if (encoder.Length == 0)
        {
            lines.Add("[缺] silk 编码器：没手配，运行时里也没找到 python + tools/to_silk.py");
            lines.Add(VoiceSettingSummary());
            return string.Join("\n", lines);
        }
        string encoderArgs;
        if (bundled)
        {
            encoderArgs = BundledEncoderArgs(PcmRate());
            lines.Add("[OK] silk 编码器：" + encoder + "（自动：插件自带 to_silk.py）");
        }
        else
        {
            encoderArgs = Configuration.SilkEncoderArgs.Trim();
            lines.Add(QQSendPolish.EncoderArgsUsable(encoderArgs)
                ? "[OK] silk 编码器：" + encoder + "（手配）"
                : "[缺] 编码器参数缺 {in} 或 {out}：现在写的是「" + encoderArgs + "」，转码时会直接报错");
        }

        // ③ pilk：编码器是 python 才查
        if (Path.GetFileNameWithoutExtension(encoder).StartsWith("python", StringComparison.OrdinalIgnoreCase))
        {
            (int code, string error) = await RunToolAsync(encoder, "-c \"import pilk; print('ok')\"");
            lines.Add(code == 0
                ? "[OK] pilk 已安装"
                : "[缺] pilk 未安装：用同一个 python 执行 -m pip install pilk（Windows 轮子只到 cp311，3.12 要 Rust 现编）"
                  + (error.Length > 0 ? "｜" + QQRestClient.Shorten(error) : ""));
        }
        else
        {
            lines.Add("[跳过] pilk 检测：编码器不是 python，依赖由它自己保证");
        }

        // ④ 端到端：拿同样的参数跑一次 0.5 秒静音 pcm → silk，能产出才算真能用
        if (lines.Any(line => line.StartsWith("[缺]", StringComparison.Ordinal)))
        {
            lines.Add("[跳过] 端到端：上面还有缺失项，先补齐再测");
        }
        else
        {
            int rate = PcmRate();
            string work = Path.Combine(Path.GetTempPath(), voiceWorkPrefix + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(work);
            string pcm = Path.Combine(work, "probe.pcm");
            string silk = Path.Combine(work, "probe.silk");
            try
            {
                // 0.5 秒 16bit 单声道静音，正好是转码链路的输入形状
                File.WriteAllBytes(pcm, new byte[rate / 2 * 2]);
                (int code, string error) = await RunToolAsync(encoder, encoderArgs
                    .Replace("{in}", "\"" + pcm + "\"", StringComparison.Ordinal)
                    .Replace("{out}", "\"" + silk + "\"", StringComparison.Ordinal));
                FileInfo output = new(silk);
                if (code == 0 && output.Exists && output.Length > 0)
                {
                    lines.Add("[OK] 端到端：编码器产出 " + output.Length.ToString(CultureInfo.InvariantCulture) + " 字节 silk");
                }
                else
                {
                    lines.Add("[缺] 端到端失败（退出码 " + code.ToString(CultureInfo.InvariantCulture) + "）："
                        + (error.Length > 0 ? QQRestClient.Shorten(error) : "编码器没产出 silk，检查编码器参数与脚本路径"));
                }
            }
            catch (Exception probeError)
            {
                lines.Add("[缺] 端到端异常：" + probeError.Message);
            }
            finally
            {
                try
                {
                    Directory.Delete(work, true);
                }
                catch
                {
                    // 探针临时目录删不掉不影响结果
                }
            }
        }

        lines.Add(VoiceSettingSummary());
        return string.Join("\n", lines);
    }

    /// <summary>检测报告末尾那句话：把实际生效的采样率/时长/响度与缓存目录写清楚，便于排查。</summary>
    string VoiceSettingSummary()
    {
        return "采样率 " + PcmRate().ToString(CultureInfo.InvariantCulture) + "Hz，最长 "
            + Math.Clamp(Configuration.VoiceMaxSeconds, 5, 3600).ToString(CultureInfo.InvariantCulture) + " 秒，响度归一化："
            + (Configuration.VoiceLoudnessNormalize ? "开" : "关")
            + "；转码缓存：" + Path.Combine(Path.GetTempPath(), voiceCacheFolder);
    }

    // ── 动图降级链 ─────────────────────────────────────────────

    static readonly Dictionary<string, DateTime> rejectedImageOriginals = new(StringComparer.OrdinalIgnoreCase);

    static bool IsAnimatedImage(string path) =>
        path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 动图发送：原图直传 → APNG → 静态 PNG → 按文件。
    /// 被平台拒过的原图 10 分钟内不再重试，免得每次发同一张图都先失败一次。
    /// </summary>
    async Task SendImageWithFallbackAsync(bool isGroup, string target, string localPath)
    {
        string original = Path.GetFullPath(localPath);
        FileInfo source = new(original);
        if (source.Exists == false)
        {
            throw new Exception("找不到文件：" + localPath);
        }
        string stamp = original + "|" + source.LastWriteTimeUtc.Ticks;
        string mode = (Configuration.AnimatedImageMode ?? "auto").Trim().ToLowerInvariant();
        if (mode == "file")
        {
            // 用户选了「原图按文件发」：保动画，但要点开下载
            await SendMediaFileAsync(isGroup, target, 4, original);
            return;
        }
        bool skipOriginal = mode == "static"
            || (rejectedImageOriginals.TryGetValue(stamp, out DateTime rejectedAt)
                && DateTime.UtcNow - rejectedAt < TimeSpan.FromMinutes(10));

        string work = Path.Combine(Path.GetTempPath(), "qqofficial_img_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            if (skipOriginal == false)
            {
                try
                {
                    await SendMediaFileAsync(isGroup, target, 1, original);
                    return;
                }
                catch (Exception ex)
                {
                    rejectedImageOriginals[stamp] = DateTime.UtcNow;
                    DetailLog("动图原图发送失败，转 APNG/PNG 再试：" + ex.Message);
                }
            }

            string ffmpeg = FindFfmpeg();
            if (ffmpeg.Length == 0)
            {
                throw new Exception("动图原图被平台拒了，转码需要 ffmpeg，但运行时与 PATH 都没找到");
            }
            // 扩展名保持 .png：平台按扩展名判图片类型，内容是不是 APNG 由它自己嗅探
            string apng = Path.Combine(work, "anim.png");
            if ((await RunToolAsync(ffmpeg, "-y -i \"" + original + "\" -plays 0 -f apng \"" + apng + "\"")).ExitCode == 0
                && File.Exists(apng) && new FileInfo(apng).Length > 0)
            {
                try
                {
                    await SendMediaFileAsync(isGroup, target, 1, apng);
                    DetailLog("动图已转 APNG 发出");
                    return;
                }
                catch (Exception ex)
                {
                    DetailLog("APNG 发送失败：" + ex.Message);
                }
            }

            string png = Path.Combine(work, "static.png");
            if ((await RunToolAsync(ffmpeg, "-y -i \"" + original + "\" -frames:v 1 \"" + png + "\"")).ExitCode == 0
                && File.Exists(png) && new FileInfo(png).Length > 0)
            {
                try
                {
                    await SendMediaFileAsync(isGroup, target, 1, png);
                    DetailLog("动图已转静态 PNG 发出");
                    return;
                }
                catch (Exception ex)
                {
                    DetailLog("静态 PNG 发送失败：" + ex.Message);
                }
            }

            // 最后按文件发：原图还在，点开就能看
            await SendMediaFileAsync(isGroup, target, 4, original);
            interactor.Poke("[QQ官方机器人] 动图按图片发不出去，已降级成文件发出：" + Path.GetFileName(original));
        }
        finally
        {
            try
            {
                Directory.Delete(work, true);
            }
            catch
            {
                // 临时目录删不掉不影响结果
            }
        }
    }

    async Task SendMediaFileAsync(bool isGroup, string target, int fileType, string path)
    {
        string fileInfo = await UploadMediaAsync(isGroup, target, fileType, path, "");
        JsonObject payload = new()
        {
            ["msg_type"] = 7,
            ["media"] = new JsonObject { ["file_info"] = fileInfo }
        };
        string key = ConversationKey(isGroup, target);
        if (lastInboundMessageId.TryGetValue(key, out string? recent) && recent.Length > 0)
        {
            payload["msg_id"] = recent;
        }
        await SendMessageAsync(isGroup, target, payload, "QQSendMedia");
    }

    static string FindTool(string executable)
    {
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (directory.Trim().Length == 0)
            {
                continue;
            }
            string candidate = Path.Combine(directory.Trim(), executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return "";
    }

    /// <summary>ffmpeg 取值优先级：自带运行时的 imageio-ffmpeg → PATH。宿主热编译按平台注入符号，可执行文件后缀要按平台取。</summary>
    static string FindFfmpeg()
    {
        string bundled = FindBundledFfmpeg();
        return bundled.Length > 0 ? bundled : FindTool(ExecutableName("ffmpeg"));
    }

    /// <summary>imageio-ffmpeg 这个 pip 包把成品 ffmpeg 放在包内 binaries 目录，文件名带版本号且会变，只能通配找。</summary>
    static string FindBundledFfmpeg()
    {
        string runtimeRoot = AlifePath.RuntimeFolderPath;
        if (Directory.Exists(runtimeRoot) == false)
        {
            return "";
        }
        foreach (string python in Directory.GetDirectories(runtimeRoot, "Python*"))
        {
            string binaries = Path.Combine(python, "Lib", "site-packages", "imageio_ffmpeg", "binaries");
            if (Directory.Exists(binaries) == false)
            {
                continue;
            }
            foreach (string file in Directory.GetFiles(binaries, "ffmpeg-*"))
            {
                if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || OperatingSystem.IsWindows() == false)
                {
                    return file;
                }
            }
        }
        return "";
    }

    static string ExecutableName(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    static async Task<(int ExitCode, string Error)> RunToolAsync(string program, string arguments)
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = program,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        process.Start();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(120));
        string error = await process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, error);
    }
}
