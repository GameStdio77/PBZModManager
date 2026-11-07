// Checks the real window on a hidden desktop: it cannot take focus, appear on screen, or touch the
// mouse, so the PC stays usable while it runs. Keep this to whatever feature is being worked on.
// Nothing is installed to a real game folder; the work dir holds a throwaway one.
// Usage: UiChecks <empty work dir> [ko|en|zh|ja]   (another language only draws pictures of the windows)
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using PBZModManager;

static class UiChecks
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, int flags, uint access, IntPtr attributes);

    const uint DesktopAllAccess = 0x000F01FF;
    static int failures;

    static void Check(string name, bool ok)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
        if (!ok) failures++;
    }

    static IEnumerable<Control> All(Control root) =>
        root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(All(c)));

    static Button Find(Form form, string text) => All(form).OfType<Button>().Single(b => b.Text == text);

    static bool Shows(Form form, string text) => All(form).OfType<Label>().Any(l => l.Text.Contains(text));

    /// <summary>Waits for an open window of a type, other than the main one, that passes the test.</summary>
    static async Task<T> Window<T>(Form main, Func<T, bool>? test = null) where T : Form
    {
        for (int i = 0; i < 100; i++)
        {
            var found = Application.OpenForms.OfType<T>().FirstOrDefault(f => f != main && (test == null || test(f)));
            if (found != null)
            {
                await Task.Delay(150);
                return found;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException("window did not open: " + typeof(T).Name);
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 1 && args[1] == "--on-hidden-desktop")
        {
            using var log = new StreamWriter(Path.Combine(args[0], "results.txt")) { AutoFlush = true };
            Console.SetOut(log);
            return args.Length > 2 && args[2] != "ko" ? Pictures(args[0], args[2]) : Run(args[0]);
        }
        return StartOnHiddenDesktop(args[0], args.Length > 1 ? args[1] : "ko");
    }

    /// <summary>Runs this program again with a desktop of its own, which nothing on screen can see or be affected by.</summary>
    static int StartOnHiddenDesktop(string workDir, string language)
    {
        const string desktopName = "PBZModManagerChecks";
        Directory.CreateDirectory(workDir);
        var desktop = CreateDesktop(desktopName, IntPtr.Zero, IntPtr.Zero, 0, DesktopAllAccess, IntPtr.Zero);
        if (desktop == IntPtr.Zero)
        {
            Console.WriteLine($"FAIL  hidden desktop unavailable ({Marshal.GetLastWin32Error()}); nothing was shown");
            return 2;
        }

        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Desktop = desktopName };
        var commandLine = new StringBuilder($"\"{Environment.ProcessPath}\" \"{workDir}\" --on-hidden-desktop {language}");
        const uint CreateNoWindow = 0x08000000;
        if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, false, CreateNoWindow, IntPtr.Zero, null, ref startup, out var info))
        {
            Console.WriteLine($"FAIL  could not start on the hidden desktop ({Marshal.GetLastWin32Error()})");
            return 2;
        }

        using var process = System.Diagnostics.Process.GetProcessById(info.ProcessId);
        var finished = process.WaitForExit(180000);
        if (!finished) process.Kill();
        var path = Path.Combine(workDir, "results.txt");
        var results = File.Exists(path) ? File.ReadAllText(path) : "";
        Console.Write(results);
        if (!finished) Console.WriteLine("FAIL  timed out");
        return finished && results.Contains("ALL PASSED") ? 0 : 1;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, Reserved2;
        public IntPtr Reserved3, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateProcessW")]
    static extern bool CreateProcess(string? application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint flags, IntPtr environment, string? directory, ref StartupInfo startup, out ProcessInformation info);

    sealed class StandIn : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(Send(request, token));

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken token)
        {
            var url = request.RequestUri!.ToString();
            var body = request.Content?.ReadAsStringAsync(token).GetAwaiter().GetResult() ?? "";
            lock (Bodies) Bodies.Add(body);
            var json = url.Contains("GameStdio77/PBZModManager")
                ? """[{"tag_name":"v9.1.0","prerelease":false,"draft":false,"html_url":"https://github.com/GameStdio77/PBZModManager/releases/tag/v9.1.0","assets":[]}]"""
                : url.Contains("api.github.com")
                ? """[{"tag_name":"2.7.1","prerelease":false,"draft":false,"html_url":"https://github.com/XTGMods/UUEPBS/releases/tag/2.7.1","assets":[{"digest":"sha256:LOCALHASH"}]}]"""
                : body.Contains("game(domainName") ? """{"data":{"game":{"id":"10614"}}}"""
                : body.Contains("modFiles") && Check.Length > 0 ? Check
                : body.Contains("description") ? """{"data":{"legacyModsByDomain":{"nodes":[{"name":"Stand-in Mod","modCategory":{"name":"Visuals"},"description":"STEPS"}]}}}""".Replace("STEPS", Steps)
                : """{"data":{"legacyModsByDomain":{"nodes":[]}}}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.Replace("LOCALHASH", LocalHash), Encoding.UTF8, "application/json") };
        }

        public string LocalHash = "";
        /// <summary>What was asked, in order.</summary>
        public readonly List<string> Bodies = new();
        /// <summary>The answer to a version check, when one is set.</summary>
        public string Check = "";
        public string Steps = "Installation:<br />1. Step one<br />Uninstall:<br />x";
    }

    static void Zip(string path, params (string Entry, string Text)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, text) in entries)
            using (var stream = zip.CreateEntry(entry).Open())
                stream.Write(Encoding.UTF8.GetBytes(text));
    }

    static Manager MakeGame(string workDir, StandIn standIn)
    {
        Manager.IgnoreGameForTests = Manager.GameRunning();
        Manager.DeleteInsteadOfRecycleForTests = true;
        NexusApi.UseHandlerForChecks(standIn);
        var root = Path.Combine(workDir, "game");
        Directory.CreateDirectory(Path.Combine(root, "ProjectPBZ"));
        Directory.CreateDirectory(Path.Combine(root, "platform", "steam"));
        // A stand-in game program: the launch button is checked, nothing is started.
        File.WriteAllText(Path.Combine(root, "platform", "steam", "PBZ.exe"), "");
        var manager = new Manager(root);
        const string old = "Old 1.0 501 1.0 2026-01-01T00-00Z a.zip";
        Zip(Path.Combine(manager.ModDir, old), ("ProjectPBZ/Content/Paks/~mods/Old_P.pak", "o"));
        manager.Install(manager.BuildPlan(Path.Combine(manager.ModDir, old)));
        return manager;
    }

    static void Click(Control control) =>
        typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(control, new object[] { EventArgs.Empty });

    static int Run(string workDir)
    {
        var standIn = new StandIn();
        var manager = MakeGame(workDir, standIn);
        Manager.DeleteInsteadOfRecycleForTests = true;
        standIn.Check = """
            {"data": {"legacyModsByDomain": {"nodes": [{"modId": 385, "name": "Female Animation", "version": "7", "status": "published", "modCategory": {"name": "Animations"}}, {"modId": 187, "name": "Map Tracker Plus - Minimap and Collectibles (UE4SS)", "version": "1.0.4", "status": "published", "modCategory": {"name": "Gameplay"}}, {"modId": 300, "name": "YoRHa 2B Replace Soul_(Outfit Options)_v2.0", "version": "2.1.1", "status": "published", "modCategory": {"name": "Characters"}}]}, "m385": [{"fileId": 1048, "name": "PBZ FemaleAnimation", "version": "1", "category": "OLD_VERSION"}, {"fileId": 1322, "name": "PBZ FemaleAnimation OtherBodies", "version": "1", "category": "OLD_VERSION"}, {"fileId": 1323, "name": "PBZ FemaleAnimation for 2B", "version": "2", "category": "OLD_VERSION"}, {"fileId": 1342, "name": "PBZ FemaleAnimation for 2B", "version": "3", "category": "OLD_VERSION"}, {"fileId": 1343, "name": "PBZ FemaleAnimation OtherBodies", "version": "3", "category": "OLD_VERSION"}, {"fileId": 1509, "name": "PBZ FemaleAnimation OtherBodies", "version": "4", "category": "OLD_VERSION"}, {"fileId": 1510, "name": "PBZ FemaleAnimation Original Idle", "version": "1", "category": "OLD_VERSION"}, {"fileId": 2304, "name": "PBZ FemaleAnimation Original Idle", "version": "6", "category": "OLD_VERSION"}, {"fileId": 2305, "name": "PBZ FemaleAnimation OtherBodies", "version": "6", "category": "OLD_VERSION"}, {"fileId": 2306, "name": "PBZ FemaleAnimation for 2B(my personal edit)", "version": "6", "category": "OLD_VERSION"}, {"fileId": 2307, "name": "PBZ FemaleAnimation For SoulVerR", "version": "5", "category": "OLD_VERSION"}, {"fileId": 2333, "name": "PBZ FemaleAnimation For Ver.R", "version": "6", "category": "OLD_VERSION"}, {"fileId": 2350, "name": "PBZ FemaleAnimation For 2B(Original)", "version": "6", "category": "OLD_VERSION"}, {"fileId": 2354, "name": "PBZ FemaleAnimation For 2B(Original)", "version": "6.1", "category": "OLD_VERSION"}, {"fileId": 2359, "name": "PBZ FemaleAnimation For 2B(Original)", "version": "7", "category": "MAIN"}, {"fileId": 2360, "name": "PBZ FemaleAnimation For Ver.R", "version": "7", "category": "MAIN"}, {"fileId": 2361, "name": "PBZ FemaleAnimation for 2B(my personal edit)", "version": "7", "category": "OPTIONAL"}, {"fileId": 2362, "name": "PBZ FemaleAnimation OtherBodies", "version": "7", "category": "MAIN"}, {"fileId": 2363, "name": "PBZ FemaleAnimation Original Idle", "version": "7", "category": "MAIN"}], "m187": [{"fileId": 1, "name": "Map Collectible Tracker 1.0.4", "version": "1.0.4", "category": "MAIN"}], "m300": [{"fileId": 2, "name": "YoRHa 2B Replace Soul_(Outfit Options)_v2.1.1", "version": "2.1.1", "category": "MAIN"}]}}
            """;
        const string tracker = "Map Collectible Tracker 1.0.4 187 1.0.4 2026-11-07T00-32Z s0mFcMSWX.zip";
        const string yorha = "YoRHa 2B Replace Soul_(Outfit Options)_v2.1 300 2.1 2026-11-06T10-08Z 5qSwPXyZZ.zip";
        const string twoB = "PBZ FemaleAnimation For 2B(Original) 385 7 2026-11-08T00-00Z abcdEFGH.zip";
        const string verR = "PBZ FemaleAnimation For Ver.R 385 7 2026-11-07T04-43Z Ihsdbc5En.zip";
        foreach (var (name, pak) in new[] { (tracker, "Tracker_P.pak"), (yorha, "YoRHa_P.pak"), (twoB, "TwoB_P.pak"), (verR, "VerR_P.pak") })
        {
            Zip(Path.Combine(manager.ModDir, name), ("ProjectPBZ/Content/Paks/~mods/" + pak, "x"), ("README.txt", "read me"));
            manager.Install(manager.BuildPlan(Path.Combine(manager.ModDir, name)));
        }
        manager.AddSeparator("Characters", null);
        var opened = new List<string>();
        var shown = new List<string>();
        var form = new MainForm(manager, Array.Empty<string>(), opened.Add)
        {
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            ClientSize = new Size(1500, 860),
            ShowInExplorer = shown.Add,
        };
        Application.ThreadException += (_, e) =>
        {
            Check("no unhandled error: " + e.Exception.Message, false);
            form.Close();
        };
        form.Shown += async (_, _) =>
        {
            try
            {
                await Task.Delay(1000);
                var lists = All(form).OfType<DarkListView>().ToList();
                var zipList = lists[0];
                var installedList = lists[1];
                ListViewItem? Row(ListView list, int column, string name) => list.Items.Cast<ListViewItem>().FirstOrDefault(i => i.SubItems[column].Text.Trim() == name);
                async Task Pick(ListView list, int column, params string[] names)
                {
                    foreach (ListViewItem i in list.Items) i.Selected = names.Contains(i.SubItems[column].Text.Trim());
                    await Task.Delay(400);
                }
                async Task<List<ToolStripItem>> Menu(ListView list)
                {
                    list.ContextMenuStrip!.Show(list, new Point(60, 60));
                    await Task.Delay(300);
                    var items = list.ContextMenuStrip.Items.Cast<ToolStripItem>().ToList();
                    list.ContextMenuStrip.Close();
                    await Task.Delay(100);
                    return items;
                }
                string Texts(List<ToolStripItem> items) => string.Join(" | ", items.Select(i => i is ToolStripSeparator ? "—" : (i.Enabled ? "" : "(x)") + i.Text));
                async Task<List<string>> Choose(List<ToolStripItem> items, string text, Func<string, DialogResult>? answer = null)
                {
                    items.Single(i => i.Text == text).PerformClick();
                    var said = new List<string>();
                    for (int n = 0; n < 30; n++)
                    {
                        await Task.Delay(100);
                        var dialog = Application.OpenForms.OfType<Form>().FirstOrDefault(f => f != form && f.Visible && f.Modal);
                        if (dialog == null) { if (n > 15) break; continue; }
                        await Task.Delay(200);
                        var words = string.Join("\n", All(dialog).OfType<Label>().Select(l => l.Text).Concat(All(dialog).OfType<TextBox>().Select(b => b.Text)));
                        said.Add(dialog.Text + ": " + words);
                        dialog.DialogResult = answer?.Invoke(words) ?? DialogResult.No;
                        n = 0;
                    }
                    await Task.Delay(500);
                    return said;
                }

                // A right click on a line not picked picks it alone.
                await Pick(zipList, 0, "Map Collectible Tracker");
                var yorhaRow = Row(zipList, 0, "YoRHa 2B Replace Soul_(Outfit Options)")!;
                typeof(Control).GetMethod("OnMouseDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(zipList, new object[] { new MouseEventArgs(MouseButtons.Right, 1, yorhaRow.Bounds.X + 20, yorhaRow.Bounds.Y + 4, 0) });
                await Task.Delay(300);
                Check("a right click picks the line under it, alone", zipList.SelectedItems.Count == 1 && zipList.SelectedItems[0] == yorhaRow);

                // One mod's menu.
                await Pick(zipList, 0, "Map Collectible Tracker");
                var items = await Menu(zipList);
                var texts = Texts(items);
                Check("mod menu: " + texts, texts == "재설치... | 이 모드만 최신 버전 확인 | 업데이트 확인 안 함 | — | 다운로드 페이지 열기 | 모드 페이지 열기 | 설명서 보기... | — | 압축 파일 위치 열기 | 설치된 모드에서 찾기 | — | (x)목록에서 빼기 | (x)파일 삭제... | (x)이전 버전 정리...");

                // Checking it alone asks Nexus about it alone.
                standIn.Bodies.Clear();
                var said = await Choose(items, "이 모드만 최신 버전 확인");
                var asked = standIn.Bodies.FirstOrDefault(b => b.Contains("modFiles")) ?? "";
                Check("one mod checked alone: " + string.Join(" | ", said), asked.Contains("m187:") && !asked.Contains("m300:") && !asked.Contains("m385:") && said.Count == 1 && said[0].Contains("모두 최신"));

                items = await Menu(zipList);
                await Choose(items, "압축 파일 위치 열기");
                Check("archive shown in its folder: " + shown.LastOrDefault(), shown.LastOrDefault()?.EndsWith(tracker) == true);
                items = await Menu(zipList);
                await Choose(items, "설치된 모드에서 찾기");
                Check("found among installed mods", installedList.SelectedItems.Count == 1 && installedList.SelectedItems[0].Tag is InstalledRow { Mod: { } m } && m.ZipName == tracker);

                // Kept out of checks: marked, left out of the full check, and let back in.
                await Pick(zipList, 0, "YoRHa 2B Replace Soul_(Outfit Options)");
                items = await Menu(zipList);
                await Choose(items, "업데이트 확인 안 함");
                var yorhaStatus = Row(zipList, 0, "YoRHa 2B Replace Soul_(Outfit Options)")?.SubItems[4].Text ?? "";
                Check("marked: " + yorhaStatus, yorhaStatus.EndsWith("업데이트 확인 안 함") && manager.LoadMeta()[yorha].NoUpdateCheck);
                items = await Menu(zipList);
                Check("kept out: its own check is off, and the item lets it back in: " + Texts(items), items.Any(i => i.Text == "업데이트 확인 다시 하기") && !items.Single(i => i.Text == "이 모드만 최신 버전 확인").Enabled);
                standIn.Bodies.Clear();
                Click(Find(form, "최신 버전 확인"));
                for (int n = 0; n < 40; n++)
                {
                    await Task.Delay(100);
                    var dialog = Application.OpenForms.OfType<Form>().FirstOrDefault(f => f != form && f.Visible && f.Modal);
                    if (dialog != null) { await Task.Delay(200); dialog.DialogResult = DialogResult.No; break; }
                }
                await Task.Delay(800);
                asked = standIn.Bodies.FirstOrDefault(b => b.Contains("modFiles")) ?? "";
                yorhaStatus = Row(zipList, 0, "YoRHa 2B Replace Soul_(Outfit Options)")?.SubItems[4].Text ?? "";
                Check("the full check leaves it out, and offers no update for it: " + yorhaStatus, asked.Contains("m187:") && asked.Contains("m385:") && !asked.Contains("m300:") && !yorhaStatus.Contains("Nexus에"));
                await Pick(zipList, 0, "YoRHa 2B Replace Soul_(Outfit Options)");
                items = await Menu(zipList);
                await Choose(items, "업데이트 확인 다시 하기");
                Check("let back in", !manager.LoadMeta()[yorha].NoUpdateCheck && !(Row(zipList, 0, "YoRHa 2B Replace Soul_(Outfit Options)")?.SubItems[4].Text ?? "").Contains("확인 안 함"));

                // Several mods, and a page's heading.
                await Pick(zipList, 0, "Map Tracker Plus - Minimap and Collectibles (UE4SS)", "YoRHa 2B Replace Soul_(Outfit Options)");
                items = await Menu(zipList);
                Check("several mods: " + Texts(items), Texts(items).StartsWith("2개 설치... | 고른 모드 2개 최신 버전 확인 | 업데이트 확인 안 함 | —"));
                await Pick(zipList, 0, "Female Animation");
                items = await Menu(zipList);
                Check("heading: " + Texts(items), Texts(items) == "이 페이지 파일 2개 최신 버전 확인 | 모드 페이지 열기");

                // Installed mods.
                await Pick(installedList, 1, "Map Tracker Plus - Minimap and Collectibles (UE4SS)");
                items = await Menu(installedList);
                Check("installed menu: " + Texts(items), Texts(items) == "선택한 모드 제거... | (x)옵션 바꾸기... | — | 위로 | 아래로 | 구분선 추가... | — | 이 모드만 최신 버전 확인 | 업데이트 확인 안 함 | — | 모드 목록에서 찾기");
                await Pick(zipList, 0);
                await Choose(items, "모드 목록에서 찾기");
                Check("found in the mod list", zipList.SelectedItems.Count == 1 && zipList.SelectedItems[0].Tag is ModGroup { Latest.FileName: tracker });
                var separator = installedList.Items.Cast<ListViewItem>().First(i => i.Tag is InstalledRow { IsSeparator: true });
                foreach (ListViewItem i in installedList.Items) i.Selected = i == separator;
                await Task.Delay(300);
                items = await Menu(installedList);
                Check("separator menu: " + Texts(items), Texts(items) == "구분선 이름 바꾸기... | 구분선 삭제 | — | (x)위로 | 아래로");

                await Pick(zipList, 0, "Map Tracker Plus - Minimap and Collectibles (UE4SS)");
                zipList.ContextMenuStrip!.Show(zipList, new Point(60, 60));
                await Task.Delay(300);
                using (var shot = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(shot, new Rectangle(Point.Empty, form.Size));
                    using var g = Graphics.FromImage(shot);
                    var menu = zipList.ContextMenuStrip;
                    using var image = new Bitmap(menu.Width, menu.Height);
                    menu.DrawToBitmap(image, new Rectangle(Point.Empty, menu.Size));
                    g.DrawImage(image, form.PointToClient(menu.Location).X + (form.Width - form.ClientSize.Width) / 2, form.PointToClient(menu.Location).Y + (form.Height - form.ClientSize.Height) - 8);
                    shot.Save(Path.Combine(workDir, "menu.png"));
                }
                zipList.ContextMenuStrip.Close();
            }
            catch (Exception ex)
            {
                Check("check stopped: " + ex.GetType().Name + " " + ex.Message, false);
            }
            form.Close();
        };

        Application.Run(form);
        Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Draws the main window and the add dialog in a language, for looking at.</summary>
    static int Pictures(string workDir, string language)
    {
        L.Use(language);
        var manager = MakeGame(workDir, new StandIn());
        var form = new MainForm(manager, Array.Empty<string>(), _ => { })
        {
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            ClientSize = new Size(1280, 760),
        };
        void Save(Control c, string name)
        {
            using var bitmap = new Bitmap(c.Width, c.Height);
            c.DrawToBitmap(bitmap, new Rectangle(Point.Empty, c.Size));
            bitmap.Save(Path.Combine(workDir, $"{name}-{language}.png"));
        }
        form.Shown += async (_, _) =>
        {
            try
            {
                await Task.Delay(1500);
                Save(form, "main");
                using var dialog = new ModInfoDialog("New Mod 1.0.zip", new ModMeta(), new List<string>(), true, _ => { }, _ => { }, "UE4SS");
                dialog.ShowInTaskbar = false;
                dialog.Show(form);
                await Task.Delay(500);
                Save(dialog, "add");
                dialog.Close();
                Check("pictures drawn in " + language, true);
            }
            catch (Exception ex)
            {
                Check("pictures stopped: " + ex.Message, false);
            }
            form.Close();
        };
        Application.Run(form);
        Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
        return failures == 0 ? 0 : 1;
    }
}
