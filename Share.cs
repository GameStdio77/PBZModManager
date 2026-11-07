namespace PBZModManager;

/// <summary>The clipboard, which checks replace so they never touch the user's own.</summary>
public static class ClipboardAccess
{
    public static Action<string> Put { get; set; } = text => Clipboard.SetText(text);
    public static Func<string> Take { get; set; } = () => Clipboard.ContainsText() ? Clipboard.GetText() : "";
}

/// <summary>A mod offered in the share window: whether it goes in, and whether its version is pinned.</summary>
public sealed class ShareChoice
{
    public required SharedMod Mod { get; init; }
    public bool Include { get; set; }
    public bool Pin { get; set; }
}

/// <summary>A line of the import window: a mod of the shared list and what the user already has of it.</summary>
public sealed class ImportChoice
{
    public required SharedMod Mod { get; init; }
    /// <summary>What this PC has of it: installed, listed, or nothing (a new place).</summary>
    public required string State { get; init; }
    public bool IsNew { get; init; }
    public bool Include { get; set; }
}

/// <summary>Columns of ticks toggled by a click on their cell, as in the install preview.</summary>
static class TickList
{
    public const string On = "■", Off = "□";

    public static string Tick(bool on) => on ? On : Off;

    /// <summary>The column a point is in.</summary>
    public static int ColumnAt(ListView list, int x)
    {
        int left = 0;
        for (int i = 0; i < list.Columns.Count; i++)
        {
            left += list.Columns[i].Width;
            if (x < left) return i;
        }
        return -1;
    }
}

/// <summary>Picks mods to share and writes them as a file or as text to paste.</summary>
public sealed class ShareDialog : Form
{
    readonly DarkListView list = new(1, (L.T("담기"), 50), (L.T("모드"), 280), (L.T("버전"), 90), (L.T("버전 고정"), 80), (L.T("상태"), 110));
    readonly Label status = new() { AutoSize = true, ForeColor = Theme.Muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly List<ShareChoice> choices;
    readonly Func<IReadOnlyList<ShareChoice>, SharedList> build;

    public ShareDialog(List<ShareChoice> choices, Func<IReadOnlyList<ShareChoice>, SharedList> build)
    {
        this.choices = choices;
        this.build = build;
        Text = L.T("모드 목록 공유");
        Theme.Apply(this);
        ClientSize = new Size(720, 520);
        MinimumSize = new Size(520, 360);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        var hint = new Label
        {
            Text = L.T("담을 모드를 고르세요. [버전 고정]을 켠 모드는 받는 쪽에 그 버전을 받도록 안내하고, 나머지는 최신 버전을 받도록 안내합니다. 칸을 눌러 바꿉니다."),
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 44,
            ForeColor = Theme.Muted,
            Padding = new Padding(12, 10, 12, 0),
        };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 4) };
        list.Dock = DockStyle.Fill;
        body.Controls.Add(list);

        var save = Theme.Button(L.T("파일로 저장..."), primary: true);
        var copy = Theme.Button(L.T("클립보드에 복사"));
        var close = Theme.Button(L.T("닫기"));
        close.DialogResult = DialogResult.OK;
        CancelButton = close;
        var bar = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.ControlHeight + 18, ColumnCount = 4, Padding = new Padding(10, 6, 10, 6) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(status, 0, 0);
        bar.Controls.Add(save, 1, 0);
        bar.Controls.Add(copy, 2, 0);
        bar.Controls.Add(close, 3, 0);
        foreach (var b in new[] { save, copy }) b.Margin = new Padding(0, 0, 6, 0);
        Controls.Add(body);
        Controls.Add(hint);
        Controls.Add(bar);

        list.MouseClick += (_, e) =>
        {
            if (list.GetItemAt(e.X, e.Y)?.Tag is not ShareChoice choice) return;
            var column = TickList.ColumnAt(list, e.X);
            if (column == 0) choice.Include = !choice.Include;
            else if (column == 3 && choice.Mod.Version.Length > 0) choice.Pin = !choice.Pin;
            else return;
            if (choice.Pin) choice.Include = true;
            Render();
        };
        save.Click += async (_, _) => { await Task.Yield(); SaveToFile(); };
        copy.Click += (_, _) => Copy();
        Render();
    }

    void Render()
    {
        list.SetRows(choices.Select(c => new ListRow(new[]
        {
            TickList.Tick(c.Include), c.Mod.Name, c.Mod.Version, c.Mod.Version.Length > 0 ? TickList.Tick(c.Pin) : "",
            c.Mod.Installed ? L.T("설치됨") : L.T("미설치"),
        }, c, c.Include ? null : Theme.Dim)).ToList());
        status.Text = L.F("{0}개 담음", choices.Count(c => c.Include));
    }

    /// <summary>The list to share; null, with a word to the user, when nothing is chosen.</summary>
    SharedList? Built()
    {
        var chosen = choices.Where(c => c.Include).ToList();
        if (chosen.Count == 0)
        {
            status.Text = L.T("담을 모드를 하나 이상 고르세요.");
            return null;
        }
        return build(chosen);
    }

    void SaveToFile()
    {
        if (Built() is not { } shared) return;
        using var dialog = new SaveFileDialog
        {
            Title = L.T("모드 목록 저장"),
            Filter = L.T("PBZ 모드 목록 (*.pbzmods.json)|*.pbzmods.json"),
            FileName = "PBZ mods" + ModListShare.FileExtension,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, ModListShare.ToJson(shared));
            status.Text = L.F("저장했습니다: {0}", Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DarkDialog.Show(this, ex.Message, Text, isError: true);
        }
    }

    public void Copy()
    {
        if (Built() is not { } shared) return;
        var text = ModListShare.ToText(shared);
        try
        {
            ClipboardAccess.Put(text);
            status.Text = L.F("클립보드에 복사했습니다 ({0}자). 받는 사람은 [목록 가져오기]에서 붙여 넣으면 됩니다.", text.Length);
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            DarkDialog.Show(this, ex.Message, Text, isError: true);
        }
    }
}

/// <summary>Shows what a shared list holds against what this PC has, and which new places to make.</summary>
public sealed class ImportDialog : Form
{
    readonly DarkListView list = new(1, (L.T("담기"), 50), (L.T("모드"), 280), (L.T("버전"), 110), (L.T("상태"), 130));
    readonly CheckBox orderBox = new()
    {
        Text = L.T("이미 설치된 모드의 순서·구분선도 공유받은 대로 맞추기"),
        AutoSize = true,
        ForeColor = Theme.Text,
        Dock = DockStyle.Fill,
        Padding = new Padding(4, 6, 0, 0),
    };
    readonly List<ImportChoice> choices;

    public bool ApplyOrder => orderBox.Checked;
    public IReadOnlyList<ImportChoice> Choices => choices;

    public ImportDialog(List<ImportChoice> choices, bool hasOrder)
    {
        this.choices = choices;
        Text = L.T("모드 목록 가져오기");
        Theme.Apply(this);
        ClientSize = new Size(720, 520);
        MinimumSize = new Size(520, 360);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        var hint = new Label
        {
            Text = L.T("체크한 새 모드는 모드 목록에 자리만 만듭니다. 내려받거나 설치하지 않습니다. 다운로드 페이지에서 받아 끌어다 놓으면 그 자리가 채워집니다."),
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 44,
            ForeColor = Theme.Muted,
            Padding = new Padding(12, 10, 12, 0),
        };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 4) };
        list.Dock = DockStyle.Fill;
        body.Controls.Add(list);

        var ok = Theme.Button(L.T("가져오기"), primary: true);
        ok.DialogResult = DialogResult.OK;
        var cancel = Theme.Button(L.T("취소"));
        cancel.DialogResult = DialogResult.Cancel;
        AcceptButton = ok;
        CancelButton = cancel;
        var bar = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.ControlHeight + 18, ColumnCount = 3, Padding = new Padding(10, 6, 10, 6) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        orderBox.Visible = hasOrder;
        bar.Controls.Add(orderBox, 0, 0);
        ok.Margin = new Padding(0, 0, 6, 0);
        bar.Controls.Add(ok, 1, 0);
        bar.Controls.Add(cancel, 2, 0);
        Controls.Add(body);
        Controls.Add(hint);
        Controls.Add(bar);

        list.MouseClick += (_, e) =>
        {
            if (list.GetItemAt(e.X, e.Y)?.Tag is not ImportChoice { IsNew: true } choice || TickList.ColumnAt(list, e.X) != 0) return;
            choice.Include = !choice.Include;
            Render();
        };
        Render();
    }

    void Render() =>
        list.SetRows(choices.Select(c => new ListRow(new[]
        {
            c.IsNew ? TickList.Tick(c.Include) : "",
            c.Mod.Name,
            c.Mod.Version.Length == 0 ? "" : c.Mod.Pinned ? L.F("{0} 고정", c.Mod.Version) : c.Mod.Version,
            c.State,
        }, c, c.IsNew && c.Include ? null : Theme.Dim)).ToList());
}
