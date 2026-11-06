using System.Diagnostics;

namespace PBZModManager;

/// <summary>The category, address and memo fields, shared by the main window and the add dialog.</summary>
public sealed class ModInfoEditor : TableLayoutPanel
{
    readonly Label title = new() { AutoEllipsis = true, Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, Height = 22 };
    readonly DarkComboBox versionBox = new(ComboBoxStyle.DropDownList);
    readonly Label versionLabel = FieldLabel("버전");
    readonly Button downloadButton = Theme.Button("다운로드 페이지");
    readonly Panel versionRow;
    readonly DarkComboBox categoryBox = new(ComboBoxStyle.DropDownList);
    readonly DarkComboBox toolBox = new(ComboBoxStyle.DropDownList);
    readonly TextBox urlBox = Theme.TextBox();
    readonly TextBox memoBox = Theme.TextBox();
    readonly TextBox notesBox = Theme.TextBox(multiline: true);
    readonly Button refetchButton = Theme.Button("다시 가져오기");
    readonly Button showNotesButton = Theme.Button("크게 보기...");
    readonly Panel notesField;
    /// <summary>Below this height the steps are not worth showing inline; the button opens them in a window.</summary>
    const int NotesMinHeight = 70;
    readonly Button manageButton = Theme.Button("관리...");
    readonly Button openUrlButton = Theme.Button("열기");
    // Waits for typing to stop, so Nexus is asked about a finished address and not about every keystroke.
    readonly System.Windows.Forms.Timer addressPause = new() { Interval = 700 };
    bool loading;

    /// <summary>The user changed a field. Loading values through <see cref="Show"/> does not raise this.</summary>
    public event Action? Edited;
    public event Action? ManageRequested;
    /// <summary>The user picked another entry in the version list. Gives its index.</summary>
    public event Action<int>? VersionChosen;
    public event Action? DownloadRequested;
    /// <summary>An entered address gave a category, and none was chosen. Gives its name.</summary>
    public event Action<string>? CategoryFound;

    public ModInfoEditor()
    {
        BackColor = Theme.Background;
        addressPause.Tick += async (_, _) => await LookUpCategory(refetch: false);
        Disposed += (_, _) => addressPause.Dispose();
        ColumnCount = 2;
        RowCount = 8;
        Padding = new Padding(8);
        Dock = DockStyle.Fill;
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 7; i++) RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Controls.Add(title, 0, 0);
        SetColumnSpan(title, 2);

        Controls.Add(versionLabel, 0, 1);
        versionBox.Dock = DockStyle.Fill;
        versionRow = Row(versionBox, downloadButton);
        Controls.Add(versionRow, 1, 1);
        versionLabel.Visible = versionRow.Visible = false;
        downloadButton.Click += (_, _) => DownloadRequested?.Invoke();

        Controls.Add(FieldLabel("카테고리"), 0, 2);
        categoryBox.Dock = DockStyle.Fill;
        Controls.Add(Row(categoryBox, manageButton), 1, 2);

        Controls.Add(FieldLabel("실행 도구"), 0, 3);
        toolBox.Dock = DockStyle.Fill;
        toolBox.Margin = new Padding(0, 3, 0, 3);
        Controls.Add(toolBox, 1, 3);
        SetTools("");

        Controls.Add(FieldLabel("주소"), 0, 4);
        var urlField = Theme.Field(urlBox);
        urlField.Dock = DockStyle.Fill;
        Controls.Add(Row(urlField, openUrlButton), 1, 4);

        // A short note of one line, shown in the installed list too.
        Controls.Add(FieldLabel("메모"), 0, 5);
        memoBox.MaxLength = 200;
        var memoField = Theme.Field(memoBox);
        memoField.Dock = DockStyle.Fill;
        memoField.Margin = new Padding(0, 3, 0, 3);
        Controls.Add(memoField, 1, 5);

        // Install steps come from the mod's page; they are read here, not typed.
        var notesHeader = new Panel { Dock = DockStyle.Fill, Height = Theme.ControlHeight, Margin = new Padding(0, 6, 0, 2) };
        refetchButton.Dock = DockStyle.Right;
        showNotesButton.Dock = DockStyle.Right;
        notesHeader.Controls.Add(new Label { Text = "설치 방법 (Nexus 설명에서 가져옴)", AutoSize = true, ForeColor = Theme.Muted, Dock = DockStyle.Left, Padding = new Padding(0, 6, 0, 0) });
        notesHeader.Controls.Add(showNotesButton);
        notesHeader.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 6 });
        notesHeader.Controls.Add(refetchButton);
        Controls.Add(notesHeader, 0, 6);
        SetColumnSpan(notesHeader, 2);
        notesBox.ReadOnly = true;
        notesField = Theme.MemoField(notesBox);
        notesField.Dock = DockStyle.Fill;
        notesField.Margin = new Padding(0, 2, 0, 0);
        Controls.Add(notesField, 0, 7);
        SetColumnSpan(notesField, 2);
        refetchButton.Click += async (_, _) => await LookUpCategory(refetch: true);
        showNotesButton.Click += async (_, _) => { await Task.Yield(); NotesWindow.Show(FindForm(), title.Text, notesBox.Text); };

        versionBox.SelectedIndexChanged += (_, _) =>
        {
            if (!loading && versionBox.SelectedIndex >= 0) VersionChosen?.Invoke(versionBox.SelectedIndex);
        };
        categoryBox.SelectedIndexChanged += (_, _) => Changed();
        toolBox.SelectedIndexChanged += (_, _) => Changed();
        urlBox.TextChanged += (_, _) =>
        {
            Changed();
            addressPause.Stop();
            if (!loading) addressPause.Start();
        };
        memoBox.TextChanged += (_, _) => Changed();
        notesBox.TextChanged += (_, _) => Changed();
        manageButton.Click += (_, _) => ManageRequested?.Invoke();
        openUrlButton.Click += (_, _) =>
        {
            if (IsWebAddress(urlBox.Text))
                Process.Start(new ProcessStartInfo(urlBox.Text.Trim()) { UseShellExecute = true });
        };
        openUrlButton.Enabled = false;
    }

    public string Category => categoryBox.SelectedItem is string s && s != Manager.NoCategory ? s : "";
    public string Url => urlBox.Text.Trim();
    /// <summary>When the panel is short, the steps field folds away and its row gives the room back.</summary>
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (notesField == null) return;
        var heights = GetRowHeights();
        if (heights.Length < 8) return;
        int above = heights.Take(7).Sum() + Padding.Vertical;
        bool fits = ClientSize.Height - above >= NotesMinHeight;
        if (notesField.Visible != fits) notesField.Visible = fits;
        var label = fits ? "크게 보기..." : "설치 방법 보기...";
        if (showNotesButton.Text != label) showNotesButton.Text = label;
    }

    public string Memo => memoBox.Text.Trim();
    public string InstallNotes => notesBox.Text.Replace("\r\n", "\n");
    /// <summary>The tool the user picked; empty while it follows what the archive holds.</summary>
    public string Tool => toolBox.SelectedIndex > 0 ? (string)toolBox.Items[toolBox.SelectedIndex]! : "";

    /// <summary>The first entry follows the archive's contents and names what they say.</summary>
    void SetTools(string detected)
    {
        var first = detected.Length > 0 ? $"자동 ({detected})" : "자동 (알 수 없음)";
        var choices = new[] { first }.Concat(ModIdentity.Tools).ToList();
        if (toolBox.Items.Cast<string>().SequenceEqual(choices)) return;
        var index = toolBox.SelectedIndex;
        var was = loading;
        loading = true;
        toolBox.Items.Clear();
        foreach (var c in choices) toolBox.Items.Add(c);
        toolBox.SelectedIndex = Math.Max(0, index);
        loading = was;
    }

    /// <summary>Shows the version list, which only the main window uses.</summary>
    public void SetVersions(IReadOnlyList<string> labels, int selected)
    {
        versionLabel.Visible = versionRow.Visible = true;
        downloadButton.Enabled = labels.Count > 0;
        loading = true;
        if (!versionBox.Items.Cast<string>().SequenceEqual(labels))
        {
            versionBox.Items.Clear();
            foreach (var label in labels) versionBox.Items.Add(label);
        }
        if (versionBox.SelectedIndex != selected && selected < versionBox.Items.Count) versionBox.SelectedIndex = selected;
        loading = false;
    }

    public void SetCategories(IReadOnlyList<string> categories)
    {
        var choices = new[] { Manager.NoCategory }.Concat(categories).ToList();
        if (categoryBox.Items.Cast<string>().SequenceEqual(choices)) return;

        var current = categoryBox.SelectedItem as string;
        loading = true;
        categoryBox.Items.Clear();
        foreach (var c in choices) categoryBox.Items.Add(c);
        categoryBox.SelectedItem = current != null && choices.Contains(current) ? current : Manager.NoCategory;
        loading = false;
    }

    /// <param name="detectedTool">What the archive's contents say the mod is made for.</param>
    public void Show(string heading, ModMeta note, bool editable, string detectedTool = "")
    {
        addressPause.Stop();
        loading = true;
        title.Text = heading;
        var category = note.Category.Length > 0 ? note.Category : Manager.NoCategory;
        if (categoryBox.SelectedItem as string != category) categoryBox.SelectedItem = category;
        SetTools(detectedTool);
        var tool = Math.Max(0, Array.IndexOf(ModIdentity.Tools, note.Tool) + 1);
        if (toolBox.SelectedIndex != tool) toolBox.SelectedIndex = tool;
        // Assigning the same text would still reset the caret and repaint the field.
        if (urlBox.Text != note.Url) urlBox.Text = note.Url;
        if (memoBox.Text != note.Memo) memoBox.Text = note.Memo;
        var notes = note.InstallNotes.ReplaceLineEndings("\r\n");
        if (notesBox.Text != notes) notesBox.Text = notes;
        // Read-only rather than disabled: disabled text boxes ignore the dark colors.
        urlBox.ReadOnly = memoBox.ReadOnly = !editable;
        loading = false;
        openUrlButton.Enabled = IsWebAddress(urlBox.Text);
        refetchButton.Enabled = editable && NexusApi.LinkFor(urlBox.Text, Array.Empty<string>()) != null;
    }

    public void FocusFirstField() => categoryBox.Select();

    /// <summary>Chooses a category as if the user had, so it is saved the same way.</summary>
    public void SelectCategory(string category) => categoryBox.SelectedItem = category;

    /// <summary>
    /// Fills an empty category and empty install steps from the page; asked again, the steps are replaced.
    /// A category the user chose stays. A failed lookup says nothing: the address is saved either way,
    /// and the version check fills in the category later.
    /// </summary>
    async Task LookUpCategory(bool refetch)
    {
        addressPause.Stop();
        var address = Url;
        if ((!refetch && Category.Length > 0 && InstallNotes.Trim().Length > 0) || NexusApi.LinkFor(address, Array.Empty<string>()) is not { } link) return;

        (string Category, string InstallNotes) found;
        try
        {
            found = await Task.Run(() => NexusApi.GetPageInfo(link));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException
            or System.Text.Json.JsonException or KeyNotFoundException or OverflowException)
        {
            return;
        }

        // The user may have moved to another mod, changed the address or filled the fields meanwhile.
        if (IsDisposed || Url != address) return;
        if ((refetch || InstallNotes.Trim().Length == 0) && found.InstallNotes.Length > 0 && !urlBox.ReadOnly)
            notesBox.Text = found.InstallNotes.ReplaceLineEndings("\r\n");
        if (Category.Length == 0 && found.Category.Length > 0) CategoryFound?.Invoke(found.Category);
    }

    void Changed()
    {
        openUrlButton.Enabled = IsWebAddress(urlBox.Text);
        refetchButton.Enabled = !urlBox.ReadOnly && NexusApi.LinkFor(urlBox.Text, Array.Empty<string>()) != null;
        if (!loading) Edited?.Invoke();
    }

    static bool IsWebAddress(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    static Panel Row(Control field, Button button)
    {
        var row = new Panel { Dock = DockStyle.Fill, Height = Theme.ControlHeight, Margin = new Padding(0, 3, 0, 3) };
        button.Dock = DockStyle.Right;
        row.Controls.Add(field);
        row.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 6 });
        row.Controls.Add(button);
        return row;
    }

    static Label FieldLabel(string text) =>
        new() { Text = text, AutoSize = true, ForeColor = Theme.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 2) };
}

/// <summary>A mod's install steps in a window of their own, for reading when the info panel is small.</summary>
public static class NotesWindow
{
    public static void Show(IWin32Window? owner, string modName, string notes)
    {
        using var form = new Form
        {
            Text = $"설치 방법 - {modName}",
            ClientSize = new Size(680, 520),
            MinimumSize = new Size(420, 300),
            StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false,
            MinimizeBox = false,
        };
        Theme.Apply(form);
        var box = Theme.TextBox(multiline: true);
        box.ReadOnly = true;
        box.Text = notes.Length > 0 ? notes : "가져온 설치 방법이 없습니다. 주소에 Nexus 모드 페이지를 넣으면 가져옵니다.";
        var field = Theme.MemoField(box);
        field.Dock = DockStyle.Fill;
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        body.Controls.Add(field);
        var close = Theme.Button("닫기", primary: true);
        close.DialogResult = DialogResult.OK;
        form.AcceptButton = close;
        form.CancelButton = close;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        bar.Controls.Add(close);
        form.Controls.Add(body);
        form.Controls.Add(bar);
        form.Shown += (_, _) => { box.SelectionStart = 0; box.SelectionLength = 0; close.Focus(); };
        form.ShowDialog(owner);
    }
}

/// <summary>Asked when a mod is added. Everything is optional and can be filled in later.</summary>
public sealed class ModInfoDialog : Form
{
    public const DialogResult SkipAll = DialogResult.Abort;

    readonly ModInfoEditor editor = new();

    public ModInfoDialog(string zipName, ModMeta note, IReadOnlyList<string> categories, bool moreToCome, Action<IWin32Window> manageCategories, Action<string> rememberCategory, string detectedTool = "")
    {
        Text = "모드 정보 입력";
        Theme.Apply(this);
        ClientSize = new Size(560, 520);
        MinimumSize = new Size(440, 340);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        editor.SetCategories(categories);
        editor.Show(zipName, note, editable: true, detectedTool);
        editor.ManageRequested += () =>
        {
            manageCategories(this);
            editor.SetCategories(categories);
        };
        editor.CategoryFound += category =>
        {
            rememberCategory(category);
            editor.SetCategories(categories);
            editor.SelectCategory(category);
        };

        var save = Theme.Button("저장", primary: true);
        save.DialogResult = DialogResult.OK;
        var later = Theme.Button("나중에");
        later.DialogResult = DialogResult.Cancel;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        if (moreToCome)
        {
            var skipAll = Theme.Button("모두 나중에");
            skipAll.DialogResult = SkipAll;
            bar.Controls.Add(skipAll);
        }
        bar.Controls.Add(later);
        bar.Controls.Add(save);
        CancelButton = later;

        Controls.Add(editor);
        Controls.Add(bar);
        Shown += (_, _) => editor.FocusFirstField();
    }

    public void CopyTo(ModMeta note)
    {
        note.Category = editor.Category;
        note.Url = editor.Url;
        note.Memo = editor.Memo;
        note.InstallNotes = editor.InstallNotes;
        note.Tool = editor.Tool;
    }
}

/// <summary>Add, rename, delete and reorder the user's categories. Changes apply immediately.</summary>
public sealed class CategoryDialog : Form
{
    readonly Dictionary<string, ModMeta> meta;
    readonly List<string> categories;
    readonly Action save;
    readonly Func<string, int> countMods;
    readonly DarkListView list = new(0, ("카테고리", 240), ("모드 수", 70));

    public CategoryDialog(Dictionary<string, ModMeta> meta, List<string> categories, Action save, Func<string, int> countMods)
    {
        this.meta = meta;
        this.categories = categories;
        this.save = save;
        this.countMods = countMods;

        Text = "카테고리 관리";
        Theme.Apply(this);
        ClientSize = new Size(480, 420);
        MinimumSize = new Size(440, 320);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Theme.Border };
        frame.Controls.Add(new ScrollHost(list) { Dock = DockStyle.Fill });
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 10, 6, 10) };
        body.Controls.Add(frame);

        var add = Theme.Button("추가...", primary: true);
        var rename = Theme.Button("이름 변경...");
        var delete = Theme.Button("삭제...");
        var up = Theme.Button("위로");
        var down = Theme.Button("아래로");
        var close = Theme.Button("닫기");
        close.DialogResult = DialogResult.Cancel;
        CancelButton = close;

        var side = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(0, 10, 10, 10), WrapContents = false };
        foreach (var button in new[] { add, rename, delete, up, down })
        {
            button.Margin = new Padding(0, 0, 0, 6);
            button.MinimumSize = new Size(100, Theme.ControlHeight);
            side.Controls.Add(button);
        }
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10, 0, 4, 10) };
        bottom.Controls.Add(close);

        Controls.Add(body);
        Controls.Add(side);
        Controls.Add(bottom);

        // The yields keep each click from sitting inside the click handler while a dialog is open.
        add.Click += async (_, _) => { await Task.Yield(); Add(); };
        rename.Click += async (_, _) => { await Task.Yield(); Rename(); };
        delete.Click += async (_, _) => { await Task.Yield(); Delete(); };
        up.Click += (_, _) => Reorder(-1);
        down.Click += (_, _) => Reorder(1);
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedItems.Count == 0 && list.Items.Count > 0) return;
            rename.Enabled = delete.Enabled = up.Enabled = down.Enabled = list.SelectedItems.Count > 0;
        };
        rename.Enabled = delete.Enabled = up.Enabled = down.Enabled = false;
        Render();
    }

    string? Selected => list.SelectedItems.Count > 0 ? (string)list.SelectedItems[0].Tag! : null;

    void Render(string? select = null)
    {
        var current = select ?? Selected;
        list.SetRows(categories.Select(c => new ListRow(
            new[] { c, countMods(c).ToString() }, c)).ToList());
        if (current != null) list.KeepSelection(i => (string)i.Tag! == current);
    }

    void Add()
    {
        var name = AskName("새 카테고리", "카테고리 이름", "", null);
        if (name == null) return;
        categories.Add(name);
        save();
        Render(name);
    }

    void Rename()
    {
        if (Selected is not { } old) return;
        var name = AskName("카테고리 이름 변경", "새 이름", old, old);
        if (name == null || name == old) return;
        Manager.RenameCategory(meta, categories, old, name);
        save();
        Render(name);
    }

    void Delete()
    {
        if (Selected is not { } name) return;
        int used = countMods(name);
        var message = used > 0
            ? $"'{name}' 카테고리를 삭제할까요?\n이 카테고리의 모드 {used}개는 미분류가 됩니다. 모드는 지워지지 않습니다."
            : $"'{name}' 카테고리를 삭제할까요?";
        if (DarkDialog.Show(this, message, Text, yesNo: true) != DialogResult.Yes) return;
        Manager.DeleteCategory(meta, categories, name);
        save();
        Render();
    }

    void Reorder(int delta)
    {
        if (Selected is not { } name) return;
        int from = categories.IndexOf(name), to = from + delta;
        if (from < 0 || to < 0 || to >= categories.Count) return;
        categories.RemoveAt(from);
        categories.Insert(to, name);
        save();
        Render(name);
    }

    string? AskName(string title, string label, string initial, string? renaming)
    {
        var text = initial;
        while (true)
        {
            text = DarkPrompt.Show(this, title, label, text);
            if (text == null) return null;
            text = text.Trim();
            var problem = Manager.CategoryNameProblem(text, categories, renaming);
            if (problem == null) return text;
            DarkDialog.Show(this, problem, title, isError: true);
        }
    }
}

/// <summary>Asks for one line of text. Returns null if cancelled.</summary>
public static class DarkPrompt
{
    public static string? Show(IWin32Window owner, string title, string label, string initial)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(380, 130),
        };
        Theme.Apply(form);

        var box = Theme.TextBox();
        box.Text = initial;
        var field = Theme.Field(box);
        field.Dock = DockStyle.Top;
        var caption = new Label { Text = label, AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.Muted, Padding = new Padding(0, 0, 0, 4) };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        body.Controls.Add(field);
        body.Controls.Add(caption);

        var ok = Theme.Button("확인", primary: true);
        ok.DialogResult = DialogResult.OK;
        var cancel = Theme.Button("취소");
        cancel.DialogResult = DialogResult.Cancel;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        form.Controls.Add(body);
        form.Controls.Add(bar);
        form.Shown += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return form.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
    }
}

/// <summary>Asks which option of each set to install, for archives whose options would overwrite each other.</summary>
public sealed class OptionsDialog : Form
{
    readonly List<(OptionGroup Group, DarkComboBox Box)> rows = new();

    const string NotInstalled = "(설치 안 함)";

    /// <summary>The picked folder of each set; a set left at "not installed" adds none.</summary>
    public List<string> Chosen => rows.Where(r => r.Box.SelectedIndex > 0).Select(r => r.Group.Options[r.Box.SelectedIndex - 1].Folder).ToList();

    public OptionsDialog(string modName, IReadOnlyList<OptionGroup> groups, IReadOnlyCollection<string> previous)
    {
        Text = $"옵션 고르기 - {modName}";
        Theme.Apply(this);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 4) };
        table.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Margin = new Padding(0, 0, 0, 10),
            Text = "이 모드에는 같은 파일을 쓰는 옵션이 있어 묶음마다 하나만 설치할 수 있습니다. 필요 없는 묶음은 '설치 안 함'으로 두세요.\n다른 옵션으로 바꾸려면 나중에 재설치하면서 다시 고르면 됩니다.",
        });

        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            table.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                ForeColor = Theme.Muted,
                Margin = new Padding(0, 6, 0, 3),
                Text = $"옵션 {i + 1} ({group.Options.Count}개 중 하나) · 겹치는 파일: {string.Join(", ", group.SharedFiles.Take(3))}{(group.SharedFiles.Count > 3 ? " 외" : "")}",
            });
            var box = new DarkComboBox(ComboBoxStyle.DropDownList) { Width = 520, Margin = new Padding(0, 0, 0, 4) };
            box.Items.Add(NotInstalled);
            foreach (var option in group.Options) box.Items.Add(option.Label);
            // The last install's choice, where "not installed" was a choice too; a first install starts on each set's first option.
            var before = group.Options.FindIndex(o => previous.Contains(o.Folder, StringComparer.OrdinalIgnoreCase));
            box.SelectedIndex = previous.Count > 0 ? before + 1 : 1;
            table.Controls.Add(box);
            rows.Add((group, box));
        }

        var ok = Theme.Button("다음", primary: true);
        ok.DialogResult = DialogResult.OK;
        var cancel = Theme.Button("취소");
        cancel.DialogResult = DialogResult.Cancel;
        AcceptButton = ok;
        CancelButton = cancel;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);

        Controls.Add(table);
        Controls.Add(bar);
    }
}
