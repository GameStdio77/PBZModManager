using System.Diagnostics;

namespace PBZModManager;

/// <summary>The right-click menus of the two lists: what the buttons do, and what only fits one mod.</summary>
public sealed partial class MainForm
{
    readonly ContextMenuStrip modMenu = NewMenu();
    readonly ContextMenuStrip installedMenu = NewMenu();

    static ContextMenuStrip NewMenu() =>
        new() { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, BackColor = Theme.Surface, ForeColor = Theme.Text };

    /// <summary>The user keeps the mod out of version checks.</summary>
    bool IgnoresUpdates(ModGroup mod) => mod.Versions.Any(v => meta.GetValueOrDefault(v.FileName)?.NoUpdateCheck == true);

    void BuildMenus()
    {
        zipList.ContextMenuStrip = modMenu;
        installedList.ContextMenuStrip = installedMenu;
        // A right click on a line that is not picked picks it alone, as Explorer does.
        foreach (var list in new ListView[] { zipList, installedList })
            list.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Right || list.HitTest(e.Location).Item is not { } item || item.Selected) return;
                foreach (ListViewItem other in list.SelectedItems) other.Selected = false;
                item.Selected = true;
                item.Focused = true;
            };
        modMenu.Opening += (_, e) => e.Cancel = !Fill(modMenu, ModMenuItems());
        installedMenu.Opening += (_, e) => e.Cancel = !Fill(installedMenu, InstalledMenuItems());
    }

    static bool Fill(ContextMenuStrip menu, List<ToolStripItem?> items)
    {
        menu.Items.Clear();
        // A separator only between two groups of items, never at either end or doubled.
        foreach (var item in items)
        {
            if (item == null)
            {
                if (menu.Items.Count > 0 && menu.Items[^1] is not ToolStripSeparator) menu.Items.Add(new ToolStripSeparator());
                continue;
            }
            menu.Items.Add(item);
        }
        while (menu.Items.Count > 0 && menu.Items[^1] is ToolStripSeparator) menu.Items.RemoveAt(menu.Items.Count - 1);
        return menu.Items.Count > 0;
    }

    static ToolStripMenuItem Item(string text, bool enabled, Func<Task> act)
    {
        var item = new ToolStripMenuItem(DarkButton.FormatText(text)) { Enabled = enabled };
        // After the menu has closed, so a dialog the action opens comes up over the window.
        item.Click += async (_, _) => { await Task.Yield(); await act(); };
        return item;
    }

    static ToolStripMenuItem Item(string text, bool enabled, Action act) => Item(text, enabled, () => { act(); return Task.CompletedTask; });

    /// <summary>A button of the window as a menu item, with its own label and whether it can be used now.</summary>
    static ToolStripMenuItem Press(Button button) => Item(button.Text, button.Enabled, () => ((DarkButton)button).Press());

    List<ToolStripItem?> ModMenuItems()
    {
        UpdateButtons();
        var items = new List<ToolStripItem?>();
        var chosen = SelectedMods();
        if (chosen.Count == 0)
        {
            if (SelectedWanted() is { } place)
            {
                items.Add(Item(L.T("다운로드 페이지 열기"), true, () => OpenWantedPage(place)));
                items.Add(null);
                items.Add(Press(unlistButton));
            }
            else if (SelectedHeading() is { } heading && FilesOf(heading) is { Count: > 0 } files)
            {
                items.Add(Item(L.F("이 페이지 파일 {0}개 최신 버전 확인", files.Count), files.Any(f => !IgnoresUpdates(f)), () => CheckLatest(files)));
                items.Add(Item(L.T("모드 페이지 열기"), PageAddressOf(files[0]) != null, () => OpenAddress(PageAddressOf(files[0])!)));
            }
            return items;
        }

        items.Add(Press(installButton));
        items.AddRange(UpdateCheckItems(chosen));
        if (chosen.Count == 1)
        {
            var mod = chosen[0];
            var shown = ChosenVersion() ?? mod.Latest;
            items.Add(null);
            items.Add(Item(L.T("다운로드 페이지 열기"), LinkOf(mod) != null || GitHubOf(mod) != null, () => OpenDownloadPage(mod)));
            items.Add(Item(L.T("모드 페이지 열기"), PageAddressOf(mod) != null, () => OpenAddress(PageAddressOf(mod)!)));
            if (!shown.Missing && Manager.DocumentsIn(shown.Path).Count > 0)
                items.Add(Item(L.T("설명서 보기..."), true, () => DocumentWindow.Show(this, mod.Shown, shown.Path, openInBrowser)));
            if (!shown.Missing && Manager.PicturesIn(shown.Path).Count > 0)
                items.Add(Item(L.T("이미지 보기..."), true, () => PictureWindow.Show(this, mod.Shown, shown.Path)));
            items.Add(null);
            items.Add(Item(L.T("압축 파일 위치 열기"), !shown.Missing, () => ShowInExplorer(shown.Path)));
            items.Add(Item(L.T("설치된 모드에서 찾기"), mod.InstalledFile != null, () => FindInstalled(mod.Key)));
            if (OldVersions(mod).Count > 0)
            {
                items.Add(null);
                items.Add(Item(L.T("이 모드의 이전 버전 정리..."), true, () => OpenBatchCleanVersionsDialog(mod)));
            }
        }
        items.Add(null);
        items.Add(Press(unlistButton));
        items.Add(Press(deleteFileButton));
        return items;
    }

    List<ToolStripItem?> InstalledMenuItems()
    {
        UpdateButtons();
        var items = new List<ToolStripItem?>();
        var row = SelectedRow();
        if (row == null) return items;
        if (row.IsSeparator)
        {
            items.Add(Item(L.T("구분선 이름 바꾸기..."), true, () => RenameSeparator(row)));
            items.Add(Press(removeButton));
            items.Add(null);
            items.Add(Press(moveUpButton));
            items.Add(Press(moveDownButton));
            return items;
        }

        items.Add(Press(removeButton));
        items.Add(Press(optionsButton));
        items.Add(null);
        items.Add(Press(moveUpButton));
        items.Add(Press(moveDownButton));
        items.Add(Press(separatorButton));
        var groups = SelectedInstalledMods().Select(GroupOf).OfType<ModGroup>().ToList();
        items.Add(null);
        items.AddRange(UpdateCheckItems(groups));
        if (row.Mod is { } installed && SelectedInstalledMods().Count == 1)
        {
            items.Add(null);
            items.Add(Item(L.T("모드 목록에서 찾기"), GroupOf(installed) != null, () => FindInList(installed.Key)));
        }
        return items;
    }

    /// <summary>Checking the chosen mods for a newer version now, and keeping them out of checks or letting them back in.</summary>
    List<ToolStripItem?> UpdateCheckItems(List<ModGroup> chosen)
    {
        var items = new List<ToolStripItem?>();
        if (chosen.Count == 0) return items;
        var checkable = chosen.Where(m => !IgnoresUpdates(m)).ToList();
        items.Add(Item(chosen.Count == 1 ? L.T("이 모드만 최신 버전 확인") : L.F("고른 모드 {0}개 최신 버전 확인", checkable.Count),
            checkable.Count > 0, () => CheckLatest(checkable)));
        var allIgnored = chosen.All(IgnoresUpdates);
        items.Add(Item(allIgnored ? L.T("업데이트 확인 다시 하기") : L.T("업데이트 확인 안 함"), true, () => SetIgnoreUpdates(chosen, !allIgnored)));
        return items;
    }

    void SetIgnoreUpdates(IEnumerable<ModGroup> chosen, bool ignore)
    {
        foreach (var version in chosen.SelectMany(m => m.Versions)) NoteOf(version.FileName).NoUpdateCheck = ignore;
        SaveMeta();
        RenderZips();
        SetStatus(ignore ? L.T("최신 버전 확인에서 뺐습니다.") : L.T("최신 버전 확인에 다시 넣었습니다."));
    }

    /// <summary>The mod's page: the address the user gave, or else the Nexus page its file names tell.</summary>
    string? PageAddressOf(ModGroup mod) => WebAddress(MetaOf(mod).Url) ?? LinkOf(mod)?.PageUrl;

    static string? WebAddress(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? uri.ToString() : null;

    void OpenAddress(string text)
    {
        if (WebAddress(text) is not { } url) return;
        try
        {
            openInBrowser(url);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus(L.F("브라우저를 열지 못했습니다: {0}", ex.Message));
        }
    }

    /// <summary>Opens the folder with the file picked in it. Checks replace it so no window opens.</summary>
    public Action<string> ShowInExplorer { get; set; } = path =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();

    void FindInstalled(string key)
    {
        foreach (ListViewItem item in installedList.Items)
            item.Selected = item.Tag is InstalledRow { Mod: { } mod } && mod.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
        if (installedList.SelectedItems.Count == 0) return;
        installedList.SelectedItems[0].EnsureVisible();
        installedList.Focus();
    }

    void FindInList(string key)
    {
        // A category filter may hide it: the whole list shows it.
        if (!zipList.Items.Cast<ListViewItem>().Any(i => RowKey(i.Tag) == key) && filterBox.Items.Count > 0 && filterBox.SelectedIndex != 0)
        {
            filterBox.SelectedIndex = 0;
            RenderZips();
        }
        foreach (ListViewItem item in zipList.Items) item.Selected = RowKey(item.Tag) == key;
        if (zipList.SelectedItems.Count == 0) return;
        zipList.SelectedItems[0].EnsureVisible();
        zipList.Focus();
    }
}
