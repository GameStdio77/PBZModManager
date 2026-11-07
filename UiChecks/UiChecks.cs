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
        // Pages 385 and 187 as Nexus answers a version check, with their titles.
        standIn.Check = """
            {"data": {"legacyModsByDomain": {"nodes": [{"modId": 385, "name": "Female Animation", "version": "7", "status": "published", "modCategory": {"name": "Animations"}}, {"modId": 187, "name": "Map Tracker Plus - Minimap and Collectibles (UE4SS)", "version": "1.0.4", "status": "published", "modCategory": {"name": "Gameplay"}}, {"modId": 300, "name": "YoRHa 2B Replace Soul_(Outfit Options)_v2.0", "version": "2.1.1", "status": "published", "modCategory": {"name": "Characters"}}]}, "m385": [{"fileId": 1048, "name": "PBZ FemaleAnimation", "version": "1", "category": "OLD_VERSION"}, {"fileId": 1322, "name": "PBZ FemaleAnimation OtherBodies", "version": "1", "category": "OLD_VERSION"}, {"fileId": 1323, "name": "PBZ FemaleAnimation for 2B", "version": "2", "category": "OLD_VERSION"}, {"fileId": 1342, "name": "PBZ FemaleAnimation for 2B", "version": "3", "category": "OLD_VERSION"}, {"fileId": 1343, "name": "PBZ FemaleAnimation OtherBodies", "version": "3", "category": "OLD_VERSION"}, {"fileId": 1509, "name": "PBZ FemaleAnimation OtherBodies", "version": "4", "category": "OLD_VERSION"}, {"fileId": 1510, "name": "PBZ FemaleAnimation Original Idle", "version": "1", "category": "OLD_VERSION"}, {"fileId": 2304, "name": "PBZ FemaleAnimation Original Idle", "version": "6", "category": "OLD_VERSION"}, {"fileId": 2305, "name": "PBZ FemaleAnimation OtherBodies", "version": "6", "category": "OLD_VERSION"}, {"fileId": 2306, "name": "PBZ FemaleAnimation for 2B(my personal edit)", "version": "6", "category": "OLD_VERSION"}, {"fileId": 2307, "name": "PBZ FemaleAnimation For SoulVerR", "version": "5", "category": "OLD_VERSION"}, {"fileId": 2333, "name": "PBZ FemaleAnimation For Ver.R", "version": "6", "category": "OLD_VERSION"}, {"fileId": 2350, "name": "PBZ FemaleAnimation For 2B(Original)", "version": "6", "category": "OLD_VERSION"}, {"fileId": 2354, "name": "PBZ FemaleAnimation For 2B(Original)", "version": "6.1", "category": "OLD_VERSION"}, {"fileId": 2359, "name": "PBZ FemaleAnimation For 2B(Original)", "version": "7", "category": "MAIN"}, {"fileId": 2360, "name": "PBZ FemaleAnimation For Ver.R", "version": "7", "category": "MAIN"}, {"fileId": 2361, "name": "PBZ FemaleAnimation for 2B(my personal edit)", "version": "7", "category": "OPTIONAL"}, {"fileId": 2362, "name": "PBZ FemaleAnimation OtherBodies", "version": "7", "category": "MAIN"}, {"fileId": 2363, "name": "PBZ FemaleAnimation Original Idle", "version": "7", "category": "MAIN"}], "m187": [{"fileId": 1, "name": "Map Collectible Tracker 1.0.4", "version": "1.0.4", "category": "MAIN"}], "m300": [{"fileId": 2, "name": "YoRHa 2B Replace Soul_(Outfit Options)_v2.1.1", "version": "2.1.1", "category": "MAIN"}]}}
            """;
        const string tracker = "Map Collectible Tracker 1.0.4 187 1.0.4 2026-11-07T00-32Z s0mFcMSWX.zip";
        const string yorha = "YoRHa 2B Replace Soul_(Outfit Options)_v2.1.1 300 2.1.1 2026-11-07T02-30Z mjROM2ABF.zip";
        const string twoB = "PBZ FemaleAnimation For 2B(Original) 385 7 2026-11-08T00-00Z abcdEFGH.zip";
        const string verR = "PBZ FemaleAnimation For Ver.R 385 7 2026-11-07T04-43Z Ihsdbc5En.zip";
        foreach (var (name, pak) in new[] { (tracker, "Tracker_P.pak"), (yorha, "YoRHa_P.pak"), (twoB, "TwoB_P.pak"), (verR, "VerR_P.pak") })
        {
            Zip(Path.Combine(manager.ModDir, name), ("ProjectPBZ/Content/Paks/~mods/" + pak, "x"));
            manager.Install(manager.BuildPlan(Path.Combine(manager.ModDir, name)));
        }
        // The title the add dialog read for 300, with the old version the author left in it.
        var meta = manager.LoadMeta();
        meta[yorha] = new ModMeta { Registered = true, Url = "https://www.nexusmods.com/phantombladezero/mods/300", PageName = "YoRHa 2B Replace Soul_(Outfit Options)_v2.0" };
        manager.SaveMeta(meta);

        var form = new MainForm(manager, Array.Empty<string>(), _ => { })
        {
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            ClientSize = new Size(1500, 860),
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
                List<string> Names(DarkListView list, int column) => list.Items.Cast<ListViewItem>().Select(i => i.SubItems[column].Text.Trim()).ToList();
                async Task Pick(string name)
                {
                    foreach (ListViewItem i in zipList.Items) i.Selected = i.SubItems[0].Text.Trim() == name;
                    await Task.Delay(400);
                }
                Check("before the check: file names, and 300 by the title read when it was added: " + string.Join(" | ", Names(zipList, 0)),
                    Names(zipList, 0).Contains("Map Collectible Tracker") && Names(zipList, 0).Contains("YoRHa 2B Replace Soul_(Outfit Options)"));

                Click(Find(form, "최신 버전 확인"));
                for (int i = 0; i < 60; i++)
                {
                    var dialog = Application.OpenForms.OfType<Form>().FirstOrDefault(f => f != form && f.Visible && f.Modal);
                    if (dialog != null) { await Task.Delay(200); dialog.DialogResult = DialogResult.No; break; }
                    await Task.Delay(100);
                }
                await Task.Delay(800);
                var names = Names(zipList, 0);
                Check("after the check: the page's title, and a heading with the files under it: " + string.Join(" | ", names),
                    names.Contains("Map Tracker Plus - Minimap and Collectibles (UE4SS)") && names.Contains("Female Animation")
                    && names.Contains("2B(Original)") && names.Contains("Ver.R"));
                Check("installed list goes by the same names: " + string.Join(" | ", Names(installedList, 1)),
                    Names(installedList, 1).Contains("Map Tracker Plus - Minimap and Collectibles (UE4SS)") && Names(installedList, 1).Contains("PBZ FemaleAnimation For Ver.R"));

                await Pick("Map Tracker Plus - Minimap and Collectibles (UE4SS)");
                var editor = All(form).OfType<ModInfoEditor>().Single();
                var title = All(editor).OfType<Label>().First();
                var display = All(editor).OfType<TextBox>().Single(b => b.PlaceholderText.Length > 0);
                Check("info: the file's name beside the title: " + title.Text, title.Text == "Map Tracker Plus - Minimap and Collectibles (UE4SS) · 파일: Map Collectible Tracker");
                Check("display name empty, the automatic one as its hint: " + display.PlaceholderText, display.Text == "" && display.PlaceholderText == "Map Tracker Plus - Minimap and Collectibles (UE4SS)");

                display.Text = "미니맵 트래커";
                await Task.Delay(500);
                Check("the user's name goes first, in both lists: " + string.Join(" | ", Names(zipList, 0)),
                    Names(zipList, 0).Contains("미니맵 트래커") && Names(installedList, 1).Contains("미니맵 트래커") && manager.LoadMeta()[tracker].DisplayName == "미니맵 트래커");
                display.Text = "";
                await Task.Delay(500);
                Check("cleared: back to the page's title", Names(zipList, 0).Contains("Map Tracker Plus - Minimap and Collectibles (UE4SS)"));

                await Pick("Female Animation");
                Check("heading: the page's title in the info: " + title.Text + ", no name field", title.Text == "Female Animation" && !display.Parent!.Visible);

                using (var shot = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(shot, new Rectangle(Point.Empty, form.Size));
                    shot.Save(Path.Combine(workDir, "list.png"));
                }
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
