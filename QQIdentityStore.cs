using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Suran.QQ;

/// <summary>
/// 身份通讯录：openid → 昵称/QQ号/备注名，以及群 openid → 群名。
///
/// 为什么需要它：官方接口在**单聊里 `author.username` 恒为空串**，而且 OpenAPI 没有任何"按 openid 查用户资料"
/// 的接口（`/users/@me` 只回机器人自己，`union_openid`/`union_user_account` 要特殊申请+内邀）。
/// 破解靠一个实测事实：**私聊的 user_openid 与群里的 member_openid 是同一个值**，
/// 而群消息是带 `username` 的 —— 所以通讯录必须**跨场景共享**：群里认识过的人，私聊也认得。
/// 第二个学习来源是引用消息（message_type=103 时 msg_elements[0].author 是完整 User 对象）。
///
/// 三条硬规矩：① 消息路径上**零 I/O**（只标脏，落盘由后台 SaveIfNeeded 做）；
/// ② 昵称与已记录不同就覆盖 ⇒ 用户改名自动跟随；③ **QQ 号只做有依据的绑定**（人工绑定、内邀字段、
/// 或由另一条 QQ 通道对撞出的唯一昵称配对），重名一律不猜。
/// </summary>
public class QQIdentityStore(string folder, Action<string> logDetail)
{
    const string fileName = "identities.json";
    const string peersFileName = "qq_peers.txt";
    const int formatVersion = 3;

    readonly Dictionary<string, Person> people = new();
    readonly Dictionary<string, string> groupNames = new();
    bool dirty;

    public sealed class Person
    {
        public string Nickname { get; set; } = "";
        public string QqNumber { get; set; } = "";
        public string Remark { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
    }

    string PathOfStore => System.IO.Path.Combine(folder, fileName);
    string PathOfPeers => System.IO.Path.Combine(folder, peersFileName);

    public int PeopleCount => people.Count;
    public int GroupCount => groupNames.Count;
    public int BoundAccountCount => people.Values.Count(person => person.QqNumber.Length > 0);

    public void Load()
    {
        if (File.Exists(PathOfStore) == false)
        {
            return;
        }
        try
        {
            JsonElement root = JsonDocument.Parse(File.ReadAllText(PathOfStore, Encoding.UTF8)).RootElement.Clone();
            foreach (JsonElement item in QQJson.Array(root, "people"))
            {
                string openid = QQJson.Text(item, "openid");
                if (openid.Length == 0)
                {
                    continue;
                }
                people[openid] = new Person
                {
                    Nickname = QQJson.Text(item, "nickname"),
                    QqNumber = QQJson.Text(item, "qq"),
                    Remark = QQJson.Text(item, "remark"),
                    UpdatedAt = QQJson.Text(item, "updated_at")
                };
            }
            foreach (JsonElement item in QQJson.Array(root, "groups"))
            {
                string openid = QQJson.Text(item, "group_openid");
                string name = QQJson.Text(item, "name");
                if (openid.Length > 0 && name.Length > 0)
                {
                    groupNames[openid] = name;
                }
            }
            logDetail?.Invoke($"通讯录已载入：{people.Count.ToString(CultureInfo.InvariantCulture)} 人、"
                + $"{groupNames.Count.ToString(CultureInfo.InvariantCulture)} 个群名");
        }
        catch (Exception loadError)
        {
            logDetail?.Invoke("通讯录文件读坏了，按空的用：" + loadError.Message);
        }
    }

    /// <summary>学到昵称就记下来；与已记录不同就覆盖（改名自动跟随）。只在内存里动，不写盘</summary>
    public bool RememberNickname(string openid, string nickname)
    {
        if (openid.Length == 0 || nickname.Trim().Length == 0)
        {
            return false;
        }
        string clean = nickname.Trim();
        if (people.TryGetValue(openid, out Person known) && known != null)
        {
            if (known.Nickname == clean)
            {
                return false;
            }
            known.Nickname = clean;
        }
        else
        {
            people[openid] = new Person { Nickname = clean };
        }
        Stamp(people[openid]);
        dirty = true;
        return true;
    }

    public bool RememberGroupName(string groupOpenid, string name)
    {
        if (groupOpenid.Length == 0 || name.Trim().Length == 0 || groupNames.TryGetValue(groupOpenid, out string have) && have == name.Trim())
        {
            return false;
        }
        groupNames[groupOpenid] = name.Trim();
        dirty = true;
        return true;
    }

    public string GroupName(string groupOpenid) => groupNames.TryGetValue(groupOpenid, out string name) ? name : "";

    /// <summary>已知群（openid → 群名）的副本，给设置页列群用。复制一份是为了避免和消息线程抢字典。</summary>
    public Dictionary<string, string> GroupNamesSnapshot()
    {
        try
        {
            return new Dictionary<string, string>(groupNames);
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>人工或接口给的 QQ 号/备注名绑定。传空串表示不动那一项</summary>
    public void Bind(string openid, string qqNumber = "", string remark = "")
    {
        if (openid.Length == 0)
        {
            return;
        }
        if (people.TryGetValue(openid, out Person person) == false || person == null)
        {
            person = new Person();
            people[openid] = person;
        }
        if (qqNumber.Trim().Length > 0)
        {
            person.QqNumber = qqNumber.Trim();
        }
        if (remark.Trim().Length > 0)
        {
            person.Remark = remark.Trim();
        }
        Stamp(person);
        dirty = true;
    }

    /// <summary>注入会话时用的显示名：备注名优先，其次 QQ号+昵称，再次昵称，都没有就明说"昵称未知"</summary>
    public string DisplayName(string openid)
    {
        if (people.TryGetValue(openid, out Person person) == false || person == null)
        {
            return "昵称未知";
        }
        if (person.Remark.Length > 0)
        {
            return person.Remark + (person.QqNumber.Length > 0 ? "／QQ " + person.QqNumber : "");
        }
        if (person.Nickname.Length > 0)
        {
            return person.Nickname + (person.QqNumber.Length > 0 ? "／QQ " + person.QqNumber : "");
        }
        return person.QqNumber.Length > 0 ? "昵称未知／QQ " + person.QqNumber : "昵称未知";
    }

    public string QqNumberOf(string openid) => people.TryGetValue(openid, out Person person) && person != null ? person.QqNumber : "";

    /// <summary>
    /// 从另一条 QQ 通道对撞学 QQ 号：`qq_peers.txt` 每行「昵称<TAB>QQ号」（由那条通道的接入方写）。
    /// 只有**全局唯一**的昵称才敢配，重名一律跳过并计数——猜错会把人认成别人。
    /// </summary>
    public int LearnQqFromPeers()
    {
        if (File.Exists(PathOfPeers) == false)
        {
            return 0;
        }
        Dictionary<string, List<string>> byName = new();
        foreach (string line in File.ReadAllLines(PathOfPeers, Encoding.UTF8))
        {
            string[] parts = line.Split('\t');
            if (parts.Length != 2 || parts[0].Trim().Length == 0 || parts[1].Trim().Length == 0)
            {
                continue;
            }
            byName.TryAdd(parts[0].Trim(), new List<string>());
            byName[parts[0].Trim()].Add(parts[1].Trim());
        }
        int matched = 0;
        int ambiguous = 0;
        foreach ((string openid, Person person) in people)
        {
            if (person.QqNumber.Length > 0 || person.Nickname.Length == 0)
            {
                continue;
            }
            if (byName.TryGetValue(person.Nickname, out List<string> numbers) == false)
            {
                continue;
            }
            if (numbers.Distinct().Count() != 1)
            {
                ambiguous++;
                continue;
            }
            person.QqNumber = numbers[0];
            Stamp(person);
            matched++;
        }
        dirty = dirty || matched > 0;
        if (matched > 0 || ambiguous > 0)
        {
            logDetail?.Invoke($"QQ号对撞：配上 {matched.ToString(CultureInfo.InvariantCulture)} 个、"
                + $"因重名跳过 {ambiguous.ToString(CultureInfo.InvariantCulture)} 个");
        }
        return matched;
    }

    void Stamp(Person person) => person.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>落盘只在后台调用（消息路径零 I/O）；不脏就直接返回</summary>
    public bool SaveIfNeeded()
    {
        if (dirty == false)
        {
            return false;
        }
        JsonObject root = new()
        {
            ["format"] = formatVersion,
            ["saved_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
        };
        JsonArray persons = new();
        foreach ((string openid, Person person) in people.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            persons.Add(new JsonObject
            {
                ["openid"] = openid,
                ["nickname"] = person.Nickname,
                ["qq"] = person.QqNumber,
                ["remark"] = person.Remark,
                ["updated_at"] = person.UpdatedAt
            });
        }
        root["people"] = persons;
        JsonArray groups = new();
        foreach ((string openid, string name) in groupNames.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            groups.Add(new JsonObject { ["group_openid"] = openid, ["name"] = name });
        }
        root["groups"] = groups;
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(PathOfStore, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            dirty = false;
            return true;
        }
        catch (Exception saveError)
        {
            logDetail?.Invoke("通讯录落盘失败（下次再试）：" + saveError.Message);
            return false;
        }
    }

    /// <summary>按昵称/备注名/QQ号/openid 子串查人，最多回 20 条</summary>
    public List<string> Search(string keyword) =>
        people.Where(entry =>
                entry.Value.Nickname.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || entry.Value.Remark.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || entry.Value.QqNumber == keyword
                || entry.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .Select(entry => entry.Key + " | " + DisplayName(entry.Key)
                + " | 昵称 " + (entry.Value.Nickname.Length > 0 ? entry.Value.Nickname : "—"))
            .ToList();

    public string Describe(int limit)
    {
        List<string> lines = new()
        {
            "通讯录：" + people.Count.ToString(CultureInfo.InvariantCulture) + " 人（其中带 QQ 号 "
            + BoundAccountCount.ToString(CultureInfo.InvariantCulture) + " 个）、群名 "
            + groupNames.Count.ToString(CultureInfo.InvariantCulture) + " 个；对撞源 "
            + (File.Exists(PathOfPeers) ? PathOfPeers : "（没有 qq_peers.txt，QQ号只能人工绑或等内邀字段）")
        };
        foreach ((string openid, Person person) in people
            .OrderBy(entry => entry.Value.UpdatedAt, StringComparer.Ordinal)
            .Take(Math.Max(1, limit)))
        {
            lines.Add("- " + openid + " | " + (person.Remark.Length > 0 ? person.Remark : person.Nickname)
                + " | QQ " + (person.QqNumber.Length > 0 ? person.QqNumber : "—")
                + " | " + person.UpdatedAt);
        }
        return string.Join("\n", lines);
    }
}
