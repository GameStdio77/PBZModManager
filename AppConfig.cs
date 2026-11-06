using System.Text.Json;

namespace PBZModManager;

/// <summary>
/// Finds the game folder. The mod folder is always the folder this program sits in, with the records in its
/// .manager folder; the game folder used with it is saved there too, so nothing is kept outside.
/// </summary>
public static class GameFolder
{
    /// <summary>The settings file older versions kept beside the exe; read once for its game folder, then removed.</summary>
    const string OldPointer = "PBZModManager.config.json";

    /// <summary>Where mods and records are kept: this program's own folder.</summary>
    public static string ModDir => Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    /// <summary>A folder that holds Phantom Blade Zero: its ProjectPBZ and platform folders.</summary>
    public static bool IsGameFolder(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(Path.Combine(path, "ProjectPBZ")) && Directory.Exists(Path.Combine(path, "platform"));

    /// <summary>
    /// The game folder: the one given, the one saved with the records, the one this exe sits in, or a Steam
    /// library on any drive. Null when none is found; the user is then asked.
    /// </summary>
    public static string? Find(string? given, string modDir)
    {
        if (IsGameFolder(given)) return Path.GetFullPath(given!);
        if (Manager.SavedGameRoot(modDir) is { } saved && IsGameFolder(saved)) return Path.GetFullPath(saved);
        if (ReadOldPointer(modDir) is { } old && IsGameFolder(old)) return Path.GetFullPath(old);
        for (var dir = new DirectoryInfo(modDir); dir != null; dir = dir.Parent)
            if (IsGameFolder(dir.FullName)) return dir.FullName;
        return SteamCandidates().FirstOrDefault(IsGameFolder);
    }

    /// <summary>Takes the game folder out of an old settings file beside the exe and removes the file.</summary>
    static string? ReadOldPointer(string modDir)
    {
        var path = Path.Combine(modDir, OldPointer);
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement.TryGetProperty("GameRoot", out var g) ? g.GetString() : null;
            File.Delete(path);
            return root;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Removes the old settings file beside the exe, if one is left.</summary>
    public static void ForgetOldPointer(string modDir)
    {
        try
        {
            var path = Path.Combine(modDir, OldPointer);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unused either way.
        }
    }

    /// <summary>The usual Steam places on every drive, and the libraries Steam's own list names.</summary>
    static IEnumerable<string> SteamCandidates()
    {
        const string game = @"steamapps\common\PBZ";
        var steams = new List<string>();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var root = drive.RootDirectory.FullName;
            steams.AddRange(new[] { @"Program Files (x86)\Steam", @"Program Files\Steam", "Steam", "SteamLibrary", @"Games\Steam", @"Games\SteamLibrary" }
                .Select(s => Path.Combine(root, s)));
        }
        foreach (var steam in steams.ToList())
        {
            var list = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(list)) continue;
            string text;
            try { text = File.ReadAllText(list); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\""))
                steams.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        return steams.Distinct(StringComparer.OrdinalIgnoreCase).Select(s => Path.Combine(s, game));
    }
}

/// <summary>Picks the game folder. The mod folder is shown: it is where this program sits.</summary>
public sealed class FolderSettingsDialog : Form
{
    readonly TextBox gameBox = Theme.TextBox();
    readonly Label status = new() { AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.Muted, Padding = new Padding(0, 8, 0, 0), MaximumSize = new Size(620, 0) };

    public string GameRoot => gameBox.Text.Trim();

    public FolderSettingsDialog(string gameRoot, string modDir)
    {
        Text = L.T("폴더 설정");
        Theme.Apply(this);
        ClientSize = new Size(660, 230);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        gameBox.Text = gameRoot;
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(14, 14, 14, 0), AutoSize = true };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        body.Controls.Add(new Label { Text = L.T("게임 폴더"), AutoSize = true, ForeColor = Theme.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 0) }, 0, 0);
        var field = Theme.Field(gameBox);
        field.Dock = DockStyle.Fill;
        field.Margin = new Padding(0, 4, 6, 4);
        body.Controls.Add(field, 1, 0);
        var browse = Theme.Button(L.T("찾아보기..."));
        browse.Margin = new Padding(0, 4, 0, 4);
        browse.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = L.T("Phantom Blade Zero 게임 폴더 (PBZ)"), UseDescriptionForTitle = true, SelectedPath = Directory.Exists(gameBox.Text) ? gameBox.Text : "" };
            if (picker.ShowDialog(this) == DialogResult.OK) gameBox.Text = picker.SelectedPath;
        };
        body.Controls.Add(browse, 2, 0);
        var modNote = new Label
        {
            AutoSize = true,
            ForeColor = Theme.Muted,
            MaximumSize = new Size(620, 0),
            Margin = new Padding(0, 10, 0, 0),
            Text = L.F("모드 보관 폴더는 이 프로그램이 있는 폴더입니다: {0}\n받은 압축 파일과 설치 기록(.manager)이 이곳에 있습니다. 다른 폴더를 쓰려면 프로그램을 그 폴더로 옮겨 실행하세요.", modDir),
        };
        body.Controls.Add(modNote, 0, 1);
        body.SetColumnSpan(modNote, 3);
        body.Controls.Add(status, 0, 2);
        body.SetColumnSpan(status, 3);

        var ok = Theme.Button(L.T("저장"), primary: true);
        var cancel = Theme.Button(L.T("취소"));
        cancel.DialogResult = DialogResult.Cancel;
        CancelButton = cancel;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);
        Controls.Add(body);
        Controls.Add(bar);

        ok.Click += (_, _) =>
        {
            if (!GameFolder.IsGameFolder(GameRoot))
            {
                status.ForeColor = Theme.Danger;
                status.Text = L.T("게임 폴더가 아닙니다. ProjectPBZ와 platform 폴더가 있는 PBZ 폴더를 골라 주세요.");
                return;
            }
            DialogResult = DialogResult.OK;
        };
    }
}
