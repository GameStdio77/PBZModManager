using System.Diagnostics;

namespace PBZModManager;

/// <summary>The category, address and memo fields, shared by the main window and the add dialog.</summary>
public sealed class ModInfoEditor : TableLayoutPanel
{
    readonly Label title = new() { AutoEllipsis = true, Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, Height = 22 };
    readonly DarkComboBox versionBox = new(ComboBoxStyle.DropDownList);
    readonly Label versionLabel = FieldLabel(L.T("버전"));
    readonly Button downloadButton = Theme.Button(L.T("다운로드 페이지"));
    /// <summary>Shown for a mod a shared list pinned to one version; lets newer versions be offered again.</summary>
    readonly Button unpinButton = Theme.Button(L.T("고정 풀기"));
    readonly Panel versionRow;
    readonly DarkComboBox categoryBox = new(ComboBoxStyle.DropDownList);
    readonly DarkComboBox toolBox = new(ComboBoxStyle.DropDownList);
    readonly TextBox urlBox = Theme.TextBox();
    readonly TextBox memoBox = Theme.TextBox();
    /// <summary>The name the user gives the mod in the list; empty shows the automatic one, given as the hint.</summary>
    readonly TextBox displayBox = Theme.TextBox();
    readonly Label displayLabel = FieldLabel(L.T("표시 이름"));
    Panel? displayField;
    /// <summary>The page's title, as the last lookup of the address found it.</summary>
    public string FoundPageName { get; private set; } = "";
    /// <summary>For a file whose name has no version: the user says which it is.</summary>
    readonly TextBox myVersionBox = Theme.TextBox();
    readonly Label myVersionLabel = FieldLabel(L.T("내 버전"));
    readonly Panel myVersionRow;
    readonly Label toolLabel = FieldLabel(L.T("실행 도구"));
    readonly Label memoLabel = FieldLabel(L.T("메모"));
    Panel? memoField;
    readonly TextBox notesBox = Theme.TextBox(multiline: true);
    readonly Button refetchButton = Theme.Button(L.T("다시 가져오기"));
    readonly Button showNotesButton = Theme.Button(L.T("크게 보기..."));
    /// <summary>Opens the archive's pictures; shown only when the archive has some.</summary>
    readonly Button picturesButton = Theme.Button(L.T("이미지 보기..."));
    /// <summary>Opens the archive's readmes; shown only when the archive has some.</summary>
    readonly Button documentsButton = Theme.Button(L.T("설명서 보기..."));
    // What the archive itself holds, apart from the steps read from the page.
    readonly Label contentsLabel = FieldLabel(L.T("모드 파일"));
    readonly FlowLayoutPanel contentsRow = new() { Dock = DockStyle.Fill, Height = Theme.ControlHeight, Margin = new Padding(0, 3, 0, 3), WrapContents = false, Padding = Padding.Empty };
    int pictureCount, documentCount;
    readonly Panel notesField;
    /// <summary>Below this height the steps are not worth showing inline; the button opens them in a window.</summary>
    const int NotesMinHeight = 70;
    readonly Button manageButton = Theme.Button(L.T("관리..."));
    readonly Button openUrlButton = Theme.Button(L.T("열기"));
    // Waits for typing to stop, so Nexus is asked about a finished address and not about every keystroke.
    readonly System.Windows.Forms.Timer addressPause = new() { Interval = 700 };
    bool loading;
    readonly bool addressFirst;

    /// <summary>The user changed a field. Loading values through <see cref="Show"/> does not raise this.</summary>
    public event Action? Edited;
    public event Action? ManageRequested;
    /// <summary>The user picked another entry in the version list. Gives its index.</summary>
    public event Action<int>? VersionChosen;
    public event Action? DownloadRequested;
    public event Action? UnpinRequested;
    public event Action? PicturesRequested;
    public event Action? DocumentsRequested;
    /// <summary>An entered address gave a category, and none was chosen. Gives its name.</summary>
    public event Action<string>? CategoryFound;

    /// <param name="addressFirst">
    /// For a mod just added: the address comes first and stands out, since it fills in the category and
    /// install steps by itself.
    /// </param>
    public ModInfoEditor(bool addressFirst = false)
    {
        this.addressFirst = addressFirst;
        BackColor = Theme.Background;
        addressPause.Tick += async (_, _) =>
        {
            // A mod number alone stands for its Nexus page; the field shows the full address from then on.
            var full = NexusApi.FullAddress(urlBox.Text);
            if (full != urlBox.Text)
            {
                urlBox.Text = full;
                urlBox.SelectionStart = full.Length;
            }
            await LookUpCategory(refetch: false);
        };
        Disposed += (_, _) => addressPause.Dispose();
        ColumnCount = 2;
        RowCount = addressFirst ? 12 : 11;
        Padding = new Padding(8);
        Dock = DockStyle.Fill;
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < RowCount - 1; i++) RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        int row = 0;
        Controls.Add(title, 0, row);
        SetColumnSpan(title, 2);
        row++;

        versionBox.Dock = DockStyle.Fill;
        versionRow = Row(versionBox, downloadButton);
        unpinButton.Dock = DockStyle.Right;
        unpinButton.Visible = false;
        unpinButton.Click += (_, _) => UnpinRequested?.Invoke();
        versionRow.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 6 });
        versionRow.Controls.Add(unpinButton);
        versionLabel.Visible = versionRow.Visible = false;
        downloadButton.Click += (_, _) => DownloadRequested?.Invoke();
        void AddVersion()
        {
            Controls.Add(versionLabel, 0, row);
            Controls.Add(versionRow, 1, row);
            row++;
        }

        void AddAddress()
        {
            var label = FieldLabel(L.T("주소"));
            var urlField = Theme.Field(urlBox);
            urlField.Dock = DockStyle.Fill;
            Controls.Add(label, 0, row);
            Controls.Add(Row(urlField, openUrlButton), 1, row);
            row++;
            if (!addressFirst) return;
            label.Font = Theme.TitleFont;
            label.ForeColor = Theme.Info;
            var hint = new Label
            {
                Text = L.T("Nexus 모드 페이지 주소나 모드 번호(예: 300)를 넣으면 카테고리와 설치 방법을 자동으로 가져옵니다."),
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Height = Theme.BaseFont.Height + 4,
                ForeColor = Theme.Info,
                Margin = new Padding(0, 0, 0, 8),
            };
            new ToolTip().SetToolTip(hint, hint.Text);
            Controls.Add(hint, 1, row);
            row++;
        }

        if (addressFirst) AddAddress();
        AddVersion();

        // Shown only when the file name gives no version: what is typed here is compared with the page.
        myVersionBox.MaxLength = 40;
        var myVersionField = Theme.Field(myVersionBox);
        myVersionField.Dock = DockStyle.Left;
        myVersionField.Width = 120;
        myVersionRow = new Panel { Dock = DockStyle.Fill, Height = Theme.ControlHeight, Margin = new Padding(0, 3, 0, 3) };
        myVersionRow.Controls.Add(new Label
        {
            Text = L.T("파일 이름에 버전이 없어, 적은 버전으로 최신 버전과 비교합니다"),
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
        });
        myVersionRow.Controls.Add(myVersionField);
        Controls.Add(myVersionLabel, 0, row);
        Controls.Add(myVersionRow, 1, row);
        row++;
        myVersionLabel.Visible = myVersionRow.Visible = false;
        myVersionBox.TextChanged += (_, _) => Changed();

        // The name in the list. A mod just added has none of its own yet, so the add dialog leaves it out.
        displayBox.MaxLength = 120;
        displayField = Theme.Field(displayBox);
        displayField.Dock = DockStyle.Fill;
        displayField.Margin = new Padding(0, 3, 0, 3);
        Controls.Add(displayLabel, 0, row);
        Controls.Add(displayField, 1, row);
        row++;
        displayLabel.Visible = displayField.Visible = !addressFirst;
        displayBox.TextChanged += (_, _) => Changed();

        Controls.Add(FieldLabel(L.T("카테고리")), 0, row);
        categoryBox.Dock = DockStyle.Fill;
        Controls.Add(Row(categoryBox, manageButton), 1, row);
        row++;

        Controls.Add(toolLabel, 0, row);
        toolBox.Dock = DockStyle.Fill;
        toolBox.Margin = new Padding(0, 3, 0, 3);
        Controls.Add(toolBox, 1, row);
        SetTools("");
        row++;

        if (!addressFirst) AddAddress();

        // A short note of one line, shown in the installed list too.
        Controls.Add(memoLabel, 0, row);
        memoBox.MaxLength = 200;
        memoField = Theme.Field(memoBox);
        memoField.Dock = DockStyle.Fill;
        memoField.Margin = new Padding(0, 3, 0, 3);
        Controls.Add(memoField, 1, row);
        row++;

        // The pictures and readmes inside the shown version's archive.
        picturesButton.Click += (_, _) => PicturesRequested?.Invoke();
        documentsButton.Click += (_, _) => DocumentsRequested?.Invoke();
        picturesButton.Margin = documentsButton.Margin = new Padding(0, 0, 6, 0);
        contentsRow.Controls.Add(documentsButton);
        contentsRow.Controls.Add(picturesButton);
        contentsRow.Controls.Add(new Label { Text = L.T("받은 압축 파일 안에 든 그림과 설명서"), AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(2, 7, 0, 0) });
        Controls.Add(contentsLabel, 0, row);
        Controls.Add(contentsRow, 1, row);
        row++;
        contentsLabel.Visible = contentsRow.Visible = false;

        // Install steps come from the mod's page; they are read here, not typed.
        var notesHeader = new Panel { Dock = DockStyle.Fill, Height = Theme.ControlHeight, Margin = new Padding(0, 6, 0, 2) };
        refetchButton.Dock = DockStyle.Right;
        showNotesButton.Dock = DockStyle.Right;
        notesHeader.Controls.Add(new Label { Text = L.T("설치 방법 (Nexus 설명에서 가져옴)"), AutoSize = true, ForeColor = Theme.Muted, Dock = DockStyle.Left, Padding = new Padding(0, 6, 0, 0) });
        notesHeader.Controls.Add(showNotesButton);
        notesHeader.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 6 });
        notesHeader.Controls.Add(refetchButton);
        Controls.Add(notesHeader, 0, row++);
        SetColumnSpan(notesHeader, 2);
        notesBox.ReadOnly = true;
        notesField = Theme.MemoField(notesBox);
        notesField.Dock = DockStyle.Fill;
        notesField.Margin = new Padding(0, 2, 0, 0);
        Controls.Add(notesField, 0, row);
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
            if (IsWebAddress(Url))
                Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
        };
        openUrlButton.Enabled = false;
    }

    public string Category => categoryBox.SelectedItem is string s && s != Manager.NoCategory ? s : "";
    /// <summary>The address entered; a mod number alone counts as its Nexus page.</summary>
    public string Url => NexusApi.FullAddress(urlBox.Text.Trim());
    /// <summary>When the panel is short, the steps field folds away and its row gives the room back.</summary>
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (notesField == null) return;
        var heights = GetRowHeights();
        if (heights.Length < RowCount) return;
        int above = heights.Take(RowCount - 1).Sum() + Padding.Vertical;
        bool fits = ClientSize.Height - above >= NotesMinHeight;
        if (notesField.Visible != fits) notesField.Visible = fits;
        var label = fits ? L.T("크게 보기...") : L.T("설치 방법 보기...");
        if (showNotesButton.Text != label) showNotesButton.Text = label;
    }

    public string Memo => memoBox.Text.Trim();
    public string DisplayName => displayBox.Text.Trim();

    /// <summary>Shows the user's own name for the mod, with the automatic one as the hint when it is empty.</summary>
    public void SetDisplayName(string own, string automatic)
    {
        var was = loading;
        loading = true;
        if (displayBox.Text != own) displayBox.Text = own;
        displayBox.PlaceholderText = automatic;
        displayBox.ReadOnly = automatic.Length == 0;
        loading = was;
    }
    /// <summary>The version the user typed for a file without one in its name.</summary>
    public string MyVersion => myVersionRow.Visible ? myVersionBox.Text.Trim().TrimStart('v', 'V') : "";

    /// <summary>Shows the version field when the shown file's name has no version, with what was given before.</summary>
    public void SetMyVersion(bool needed, string value)
    {
        var was = loading;
        loading = true;
        myVersionLabel.Visible = myVersionRow.Visible = needed;
        if (myVersionBox.Text != value) myVersionBox.Text = value;
        myVersionBox.ReadOnly = urlBox.ReadOnly;
        loading = was;
    }
    public string InstallNotes => notesBox.Text.Replace("\r\n", "\n");
    /// <summary>The tool the user picked; empty while it follows what the archive holds.</summary>
    public string Tool => toolBox.SelectedIndex > 0 ? ModIdentity.Tools[toolBox.SelectedIndex - 1] : "";

    /// <summary>The first entry follows the archive's contents and names what they say.</summary>
    void SetTools(string detected)
    {
        var first = detected.Length > 0 ? L.F("자동 ({0})", ModIdentity.ToolLabel(detected)) : L.T("자동 (알 수 없음)");
        var choices = new[] { first }.Concat(ModIdentity.Tools.Select(ModIdentity.ToolLabel)).ToList();
        if (toolBox.Items.Cast<string>().SequenceEqual(choices)) return;
        var index = toolBox.SelectedIndex;
        var was = loading;
        loading = true;
        toolBox.Items.Clear();
        foreach (var c in choices) toolBox.Items.Add(c);
        toolBox.SelectedIndex = Math.Max(0, index);
        loading = was;
    }

    /// <summary>
    /// For the heading of a page's files: only what the files share is shown, the category, address and
    /// install steps; the version, tool and memo, each file's own, are hidden.
    /// </summary>
    /// <param name="keepMemo">For a mod held by its address: the memo is its own, so it stays.</param>
    /// <param name="keepDownload">The download button stays, without the version list.</param>
    public void SetPageMode(bool page, bool keepMemo = false, bool keepDownload = false)
    {
        void Show(Control c, bool visible) { if (c.Visible != visible) c.Visible = visible; }
        Show(versionLabel, !page);
        Show(versionBox, !page);
        Show(versionRow, !page || keepDownload);
        Show(toolLabel, !page);
        Show(toolBox, !page);
        Show(memoLabel, !page || keepMemo);
        Show(memoField!, !page || keepMemo);
        if (page) myVersionLabel.Visible = myVersionRow.Visible = false;
        if (!addressFirst)
        {
            Show(displayLabel, !page);
            Show(displayField!, !page);
        }
        if (page) pictureCount = documentCount = 0;
        if (page) ShowContents();
        if (page) downloadButton.Enabled = true;
    }

    /// <summary>Shows the pin of a version a shared list asked for, with the button that lifts it; empty hides both.</summary>
    public void SetPinned(string version)
    {
        unpinButton.Visible = version.Length > 0;
        if (version.Length > 0) unpinButton.Text = L.F("{0} 고정 풀기", version);
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
        openUrlButton.Enabled = IsWebAddress(Url);
        refetchButton.Enabled = editable && NexusApi.LinkFor(Url, Array.Empty<string>()) != null;
    }

    /// <summary>How many pictures the shown version's archive holds; none hides the button.</summary>
    public void SetPictureCount(int count)
    {
        pictureCount = count;
        picturesButton.Text = L.F("이미지 보기 ({0})...", count);
        ShowContents();
    }

    /// <summary>How many readmes the shown version's archive holds; none hides the button.</summary>
    public void SetDocumentCount(int count)
    {
        documentCount = count;
        documentsButton.Text = count > 1 ? L.F("설명서 보기 ({0})...", count) : L.T("설명서 보기...");
        ShowContents();
    }

    /// <summary>The archive's row shows only what it holds, and goes away when it holds neither.</summary>
    void ShowContents()
    {
        // A hidden row reads its buttons as hidden too, so theirs are set without asking first.
        picturesButton.Visible = pictureCount > 0;
        documentsButton.Visible = documentCount > 0;
        void Show(Control c, bool visible) { if (c.Visible != visible) c.Visible = visible; }
        Show(contentsLabel, pictureCount + documentCount > 0);
        Show(contentsRow, pictureCount + documentCount > 0);
    }

    public void FocusFirstField()
    {
        if (addressFirst) urlBox.Select();
        else categoryBox.Select();
    }

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

        (string Name, string Category, string InstallNotes) found;
        try
        {
            found = await Task.Run(() => NexusApi.GetPage(link));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException
            or System.Text.Json.JsonException or KeyNotFoundException or OverflowException)
        {
            return;
        }

        // The user may have moved to another mod, changed the address or filled the fields meanwhile.
        if (IsDisposed || Url != address) return;
        if (found.Name.Length > 0) FoundPageName = found.Name;
        if ((refetch || InstallNotes.Trim().Length == 0) && found.InstallNotes.Length > 0 && !urlBox.ReadOnly)
            notesBox.Text = found.InstallNotes.ReplaceLineEndings("\r\n");
        if (Category.Length == 0 && found.Category.Length > 0) CategoryFound?.Invoke(found.Category);
    }

    void Changed()
    {
        openUrlButton.Enabled = IsWebAddress(Url);
        refetchButton.Enabled = !urlBox.ReadOnly && NexusApi.LinkFor(Url, Array.Empty<string>()) != null;
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
            Text = L.F("설치 방법 - {0}", modName),
            ClientSize = new Size(680, 520),
            MinimumSize = new Size(420, 300),
            StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false,
            MinimizeBox = false,
        };
        Theme.Apply(form);
        var box = Theme.TextBox(multiline: true);
        box.ReadOnly = true;
        box.Text = notes.Length > 0 ? notes : L.T("가져온 설치 방법이 없습니다. 주소에 Nexus 모드 페이지를 넣으면 가져옵니다.");
        var field = Theme.MemoField(box);
        field.Dock = DockStyle.Fill;
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        body.Controls.Add(field);
        var close = Theme.Button(L.T("닫기"), primary: true);
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

/// <summary>The pictures inside a mod's archive, one at a time: buttons, arrow keys or a click on either half turn them.</summary>
public sealed class PictureWindow : Form
{
    readonly System.IO.Compression.ZipArchive zip;
    readonly List<string> names;
    readonly PictureBox picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Background, Cursor = Cursors.Hand };
    readonly Label caption = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted, AutoEllipsis = true };
    readonly Button previous = Theme.Button(L.T("◀ 이전"));
    readonly Button next = Theme.Button(L.T("다음 ▶"));
    int index;

    public string Caption => caption.Text;

    PictureWindow(string modName, string zipPath, List<string> names)
    {
        this.names = names;
        zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
        Text = L.F("미리보기 이미지 - {0}", modName);
        Theme.Apply(this);
        ClientSize = new Size(1100, 760);
        MinimumSize = new Size(480, 360);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        var close = Theme.Button(L.T("닫기"), primary: true);
        close.DialogResult = DialogResult.OK;
        CancelButton = close;
        var bar = new TableLayoutPanel { Dock = DockStyle.Bottom, ColumnCount = 4, Height = Theme.ControlHeight + 16, Padding = new Padding(8) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var b in new[] { previous, next, close }) b.Margin = new Padding(0, 0, 6, 0);
        bar.Controls.Add(previous, 0, 0);
        bar.Controls.Add(caption, 1, 0);
        bar.Controls.Add(next, 2, 0);
        bar.Controls.Add(close, 3, 0);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 0) };
        body.Controls.Add(picture);
        Controls.Add(body);
        Controls.Add(bar);

        previous.Click += (_, _) => ShowAt(index - 1);
        next.Click += (_, _) => ShowAt(index + 1);
        picture.MouseClick += (_, e) => ShowAt(e.X < picture.Width / 2 ? index - 1 : index + 1);
        previous.Enabled = next.Enabled = names.Count > 1;
        FormClosed += (_, _) =>
        {
            picture.Image?.Dispose();
            zip.Dispose();
        };
        ShowAt(0);
    }

    public static void Show(IWin32Window? owner, string modName, string zipPath)
    {
        var names = Manager.PicturesIn(zipPath);
        if (names.Count == 0) return;
        using var form = new PictureWindow(modName, zipPath, names);
        form.ShowDialog(owner);
    }

    /// <summary>Turns to a picture; past either end it wraps around.</summary>
    public void ShowAt(int at)
    {
        index = (at % names.Count + names.Count) % names.Count;
        var old = picture.Image;
        try
        {
            using var stream = zip.GetEntry(names[index])!.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            using var image = Image.FromStream(copy);
            picture.Image = new Bitmap(image);
            caption.Text = $"{Path.GetFileName(names[index])}  ({index + 1}/{names.Count})";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or OutOfMemoryException)
        {
            picture.Image = null;
            caption.Text = L.F("열 수 없는 이미지입니다: {0}", Path.GetFileName(names[index])) + $"  ({index + 1}/{names.Count})";
        }
        old?.Dispose();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.Left or Keys.Right)
        {
            ShowAt(keyData == Keys.Left ? index - 1 : index + 1);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

/// <summary>The readmes and manuals inside a mod's archive, read in place. A PDF opens in its own program.</summary>
public sealed class DocumentWindow : Form
{
    // Larger text files are cut: a readme is never this long, and the box would get slow.
    const int MaxBytes = 2 * 1024 * 1024;
    readonly string zipPath;
    readonly List<string> names;
    // Both keep their native scrollbar always there, so the app's own bar can stand over it (ScrollHost).
    readonly ListBox list = new() { IntegralHeight = false, BorderStyle = BorderStyle.None, ScrollAlwaysVisible = true, DrawMode = DrawMode.OwnerDrawFixed };
    readonly RichTextBox text = new() { ReadOnly = true, BorderStyle = BorderStyle.None, DetectUrls = false, WordWrap = true, ScrollBars = RichTextBoxScrollBars.ForcedVertical };
    readonly Label caption = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted, AutoEllipsis = true };
    readonly Button openOutside = Theme.Button(L.T("PDF 프로그램으로 열기"));
    readonly Action<string> open;

    /// <summary>The text on show.</summary>
    public string ShownText => text.Text;
    public IReadOnlyList<string> Names => names;

    DocumentWindow(string modName, string zipPath, List<string> names, Action<string> open)
    {
        this.zipPath = zipPath;
        this.names = names;
        this.open = open;
        Text = L.F("설명서 - {0}", modName);
        Theme.Apply(this);
        list.BackColor = text.BackColor = Theme.Surface;
        list.ForeColor = text.ForeColor = Theme.Text;
        ClientSize = new Size(1000, 720);
        MinimumSize = new Size(480, 360);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        var close = Theme.Button(L.T("닫기"), primary: true);
        close.DialogResult = DialogResult.OK;
        CancelButton = close;
        var bar = new TableLayoutPanel { Dock = DockStyle.Bottom, ColumnCount = 3, Height = Theme.ControlHeight + 16, Padding = new Padding(8) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        openOutside.Margin = new Padding(0, 0, 6, 0);
        bar.Controls.Add(caption, 0, 0);
        bar.Controls.Add(openOutside, 1, 0);
        bar.Controls.Add(close, 2, 0);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 0) };
        static Panel Framed(Control inside, Padding inset)
        {
            var frame = new Panel { BackColor = Theme.Border, Padding = new Padding(1) };
            frame.Controls.Add(new ScrollHost(inside, inset) { Dock = DockStyle.Fill });
            return frame;
        }
        var textFrame = Framed(text, new Padding(8, 6, 0, 6));
        textFrame.Dock = DockStyle.Fill;
        body.Controls.Add(textFrame);
        if (names.Count > 1)
        {
            body.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 8 });
            var listFrame = Framed(list, new Padding(0, 2, 0, 2));
            listFrame.Dock = DockStyle.Left;
            listFrame.Width = 260;
            body.Controls.Add(listFrame);
            list.ItemHeight = Theme.BaseFont.Height + 8;
            list.DrawItem += (_, e) =>
            {
                if (e.Index < 0) return;
                var chosen = (e.State & DrawItemState.Selected) != 0;
                using (var back = new SolidBrush(chosen ? Theme.Selection : Theme.Surface))
                    e.Graphics.FillRectangle(back, e.Bounds);
                TextRenderer.DrawText(e.Graphics, names[e.Index], list.Font, new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height),
                    Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            foreach (var name in names) list.Items.Add(name);
            list.SelectedIndexChanged += (_, _) => { if (list.SelectedIndex >= 0) ShowAt(list.SelectedIndex); };
        }
        Controls.Add(body);
        Controls.Add(bar);
        openOutside.Click += (_, _) => OpenOutside();
        Shown += (_, _) => close.Focus();
        ShowAt(0);
    }

    public static void Show(IWin32Window? owner, string modName, string zipPath, Action<string> open)
    {
        var names = Manager.DocumentsIn(zipPath);
        if (names.Count == 0) return;
        using var form = new DocumentWindow(modName, zipPath, names, open);
        form.ShowDialog(owner);
    }

    bool IsPdf(int at) => Path.GetExtension(names[at]).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    int Current => list.Items.Count > 0 ? Math.Max(list.SelectedIndex, 0) : 0;

    public void ShowAt(int at)
    {
        // With a list, choosing the row shows it.
        if (list.Items.Count > 0 && list.SelectedIndex != at) { list.SelectedIndex = at; return; }
        var name = names[at];
        caption.Text = names.Count > 1 ? $"{name}  ({at + 1}/{names.Count})" : name;
        openOutside.Visible = IsPdf(at);
        text.Clear();
        text.ForeColor = Theme.Text;
        if (IsPdf(at))
        {
            text.Text = L.T("PDF는 여기서 보여 줄 수 없습니다. [PDF 프로그램으로 열기]를 눌러 주세요.");
            return;
        }
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
            var entry = zip.GetEntry(name)!;
            using var stream = entry.Open();
            var bytes = new byte[Math.Min(entry.Length, MaxBytes)];
            stream.ReadExactly(bytes);
            var words = Manager.ReadDocumentText(bytes);
            var type = Path.GetExtension(name).ToLowerInvariant();
            if (type == ".rtf")
            {
                try
                {
                    text.Rtf = words;
                    // The author's colours are often black on white; the dark window keeps its own.
                    text.SelectAll();
                    text.SelectionColor = Theme.Text;
                    text.SelectionBackColor = Theme.Surface;
                    text.Select(0, 0);
                    return;
                }
                catch (ArgumentException) { }
            }
            if (type is ".html" or ".htm") words = Manager.PlainFromHtml(words);
            text.Text = words.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            text.Text = L.F("열 수 없는 설명서입니다: {0}", Path.GetFileName(name));
        }
    }

    /// <summary>Copies the PDF out to the temporary folder and opens it from there.</summary>
    void OpenOutside()
    {
        var at = Current;
        if (!IsPdf(at)) return;
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "PBZModManager", "docs");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, Path.GetFileName(names[at]));
            using (var zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
                System.IO.Compression.ZipFileExtensions.ExtractToFile(zip.GetEntry(names[at])!, file, overwrite: true);
            open(file);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            DarkDialog.Show(this, ex.Message, L.T("설명서"), isError: true);
        }
    }
}

/// <summary>Asked when a mod is added. Everything is optional and can be filled in later.</summary>
public sealed class ModInfoDialog : Form
{
    public const DialogResult SkipAll = DialogResult.Abort;

    readonly ModInfoEditor editor = new(addressFirst: true);

    public ModInfoDialog(string zipName, ModMeta note, IReadOnlyList<string> categories, bool moreToCome, Action<IWin32Window> manageCategories, Action<string> rememberCategory, string detectedTool = "")
    {
        Text = L.T("모드 정보 입력");
        Theme.Apply(this);
        ClientSize = new Size(640, 540);
        MinimumSize = new Size(440, 340);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        editor.SetCategories(categories);
        editor.Show(zipName, note, editable: true, detectedTool);
        editor.SetMyVersion(ModIdentity.ParseFileName(zipName).Version.Length == 0, note.Version);
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

        var save = Theme.Button(L.T("저장"), primary: true);
        save.DialogResult = DialogResult.OK;
        var later = Theme.Button(L.T("나중에"));
        later.DialogResult = DialogResult.Cancel;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        if (moreToCome)
        {
            var skipAll = Theme.Button(L.T("모두 나중에"));
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
        note.Version = editor.MyVersion;
        if (editor.FoundPageName.Length > 0) note.PageName = editor.FoundPageName;
    }
}

/// <summary>Add, rename, delete and reorder the user's categories. Changes apply immediately.</summary>
public sealed class CategoryDialog : Form
{
    readonly Dictionary<string, ModMeta> meta;
    readonly List<string> categories;
    readonly Action save;
    readonly Func<string, int> countMods;
    readonly DarkListView list = new(0, (L.T("카테고리"), 240), (L.T("모드 수"), 70));

    public CategoryDialog(Dictionary<string, ModMeta> meta, List<string> categories, Action save, Func<string, int> countMods)
    {
        this.meta = meta;
        this.categories = categories;
        this.save = save;
        this.countMods = countMods;

        Text = L.T("카테고리 관리");
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

        var add = Theme.Button(L.T("추가..."), primary: true);
        var rename = Theme.Button(L.T("이름 변경..."));
        var delete = Theme.Button(L.T("삭제..."));
        var up = Theme.Button(L.T("위로"));
        var down = Theme.Button(L.T("아래로"));
        var close = Theme.Button(L.T("닫기"));
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
        var name = AskName(L.T("새 카테고리"), L.T("카테고리 이름"), "", null);
        if (name == null) return;
        categories.Add(name);
        save();
        Render(name);
    }

    void Rename()
    {
        if (Selected is not { } old) return;
        var name = AskName(L.T("카테고리 이름 변경"), L.T("새 이름"), old, old);
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
            ? L.F("'{0}' 카테고리를 삭제할까요?\n이 카테고리의 모드 {1}개는 미분류가 됩니다. 모드는 지워지지 않습니다.", name, used)
            : L.F("'{0}' 카테고리를 삭제할까요?", name);
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

        var ok = Theme.Button(L.T("확인"), primary: true);
        ok.DialogResult = DialogResult.OK;
        var cancel = Theme.Button(L.T("취소"));
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

    static string NotInstalled => L.T("(설치 안 함)");

    /// <summary>The picked folder of each set; a set left at "not installed" adds none.</summary>
    public List<string> Chosen => rows.Where(r => r.Box.SelectedIndex > 0).Select(r => r.Group.Options[r.Box.SelectedIndex - 1].Folder).ToList();

    public OptionsDialog(string modName, IReadOnlyList<OptionGroup> groups, IReadOnlyCollection<string> previous)
    {
        Text = L.F("옵션 고르기 - {0}", modName);
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
            Text = L.T("이 모드에는 같은 파일을 쓰는 옵션이 있어 묶음마다 하나만 설치할 수 있습니다. 필요 없는 묶음은 '설치 안 함'으로 두세요.\n다른 옵션으로 바꾸려면 나중에 재설치하면서 다시 고르면 됩니다."),
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
                Text = L.F("옵션 {0} ({1}개 중 하나) · 겹치는 파일: {2}{3}", i + 1, group.Options.Count, string.Join(", ", group.SharedFiles.Take(3)), (group.SharedFiles.Count > 3 ? L.T(" 외") : "")),
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

        var ok = Theme.Button(L.T("다음"), primary: true);
        ok.DialogResult = DialogResult.OK;
        var cancel = Theme.Button(L.T("취소"));
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
