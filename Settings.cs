namespace PBZModManager;

public sealed class SettingsDialog : Form
{
    readonly Manager manager;
    readonly Action<string>? openFolder;
    readonly Action? restartApp;

    readonly CheckBox showRowNumbersBox;
    readonly DarkComboBox separatorCombo = new(ComboBoxStyle.DropDownList);
    readonly TextBox separatorBox = Theme.TextBox();
    readonly Label separatorPreview = new() { AutoSize = true, ForeColor = Theme.Info, Margin = new Padding(0, 2, 0, 4) };

    readonly DarkComboBox launchBehaviorCombo = new(ComboBoxStyle.DropDownList);

    readonly TextBox gameBox = Theme.TextBox();
    readonly Label leftoversStatus = new() { AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 8) };
    readonly Label dialogStatus = new() { AutoSize = true, ForeColor = Theme.Danger, Margin = new Padding(0, 6, 16, 0) };

    static readonly string[] SeparatorPresets =
    {
        "── {0} ──",
        "━━ {0} ━━",
        "══ {0} ══",
        "■ {0} ■",
        "◆ {0} ◆",
        "[ {0} ]",
        "( {0} )"
    };

    public bool NeedsRestart { get; private set; }
    public string NewGameRoot => gameBox.Text.Trim();

    public SettingsDialog(Manager manager, Action<string>? openFolder = null, Action? restartApp = null)
    {
        this.manager = manager;
        this.openFolder = openFolder;
        this.restartApp = restartApp;

        Text = L.T("설정");
        Theme.Apply(this);
        ClientSize = new Size(680, 660);
        MinimumSize = new Size(600, 520);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 15,
            Padding = new Padding(24, 16, 24, 20)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // 1. 화면 표시
        showRowNumbersBox = Theme.CheckBox(L.T("모드 목록 및 설치 목록에 순번(No.) 열 표시"), manager.ShowRowNumbers);
        showRowNumbersBox.AutoSize = true;
        showRowNumbersBox.Margin = new Padding(0, 0, 0, 8);

        foreach (var p in SeparatorPresets) separatorCombo.Items.Add(p);
        separatorCombo.Items.Add(L.T("직접 입력..."));
        separatorCombo.Width = 140;

        separatorBox.Text = manager.SeparatorFormat;
        int presetIndex = Array.IndexOf(SeparatorPresets, manager.SeparatorFormat);
        separatorCombo.SelectedIndex = presetIndex >= 0 ? presetIndex : separatorCombo.Items.Count - 1;

        separatorCombo.SelectionChangeCommitted += (_, _) =>
        {
            if (separatorCombo.SelectedIndex >= 0 && separatorCombo.SelectedIndex < SeparatorPresets.Length)
                separatorBox.Text = SeparatorPresets[separatorCombo.SelectedIndex];
            UpdateSeparatorPreview();
        };
        separatorBox.TextChanged += (_, _) =>
        {
            int idx = Array.IndexOf(SeparatorPresets, separatorBox.Text.Trim());
            separatorCombo.SelectedIndex = idx >= 0 ? idx : separatorCombo.Items.Count - 1;
            UpdateSeparatorPreview();
        };
        UpdateSeparatorPreview();

        var sepRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 4)
        };
        sepRow.Controls.Add(new Label { Text = L.T("구분선 양식:"), AutoSize = true, ForeColor = Theme.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) });
        sepRow.Controls.Add(separatorCombo);
        var sepField = Theme.Field(separatorBox);
        sepField.Width = 140;
        sepField.Margin = new Padding(8, 0, 0, 0);
        sepRow.Controls.Add(sepField);

        // 2. 실행 및 업데이트
        launchBehaviorCombo.Items.Add(L.T("매니저 계속 열어두기"));
        launchBehaviorCombo.Items.Add(L.T("작업 표시줄로 최소화"));
        launchBehaviorCombo.Items.Add(L.T("매니저 자동 종료"));
        launchBehaviorCombo.SelectedIndex = (int)manager.LaunchBehavior;
        launchBehaviorCombo.Width = 200;

        var launchRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8)
        };
        launchRow.Controls.Add(new Label { Text = L.T("게임 실행 시:"), AutoSize = true, ForeColor = Theme.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) });
        launchRow.Controls.Add(launchBehaviorCombo);


        // 3. 게임 및 모드 폴더
        gameBox.Text = manager.Root;
        var gameField = Theme.Field(gameBox);
        gameField.Dock = DockStyle.Fill;
        gameField.Margin = new Padding(0, 2, 8, 2);

        var browseButton = Theme.Button(L.T("찾아보기..."));
        browseButton.Margin = Padding.Empty;
        browseButton.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog
            {
                Description = L.T("Phantom Blade Zero 게임 폴더 (PBZ)"),
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(gameBox.Text) ? gameBox.Text : ""
            };
            if (picker.ShowDialog(this) == DialogResult.OK) gameBox.Text = picker.SelectedPath;
        };

        var folderGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        folderGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        folderGrid.Controls.Add(new Label { Text = L.T("게임 폴더"), AutoSize = true, ForeColor = Theme.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) }, 0, 0);
        folderGrid.Controls.Add(gameField, 1, 0);
        folderGrid.Controls.Add(browseButton, 2, 0);

        var modFolderRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 6)
        };
        var openModDirButton = Theme.Button(L.T("모드 폴더 열기"));
        openModDirButton.Click += (_, _) => this.openFolder?.Invoke(manager.ArchiveDir);
        modFolderRow.Controls.Add(openModDirButton);

        var modNote = new Label
        {
            AutoSize = true,
            ForeColor = Theme.Muted,
            MaximumSize = new Size(600, 0),
            Margin = new Padding(0, 0, 0, 4),
            Text = L.F("받은 모드 파일과 기록은 .manager 폴더에 저장됩니다: {0}", Path.Combine(manager.ModDir, ".manager"))
        };

        // 4. 보관 폴더 관리
        var leftoversNote = new Label
        {
            AutoSize = true,
            ForeColor = Theme.Muted,
            MaximumSize = new Size(600, 0),
            Margin = new Padding(0, 0, 0, 8),
            Text = L.T("모드를 제거하거나 교체할 때 덮어썼던 원본 파일이나 생성된 잔여 파일이 안전하게 보관되는 폴더입니다. 언제든 휴지통으로 비울 수 있습니다.")
        };

        var leftoversButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8)
        };
        var openLeftovers = Theme.Button(L.T("보관 폴더 열기"));
        openLeftovers.Click += (_, _) => this.openFolder?.Invoke(manager.LeftoversDir);
        var clearLeftovers = Theme.Button(L.T("보관 폴더 비우기..."));
        clearLeftovers.Margin = new Padding(8, 0, 0, 0);
        clearLeftovers.Click += async (_, _) =>
        {
            await Task.Yield();
            var (runs, files, bytes) = manager.LeftoversSize();
            if (files == 0)
            {
                DarkDialog.Show(this, L.T("보관 폴더가 비어 있습니다."), Text);
                return;
            }
            var question = L.F("보관 폴더에 있는 파일 {0}개({1:0.#} MB)를 휴지통으로 보낼까요?\n\n모드를 제거할 때 덮어썼던 원본이나 생성된 파일이 보관되어 있습니다. 휴지통에서 다시 꺼낼 수 있습니다.", files, bytes / 1048576.0);
            if (DarkDialog.Show(this, question, L.T("보관 폴더 비우기"), yesNo: true) != DialogResult.Yes) return;
            var failed = manager.ClearLeftovers();
            if (failed.Count > 0)
                DarkDialog.Show(this, L.T("휴지통으로 보내지 못한 것:\n") + string.Join("\n", failed), L.T("보관 폴더 비우기"), isError: true);
            else
                DarkDialog.Show(this, L.F("보관 폴더를 비웠습니다 ({0:0.#} MB, 휴지통에서 되살릴 수 있음).", bytes / 1048576.0), Text);
            UpdateLeftoversStatus();
        };
        leftoversButtons.Controls.Add(openLeftovers);
        leftoversButtons.Controls.Add(clearLeftovers);

        UpdateLeftoversStatus();

        // Add all controls sequentially into table (0, row)
        table.Controls.Add(MakeHeader(L.T("화면 표시"), first: true), 0, 0);
        table.Controls.Add(showRowNumbersBox, 0, 1);
        table.Controls.Add(sepRow, 0, 2);
        table.Controls.Add(separatorPreview, 0, 3);

        // Nexus is asked only when the user presses [최신 버전 확인]: its API policy wants a user's action behind each check.
        table.Controls.Add(MakeHeader(L.T("게임 실행")), 0, 4);
        table.Controls.Add(launchRow, 0, 5);

        table.Controls.Add(MakeHeader(L.T("게임 및 모드 폴더")), 0, 7);
        table.Controls.Add(folderGrid, 0, 8);
        table.Controls.Add(modFolderRow, 0, 9);
        table.Controls.Add(modNote, 0, 10);

        table.Controls.Add(MakeHeader(L.T("보관 폴더 관리")), 0, 11);
        table.Controls.Add(leftoversNote, 0, 12);
        table.Controls.Add(leftoversStatus, 0, 13);
        table.Controls.Add(leftoversButtons, 0, 14);

        scroll.Controls.Add(table);

        // Bottom action buttons
        var ok = Theme.Button(L.T("저장"), primary: true);
        var cancel = Theme.Button(L.T("취소"));
        cancel.DialogResult = DialogResult.Cancel;
        CancelButton = cancel;

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(16, 12, 16, 12),
            BackColor = Theme.Surface
        };
        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);
        bar.Controls.Add(dialogStatus);

        ok.Click += (_, _) =>
        {
            var root = NewGameRoot;
            if (root.Length == 0)
            {
                dialogStatus.Text = L.T("게임 폴더를 골라 주세요.");
                return;
            }
            if (!GameFolder.IsGameFolder(root))
            {
                dialogStatus.Text = L.T("게임 폴더가 아닙니다. ProjectPBZ와 platform 폴더가 있는 PBZ 폴더를 골라 주세요.");
                return;
            }

            var launch = (GameLaunchBehavior)Math.Clamp(launchBehaviorCombo.SelectedIndex, 0, 2);
            var sepFmt = separatorBox.Text.Trim();
            if (string.IsNullOrEmpty(sepFmt)) sepFmt = "── {0} ──";
            if (!sepFmt.Contains("{0}")) sepFmt = $"── {sepFmt} ──";

            manager.SavePreferences(showRowNumbersBox.Checked, sepFmt, launch);

            if (!root.Equals(manager.Root, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    new Manager(root, manager.ModDir).RememberGameRoot();
                    NeedsRestart = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    dialogStatus.Text = L.F("설정을 저장하지 못했습니다: {0}", ex.Message);
                    return;
                }
            }

            DialogResult = DialogResult.OK;
        };

        Controls.Add(scroll);
        Controls.Add(bar);
    }

    void UpdateSeparatorPreview()
    {
        var fmt = separatorBox.Text.Trim();
        if (string.IsNullOrEmpty(fmt)) fmt = "── {0} ──";
        separatorPreview.Text = L.F("미리보기: {0}", Manager.FormatSeparator(L.T("무기"), fmt));
    }

    void UpdateLeftoversStatus()
    {
        var (runs, files, bytes) = manager.LeftoversSize();
        leftoversStatus.Text = files > 0
            ? L.F("보관된 파일: {0}회 작업, 총 {1}개 파일 ({2})", runs, files, MainForm.FormatSize(bytes))
            : L.T("보관된 파일: 비어 있음");
    }

    static Label MakeHeader(string title, bool first = false) => new()
    {
        Text = title,
        Font = Theme.TitleFont,
        ForeColor = Theme.Info,
        AutoSize = true,
        Margin = new Padding(0, first ? 0 : 20, 0, 8)
    };
}
