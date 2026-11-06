using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace PBZModManager;

public enum ChangeKind { Add, Overwrite, Same, Keep }

public sealed record PlanItem(string Path, ChangeKind Kind, string NewHash, string? OldHash, long Size);

public sealed class InstallPlan
{
    public required string ZipPath { get; init; }
    public List<PlanItem> Items { get; } = new();
    public List<string> NewDirs { get; } = new();
    public List<string> Warnings { get; } = new();
    /// <summary>Files of the installed version that the archive no longer has. An update removes them.</summary>
    public List<StaleFile> Stale { get; } = new();
    /// <summary>The installed version this replaces, or null when the mod is not installed.</summary>
    public string? ReplacesZip { get; set; }
    /// <summary>Where files packaged for Mods Menu go instead, in the loader's mods folder.</summary>
    public HashSet<string> Relocated { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>How many of the archive's files each placement rule handled.</summary>
    public Dictionary<Placement, int> Placements { get; } = new();
    /// <summary>Mods Menu copies left out because the archive also has the mod in the loader's folder.</summary>
    public int SkippedCopies => Placements.GetValueOrDefault(Placement.OtherCopyLeftOut);
    /// <summary>Files of a UE4SS package placed beside PBZ.exe, where it has to be.</summary>
    public int PlacedBesideGame => Placements.GetValueOrDefault(Placement.Ue4ssPackage);
    /// <summary>UE4SS mods in the archive, by folder name.</summary>
    public SortedSet<string> Ue4ssMods { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The archive's sets of options, of which one each is installed.</summary>
    public List<OptionGroup> Options { get; } = new();
    /// <summary>The option folders this plan installs, one per set.</summary>
    public List<string> Chosen { get; } = new();
    /// <summary>Files the user unticked in the preview, by where they would go. They are not installed.</summary>
    public List<string> Excluded { get; } = new();

    public bool HasChanges =>
        NewDirs.Count > 0 || Stale.Count > 0 || Items.Any(i => i.Kind is ChangeKind.Add or ChangeKind.Overwrite);

    public int Count(ChangeKind kind) => Items.Count(i => i.Kind == kind);

    /// <summary>Identifies what the plan would do, to detect changes between preview and install.</summary>
    public string Fingerprint() =>
        string.Join("\n", Items.Select(i => $"{i.Path}|{i.Kind}|{i.NewHash}|{i.OldHash}").Concat(NewDirs)
            .Concat(Stale.Select(s => $"stale|{s.Path}|{s.Hash}")));
}

public sealed record StaleFile(string Path, string Hash, bool Modified);

public enum Placement
{
    AsPackaged, Ue4ssPackage, Pak, Ue4ssMod, BesideExe, MovedToLoader,
    // Left out:
    OtherCopyLeftOut, UnchosenLeftOut, PictureLeftOut, FolderLeftOut, UserLeftOut,
}

/// <summary>The main window's place and size in normal state, whether it was maximized, and its splitters.</summary>
public sealed class WindowLayout
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
    public int MainSplit { get; set; }
    public int LeftSplit { get; set; }
    public int RightSplit { get; set; }
}

/// <summary>Which script loader runs the JavaScript mods. Auto follows the one that is installed.</summary>
public enum ScriptRuntime { Auto, Loader, Menu }

public sealed record OptionChoice(string Folder, string Label);

/// <summary>Folders of an archive that install the same files, so only one of them can be installed.</summary>
public sealed record OptionGroup(List<OptionChoice> Options, List<string> SharedFiles);

/// <summary>
/// Places archives made for other mod managers, which hold mod files without the game's folders around
/// them: pak files, UE4SS mods and their extras, and options of which one is picked.
/// </summary>
public static class LooseLayout
{
    public const string PakHome = "ProjectPBZ/Content/Paks/~mods/";
    public const string LogicModsHome = "ProjectPBZ/Content/Paks/LogicMods/";
    public const string Ue4ssModsHome = ModIdentity.Ue4ssHome + "ue4ss/Mods/";
    static readonly string[] PakTypes = { ".pak", ".ucas", ".utoc" };
    static readonly string[] Pictures = { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" };
    // Folders a mod reads from beside PBZ.exe: UUEPBS keeps its presets there.
    static readonly string[] BesideExeFolders = { "UUEPBS Presets/" };
    static readonly System.Text.RegularExpressions.Regex Ue4ssMod = new(
        @"^(?<prefix>(?:.*/)?)(?<name>[^/]+)/Scripts/main\.lua$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>A path the archive already gives inside the game folder.</summary>
    public static bool InGameLayout(string path) =>
        new[] { "ProjectPBZ/", "platform/", "Engine/" }.Any(top => (path + "/").StartsWith(top, StringComparison.OrdinalIgnoreCase));

    static bool IsPak(string path) => PakTypes.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Takes off one folder that wraps a whole archive of loose mod files, such as "SoulVerR AIO v1.02/":
    /// options are told apart by the folders right inside it, and pictures and presets are found at the top.
    /// A wrapper is only taken off when pak files are inside it, and never when it is a UE4SS mod's own folder.
    /// </summary>
    public static Func<string, string> Unwrapper(IReadOnlyCollection<string> files)
    {
        static string Same(string p) => p;
        if (files.Count == 0 || files.Any(f => !f.Contains('/'))) return Same;
        var top = files.First()[..(files.First().IndexOf('/') + 1)];
        if (InGameLayout(top) || files.Any(f => !f.StartsWith(top, StringComparison.OrdinalIgnoreCase))) return Same;
        var inside = files.Select(f => f[top.Length..]).ToList();
        if (!inside.Any(f => !InGameLayout(f) && IsPak(f)) || inside.Any(f => f.StartsWith("Scripts/", StringComparison.OrdinalIgnoreCase))) return Same;
        return p => p.StartsWith(top, StringComparison.OrdinalIgnoreCase) ? p[top.Length..] : p;
    }

    /// <summary>Folders holding a UE4SS mod (its Scripts/main.lua), as folder path ending in a slash → the mod's name.</summary>
    static Dictionary<string, string> Ue4ssModFolders(IEnumerable<string> files)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(f => !InGameLayout(f)))
        {
            var match = Ue4ssMod.Match(file);
            if (match.Success) found[match.Groups["prefix"].Value + match.Groups["name"].Value + "/"] = match.Groups["name"].Value;
        }
        return found;
    }

    /// <summary>
    /// Top folders whose pak files would land on the same names: picking more than one would overwrite.
    /// The author's "(choose one)" label is not needed; the clash itself decides.
    /// </summary>
    public static List<OptionGroup> OptionGroups(IEnumerable<string> files)
    {
        var byFolder = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(f => !InGameLayout(f) && IsPak(f) && f.Contains('/')))
        {
            var top = file[..file.IndexOf('/')];
            if (!byFolder.TryGetValue(top, out var names)) byFolder[top] = names = new(StringComparer.OrdinalIgnoreCase);
            names.Add(Path.GetFileName(file));
        }

        var groups = new List<(HashSet<string> Folders, HashSet<string> Names)>();
        foreach (var (folder, names) in byFolder.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var merged = (Folders: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { folder }, Names: new HashSet<string>(names, StringComparer.OrdinalIgnoreCase));
            foreach (var other in groups.Where(g => g.Names.Overlaps(names)).ToList())
            {
                merged.Folders.UnionWith(other.Folders);
                merged.Names.UnionWith(other.Names);
                groups.Remove(other);
            }
            groups.Add(merged);
        }

        return groups.Where(g => g.Folders.Count > 1)
            .Select(g => new OptionGroup(
                g.Folders.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Select(f => new OptionChoice(f, Label(f))).ToList(),
                g.Names.Where(n => g.Folders.Count(f => byFolder[f].Contains(n)) > 1).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderBy(g => g.Options[0].Folder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static string Label(string folder) =>
        System.Text.RegularExpressions.Regex.Replace(folder, @"\s*\(choose one\)\s*", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

    /// <summary>
    /// Where each path goes. A folder's path ends in a slash. Paths the archive already lays out like the
    /// game folder stay as they are.
    /// </summary>
    public static Func<string, (string? Path, Placement Kind)> For(IReadOnlyCollection<string> files, IReadOnlyCollection<string> unchosen)
    {
        var modFolders = Ue4ssModFolders(files);
        var laidOut = modFolders.Count > 0 || files.Any(f => !InGameLayout(f) && IsPak(f));

        return path =>
        {
            if (InGameLayout(path)) return (path, Placement.AsPackaged);
            if (unchosen.Any(u => path.StartsWith(u + "/", StringComparison.OrdinalIgnoreCase))) return (null, Placement.UnchosenLeftOut);
            foreach (var (folder, name) in modFolders)
                if (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                    return (Ue4ssModsHome + name + "/" + path[folder.Length..], Placement.Ue4ssMod);
            if (BesideExeFolders.FirstOrDefault(f => path.StartsWith(f, StringComparison.OrdinalIgnoreCase)) is { } beside)
            {
                // Some archives lay the folder out again inside it: "UUEPBS Presets/platform/steam/UUEPBS Presets/x.json".
                var rest = path[beside.Length..];
                if (InGameLayout(rest)) return (rest, Placement.BesideExe);
                if (rest.Length == 0 || !files.Any(f => f.StartsWith(beside + "platform/", StringComparison.OrdinalIgnoreCase)))
                    return (ModIdentity.Ue4ssHome + path, Placement.BesideExe);
                return (null, Placement.FolderLeftOut);
            }

            var isDir = path.EndsWith('/');
            // Blueprint mods are read from LogicMods by UE4SS's loader; other paks from ~mods.
            if (!isDir && IsPak(path))
                return ((("/" + path).Contains("/LogicMods/", StringComparison.OrdinalIgnoreCase) ? LogicModsHome : PakHome) + Path.GetFileName(path), Placement.Pak);
            if (laidOut && isDir) return (null, Placement.FolderLeftOut);
            if (laidOut && !path.Contains('/') && Pictures.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                return (null, Placement.PictureLeftOut);
            return (path, Placement.AsPackaged);
        };
    }
}

/// <summary>Works out which mod and version an archive or install is, from its contents and file name.</summary>
public static class ModIdentity
{
    const string ModsPrefix = "ProjectPBZ/PBZModLoader/mods/";
    const string LoaderMarker = "/PBZModLoader/bootstrap.js";
    const string MenuRoot = "ProjectPBZ/Content/JavaScript/PBZModsMenu/";
    const string MenuPrefix = MenuRoot + "Mods/";
    // The loader takes a folder name as the mod's ID and only allows these characters.
    static readonly System.Text.RegularExpressions.Regex LoaderId = new("^[A-Za-z0-9_-]+$");
    public const string LoaderKey = "loader";

    // Download names look like "<title> <page id> <version> <timestamp> <random>.zip".
    static readonly System.Text.RegularExpressions.Regex DownloadName = new(
        @"^(?<title>.+?) (?<page>\d+) (?<version>\S+) (?<stamp>\d{4}-\d{2}-\d{2}T\d{2}-\d{2}Z) \S+$");
    static readonly System.Text.RegularExpressions.Regex VersionInText = new(
        @"(?:^|[\s_-])v?(?<version>\d+(?:\.\d+)+[\w.-]*)");

    /// <summary>The display name, version and timestamp a file name carries. Any of the last two may be empty.</summary>
    public static (string Name, string Version, string Stamp, string PageId) ParseFileName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var download = DownloadName.Match(stem);
        var text = download.Success ? download.Groups["title"].Value : stem;
        var inText = VersionInText.Match(text);
        var name = (inText.Success ? text[..inText.Index] : text).TrimEnd(' ', '-', '_');
        if (name.Length == 0) name = text;
        var version = download.Success ? download.Groups["version"].Value : inText.Success ? inText.Groups["version"].Value : "";
        return (name, version, download.Success ? download.Groups["stamp"].Value : "", download.Success ? download.Groups["page"].Value : "");
    }

    /// <summary>The title part of a download's name, such as "PBZMapMarkers 1.8.1 Fog".</summary>
    public static string TitleOf(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var download = DownloadName.Match(stem);
        return download.Success ? download.Groups["title"].Value : stem;
    }

    /// <summary>
    /// What makes two archives the same mod. A Nexus download names its page and the mod: the same page and
    /// name are one mod even when a version moves to another tool, as Map Collectible Tracker went from
    /// PBZModLoader to UE4SS. Without a page, the folder the mod installs into decides, then the file name.
    /// </summary>
    public static string KeyOf(IEnumerable<string> paths, string fileName)
    {
        var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (path.EndsWith(LoaderMarker, StringComparison.OrdinalIgnoreCase)) return LoaderKey;
            if (MenuModId(path) is { } menuId) ids.Add(menuId);
            if (!path.StartsWith(ModsPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = path[ModsPrefix.Length..];
            var slash = rest.IndexOf('/');
            if (slash > 0) ids.Add(rest[..slash]);
        }
        var parsed = ParseFileName(fileName);
        if (parsed.PageId.Length > 0) return $"page:{parsed.PageId}:{parsed.Name.ToLowerInvariant()}";
        return ids.Count > 0
            ? "mods:" + string.Join(",", ids).ToLowerInvariant()
            : "name:" + parsed.Name.ToLowerInvariant();
    }

    /// <summary>The mod a path in Mods Menu's mods folder belongs to, when the loader could run it under that name.</summary>
    static string? MenuModId(string path)
    {
        if (!path.StartsWith(MenuPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = path[MenuPrefix.Length..];
        var slash = rest.IndexOf('/');
        return slash > 0 && LoaderId.IsMatch(rest[..slash]) ? rest[..slash] : null;
    }

    public const string LoaderBootstrap = "ProjectPBZ/Content/JavaScript/PBZModLoader/bootstrap.js";
    public const string MenuBootstrap = MenuRoot + "bootstrap.js";
    /// <summary>Where UE4SS lives: beside PBZ.exe.</summary>
    public const string Ue4ssHome = "platform/steam/";

    /// <summary>
    /// The folder of an archive that holds UE4SS (dwmapi.dll beside a ue4ss folder) packaged to be extracted
    /// next to PBZ.exe rather than into the game folder. "" is the archive's top; null means no such folder.
    /// </summary>
    public static string? Ue4ssBase(IReadOnlyCollection<string> files)
    {
        foreach (var file in files)
        {
            if (!Path.GetFileName(file).Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase)) continue;
            var dir = file[..^"dwmapi.dll".Length];
            if (dir.StartsWith("platform/", StringComparison.OrdinalIgnoreCase) || dir.StartsWith("ProjectPBZ/", StringComparison.OrdinalIgnoreCase)) continue;
            if (files.Any(f => f.StartsWith(dir + "ue4ss/", StringComparison.OrdinalIgnoreCase))) return dir;
        }
        return null;
    }

    /// <summary>
    /// Where an archive's paths install for the loader in use. For PBZModLoader, mods packaged for Mods Menu go
    /// to the loader's mods folder, which runs the same kind of script. Mods Menu runs the loader's mods folder
    /// where it is, so for it loader mods stay put. Either way, a mod the archive has for both keeps only the
    /// copy for the loader in use, since both would start. A loader's own archive never moves. Null means the
    /// path is left out; a folder's path ends in a slash.
    /// </summary>
    public static Func<string, string?> InstallLayout(IEnumerable<string> files, ScriptRuntime target)
    {
        files = files.ToList();
        if (target == ScriptRuntime.Auto
            || files.Any(f => f.Equals(MenuBootstrap, StringComparison.OrdinalIgnoreCase) || f.Equals(LoaderBootstrap, StringComparison.OrdinalIgnoreCase)))
            return path => path;
        var forLoader = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var forMenu = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (MenuModId(file) is { } menuId) forMenu.Add(menuId);
            if (!file.StartsWith(ModsPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var slash = file.IndexOf('/', ModsPrefix.Length);
            if (slash > ModsPrefix.Length) forLoader.Add(file[ModsPrefix.Length..slash]);
        }

        if (target == ScriptRuntime.Menu)
            return path =>
            {
                if (!path.StartsWith(ModsPrefix, StringComparison.OrdinalIgnoreCase)) return path;
                var rest = path[ModsPrefix.Length..];
                var slash = rest.IndexOf('/');
                return slash > 0 && forMenu.Contains(rest[..slash]) ? null : path;
            };

        return path =>
        {
            // Mods Menu's own folders are only needed for what is put in them.
            if (path.Equals(MenuRoot, StringComparison.OrdinalIgnoreCase) || path.Equals(MenuPrefix, StringComparison.OrdinalIgnoreCase)) return null;
            if (MenuModId(path) is not { } id) return path;
            return forLoader.Contains(id) ? null : ModsPrefix + path[MenuPrefix.Length..];
        };
    }

    /// <summary>The tools a user can mark a mod as made for.</summary>
    public static readonly string[] Tools =
    {
        "PBZModLoader", "Mods Menu", "PBZModLoader · Mods Menu", "UE4SS", "pak",
        "PBZModLoader 본체", "Mods Menu 본체", "UE4SS 본체", "기타",
    };

    /// <summary>A tool name as shown: the stored names are Korean, and each part is shown in the current language.</summary>
    public static string ToolLabel(string tool) => string.Join(" · ", tool.Split(" · ").Select(L.T));

    /// <summary>What an archive's contents say it is made for, such as "UE4SS" or "Mods Menu"; empty when they do not say.</summary>
    public static string ToolOf(IReadOnlyCollection<string> paths)
    {
        if (paths.Any(p => p.EndsWith(LoaderMarker, StringComparison.OrdinalIgnoreCase))) return "PBZModLoader 본체";
        if (paths.Any(p => p.Equals(MenuBootstrap, StringComparison.OrdinalIgnoreCase))) return "Mods Menu 본체";
        if (Ue4ssBase(paths) != null || paths.Any(p => p.Equals(Ue4ssHome + "dwmapi.dll", StringComparison.OrdinalIgnoreCase))) return "UE4SS 본체";

        var parts = new List<string>();
        var loader = paths.Any(p => p.StartsWith(ModsPrefix, StringComparison.OrdinalIgnoreCase));
        var menu = paths.Any(p => MenuModId(p) != null);
        if (loader && menu) parts.Add("PBZModLoader · Mods Menu");
        else if (loader) parts.Add("PBZModLoader");
        else if (menu) parts.Add("Mods Menu");
        if (paths.Any(p => p.StartsWith(Ue4ssHome + "ue4ss/Mods/", StringComparison.OrdinalIgnoreCase)
                || System.Text.RegularExpressions.Regex.IsMatch(p, @"(^|/)[^/]+/Scripts/main\.lua$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            parts.Add("UE4SS");
        if (paths.Any(p => new[] { ".pak", ".ucas", ".utoc" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))) parts.Add("pak");
        return string.Join(" · ", parts);
    }

    /// <summary>The identity and the tool of an archive, read in one pass.</summary>
    public static (string Key, string Tool) ReadZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var paths = zip.Entries.Where(e => !e.FullName.EndsWith('/')).Select(e => e.FullName.Replace('\\', '/')).ToList();
        return (KeyOf(paths, Path.GetFileName(zipPath)), ToolOf(paths));
    }

    public static string KeyOfZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return KeyOf(zip.Entries.Where(e => !e.FullName.EndsWith('/')).Select(e => e.FullName.Replace('\\', '/')), Path.GetFileName(zipPath));
    }

    public static string KeyOfTx(Tx tx) =>
        KeyOf(tx.Files.Where(f => !f.Deleted).Select(f => f.Path).Concat(tx.Unchanged.Select(u => u.Path)), tx.ZipName);

    /// <summary>Numeric parts compare as numbers; a pre-release sorts before its release. Positive means a is newer.</summary>
    public static int CompareVersions(string a, string b)
    {
        static (long[] Parts, string Pre) Split(string v)
        {
            var dash = v.IndexOf('-');
            var core = dash >= 0 ? v[..dash] : v;
            var parts = core.TrimStart('v', 'V').Split('.').Select(p => long.TryParse(p, out var n) ? n : 0).ToArray();
            return (parts, dash >= 0 ? v[(dash + 1)..] : "");
        }

        var (pa, prea) = Split(a);
        var (pb, preb) = Split(b);
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var c = (i < pa.Length ? pa[i] : 0).CompareTo(i < pb.Length ? pb[i] : 0);
            if (c != 0) return c;
        }
        if (prea.Length == 0 && preb.Length > 0) return 1;
        if (prea.Length > 0 && preb.Length == 0) return -1;
        return string.Compare(prea, preb, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Compares two file names by version, then by download timestamp.</summary>
    public static int CompareFiles(string fileA, string fileB)
    {
        var a = ParseFileName(fileA);
        var b = ParseFileName(fileB);
        var byVersion = CompareVersions(a.Version, b.Version);
        return byVersion != 0 ? byVersion : string.CompareOrdinal(a.Stamp, b.Stamp);
    }
}

public sealed record ModVersion(string Path, string FileName, string Version)
{
    /// <summary>What the archive's contents say it is made for; see <see cref="ModIdentity.ToolOf"/>.</summary>
    public string Tool { get; init; } = "";
    /// <summary>The archive is no longer in the MOD folder.</summary>
    public bool Missing { get; init; }
}

public sealed record NexusLink(string Game, string ModId)
{
    public string PageUrl => $"https://www.nexusmods.com/{Game}/mods/{ModId}";

    /// <summary>The page's Files tab, on one file's download when its number is known.</summary>
    public string DownloadUrl(string fileId) =>
        fileId.Length > 0 ? $"{PageUrl}?tab=files&file_id={Uri.EscapeDataString(fileId)}" : $"{PageUrl}?tab=files";
}

public sealed record NexusFile(string Name, string Version, string Category, string FileId = "");

/// <summary>What Nexus Mods said about a mod the last time the user checked.</summary>
public sealed class RemoteInfo
{
    public string Version { get; set; } = "";
    /// <summary>True when a file with this mod's name gave the version; false when only the page's version was available.</summary>
    public bool MatchedFile { get; set; }
    public string Status { get; set; } = "";
    /// <summary>The category the mod has on Nexus.</summary>
    public string Category { get; set; } = "";
    /// <summary>The file to send the user to. Empty in results saved before this existed, or when only the page's version was known.</summary>
    public string FileId { get; set; } = "";
    /// <summary>Where it came from: empty for Nexus, "github" for GitHub releases.</summary>
    public string Source { get; set; } = "";
    /// <summary>The page to download the newest version from, for sources other than Nexus.</summary>
    public string PageUrl { get; set; } = "";
    /// <summary>The version of the newest local archive as the source tells it by its checksum, when its name has none.</summary>
    public string LocalVersion { get; set; } = "";
    /// <summary>What Nexus files this mod's file under: MAIN, OPTIONAL, MISCELLANEOUS... Empty when not known.</summary>
    public string FileCategory { get; set; } = "";
    public DateTime CheckedAt { get; set; }
}

/// <summary>Takes the install steps out of a Nexus description, for the mod's memo.</summary>
public static class InstallNotes
{
    /// <summary>How install steps began when they were still put into the memo.</summary>
    public const string OldMemoHeading = "[Nexus 설명의 설치 방법]";

    // Only a runaway section is cut: the section ends at the next heading anyway, and long ones are read in their own window.
    const int MaxLines = 300;
    const int MaxLength = 20000;
    static readonly string[] Starts = { "install", "설치", "安装", "インストール" };
    static readonly string[] NotStarts = { "uninstall", "卸载", "제거", "アンインストール" };
    static readonly string[] Stops =
    {
        "uninstall", "removal", "remove", "credit", "known issue", "changelog", "change log", "faq", "permission",
        "support", "donat", "what's new", "compatib", "need help", "troubleshoot", "how to use", "usage", "feature", "which version",
        "卸载", "제거", "사용법", "已知问题", "アンインストール", "既知",
    };

    /// <summary>Turns BBCode and HTML into plain lines: links keep their text, pictures go.</summary>
    public static string PlainText(string description)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(description, @"<br\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[img[^\]]*\].*?\[/img\]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[/\*\]", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[\*\]", "• ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[/?[a-zA-Z]+(=[^\]]*)?\]", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", "");
        text = System.Net.WebUtility.HtmlDecode(text).Replace("\r", "").Replace("﻿", "").Replace("​", "");
        return System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
    }

    /// <summary>
    /// The section under the first heading about installing, up to the next heading about something else.
    /// Empty when the description has none.
    /// </summary>
    public static string Extract(string description)
    {
        var lines = PlainText(description).Split('\n').Select(l => l.TrimEnd()).ToList();
        // Descriptions written in several languages have a section for each: the one in the program's
        // language is taken, else the English one, else the first.
        var headings = lines.Select((l, i) => (Line: l, Index: i)).Where(x => IsHeading(x.Line, Starts) && !IsHeading(x.Line, NotStarts)).ToList();
        if (headings.Count == 0) return "";
        int start = headings.OrderBy(x => LanguageOf(x.Line) == L.Current ? 0 : LanguageOf(x.Line) == "en" ? 1 : 2).ThenBy(x => x.Index).First().Index;

        var taken = new List<string> { lines[start].Trim() };
        for (int i = start + 1; i < lines.Count && taken.Count < MaxLines; i++)
        {
            var line = lines[i];
            // Numbered steps such as "2. install this mod" also look like headings, so only a new topic ends the section.
            if (IsHeading(line, Stops) || IsDivider(line)) break;
            // Descriptions often put a blank line after every line; the memo keeps them together.
            if (line.Trim().Length == 0) continue;
            taken.Add(line.Trim());
        }
        var notes = string.Join("\n", taken).Trim();
        return notes.Length > MaxLength ? notes[..MaxLength].TrimEnd() + "…" : notes;
    }

    /// <summary>A rule of dashes or equals signs, or a section title such as "【English Description】": where another part begins.</summary>
    static bool IsDivider(string line)
    {
        var bare = line.Trim();
        return System.Text.RegularExpressions.Regex.IsMatch(bare, @"^[-=_—─━~*#]{5,}") || bare.StartsWith('【');
    }

    /// <summary>The language a heading is written in, by its letters: ko, ja, zh, or en for the rest.</summary>
    static string LanguageOf(string text)
    {
        if (text.Any(c => c is >= '\uAC00' and <= '\uD7A3')) return "ko";
        if (text.Any(c => c is >= '\u3040' and <= '\u30FF')) return "ja";
        if (text.Any(c => c is >= '\u4E00' and <= '\u9FFF')) return "zh";
        return "en";
    }

    /// <summary>A short line, once list marks and numbering are dropped, that names one of the words.</summary>
    static bool IsHeading(string line, string[] words)
    {
        var bare = line.Trim().TrimStart('#', '◆', '▸', '■', '•', '*', '-', ' ').TrimEnd(':', '：', ' ');
        bare = System.Text.RegularExpressions.Regex.Replace(bare, @"^\d+[.)]\s*", "");
        return bare.Length is > 0 and <= 40 && words.Any(w => bare.Contains(w, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Reads public mod data from Nexus Mods. Version checks need no API key, and only ever run when the user asks.</summary>
public static class NexusApi
{
    public const string DefaultGame = "phantombladezero";
    const string Endpoint = "https://api.nexusmods.com/v2/graphql";

    static readonly System.Text.RegularExpressions.Regex PageUrl = new(
        @"nexusmods\.com/(?<game>[a-z0-9-]+)/mods/(?<id>\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    static HttpMessageHandler? handlerForChecks;
    static HttpClient? apiClient;

    static HttpClient Api => apiClient ??= CreateApiClient();

    static HttpClient CreateApiClient()
    {
        var client = handlerForChecks != null ? new HttpClient(handlerForChecks, false) : new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.Add("User-Agent", "PBZModManager/1.3");
        // Nexus's API policy asks every client to identify itself with these two.
        client.DefaultRequestHeaders.Add("Application-Name", "PBZModManager");
        client.DefaultRequestHeaders.Add("Application-Version", "1.3.0");
        return client;
    }

    /// <summary>Answers requests with a stand-in, so checks never reach Nexus.</summary>
    public static void UseHandlerForChecks(HttpMessageHandler handler)
    {
        handlerForChecks = handler;
        apiClient = null;
    }

    /// <summary>What follows the version in a title: the "Fog" of "PBZMapMarkers 1.8.1 Fog".</summary>
    static string VariantOf(string title)
    {
        var match = System.Text.RegularExpressions.Regex.Match(title, @"\d+(?:\.\d+)+\S*\s*(?<rest>.*)$");
        return match.Success ? match.Groups["rest"].Value.Trim().TrimStart('-', ' ') : "";
    }

    /// <summary>Where a mod lives on Nexus: the address the user entered, or else the page number in a download's name.</summary>
    /// <summary>A mod number typed alone, such as "300", as the full address of that mod's page; anything else as it is.</summary>
    public static string FullAddress(string text)
    {
        var bare = text.Trim().TrimStart('#');
        return bare.Length is > 0 and <= 7 && bare.All(char.IsAsciiDigit) && bare != "0"
            ? $"https://www.nexusmods.com/{DefaultGame}/mods/{bare.TrimStart('0')}"
            : text;
    }

    public static NexusLink? LinkFor(string url, IEnumerable<string> fileNames)
    {
        var match = PageUrl.Match(url);
        if (match.Success) return new NexusLink(match.Groups["game"].Value.ToLowerInvariant(), match.Groups["id"].Value);
        var page = fileNames.Select(f => ModIdentity.ParseFileName(f).PageId).FirstOrDefault(p => p.Length > 0);
        return page == null ? null : new NexusLink(DefaultGame, page);
    }

    /// <summary>
    /// The newest version of this mod's own file. One page can carry unrelated files with their own
    /// numbering (a template at 0.2.0 beside a loader at 1.0.0, a 2.0.0 variant beside 1.8.1), so the
    /// file's name decides, and files the author archived do not count.
    /// </summary>
    public static (string Version, bool MatchedFile) ChooseVersion(string localName, IEnumerable<NexusFile> files, string pageVersion)
    {
        var mine = MineOf(localName, files);
        if (mine.Count == 0) return (pageVersion, false);
        return (Newest(mine).Version, true);
    }

    /// <summary>The file to send the user to: the newest of this mod's own, in the variant they have ("Fog" stays "Fog").</summary>
    public static string ChooseFileId(string localName, string localFile, IEnumerable<NexusFile> files)
    {
        var mine = MineOf(localName, files);
        if (mine.Count == 0) return "";
        var newest = Newest(mine);
        var variant = VariantOf(ModIdentity.TitleOf(localFile));
        var sameVariant = mine.FirstOrDefault(f => ModIdentity.CompareVersions(f.Version, newest.Version) == 0
            && VariantOf(f.Name).Equals(variant, StringComparison.OrdinalIgnoreCase));
        return (sameVariant ?? newest).FileId;
    }

    static List<NexusFile> MineOf(string localName, IEnumerable<NexusFile> files) =>
        files.Where(f => f.Version.Length > 0
                && !f.Category.Equals("ARCHIVED", StringComparison.OrdinalIgnoreCase)
                && !f.Category.Equals("REMOVED", StringComparison.OrdinalIgnoreCase)
                && ModIdentity.ParseFileName(f.Name + ".zip").Name.Equals(localName, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>The category of this mod's current file on the page; its old versions do not count.</summary>
    public static string FileCategoryOf(string localName, IEnumerable<NexusFile> files)
    {
        var current = MineOf(localName, files).Where(f => !f.Category.Equals("OLD_VERSION", StringComparison.OrdinalIgnoreCase)).ToList();
        return current.Count > 0 ? Newest(current).Category.ToUpperInvariant() : "";
    }

    static NexusFile Newest(List<NexusFile> files) =>
        files.Aggregate((a, b) => ModIdentity.CompareVersions(b.Version, a.Version) > 0 ? b : a);

    /// <summary>The category a page has on Nexus, empty if it has none. Asked when the user enters an address.</summary>
    public static string GetCategory(NexusLink link) => GetPageInfo(link).Category;

    /// <summary>A page's category and the install steps from its description; either may be empty.</summary>
    public static (string Category, string InstallNotes) GetPageInfo(NexusLink link)
    {
        var body = JsonSerializer.Serialize(new
        {
            query = "query($ids:[CompositeDomainWithIdInput!]!){ legacyModsByDomain(ids:$ids){ nodes { modCategory { name } description } } }",
            variables = new { ids = new[] { new { gameDomain = link.Game, modId = int.Parse(link.ModId) } } },
        });
        var json = Post(body);
        using var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("data").GetProperty("legacyModsByDomain").GetProperty("nodes");
        var description = nodes.GetArrayLength() > 0 && nodes[0].TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString() ?? "" : "";
        return (ParseCategory(json), InstallNotes.Extract(description));
    }

    public static string ParseCategory(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("data").GetProperty("legacyModsByDomain").GetProperty("nodes");
        return nodes.GetArrayLength() > 0
            && nodes[0].TryGetProperty("modCategory", out var category) && category.ValueKind == JsonValueKind.Object
            && category.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
            ? name.GetString() ?? "" : "";
    }

    /// <summary>Asks Nexus about the given mods. A mod Nexus does not know is simply missing from the result.</summary>
    public static Dictionary<string, RemoteInfo> Check(IReadOnlyList<(string Key, string LocalName, string LocalFile, NexusLink Link)> mods)
    {
        var results = new Dictionary<string, RemoteInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in mods.GroupBy(m => m.Link.Game))
        {
            var gameBody = JsonSerializer.Serialize(new
            {
                query = "query($d:String!){ game(domainName:$d){ id } }",
                variables = new { d = game.Key },
            });
            using var gameDoc = JsonDocument.Parse(Post(gameBody));
            var data = gameDoc.RootElement.GetProperty("data");
            if (data.GetProperty("game").ValueKind != JsonValueKind.Object) continue;
            var gameId = data.GetProperty("game").GetProperty("id").ToString();

            var ids = game.Select(m => m.Link.ModId).Distinct().ToList();
            var aliases = string.Concat(ids.Select(id => $"m{id}: modFiles(modId:\"{id}\", gameId:\"{gameId}\"){{ fileId name version category }} "));
            var body = JsonSerializer.Serialize(new
            {
                query = "query($ids:[CompositeDomainWithIdInput!]!){ legacyModsByDomain(ids:$ids){ nodes { modId version status modCategory { name } } } " + aliases + "}",
                variables = new { ids = ids.Select(id => new { gameDomain = game.Key, modId = int.Parse(id) }) },
            });
            foreach (var pair in ParseResponse(Post(body), game.Select(m => (m.Key, m.LocalName, m.LocalFile, m.Link.ModId)).ToList()))
                results[pair.Key] = pair.Value;
        }
        return results;
    }

    public static Dictionary<string, RemoteInfo> ParseResponse(string json, IReadOnlyList<(string Key, string LocalName, string LocalFile, string ModId)> mods)
    {
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");

        string Text(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";

        var pages = data.GetProperty("legacyModsByDomain").GetProperty("nodes").EnumerateArray()
            .ToDictionary(n => Text(n, "modId"), n => (
                Version: Text(n, "version"),
                Status: Text(n, "status"),
                Category: n.TryGetProperty("modCategory", out var c) && c.ValueKind == JsonValueKind.Object ? Text(c, "name") : ""));

        var results = new Dictionary<string, RemoteInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, localName, localFile, modId) in mods)
        {
            var files = data.TryGetProperty("m" + modId, out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Select(f => new NexusFile(Text(f, "name"), Text(f, "version"), Text(f, "category"), Text(f, "fileId"))).ToList()
                : new List<NexusFile>();
            pages.TryGetValue(modId, out var page);
            var (version, matched) = ChooseVersion(localName, files, page.Version ?? "");
            if (version.Length == 0) continue;
            results[key] = new RemoteInfo
            {
                Version = version, MatchedFile = matched, Status = page.Status ?? "", Category = page.Category ?? "",
                FileId = ChooseFileId(localName, localFile, files), FileCategory = FileCategoryOf(localName, files), CheckedAt = DateTime.Now,
            };
        }
        return results;
    }

    static string Post(string body)
    {
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = Api.PostAsync(Endpoint, content).GetAwaiter().GetResult();
        var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(L.F("Nexus 응답 오류 ({0}). 잠시 후 다시 시도해 주세요.", (int)response.StatusCode));
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0
            && !doc.RootElement.TryGetProperty("data", out _))
            throw new InvalidOperationException(L.T("Nexus가 요청을 처리하지 못했습니다: ") + errors[0].GetProperty("message").GetString());
        return text;
    }

    /// <summary>A plain GET through the same client, so checks can stand in for it too.</summary>
    internal static string Get(string url, string accept)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd(accept);
        using var response = Api.Send(request);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(L.F("응답 오류 ({0}). 잠시 후 다시 시도해 주세요.", (int)response.StatusCode));
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }
}

public sealed record GitHubLink(string Owner, string Repo)
{
    public string ReleasesUrl => $"https://github.com/{Owner}/{Repo}/releases";
}

/// <summary>
/// Newest releases of mods kept on GitHub. A file name often carries no version (UUEPBS.zip), so the archive's
/// SHA-256 is matched against the checksums GitHub lists for each release file to tell which version it is.
/// Only asked from the version check button; without a token GitHub allows 60 requests an hour.
/// </summary>
public static class GitHubApi
{
    static readonly System.Text.RegularExpressions.Regex Repo = new(
        @"github\.com/(?<owner>[A-Za-z0-9-]+)/(?<repo>[A-Za-z0-9._-]+?)(?:\.git)?(?:[/?#]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static GitHubLink? LinkFor(string url)
    {
        var match = Repo.Match(url);
        return match.Success ? new GitHubLink(match.Groups["owner"].Value, match.Groups["repo"].Value) : null;
    }

    /// <summary>What GitHub says about the repository, or null when it lists no published release.</summary>
    public static RemoteInfo? Check(GitHubLink link, string? localZip)
    {
        var json = NexusApi.Get($"https://api.github.com/repos/{link.Owner}/{link.Repo}/releases?per_page=30", "application/vnd.github+json");
        string? hash = null;
        if (localZip != null && File.Exists(localZip))
        {
            using var stream = File.OpenRead(localZip);
            hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        }
        return ParseReleases(json, hash, link);
    }

    static string Clean(string tag) => tag.Trim().TrimStart('v', 'V');

    public static RemoteInfo? ParseReleases(string json, string? localSha256, GitHubLink link)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        var releases = doc.RootElement.EnumerateArray().Where(r => !Flag(r, "draft")).ToList();
        var latest = releases.FirstOrDefault(r => !Flag(r, "prerelease"));
        if (latest.ValueKind != JsonValueKind.Object || Text(latest, "tag_name").Length == 0) return null;

        var local = "";
        if (localSha256 != null)
            foreach (var release in releases)
            {
                if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
                if (assets.EnumerateArray().Any(a => Text(a, "digest").Equals("sha256:" + localSha256, StringComparison.OrdinalIgnoreCase)))
                {
                    local = Clean(Text(release, "tag_name"));
                    break;
                }
            }

        var page = Text(latest, "html_url");
        return new RemoteInfo
        {
            Version = Clean(Text(latest, "tag_name")),
            MatchedFile = true,
            Status = "published",
            Source = "github",
            PageUrl = page.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) ? page : link.ReleasesUrl,
            LocalVersion = local,
            CheckedAt = DateTime.Now,
        };
    }
}

/// <summary>Whether a newer release of this manager is out. Asked once when the window opens.</summary>
public static class AppUpdate
{
    public static readonly GitHubLink Project = new("GameStdio77", "PBZModManager");

    /// <summary>The newest release when it is newer than <paramref name="running"/>; null when not, or when GitHub cannot say.</summary>
    public static (string Version, string Url)? Check(string running)
    {
        try
        {
            var json = NexusApi.Get($"https://api.github.com/repos/{Project.Owner}/{Project.Repo}/releases?per_page=10", "application/vnd.github+json");
            return Newer(GitHubApi.ParseReleases(json, null, Project), running);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            return null;
        }
    }

    public static (string Version, string Url)? Newer(RemoteInfo? latest, string running) =>
        latest != null && Version.TryParse(Pad(latest.Version), out var found) && Version.TryParse(Pad(running), out var mine) && found > mine
            ? (latest.Version, latest.PageUrl)
            : null;

    /// <summary>"1.1" reads as 1.1.0, so it compares with "1.1.0" as the same.</summary>
    static string Pad(string version)
    {
        var core = version.Split('-', '+')[0];
        return core.Count(c => c == '.') == 0 ? core + ".0" : core;
    }
}

/// <summary>A line of the installed list: a mod, or a separator with the user's text.</summary>
public sealed record InstalledRow(InstalledMod? Mod, string? SeparatorId, string Text)
{
    public bool IsSeparator => Mod == null;
    public string Key => Mod?.Key ?? "sep:" + SeparatorId;
}

/// <summary>One installed mod, whatever number of updates it has been through.</summary>
public sealed class InstalledMod
{
    public required string Key { get; init; }
    /// <summary>This mod's installs, oldest first. The last one is what is in effect.</summary>
    public required List<Tx> Installs { get; init; }

    public string ZipName => Installs[^1].ZipName;
    public string Name => ModIdentity.ParseFileName(ZipName).Name;
    public string Version => ModIdentity.ParseFileName(ZipName).Version;
}

/// <summary>All listed versions of one mod, newest first, and what is currently installed.</summary>
public sealed class ModGroup
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    /// <summary>What the newest archive's contents say the mod is made for.</summary>
    public string Tool { get; init; } = "";
    public required List<ModVersion> Versions { get; init; }
    /// <summary>This mod's installs, oldest first. The last one is what is in effect.</summary>
    public required List<Tx> Installs { get; init; }

    public ModVersion Latest => Versions[0];
    public string? InstalledFile => Installs.LastOrDefault()?.ZipName;
    public string? InstalledVersion => InstalledFile == null ? null : ModIdentity.ParseFileName(InstalledFile).Version;
    public bool UpdateAvailable => InstalledFile != null && ModIdentity.CompareFiles(Latest.FileName, InstalledFile) > 0;
}

public sealed class TxFile
{
    public string Path { get; set; } = "";
    /// <summary>True when the file existed before the install and a backup was taken.</summary>
    public bool Overwrote { get; set; }
    /// <summary>An update removed this file because the new version no longer has it. Undoing brings it back.</summary>
    public bool Deleted { get; set; }
    public string InstalledHash { get; set; } = "";
    public string? BackupHash { get; set; }
}

/// <summary>
/// A file the archive also contains but the install left alone (identical, or kept as configured).
/// Recorded so the file survives when the install that actually wrote it is removed.
/// </summary>
public sealed class TxUnchanged
{
    public string Path { get; set; } = "";
    /// <summary>The version this archive carries.</summary>
    public string Hash { get; set; } = "";
}

public sealed class Tx
{
    public const string Installing = "installing";
    public const string Installed = "installed";
    public const string Undoing = "undoing";

    public int Id { get; set; }
    public string ZipName { get; set; } = "";
    public DateTime Time { get; set; }
    public string State { get; set; } = Installing;
    public List<TxFile> Files { get; set; } = new();
    public List<TxUnchanged> Unchanged { get; set; } = new();
    public List<string> CreatedDirs { get; set; } = new();
    /// <summary>The option folders picked for this install. Empty for archives without options.</summary>
    public List<string> Options { get; set; } = new();
    /// <summary>Files the user left out of this install, by where they would have gone.</summary>
    public List<string> Excluded { get; set; } = new();
}

public enum StepKind { None, Delete, Restore, StashDelete, StashRestore, KeepForLater }

public sealed record RollbackStep(int TxId, string Path, StepKind Kind);

public sealed class RollbackPlan
{
    public int TargetId { get; init; }
    public List<int> TxIds { get; } = new();
    public List<RollbackStep> Steps { get; } = new();
    /// <summary>Files that appeared inside install-created folders after the install (caches, logs, user mods).</summary>
    public List<string> ExtraFiles { get; } = new();
    public List<string> RemovedDirs { get; } = new();
    /// <summary>Folders the removed install created that a later install still needs.</summary>
    public List<string> KeptDirs { get; } = new();
    public bool RemovesLoaderUnderOtherMods { get; set; }
}

public sealed record FileStatus(TxFile File, string State);

/// <summary>The user's own notes about an archive. Independent of whether it is installed.</summary>
public sealed class ModMeta
{
    public string Category { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>The user's short note, one line; it shows in the installed list too.</summary>
    public string Memo { get; set; } = "";
    /// <summary>The install steps taken from the mod's page, kept apart from the user's note.</summary>
    public string InstallNotes { get; set; } = "";
    /// <summary>The tool the mod is made for, as the user set it; empty follows what the archive holds.</summary>
    public string Tool { get; set; } = "";
    /// <summary>The version the user gave a file whose name carries none, compared with the page's.</summary>
    public string Version { get; set; } = "";
    /// <summary>The user added this archive to the list. Other files in the folder are not shown.</summary>
    public bool Registered { get; set; }

    public bool IsEmpty() => !Registered && Category.Length == 0 && Url.Length == 0 && Memo.Length == 0 && Tool.Length == 0 && InstallNotes.Length == 0;

    /// <summary>
    /// Brings notes from before install steps had their own place: steps that were put in the memo move out,
    /// and a memo of several lines becomes one.
    /// </summary>
    public bool Migrate()
    {
        bool changed = false;
        if (Memo.StartsWith(PBZModManager.InstallNotes.OldMemoHeading, StringComparison.Ordinal))
        {
            if (InstallNotes.Length == 0) InstallNotes = Memo[PBZModManager.InstallNotes.OldMemoHeading.Length..].Trim();
            Memo = "";
            changed = true;
        }
        if (Memo.Contains('\n') || Memo.Contains('\r'))
        {
            Memo = string.Join(" · ", Memo.Split('\n', '\r').Select(l => l.Trim()).Where(l => l.Length > 0));
            changed = true;
        }
        return changed;
    }
}

public sealed class Manager
{
    // Settings the user may have changed: an update keeps them.
    static readonly string[] KeepIfExists =
    {
        "ProjectPBZ/PBZModLoader/mods.ini",
        ModIdentity.Ue4ssHome + "ue4ss/UE4SS-settings.ini",
        ModIdentity.Ue4ssHome + "ue4ss/Mods/mods.txt",
    };

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public string Root { get; }
    public string ModDir { get; }
    public string StateDir { get; }
    /// <summary>Where the mod archives are kept, inside the records: added files are moved here.</summary>
    public string ArchiveDir { get; }
    public string LeftoversDir { get; }
    string HistoryDir { get; }
    string TmpDir { get; }

    /// <param name="modDir">Where archives and the manager's records are kept: a full path, or a folder name inside the game folder.</param>
    public Manager(string root, string modDir = "[] MOD")
    {
        Root = Path.GetFullPath(root).TrimEnd('\\', '/');
        ModDir = Path.GetFullPath(Path.Combine(Root, modDir)).TrimEnd('\\', '/');
        StateDir = Path.Combine(ModDir, ".manager");
        ArchiveDir = Path.Combine(StateDir, "archives");
        HistoryDir = Path.Combine(StateDir, "history");
        LeftoversDir = Path.Combine(StateDir, "leftovers");
        TmpDir = Path.Combine(StateDir, "tmp");
        Directory.CreateDirectory(HistoryDir);
        Directory.CreateDirectory(LeftoversDir);
        Directory.CreateDirectory(TmpDir);
        Directory.CreateDirectory(ArchiveDir);
        settings = LoadSettings();
        TrimLog();
    }

    const int LogKeepDays = 180;
    const int LogKeepLines = 2000;

    string LogPath => Path.Combine(StateDir, "log.txt");

    /// <summary>
    /// The log is a readable account for the user; the install records are what undo relies on. So old
    /// lines can go: those over half a year old, and beyond the newest 2000.
    /// </summary>
    public void TrimLog(DateTime? now = null)
    {
        try
        {
            if (!File.Exists(LogPath)) return;
            var lines = File.ReadAllLines(LogPath);
            var cutoff = (now ?? DateTime.Now).AddDays(-LogKeepDays);
            var kept = lines
                .Where(l => l.Length < 19 || !DateTime.TryParseExact(l[..19], "yyyy-MM-dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out var at) || at >= cutoff)
                .TakeLast(LogKeepLines)
                .ToArray();
            if (kept.Length == lines.Length) return;
            var tmp = NewTmpPath();
            File.WriteAllLines(tmp, kept);
            File.Move(tmp, LogPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log is only for reading; trimming waits for another start.
        }
    }

    // ---------------------------------------------------------------- settings

    string SettingsPath => Path.Combine(StateDir, "settings.json");

    sealed class Settings
    {
        public ScriptRuntime Runtime { get; set; }
        public WindowLayout? Window { get; set; }
        /// <summary>The game folder last used with this mod folder, found again from here on the next start.</summary>
        public string GameRoot { get; set; } = "";
        /// <summary>The language picked in the window; empty follows Windows.</summary>
        public string Language { get; set; } = "";
    }

    Settings settings;

    /// <summary>The game folder saved in a mod folder's records, read before the manager for it exists.</summary>
    public static string? SavedGameRoot(string modDir)
    {
        try
        {
            var path = Path.Combine(modDir, ".manager", "settings.json");
            return File.Exists(path) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) is { GameRoot.Length: > 0 } s ? s.GameRoot : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The language saved in a mod folder's records, read before any window is made.</summary>
    public static string? SavedLanguage(string modDir)
    {
        try
        {
            var path = Path.Combine(modDir, ".manager", "settings.json");
            return File.Exists(path) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) is { Language.Length: > 0 } s ? s.Language : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public void SaveLanguage(string code)
    {
        settings.Language = code;
        SaveSettings();
    }

    /// <summary>The Steam build's game program, which the launch button starts.</summary>
    public string GameExe => Path.Combine(Root, "platform", "steam", "PBZ.exe");

    /// <summary>Keeps the game folder with the records when it changed.</summary>
    public void RememberGameRoot()
    {
        if (settings.GameRoot.Equals(Root, StringComparison.OrdinalIgnoreCase)) return;
        settings.GameRoot = Root;
        SaveSettings();
    }

    Settings LoadSettings()
    {
        try
        {
            return File.Exists(SettingsPath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) ?? new Settings() : new Settings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            return new Settings();
        }
    }

    void SaveSettings()
    {
        var tmp = NewTmpPath();
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOpts));
        File.Move(tmp, SettingsPath, overwrite: true);
    }

    /// <summary>Where the window was, and how its panes were split, when it last closed.</summary>
    public WindowLayout? Window => settings.Window;

    public void SaveWindow(WindowLayout window)
    {
        settings.Window = window;
        SaveSettings();
    }

    // ---------------------------------------------------------------- script loader

    /// <summary>The script loader the user picked: mods are placed where it runs them.</summary>
    public ScriptRuntime Runtime => settings.Runtime;

    public void SetRuntime(ScriptRuntime runtime)
    {
        var before = settings.Runtime;
        settings.Runtime = runtime;
        try
        {
            SaveSettings();
        }
        catch
        {
            settings.Runtime = before;
            throw;
        }
    }

    public bool LoaderInstalled => File.Exists(Full(ModIdentity.LoaderBootstrap));
    public bool MenuInstalled => File.Exists(Full(ModIdentity.MenuBootstrap));

    /// <summary>
    /// The loader mods are placed for. Left on automatic, it is the one installed; with both or neither,
    /// archives install as they are packaged.
    /// </summary>
    public ScriptRuntime TargetRuntime => Runtime != ScriptRuntime.Auto ? Runtime
        : LoaderInstalled && !MenuInstalled ? ScriptRuntime.Loader
        : MenuInstalled && !LoaderInstalled ? ScriptRuntime.Menu
        : ScriptRuntime.Auto;

    /// <summary>
    /// Installed mods that the current loader choice would place differently, each with the plan that moves
    /// them. Their own options are kept. A mod whose archive is gone from the MOD folder is left out.
    /// </summary>
    public List<(InstalledMod Mod, InstallPlan Plan)> PlansToFollowRuntime()
    {
        var found = new List<(InstalledMod, InstallPlan)>();
        foreach (var mod in ListInstalled())
        {
            var zip = Path.Combine(ArchiveDir, mod.ZipName);
            if (!File.Exists(zip)) continue;
            var plan = BuildPlan(zip);
            var moves = plan.Placements.GetValueOrDefault(Placement.MovedToLoader) + plan.Placements.GetValueOrDefault(Placement.OtherCopyLeftOut);
            if (plan.HasChanges && (moves > 0 || plan.Stale.Count > 0)) found.Add((mod, plan));
        }
        return found;
    }

    // ---------------------------------------------------------------- queries

    /// <summary>Archives on the list: those the user registered, plus anything currently installed.</summary>
    public List<string> ListZips(IReadOnlyDictionary<string, ModMeta> meta)
    {
        var names = meta.Where(p => p.Value.Registered).Select(p => p.Key).Concat(History().Select(t => t.ZipName));
        // A file that went missing stays listed, marked as such, so its notes and installs are not lost from view.
        return names.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => Path.Combine(ArchiveDir, name))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Sends an archive in the MOD folder to the Recycle Bin. The one an installed mod came from is kept: reinstalling
    /// and changing options need it.
    /// </summary>
    public void DeleteZip(string fileName)
    {
        if (History().Any(t => t.ZipName.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(L.F("설치된 버전의 파일은 삭제할 수 없습니다. 먼저 모드를 제거하거나 다른 버전으로 업데이트해 주세요.\n{0}", fileName));
        var path = Path.Combine(ArchiveDir, Path.GetFileName(fileName));
        if (!File.Exists(path)) return;
        if (!SendToRecycleBin(path))
            throw new IOException(L.F("파일을 휴지통으로 보내지 못했습니다: {0}", fileName));
    }

    /// <summary>Moves a file or folder to the Recycle Bin, without asking; true when it is gone from where it was.</summary>
    /// <summary>Checks delete their throwaway files outright instead of filling the user's Recycle Bin.</summary>
    public static bool DeleteInsteadOfRecycleForTests;

    static bool SendToRecycleBin(string path)
    {
        if (DeleteInsteadOfRecycleForTests)
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, true);
            return true;
        }
        var op = new ShFileOp
        {
            Func = 3, // delete
            From = path + "\0\0",
            Flags = 0x0040 | 0x0010 | 0x0004 | 0x0400, // to the Recycle Bin, no questions, no progress, no error box
        };
        return SHFileOperation(ref op) == 0 && !File.Exists(path) && !Directory.Exists(path);
    }

    /// <summary>What the keep folder holds: files that removals set aside instead of deleting.</summary>
    public (int Runs, int Files, long Bytes) LeftoversSize()
    {
        if (!Directory.Exists(LeftoversDir)) return (0, 0, 0);
        var files = Directory.EnumerateFiles(LeftoversDir, "*", SearchOption.AllDirectories).Select(f => new FileInfo(f)).ToList();
        return (Directory.GetDirectories(LeftoversDir).Length, files.Count, files.Sum(f => f.Length));
    }

    /// <summary>Sends everything in the keep folder to the Recycle Bin, where it can still be restored.</summary>
    /// <returns>Names that could not be moved.</returns>
    public List<string> ClearLeftovers()
    {
        var failed = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(LeftoversDir).ToList())
            if (!SendToRecycleBin(entry)) failed.Add(Path.GetFileName(entry));
        return failed;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    struct ShFileOp
    {
        public IntPtr Window;
        public uint Func;
        public string From;
        public string? To;
        public ushort Flags;
        public int AnyAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int SHFileOperation(ref ShFileOp op);

    /// <summary>The list grouped by mod, so several versions of one mod appear as one entry.</summary>
    public List<ModGroup> ListMods(IReadOnlyDictionary<string, ModMeta> meta)
    {
        var history = History();
        var byKey = new Dictionary<string, List<ModVersion>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in ListZips(meta))
        {
            var fileName = Path.GetFileName(path);
            var missing = !File.Exists(path);
            string key, tool;
            if (missing)
            {
                // Gone from the folder: what was installed from it still tells which mod it was.
                var tx = history.LastOrDefault(t => t.ZipName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                (key, tool) = (tx != null ? ModIdentity.KeyOfTx(tx) : ModIdentity.KeyOf(Array.Empty<string>(), fileName), "");
            }
            else
            {
                try
                {
                    (key, tool) = ModIdentity.ReadZip(path);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    (key, tool) = (ModIdentity.KeyOf(Array.Empty<string>(), fileName), "");
                }
            }
            if (!byKey.TryGetValue(key, out var versions)) byKey[key] = versions = new List<ModVersion>();
            versions.Add(new ModVersion(path, fileName, ModIdentity.ParseFileName(fileName).Version) { Tool = tool, Missing = missing });
        }

        return byKey.Select(pair =>
        {
            pair.Value.Sort((x, y) => ModIdentity.CompareFiles(y.FileName, x.FileName));
            return new ModGroup
            {
                Key = pair.Key,
                Name = ModIdentity.ParseFileName(pair.Value[0].FileName).Name,
                Tool = pair.Value[0].Tool,
                Versions = pair.Value,
                Installs = history.Where(t => ModIdentity.KeyOfTx(t).Equals(pair.Key, StringComparison.OrdinalIgnoreCase)).ToList(),
            };
        }).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Validates an archive and makes sure it is in the MOD folder, copying it there if needed.</summary>
    /// <returns>The archive's path in the MOD folder.</returns>
    /// <summary>Archive types taken besides zip. They are turned into a zip when added, and everything after works on that.</summary>
    public static readonly string[] OtherArchiveTypes = { ".rar", ".7z" };

    public static bool CanAdd(string path) =>
        Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase) || OtherArchiveTypes.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public string AddZip(string source, Action<string>? progress = null)
    {
        var full = Path.GetFullPath(source);
        if (!File.Exists(full) || !CanAdd(full))
            throw new InvalidOperationException(L.F("zip, rar, 7z 파일만 추가할 수 있습니다: {0}", Path.GetFileName(full)));
        if (!Path.GetExtension(full).Equals(".zip", StringComparison.OrdinalIgnoreCase)) return AddConverted(full, progress);
        try
        {
            using var zip = ZipFile.OpenRead(full);
        }
        catch (InvalidDataException)
        {
            throw new InvalidOperationException(L.F("올바른 zip 파일이 아닙니다: {0}", Path.GetFileName(full)));
        }

        var dest = Path.Combine(ArchiveDir, Path.GetFileName(full));
        if (string.Equals(full, dest, StringComparison.OrdinalIgnoreCase)) return dest;

        // The same file under another name, such as one fetched earlier by hand, is not added twice;
        // the one dropped goes to the Recycle Bin, as it would have been moved anyway.
        var size = new FileInfo(full).Length;
        var hash = HashFile(full);
        foreach (var existing in Directory.GetFiles(ArchiveDir, "*.zip"))
            if (new FileInfo(existing).Length == size && HashFile(existing) == hash)
            {
                SendToRecycleBin(full);
                return existing;
            }

        if (File.Exists(dest))
            throw new InvalidOperationException(L.F("MOD 폴더에 이름이 같은 다른 파일이 이미 있습니다: {0}", Path.GetFileName(full)));

        MoveIn(full, dest);
        return dest;
    }

    /// <summary>
    /// Moves a file into the archive folder. Across drives it is copied first and the original removed only
    /// once the copy is in place; an original that cannot be removed (still open elsewhere) is left as it is.
    /// </summary>
    void MoveIn(string source, string dest)
    {
        if (string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(dest), StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                File.Move(source, dest);
                return;
            }
            catch (IOException) when (File.Exists(source) && !File.Exists(dest))
            {
                // Held open by another program: copied below instead.
            }
        }
        var tmp = NewTmpPath();
        File.Copy(source, tmp);
        File.Move(tmp, dest);
        try { File.Delete(source); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Moves archives kept in the mod folder by earlier versions into the archive folder. Names stay, so
    /// records, notes and options carry over. Returns how many were moved.
    /// </summary>
    public int MoveArchivesIn(Action<string>? progress = null)
    {
        var found = Directory.GetFiles(ModDir, "*.zip");
        var moved = 0;
        foreach (var path in found)
        {
            progress?.Invoke(L.F("모드 파일을 .manager\\archives로 옮기는 중... {0}/{1}", moved + 1, found.Length));
            var dest = Path.Combine(ArchiveDir, Path.GetFileName(path));
            try
            {
                if (!File.Exists(dest)) MoveIn(path, dest);
                else if (HashFile(dest) == HashFile(path)) SendToRecycleBin(path);
                else continue;
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left where it is; tried again on the next start.
            }
        }
        if (moved > 0) Log($"moved {moved} archive(s) into .manager/archives");
        return moved;
    }

    /// <summary>
    /// Writes a rar or 7z archive's files into a zip of the same name in the archive folder, then sends the
    /// original to the Recycle Bin, so the mod is not kept twice.
    /// </summary>
    string AddConverted(string full, Action<string>? progress)
    {
        var dest = Path.Combine(ArchiveDir, Path.GetFileNameWithoutExtension(full) + ".zip");
        if (!File.Exists(dest))
        {
            var tmp = NewTmpPath();
            try
            {
                using (var archive = SharpCompress.Archives.ArchiveFactory.Open(full))
                using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
                {
                    var total = archive.Entries.Count(e => !e.IsDirectory);
                    var done = 0;
                    // Read in order: a solid rar can only be unpacked from the start.
                    using var reader = archive.ExtractAllEntries();
                    while (reader.MoveToNextEntry())
                    {
                        var name = (reader.Entry.Key ?? "").Replace('\\', '/');
                        if (name.Length == 0) continue;
                        if (reader.Entry.IsDirectory)
                        {
                            zip.CreateEntry(name.TrimEnd('/') + "/");
                            continue;
                        }
                        progress?.Invoke(L.F("압축 형식 바꾸는 중... {0}/{1}", ++done, total));
                        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                        if (reader.Entry.LastModifiedTime is { Year: >= 1980 and < 2108 } time) entry.LastWriteTime = time;
                        using var stream = entry.Open();
                        reader.WriteEntryTo(stream);
                    }
                }
                File.Move(tmp, dest);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The library reports damaged, unsupported or locked archives in many ways; each means the same here.
                try { File.Delete(tmp); } catch (IOException) { }
                throw new InvalidOperationException(L.F("압축 파일을 읽지 못했습니다: {0}\n{1}", Path.GetFileName(full), ex.Message), ex);
            }
        }
        // Its files are in the zip now; the original goes to the Recycle Bin, where it can still be had back.
        SendToRecycleBin(full);
        return dest;
    }

    /// <summary>The pictures in an archive (its preview images, usually), by entry name in archive order.</summary>
    public static List<string> PicturesIn(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            return zip.Entries.Where(e => e.Length > 0 && PictureTypes.Contains(Path.GetExtension(e.FullName), StringComparer.OrdinalIgnoreCase))
                .Select(e => e.FullName).OrderBy(n => n, Comparer<string>.Create(StrCmpLogicalW)).ToList();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new List<string>();
        }
    }

    /// <summary>Compares names the way Explorer sorts them: "(2)" before "(11)".</summary>
    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int StrCmpLogicalW(string a, string b);

    /// <summary>Picture types Windows can show without help.</summary>
    public static readonly string[] PictureTypes = { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };

    /// <summary>Installed transactions, oldest first.</summary>
    public List<Tx> History()
    {
        var list = new List<Tx>();
        foreach (var dir in Directory.GetDirectories(HistoryDir))
        {
            var file = Path.Combine(dir, "manifest.json");
            if (File.Exists(file))
                list.Add(JsonSerializer.Deserialize<Tx>(File.ReadAllText(file), JsonOpts)!);
        }
        return list.OrderBy(t => t.Id).ToList();
    }

    /// <summary>A transaction interrupted by a crash or failure; it must be rolled back before anything else.</summary>
    public Tx? Pending() => History().LastOrDefault(t => t.State != Tx.Installed);

    public static bool GameRunning()
    {
        var processes = Process.GetProcessesByName("PBZ");
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }

    public List<FileStatus> GetFileStatuses(Tx tx)
    {
        var later = History().Where(t => t.Id > tx.Id).SelectMany(t => t.Files)
            .Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return tx.Files.Select(f =>
        {
            if (later.Contains(f.Path)) return new FileStatus(f, L.T("이후 설치로 덮어씀"));
            var cur = CurrentHash(f.Path);
            if (f.Deleted) return new FileStatus(f, cur == null ? L.T("새 버전에 없어 제거됨") : L.T("변경됨"));
            if (cur == null) return new FileStatus(f, L.T("없음"));
            return new FileStatus(f, cur == f.InstalledHash ? L.T("정상") : L.T("변경됨"));
        }).ToList();
    }

    // ---------------------------------------------------------------- installed mods

    string OrderPath => Path.Combine(StateDir, "order.json");

    /// <summary>Installed mods in the user's order. Mods the order does not name yet follow, oldest install first.</summary>
    public List<InstalledMod> ListInstalled()
    {
        var mods = History().GroupBy(ModIdentity.KeyOfTx, StringComparer.OrdinalIgnoreCase)
            .Select(g => new InstalledMod { Key = g.Key, Installs = g.OrderBy(t => t.Id).ToList() })
            .OrderBy(m => m.Installs[0].Id)
            .ToList();
        var order = LoadOrder();
        int Rank(InstalledMod m)
        {
            var index = order.FindIndex(k => k.Equals(m.Key, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? index : int.MaxValue;
        }
        return mods.OrderBy(Rank).ToList();
    }

    /// <summary>Moves a mod or a separator up (negative) or down (positive) in the list. This only arranges the list.</summary>
    public void MoveInstalled(string key, int delta)
    {
        var rows = InstalledRows();
        var from = rows.FindIndex(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        var to = from + delta;
        if (from < 0 || to < 0 || to >= rows.Count) return;
        var moved = rows[from];
        rows.RemoveAt(from);
        rows.Insert(to, moved);
        SaveOrder(rows);
    }

    // A separator is kept in the order list as "sep:<id>:<text>".
    const string SeparatorPrefix = "sep:";

    /// <summary>
    /// The installed list as shown: mods in the user's order, with the user's separators between them.
    /// Mods not placed yet follow at the end, oldest install first.
    /// </summary>
    public List<InstalledRow> InstalledRows()
    {
        var mods = ListInstalled();
        var byKey = mods.ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase);
        var rows = new List<InstalledRow>();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in LoadOrder())
        {
            if (entry.StartsWith(SeparatorPrefix, StringComparison.Ordinal))
            {
                var rest = entry[SeparatorPrefix.Length..];
                var colon = rest.IndexOf(':');
                if (colon > 0) rows.Add(new InstalledRow(null, rest[..colon], rest[(colon + 1)..]));
            }
            else if (byKey.TryGetValue(entry, out var mod) && placed.Add(mod.Key))
                rows.Add(new InstalledRow(mod, null, mod.Name));
        }
        rows.AddRange(mods.Where(m => !placed.Contains(m.Key)).Select(m => new InstalledRow(m, null, m.Name)));
        return rows;
    }

    /// <summary>Adds a separator below the given row, or at the top when none is given.</summary>
    /// <returns>The new separator's row key.</returns>
    public string AddSeparator(string text, string? belowKey)
    {
        var rows = InstalledRows();
        var row = new InstalledRow(null, Guid.NewGuid().ToString("N")[..8], text.Trim());
        var at = belowKey == null ? 0 : rows.FindIndex(r => r.Key.Equals(belowKey, StringComparison.OrdinalIgnoreCase)) + 1;
        rows.Insert(Math.Clamp(at, 0, rows.Count), row);
        SaveOrder(rows);
        return row.Key;
    }

    public void RenameSeparator(string key, string text)
    {
        var rows = InstalledRows();
        var index = rows.FindIndex(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && r.IsSeparator);
        if (index < 0) return;
        rows[index] = rows[index] with { Text = text.Trim() };
        SaveOrder(rows);
    }

    public void RemoveSeparator(string key)
    {
        var rows = InstalledRows();
        if (rows.RemoveAll(r => r.IsSeparator && r.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) > 0) SaveOrder(rows);
    }

    void SaveOrder(IEnumerable<InstalledRow> rows)
    {
        var entries = rows.Select(r => r.IsSeparator ? $"{SeparatorPrefix}{r.SeparatorId}:{r.Text.Replace('\n', ' ').Replace('\r', ' ')}" : r.Mod!.Key).ToList();
        var tmp = NewTmpPath();
        File.WriteAllText(tmp, JsonSerializer.Serialize(entries, JsonOpts));
        File.Move(tmp, OrderPath, overwrite: true);
    }

    List<string> LoadOrder()
    {
        if (!File.Exists(OrderPath)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(OrderPath)) ?? new List<string>();
        }
        catch (JsonException)
        {
            File.Move(OrderPath, UniquePath(OrderPath + ".broken"));
            return new List<string>();
        }
    }

    /// <summary>The files of all a mod's installs. Where versions touched the same file, the newest record counts.</summary>
    public List<FileStatus> GetModFileStatuses(IEnumerable<Tx> installs)
    {
        var merged = new Dictionary<string, FileStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var tx in installs.OrderBy(t => t.Id))
            foreach (var status in GetFileStatuses(tx))
                merged[status.File.Path] = status;
        return merged.Values.ToList();
    }

    // ---------------------------------------------------------------- notes

    string MetaPath => Path.Combine(StateDir, "meta.json");

    /// <summary>Notes by archive file name. A damaged file is set aside rather than overwritten.</summary>
    public Dictionary<string, ModMeta> LoadMeta()
    {
        var empty = new Dictionary<string, ModMeta>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(MetaPath)) return empty;
        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, ModMeta>>(File.ReadAllText(MetaPath));
            if (loaded == null) return empty;
            var meta = new Dictionary<string, ModMeta>(loaded, StringComparer.OrdinalIgnoreCase);
            // Older notes are brought into the current shape once, and saved that way.
            if (meta.Values.Count(m => m.Migrate()) > 0) SaveMeta(meta);
            return meta;
        }
        catch (JsonException)
        {
            File.Move(MetaPath, UniquePath(MetaPath + ".broken"));
            return empty;
        }
    }

    public void SaveMeta(IReadOnlyDictionary<string, ModMeta> meta)
    {
        var tmp = NewTmpPath();
        var kept = meta.Where(p => !p.Value.IsEmpty()).ToDictionary(p => p.Key, p => p.Value);
        File.WriteAllText(tmp, JsonSerializer.Serialize(kept, JsonOpts));
        File.Move(tmp, MetaPath, overwrite: true);
    }

    /// <summary>A mod stays on the list after it is removed, so anything installed counts as registered.</summary>
    /// <returns>True if an archive was newly registered and the notes need saving.</returns>
    public bool RegisterInstalled(Dictionary<string, ModMeta> meta)
    {
        bool changed = false;
        foreach (var tx in History())
        {
            if (!meta.TryGetValue(tx.ZipName, out var note)) meta[tx.ZipName] = note = new ModMeta();
            if (!note.Registered)
            {
                note.Registered = true;
                changed = true;
            }
        }
        return changed;
    }

    // ---------------------------------------------------------------- last check on Nexus

    string RemotePath => Path.Combine(StateDir, "remote.json");

    public Dictionary<string, RemoteInfo> LoadRemote()
    {
        var empty = new Dictionary<string, RemoteInfo>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(RemotePath)) return empty;
        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, RemoteInfo>>(File.ReadAllText(RemotePath));
            return loaded == null ? empty : new Dictionary<string, RemoteInfo>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            File.Move(RemotePath, UniquePath(RemotePath + ".broken"));
            return empty;
        }
    }

    public void SaveRemote(IReadOnlyDictionary<string, RemoteInfo> remote)
    {
        var tmp = NewTmpPath();
        File.WriteAllText(tmp, JsonSerializer.Serialize(remote, JsonOpts));
        File.Move(tmp, RemotePath, overwrite: true);
    }

    // ---------------------------------------------------------------- categories

    public static string NoCategory => L.T("미분류");
    public static string AllCategories => L.T("전체");

    string CategoriesPath => Path.Combine(StateDir, "categories.json");

    /// <summary>The user's categories in their order, plus any a note uses that the list lacks.</summary>
    public List<string> LoadCategories(IReadOnlyDictionary<string, ModMeta> meta)
    {
        var list = new List<string>();
        if (File.Exists(CategoriesPath))
        {
            try
            {
                list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(CategoriesPath)) ?? list;
            }
            catch (JsonException)
            {
                File.Move(CategoriesPath, UniquePath(CategoriesPath + ".broken"));
            }
        }
        foreach (var used in meta.Values.Select(m => m.Category).Where(c => c.Length > 0))
            if (!list.Contains(used, StringComparer.OrdinalIgnoreCase)) list.Add(used);
        return list;
    }

    public void SaveCategories(IReadOnlyList<string> categories)
    {
        var tmp = NewTmpPath();
        File.WriteAllText(tmp, JsonSerializer.Serialize(categories, JsonOpts));
        File.Move(tmp, CategoriesPath, overwrite: true);
    }

    /// <returns>Why the name cannot be used, or null if it is fine.</returns>
    public static string? CategoryNameProblem(string name, IReadOnlyList<string> categories, string? renaming = null)
    {
        if (name.Length == 0) return L.T("이름을 입력해 주세요.");
        if (name.Equals(NoCategory, StringComparison.OrdinalIgnoreCase) || name.Equals(AllCategories, StringComparison.OrdinalIgnoreCase))
            return L.F("'{0}'은(는) 이미 쓰고 있는 이름이라 사용할 수 없습니다.", name);
        bool taken = categories.Any(c => c.Equals(name, StringComparison.OrdinalIgnoreCase)
            && !c.Equals(renaming, StringComparison.OrdinalIgnoreCase));
        return taken ? L.F("'{0}' 카테고리가 이미 있습니다.", name) : null;
    }

    public static void RenameCategory(IDictionary<string, ModMeta> meta, List<string> categories, string oldName, string newName)
    {
        var index = categories.FindIndex(c => c.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) categories[index] = newName;
        foreach (var note in meta.Values.Where(m => m.Category.Equals(oldName, StringComparison.OrdinalIgnoreCase)))
            note.Category = newName;
    }

    /// <summary>Mods in the category become uncategorized; they are not removed.</summary>
    public static void DeleteCategory(IDictionary<string, ModMeta> meta, List<string> categories, string name)
    {
        categories.RemoveAll(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
        foreach (var note in meta.Values.Where(m => m.Category.Equals(name, StringComparison.OrdinalIgnoreCase)))
            note.Category = "";
    }

    // ---------------------------------------------------------------- install

    /// <param name="chosen">
    /// At most one folder of each option set; a set with none is not installed. Left out, the choice of the
    /// mod's last install is used, or else each set's first option.
    /// </param>
    /// <param name="excluded">Files left out, by where they would go. Left out, those the mod's last install left out.</param>
    /// <param name="progress">Told which file is being read, as "n/total": large archives take a while.</param>
    public InstallPlan BuildPlan(string zipPath, IReadOnlyCollection<string>? chosen = null, IReadOnlyCollection<string>? excluded = null,
        Action<string>? progress = null)
    {
        var plan = new InstallPlan { ZipPath = zipPath };
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var zip = ZipFile.OpenRead(zipPath);
        long totalSize = 0;
        var (laid, options, picked) = LayOut(zip, chosen ?? (PreviousOptions(zipPath) is { Count: > 0 } previous ? previous : null), excluded ?? PreviousExcluded(zipPath));
        plan.Options.AddRange(options);
        plan.Chosen.AddRange(picked);
        int fileCount = laid.Count(l => !l.IsDir), read = 0;
        foreach (var (entry, rel, isDir, placement) in laid)
        {
            if (!isDir) progress?.Invoke(L.F("압축 파일 확인 중... {0}/{1}", ++read, fileCount));
            if (!isDir) plan.Placements[placement] = plan.Placements.GetValueOrDefault(placement) + 1;
            if (placement == Placement.UserLeftOut) plan.Excluded.Add(rel);
            if (placement >= Placement.OtherCopyLeftOut) continue;
            if (placement == Placement.MovedToLoader && !isDir) plan.Relocated.Add(rel);
            if (placement == Placement.Ue4ssMod) plan.Ue4ssMods.Add(rel[LooseLayout.Ue4ssModsHome.Length..].Split('/')[0]);

            var parent = isDir ? rel : ParentOf(rel);
            while (parent != null)
            {
                if (File.Exists(Full(parent)))
                    throw new InvalidOperationException(L.F("폴더가 들어갈 자리에 같은 이름의 파일이 있습니다: {0}", parent));
                if (!Directory.Exists(Full(parent))) dirs.Add(parent);
                parent = ParentOf(parent);
            }
            if (isDir) continue;

            if (!seen.Add(rel))
                throw new InvalidOperationException(L.F("압축 안에 같은 경로의 파일이 중복됩니다: {0}", rel));
            if (Directory.Exists(Full(rel)))
                throw new InvalidOperationException(L.F("파일이 들어갈 자리에 같은 이름의 폴더가 있습니다: {0}", rel));

            string newHash;
            using (var s = entry.Open()) newHash = HashStream(s);

            var oldHash = CurrentHash(rel);
            var kind = oldHash == null ? ChangeKind.Add
                : oldHash == newHash ? ChangeKind.Same
                : KeepIfExists.Contains(rel, StringComparer.OrdinalIgnoreCase) ? ChangeKind.Keep
                : ChangeKind.Overwrite;
            plan.Items.Add(new PlanItem(rel, kind, newHash, oldHash, entry.Length));
            totalSize += entry.Length;
        }

        plan.NewDirs.AddRange(dirs.OrderBy(d => d.Length).ThenBy(d => d, StringComparer.OrdinalIgnoreCase));
        FindStaleFiles(plan, seen, ModIdentity.KeyOf(seen, Path.GetFileName(zipPath)));
        if (plan.Relocated.Count > 0)
            plan.Warnings.Add(L.F("Mods Menu용 파일 {0}개를 모드 로더 폴더로 옮겨 설치합니다. Mods Menu의 설정 화면은 쓸 수 없고, 메뉴 기능이 필요한 모드는 동작하지 않을 수 있습니다.", plan.Relocated.Count));
        if (plan.PlacedBesideGame > 0)
            plan.Warnings.Add(L.F("UE4SS 파일 {0}개를 게임 실행 파일(PBZ.exe) 옆인 platform/steam에 설치합니다.", plan.PlacedBesideGame));
        if (plan.SkippedCopies > 0)
            plan.Warnings.Add(TargetRuntime == ScriptRuntime.Menu
                ? L.F("모드 로더용 복사본 {0}개는 설치하지 않습니다. 같은 모드가 Mods Menu용으로도 들어 있습니다.", plan.SkippedCopies)
                : L.F("Mods Menu용 복사본 {0}개는 설치하지 않습니다. 같은 모드가 모드 로더용으로도 들어 있습니다.", plan.SkippedCopies));
        // Both loaders installed: whichever starts first runs, and Mods Menu then stays off.
        bool Has(string path) => plan.Items.Any(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (Has(ModIdentity.LoaderBootstrap) && MenuInstalled)
            plan.Warnings.Add(L.T("Mods Menu가 이미 설치되어 있습니다. 두 스크립트 로더가 함께 있으면 하나만 동작하니, 쓰지 않을 쪽은 제거해 주세요."));
        if (Has(ModIdentity.MenuBootstrap) && LoaderInstalled)
            plan.Warnings.Add(L.T("PBZModLoader가 이미 설치되어 있습니다. 두 스크립트 로더가 함께 있으면 하나만 동작하니, 쓰지 않을 쪽은 제거해 주세요."));
        if (plan.Placements.GetValueOrDefault(Placement.Pak) > 0)
            foreach (var home in new[] { LooseLayout.PakHome, LooseLayout.LogicModsHome })
                if (plan.Items.Count(i => i.Path.StartsWith(home, StringComparison.OrdinalIgnoreCase) && !i.Path[home.Length..].Contains('/')) is > 0 and var paks)
                    plan.Warnings.Add(L.F("pak 파일 {0}개를 {1}에 설치합니다.", paks, home.TrimEnd('/')));
        if (plan.Ue4ssMods.Count > 0)
        {
            plan.Warnings.Add(L.F("UE4SS 모드 {0}을(를) {1}에 설치합니다.", string.Join(", ", plan.Ue4ssMods), LooseLayout.Ue4ssModsHome.TrimEnd('/')));
            if (!File.Exists(Full(ModIdentity.Ue4ssHome + "dwmapi.dll")))
                plan.Warnings.Add(L.T("UE4SS가 설치되어 있지 않습니다. UE4SS를 먼저 설치해야 이 모드가 동작합니다."));
        }
        if (plan.Placements.GetValueOrDefault(Placement.BesideExe) is > 0 and var beside)
            plan.Warnings.Add(L.F("파일 {0}개를 게임 실행 파일(PBZ.exe) 옆인 platform/steam에 설치합니다.", beside));
        var unchosenFiles = plan.Placements.GetValueOrDefault(Placement.UnchosenLeftOut);
        var pictures = plan.Placements.GetValueOrDefault(Placement.PictureLeftOut);
        if (unchosenFiles + pictures > 0)
            plan.Warnings.Add(L.T("설치하지 않는 파일: ") + string.Join(", ", new[]
            {
                unchosenFiles > 0 ? L.F("고르지 않은 옵션 {0}개", unchosenFiles) : null,
                pictures > 0 ? L.F("설명 그림 {0}개", pictures) : null,
            }.Where(s => s != null)));

        // Backups and new files together need at most the size of the archive contents twice.
        var free = new DriveInfo(Path.GetPathRoot(Root)!).AvailableFreeSpace;
        if (totalSize * 2 > free)
            throw new InvalidOperationException(L.T("디스크 여유 공간이 부족합니다."));

        // Detects the "extra wrapping folder" packaging mistake: nothing would land in an existing folder.
        var tops = plan.Items.Select(i => i.Path).Concat(dirs).Select(p => p.Split('/')[0]).Distinct();
        if (!tops.Any(t => Directory.Exists(Full(t)) && !dirs.Contains(t)))
            plan.Warnings.Add(L.T("압축의 최상위 폴더가 게임 폴더 구조와 맞지 않습니다. 폴더가 한 겹 더 감싸져 있을 수 있습니다."));
        return plan;
    }

    sealed record Laid(ZipArchiveEntry Entry, string Path, bool IsDir, Placement Kind)
    {
        public bool LeftOut => Kind >= Placement.OtherCopyLeftOut;
    }

    /// <summary>The archive's paths, after a UE4SS package is moved beside PBZ.exe.</summary>
    static Func<string, string> Ue4ssPackagePlacement(IReadOnlyCollection<string> files)
    {
        var ue4ss = ModIdentity.Ue4ssBase(files);
        return path => ue4ss != null && path.StartsWith(ue4ss, StringComparison.OrdinalIgnoreCase)
            ? ModIdentity.Ue4ssHome + path[ue4ss.Length..] : path;
    }

    /// <summary>
    /// Every archive entry with where it installs. All paths are checked, including those left out.
    /// <paramref name="chosen"/> picks one folder of each option set; the first one stands in for a set it misses.
    /// </summary>
    (List<Laid> Entries, List<OptionGroup> Options, List<string> Chosen) LayOut(ZipArchive zip, IReadOnlyCollection<string>? chosen, IReadOnlyCollection<string>? excluded = null)
    {
        var entries = zip.Entries.Select(e => (Entry: e, Rel: NormalizeEntryPath(e.FullName), IsDir: e.FullName.EndsWith('/') || e.FullName.EndsWith('\\'))).ToList();
        var unwrap = LooseLayout.Unwrapper(entries.Where(e => !e.IsDir).Select(e => e.Rel).ToList());
        // The wrapping folder itself is not installed; what is inside it is laid out as if it were the top.
        entries = entries.Select(e => (e.Entry, Rel: e.IsDir ? unwrap(e.Rel + "/").TrimEnd('/') : unwrap(e.Rel), e.IsDir)).ToList();
        var files = entries.Where(e => !e.IsDir).Select(e => e.Rel).ToList();

        var package = Ue4ssPackagePlacement(files);
        var packaged = files.Select(package).ToList();

        // A set none of whose folders was chosen is not installed; with no choice at all, each set's first stands in.
        var options = LooseLayout.OptionGroups(packaged);
        var picked = options.Select(g => chosen == null ? g.Options[0].Folder
                : g.Options.FirstOrDefault(o => chosen.Contains(o.Folder, StringComparer.OrdinalIgnoreCase))?.Folder)
            .Where(f => f != null).Select(f => f!).ToList();
        var unchosen = options.SelectMany(g => g.Options).Select(o => o.Folder).Except(picked, StringComparer.OrdinalIgnoreCase).ToList();
        var loose = LooseLayout.For(packaged, unchosen);
        var placedFiles = packaged.Select(p => loose(p).Path).Where(p => p != null).Select(p => p!).ToList();

        var runtime = TargetRuntime;
        var menu = ModIdentity.InstallLayout(placedFiles, runtime);

        var laid = entries.Select(e =>
        {
            if (e.Rel.Length == 0) return new Laid(e.Entry, e.Rel, e.IsDir, Placement.FolderLeftOut);
            var start = package(e.IsDir ? e.Rel + "/" : e.Rel);
            var (path, kind) = loose(start);
            if (path == null) return new Laid(e.Entry, e.Rel, e.IsDir, kind);
            if (start != (e.IsDir ? e.Rel + "/" : e.Rel) && kind == Placement.AsPackaged) kind = Placement.Ue4ssPackage;
            var target = menu(path);
            if (target == null) return new Laid(e.Entry, e.Rel, e.IsDir, Placement.OtherCopyLeftOut);
            if (target != path) kind = Placement.MovedToLoader;
            if (!e.IsDir && excluded?.Contains(target, StringComparer.OrdinalIgnoreCase) == true) return new Laid(e.Entry, target, false, Placement.UserLeftOut);
            return new Laid(e.Entry, target.TrimEnd('/'), e.IsDir, kind);
        }).ToList();
        return (laid, options, picked);
    }

    /// <summary>The option sets an archive offers, for the user to pick from before the plan is built.</summary>
    public List<OptionGroup> OptionGroups(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return LayOut(zip, null).Options;
    }

    /// <summary>The options picked when this mod was last installed, offered again as the starting choice.</summary>
    public List<string> PreviousOptions(string zipPath)
    {
        var key = ModIdentity.KeyOfZip(zipPath);
        return History().LastOrDefault(t => t.Options.Count > 0 && ModIdentity.KeyOfTx(t).Equals(key, StringComparison.OrdinalIgnoreCase))?.Options
            ?? new List<string>();
    }

    /// <summary>
    /// Why this archive should go in after the installed version of its mod is removed, rather than over it:
    /// its author laid it out anew, so the old options do not carry over or most old files sit elsewhere.
    /// Null when it updates over the installed version as usual, or when the mod is not installed.
    /// </summary>
    public string? LayoutChange(string zipPath)
    {
        var key = ModIdentity.KeyOfZip(zipPath);
        var mine = History().Where(t => t.State == Tx.Installed && ModIdentity.KeyOfTx(t).Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (mine.Count == 0 || mine[^1].ZipName.Equals(Path.GetFileName(zipPath), StringComparison.OrdinalIgnoreCase)) return null;

        using var zip = ZipFile.OpenRead(zipPath);
        var (laid, options, _) = LayOut(zip, null);
        var previous = PreviousOptions(zipPath);
        if (previous.Count > 0 && options.Count > 0
            && !options.SelectMany(g => g.Options).Any(o => previous.Contains(o.Folder, StringComparer.OrdinalIgnoreCase)))
            return L.T("옵션 구성이 바뀌어 이전에 고른 옵션을 이어받을 수 없습니다.");

        // Any option of a set lands on the same paths, so the first ones stand for all.
        var targets = laid.Where(l => !l.IsDir && !l.LeftOut).Select(l => l.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var old = mine.SelectMany(t => t.Files).Where(f => !f.Deleted).Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var moved = old.Count(p => !targets.Contains(p));
        return old.Count > 0 && moved * 2 >= old.Count
            ? L.F("설치 위치가 바뀌어 이전 버전 파일 {0}개 중 {1}개가 새 버전에 없습니다.", old.Count, moved)
            : null;
    }

    /// <summary>The files the mod's last install left out, offered unticked again so an update keeps the choice.</summary>
    public List<string> PreviousExcluded(string zipPath)
    {
        var key = ModIdentity.KeyOfZip(zipPath);
        return History().LastOrDefault(t => ModIdentity.KeyOfTx(t).Equals(key, StringComparison.OrdinalIgnoreCase))?.Excluded
            ?? new List<string>();
    }

    /// <summary>
    /// When this mod is already installed, finds its files the new archive lacks. Left in place they
    /// would keep running: the loader prefers a leftover main.ts over a new main.js, for one.
    /// </summary>
    void FindStaleFiles(InstallPlan plan, IReadOnlySet<string> inArchive, string key)
    {
        var history = History();
        var mine = history.Where(t => ModIdentity.KeyOfTx(t).Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (mine.Count == 0) return;
        plan.ReplacesZip = mine[^1].ZipName;

        var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tx in mine)
        {
            foreach (var u in tx.Unchanged) installed[u.Path] = u.Hash;
            foreach (var f in tx.Files)
            {
                if (f.Deleted) installed.Remove(f.Path);
                else installed[f.Path] = f.InstalledHash;
            }
        }

        var others = history.Except(mine).Where(t => t.Files.Any(f => !f.Deleted) || t.Unchanged.Count > 0)
            .SelectMany(t => t.Files.Where(f => !f.Deleted).Select(f => f.Path).Concat(t.Unchanged.Select(u => u.Path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, installedHash) in installed.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            // A file another mod also uses is not this mod's to remove.
            if (inArchive.Contains(path) || others.Contains(path)) continue;
            var current = CurrentHash(path);
            if (current != null) plan.Stale.Add(new StaleFile(path, current, current != installedHash));
        }
    }

    /// <param name="expected">The previewed plan. Install is refused if the game folder changed since then.</param>
    public Tx Install(InstallPlan expected, Action<string>? progress = null)
    {
        EnsureGameNotRunning();
        var pending = Pending();
        if (pending != null)
            throw new InvalidOperationException(L.F("#{0} 작업이 중단된 상태입니다. 먼저 되돌려 주세요.", pending.Id));

        var plan = BuildPlan(expected.ZipPath, expected.Chosen, expected.Excluded, progress);
        if (plan.Fingerprint() != expected.Fingerprint())
            throw new InvalidOperationException(L.T("미리보기 이후 게임 폴더가 변경되었습니다. 다시 시도해 주세요."));
        if (!plan.HasChanges)
            throw new InvalidOperationException(L.T("적용할 변경 사항이 없습니다."));

        var changes = plan.Items.Where(i => i.Kind is ChangeKind.Add or ChangeKind.Overwrite).ToList();
        var tx = new Tx
        {
            Id = History().Select(t => t.Id).DefaultIfEmpty(0).Max() + 1,
            ZipName = Path.GetFileName(plan.ZipPath),
            Time = DateTime.Now,
            Options = plan.Chosen.ToList(),
            Excluded = plan.Excluded.ToList(),
            CreatedDirs = plan.NewDirs.ToList(),
            Files = changes.Select(i => new TxFile
            {
                Path = i.Path,
                Overwrote = i.Kind == ChangeKind.Overwrite,
                InstalledHash = i.NewHash,
                BackupHash = i.OldHash,
            }).Concat(plan.Stale.Select(s => new TxFile
            {
                Path = s.Path,
                Overwrote = true,
                Deleted = true,
                BackupHash = s.Hash,
            })).ToList(),
            Unchanged = plan.Items.Where(i => i.Kind is ChangeKind.Same or ChangeKind.Keep)
                .Select(i => new TxUnchanged { Path = i.Path, Hash = i.NewHash }).ToList(),
        };

        // The manifest is written before the game folder is touched, so any interruption can be undone.
        Directory.CreateDirectory(TxDir(tx.Id));
        SaveTx(tx);

        try
        {
            progress?.Invoke(L.T("백업 중..."));
            var toBackUp = changes.Where(i => i.Kind == ChangeKind.Overwrite).Select(i => (i.Path, i.OldHash))
                .Concat(plan.Stale.Select(s => (s.Path, (string?)s.Hash))).ToList();
            int backedUp = 0;
            foreach (var (path, hash) in toBackUp)
            {
                progress?.Invoke(L.F("백업 중... {0}/{1}", ++backedUp, toBackUp.Count));
                var backup = BackupPath(tx.Id, path);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(Full(path), backup, overwrite: true);
                if (HashFile(backup) != hash)
                    throw new IOException(L.F("백업 검증 실패: {0}", path));
            }

            progress?.Invoke(L.T("파일 복사 중..."));
            foreach (var dir in plan.NewDirs) Directory.CreateDirectory(Full(dir));

            using var zip = ZipFile.OpenRead(plan.ZipPath);
            var byPath = LayOut(zip, plan.Chosen, plan.Excluded).Entries
                .Where(e => !e.IsDir && !e.LeftOut)
                .ToDictionary(e => e.Path, e => e.Entry, StringComparer.OrdinalIgnoreCase);
            int copied = 0;
            foreach (var item in changes)
            {
                progress?.Invoke(L.F("파일 복사 중... {0}/{1}", ++copied, changes.Count));
                var entry = byPath[item.Path];
                var tmp = NewTmpPath();
                try
                {
                    using (var input = entry.Open())
                    using (var output = File.Create(tmp)) input.CopyTo(output);
                    if (HashFile(tmp) != item.NewHash)
                        throw new IOException(L.F("압축 해제 검증 실패: {0}", item.Path));
                    if (entry.LastWriteTime.Year >= 1980)
                        File.SetLastWriteTime(tmp, entry.LastWriteTime.DateTime);
                    MoveReplacing(tmp, Full(item.Path));
                }
                finally
                {
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
            }

            foreach (var stale in plan.Stale) DeleteFile(Full(stale.Path));

            tx.State = Tx.Installed;
            SaveTx(tx);
            Log(L.F("{0} #{1} {2} (추가 {3}, 덮어씀 {4}, 옛 파일 제거 {5})", (plan.ReplacesZip == null ? L.T("설치") : L.T("업데이트")), tx.Id, tx.ZipName, tx.Files.Count(f => !f.Overwrote), tx.Files.Count(f => f.Overwrote && !f.Deleted), plan.Stale.Count));
            return tx;
        }
        catch (Exception ex)
        {
            Log(L.F("설치 실패 #{0} {1}: {2}", tx.Id, tx.ZipName, ex.Message));
            try
            {
                UndoTx(tx, NewLeftoverRun(tx.Id));
            }
            catch (Exception undoEx)
            {
                throw new InvalidOperationException(
                    L.F("설치 실패: {0}\n자동 복구도 실패했습니다: {1}\n다시 실행하면 복구를 재시도합니다.", ex.Message, undoEx.Message), ex);
            }
            throw new InvalidOperationException(L.F("설치 실패, 원래 상태로 복구했습니다.\n{0}", ex.Message), ex);
        }
    }

    // ---------------------------------------------------------------- rollback

    /// <summary>Previews undoing transaction <paramref name="targetId"/> and everything installed after it.</summary>
    public RollbackPlan BuildRollbackPlan(int targetId)
    {
        var plan = new RollbackPlan { TargetId = targetId };
        var txs = History().Where(t => t.Id >= targetId).OrderByDescending(t => t.Id).ToList();
        plan.TxIds.AddRange(txs.Select(t => t.Id));

        // Undoing a newer install changes what the older one will find, so the preview tracks that.
        var simulated = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string? Current(string path) => simulated.TryGetValue(path, out var h) ? h : CurrentHash(path);

        foreach (var tx in txs)
            foreach (var f in tx.Files)
            {
                var kind = StepFor(f, Current(f.Path));
                plan.Steps.Add(new RollbackStep(tx.Id, f.Path, kind));
                simulated[f.Path] = kind switch
                {
                    StepKind.Delete or StepKind.StashDelete => null,
                    StepKind.Restore or StepKind.StashRestore => f.BackupHash,
                    _ => Current(f.Path),
                };
            }

        var tracked = txs.SelectMany(t => t.Files).Select(f => f.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extras = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scan = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        foreach (var dir in txs.SelectMany(t => t.CreatedDirs).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            plan.RemovedDirs.Add(dir);
            if (!Directory.Exists(Full(dir))) continue;
            foreach (var file in Directory.EnumerateFiles(Full(dir), "*", scan))
            {
                var rel = Path.GetRelativePath(Root, file).Replace('\\', '/');
                if (!tracked.Contains(rel)) extras.Add(rel);
            }
        }
        plan.ExtraFiles.AddRange(extras.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        return plan;
    }

    /// <summary>Undoes transaction <paramref name="targetId"/> and everything after it, newest first.</summary>
    /// <returns>The folder holding files moved aside, or null if nothing had to be kept.</returns>
    public string? RollbackTo(int targetId, Action<string>? progress = null)
    {
        EnsureGameNotRunning();
        if (!History().Any(t => t.Id >= targetId))
            throw new InvalidOperationException(L.F("#{0} 기록이 없습니다.", targetId));

        var leftovers = NewLeftoverRun(targetId);
        FinishPending(leftovers);
        foreach (var tx in History().Where(t => t.Id >= targetId).OrderByDescending(t => t.Id))
        {
            progress?.Invoke(L.F("#{0} 되돌리는 중...", tx.Id));
            UndoTx(tx, leftovers);
        }
        return Directory.Exists(leftovers) ? leftovers : null;
    }

    /// <summary>Previews removing only transaction <paramref name="id"/>, leaving every other install in place.</summary>
    public RollbackPlan BuildRemovePlan(int id) => BuildRemovePlan(new[] { id });

    /// <summary>Previews removing several installs, such as every version of one mod, leaving all others in place.</summary>
    public RollbackPlan BuildRemovePlan(IReadOnlyCollection<int> ids)
    {
        var all = History();
        var removing = all.Where(t => ids.Contains(t.Id)).OrderByDescending(t => t.Id).ToList();
        var remaining = all.Where(t => !ids.Contains(t.Id)).ToList();
        var plan = new RollbackPlan
        {
            TargetId = removing.Select(t => t.Id).DefaultIfEmpty(0).Min(),
            RemovesLoaderUnderOtherMods = remaining.Count > 0 && removing.Any(t =>
                t.Files.Any(f => f.Path.EndsWith("/PBZModLoader/bootstrap.js", StringComparison.OrdinalIgnoreCase))),
        };
        plan.TxIds.AddRange(removing.Select(t => t.Id));

        // Newest first, tracking the result, so one file touched by several versions shows its final outcome once.
        var simulated = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var outcome = new Dictionary<string, RollbackStep>(StringComparer.OrdinalIgnoreCase);
        string? Current(string path) => simulated.TryGetValue(path, out var h) ? h : CurrentHash(path);

        foreach (var tx in removing)
            foreach (var f in tx.Files)
            {
                var laterHere = remaining.Where(t => t.Id > tx.Id);
                var kind = NextFileOwner(laterHere, f.Path) != null ? StepKind.KeepForLater : StepFor(f, Current(f.Path));
                if (kind is StepKind.Delete or StepKind.StashDelete) simulated[f.Path] = null;
                else if (kind is StepKind.Restore or StepKind.StashRestore) simulated[f.Path] = f.BackupHash;

                // Anything moved aside along the way stays reported, whatever happens to the file after.
                if (outcome.TryGetValue(f.Path, out var earlier) && earlier.Kind is StepKind.StashDelete or StepKind.StashRestore)
                    kind = kind is StepKind.Restore or StepKind.StashRestore ? StepKind.StashRestore : StepKind.StashDelete;
                else if (kind == StepKind.None && outcome.TryGetValue(f.Path, out var kept))
                    kind = kept.Kind;
                outcome[f.Path] = new RollbackStep(tx.Id, f.Path, kind);
            }
        plan.Steps.AddRange(outcome.Values);

        var tracked = removing.SelectMany(t => t.Files).Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extras = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scan = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        foreach (var tx in removing)
            foreach (var dir in tx.CreatedDirs)
            {
                if (NextDirOwner(remaining.Where(t => t.Id > tx.Id), dir) != null)
                {
                    plan.KeptDirs.Add(dir);
                    continue;
                }
                plan.RemovedDirs.Add(dir);
                if (!Directory.Exists(Full(dir))) continue;
                foreach (var file in Directory.EnumerateFiles(Full(dir), "*", scan))
                {
                    var rel = Path.GetRelativePath(Root, file).Replace('\\', '/');
                    if (!tracked.Contains(rel)) extras.Add(rel);
                }
            }
        plan.ExtraFiles.AddRange(extras.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        return plan;
    }

    /// <summary>Removes several installs, newest first, leaving all others in place.</summary>
    /// <returns>The folder holding files moved aside, or null if nothing had to be kept.</returns>
    public string? RemoveMany(IReadOnlyCollection<int> ids, Action<string>? progress = null)
    {
        EnsureGameNotRunning();
        var leftovers = NewLeftoverRun(ids.Count > 0 ? ids.Min() : 0);
        FinishPending(leftovers);
        foreach (var tx in History().Where(t => ids.Contains(t.Id)).OrderByDescending(t => t.Id))
        {
            progress?.Invoke(L.F("#{0} 제거 중...", tx.Id));
            UndoTx(tx, leftovers);
        }
        return Directory.Exists(leftovers) ? leftovers : null;
    }

    /// <summary>Removes only transaction <paramref name="id"/>; later installs keep working from their own records.</summary>
    /// <returns>The folder holding files moved aside, or null if nothing had to be kept.</returns>
    public string? RemoveOne(int id, Action<string>? progress = null)
    {
        EnsureGameNotRunning();
        if (History().All(t => t.Id != id))
            throw new InvalidOperationException(L.F("#{0} 기록이 없습니다.", id));

        var leftovers = NewLeftoverRun(id);
        FinishPending(leftovers);
        var tx = History().FirstOrDefault(t => t.Id == id);
        if (tx != null)
        {
            progress?.Invoke(L.F("#{0} 제거 중...", id));
            UndoTx(tx, leftovers);
        }
        return Directory.Exists(leftovers) ? leftovers : null;
    }

    /// <summary>An interrupted undo must complete first: later installs may hold half-updated records until it does.</summary>
    void FinishPending(string leftovers)
    {
        foreach (var tx in History().Where(t => t.State != Tx.Installed).OrderByDescending(t => t.Id))
            UndoTx(tx, leftovers);
    }

    static Tx? NextFileOwner(IEnumerable<Tx> later, string path) => later.FirstOrDefault(t =>
        t.Files.Any(f => SamePath(f.Path, path)) || t.Unchanged.Any(u => SamePath(u.Path, path)));

    static Tx? NextDirOwner(IEnumerable<Tx> later, string dir) => later.FirstOrDefault(t =>
        t.Files.Any(f => IsInside(f.Path, dir)) || t.Unchanged.Any(u => IsInside(u.Path, dir))
        || t.CreatedDirs.Any(d => SamePath(d, dir) || IsInside(d, dir)));

    static bool SamePath(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
    static bool IsInside(string path, string dir) => path.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase);

    static StepKind StepFor(TxFile f, string? current)
    {
        if (!f.Overwrote)
        {
            if (current == null) return StepKind.None;
            return current == f.InstalledHash ? StepKind.Delete : StepKind.StashDelete;
        }
        if (current == f.BackupHash) return StepKind.None;
        return current == null || current == f.InstalledHash ? StepKind.Restore : StepKind.StashRestore;
    }

    /// <summary>Safe to repeat: every step checks the current disk state, so a failed undo can be retried.</summary>
    void UndoTx(Tx tx, string leftovers)
    {
        tx.State = Tx.Undoing;
        SaveTx(tx);

        // Files and folders a later install still relies on stay on disk and pass to its record.
        // Records are saved first, so a failure below cannot leave a later install's record stale.
        var later = History().Where(t => t.Id > tx.Id).ToList();
        var updated = new HashSet<Tx>();
        var handedOver = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in tx.Files)
        {
            var owner = NextFileOwner(later, f.Path);
            if (owner == null) continue;
            HandOverFile(tx, f, owner);
            updated.Add(owner);
            handedOver.Add(f.Path);
        }
        var keptDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in tx.CreatedDirs)
        {
            var owner = NextDirOwner(later, dir);
            if (owner == null) continue;
            if (!owner.CreatedDirs.Contains(dir, StringComparer.OrdinalIgnoreCase)) owner.CreatedDirs.Add(dir);
            updated.Add(owner);
            keptDirs.Add(dir);
        }
        foreach (var owner in updated) SaveTx(owner);

        foreach (var f in tx.Files.Where(f => !handedOver.Contains(f.Path)))
        {
            var kind = StepFor(f, CurrentHash(f.Path));
            if (kind is StepKind.StashDelete or StepKind.StashRestore)
                StashFile(Full(f.Path), Path.Combine(leftovers, ToNative(f.Path)));
            else if (kind == StepKind.Delete)
                DeleteFile(Full(f.Path));

            if (kind is StepKind.Restore or StepKind.StashRestore)
                RestoreFile(tx.Id, f);
        }

        foreach (var dir in tx.CreatedDirs.Where(d => !keptDirs.Contains(d)).OrderByDescending(d => d.Length))
        {
            var full = Full(dir);
            if (!Directory.Exists(full)) continue;
            if (Directory.EnumerateFileSystemEntries(full).Any())
                StashDir(full, Path.Combine(leftovers, ToNative(dir)));
            else
                Directory.Delete(full);
        }

        Directory.Delete(TxDir(tx.Id), recursive: true);
        Log(L.F("되돌림 #{0} {1}", tx.Id, tx.ZipName) + (Directory.Exists(leftovers) ? L.F(" (보관: {0})", Path.GetFileName(leftovers)) : ""));
    }

    /// <summary>
    /// The later install takes over what was underneath this one, so undoing it later still
    /// reaches the true original. Repeating this gives the same result.
    /// </summary>
    void HandOverFile(Tx tx, TxFile f, Tx owner)
    {
        var target = owner.Files.FirstOrDefault(x => SamePath(x.Path, f.Path));
        if (target == null)
        {
            var unchanged = owner.Unchanged.First(u => SamePath(u.Path, f.Path));
            target = new TxFile { Path = unchanged.Path, InstalledHash = unchanged.Hash };
            owner.Files.Add(target);
            owner.Unchanged.Remove(unchanged);
        }
        target.Overwrote = f.Overwrote;
        target.BackupHash = f.BackupHash;

        var ownerBackup = BackupPath(owner.Id, f.Path);
        if (f.Overwrote)
        {
            var tmp = NewTmpPath();
            File.Copy(BackupPath(tx.Id, f.Path), tmp, overwrite: true);
            Directory.CreateDirectory(Path.GetDirectoryName(ownerBackup)!);
            MoveReplacing(tmp, ownerBackup);
        }
        else if (File.Exists(ownerBackup))
        {
            DeleteFile(ownerBackup);
        }
    }

    void RestoreFile(int txId, TxFile f)
    {
        var backup = BackupPath(txId, f.Path);
        if (!File.Exists(backup))
            throw new IOException(L.F("백업 파일이 없습니다: {0}", f.Path));
        var tmp = NewTmpPath();
        try
        {
            File.Copy(backup, tmp, overwrite: true);
            if (HashFile(tmp) != f.BackupHash)
                throw new IOException(L.F("백업 파일이 손상되었습니다: {0}", f.Path));
            Directory.CreateDirectory(Path.GetDirectoryName(Full(f.Path))!);
            MoveReplacing(tmp, Full(f.Path));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    static void StashFile(string source, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        ClearReadOnly(source);
        File.Move(source, UniquePath(dest));
    }

    /// <summary>Moves a folder aside, merging when an earlier step already moved part of its tree.</summary>
    static void StashDir(string source, string dest)
    {
        if (!Directory.Exists(dest) && !File.Exists(dest))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            Directory.Move(source, dest);
            return;
        }
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            var target = Path.Combine(dest, entry.Name);
            if (entry is FileInfo) StashFile(entry.FullName, target);
            else if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) Directory.Move(entry.FullName, UniquePath(target));
            else StashDir(entry.FullName, target);
        }
        Directory.Delete(source);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Archives are untrusted: rejects any entry that could land outside the game folder.</summary>
    string NormalizeEntryPath(string entryName)
    {
        var rel = entryName.Replace('\\', '/').TrimEnd('/');
        if (rel.Length == 0 || rel.StartsWith('/'))
            throw new InvalidOperationException(L.F("허용되지 않는 압축 경로: {0}", entryName));

        var parts = rel.Split('/');
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var part in parts)
        {
            var stem = part.Split('.')[0];
            bool reserved = stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
                || (stem.Length == 4 && char.IsDigit(stem[3])
                    && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                        || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)));
            if (part.Length == 0 || part is "." or ".." || reserved || part.EndsWith('.') || part.EndsWith(' ')
                || part.IndexOfAny(invalid) >= 0)
                throw new InvalidOperationException(L.F("허용되지 않는 압축 경로: {0}", entryName));
        }
        // Only a folder that really is the manager's, inside the game folder, is off limits.
        if (Path.GetDirectoryName(ModDir)!.Equals(Root, StringComparison.OrdinalIgnoreCase)
            && parts[0].Equals(Path.GetFileName(ModDir), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.F("매니저 폴더에는 설치할 수 없습니다: {0}", entryName));

        var full = Path.GetFullPath(Path.Combine(Root, ToNative(rel)));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.F("허용되지 않는 압축 경로: {0}", entryName));
        return rel;
    }

    static string? ParentOf(string rel)
    {
        var i = rel.LastIndexOf('/');
        return i < 0 ? null : rel[..i];
    }

    static string ToNative(string rel) => rel.Replace('/', Path.DirectorySeparatorChar);
    string Full(string rel) => Path.Combine(Root, ToNative(rel));
    string TxDir(int id) => Path.Combine(HistoryDir, id.ToString("D4"));
    string BackupPath(int id, string rel) => Path.Combine(TxDir(id), "backup", ToNative(rel));
    string NewTmpPath() => Path.Combine(TmpDir, Guid.NewGuid().ToString("N"));

    /// <summary>A new, not yet created folder. It only appears on disk if something has to be kept.</summary>
    string NewLeftoverRun(int id) =>
        UniquePath(Path.Combine(LeftoversDir, $"{DateTime.Now:yyyyMMdd-HHmmss}-from-{id:D4}"));

    string? CurrentHash(string rel) => File.Exists(Full(rel)) ? HashFile(Full(rel)) : null;

    static string HashFile(string path)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return HashStream(s);
    }

    static string HashStream(Stream s) => Convert.ToHexString(SHA256.HashData(s));

    static void ClearReadOnly(string path)
    {
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
    }

    static void DeleteFile(string path)
    {
        ClearReadOnly(path);
        File.Delete(path);
    }

    static void MoveReplacing(string source, string dest)
    {
        ClearReadOnly(dest);
        File.Move(source, dest, overwrite: true);
    }

    static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        for (int i = 1; ; i++)
        {
            var candidate = $"{path}.{i}";
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    /// <summary>Lets the tests install into throwaway folders while the real game is open. The app never sets it.</summary>
    internal static bool IgnoreGameForTests;

    static void EnsureGameNotRunning()
    {
        if (!IgnoreGameForTests && GameRunning())
            throw new InvalidOperationException(L.T("게임이 실행 중입니다. 게임을 종료한 뒤 다시 시도해 주세요."));
    }

    void SaveTx(Tx tx)
    {
        var tmp = NewTmpPath();
        File.WriteAllText(tmp, JsonSerializer.Serialize(tx, JsonOpts));
        File.Move(tmp, Path.Combine(TxDir(tx.Id), "manifest.json"), overwrite: true);
    }

    void Log(string message) =>
        File.AppendAllText(Path.Combine(StateDir, "log.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}\r\n");
}
