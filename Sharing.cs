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
    string PresetsPath => Path.Combine(StateDir, "presets.json");

    /// <summary>The user's presets, in the order they keep them.</summary>
    public List<Preset> LoadPresets() => LoadList<Preset>(PresetsPath);

    public void SavePresets(IEnumerable<Preset> presets) => SaveList(PresetsPath, presets.ToList());

    /// <summary>
    /// Puts the installed list in a preset's order: its mods and the separators it keeps first, as saved, then
    /// everything it does not name. A separator of the same text is reused.
    /// </summary>
    /// <param name="wholeList">
    /// True when the preset is the whole installed set: it goes first. Otherwise its lines stay together where the
    /// first of them already was, so a small preset does not jump to the top.
    /// </param>
    public void ApplyPresetOrder(IReadOnlyList<string> order, bool wholeList = true)
    {
        if (wholeList)
        {
            ApplyOrder(order);
            return;
        }
        if (order.Count == 0) return;
        var rows = InstalledRows();
        var block = new List<InstalledRow>();
        int first = int.MaxValue;
        foreach (var entry in order)
        {
            InstalledRow? found;
            if (entry.StartsWith(ModListShare.SeparatorPrefix, StringComparison.Ordinal))
            {
                var text = entry[ModListShare.SeparatorPrefix.Length..];
                found = rows.FirstOrDefault(r => r.IsSeparator && r.Text == text && !block.Contains(r));
                block.Add(found ?? new InstalledRow(null, Guid.NewGuid().ToString("N")[..8], text));
            }
            else
            {
                found = rows.FirstOrDefault(r => !r.IsSeparator && r.Mod!.Key.Equals(entry, StringComparison.OrdinalIgnoreCase));
                if (found != null) block.Add(found);
            }
            if (found != null) first = Math.Min(first, rows.IndexOf(found));
        }
        var rest = rows.Where(r => !block.Contains(r)).ToList();
        var at = first == int.MaxValue ? rest.Count : rows.Take(first).Count(r => !block.Contains(r));
        rest.InsertRange(at, block);
        SaveOrder(rest);
    }

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
    public void ApplySharedOrder() => ApplyOrder(LoadSharedOrder());

    void ApplyOrder(IReadOnlyList<string> shared)
    {
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

/// <summary>One mod in a preset: which mod, which version, and how it was installed.</summary>
public sealed class PresetMod
{
    /// <summary>The mod's key (<see cref="ModIdentity.KeyOf"/>); a file renamed since still leads to it.</summary>
    public string Key { get; set; } = "";
    /// <summary>The name it was shown by when saved, for when the mod is no longer listed.</summary>
    public string Name { get; set; } = "";
    /// <summary>The version it had when saved; installed only when <see cref="Pinned"/>, else the newest is.</summary>
    public string Version { get; set; } = "";
    public bool Pinned { get; set; }
    /// <summary>The option folders picked; null when not known, and then the last ones used, or the user, decide.</summary>
    public List<string>? Options { get; set; }
    /// <summary>Files left out; null when not known.</summary>
    public List<string>? Excluded { get; set; }
}

/// <summary>A set of mods saved under a name, to install, keep or remove together.</summary>
public sealed class Preset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    /// <summary>
    /// Presets of one group are not used together: applying one first removes what another of its group put in.
    /// Empty for none.
    /// </summary>
    public string Group { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<PresetMod> Mods { get; set; } = new();
    /// <summary>The installed list's order to put back: mod keys and the separators chosen ("sep:text"). Empty leaves the order.</summary>
    public List<string> Order { get; set; } = new();
}

/// <summary>How a preset is applied.</summary>
public enum PresetMode
{
    /// <summary>The installed mods become exactly the preset's: others are removed.</summary>
    Match,
    /// <summary>The preset's mods are installed as saved; others stay.</summary>
    TurnOn,
    /// <summary>The preset's mods are removed; others stay.</summary>
    TurnOff,
}

public enum PresetAction { Install, Update, Reinstall, Keep, Remove, Skip }

/// <summary>What applying a preset does to one mod.</summary>
/// <param name="Fallback">For a pinned version whose file is gone: the newest file, which the user may take instead.</param>
public sealed record PresetStep(PresetAction Action, string Key, string Name, string Note,
    ModGroup? Mod = null, ModVersion? Version = null, PresetMod? Entry = null, InstalledMod? Installed = null, ModVersion? Fallback = null);

public static class PresetPlanner
{
    static bool SameSet(IReadOnlyCollection<string>? wanted, IReadOnlyCollection<string> now) =>
        wanted == null || new HashSet<string>(wanted, StringComparer.OrdinalIgnoreCase).SetEquals(now);

    /// <summary>Whether two presets share a group, and so are not used together.</summary>
    public static bool SameGroup(Preset a, Preset b) =>
        a.Id != b.Id && a.Group.Trim().Length > 0 && a.Group.Trim().Equals(b.Group.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What applying a preset would do, mod by mod, in the preset's order and then the removals. Installing it, the
    /// installed mods of other presets of its group (<paramref name="others"/>) that it does not keep are removed first.
    /// </summary>
    public static List<PresetStep> Plan(Preset preset, PresetMode mode, IReadOnlyList<ModGroup> mods, IReadOnlyList<InstalledMod> installed,
        IEnumerable<Preset>? others = null)
    {
        var steps = new List<PresetStep>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ModGroup? ModOf(string key) => mods.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        InstalledMod? InstalledOf(string key) => installed.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        foreach (var entry in preset.Mods)
        {
            var key = ModIdentity.Follow(entry.Key);
            if (!keys.Add(key)) continue;
            var mod = ModOf(key);
            var now = InstalledOf(key);
            var name = mod?.Shown ?? (entry.Name.Length > 0 ? entry.Name : key);

            if (mode == PresetMode.TurnOff)
            {
                steps.Add(now != null
                    ? new PresetStep(PresetAction.Remove, key, name, "", mod, null, entry, now)
                    : new PresetStep(PresetAction.Keep, key, name, L.T("설치되어 있지 않음"), mod, null, entry));
                continue;
            }

            if (mod == null)
            {
                steps.Add(new PresetStep(PresetAction.Skip, key, name, L.T("모드 목록에 없음"), null, null, entry, now));
                continue;
            }
            var target = entry.Pinned && entry.Version.Length > 0
                ? mod.Versions.FirstOrDefault(v => v.Version.Equals(entry.Version, StringComparison.OrdinalIgnoreCase))
                : mod.Latest;
            if (target == null || target.Missing)
            {
                var fallback = mod.Latest.Missing ? null : mod.Latest;
                var why = entry.Pinned && entry.Version.Length > 0 ? L.F("고정한 {0} 버전 파일이 없음", entry.Version) : L.T("파일 없음");
                steps.Add(new PresetStep(PresetAction.Skip, key, name, why, mod, null, entry, now, entry.Pinned ? fallback : null));
                continue;
            }

            if (now == null)
                steps.Add(new PresetStep(PresetAction.Install, key, name, target.Version, mod, target, entry));
            else if (!now.ZipName.Equals(target.FileName, StringComparison.OrdinalIgnoreCase))
                steps.Add(new PresetStep(PresetAction.Update, key, name, $"{now.Version} → {target.Version}", mod, target, entry, now));
            else if (!SameSet(entry.Options, now.Installs[^1].Options) || !SameSet(entry.Excluded, now.Installs[^1].Excluded))
                steps.Add(new PresetStep(PresetAction.Reinstall, key, name, L.T("옵션·뺀 파일이 다름"), mod, target, entry, now));
            else
                steps.Add(new PresetStep(PresetAction.Keep, key, name, target.Version, mod, target, entry, now));
        }

        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (mode != PresetMode.TurnOff)
            foreach (var rival in (others ?? Array.Empty<Preset>()).Where(o => SameGroup(o, preset)))
                foreach (var entry in rival.Mods)
                {
                    var key = ModIdentity.Follow(entry.Key);
                    if (keys.Contains(key) || InstalledOf(key) is not { } now || !removed.Add(key)) continue;
                    steps.Add(new PresetStep(PresetAction.Remove, key, ModOf(key)?.Shown ?? now.Name,
                        L.F("교체 그룹 {0}: {1}", rival.Group.Trim(), rival.Name), ModOf(key), null, entry, now));
                }
        if (mode == PresetMode.Match)
            foreach (var other in installed.Where(m => !keys.Contains(m.Key) && !removed.Contains(m.Key)))
                steps.Add(new PresetStep(PresetAction.Remove, other.Key, ModOf(other.Key)?.Shown ?? other.Name, "", ModOf(other.Key), null, null, other));
        return steps;
    }

    /// <summary>
    /// How many of the preset's own mods are not as it keeps them now: missing, another version, other options,
    /// or gone. None means it is on. Mods outside it do not count, so a small preset can be on beside others.
    /// </summary>
    public static int Differences(Preset preset, IReadOnlyList<ModGroup> mods, IReadOnlyList<InstalledMod> installed) =>
        Plan(preset, PresetMode.TurnOn, mods, installed).Count(s => s.Action != PresetAction.Keep);

    /// <summary>None of the preset's mods is installed.</summary>
    public static bool IsOff(Preset preset, IReadOnlyList<InstalledMod> installed) =>
        preset.Mods.All(e => !installed.Any(m => m.Key.Equals(ModIdentity.Follow(e.Key), StringComparison.OrdinalIgnoreCase)));
}
