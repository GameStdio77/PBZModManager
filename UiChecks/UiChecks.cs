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
                : body.Contains("description") ? """{"data":{"legacyModsByDomain":{"nodes":[{"modCategory":{"name":"Visuals"},"description":"Installation:<br />1. Step one<br />Uninstall:<br />x"}]}}}"""
                : """{"data":{"legacyModsByDomain":{"nodes":[]}}}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.Replace("LOCALHASH", LocalHash), Encoding.UTF8, "application/json") };
        }

        public string LocalHash = "";
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
        // An option mod whose next version its author packed anew: wrapped in a folder, its options renamed.
        var v101 = Path.Combine(manager.ModDir, "Outfit AIO 414 1.01 2026-11-04T10-23Z abc.zip");
        var v102 = Path.Combine(manager.ModDir, "Outfit AIO 414 1.02 2026-11-06T14-04Z def.zip");
        Zip(v101, ("Outfit-00-Body(choose one)-01-a/Outfit_Body_P.pak", "a"), ("Outfit-00-Body(choose one)-02-b/Outfit_Body_P.pak", "b"));
        Zip(v102, ("Outfit AIO v1.02/Outfit-00-Body(choose one)-01-stockings/LogicMods/Outfit_Body_P.pak", "new a"),
            ("Outfit AIO v1.02/Outfit-00-Body(choose one)-02-side/LogicMods/Outfit_Body_P.pak", "new b"));
        manager.Install(manager.BuildPlan(v101, new[] { "Outfit-00-Body(choose one)-02-b" }));
        File.WriteAllText(Path.Combine(manager.StateDir, "meta.json"), System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [Path.GetFileName(v101)] = new { Registered = true },
            [Path.GetFileName(v102)] = new { Registered = true },
        }));
        var root = manager.Root;

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
                var zipList = All(form).OfType<DarkListView>().First();
                foreach (ListViewItem i in zipList.Items) i.Selected = i.SubItems[0].Text.Trim() == "Outfit AIO";
                await Task.Delay(300);
                var install = All(form).OfType<Button>().First(b => b.Text.StartsWith("업데이트"));
                install.PerformClick();

                var ask = await Window<Form>(form, f => f.Text == "구조가 바뀐 버전");
                Check("a version laid out anew is pointed out", Shows(ask, "구조가 달라졌습니다") && Shows(ask, "옵션 구성이 바뀌어"));
                Find(ask, "예").PerformClick();

                var remove = await Window<Form>(form, f => f.Text == "모드 제거 확인 - Outfit AIO");
                Find(remove, "제거").PerformClick();
                var options = await Window<Form>(form, f => f.Text == "옵션 고르기 - Outfit AIO");
                Check("then installed fresh, options asked", true);
                Find(options, "다음").PerformClick();
                var plan = await Window<Form>(form, f => f.Text == "설치 확인 - Outfit AIO");
                Find(plan, "설치").PerformClick();
                await Task.Delay(1000);

                Check("old version removed, new one in LogicMods",
                    !File.Exists(Path.Combine(root, "ProjectPBZ/Content/Paks/~mods/Outfit_Body_P.pak"))
                    && File.ReadAllText(Path.Combine(root, "ProjectPBZ/Content/Paks/LogicMods/Outfit_Body_P.pak")) == "new a");
                var mine = manager.History().Where(t => t.ZipName.StartsWith("Outfit AIO")).ToList();
                Check("one install of the new version left: " + string.Join(", ", mine.Select(t => t.ZipName)),
                    mine.Count == 1 && mine[0].ZipName == Path.GetFileName(v102));

                // A rar put next to the work folder is added as a zip; its pictures open in their own window.
                var rar = Directory.GetFiles(workDir, "*.rar").FirstOrDefault();
                if (rar == null) Console.WriteLine("SKIP  rar (none given)");
                else
                {
                    // Added the way a drop on the window adds it; the info dialog for a new mod is put off.
                    var adding = (Task)typeof(MainForm).GetMethod("AddZips", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(form, new object[] { new[] { rar } })!;
                    var info = await Window<ModInfoDialog>(form);
                    Check("a new mod asks for its address first", All(info).OfType<Label>().Any(l => l.Text == "주소"));
                    Find(info, "나중에").PerformClick();
                    await adding;
                    var converted = Path.Combine(manager.ModDir, Path.GetFileNameWithoutExtension(rar) + ".zip");
                    Check("rar added as a zip of the same name, the original left where it was", File.Exists(converted) && File.Exists(rar));
                    using (var z = System.IO.Compression.ZipFile.OpenRead(converted))
                        Console.WriteLine($"      {z.Entries.Count} entries, options: {string.Join(" | ", manager.OptionGroups(converted).Select(g => g.Options.Count + " choices"))}");
                    Check("the zip can be planned", manager.BuildPlan(converted).Items.Count > 0);
                    var pictures = Manager.PicturesIn(converted);
                    Console.WriteLine("      pictures: " + string.Join(", ", pictures.Take(6)));

                    var mods = All(form).OfType<DarkListView>().First();
                    var name = ModIdentity.ParseFileName(Path.GetFileName(converted)).Name;
                    foreach (ListViewItem i in mods.Items) i.Selected = i.SubItems[0].Text.Contains("YoRHa");
                    Console.WriteLine("      listed: " + string.Join(" | ", mods.Items.Cast<ListViewItem>().Select(i => i.SubItems[0].Text)) + " ; parsed name: " + name);
                    await Task.Delay(500);
                    var button = All(form).OfType<Button>().FirstOrDefault(b => b.Text.StartsWith("이미지 보기"));
                    Check("picture button says how many: " + button?.Text, pictures.Count > 0 && button is { Visible: true } && button.Text == $"이미지 보기 ({pictures.Count})...");
                    button!.PerformClick();
                    var window = await Window<PictureWindow>(form);
                    var first = window.Caption;
                    window.ShowAt(1);
                    Check("pictures turn: " + first + " → " + window.Caption, first.EndsWith($"(1/{pictures.Count})") && window.Caption.EndsWith($"(2/{Math.Max(2, pictures.Count)})") || pictures.Count == 1);
                    var picture = All(window).OfType<PictureBox>().Single();
                    Check("a picture is shown", picture.Image != null);
                    using (var shot = new Bitmap(window.Width, window.Height))
                    {
                        window.DrawToBitmap(shot, new Rectangle(Point.Empty, window.Size));
                        shot.Save(Path.Combine(workDir, "pictures.png"));
                    }
                    window.Close();
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
