using System.Diagnostics;

namespace PBZModManager;

public sealed class MainForm : Form, IMessageFilter
{
    readonly Manager manager;
    readonly Dictionary<string, ModMeta> meta;
    readonly List<string> categories;
    readonly Dictionary<string, RemoteInfo> remote;
    readonly Action<string> openInBrowser;

    public const string ProjectUrl = "https://github.com/GameStdio77/PBZModManager";

    /// <summary>The version this build was given, without any build suffix.</summary>
    static string AppVersion => Application.ProductVersion.Split('+')[0];
    readonly BusyOverlay overlay;

    readonly DarkListView zipList = new(0, (L.T("모드"), 230), (L.T("받은 최신"), 80), (L.T("카테고리"), 100), (L.T("실행 도구"), 130), (L.T("상태"), 170));
    readonly DarkListView installedList = new(1, (L.T("순번"), 44), (L.T("모드"), 230), (L.T("버전"), 90), (L.T("메모"), 200), (L.T("설치 시각"), 120));
    readonly DarkListView fileList = new(0, (L.T("경로"), 400), (L.T("종류"), 60), (L.T("현재 상태"), 110));
    readonly DarkComboBox runtimeBox = new(ComboBoxStyle.DropDownList) { Width = 170 };
    readonly Button optionsButton = Theme.Button(L.T("옵션 바꾸기..."));
    readonly Button separatorButton = Theme.Button(L.T("구분선 추가..."));
    readonly DarkComboBox filterBox = new(ComboBoxStyle.DropDownList);
    readonly ModInfoEditor infoEditor = new();
    readonly Button addButton = Theme.Button(L.T("추가..."));
    readonly Button unlistButton = Theme.Button(L.T("목록에서 빼기"));
    readonly Button deleteFileButton = Theme.Button(L.T("파일 삭제..."));
    readonly Button cleanVersionsButton = Theme.Button(L.T("이전 버전 정리..."));
    readonly Button installButton = Theme.Button(L.T("설치..."), primary: true);
    readonly Button removeButton = Theme.Button(L.T("선택한 모드 제거..."), primary: true);
    readonly Button moveUpButton = Theme.Button(L.T("위로"));
    readonly Button moveDownButton = Theme.Button(L.T("아래로"));
    readonly Button refreshButton = Theme.Button(L.T("새로고침"));
    readonly Button checkLatestButton = Theme.Button(L.T("최신 버전 확인"));
    readonly Button openModButton = Theme.Button(L.T("모드 폴더 열기"));
    readonly Button folderSettingsButton = Theme.Button(L.T("폴더 설정..."));
    readonly Button launchButton = Theme.Button(L.T("게임 실행"), primary: true);
    readonly DarkComboBox languageBox = new(ComboBoxStyle.DropDownList) { Width = 100 };
    /// <summary>Shown in the status bar when GitHub has a newer release of this manager.</summary>
    readonly Label updateLink = new() { AutoSize = true, Dock = DockStyle.Right, Padding = new Padding(8, 5, 4, 5), ForeColor = Theme.Warning, Cursor = Cursors.Hand, Visible = false };
    /// <summary>Starts the game program; checks replace it so no game starts.</summary>
    public Action<string> StartGame { get; set; } = exe =>
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! })?.Dispose();
    /// <summary>Asks GitHub for a newer release of this manager; checks replace it.</summary>
    public Func<string, (string Version, string Url)?> CheckForAppUpdate { get; set; } = AppUpdate.Check;
    /// <summary>Starts the manager again; checks replace it so nothing restarts.</summary>
    public Action RestartApp { get; set; } = Application.Restart;
    readonly Button openLeftoversButton = Theme.Button(L.T("보관 폴더 열기"));
    readonly Button clearLeftoversButton = Theme.Button(L.T("보관 폴더 비우기..."));
    readonly Label statusLabel = new() { AutoSize = true, Dock = DockStyle.Left, Padding = new Padding(8, 5, 8, 5), ForeColor = Theme.Muted, Text = L.T("준비") };

    List<ModGroup> mods = new();
    List<InstalledMod> installed = new();
    List<InstalledRow> installedRows = new();
    List<Tx> history = new();
    readonly Dictionary<string, string> chosenVersion = new(StringComparer.OrdinalIgnoreCase);
    string? selectAfterRefresh;
    string? shownNoteFor;
    bool busy;
    int fileListRequest;

    public MainForm(Manager manager, string[] filesToAdd, Action<string>? openInBrowser = null)
    {
        this.manager = manager;
        this.openInBrowser = openInBrowser ?? (url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
        meta = manager.LoadMeta();
        categories = manager.LoadCategories(meta);
        remote = manager.LoadRemote();

        Text = L.T("PBZ 모드 매니저");
        Theme.Apply(this);
        ClientSize = new Size(1440, 860);
        MinimumSize = new Size(1180, 680);
        StartPosition = FormStartPosition.CenterScreen;

        // One row, every item the control height and centred on it: the folder, then the JS loader choice, then a button.
        var header = new Panel { Dock = DockStyle.Top, Height = Theme.ControlHeight + 12, Padding = new Padding(8, 6, 8, 6) };
        header.Controls.Add(new Label { AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted, Text = L.F("게임: {0}    모드 보관: {1}", manager.Root, manager.ModDir) });
        var runtimeLabel = new Label
        {
            Text = L.T("JS 모드 실행기"),
            AutoSize = false,
            Height = Theme.ControlHeight,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 0, 6, 0),
        };
        runtimeLabel.Width = TextRenderer.MeasureText(runtimeLabel.Text, runtimeLabel.Font).Width + 4;
        runtimeBox.Dock = DockStyle.None;
        runtimeBox.Margin = new Padding(0, 0, 14, 0);
        openModButton.Margin = Padding.Empty;
        var tools = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = Padding.Empty, Margin = Padding.Empty };
        folderSettingsButton.Margin = new Padding(6, 0, 0, 0);
        languageBox.Dock = DockStyle.None;
        languageBox.Margin = new Padding(14, 0, 0, 0);
        foreach (var language in L.Languages) languageBox.Items.Add(language.Name);
        languageBox.SelectedIndex = Array.FindIndex(L.Languages, l => l.Code == L.Current);
        launchButton.Margin = new Padding(14, 0, 0, 0);
        tools.Controls.AddRange(new Control[] { runtimeLabel, runtimeBox, openModButton, folderSettingsButton, languageBox, launchButton });
        header.Controls.Add(tools);
        var tip = new ToolTip();
        var explain = L.T("JavaScript 모드를 실행하는 프로그램입니다. PBZModLoader와 Mods Menu 중 쓰는 쪽을 고르면,\n그 프로그램이 읽는 폴더에 맞춰 모드를 설치합니다. 자동은 게임 폴더에 설치된 쪽을 따릅니다.");
        tip.SetToolTip(runtimeLabel, explain);
        tip.SetToolTip(runtimeBox, explain);
        tip.SetToolTip(languageBox, "Language / 언어 / 语言 / 言語");
        tip.SetToolTip(launchButton, manager.GameExe);
        foreach (var item in new[] { L.T("자동"), "PBZModLoader", "Mods Menu" }) runtimeBox.Items.Add(item);
        ShowRuntime();
        var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Theme.Surface };
        statusBar.Controls.Add(statusLabel);
        // Where the source lives, with the running version beside it.
        var projectLink = new Label
        {
            Text = $"GitHub · v{AppVersion}",
            AutoSize = true,
            Dock = DockStyle.Right,
            Padding = new Padding(8, 5, 10, 5),
            ForeColor = Theme.Info,
            Cursor = Cursors.Hand,
        };
        new ToolTip().SetToolTip(projectLink, ProjectUrl);
        projectLink.Click += (_, _) => this.openInBrowser(ProjectUrl);
        // Docked from the last added inward: the project link stays in the corner, the update notice beside it.
        statusBar.Controls.Add(updateLink);
        statusBar.Controls.Add(projectLink);

        var filterLabel = new Label { Text = L.T("카테고리"), AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(12, 6, 6, 0) };
        filterBox.Width = 130;

        // Left: the mod list over its info. Right: what is installed over its files.
        var zipSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2 };
        Theme.StyleSplit(zipSplit);
        var zipGroup = Theme.Group(L.T("모드 목록 (여기에 끌어다 놓아 추가)"), zipList,
            new Control[] { installButton, addButton, checkLatestButton, refreshButton, unlistButton, deleteFileButton, cleanVersionsButton },
            new Control[] { filterLabel, filterBox });
        zipSplit.Panel1.Controls.Add(zipGroup);
        zipSplit.Panel2.Controls.Add(Theme.Group(L.T("모드 정보"), infoEditor));
        EnableFileDrop(zipGroup);

        var lower = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        Theme.StyleSplit(lower);
        lower.Panel1.Controls.Add(Theme.Group(L.T("설치된 모드"), installedList, removeButton, optionsButton, moveUpButton, moveDownButton, separatorButton, openLeftoversButton, clearLeftoversButton));
        lower.Panel2.Controls.Add(Theme.Group(L.T("선택한 모드의 파일"), fileList));

        var main = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        Theme.StyleSplit(main);
        main.Panel1.Controls.Add(zipSplit);
        main.Panel2.Controls.Add(lower);

        Controls.Add(main);
        Controls.Add(statusBar);
        Controls.Add(header);

        Application.AddMessageFilter(this);
        overlay = new BusyOverlay(this);
        FormClosed += (_, _) => { Application.RemoveMessageFilter(this); overlay.Dispose(); };
        FormClosing += (_, e) =>
        {
            e.Cancel = busy;
            if (!e.Cancel) RememberWindow(main, zipSplit, lower);
        };
        PlaceWindow();

        // The mod list and the installed list keep one height: moving either splitter moves the other.
        bool syncing = false;
        void Follow(SplitContainer from, SplitContainer to)
        {
            if (syncing || to.SplitterDistance == from.SplitterDistance) return;
            syncing = true;
            SetSplit(to, from.SplitterDistance, to.SplitterDistance);
            syncing = false;
        }
        zipSplit.SplitterMoved += (_, _) => Follow(zipSplit, lower);
        lower.SplitterMoved += (_, _) => Follow(lower, zipSplit);
        zipList.MultiSelect = true;
        installedList.MultiSelect = true;

        Load += async (_, _) =>
        {
            var saved = manager.Window;
            SetSplit(main, saved?.MainSplit ?? 0, main.Width / 2);
            SetSplit(zipSplit, saved?.LeftSplit ?? 0, Math.Max(200, zipSplit.Height - 330));
            SetSplit(lower, zipSplit.SplitterDistance, zipSplit.SplitterDistance);
            await RefreshAll();
            _ = LookForAppUpdate();
            await OfferRecovery();
            if (filesToAdd.Length > 0) await AddZips(filesToAdd);
        };
        installButton.Click += async (_, _) => { await Task.Yield(); await InstallChosen(); };
        addButton.Click += async (_, _) => { await Task.Yield(); await ChooseAndAdd(); };
        unlistButton.Click += async (_, _) => await UnlistChosen();
        deleteFileButton.Click += async (_, _) => { await Task.Yield(); await DeleteChosenFile(); };
        cleanVersionsButton.Click += async (_, _) => { await Task.Yield(); if (SelectedMod() is { } mod) await CleanOldVersions(mod); };
        removeButton.Click += async (_, _) =>
        {
            await Task.Yield();
            if (SelectedRow() is { IsSeparator: true } separator) await RemoveSeparator(separator);
            else if (SelectedInstalledMods() is { Count: > 0 } chosen) await RemoveMods(chosen);
        };
        optionsButton.Click += async (_, _) => { if (SelectedInstalled() is { } mod) await ChangeOptions(mod); };
        separatorButton.Click += async (_, _) => { await Task.Yield(); await AddSeparator(); };
        installedList.DoubleClick += async (_, _) => { await Task.Yield(); if (SelectedRow() is { IsSeparator: true } row) await RenameSeparator(row); };
        runtimeBox.SelectionChangeCommitted += async (_, _) => { await Task.Yield(); await ChangeRuntime(); };
        moveUpButton.Click += async (_, _) => await MoveSelected(-1);
        moveDownButton.Click += async (_, _) => await MoveSelected(1);
        refreshButton.Click += async (_, _) => await RefreshAll();
        checkLatestButton.Click += async (_, _) => await CheckLatest();
        infoEditor.DownloadRequested += async () =>
        {
            await Task.Yield();
            if (SelectedMod() is not { } mod) return;
            if (LinkOf(mod) == null && GitHubOf(mod) == null) DarkDialog.Show(this, L.T("모드 정보의 주소에 Nexus 모드 페이지나 GitHub 저장소 주소를 넣어 주세요."), Text);
            else OpenDownloadPage(mod);
        };
        openModButton.Click += (_, _) => OpenFolder(manager.ModDir);
        folderSettingsButton.Click += async (_, _) => { await Task.Yield(); ChangeFolders(); };
        launchButton.Click += async (_, _) => { await Task.Yield(); LaunchGame(); };
        languageBox.SelectionChangeCommitted += async (_, _) => { await Task.Yield(); ChangeLanguage(); };
        openLeftoversButton.Click += (_, _) => OpenFolder(manager.LeftoversDir);
        clearLeftoversButton.Click += async (_, _) => { await Task.Yield(); await ClearLeftovers(); };
        filterBox.SelectionChangeCommitted += (_, _) => RenderZips();
        infoEditor.Edited += NoteEdited;
        infoEditor.PicturesRequested += async () =>
        {
            await Task.Yield();
            if (SelectedMod() is { } mod && (ChosenVersion() ?? mod.Latest) is { Missing: false } shown) PictureWindow.Show(this, mod.Name, shown.Path);
        };
        infoEditor.CategoryFound += category =>
        {
            RememberCategory(category);
            RenderFilter();
            infoEditor.SetCategories(categories);
            infoEditor.SelectCategory(category);
        };
        infoEditor.ManageRequested += async () => { await Task.Yield(); ManageCategories(this); };
        infoEditor.VersionChosen += index =>
        {
            if (SelectedMod() is not { } mod || index >= mod.Versions.Count) return;
            chosenVersion[mod.Key] = mod.Versions[index].Path;
            // The tool field follows the version shown.
            LoadNote(force: true);
            UpdateButtons();
        };

        // A selection change passes through "nothing selected"; reacting to that blanks the panels for a moment.
        zipList.SelectedIndexChanged += (_, _) =>
        {
            if (zipList.SelectedItems.Count == 0 && zipList.Items.Count > 0) return;
            LoadNote();
            UpdateButtons();
        };
        installedList.SelectedIndexChanged += async (_, _) =>
        {
            if (installedList.SelectedItems.Count == 0 && installedList.Items.Count > 0) return;
            UpdateButtons();
            await ShowFilesOfSelected();
        };
    }

    // ---------------------------------------------------------------- busy state

    /// <summary>
    /// Blocks clicks and keys during work. Disabling the window instead would redraw every control
    /// in its disabled colors and back, a flash on each operation, and the text there goes dark.
    /// </summary>
    public bool PreFilterMessage(ref Message m)
    {
        const int KeyFirst = 0x0100, KeyLast = 0x0109, MouseMove = 0x0200, MouseFirst = 0x0201, MouseLast = 0x020E;
        return busy && (m.Msg is >= KeyFirst and <= KeyLast || (m.Msg is >= MouseFirst and <= MouseLast && m.Msg != MouseMove));
    }

    // ---------------------------------------------------------------- the list

    void EnableFileDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        control.DragDrop += (_, e) =>
        {
            // Deferred so the Explorer window that started the drag is not held up by this work.
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
                BeginInvoke(async () => await AddZips(files));
        };
        foreach (Control child in control.Controls) EnableFileDrop(child);
    }

    async Task ChooseAndAdd()
    {
        using var dialog = new OpenFileDialog
        {
            Title = L.T("추가할 모드 압축 파일 선택"),
            Filter = L.T("모드 압축 파일 (*.zip, *.rar, *.7z)|*.zip;*.rar;*.7z"),
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) await AddZips(dialog.FileNames);
    }

    async Task AddZips(string[] sources)
    {
        var added = await Run(L.T("압축 파일 추가 중..."), progress =>
        {
            var paths = new List<string>();
            var errors = new List<string>();
            foreach (var source in sources)
            {
                try { paths.Add(manager.AddZip(source, progress)); }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) { errors.Add(ex.Message); }
            }
            return (Paths: paths, Errors: errors);
        });
        if (added == default) return;

        var newFiles = new List<string>();
        foreach (var path in added.Paths)
        {
            var name = Path.GetFileName(path);
            if (!meta.TryGetValue(name, out var note)) meta[name] = note = new ModMeta();
            if (note.Registered) continue;
            note.Registered = true;
            newFiles.Add(path);
        }
        SaveMeta();
        if (added.Paths.Count > 0) selectAfterRefresh = added.Paths[^1];
        await RefreshAll();
        if (added.Errors.Count > 0)
            DarkDialog.Show(this, string.Join("\n", added.Errors), Text, isError: true);

        // A new version of a known mod inherits its info; only a mod not seen before is asked about.
        var asking = new List<string>();
        foreach (var path in newFiles)
        {
            var name = Path.GetFileName(path);
            var group = mods.FirstOrDefault(m => m.Versions.Any(v => v.FileName == name));
            if (group == null) continue;
            var known = group.Versions.Where(v => v.FileName != name).Select(v => NoteOf(v.FileName)).FirstOrDefault(HasInfo);
            if (known != null)
            {
                var note = meta[name];
                note.Category = known.Category;
                note.Url = known.Url;
                note.Memo = known.Memo;
                note.InstallNotes = known.InstallNotes;
            }
            if (group.Versions.Count == 1) asking.Add(name);
        }
        SaveMeta();
        await AskForInfo(asking);
        await OfferUpdates(newFiles);
    }

    /// <summary>Offers to fill in each new mod's info. Skipping loses nothing: the panel can do it later.</summary>
    async Task AskForInfo(List<string> names)
    {
        var saved = new List<ModMeta>();
        for (int i = 0; i < names.Count; i++)
        {
            var note = meta[names[i]];
            var tool = mods.FirstOrDefault(m => m.Versions.Any(v => v.FileName == names[i]))?.Versions.First(v => v.FileName == names[i]).Tool ?? "";
            using var dialog = new ModInfoDialog(names[i], note, categories, moreToCome: i < names.Count - 1, ManageCategories, RememberCategory, tool);
            var answer = dialog.ShowDialog(this);
            if (answer == ModInfoDialog.SkipAll) break;
            if (answer != DialogResult.OK) continue;
            dialog.CopyTo(note);
            SaveMeta();
            saved.Add(note);
        }
        await FillCategories(saved);
        RenderFilter();
        RenderZips();
        LoadNote(force: true);
    }

    /// <summary>
    /// For an address saved before its lookup in the dialog could finish: an empty category and an empty
    /// memo are filled from the page. A failure leaves them empty.
    /// </summary>
    async Task FillCategories(List<ModMeta> notes)
    {
        foreach (var note in notes)
        {
            if ((note.Category.Length > 0 && note.InstallNotes.Trim().Length > 0) || NexusApi.LinkFor(note.Url, Array.Empty<string>()) is not { } link) continue;
            var found = await Task.Run(() =>
            {
                try { return NexusApi.GetPageInfo(link); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException
                    or System.Text.Json.JsonException or KeyNotFoundException or OverflowException) { return (Category: "", InstallNotes: ""); }
            });
            if (note.InstallNotes.Trim().Length == 0 && found.InstallNotes.Length > 0) note.InstallNotes = found.InstallNotes;
            if (note.Category.Length == 0 && found.Category.Length > 0)
            {
                note.Category = found.Category;
                RememberCategory(found.Category);
            }
            SaveMeta();
        }
    }

    /// <summary>When a file just added is newer than what is installed, offers to update right away.</summary>
    async Task OfferUpdates(List<string> newFiles)
    {
        foreach (var path in newFiles)
        {
            var name = Path.GetFileName(path);
            var group = mods.FirstOrDefault(m => m.Versions.Any(v => v.FileName == name));
            if (group?.InstalledFile == null || ModIdentity.CompareFiles(name, group.InstalledFile) <= 0) continue;

            var version = ModIdentity.ParseFileName(name).Version;
            var answer = DarkDialog.Show(this,
                L.F("{0}의 새 버전을 추가했습니다.{1}\n\n지금 업데이트할까요? 나중에 모드 목록에서 해도 됩니다.", group.Name, Change(group.InstalledVersion, version).Replace(" (", "\n").TrimEnd(')')),
                Text, yesNo: true);
            if (answer != DialogResult.Yes) continue;

            chosenVersion[group.Key] = path;
            selectAfterRefresh = path;
            RenderZips();
            await InstallChosen();
        }
    }

    /// <summary>Takes the chosen file off the list. It stays in the folder, and its notes are kept for if it returns.</summary>
    async Task UnlistChosen()
    {
        if (ChosenVersion() is not { } version || IsInstalledFile(version.FileName)) return;
        NoteOf(version.FileName).Registered = false;
        SaveMeta();
        await RefreshAll();
    }

    bool IsInstalledFile(string fileName) =>
        history.Any(t => t.ZipName.Equals(fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Versions kept besides the newest and the installed one: those a clean-up can delete.</summary>
    List<ModVersion> OldVersions(ModGroup mod) =>
        mod.Versions.Where(v => v != mod.Latest && !v.Missing && !IsInstalledFile(v.FileName)).ToList();

    /// <summary>Sends the chosen version's archive to the Recycle Bin and takes it off the list.</summary>
    async Task DeleteChosenFile()
    {
        if (ChosenVersion() is not { Missing: false } version) return;
        if (DarkDialog.Show(this, L.F("이 파일을 휴지통으로 보낼까요?\n{0}\n\n모드 정보(카테고리·주소·메모)는 남겨 둡니다.", version.FileName), L.T("파일 삭제"), yesNo: true) != DialogResult.Yes) return;
        await DeleteFiles(new[] { version });
    }

    async Task CleanOldVersions(ModGroup mod)
    {
        var old = OldVersions(mod);
        if (old.Count == 0) return;
        var keep = new[] { mod.Latest.FileName }.Concat(mod.InstalledFile is { } installedFile ? new[] { installedFile } : Array.Empty<string>()).Distinct();
        var message = L.F("{0}의 이전 버전 {1}개를 휴지통으로 보낼까요?\n", mod.Name, old.Count)
            + string.Join("\n", old.Select(v => "  " + v.FileName))
            + L.T("\n\n남기는 파일:\n") + string.Join("\n", keep.Select(f => "  " + f));
        if (DarkDialog.Show(this, message, L.T("이전 버전 정리"), yesNo: true) != DialogResult.Yes) return;
        await DeleteFiles(old);
    }

    async Task DeleteFiles(IReadOnlyList<ModVersion> versions)
    {
        var failed = await Run(L.T("파일 삭제 중..."), _ =>
        {
            var errors = new List<string>();
            foreach (var version in versions)
            {
                try { manager.DeleteZip(version.FileName); }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) { errors.Add(ex.Message); }
            }
            return errors;
        });
        if (failed == null) return;
        foreach (var version in versions.Where(v => !File.Exists(v.Path))) NoteOf(version.FileName).Registered = false;
        SaveMeta();
        await RefreshAll();
        if (failed.Count > 0) DarkDialog.Show(this, string.Join("\n\n", failed), L.T("파일 삭제"), isError: true);
        else SetStatus(L.F("파일 {0}개를 휴지통으로 보냈습니다.", versions.Count));
    }

    void SaveMeta()
    {
        try
        {
            manager.SaveMeta(meta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(L.F("모드 정보 저장 실패: {0}", ex.Message));
        }
    }

    // ---------------------------------------------------------------- notes and categories

    ModGroup? SelectedMod() => zipList.SelectedItems.Count > 0 ? (ModGroup)zipList.SelectedItems[0].Tag! : null;

    InstalledRow? SelectedRow() => installedList.SelectedItems.Count > 0 ? (InstalledRow)installedList.SelectedItems[0].Tag! : null;

    InstalledMod? SelectedInstalled() => SelectedRow()?.Mod;

    ModVersion? ChosenVersion()
    {
        if (SelectedMod() is not { } mod) return null;
        return chosenVersion.TryGetValue(mod.Key, out var path) && mod.Versions.FirstOrDefault(v => v.Path == path) is { } chosen
            ? chosen
            : mod.Latest;
    }

    ModMeta NoteOf(string fileName) =>
        meta.TryGetValue(fileName, out var m) ? m : meta[fileName] = new ModMeta { Registered = true };

    static bool HasInfo(ModMeta m) => m.Category.Length > 0 || m.Url.Length > 0 || m.Memo.Length > 0 || m.Tool.Length > 0 || m.InstallNotes.Length > 0;

    /// <summary>Info belongs to the mod; it is stored per file, so any version that has it speaks for them all.</summary>
    ModMeta MetaOf(ModGroup mod) =>
        mod.Versions.Select(v => meta.GetValueOrDefault(v.FileName)).FirstOrDefault(m => m != null && HasInfo(m)) ?? new ModMeta();

    static string CategoryLabel(ModMeta m) => m.Category.Length > 0 ? m.Category : Manager.NoCategory;

    int CountMods(string category) =>
        mods.Count(m => MetaOf(m).Category.Equals(category, StringComparison.OrdinalIgnoreCase));

    void LoadNote(bool force = false)
    {
        var mod = SelectedMod();
        ShowVersions(mod);
        if (!force && mod?.Key == shownNoteFor) return;
        shownNoteFor = mod?.Key;
        infoEditor.SetCategories(categories);
        var shown = mod == null ? null : ChosenVersion() ?? mod.Latest;
        var note = mod == null ? new ModMeta() : MetaOf(mod);
        // The tool is the shown version's own, not shared across versions like the rest.
        var view = new ModMeta { Category = note.Category, Url = note.Url, Memo = note.Memo, InstallNotes = note.InstallNotes, Registered = note.Registered, Tool = shown == null ? "" : meta.GetValueOrDefault(shown.FileName)?.Tool ?? "" };
        infoEditor.Show(mod?.Name ?? L.T("모드를 선택하세요"), view, mod != null, shown?.Tool ?? "");
        infoEditor.SetPictureCount(shown is { Missing: false } ? Manager.PicturesIn(shown.Path).Count : 0);
    }

    /// <summary>The tool a version is made for: what the user set for it, or else what its archive holds.</summary>
    string ToolOf(ModVersion version) => ModIdentity.ToolLabel(meta.GetValueOrDefault(version.FileName)?.Tool is { Length: > 0 } set ? set : version.Tool);

    void ShowVersions(ModGroup? mod)
    {
        if (mod == null)
        {
            infoEditor.SetVersions(Array.Empty<string>(), -1);
            return;
        }
        var tools = mod.Versions.Select(ToolOf).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var labels = mod.Versions.Select(v =>
        {
            var text = v.Version.Length > 0 ? v.Version : v.FileName;
            // Versions made for different tools say which.
            if (tools > 1 && ToolOf(v).Length > 0) text += $" [{ToolOf(v)}]";
            if (v.FileName.Equals(mod.InstalledFile, StringComparison.OrdinalIgnoreCase)) text += L.T(" (설치됨)");
            else if (v == mod.Latest && mod.Versions.Count > 1) text += L.T(" (최신)");
            if (v.Missing) text += L.T(" (파일 없음)");
            return text;
        }).ToList();
        infoEditor.SetVersions(labels, ChosenVersion() is { } chosen ? mod.Versions.IndexOf(chosen) : 0);
    }

    /// <summary>Everything typed or chosen is saved right away, for every version of the mod.</summary>
    void SaveRemoteQuietly()
    {
        try
        {
            manager.SaveRemote(remote);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(L.F("확인 결과 저장 실패: {0}", ex.Message));
        }
    }

    void NoteEdited()
    {
        if (SelectedMod() is not { } mod) return;

        var shown = ChosenVersion() ?? mod.Latest;
        bool urlChanged = MetaOf(mod).Url != infoEditor.Url;
        bool toolChanged = (meta.GetValueOrDefault(shown.FileName)?.Tool ?? "") != infoEditor.Tool;
        bool listChanged = MetaOf(mod).Category != infoEditor.Category || toolChanged || urlChanged;
        // What Nexus said came from the old address; it no longer speaks for this mod.
        if (urlChanged && remote.Remove(mod.Key)) SaveRemoteQuietly();
        foreach (var version in mod.Versions)
        {
            var note = NoteOf(version.FileName);
            note.Category = infoEditor.Category;
            note.Url = infoEditor.Url;
            note.Memo = infoEditor.Memo;
            note.InstallNotes = infoEditor.InstallNotes;
        }
        // One mod can change tools between versions, so the tool belongs to the version shown.
        NoteOf(shown.FileName).Tool = infoEditor.Tool;
        // The memo also shows in the installed list.
        foreach (ListViewItem item in installedList.Items)
            if (item.Tag is InstalledRow { Mod: { } installedMod } && installedMod.Key.Equals(mod.Key, StringComparison.OrdinalIgnoreCase)
                && item.SubItems[3].Text != infoEditor.Memo)
                item.SubItems[3].Text = infoEditor.Memo;
        SaveMeta();
        // The tool and the page show on the list: the page keeps mods of one page together.
        if (listChanged) RenderZips();
    }

    void ManageCategories(IWin32Window owner)
    {
        using (var dialog = new CategoryDialog(meta, categories, SaveCategories, CountMods)) dialog.ShowDialog(owner);
        RenderFilter();
        RenderZips();
        LoadNote(force: true);
    }

    void SaveCategories()
    {
        try
        {
            manager.SaveCategories(categories);
            manager.SaveMeta(meta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(L.F("카테고리 저장 실패: {0}", ex.Message));
        }
    }

    void RenderFilter()
    {
        var choices = new[] { Manager.AllCategories, Manager.NoCategory }.Concat(categories).ToList();
        if (filterBox.Items.Cast<string>().SequenceEqual(choices)) return;

        var current = filterBox.SelectedItem as string ?? Manager.AllCategories;
        filterBox.Items.Clear();
        foreach (var c in choices) filterBox.Items.Add(c);
        filterBox.SelectedItem = choices.Contains(current) ? current : Manager.AllCategories;
    }

    void RememberCategory(string category)
    {
        if (categories.Contains(category, StringComparer.OrdinalIgnoreCase)) return;
        categories.Add(category);
        SaveCategories();
    }

    // ---------------------------------------------------------------- newest version on Nexus

    /// <summary>A version on Nexus that is newer than any file the user has downloaded.</summary>
    /// A file name without a version, such as "Wuchang Volice 2.zip", says nothing to compare unless the source
    /// recognised the file by its checksum, as GitHub can; otherwise it is never called outdated.
    RemoteInfo? NewerOnNexus(ModGroup mod) =>
        remote.TryGetValue(mod.Key, out var info) && LocalVersionOf(mod) is { Length: > 0 } local
        && ModIdentity.CompareVersions(info.Version, local) > 0 ? info : null;

    /// <summary>The newest local version: from its file name, or as the source recognised the file.</summary>
    string LocalVersionOf(ModGroup mod) =>
        mod.Latest.Version.Length > 0 ? mod.Latest.Version : remote.TryGetValue(mod.Key, out var info) ? info.LocalVersion : "";

    static string SourceName(RemoteInfo info) => info.Source == "github" ? "GitHub" : "Nexus";

    NexusLink? LinkOf(ModGroup mod) => NexusApi.LinkFor(MetaOf(mod).Url, mod.Versions.Select(v => v.FileName));

    GitHubLink? GitHubOf(ModGroup mod) => GitHubApi.LinkFor(MetaOf(mod).Url);

    /// <summary>Sends the user to the new file's download when there is one, otherwise to the page's file list or releases.</summary>
    void OpenDownloadPage(ModGroup mod)
    {
        string? url = null;
        if (LinkOf(mod) is { } link) url = link.DownloadUrl(NewerOnNexus(mod)?.FileId ?? "");
        else if (GitHubOf(mod) is { } repo)
            url = remote.TryGetValue(mod.Key, out var info) && info.Source == "github" && info.PageUrl.Length > 0 ? info.PageUrl : repo.ReleasesUrl;
        if (url == null) return;
        try
        {
            openInBrowser(url);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus(L.F("브라우저를 열지 못했습니다: {0}", ex.Message));
        }
    }

    /// <summary>Asks Nexus, and GitHub for mods kept there, about every listed mod. Only ever runs from the button.</summary>
    async Task CheckLatest()
    {
        var targets = mods.Select(m => (m.Key, m.Name, File: m.InstalledFile ?? m.Latest.FileName, Link: LinkOf(m)))
            .Where(t => t.Link != null)
            .Select(t => (t.Key, t.Name, t.File, Link: t.Link!))
            .ToList();
        var onGitHub = mods.Where(m => targets.All(t => t.Key != m.Key) && GitHubOf(m) != null)
            .Select(m => (m.Key, m.Name, Repo: GitHubOf(m)!, Zip: m.Latest.Missing ? null : m.Latest.Path))
            .ToList();
        var unknown = mods.Where(m => targets.All(t => t.Key != m.Key) && onGitHub.All(g => g.Key != m.Key)).Select(m => m.Name).ToList();
        if (targets.Count == 0 && onGitHub.Count == 0)
        {
            // Nothing has an address any more, so no old result still applies.
            if (mods.Any(m => remote.Remove(m.Key)))
            {
                SaveRemoteQuietly();
                RenderZips();
            }
            DarkDialog.Show(this, L.T("확인할 수 있는 모드가 없습니다. 모드 정보의 주소에 Nexus 모드 페이지나 GitHub 저장소 주소를 넣어 주세요."), Text);
            return;
        }

        var unreachable = new List<string>();
        Dictionary<string, RemoteInfo>? found = await Run(L.T("최신 버전 확인 중..."), report =>
        {
            var results = new Dictionary<string, RemoteInfo>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (targets.Count > 0)
                    foreach (var pair in NexusApi.Check(targets)) results[pair.Key] = pair.Value;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new InvalidOperationException(L.T("Nexus에 연결하지 못했습니다. 인터넷 연결을 확인한 뒤 다시 시도해 주세요."), ex);
            }
            foreach (var (key, name, repo, zip) in onGitHub)
            {
                report(L.F("GitHub에서 {0} 확인 중...", name));
                try
                {
                    if (GitHubApi.Check(repo, zip) is { } info) results[key] = info;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException or IOException)
                {
                    // One repository that cannot be read does not stop the others.
                    unreachable.Add(name);
                }
            }
            return results;
        });
        if (found == null) return;

        // A listed mod this check gave nothing for, with no address or one the source does not know, keeps no old result.
        foreach (var mod in mods.Where(m => !found.ContainsKey(m.Key) && !unreachable.Contains(m.Name))) remote.Remove(mod.Key);
        foreach (var pair in found) remote[pair.Key] = pair.Value;
        SaveRemoteQuietly();

        // With the page known, the address field's Open button can take the user straight to the download.
        foreach (var target in targets.Where(t => found.ContainsKey(t.Key)))
        {
            var mod = mods.First(m => m.Key == target.Key);
            if (MetaOf(mod).Url.Length == 0)
                foreach (var version in mod.Versions) NoteOf(version.FileName).Url = target.Link.PageUrl;

            // A category the user chose stays; only an empty one takes the one from Nexus.
            var nexusCategory = found[target.Key].Category;
            if (MetaOf(mod).Category.Length == 0 && nexusCategory.Length > 0)
            {
                foreach (var version in mod.Versions) NoteOf(version.FileName).Category = nexusCategory;
                RememberCategory(nexusCategory);
            }
        }
        RenderFilter();
        SaveMeta();
        // Both lists show what the check found, the installed one included.
        await RefreshAll();
        LoadNote(force: true);

        var newer = mods.Where(m => NewerOnNexus(m) != null).ToList();
        var missing = targets.Where(t => !found.ContainsKey(t.Key)).Select(t => t.Name)
            .Concat(onGitHub.Where(g => !found.ContainsKey(g.Key) && !unreachable.Contains(g.Name)).Select(g => g.Name))
            .Concat(unknown).ToList();
        var pageOnly = mods.Where(m => found.TryGetValue(m.Key, out var i) && !i.MatchedFile).Select(m => m.Name).ToList();
        var hidden = mods.Where(m => found.TryGetValue(m.Key, out var i) && i.Status.Length > 0 && !i.Status.Equals("published", StringComparison.OrdinalIgnoreCase)).Select(m => m.Name).ToList();
        var compared = found.Keys.Count(k => mods.Any(m => m.Key == k && LocalVersionOf(m).Length > 0));

        var lines = new List<string>
        {
            newer.Count > 0
                ? L.T("새 버전이 있습니다:\n") + string.Join("\n", newer.Select(m => $"  {m.Name}: {LocalVersionOf(m)} → {NewerOnNexus(m)!.Version} ({SourceName(NewerOnNexus(m)!)})"))
                : L.F("받아 둔 모드 {0}개 모두 최신입니다.", compared),
        };
        var recognised = mods.Where(m => m.Latest.Version.Length == 0 && found.TryGetValue(m.Key, out var i) && i.LocalVersion.Length > 0)
            .Select(m => $"{m.Name} = {found[m.Key].LocalVersion}").ToList();
        if (recognised.Count > 0) lines.Add(L.F("파일 내용으로 버전을 확인함: {0}", string.Join(", ", recognised)));
        if (pageOnly.Count > 0) lines.Add(L.F("같은 이름의 파일을 찾지 못해 페이지 버전으로 비교: {0}", string.Join(", ", pageOnly)));
        var noVersion = mods.Where(m => LocalVersionOf(m).Length == 0 && found.ContainsKey(m.Key))
            .Select(m => $"{m.Name} ({SourceName(found[m.Key])}: {found[m.Key].Version})").ToList();
        if (noVersion.Count > 0) lines.Add(L.F("파일 이름에 버전이 없어 비교하지 못함: {0}", string.Join(", ", noVersion)));
        if (hidden.Count > 0) lines.Add(L.F("페이지가 공개 상태가 아님: {0}", string.Join(", ", hidden)));
        if (unreachable.Count > 0) lines.Add(L.F("GitHub에 연결하지 못함 (잠시 후 다시 시도해 주세요): {0}", string.Join(", ", unreachable)));
        if (missing.Count > 0) lines.Add(L.F("확인하지 못함 (모드 정보의 주소를 확인해 주세요): {0}", string.Join(", ", missing)));
        SetStatus(newer.Count > 0 ? L.F("새 버전 {0}개 있음", newer.Count) : L.T("최신 버전 확인 완료"));
        if (newer.Count == 0)
        {
            DarkDialog.Show(this, string.Join("\n\n", lines), L.T("최신 버전 확인"));
            return;
        }

        lines.Add(L.F("다운로드 페이지를 브라우저로 열까요? ({0}개)\n받은 파일을 모드 목록에 끌어다 놓으면 업데이트할 수 있습니다.", newer.Count));
        if (DarkDialog.Show(this, string.Join("\n\n", lines), L.T("최신 버전 확인"), yesNo: true) == DialogResult.Yes)
            foreach (var mod in newer) OpenDownloadPage(mod);
    }

    // ---------------------------------------------------------------- install and update

    // ---------------------------------------------------------------- game, language, own updates

    /// <summary>Starts the Steam build of the game from the game folder.</summary>
    void LaunchGame()
    {
        if (busy) return;
        var exe = manager.GameExe;
        if (!File.Exists(exe))
        {
            DarkDialog.Show(this, L.F("게임 실행 파일을 찾지 못했습니다: {0}\n이 매니저는 Steam 버전 기준입니다. [폴더 설정]에서 게임 폴더를 확인해 주세요.", exe), Text, isError: true);
            return;
        }
        if (Manager.GameRunning())
        {
            DarkDialog.Show(this, L.T("게임이 이미 실행 중입니다."), Text);
            return;
        }
        try
        {
            StartGame(exe);
            SetStatus(L.T("게임을 실행했습니다."));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            DarkDialog.Show(this, L.F("게임을 실행하지 못했습니다: {0}", ex.Message), Text, isError: true);
        }
    }

    /// <summary>Saves the picked language; it takes effect when the manager starts again.</summary>
    void ChangeLanguage()
    {
        var index = languageBox.SelectedIndex;
        if (index < 0 || L.Languages[index].Code == L.Current) return;
        try
        {
            manager.SaveLanguage(L.Languages[index].Code);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DarkDialog.Show(this, L.F("설정 저장 실패: {0}", ex.Message), Text, isError: true);
            return;
        }
        // Asked in both languages: the new one is what the user reads best now.
        var question = L.T("바꾼 언어는 매니저를 다시 시작하면 적용됩니다. 지금 다시 시작할까요?");
        var was = L.Current;
        L.Use(L.Languages[index].Code);
        var inNew = L.T("바꾼 언어는 매니저를 다시 시작하면 적용됩니다. 지금 다시 시작할까요?");
        L.Use(was);
        if (DarkDialog.Show(this, inNew == question ? question : inNew + "\n\n" + question, "Language", yesNo: true) == DialogResult.Yes && !busy) RestartApp();
    }

    /// <summary>Asks GitHub once whether a newer manager is out; says so in the corner. Silent when it cannot tell.</summary>
    async Task LookForAppUpdate()
    {
        var found = await Task.Run(() => CheckForAppUpdate(AppVersion));
        if (found is not { } update || IsDisposed) return;
        updateLink.Text = L.F("새 버전 v{0} 있음", update.Version);
        new ToolTip().SetToolTip(updateLink, update.Url);
        updateLink.Click += (_, _) => openInBrowser(update.Url);
        updateLink.Visible = true;
    }

    // ---------------------------------------------------------------- folders

    /// <summary>Saves a new game folder with the records and restarts into it.</summary>
    void ChangeFolders()
    {
        if (busy) return;
        using var dialog = new FolderSettingsDialog(manager.Root, manager.ModDir);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var root = Path.GetFullPath(dialog.GameRoot).TrimEnd('\\');
        if (root.Equals(manager.Root, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            new Manager(root, manager.ModDir).RememberGameRoot();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DarkDialog.Show(this, L.F("설정을 저장하지 못했습니다: {0}", ex.Message), L.T("폴더 설정"), isError: true);
            return;
        }
        if (DarkDialog.Show(this, L.T("새 게임 폴더로 매니저를 다시 시작할까요?"), L.T("폴더 설정"), yesNo: true) == DialogResult.Yes) RestartApp();
    }
    // ---------------------------------------------------------------- window place

    /// <summary>Puts the window where it last closed, as long as enough of that place is still on a screen.</summary>
    void PlaceWindow()
    {
        if (manager.Window is not { Width: > 0, Height: > 0 } saved) return;
        var bounds = new Rectangle(saved.X, saved.Y, Math.Max(saved.Width, MinimumSize.Width), Math.Max(saved.Height, MinimumSize.Height));
        bool visible = Screen.AllScreens.Any(s =>
        {
            var shown = Rectangle.Intersect(s.WorkingArea, bounds);
            return shown.Width >= 200 && shown.Height >= 100;
        });
        if (!visible) return;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        if (saved.Maximized) WindowState = FormWindowState.Maximized;
    }

    void RememberWindow(SplitContainer main, SplitContainer left, SplitContainer right)
    {
        var normal = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        try
        {
            manager.SaveWindow(new WindowLayout
            {
                X = normal.X, Y = normal.Y, Width = normal.Width, Height = normal.Height,
                Maximized = WindowState == FormWindowState.Maximized,
                MainSplit = main.SplitterDistance, LeftSplit = left.SplitterDistance, RightSplit = right.SplitterDistance,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth keeping the window open for: it opens at its usual place next time.
        }
    }

    /// <summary>A saved splitter place when it still fits the window, else the usual one.</summary>
    static void SetSplit(SplitContainer split, int saved, int usual)
    {
        int room = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
        int wanted = saved > split.Panel1MinSize && saved < room - split.Panel2MinSize - split.SplitterWidth ? saved : usual;
        try
        {
            split.SplitterDistance = wanted;
        }
        catch (InvalidOperationException)
        {
            // The window is too small for either place; the control keeps its own.
        }
    }

    // ---------------------------------------------------------------- script loader

    static string RuntimeName(ScriptRuntime runtime) => runtime switch
    {
        ScriptRuntime.Loader => "PBZModLoader",
        ScriptRuntime.Menu => "Mods Menu",
        _ => L.T("설치된 것"),
    };

    /// <summary>Shows the choice, and on automatic, which loader that comes to now.</summary>
    void ShowRuntime()
    {
        var auto = manager.Runtime == ScriptRuntime.Auto && manager.TargetRuntime != ScriptRuntime.Auto
            ? L.F("자동 ({0})", RuntimeName(manager.TargetRuntime)) : L.T("자동");
        if ((string)runtimeBox.Items[0]! != auto) runtimeBox.Items[0] = auto;
        if (runtimeBox.SelectedIndex != (int)manager.Runtime) runtimeBox.SelectedIndex = (int)manager.Runtime;
    }

    /// <summary>
    /// Keeps the new choice for what is installed from now on, and offers to move the installed mods that
    /// it places differently. Each is an ordinary install, so cancelling its update brings the old place back.
    /// </summary>
    async Task ChangeRuntime()
    {
        var picked = (ScriptRuntime)Math.Max(0, runtimeBox.SelectedIndex);
        if (picked == manager.Runtime || busy) { ShowRuntime(); return; }
        try
        {
            manager.SetRuntime(picked);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(L.F("설정 저장 실패: {0}", ex.Message));
            ShowRuntime();
            return;
        }
        ShowRuntime();

        var target = manager.TargetRuntime;
        var notes = new List<string>();
        if (target == ScriptRuntime.Menu && !manager.MenuInstalled) notes.Add(L.T("Mods Menu가 아직 설치되어 있지 않습니다. 모드 목록에서 Mods Menu를 설치해 주세요."));
        if (target == ScriptRuntime.Loader && !manager.LoaderInstalled) notes.Add(L.T("PBZModLoader가 아직 설치되어 있지 않습니다. 모드 목록에서 PBZModLoader를 설치해 주세요."));
        if (target == ScriptRuntime.Menu && manager.LoaderInstalled) notes.Add(L.T("PBZModLoader도 설치되어 있습니다. 둘이 함께 있으면 하나만 동작하니 PBZModLoader는 제거해 주세요."));
        if (target == ScriptRuntime.Loader && manager.MenuInstalled) notes.Add(L.T("Mods Menu도 설치되어 있습니다. 둘이 함께 있으면 하나만 동작하니 Mods Menu는 제거해 주세요."));

        var moves = await Run(L.T("설치된 모드 확인 중..."), _ => manager.PlansToFollowRuntime());
        if (moves == null) return;
        if (moves.Count == 0)
        {
            DarkDialog.Show(this, string.Join("\n\n", new[] { L.F("앞으로 모드를 {0}에 맞춰 설치합니다. 다시 배치할 설치된 모드는 없습니다.", RuntimeName(target)) }.Concat(notes)), L.T("스크립트 로더"));
            return;
        }

        var question = L.F("앞으로 모드를 {0}에 맞춰 설치합니다.\n이미 설치된 다음 모드도 지금 다시 배치할까요?\n", RuntimeName(target))
            + string.Join("\n", moves.Select(m => "  " + m.Mod.Name))
            + L.T("\n\n고른 옵션과 뺀 파일은 그대로 씁니다.");
        if (DarkDialog.Show(this, string.Join("\n\n", new[] { question }.Concat(notes)), L.T("스크립트 로더"), yesNo: true) != DialogResult.Yes) return;

        var failed = await Run(L.T("다시 배치 중..."), report =>
        {
            var errors = new List<string>();
            foreach (var (mod, plan) in moves)
            {
                try
                {
                    report(L.F("{0} 다시 배치 중...", mod.Name));
                    manager.Install(plan);
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{mod.Name}: {ex.Message}");
                }
            }
            return errors;
        });
        await RefreshAll();
        if (failed == null) return;
        if (failed.Count > 0) DarkDialog.Show(this, L.T("다시 배치하지 못한 모드:\n") + string.Join("\n", failed), L.T("스크립트 로더"), isError: true);
        else SetStatus(L.F("설치된 모드 {0}개를 {1}에 맞춰 다시 배치했습니다.", moves.Count, RuntimeName(target)));
    }

    /// <summary>Installs, updates or downgrades to the version chosen for the selected mod.</summary>
    async Task InstallChosen()
    {
        if (SelectedMods() is { Count: > 1 } several)
        {
            await InstallMany(several);
            return;
        }
        if (SelectedMod() is not { } mod || ChosenVersion() is not { } version) return;
        await InstallVersion(mod, version);
    }

    List<ModGroup> SelectedMods() => zipList.SelectedItems.Cast<ListViewItem>().Select(i => (ModGroup)i.Tag!).ToList();

    List<InstalledMod> SelectedInstalledMods() => installedList.SelectedItems.Cast<ListViewItem>().Select(i => ((InstalledRow)i.Tag!).Mod).OfType<InstalledMod>().ToList();

    /// <summary>The version picked for a mod in its info panel, or else its newest.</summary>
    ModVersion ChosenVersionOf(ModGroup mod) =>
        chosenVersion.TryGetValue(mod.Key, out var path) && mod.Versions.FirstOrDefault(v => v.Path == path) is { } picked ? picked : mod.Latest;

    /// <summary>Picks the options of an installed mod again and swaps its files to match.</summary>
    async Task ChangeOptions(InstalledMod installedMod)
    {
        var mod = mods.FirstOrDefault(m => m.Key.Equals(installedMod.Key, StringComparison.OrdinalIgnoreCase));
        var version = mod?.Versions.FirstOrDefault(v => v.FileName.Equals(installedMod.ZipName, StringComparison.OrdinalIgnoreCase));
        if (mod == null || version == null || !File.Exists(version.Path))
        {
            DarkDialog.Show(this, L.F("설치한 압축 파일이 MOD 폴더에 없어 옵션을 바꿀 수 없습니다.\n{0}", installedMod.ZipName), Text);
            return;
        }
        await InstallVersion(mod, version, L.T("옵션 변경"));
    }

    /// <param name="action">The name of the step, when it is not the usual install, update or reinstall.</param>
    async Task InstallVersion(ModGroup mod, ModVersion version, string? action = null)
    {
        // A version laid out anew goes in best on its own: the old one is removed first, if the user agrees.
        if (action == null && mod.InstalledFile != null)
        {
            var change = await Run(L.T("압축 파일 확인 중..."), _ => Tuple.Create(manager.LayoutChange(version.Path)));
            if (change == null) return;
            if (change.Item1 is { } why && installed.FirstOrDefault(i => i.Key.Equals(mod.Key, StringComparison.OrdinalIgnoreCase)) is { } current)
            {
                var answer = DarkDialog.Show(this,
                    L.F("{0}의 이 버전은 이전 버전과 구조가 달라졌습니다.\n{1}\n\n이전 버전을 먼저 제거한 뒤 새로 설치하는 것을 권합니다. 지금 제거하고 설치할까요?\n(아니요: 이전 버전 위에 그대로 업데이트)", mod.Name, why),
                    L.T("구조가 바뀐 버전"), yesNo: true);
                if (answer == DialogResult.Yes)
                {
                    await RemoveMods(new[] { current });
                    if (installed.Any(i => i.Key.Equals(mod.Key, StringComparison.OrdinalIgnoreCase))) return;
                    // The lists were read again; carry on with the same file as a fresh install.
                    var fresh = mods.FirstOrDefault(m => m.Versions.Any(v => v.Path == version.Path));
                    if (fresh == null) return;
                    await InstallVersion(fresh, fresh.Versions.First(v => v.Path == version.Path));
                    return;
                }
            }
        }

        var groups = await Run(L.T("압축 파일 확인 중..."), _ => manager.OptionGroups(version.Path));
        if (groups == null) return;
        if (action != null && groups.Count == 0)
        {
            DarkDialog.Show(this, L.T("이 모드에는 고를 옵션이 없습니다."), Text);
            return;
        }
        List<string>? chosen = null;
        if (groups.Count > 0)
        {
            using var options = new OptionsDialog(mod.Name, groups, manager.PreviousOptions(version.Path));
            if (options.ShowDialog(this) != DialogResult.OK) return;
            chosen = options.Chosen;
        }

        action ??= InstallAction(mod, version);
        InstallPlan? plan = await Run(L.T("압축 파일 확인 중..."), p => manager.BuildPlan(version.Path, chosen, progress: p));
        if (plan == null) return;
        if (!plan.HasChanges && plan.Excluded.Count == 0)
        {
            DarkDialog.Show(this, L.T("게임 폴더의 파일과 모두 같아 설치할 내용이 없습니다."), Text);
            return;
        }

        // Every file the archive installs is a choice; those left out last time start unticked.
        var rows = plan.Stale.Select(s => s.Modified
                ? new PlanRow(L.T("수정된 옛 파일 제거 (백업)"), s.Path, Theme.Warning)
                : new PlanRow(L.T("옛 버전 파일 제거"), s.Path, Theme.Info))
            .Concat(plan.Items
                .Select(i => (i.Kind switch
                {
                    ChangeKind.Overwrite => OverwriteRow(mod, i),
                    ChangeKind.Keep => new PlanRow(L.T("기존 파일 유지"), i.Path, Theme.Info),
                    ChangeKind.Add => new PlanRow(plan.Relocated.Contains(i.Path) ? L.T("새로 추가 (모드 로더 경로로 옮김)") : L.T("새로 추가"), i.Path, null),
                    _ => new PlanRow(L.T("동일 (변경 없음)"), i.Path, Theme.Dim),
                }) with { Checked = true }))
            .Concat(plan.Excluded.Select(p => new PlanRow(L.T("설치 안 함 (지난번에 뺀 파일)"), p, Theme.Dim, Checked: false)))
            .ToList();

        var summary = L.F("새로 추가 {0}개 · 덮어쓰기 {1}개 · ", plan.Count(ChangeKind.Add), plan.Count(ChangeKind.Overwrite))
            + (plan.Stale.Count > 0 ? L.F("옛 파일 제거 {0}개 · ", plan.Stale.Count) : "")
            + L.F("기존 파일 유지 {0}개 · 동일 {1}개 · 새 폴더 {2}개", plan.Count(ChangeKind.Keep), plan.Count(ChangeKind.Same), plan.NewDirs.Count);
        if (plan.ReplacesZip != null)
            summary = $"{mod.Name}{Change(ModIdentity.ParseFileName(plan.ReplacesZip).Version, version.Version)}\n{summary}";
        if (plan.Options.Count > 0)
            summary += L.T("\n선택한 옵션: ") + string.Join(", ", plan.Options.SelectMany(g => g.Options).Where(o => plan.Chosen.Contains(o.Folder)).Select(o => o.Label));

        var warnings = plan.Warnings.ToList();
        var overwritten = plan.Items.Where(i => i.Kind == ChangeKind.Overwrite).Select(i => OwnerOf(i.Path)).ToList();
        int fromMods = overwritten.Count(o => o != null && !SameMod(o, mod));
        int fromOutside = overwritten.Count(o => o == null);
        int edited = plan.Items.Count(i => i.Kind == ChangeKind.Overwrite && WasEdited(mod, i)) + plan.Stale.Count(s => s.Modified);
        if (plan.ReplacesZip != null)
            warnings.Add(L.T("모드 설정처럼 설치 후 생긴 파일은 그대로 둡니다. 이전 버전으로 돌아가려면 모드 정보에서 버전을 골라 설치하면 됩니다."));
        if (edited > 0)
            warnings.Add(L.F("설치 후 수정된 파일 {0}개가 바뀝니다. 수정 내용은 백업되며, 업데이트를 취소하면 돌아옵니다.", edited));
        if (fromMods > 0)
            warnings.Add(L.F("설치된 다른 모드의 파일 {0}개를 덮어씁니다. 나중에 설치한 쪽이 적용되며, 이 모드를 제거하면 이전 내용으로 돌아옵니다.", fromMods));
        if (fromOutside > 0)
            warnings.Add(L.F("매니저로 설치하지 않은 파일 {0}개를 덮어씁니다. 게임 파일이거나 다른 도구의 파일일 수 있습니다. 원본은 백업됩니다.", fromOutside));

        List<string> unticked;
        using (var dialog = new PlanDialog(L.F("{0} 확인 - {1}", action, mod.Name), summary, warnings, rows, action))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            unticked = dialog.Unticked.ToList();
        }

        // A different choice of files is a different plan: built again with it, then installed.
        if (!unticked.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(plan.Excluded))
        {
            var current = plan;
            plan = await Run(L.T("압축 파일 확인 중..."), p => manager.BuildPlan(version.Path, current.Chosen, unticked, p));
            if (plan == null) return;
            if (!plan.HasChanges)
            {
                DarkDialog.Show(this, L.T("고른 파일로는 바뀌는 내용이 없습니다."), Text);
                return;
            }
        }

        var ready = plan;
        var tx = await Run(L.F("{0} 중...", action), p => manager.Install(ready, p));
        await RefreshAll();
        if (tx != null) SetStatus(L.F("{0}{1} {2} 완료", mod.Name, Paren(version.Version), action) + (ready.Excluded.Count > 0 ? L.F(" (뺀 파일 {0}개)", ready.Excluded.Count) : ""));
    }

    /// <summary>" (1.0)", or nothing when the version is not known: an empty "()" says nothing.</summary>
    static string Paren(string? version) => string.IsNullOrEmpty(version) ? "" : $" ({version})";

    /// <summary>" (1.0 → 2.0)", " (→ 2.0)" when the old one is not known, or nothing when neither is.</summary>
    static string Change(string? from, string? to) =>
        string.IsNullOrEmpty(to) ? Paren(from) : string.IsNullOrEmpty(from) ? $" (→ {to})" : $" ({from} → {to})";

    static string InstallAction(ModGroup mod, ModVersion version)
    {
        if (mod.InstalledFile == null) return L.T("설치");
        var compared = ModIdentity.CompareFiles(version.FileName, mod.InstalledFile);
        return compared > 0 ? L.T("업데이트") : compared < 0 ? L.T("다운그레이드") : L.T("재설치");
    }

    /// <summary>The newest install that wrote or shares this file, or null if the manager did not put it there.</summary>
    Tx? OwnerOf(string path) => history.LastOrDefault(t =>
        t.Files.Any(f => !f.Deleted && f.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
        || t.Unchanged.Any(u => u.Path.Equals(path, StringComparison.OrdinalIgnoreCase)));

    static bool SameMod(Tx tx, ModGroup mod) => ModIdentity.KeyOfTx(tx).Equals(mod.Key, StringComparison.OrdinalIgnoreCase);

    /// <summary>The file on disk differs from what this mod installed, so someone changed it since.</summary>
    bool WasEdited(ModGroup mod, PlanItem item)
    {
        if (OwnerOf(item.Path) is not { } owner || !SameMod(owner, mod)) return false;
        var written = owner.Files.FirstOrDefault(f => f.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase))?.InstalledHash
            ?? owner.Unchanged.First(u => u.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase)).Hash;
        return written != item.OldHash;
    }

    PlanRow OverwriteRow(ModGroup mod, PlanItem item)
    {
        if (OwnerOf(item.Path) is not { } owner) return new PlanRow(L.T("덮어쓰기 (원본 백업)"), item.Path, Theme.Warning);
        if (!SameMod(owner, mod))
            return new PlanRow(L.F("덮어쓰기 ({0} 모드와 충돌)", ModIdentity.ParseFileName(owner.ZipName).Name), item.Path, Theme.Warning);
        return WasEdited(mod, item)
            ? new PlanRow(L.T("수정된 파일 덮어쓰기 (백업)"), item.Path, Theme.Warning)
            : new PlanRow(L.T("새 버전으로 교체"), item.Path, null);
    }

    // ---------------------------------------------------------------- remove and undo

    string Describe(int txId)
    {
        var tx = history.First(t => t.Id == txId);
        var parsed = ModIdentity.ParseFileName(tx.ZipName);
        return parsed.Version.Length > 0 ? $"{parsed.Name} {parsed.Version}" : parsed.Name;
    }

    /// <summary>Removes one or more installed mods together, after one preview of every file.</summary>
    async Task RemoveMods(IReadOnlyList<InstalledMod> chosen)
    {
        var ids = chosen.SelectMany(m => m.Installs).Select(t => t.Id).ToList();
        var plan = await Run(L.T("제거 확인 중..."), _ => manager.BuildRemovePlan(ids));
        if (plan == null) return;

        var names = string.Join(", ", chosen.Select(m => m.Name));
        var headline = chosen.Count == 1
            ? L.F("{0} 모드를 제거합니다. 다른 모드는 그대로 두고, 모드 목록에는 남습니다.", names)
            : L.F("모드 {0}개를 제거합니다: {1}\n다른 모드는 그대로 두고, 모드 목록에는 남습니다.", chosen.Count, names);
        var warnings = new List<string>();
        if (plan.RemovesLoaderUnderOtherMods)
            warnings.Add(L.T("모드 로더를 제거하면 남아 있는 다른 모드는 게임에서 동작하지 않습니다."));
        var title = chosen.Count == 1 ? L.F("모드 제거 확인 - {0}", names) : L.F("모드 {0}개 제거 확인", chosen.Count);
        if (!ConfirmPlan(title, headline, plan, warnings, L.T("제거"))) return;

        var result = await Run(L.T("제거 중..."), p => new UndoResult(manager.RemoveMany(ids, p)));
        await RefreshAll();
        if (result != null) SetStatus(DoneMessage(chosen.Count == 1 ? L.F("{0} 제거 완료", names) : L.F("모드 {0}개 제거 완료", chosen.Count), result));
    }

    /// <summary>
    /// Installs or updates several mods in one go: one list to confirm, then each in turn. A mod with option
    /// sets that has never been installed is still asked about its options; others reuse their last choices.
    /// </summary>
    async Task InstallMany(IReadOnlyList<ModGroup> chosen)
    {
        var work = chosen
            .Select(m => (Mod: m, Version: ChosenVersionOf(m)))
            .Where(x => !x.Version.Missing && InstallAction(x.Mod, x.Version) != L.T("재설치"))
            .ToList();
        if (work.Count == 0)
        {
            DarkDialog.Show(this, L.T("고른 모드는 모두 이미 그 버전으로 설치되어 있습니다."), Text);
            return;
        }
        var list = string.Join("\n", work.Select(x => $"  {x.Mod.Name}: {InstallAction(x.Mod, x.Version)}{Change(x.Mod.InstalledVersion, x.Version.Version)}"));
        if (DarkDialog.Show(this, L.F("모드 {0}개를 설치할까요?\n{1}\n\n파일 하나하나 고르려면 모드를 하나만 골라 설치하세요.", work.Count, list), L.T("여러 모드 설치"), yesNo: true) != DialogResult.Yes) return;

        var errors = new List<string>();
        foreach (var (mod, version) in work)
        {
            List<string>? options = null;
            var groups = await Run(L.T("압축 파일 확인 중..."), _ => manager.OptionGroups(version.Path));
            if (groups == null) { errors.Add(L.F("{0}: 압축 파일을 읽지 못했습니다.", mod.Name)); continue; }
            if (groups.Count > 0 && manager.PreviousOptions(version.Path).Count == 0)
            {
                using var dialog = new OptionsDialog(mod.Name, groups, Array.Empty<string>());
                if (dialog.ShowDialog(this) != DialogResult.OK) { errors.Add(L.F("{0}: 옵션을 고르지 않아 건너뜀", mod.Name)); continue; }
                options = dialog.Chosen;
            }
            var done = await Run(L.F("{0} 설치 중...", mod.Name), p =>
            {
                try
                {
                    var plan = manager.BuildPlan(version.Path, options, progress: p);
                    if (plan.HasChanges) manager.Install(plan, p);
                    return "";
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    return ex.Message;
                }
            });
            if (done is { Length: > 0 }) errors.Add($"{mod.Name}: {done}");
        }
        await RefreshAll();
        if (errors.Count > 0) DarkDialog.Show(this, L.T("설치하지 못한 모드:\n") + string.Join("\n", errors), L.T("여러 모드 설치"), isError: true);
        else SetStatus(L.F("모드 {0}개 설치 완료", work.Count));
    }

    static string DoneMessage(string done, UndoResult result) =>
        result.Leftovers == null ? done : L.F("{0}, 보관: {1}", done, Path.GetFileName(result.Leftovers));

    bool ConfirmPlan(string title, string headline, RollbackPlan plan, List<string> warnings, string confirmText)
    {
        var rows = plan.ExtraFiles.Select(f => new PlanRow(L.T("설치 후 생긴 파일 → 보관"), f, Theme.Warning))
            .Concat(plan.Steps.Where(s => s.Kind != StepKind.None)
                .OrderBy(s => s.Kind switch { StepKind.StashRestore => 0, StepKind.StashDelete => 1, StepKind.Restore => 2, StepKind.KeepForLater => 3, _ => 4 })
                .ThenBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
                .Select(s => s.Kind switch
                {
                    StepKind.StashRestore => new PlanRow(L.T("변경됨 → 보관 후 원본 복원"), s.Path, Theme.Warning),
                    StepKind.StashDelete => new PlanRow(L.T("변경됨 → 보관 후 제거"), s.Path, Theme.Warning),
                    StepKind.Restore => new PlanRow(L.T("원본 복원"), s.Path, Theme.Info),
                    StepKind.KeepForLater => new PlanRow(L.T("이후 설치가 사용 → 유지"), s.Path, Theme.Dim),
                    _ => new PlanRow(L.T("제거"), s.Path, null),
                }))
            .ToList();

        var kept = plan.ExtraFiles.Count + plan.Steps.Count(s => s.Kind is StepKind.StashDelete or StepKind.StashRestore);
        var summary = headline + "\n"
            + L.F("원본 복원 {0}개 · ", plan.Steps.Count(s => s.Kind is StepKind.Restore or StepKind.StashRestore))
            + L.F("제거 {0}개 · ", plan.Steps.Count(s => s.Kind is StepKind.Delete or StepKind.StashDelete))
            + L.F("보관 폴더로 이동 {0}개 · 폴더 제거 {1}개", kept, plan.RemovedDirs.Count)
            + (plan.KeptDirs.Count > 0 ? L.F(" · 다른 설치가 쓰는 폴더 유지 {0}개", plan.KeptDirs.Count) : "");
        if (kept > 0)
            warnings.Add(L.T("설치 후 변경되었거나 새로 생긴 파일은 지우지 않고 보관 폴더로 옮깁니다. 모드 설정이나 직접 넣은 파일은 거기서 꺼낼 수 있습니다."));
        if (plan.Steps.Any(s => s.Kind == StepKind.StashRestore))
            warnings.Add(L.T("덮어썼던 파일이 설치 후 변경되었습니다. 게임 업데이트로 바뀐 파일이라면 원본 복원 뒤 Steam 파일 검사가 필요할 수 있습니다."));

        using var dialog = new PlanDialog(title, summary, warnings, rows, confirmText);
        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    async Task OfferRecovery()
    {
        var pending = manager.Pending();
        if (pending == null) return;
        var answer = DarkDialog.Show(this,
            L.F("{0} 작업이 중간에 중단되었습니다.\n지금 마저 정리할까요?\n\n정리하기 전에는 새 설치를 할 수 없습니다.", Describe(pending.Id)),
            Text, yesNo: true);
        if (answer != DialogResult.Yes) return;

        var result = await Run(L.T("정리 중..."), p => new UndoResult(manager.RemoveOne(pending.Id, p)));
        await RefreshAll();
        if (result != null) SetStatus(DoneMessage(L.T("정리 완료"), result));
    }

    async Task MoveSelected(int delta)
    {
        if (busy || SelectedRow() is not { } row) return;
        try
        {
            manager.MoveInstalled(row.Key, delta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(L.F("순서 저장 실패: {0}", ex.Message));
            return;
        }
        await RefreshAll();
    }

    string? selectAfterSeparator;

    /// <summary>
    /// Empties the keep folder, where removals set aside files the mod had not brought, such as its settings
    /// or files changed after install. They go to the Recycle Bin, so they can still be brought back.
    /// </summary>
    async Task ClearLeftovers()
    {
        if (busy) return;
        var (runs, files, bytes) = manager.LeftoversSize();
        if (files == 0)
        {
            DarkDialog.Show(this, L.T("보관 폴더가 비어 있습니다."), Text);
            return;
        }
        var question = L.F("보관 폴더의 파일 {0}개({1:0.#} MB, 제거 {2}번 분량)를 휴지통으로 보낼까요?\n\n", files, bytes / 1048576.0, runs)
            + L.T("모드를 제거할 때 지우지 않고 옮겨 둔 파일들입니다. 모드 설정 파일이나 설치 후 바뀐 파일일 수 있으니, 필요한 것이 있으면 먼저 [보관 폴더 열기]로 확인해 주세요.");
        if (DarkDialog.Show(this, question, L.T("보관 폴더 비우기"), yesNo: true) != DialogResult.Yes) return;
        var failed = await Run(L.T("보관 폴더 비우는 중..."), _ => manager.ClearLeftovers());
        if (failed == null) return;
        if (failed.Count > 0) DarkDialog.Show(this, L.T("휴지통으로 보내지 못한 것:\n") + string.Join("\n", failed), L.T("보관 폴더 비우기"), isError: true);
        else SetStatus(L.F("보관 폴더를 비웠습니다 ({0:0.#} MB, 휴지통에서 되살릴 수 있음).", bytes / 1048576.0));
    }

    /// <summary>Adds a named separator below the selected line, or at the top.</summary>
    async Task AddSeparator()
    {
        if (busy) return;
        var text = DarkPrompt.Show(this, L.T("구분선 추가"), L.T("구분선에 쓸 이름 (예: 그래픽, UI, 편의 기능)"), "");
        if (text == null) return;
        try
        {
            selectAfterSeparator = manager.AddSeparator(text.Length > 0 ? text : L.T("구분선"), SelectedRow()?.Key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(L.F("구분선 저장 실패: {0}", ex.Message));
            return;
        }
        await RefreshAll();
    }

    async Task RenameSeparator(InstalledRow row)
    {
        var text = DarkPrompt.Show(this, L.T("구분선 이름 바꾸기"), L.T("구분선 이름"), row.Text);
        if (text == null) return;
        try
        {
            manager.RenameSeparator(row.Key, text.Length > 0 ? text : L.T("구분선"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(L.F("구분선 저장 실패: {0}", ex.Message));
            return;
        }
        await RefreshAll();
    }

    async Task RemoveSeparator(InstalledRow row)
    {
        try
        {
            manager.RemoveSeparator(row.Key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(L.F("구분선 저장 실패: {0}", ex.Message));
            return;
        }
        await RefreshAll();
    }

    sealed record UndoResult(string? Leftovers);

    // ---------------------------------------------------------------- view

    async Task RefreshAll()
    {
        var data = await Run(L.T("불러오는 중..."), _ => manager.History(), lockWindow: false);
        if (data == null) return;
        history = data;
        if (manager.RegisterInstalled(meta)) SaveMeta();
        mods = manager.ListMods(meta);
        installedRows = manager.InstalledRows();
        installed = installedRows.Select(r => r.Mod).OfType<InstalledMod>().ToList();
        ShowRuntime();

        RenderFilter();
        RenderZips();

        // Mods are numbered; separators are headings between them.
        var selectedKey = selectAfterSeparator ?? SelectedRow()?.Key;
        selectAfterSeparator = null;
        int number = 0;
        installedList.SetRows(installedRows.Select(row => row.Mod is not { } mod
            ? new ListRow(new[] { "", $"── {row.Text} ──", "", "", "" }, row, Theme.Info)
            : new ListRow(new[]
            {
                (++number).ToString(),
                mod.Installs[^1].State != Tx.Installed ? L.F("{0} (중단됨)", mod.Name) : mod.Name,
                InstalledVersionText(mod),
                GroupOf(mod) is { } group ? MetaOf(group).Memo : "",
                mod.Installs[^1].Time.ToString("yyyy-MM-dd HH:mm"),
            }, row, mod.Installs[^1].State != Tx.Installed ? Theme.Danger : null)).ToList());
        installedList.KeepSelection(i => ((InstalledRow)i.Tag!).Key == selectedKey);
        if (installedList.Items.Count == 0) fileList.SetRows(Array.Empty<ListRow>());
        await ShowFilesOfSelected();
        UpdateButtons();
    }

    /// <summary>Lists the mods under the current filter, grouped by category in the user's order.</summary>
    void RenderZips()
    {
        var selectedKey = selectAfterRefresh != null
            ? mods.FirstOrDefault(m => m.Versions.Any(v => v.Path == selectAfterRefresh))?.Key
            : SelectedMod()?.Key;
        selectAfterRefresh = null;
        var filter = filterBox.SelectedItem as string ?? Manager.AllCategories;

        int Rank(string category)
        {
            var index = categories.FindIndex(c => c.Equals(category, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? index : int.MaxValue;
        }

        // Mods from one Nexus page stay together, the page's others indented under the first.
        var groups = mods
            .Select(m => (Mod: m, Category: CategoryLabel(MetaOf(m)), Page: LinkOf(m) is { } link ? $"{link.Game}/{link.ModId}" : "key:" + m.Key))
            .Where(x => filter == Manager.AllCategories || x.Category.Equals(filter, StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.Page, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(x => x.Mod.Name, StringComparer.OrdinalIgnoreCase).ToList())
            .OrderBy(g => Rank(g[0].Category))
            .ThenBy(g => g[0].Mod.Name, StringComparer.OrdinalIgnoreCase);

        var rows = groups.SelectMany(g => g.Select((x, index) =>
            {
                var status = x.Mod.InstalledFile == null ? L.T("미설치")
                    : x.Mod.UpdateAvailable ? L.T("업데이트 가능") + Change(x.Mod.InstalledVersion, x.Mod.Latest.Version)
                    : L.T("설치됨") + Paren(x.Mod.InstalledVersion);
                var newer = NewerOnNexus(x.Mod);
                if (newer != null) status = L.F("{0}에 {1} 있음 · {2}", SourceName(newer), newer.Version, status);
                var missing = x.Mod.Versions.All(v => v.Missing) ? L.T("파일 없음") : x.Mod.Versions.Any(v => v.Missing) ? L.T("일부 파일 없음") : null;
                if (missing != null) status = $"{missing} · {status}";
                var name = index > 0 ? "   └ " + x.Mod.Name : x.Mod.Name;
                var shownVersion = x.Mod.Latest.Version.Length > 0 ? x.Mod.Latest.Version : LocalVersionOf(x.Mod) is { Length: > 0 } known ? $"({known})" : "";
                return new ListRow(new[] { name, shownVersion, x.Category, ToolOf(x.Mod.Latest), status },
                    x.Mod, missing != null ? Theme.Danger : newer != null ? Theme.Warning : x.Mod.UpdateAvailable ? Theme.Info : null);
            })).ToList();

        zipList.SetRows(rows);
        zipList.KeepSelection(i => ((ModGroup)i.Tag!).Key == selectedKey);
        LoadNote();
        UpdateButtons();
    }

    ModGroup? GroupOf(InstalledMod mod) => mods.FirstOrDefault(m => m.Key.Equals(mod.Key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The installed version: from the file name, or as GitHub told it by the file's checksum, in brackets.</summary>
    string InstalledVersionText(InstalledMod mod)
    {
        if (mod.Version.Length > 0) return mod.Version;
        return GroupOf(mod) is { } group && group.Latest.FileName.Equals(mod.ZipName, StringComparison.OrdinalIgnoreCase)
            && LocalVersionOf(group) is { Length: > 0 } known ? $"({known})" : "";
    }

    async Task ShowFilesOfSelected()
    {
        int request = ++fileListRequest;
        if (SelectedInstalled() is not { } mod)
        {
            // A separator has no files.
            fileList.SetRows(Array.Empty<ListRow>());
            return;
        }
        List<FileStatus> statuses;
        try
        {
            statuses = await Task.Run(() => manager.GetModFileStatuses(mod.Installs));
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            return;
        }
        if (request != fileListRequest) return;

        // Files the user unticked in the current install are listed too, greyed, so the whole archive shows.
        var listed = statuses.Select(s => s.File.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var leftOut = mod.Installs[^1].Excluded.Where(p => !listed.Contains(p)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(p => new ListRow(new[] { p, L.T("제외"), L.T("설치 안 함") }, p, Theme.Dim));
        fileList.SetRows(statuses.Select(s => new ListRow(
            new[] { s.File.Path, s.File.Deleted ? L.T("제거") : s.File.Overwrote ? L.T("덮어씀") : L.T("추가"), s.State },
            s, s.State == L.T("변경됨") || s.State == L.T("없음") ? Theme.Danger : null)).Concat(leftOut).ToList());
    }

    void UpdateButtons()
    {
        var mod = SelectedMod();
        var version = ChosenVersion();
        var many = zipList.SelectedItems.Count;
        SetEnabled(installButton, many > 1 || version is { Missing: false });
        var label = many > 1 ? L.F("{0}개 설치...", many) : (mod != null && version != null ? InstallAction(mod, version) : L.T("설치")) + "...";
        if (installButton.Text != label) installButton.Text = label;
        SetEnabled(unlistButton, version != null && !IsInstalledFile(version.FileName));
        SetEnabled(deleteFileButton, version is { Missing: false } && !IsInstalledFile(version.FileName));
        SetEnabled(cleanVersionsButton, mod != null && OldVersions(mod).Count > 0);

        var row = SelectedRow();
        var selected = row?.Mod;
        var index = row == null ? -1 : installedRows.FindIndex(r => r.Key == row.Key);
        // On a separator the remove button removes the separator.
        var removeLabel = row is { IsSeparator: true } ? L.T("구분선 삭제") : L.T("선택한 모드 제거...");
        if (removeButton.Text != removeLabel) removeButton.Text = removeLabel;
        SetEnabled(removeButton, row != null);
        SetEnabled(optionsButton, selected?.Installs.Any(t => t.Options.Count > 0) == true);
        SetEnabled(moveUpButton, index > 0);
        SetEnabled(moveDownButton, index >= 0 && index < installedRows.Count - 1);
    }

    static void SetEnabled(Control control, bool enabled)
    {
        if (control.Enabled != enabled) control.Enabled = enabled;
    }

    void SetStatus(string message)
    {
        if (statusLabel.Text != message) statusLabel.Text = message;
    }

    /// <summary>Runs work off the UI thread. Returns default after showing the error if it fails.</summary>
    async Task<T?> Run<T>(string message, Func<Action<string>, T> work, bool lockWindow = true)
    {
        if (lockWindow && busy) return default;
        var progress = new Progress<string>(text =>
        {
            SetStatus(text);
            if (lockWindow) overlay.Report(text);
        });
        if (lockWindow)
        {
            busy = true;
            SetStatus(message);
            overlay.Show(message);
        }
        try
        {
            var result = await Task.Run(() => work(((IProgress<string>)progress).Report));
            if (lockWindow) SetStatus(L.T("준비"));
            return result;
        }
        catch (Exception ex)
        {
            // Input has to work again before the error box opens, or it could not be dismissed.
            if (lockWindow)
            {
                busy = false;
                overlay.Hide();
            }
            SetStatus(L.T("오류"));
            DarkDialog.Show(this, ex.Message, Text, isError: true);
            return default;
        }
        finally
        {
            if (lockWindow)
            {
                busy = false;
                overlay.Hide();
            }
        }
    }

    void OpenFolder(string path) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
}

/// <param name="Checked">For a file the user may leave out: whether it is ticked. Null for a step that is not a choice.</param>
public sealed record PlanRow(string Action, string Path, Color? Color, bool? Checked = null);

/// <summary>
/// Shows what an operation will do to each file, grouped by folder or by step, and asks for confirmation.
/// Files that are a choice carry a tick: clicking the first column or pressing Space switches it, and a
/// group's tick switches the whole group. Clicking elsewhere on a group folds it.
/// </summary>
public sealed class PlanDialog : Form
{
    const string On = "■", Off = "□", Some = "▣";
    static string RootFolder => L.T("(게임 폴더 맨 위)");

    readonly List<PlanRow> rows;
    readonly HashSet<string> unticked = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> folded = new(StringComparer.OrdinalIgnoreCase);
    readonly DarkListView list = new(2, ("", 30), (L.T("작업"), 230), (L.T("경로"), 560));
    readonly DarkComboBox groupBox = new(ComboBoxStyle.DropDownList) { Width = 120 };
    readonly Label countLabel = new() { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(12, 7, 0, 0) };
    readonly bool selectable;

    /// <summary>The files the user unticked, by path.</summary>
    public IReadOnlyCollection<string> Unticked => unticked;

    sealed record GroupTag(string Key);

    public PlanDialog(string title, string summary, IReadOnlyList<string> warnings, IReadOnlyList<PlanRow> rows, string confirmText)
    {
        this.rows = rows.ToList();
        selectable = rows.Any(r => r.Checked != null);
        foreach (var row in rows.Where(r => r.Checked == false)) unticked.Add(row.Path);

        Text = title;
        Theme.Apply(this);
        ClientSize = new Size(940, 620);
        MinimumSize = new Size(620, 380);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        var note = selectable ? L.T("\n체크를 푼 파일은 설치하지 않습니다. 이미 설치되어 있던 파일이면 제거됩니다. 이 선택은 다음 업데이트에도 이어집니다.") : "";
        var top = new Label { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(10, 10, 10, 8), MaximumSize = new Size(920, 0), Text = summary + note };
        var warn = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(10, 0, 10, 8),
            MaximumSize = new Size(920, 0),
            ForeColor = Theme.Warning,
            Text = string.Join("\n", warnings),
            Visible = warnings.Count > 0,
        };

        // Grouping, and for choices, ticking everything at once.
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(8, 0, 8, 6) };
        tools.Controls.Add(new Label { Text = L.T("묶어 보기"), AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(2, 7, 6, 0) });
        groupBox.Items.AddRange(new object[] { L.T("경로별"), L.T("작업별") });
        groupBox.SelectedIndex = 0;
        groupBox.SelectedIndexChanged += (_, _) => Render();
        tools.Controls.Add(groupBox);
        if (selectable)
        {
            var all = Theme.Button(L.T("전체 선택"));
            var none = Theme.Button(L.T("전체 해제"));
            all.Margin = new Padding(14, 0, 0, 0);
            none.Margin = new Padding(6, 0, 0, 0);
            all.Click += (_, _) => { unticked.Clear(); Render(); };
            none.Click += (_, _) => { foreach (var r in Choices()) unticked.Add(r.Path); Render(); };
            tools.Controls.Add(all);
            tools.Controls.Add(none);
            tools.Controls.Add(countLabel);
        }

        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Theme.Border };
        frame.Controls.Add(new ScrollHost(list) { Dock = DockStyle.Fill });
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 0) };
        body.Controls.Add(frame);

        var confirm = Theme.Button(confirmText, primary: true);
        confirm.DialogResult = DialogResult.OK;
        var cancel = Theme.Button(L.T("취소"));
        cancel.DialogResult = DialogResult.Cancel;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        bar.Controls.Add(cancel);
        bar.Controls.Add(confirm);
        AcceptButton = confirm;
        CancelButton = cancel;

        Controls.Add(body);
        Controls.Add(bar);
        Controls.Add(tools);
        Controls.Add(warn);
        Controls.Add(top);

        list.MouseClick += (_, e) =>
        {
            if (list.GetItemAt(e.X, e.Y) is not { } item) return;
            Toggle(item, onTick: e.X < list.Columns[0].Width);
        };
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Space || list.SelectedItems.Count == 0) return;
            Toggle(list.SelectedItems[0], onTick: true);
            e.Handled = true;
        };
        Render();
    }

    IEnumerable<PlanRow> Choices() => rows.Where(r => r.Checked != null);

    bool ByFolder => groupBox.SelectedIndex == 0;

    static string FolderOf(string path) => path.LastIndexOf('/') is var slash and > 0 ? path[..slash] : RootFolder;

    string KeyOf(PlanRow row) => ByFolder ? FolderOf(row.Path) : row.Action;

    void Toggle(ListViewItem item, bool onTick)
    {
        if (item.Tag is GroupTag group)
        {
            var members = Choices().Where(r => KeyOf(r).Equals(group.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (onTick && members.Count > 0)
            {
                bool allOn = members.All(r => !unticked.Contains(r.Path));
                foreach (var r in members)
                    if (allOn) unticked.Add(r.Path); else unticked.Remove(r.Path);
            }
            else if (!folded.Remove(group.Key)) folded.Add(group.Key);
        }
        else if (item.Tag is PlanRow { Checked: not null } row && onTick)
        {
            if (!unticked.Remove(row.Path)) unticked.Add(row.Path);
        }
        else return;
        Render();
    }

    string Tick(PlanRow row) => row.Checked == null ? "" : unticked.Contains(row.Path) ? Off : On;

    /// <summary>Draws the list again, keeping the scroll place and the selected line.</summary>
    void Render()
    {
        var top = list.TopItem?.Index ?? 0;
        var selected = list.SelectedIndices.Count > 0 ? list.SelectedIndices[0] : -1;
        list.BeginUpdate();
        list.Items.Clear();
        var groups = rows.GroupBy(KeyOf, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => ByFolder ? (g.Key == RootFolder ? "" : g.Key) : g.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var members = group.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList();
            var choices = members.Where(r => r.Checked != null).ToList();
            var on = choices.Count(r => !unticked.Contains(r.Path));
            var tick = choices.Count == 0 ? "" : on == choices.Count ? On : on == 0 ? Off : Some;
            var isFolded = folded.Contains(group.Key);
            var header = new ListViewItem(new[] { tick, $"{(isFolded ? "▶" : "▼")} {group.Key}", choices.Count > 0 ? L.F("{0} / {1}개", on, members.Count) : L.F("{0}개", members.Count) })
            {
                Tag = new GroupTag(group.Key),
                ForeColor = Theme.Info,
            };
            list.Items.Add(header);
            if (isFolded) continue;
            foreach (var row in members)
            {
                var name = ByFolder ? row.Path[(row.Path.LastIndexOf('/') + 1)..] : row.Path;
                var item = new ListViewItem(new[] { Tick(row), ByFolder ? "    " + row.Action : "    " + row.Action, ByFolder ? "    " + name : row.Path }) { Tag = row };
                if (row.Checked != null && unticked.Contains(row.Path)) item.ForeColor = Theme.Dim;
                else if (row.Color is { } color) item.ForeColor = color;
                list.Items.Add(item);
            }
        }
        list.EndUpdate();
        if (list.Items.Count > 0) list.TopItem = list.Items[Math.Min(top, list.Items.Count - 1)];
        if (selected >= 0 && selected < list.Items.Count) list.Items[selected].Selected = true;
        if (selectable)
        {
            var all = Choices().Count();
            countLabel.Text = L.F("설치할 파일 {0} / {1}개", all - unticked.Count(p => Choices().Any(r => r.Path.Equals(p, StringComparison.OrdinalIgnoreCase))), all);
        }
    }
}
