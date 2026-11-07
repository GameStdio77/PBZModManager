using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PBZModManager;

/// <summary>
/// A mod the user means to get: a place in the mod list held by its address alone, before any file is there.
/// It fills in when an archive from that page arrives.
/// </summary>
public sealed class WantedMod
{
    public string Url { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Memo { get; set; } = "";
    public string InstallNotes { get; set; } = "";
    /// <summary>The version the list it came from was using; empty when unknown.</summary>
    public string Version { get; set; } = "";
    /// <summary>The sender asked for exactly <see cref="Version"/>; otherwise the newest is meant.</summary>
    public bool Pinned { get; set; }
    public DateTime AddedAt { get; set; }

    /// <summary>The page it is from: "game/id" for Nexus, as the mod list groups pages; else the address.</summary>
    [JsonIgnore]
    public string Page => NexusApi.LinkFor(Url, Array.Empty<string>()) is { } link ? $"{link.Game}/{link.ModId}" : Url.Trim();

    /// <summary>Whether an archive of this name from this page is the one meant.</summary>
    public bool Matches(string archiveName) =>
        ModIdentity.ParseFileName(archiveName).Name.Equals(Name, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One mod in a shared list.</summary>
public sealed class SharedMod
{
    /// <summary>The mod's key, which is the same on every PC for a Nexus file: it places the mod in the shared order.</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Version { get; set; } = "";
    public bool Pinned { get; set; }
    public string Category { get; set; } = "";
    public string Memo { get; set; } = "";
    public bool Installed { get; set; }
}

/// <summary>A mod list as one user shares it with another.</summary>
public sealed class SharedList
{
    public const string FormatName = "pbz-mod-list";
    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = 1;
    public string App { get; set; } = "";
    public List<SharedMod> Mods { get; set; } = new();
    /// <summary>The installed list's order: mod keys, and "sep:&lt;text&gt;" for the sender's separators.</summary>
    public List<string> Order { get; set; } = new();
}

/// <summary>A file's key after its author renamed it, and the key of the mod it was.</summary>
public sealed class ModRename
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public DateTime LinkedAt { get; set; }
}

/// <summary>Writes and reads shared mod lists: as a file, or as text to paste anywhere.</summary>
public static class ModListShare
{
    public const string FileExtension = ".pbzmods.json";
    const string TextMarker = "pbzmods:";
    public const string SeparatorPrefix = "sep:";

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string ToJson(SharedList list) => JsonSerializer.Serialize(list, Options);

    /// <summary>A readable list to paste in a chat, ending in one line that carries the whole list.</summary>
    public static string ToText(SharedList list)
    {
        var text = new StringBuilder();
        text.AppendLine(L.F("PBZ Mod Manager 모드 목록 ({0}개)", list.Mods.Count));
        foreach (var mod in list.Mods)
        {
            var version = mod.Version.Length == 0 ? "" : mod.Pinned ? " " + L.F("{0} 고정", mod.Version) : " " + mod.Version;
            text.AppendLine($"- {mod.Name}{version}  {mod.Url}".TrimEnd());
        }
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.SmallestSize))
            deflate.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(list, new JsonSerializerOptions(Options) { WriteIndented = false })));
        text.Append(TextMarker).Append(Convert.ToBase64String(packed.ToArray()));
        return text.ToString();
    }

    /// <summary>Reads a shared list from a file's text or a pasted text. Anything else is refused.</summary>
    public static SharedList Read(string text)
    {
        var marker = text.LastIndexOf(TextMarker, StringComparison.Ordinal);
        string json;
        try
        {
            if (marker >= 0)
            {
                var data = new string(text[(marker + TextMarker.Length)..].TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());
                using var deflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(data)), CompressionMode.Decompress);
                using var reader = new StreamReader(deflate, Encoding.UTF8);
                json = reader.ReadToEnd();
            }
            else json = text;
            var list = JsonSerializer.Deserialize<SharedList>(json, Options);
            if (list?.Format == SharedList.FormatName && list.Version == 1 && list.Mods != null)
            {
                list.Order ??= new List<string>();
                list.Mods.RemoveAll(m => m == null || m.Name.Length == 0);
                return list;
            }
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidDataException or NotSupportedException)
        {
            // Falls through to the refusal below.
        }
        throw new InvalidOperationException(L.T("PBZ Mod Manager 모드 목록이 아닙니다. 공유받은 파일이나 글 전체를 그대로 넣어 주세요."));
    }
}

public sealed partial class Manager
{
    string WantedPath => Path.Combine(StateDir, "wanted.json");
    string SharedOrderPath => Path.Combine(StateDir, "shared-order.json");
    string RenamesPath => Path.Combine(StateDir, "renames.json");

    /// <summary>Files the author renamed on their page, each joined to the mod it was.</summary>
    public List<ModRename> LoadRenames() => LoadList<ModRename>(RenamesPath);

    /// <summary>
    /// Joins a renamed file's key to the mod it was, from now on everywhere keys are made.
    /// Nothing happens when the key already leads there, or would lead back to itself.
    /// </summary>
    public bool LinkRename(string from, string to)
    {
        to = ModIdentity.Follow(to);
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase) || ModIdentity.Follow(from).Equals(to, StringComparison.OrdinalIgnoreCase)) return false;
        var links = LoadRenames();
        links.RemoveAll(l => l.From.Equals(from, StringComparison.OrdinalIgnoreCase));
        links.Add(new ModRename { From = from, To = to, LinkedAt = DateTime.Now });
        SaveList(RenamesPath, links);
        ModIdentity.UseRenames(links);
        Log($"이름 연결 {from} → {to}");
        return true;
    }

    /// <summary>Mods the user means to get, held in the list by their address.</summary>
    public List<WantedMod> LoadWanted() => LoadList<WantedMod>(WantedPath);

    public void SaveWanted(IEnumerable<WantedMod> wanted) => SaveList(WantedPath, wanted.ToList());

    /// <summary>The order a shared list came with, kept to place its mods as the user installs them.</summary>
    public List<string> LoadSharedOrder() => LoadList<string>(SharedOrderPath);

    public void SaveSharedOrder(IReadOnlyList<string> order) => SaveList(SharedOrderPath, order.ToList());

    List<T> LoadList<T>(string path)
    {
        if (!File.Exists(path)) return new List<T>();
        try
        {
            return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path)) ?? new List<T>();
        }
        catch (JsonException)
        {
            File.Move(path, UniquePath(path + ".broken"));
            return new List<T>();
        }
    }

    void SaveList<T>(string path, List<T> items)
    {
        var tmp = NewTmpPath();
        File.WriteAllText(tmp, JsonSerializer.Serialize(items, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>The installed list's order as shared: mod keys and separator texts, for the mods chosen.</summary>
    public List<string> OrderToShare(IReadOnlySet<string> keys) =>
        InstalledRows()
            .Where(r => r.IsSeparator || keys.Contains(r.Mod!.Key))
            .Select(r => r.IsSeparator ? ModListShare.SeparatorPrefix + r.Text : r.Mod!.Key)
            .ToList();

    /// <summary>
    /// Places installed mods the user's order does not name yet where the shared order has them: after the
    /// nearest entry before them that the list already has, with the shared separators between brought along.
    /// </summary>
    public void PlaceBySharedOrder()
    {
        var shared = LoadSharedOrder();
        if (shared.Count == 0) return;
        var named = LoadOrder();
        var rows = InstalledRows();
        bool changed = false;
        foreach (var mod in rows.Where(r => !r.IsSeparator && !named.Contains(r.Mod!.Key, StringComparer.OrdinalIgnoreCase)).Select(r => r.Mod!).ToList())
        {
            var at = shared.FindIndex(e => e.Equals(mod.Key, StringComparison.OrdinalIgnoreCase));
            if (at < 0) continue;
            rows.RemoveAll(r => !r.IsSeparator && r.Mod!.Key.Equals(mod.Key, StringComparison.OrdinalIgnoreCase));
            // The nearest earlier entry the list has; separators passed on the way that it lacks come along.
            var missingSeparators = new List<string>();
            int insertAt = 0;
            for (int i = at - 1; i >= 0; i--)
            {
                var entry = shared[i];
                int found = entry.StartsWith(ModListShare.SeparatorPrefix, StringComparison.Ordinal)
                    ? rows.FindIndex(r => r.IsSeparator && r.Text == entry[ModListShare.SeparatorPrefix.Length..])
                    : rows.FindIndex(r => !r.IsSeparator && r.Mod!.Key.Equals(entry, StringComparison.OrdinalIgnoreCase));
                if (found >= 0)
                {
                    insertAt = found + 1;
                    break;
                }
                if (entry.StartsWith(ModListShare.SeparatorPrefix, StringComparison.Ordinal))
                    missingSeparators.Insert(0, entry[ModListShare.SeparatorPrefix.Length..]);
            }
            foreach (var text in missingSeparators)
                rows.Insert(insertAt++, new InstalledRow(null, Guid.NewGuid().ToString("N")[..8], text));
            rows.Insert(insertAt, new InstalledRow(mod, null, mod.Name));
            changed = true;
        }
        if (changed) SaveOrder(rows);
    }

    /// <summary>Puts the installed list in the shared order now; mods and separators it does not name follow.</summary>
    public void ApplySharedOrder()
    {
        var shared = LoadSharedOrder();
        if (shared.Count == 0) return;
        var rows = InstalledRows();
        var result = new List<InstalledRow>();
        foreach (var entry in shared)
        {
            if (entry.StartsWith(ModListShare.SeparatorPrefix, StringComparison.Ordinal))
            {
                var text = entry[ModListShare.SeparatorPrefix.Length..];
                var existing = rows.FirstOrDefault(r => r.IsSeparator && r.Text == text);
                if (existing != null) rows.Remove(existing);
                result.Add(existing ?? new InstalledRow(null, Guid.NewGuid().ToString("N")[..8], text));
            }
            else if (rows.FirstOrDefault(r => !r.IsSeparator && r.Mod!.Key.Equals(entry, StringComparison.OrdinalIgnoreCase)) is { } mod)
            {
                rows.Remove(mod);
                result.Add(mod);
            }
        }
        result.AddRange(rows);
        SaveOrder(result);
    }
}
