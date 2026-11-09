namespace PBZModManager;

/// <summary>A listed mod as offered for a preset, as it is now.</summary>
public sealed class PresetChoice
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string Version { get; init; } = "";
    public bool Installed { get; init; }
    public List<string>? Options { get; init; }
    public List<string>? Excluded { get; init; }
    /// <summary>How the mod list shows it: under its page's heading, or as a branch of its page's main file.</summary>
    public string Label { get; init; } = "";
    /// <summary>A page's heading or the line between installed and other mods: shown, not put in.</summary>
    public bool Heading { get; init; }
}

/// <summary>A line of a preset as it is edited: a mod, or a separator.</summary>
public sealed class PresetLine
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public bool Pin { get; set; }
    public List<string>? Options { get; set; }
    public List<string>? Excluded { get; set; }
    /// <summary>The separator's text; null for a mod.</summary>
    public string? Separator { get; set; }
    public bool IsSeparator => Separator != null;

    public static PresetLine From(PresetChoice c) =>
        new() { Key = c.Key, Name = c.Name, Version = c.Version, Options = c.Options, Excluded = c.Excluded };

    public static PresetLine Line(string text) => new() { Separator = text };
}

/// <summary>What the preset windows need from the main window.</summary>
public sealed record PresetHost(
    Func<Preset, int> Differences,
    Func<Preset, bool> IsOff,
    Func<PresetMod, string> StateOf,
    Func<List<PresetChoice>> Choices,
    Func<Preset?, List<PresetLine>> StartLines,
    Func<Preset, Preset> Refresh,
    Action<List<Preset>> Save,
    Func<string, string>? FormatSeparator = null,
    Func<Preset, PresetMode, IReadOnlyList<Preset>, IWin32Window, Task>? ApplyPreset = null);

/// <summary>The user's presets: made from what is installed, edited, and applied in one of three ways.</summary>
public sealed class PresetDialog : Form
{
    readonly DarkListView presetList = new(0, (L.T("프리셋"), 180), (L.T("교체 그룹"), 70), (L.T("모드"), 50), (L.T("상태"), 100));
    readonly DarkListView modList = new(0, (L.T("모드"), 260), (L.T("버전"), 100), (L.T("지금 상태"), 120));
    readonly List<Preset> presets;
    readonly PresetHost host;
    readonly Button applyMatchButton = Theme.Button(L.T("이 구성으로 맞추기"), primary: true);
    readonly Button applyTurnOnButton = Theme.Button(L.T("모드 켜기"));
    readonly Button applyTurnOffButton = Theme.Button(L.T("모드 끄기"));
    readonly List<Button> needsPreset = new();

    /// <summary>The preset and the way to apply it the user chose; null when closed without applying.</summary>
    public Preset? Chosen { get; private set; }
    public PresetMode Mode { get; private set; }
    public IReadOnlyList<Preset> Presets => presets;

    public PresetDialog(List<Preset> presets, PresetHost host, string? select = null)
    {
        this.presets = presets;
        this.host = host;
        Text = L.T("프리셋");
        Theme.Apply(this);
        ClientSize = new Size(980, 560);
        MinimumSize = new Size(720, 400);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        var hint = new Label
        {
            Text = L.T("설치된 모드 구성을 저장해 두고 한 번에 적용합니다. 이 구성으로 맞출지(나머지 제거), 이 모드들만 켤지, 끌지 고릅니다. 교체 그룹이 같은 프리셋끼리는 같이 쓰지 않습니다."),
            Dock = DockStyle.Top, AutoSize = false, Height = 44, ForeColor = Theme.Muted, Padding = new Padding(12, 10, 12, 0),
        };
        var split = new DarkSplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        Theme.StyleSplit(split);
        split.Panel1.Padding = new Padding(12, 4, 0, 4);
        split.Panel2.Padding = new Padding(0, 4, 12, 4);
        split.Panel1.Controls.Add(PresetUi.Framed(presetList));
        split.Panel2.Controls.Add(PresetUi.Framed(modList));

        Button Add(string text, Action act, bool forPreset = true)
        {
            var button = Theme.Button(text);
            button.Margin = new Padding(0, 0, 6, 0);
            button.Click += async (_, _) => { await Task.Yield(); act(); };
            if (forPreset) needsPreset.Add(button);
            return button;
        }
        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = false, Padding = Padding.Empty };
        left.Controls.Add(Add(L.T("새로 만들기..."), CreateNew, forPreset: false));
        left.Controls.Add(Add(L.T("수정..."), EditChosen));
        left.Controls.Add(Add(L.T("현재 상태로 갱신"), RefreshChosen));
        left.Controls.Add(Add(L.T("복제"), CopyChosen));
        left.Controls.Add(Add(L.T("삭제"), DeleteChosen));
        var close = Theme.Button(L.T("닫기"));
        close.DialogResult = DialogResult.Cancel;
        CancelButton = close;

        applyMatchButton.Margin = new Padding(0, 0, 6, 0);
        applyTurnOnButton.Margin = new Padding(0, 0, 6, 0);
        applyTurnOffButton.Margin = new Padding(0, 0, 6, 0);
        close.Margin = Padding.Empty;

        needsPreset.Add(applyMatchButton);
        needsPreset.Add(applyTurnOnButton);
        needsPreset.Add(applyTurnOffButton);

        applyMatchButton.Click += async (_, _) => { await Task.Yield(); await Apply(PresetMode.Match); };
        applyTurnOnButton.Click += async (_, _) => { await Task.Yield(); await Apply(PresetMode.TurnOn); };
        applyTurnOffButton.Click += async (_, _) => { await Task.Yield(); await Apply(PresetMode.TurnOff); };

        var tip = new DarkToolTip();
        tip.SetToolTip(applyMatchButton, L.T("이 프리셋의 모드들만 설치하고, 프리셋에 없는 모드는 제거합니다."));
        tip.SetToolTip(applyTurnOnButton, L.T("현재 설치된 다른 모드는 그대로 두고, 이 프리셋의 모드들만 켭니다."));
        tip.SetToolTip(applyTurnOffButton, L.T("이 프리셋에 포함된 모드들만 끕니다."));

        var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Padding = Padding.Empty, Margin = Padding.Empty };
        right.Controls.Add(applyMatchButton);
        right.Controls.Add(applyTurnOnButton);
        right.Controls.Add(applyTurnOffButton);
        right.Controls.Add(close);

        var bar = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.ControlHeight + 18, ColumnCount = 2, Padding = new Padding(10, 6, 10, 6) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(left, 0, 0);
        bar.Controls.Add(right, 1, 0);
        Controls.Add(split);
        Controls.Add(hint);
        Controls.Add(bar);
        Load += (_, _) => split.SplitterDistance = Math.Max(300, ClientSize.Width * 9 / 20);

        presetList.SelectedIndexChanged += (_, _) => ShowChosen();
        presetList.DoubleClick += (_, _) => EditChosen();
        Render(select);
    }

    Preset? Selected => presetList.SelectedItems.Count > 0 ? presetList.SelectedItems[0].Tag as Preset : null;
    bool applying;

    async Task Apply(PresetMode mode)
    {
        if (applying) return;
        if (Selected is not { } preset) return;
        if (host.ApplyPreset == null) return;
        applying = true;
        UseWaitCursor = true;
        try
        {
            await host.ApplyPreset(preset, mode, presets, this);
        }
        finally
        {
            applying = false;
            if (!IsDisposed)
            {
                UseWaitCursor = false;
                Render(preset.Id);
                ShowChosen();
            }
        }
    }

    string StateOf(Preset preset)
    {
        if (preset.Mods.Count == 0) return L.T("비어 있음");
        if (host.IsOff(preset)) return L.T("꺼져 있음");
        var differences = host.Differences(preset);
        return differences == 0 ? L.T("적용됨") : L.F("{0}개 다름", differences);
    }

    void Render(string? select = null)
    {
        var keep = select ?? Selected?.Id;
        presetList.SetRows(presets.Select(p =>
        {
            var state = StateOf(p);
            return new ListRow(new[] { p.Name, p.Group, p.Mods.Count.ToString(), state }, p, state == L.T("적용됨") ? Theme.Info : null);
        }).ToList());
        presetList.KeepSelection(i => (i.Tag as Preset)?.Id == keep);
        ShowChosen();
    }

    void ShowChosen()
    {
        var preset = Selected;
        foreach (var button in needsPreset) button.Enabled = preset != null;
        if (preset == null)
        {
            modList.SetRows(new List<ListRow>());
            return;
        }
        var order = preset.Order.Count > 0 ? preset.Order : preset.Mods.Select(m => m.Key).ToList();
        modList.SetRows(order.Select(o =>
        {
            if (o.StartsWith(ModListShare.SeparatorPrefix, StringComparison.Ordinal))
            {
                var text = o[ModListShare.SeparatorPrefix.Length..];
                var formatted = host.FormatSeparator != null ? host.FormatSeparator(text) : Manager.FormatSeparator(text);
                return new ListRow(new[] { formatted, "", "" }, o, Theme.Info);
            }
            var e = preset.Mods.FirstOrDefault(m => m.Key.Equals(o, StringComparison.OrdinalIgnoreCase));
            if (e == null) return null;
            var state = host.StateOf(e);
            var version = e.Pinned ? L.F("{0} 고정", e.Version) : L.T("받은 최신");
            return new ListRow(new[] { e.Name, version, state }, e, state == L.T("설치됨") ? null : Theme.Warning);
        }).OfType<ListRow>().ToList());
    }

    void Save(string? select = null)
    {
        host.Save(presets);
        Render(select);
    }

    List<string> Groups() => presets.Select(p => p.Group.Trim()).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g).ToList();

    void CreateNew()
    {
        using var editor = new PresetEditor(L.T("새 프리셋"), NextName(), "", Groups(), host.Choices(), host.StartLines(null), host.FormatSeparator);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        var preset = editor.Build(new Preset { CreatedAt = DateTime.Now });
        presets.Add(preset);
        Save(preset.Id);
    }

    string NextName()
    {
        for (int i = 1; ; i++)
        {
            var name = L.F("프리셋 {0}", i);
            if (!presets.Any(p => p.Name == name)) return name;
        }
    }

    void EditChosen()
    {
        if (Selected is not { } preset) return;
        using var editor = new PresetEditor(L.F("프리셋 수정 - {0}", preset.Name), preset.Name, preset.Group, Groups(), host.Choices(), host.StartLines(preset), host.FormatSeparator);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        var edited = editor.Build(preset);
        presets[presets.IndexOf(preset)] = edited;
        Save(edited.Id);
    }

    void RefreshChosen()
    {
        if (Selected is not { } preset) return;
        if (DarkDialog.Show(this, L.F("{0}의 모드를 지금 설치된 버전·옵션·뺀 파일로 갱신할까요?\n지금 설치되어 있지 않은 모드는 그대로 둡니다. 모드를 더하거나 빼려면 [수정...]을 쓰세요.", preset.Name),
            L.T("현재 상태로 갱신"), yesNo: true) != DialogResult.Yes) return;
        var refreshed = host.Refresh(preset);
        presets[presets.IndexOf(preset)] = refreshed;
        Save(refreshed.Id);
    }

    void CopyChosen()
    {
        if (Selected is not { } preset) return;
        var copy = System.Text.Json.JsonSerializer.Deserialize<Preset>(System.Text.Json.JsonSerializer.Serialize(preset))!;
        copy.Id = Guid.NewGuid().ToString("N")[..8];
        copy.Name = L.F("{0} 복사본", preset.Name);
        copy.CreatedAt = copy.UpdatedAt = DateTime.Now;
        presets.Insert(presets.IndexOf(preset) + 1, copy);
        Save(copy.Id);
    }

    void DeleteChosen()
    {
        if (Selected is not { } preset) return;
        if (DarkDialog.Show(this, L.F("{0} 프리셋을 삭제할까요?\n설치된 모드는 그대로 둡니다.", preset.Name), L.T("프리셋 삭제"), yesNo: true) != DialogResult.Yes) return;
        presets.Remove(preset);
        Save();
    }
}

static class PresetUi
{
    public static Control Framed(DarkListView list)
    {
        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Theme.Border };
        frame.Controls.Add(new ScrollHost(list) { Dock = DockStyle.Fill });
        return frame;
    }
}

/// <summary>
/// A preset laid out like the main window: the mod list on the left, the preset's own installed list on the right,
/// in its order and with its separators. Mods move across; lines move up and down.
/// </summary>
public sealed class PresetEditor : Form
{
    readonly TextBox nameBox = Theme.TextBox();
    readonly DarkComboBox groupBox = new(ComboBoxStyle.DropDownList) { Width = 160 };
    readonly DarkListView left = new(0, (L.T("모드"), 220), (L.T("버전"), 70), (L.T("상태"), 80));
    readonly DarkListView right = new(1, (L.T("순번"), 44), (L.T("모드"), 220), (L.T("버전"), 70), (L.T("버전 고정"), 70));
    readonly Label status = new() { AutoSize = true, ForeColor = Theme.Muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly Button ok = Theme.Button(L.T("저장"), primary: true);
    readonly List<PresetChoice> choices;
    readonly List<string> groups;
    readonly Func<string, string>? formatSeparator;
    string group;

    public List<PresetLine> Lines { get; }
    public string PresetName => nameBox.Text.Trim();
    public string Group => group;

    public PresetEditor(string title, string name, string group, List<string> groups, List<PresetChoice> choices, List<PresetLine> lines, Func<string, string>? formatSeparator = null)
    {
        this.choices = choices;
        this.groups = groups;
        this.formatSeparator = formatSeparator;
        this.group = group.Trim();
        Lines = lines;
        Text = title;
        Theme.Apply(this);
        ClientSize = new Size(1020, 620);
        MinimumSize = new Size(760, 440);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        left.MultiSelect = right.MultiSelect = true;

        nameBox.Text = name;
        nameBox.MaxLength = 80;
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = Theme.ControlHeight * 2 + 26, ColumnCount = 3, RowCount = 2, Padding = new Padding(12, 8, 12, 0) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Label Label(string text) => new() { Text = text, AutoSize = true, ForeColor = Theme.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };
        top.Controls.Add(Label(L.T("이름")), 0, 0);
        var field = Theme.Field(nameBox);
        field.Dock = DockStyle.Fill;
        field.Margin = new Padding(0, 2, 0, 2);
        top.Controls.Add(field, 1, 0);
        top.SetColumnSpan(field, 2);
        top.Controls.Add(Label(L.T("교체 그룹")), 0, 1);
        groupBox.Margin = new Padding(0, 4, 8, 0);
        top.Controls.Add(groupBox, 1, 1);
        top.Controls.Add(new Label
        {
            Text = L.T("같은 그룹의 프리셋끼리는 같이 쓰지 않습니다. 적용하면 같은 그룹의 다른 프리셋 모드를 먼저 제거합니다."),
            AutoSize = true, ForeColor = Theme.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 0),
        }, 2, 1);
        FillGroups();
        groupBox.SelectionChangeCommitted += (_, _) => GroupChosen();

        // Left: the mod list. Middle: across. Right: the preset, with the buttons that arrange it.
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(12, 8, 12, 4) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        body.Controls.Add(Titled(L.T("모드 목록"), PresetUi.Framed(left)), 0, 0);
        var across = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Anchor = AnchorStyles.None, Padding = new Padding(8, 0, 8, 0), WrapContents = false };
        var put = Button(L.T("넣기 →"), PutIn);
        var take = Button(L.T("← 빼기"), TakeOut);
        put.Margin = new Padding(0, 0, 0, 8);
        across.Controls.Add(put);
        across.Controls.Add(take);
        body.Controls.Add(across, 1, 0);
        var arrange = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        foreach (var b in new[] { Button(L.T("위로"), () => MoveLines(-1)), Button(L.T("아래로"), () => MoveLines(1)), Button(L.T("구분선 추가..."), AddSeparator), Button(L.T("구분선 이름 바꾸기..."), RenameSeparator) })
        {
            b.Margin = new Padding(0, 0, 6, 0);
            arrange.Controls.Add(b);
        }
        var rightPanel = Titled(L.T("프리셋 (설치 순서)"), PresetUi.Framed(right));
        rightPanel.Controls.Add(arrange);
        body.Controls.Add(rightPanel, 2, 0);

        var cancel = Theme.Button(L.T("취소"));
        cancel.DialogResult = DialogResult.Cancel;
        CancelButton = cancel;
        ok.Margin = new Padding(0, 0, 6, 0);
        var bar = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.ControlHeight + 18, ColumnCount = 3, Padding = new Padding(10, 6, 10, 6) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(status, 0, 0);
        bar.Controls.Add(ok, 1, 0);
        bar.Controls.Add(cancel, 2, 0);
        Controls.Add(body);
        Controls.Add(top);
        Controls.Add(bar);

        left.DoubleClick += (_, _) => PutIn();
        right.DoubleClick += (_, _) =>
        {
            if (right.SelectedItems.Count == 1 && right.SelectedItems[0].Tag is PresetLine { IsSeparator: true }) RenameSeparator();
            else TakeOut();
        };
        right.MouseClick += (_, e) =>
        {
            if (right.GetItemAt(e.X, e.Y)?.Tag is not PresetLine { IsSeparator: false } line || TickList.ColumnAt(right, e.X) != 3 || line.Version.Length == 0) return;
            line.Pin = !line.Pin;
            Render();
        };
        ok.Click += (_, _) =>
        {
            if (PresetName.Length == 0)
            {
                DarkDialog.Show(this, L.T("프리셋 이름을 넣어 주세요."), Text);
                return;
            }
            DialogResult = DialogResult.OK;
        };
        Render();
    }

    static Panel Titled(string title, Control content)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        content.Dock = DockStyle.Fill;
        panel.Controls.Add(content);
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 22, ForeColor = Theme.Text, Font = Theme.TitleFont });
        return panel;
    }

    static Button Button(string text, Action act)
    {
        var button = Theme.Button(text);
        button.Click += (_, _) => act();
        return button;
    }

    void FillGroups()
    {
        groupBox.Items.Clear();
        groupBox.Items.Add(L.T("그룹 없음"));
        foreach (var g in groups.Concat(group.Length > 0 ? new[] { group } : Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase)) groupBox.Items.Add(g);
        groupBox.Items.Add(L.T("새 그룹..."));
        groupBox.SelectedIndex = group.Length == 0 ? 0 : groupBox.Items.IndexOf(groupBox.Items.Cast<string>().First(i => i.Equals(group, StringComparison.OrdinalIgnoreCase)));
    }

    void GroupChosen()
    {
        if (groupBox.SelectedIndex == 0) group = "";
        else if (groupBox.SelectedIndex == groupBox.Items.Count - 1)
        {
            var text = TextPrompt.Ask(this, L.T("새 그룹"), L.T("교체 그룹 이름 (예: A, 외형)"), "");
            if (!string.IsNullOrWhiteSpace(text))
            {
                group = text.Trim();
                if (!groups.Contains(group, StringComparer.OrdinalIgnoreCase)) groups.Add(group);
            }
            FillGroups();
        }
        else group = (string)groupBox.SelectedItem!;
    }

    /// <summary>Sets the group as choosing it would; empty for none.</summary>
    public void SetGroup(string value)
    {
        group = value.Trim();
        if (group.Length > 0 && !groups.Contains(group, StringComparer.OrdinalIgnoreCase)) groups.Add(group);
        FillGroups();
    }

    bool InPreset(string key) => Lines.Any(l => !l.IsSeparator && l.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Puts the mods picked on the left in, below the line picked on the right, or at the end.</summary>
    public void PutIn()
    {
        var picked = left.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<PresetChoice>().Where(c => !c.Heading && !InPreset(c.Key)).ToList();
        if (picked.Count == 0) return;
        var at = right.SelectedIndices.Count > 0 ? right.SelectedIndices.Cast<int>().Max() + 1 : Lines.Count;
        Lines.InsertRange(at, picked.Select(PresetLine.From));
        Render(picked.Select(PresetLine.From).Select(l => l.Key).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Takes the lines picked on the right out: mods back to the list, separators gone.</summary>
    public void TakeOut()
    {
        var picked = right.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<PresetLine>().ToList();
        if (picked.Count == 0) return;
        foreach (var line in picked) Lines.Remove(line);
        Render();
    }

    /// <summary>Moves the lines picked on the right up (negative) or down together.</summary>
    public void MoveLines(int delta)
    {
        var picked = right.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<PresetLine>().ToList();
        if (picked.Count == 0) return;
        var indexes = picked.Select(l => Lines.IndexOf(l)).OrderBy(i => i).ToList();
        if ((delta < 0 && indexes[0] == 0) || (delta > 0 && indexes[^1] == Lines.Count - 1)) return;
        foreach (var index in delta < 0 ? indexes : Enumerable.Reverse(indexes))
        {
            var line = Lines[index];
            Lines.RemoveAt(index);
            Lines.Insert(index + delta, line);
        }
        Render(keepLines: picked);
    }

    void AddSeparator()
    {
        var text = TextPrompt.Ask(this, L.T("구분선 추가"), L.T("구분선 이름"), "");
        if (string.IsNullOrWhiteSpace(text)) return;
        var line = PresetLine.Line(text.Trim());
        var at = right.SelectedIndices.Count > 0 ? right.SelectedIndices.Cast<int>().Min() : 0;
        Lines.Insert(at, line);
        Render(keepLines: new[] { line });
    }

    void RenameSeparator()
    {
        if (right.SelectedItems.Count != 1 || right.SelectedItems[0].Tag is not PresetLine { IsSeparator: true } line) return;
        var text = TextPrompt.Ask(this, L.T("구분선 이름 바꾸기"), L.T("구분선 이름"), line.Separator!);
        if (string.IsNullOrWhiteSpace(text)) return;
        line.Separator = text.Trim();
        Render(keepLines: new[] { line });
    }

    void Render(IReadOnlySet<string>? keepKeys = null, IReadOnlyCollection<PresetLine>? keepLines = null)
    {
        left.SetRows(choices.Select(c => c.Heading
            ? new ListRow(new[] { c.Label, "", "" }, c, Theme.Info, IsSeparator: c.Label.Length == 0)
            : new ListRow(new[] { c.Label.Length > 0 ? c.Label : c.Name, c.Version, c.Installed ? L.T("설치됨") : L.T("미설치") }, c,
                InPreset(c.Key) ? Theme.Dim : null)).ToList());
        int number = 0;
        right.SetRows(Lines.Select(l => l.IsSeparator
            ? new ListRow(new[] { "", formatSeparator != null ? formatSeparator(l.Separator!) : Manager.FormatSeparator(l.Separator!), "", "" }, l, Theme.Info)
            : new ListRow(new[] { (++number).ToString(), l.Name, l.Version, l.Version.Length > 0 ? TickList.Tick(l.Pin) : "" }, l,
                choices.Any(c => !c.Heading && c.Key.Equals(l.Key, StringComparison.OrdinalIgnoreCase)) ? null : Theme.Warning)).ToList());
        if (keepKeys != null || keepLines != null)
            foreach (ListViewItem item in right.Items)
                item.Selected = item.Tag is PresetLine l && (keepLines?.Contains(l) == true || (!l.IsSeparator && keepKeys?.Contains(l.Key) == true));
        var count = Lines.Count(l => !l.IsSeparator);
        status.Text = L.F("모드 {0}개 담음", count);
        ok.Enabled = count > 0;
    }

    /// <summary>The preset as edited: its mods and its order, separators included, from the right-hand list.</summary>
    public Preset Build(Preset basis) => new()
    {
        Id = basis.Id,
        Name = PresetName,
        Group = Group,
        CreatedAt = basis.CreatedAt == default ? DateTime.Now : basis.CreatedAt,
        UpdatedAt = DateTime.Now,
        Mods = Lines.Where(l => !l.IsSeparator)
            .Select(l => new PresetMod { Key = l.Key, Name = l.Name, Version = l.Version, Pinned = l.Pin, Options = l.Options, Excluded = l.Excluded }).ToList(),
        Order = Lines.Select(l => l.IsSeparator ? ModListShare.SeparatorPrefix + l.Separator : l.Key).ToList(),
    };
}

/// <summary>What applying a preset will do, mod by mod, for the user to agree to before anything changes.</summary>
public sealed class PresetPreview : Form
{
    readonly DarkListView list = new(1, (L.T("작업"), 150), (L.T("모드"), 300), (L.T("내용"), 200), (L.T("최신으로 대신"), 100));
    readonly List<PresetStep> steps;
    readonly HashSet<PresetStep> useNewest = new();

    public PresetPreview(string presetName, PresetMode mode, List<PresetStep> steps, string group)
    {
        this.steps = steps;
        Text = L.F("프리셋 적용 - {0}", presetName);
        Theme.Apply(this);
        ClientSize = new Size(860, 540);
        MinimumSize = new Size(600, 380);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        var how = mode switch
        {
            PresetMode.Match => L.T("이 구성으로 맞춥니다. 프리셋에 없는 설치된 모드는 제거합니다."),
            PresetMode.TurnOn => L.T("프리셋의 모드를 켭니다. 다른 모드는 그대로 둡니다."),
            _ => L.T("프리셋의 모드를 끕니다(제거). 다른 모드는 그대로 둡니다."),
        };
        if (mode != PresetMode.TurnOff && group.Trim().Length > 0 && steps.Any(s => s.Action == PresetAction.Remove && s.Note.Length > 0))
            how += " " + L.F("교체 그룹 {0}의 다른 프리셋 모드를 먼저 제거합니다.", group.Trim());
        var count = (PresetAction a) => steps.Count(s => s.Action == a);
        var summary = how + "\n" + L.F("설치 {0} · 업데이트 {1} · 다시 설치 {2} · 제거 {3} · 그대로 {4} · 건너뜀 {5}",
            count(PresetAction.Install), count(PresetAction.Update), count(PresetAction.Reinstall), count(PresetAction.Remove), count(PresetAction.Keep), count(PresetAction.Skip));
        var top = new Label { Text = summary, Dock = DockStyle.Top, AutoSize = false, Height = 52, ForeColor = Theme.Text, Padding = new Padding(12, 10, 12, 0) };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 4) };
        body.Controls.Add(PresetUi.Framed(list));

        var ok = Theme.Button(L.T("적용"), primary: true);
        ok.DialogResult = DialogResult.OK;
        var cancel = Theme.Button(L.T("취소"));
        cancel.DialogResult = DialogResult.Cancel;
        CancelButton = cancel;
        ok.Margin = new Padding(0, 0, 6, 0);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);
        Controls.Add(body);
        Controls.Add(top);
        Controls.Add(bar);

        list.MouseClick += (_, e) =>
        {
            if (list.GetItemAt(e.X, e.Y)?.Tag is not PresetStep { Fallback: not null } step || TickList.ColumnAt(list, e.X) != 3) return;
            if (!useNewest.Remove(step)) useNewest.Add(step);
            Render();
        };
        Render();
    }

    static int Rank(PresetAction a) => a switch
    {
        PresetAction.Remove => 0, PresetAction.Install => 1, PresetAction.Update => 2, PresetAction.Reinstall => 3, PresetAction.Skip => 4, _ => 5,
    };

    static string ActionText(PresetAction a) => a switch
    {
        PresetAction.Install => L.T("설치"),
        PresetAction.Update => L.T("버전 바꾸기"),
        PresetAction.Reinstall => L.T("다시 설치"),
        PresetAction.Remove => L.T("제거"),
        PresetAction.Skip => L.T("건너뜀"),
        _ => L.T("그대로"),
    };

    void Render()
    {
        list.SetRows(steps.OrderBy(s => Rank(s.Action)).Select(s => new ListRow(new[]
        {
            ActionText(s.Action), s.Name, s.Note, s.Fallback != null ? TickList.Tick(useNewest.Contains(s)) + " " + s.Fallback.Version : "",
        }, s, s.Action switch
        {
            PresetAction.Skip => Theme.Warning,
            PresetAction.Remove => s.Note.Length > 0 ? Theme.Warning : Theme.Danger,
            PresetAction.Keep => Theme.Dim,
            _ => (Color?)null,
        })).ToList());
    }

    /// <summary>The steps as agreed: a skipped pinned version the user let the newest stand in for becomes an install.</summary>
    public List<PresetStep> Agreed() => steps.Select(s =>
        s.Action == PresetAction.Skip && s.Fallback != null && useNewest.Contains(s)
            ? s with { Action = s.Installed == null ? PresetAction.Install : PresetAction.Update, Version = s.Fallback, Note = s.Fallback.Version }
            : s).ToList();
}

/// <summary>Asks for one line of text.</summary>
public static class TextPrompt
{
    public static string? Ask(IWin32Window owner, string title, string label, string text)
    {
        using var form = new Form
        {
            Text = title, ClientSize = new Size(420, 120), StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog,
        };
        Theme.Apply(form);
        var box = Theme.TextBox();
        box.Text = text;
        box.MaxLength = 80;
        var field = Theme.Field(box);
        field.SetBounds(14, 40, 392, Theme.ControlHeight);
        form.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Theme.Muted, Location = new Point(14, 14) });
        form.Controls.Add(field);
        var ok = Theme.Button(L.T("확인"), primary: true);
        ok.DialogResult = DialogResult.OK;
        var cancel = Theme.Button(L.T("취소"));
        cancel.DialogResult = DialogResult.Cancel;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);
        form.Controls.Add(bar);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Shown += (_, _) => { box.Focus(); box.SelectAll(); };
        return form.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
    }
}

public sealed partial class MainForm
{
    readonly Button presetButton = Theme.Button(L.T("프리셋..."));

    PresetHost MakePresetHost(IReadOnlyCollection<ModGroup>? startWith = null) => new(
        Differences: p => PresetPlanner.Differences(p, mods, installed),
        IsOff: p => PresetPlanner.IsOff(p, installed),
        StateOf: e =>
        {
            var key = ModIdentity.Follow(e.Key);
            var mod = mods.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            var now = installed.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (mod == null) return L.T("모드 목록에 없음");
            if (now == null) return L.T("미설치");
            if (e.Pinned && !now.Version.Equals(e.Version, StringComparison.OrdinalIgnoreCase)) return L.F("{0} 설치됨", now.Version);
            return L.T("설치됨");
        },
        Choices: PresetChoices,
        StartLines: p => p == null ? StartLines(startWith) : LinesOf(p),
        Refresh: RefreshPreset,
        Save: list =>
        {
            try { manager.SavePresets(list); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { DarkDialog.Show(this, ex.Message, L.T("프리셋"), isError: true); }
        },
        FormatSeparator: manager.FormatSeparator,
        ApplyPreset: (preset, mode, all, owner) => ApplyPreset(preset, mode, all, owner));

    /// <summary>Every listed mod as the mod list shows it, grouped and sorted the same, whatever its filter.</summary>
    List<PresetChoice> PresetChoices()
    {
        var choices = new List<PresetChoice>();
        foreach (var row in ModListRows(Manager.AllCategories))
        {
            var label = row.Cells.Length > 1 ? row.Cells[1] ?? "" : "";
            if (row.Tag is ModGroup mod)
            {
                var c = ChoiceOf(mod);
                choices.Add(new PresetChoice { Key = c.Key, Name = c.Name, Version = c.Version, Installed = c.Installed, Options = c.Options, Excluded = c.Excluded, Label = label });
            }
            else if (row.Tag is PageHeading heading)
                choices.Add(new PresetChoice { Key = "heading:" + heading.Page, Name = label, Label = label, Heading = true });
            else if (row.IsSeparator)
                choices.Add(new PresetChoice { Key = "divider", Name = "", Label = "", Heading = true });
        }
        return choices;
    }

    PresetChoice ChoiceOf(ModGroup m)
    {
        var now = installed.FirstOrDefault(i => i.Key.Equals(m.Key, StringComparison.OrdinalIgnoreCase));
        return new PresetChoice
        {
            Key = m.Key, Name = m.Shown, Version = now?.Version ?? m.Latest.Version, Installed = now != null,
            Options = now?.Installs[^1].Options.ToList(), Excluded = now?.Installs[^1].Excluded.ToList(),
        };
    }

    /// <summary>
    /// A new preset's lines: the mods given (from a right click), in the installed order, each under the separator
    /// it sits under; with none given, every installed mod and separator. The installed list always keeps a line
    /// picked, so what is picked there is not taken as a choice.
    /// </summary>
    List<PresetLine> StartLines(IReadOnlyCollection<ModGroup>? given)
    {
        var keys = (given?.Select(m => m.Key) ?? Enumerable.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var all = keys.Count == 0;
        var lines = new List<PresetLine>();
        string? section = null;
        bool sectionAdded = false;
        foreach (var row in installedRows)
        {
            if (row.IsSeparator)
            {
                section = row.Text;
                sectionAdded = false;
                if (all) { lines.Add(PresetLine.Line(row.Text)); sectionAdded = true; }
                continue;
            }
            if (!all && !keys.Remove(row.Mod!.Key)) continue;
            if (section != null && !sectionAdded) { lines.Add(PresetLine.Line(section)); sectionAdded = true; }
            if (GroupOf(row.Mod!) is { } group) lines.Add(PresetLine.From(ChoiceOf(group)));
        }
        // Mods given that are not installed go last.
        foreach (var key in keys)
            if (mods.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } mod) lines.Add(PresetLine.From(ChoiceOf(mod)));
        return lines;
    }

    /// <summary>A saved preset's lines, in its order; one from before orders were kept goes by its mods.</summary>
    static List<PresetLine> LinesOf(Preset preset)
    {
        var order = preset.Order.Count > 0 ? preset.Order : preset.Mods.Select(m => m.Key).ToList();
        var lines = new List<PresetLine>();
        foreach (var entry in order)
        {
            if (entry.StartsWith(ModListShare.SeparatorPrefix, StringComparison.Ordinal))
                lines.Add(PresetLine.Line(entry[ModListShare.SeparatorPrefix.Length..]));
            else if (preset.Mods.FirstOrDefault(m => m.Key.Equals(entry, StringComparison.OrdinalIgnoreCase)) is { } e)
                lines.Add(new PresetLine { Key = ModIdentity.Follow(e.Key), Name = e.Name, Version = e.Version, Pin = e.Pinned, Options = e.Options, Excluded = e.Excluded });
        }
        foreach (var e in preset.Mods.Where(m => !order.Contains(m.Key, StringComparer.OrdinalIgnoreCase)))
            lines.Add(new PresetLine { Key = ModIdentity.Follow(e.Key), Name = e.Name, Version = e.Version, Pin = e.Pinned, Options = e.Options, Excluded = e.Excluded });
        return lines;
    }

    /// <summary>The preset's mods as they are installed now: version, options and left-out files. Others stay as saved.</summary>
    Preset RefreshPreset(Preset preset)
    {
        foreach (var entry in preset.Mods)
        {
            var now = installed.FirstOrDefault(i => i.Key.Equals(ModIdentity.Follow(entry.Key), StringComparison.OrdinalIgnoreCase));
            if (now == null) continue;
            var index = preset.Order.FindIndex(o => o.Equals(entry.Key, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) preset.Order[index] = now.Key;
            entry.Key = now.Key;
            entry.Version = now.Version;
            entry.Options = now.Installs[^1].Options.ToList();
            entry.Excluded = now.Installs[^1].Excluded.ToList();
            if (GroupOf(now) is { } group) entry.Name = group.Shown;
        }
        preset.UpdatedAt = DateTime.Now;
        return preset;
    }

    /// <summary>Opens the presets dialog.</summary>
    void OpenPresets(string? select = null)
    {
        using var dialog = new PresetDialog(manager.LoadPresets(), MakePresetHost(), select);
        dialog.ShowDialog(this);
    }

    async Task ApplyPreset(Preset preset, PresetMode mode, IReadOnlyList<Preset> all, IWin32Window? owner = null)
    {
        owner ??= this;
        var steps = PresetPlanner.Plan(preset, mode, mods, installed, all);
        var order = preset.Order.Count > 0 ? preset.Order : preset.Mods.Select(m => m.Key).ToList();
        var orderToApply = mode != PresetMode.TurnOff && order.Count > 0;
        if (steps.All(s => s.Action is PresetAction.Keep or PresetAction.Skip) && !steps.Any(s => s.Fallback != null))
        {
            if (orderToApply) { manager.ApplyPresetOrder(order, mode == PresetMode.Match); await RefreshAll(); }
            var skipped = steps.Where(s => s.Action == PresetAction.Skip).Select(s => $"  {s.Name}: {s.Note}").ToList();
            DarkDialog.Show(owner, L.F("{0}: 바꿀 모드가 없습니다.", preset.Name) + (skipped.Count > 0 ? "\n\n" + L.T("건너뛴 모드:") + "\n" + string.Join("\n", skipped) : ""), L.T("프리셋"));
            return;
        }
        using (var preview = new PresetPreview(preset.Name, mode, steps, preset.Group))
        {
            if (preview.ShowDialog(owner) != DialogResult.OK) return;
            steps = preview.Agreed();
        }

        var errors = new List<string>();
        var removing = steps.Where(s => s.Action == PresetAction.Remove && s.Installed != null).ToList();
        if (removing.Count > 0)
        {
            var ids = removing.SelectMany(s => s.Installed!.Installs).Select(t => t.Id).ToList();
            var removed = await Run(L.T("제거 중..."), p => new UndoResult(manager.RemoveMany(ids, p)));
            if (removed == null) errors.Add(L.T("제거하지 못했습니다."));
            await RefreshAll();
        }

        int done = 0;
        foreach (var step in steps.Where(s => s.Action is PresetAction.Install or PresetAction.Update or PresetAction.Reinstall))
        {
            var mod = mods.FirstOrDefault(m => m.Key.Equals(step.Key, StringComparison.OrdinalIgnoreCase));
            var version = mod?.Versions.FirstOrDefault(v => v.FileName.Equals(step.Version?.FileName, StringComparison.OrdinalIgnoreCase));
            if (mod == null || version == null || version.Missing) { errors.Add(L.F("{0}: 파일 없음", step.Name)); continue; }
            var problem = await InstallOne(mod, version, step.Entry?.Options, step.Entry?.Excluded);
            if (problem.Length > 0) errors.Add($"{step.Name}: {problem}");
            else done++;
        }
        await RefreshAll();
        if (orderToApply)
        {
            try { manager.ApplyPresetOrder(order, mode == PresetMode.Match); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add(ex.Message); }
            await RefreshAll();
        }
        await FollowLoaderChange();
        SetStatus(L.F("{0} 적용: 설치·변경 {1}개, 제거 {2}개", preset.Name, done, removing.Count));
        if (errors.Count > 0) DarkDialog.Show(owner, L.T("적용하지 못한 모드:") + "\n" + string.Join("\n", errors), L.T("프리셋"), isError: true);
    }

    /// <summary>
    /// Installs one version with the options and left-out files given; none given reuses the last ones, and a mod
    /// with options never chosen asks for them. Returns what went wrong, empty when it went in.
    /// </summary>
    async Task<string> InstallOne(ModGroup mod, ModVersion version, List<string>? options, List<string>? excluded)
    {
        var groups = await Run(L.T("압축 파일 확인 중..."), _ => manager.OptionGroups(version.Path));
        if (groups == null) return L.T("압축 파일을 읽지 못했습니다.");
        if (groups.Count == 0) options = null;
        else if (options == null || options.Count == 0)
        {
            if (manager.PreviousOptions(version.Path).Count > 0) options = null;
            else
            {
                using var dialog = new OptionsDialog(mod.Shown, groups, Array.Empty<string>());
                if (dialog.ShowDialog(this) != DialogResult.OK) return L.T("옵션을 고르지 않아 건너뜀");
                options = dialog.Chosen;
            }
        }
        var result = await Run(L.F("{0} 설치 중...", mod.Shown), p =>
        {
            try
            {
                var plan = manager.BuildPlan(version.Path, options, excluded, progress: p);
                if (plan.HasChanges) manager.Install(plan, p);
                return "";
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return ex.Message;
            }
        });
        return result ?? L.T("설치하지 못했습니다.");
    }

    /// <summary>Puts mods at the end of a preset, as their version, options and left-out files are now.</summary>
    void AddToPreset(Preset preset, IEnumerable<ModGroup> chosen)
    {
        var presets = manager.LoadPresets();
        var target = presets.FirstOrDefault(p => p.Id == preset.Id);
        if (target == null) return;
        if (target.Order.Count == 0) target.Order.AddRange(target.Mods.Select(m => m.Key));
        foreach (var mod in chosen)
        {
            if (target.Mods.Any(e => ModIdentity.Follow(e.Key).Equals(mod.Key, StringComparison.OrdinalIgnoreCase))) continue;
            var c = ChoiceOf(mod);
            target.Mods.Add(new PresetMod { Key = c.Key, Name = c.Name, Version = c.Version, Options = c.Options, Excluded = c.Excluded });
            target.Order.Add(c.Key);
        }
        target.UpdatedAt = DateTime.Now;
        MakePresetHost().Save(presets);
        SetStatus(L.F("{0}에 넣었습니다.", target.Name));
    }

    /// <summary>"Add to preset ▸" for the mods given: each preset, and a new one made of them.</summary>
    ToolStripMenuItem PresetMenu(List<ModGroup> chosen)
    {
        var item = new ToolStripMenuItem(L.T("프리셋에 넣기")) { Enabled = chosen.Count > 0 };
        item.DropDown.Renderer = new DarkMenuRenderer();
        item.DropDown.BackColor = Theme.Surface;
        item.DropDown.ForeColor = Theme.Text;
        ((ToolStripDropDownMenu)item.DropDown).ShowImageMargin = false;
        foreach (var preset in manager.LoadPresets())
            item.DropDownItems.Add(Item(preset.Name, true, () => AddToPreset(preset, chosen)));
        if (item.DropDownItems.Count > 0) item.DropDownItems.Add(new ToolStripSeparator());
        item.DropDownItems.Add(Item(L.T("새 프리셋..."), true, () => NewPresetFrom(chosen)));
        return item;
    }

    /// <summary>Opens the editor with the mods given, under their separators, and keeps the preset it makes.</summary>
    void NewPresetFrom(List<ModGroup> chosen)
    {
        var presets = manager.LoadPresets();
        var host = MakePresetHost(chosen);
        var groups = presets.Select(p => p.Group.Trim()).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        using var editor = new PresetEditor(L.T("새 프리셋"), chosen.Count == 1 ? chosen[0].Shown : L.F("프리셋 {0}", presets.Count + 1), "", groups, host.Choices(), host.StartLines(null), host.FormatSeparator);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        presets.Add(editor.Build(new Preset { CreatedAt = DateTime.Now }));
        host.Save(presets);
        SetStatus(L.F("{0}에 넣었습니다.", editor.PresetName));
    }
}
