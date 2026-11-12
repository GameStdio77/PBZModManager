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
            if (args.Length > 2 && args[2].StartsWith("shots:")) return Shots(args[0], args[2][6..]);
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

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    static int Run(string workDir)
    {
        var standIn = new StandIn();
        var manager = MakeGame(workDir, standIn);
        Manager.DeleteInsteadOfRecycleForTests = true;
        // Copies of two real Bridge archives, the installed 0.3.1 and the 0.9.6 with the same files,
        // given by BRIDGE_OLD and BRIDGE_NEW (full paths).
        var oldPath = Environment.GetEnvironmentVariable("BRIDGE_OLD")!;
        var oldName = Path.GetFileName(oldPath);
        File.Copy(oldPath, Path.Combine(manager.ModDir, oldName));
        manager.Install(manager.BuildPlan(Path.Combine(manager.ModDir, oldName)));
        var newPath = Environment.GetEnvironmentVariable("BRIDGE_NEW")!;
        var incoming = Path.Combine(workDir, Path.GetFileName(newPath));
        File.Copy(newPath, incoming);

        var form = new MainForm(manager, Array.Empty<string>(), _ => { }) { StartPosition = FormStartPosition.Manual, ShowInTaskbar = false, ClientSize = new Size(1500, 860) };
        Application.ThreadException += (_, e) => Check("no unhandled error: " + e.Exception.Message, false);
        form.Shown += async (_, _) =>
        {
            try
            {
                await Task.Delay(1000);
                var zipList = All(form).OfType<DarkListView>().First();
                var adding = (Task)typeof(MainForm).GetMethod("AddZips", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(form, new object[] { new[] { incoming } })!;
                // The new version is offered as an update: answered yes.
                var offer = await Window<Form>(form, f => f.Visible && f.Modal && f != form);
                var words = string.Join(" ", All(offer).OfType<Label>().Select(l => l.Text));
                Check("offered as an update: " + words, words.Contains("새 버전을 추가했습니다") && words.Contains("0.9.6"));
                offer.DialogResult = DialogResult.Yes;
                var plan = await Window<PlanDialog>(form);
                var planWords = string.Join(" ", All(plan).OfType<Control>().Select(c => c.Text));
                Check("the preview says only the version changes", planWords.Contains("버전 번호만"));
                plan.DialogResult = DialogResult.OK;
                await adding;
                await Task.Delay(800);
                var installed = manager.ListInstalled().Single(m => m.Name.Contains("Bridge"));
                Check("installed version now 0.9.6: " + installed.ZipName, installed.Version == "0.9.6");
                var row = zipList.Items.Cast<ListViewItem>().First(i => i.Tag is ModGroup g && g.Name.Contains("Bridge"));
                Check("status: " + row.SubItems[5].Text, row.SubItems[5].Text == "설치됨 (0.9.6)");
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

    sealed class Quiet : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.Host.Contains("github") ? "[]" : """{"data":{}}""", Encoding.UTF8, "application/json") });
    }

    /// <summary>
    /// README pictures from a copy of real records: workDir holds "game" (folders only) and "mods\.manager".
    /// Every dialog is cancelled; nothing is installed.
    /// </summary>
    static int Shots(string workDir, string language)
    {
        L.Use(language);
        Manager.IgnoreGameForTests = true;
        Manager.DeleteInsteadOfRecycleForTests = true;
        NexusApi.UseHandlerForChecks(new Quiet());
        var manager = new Manager(Path.Combine(workDir, "game"), Path.Combine(workDir, "mods"));
        var outDir = Path.Combine(workDir, "shots");
        Directory.CreateDirectory(outDir);
        var form = new MainForm(manager, Array.Empty<string>(), _ => { }) { StartPosition = FormStartPosition.Manual, ShowInTaskbar = false };
        // The window as Windows draws it (PrintWindow, so the list's own scroll bars stay hidden), without the title bar.
        void Save(Control c, string name)
        {
            using var whole = new Bitmap(c.Width, c.Height);
            using (var g = Graphics.FromImage(whole))
            {
                var dc = g.GetHdc();
                PrintWindow(c.Handle, dc, 2);
                g.ReleaseHdc(dc);
            }
            var origin = c.PointToScreen(Point.Empty);
            var client = new Rectangle(origin.X - c.Bounds.X, origin.Y - c.Bounds.Y, c.ClientSize.Width, c.ClientSize.Height);
            using var cut = whole.Clone(client, whole.PixelFormat);
            cut.Save(Path.Combine(outDir, $"{name}-{language}.png"));
        }
        void Later(Action a) => form.BeginInvoke(a);
        form.Shown += async (_, _) =>
        {
            try
            {
                form.WindowState = FormWindowState.Normal;
                form.ClientSize = new Size(1440, 860);
                await Task.Delay(1500);
                // The paths in the header are the user's own; shown as a usual Steam library instead.
                foreach (var label in All(form).OfType<Label>().Where(l => l.Text.Contains(manager.Root)))
                    label.Text = L.F("게임: {0}    모드 보관: {1}", @"D:\SteamLibrary\steamapps\common\PBZ", @"D:\SteamLibrary\steamapps\common\PBZ\MOD\.manager\archives");
                // This tool's own version is not the manager's.
                foreach (var label in All(form).OfType<Label>().Where(l => l.Text.StartsWith("GitHub · v")))
                    label.Text = "GitHub · v" + Environment.GetEnvironmentVariable("SHOT_VERSION");
                // A mod row chosen in the installed list rather than the separator at the top.
                var installedList = All(form).OfType<DarkListView>().Skip(1).First();
                var firstMod = installedList.Items.Cast<ListViewItem>().First(i => i.SubItems[0].Text.Trim().Length > 0 && char.IsDigit(i.SubItems[0].Text.Trim()[0]));
                foreach (ListViewItem i in installedList.Items) i.Selected = i == firstMod;
                await Task.Delay(300);
                Save(form, "main");

                // Install preview of a mod not installed now.
                var mods = (List<ModGroup>)typeof(MainForm).GetField("mods", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(form)!;
                var pick = mods.Where(m => m.InstalledFile == null && !m.Latest.Missing).ToList();
                Console.WriteLine("not installed: " + string.Join(" | ", pick.Select(m => m.Shown)));
                var mod = pick.FirstOrDefault(m => m.Shown.Contains(Environment.GetEnvironmentVariable("SHOT_MOD") ?? "\u0001")) ?? pick.First();
                var installing = (Task)typeof(MainForm).GetMethod("InstallVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(form, new object?[] { mod, mod.Latest, null })!;
                var next = await Window<Form>(form, f => f.Visible && f.Modal && f != form);
                if (next is OptionsDialog options)
                {
                    Save(options, "options");
                    options.DialogResult = DialogResult.OK;
                    next = await Window<PlanDialog>(form);
                }
                if (next is PlanDialog plan)
                {
                    await Task.Delay(400);
                    Save(plan, "plan");
                }
                next.DialogResult = DialogResult.Cancel;
                await installing;

                // Presets and the editor of the first one.
                Later(() => typeof(MainForm).GetMethod("OpenPresets", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(form, new object?[] { null }));
                var presets = await Window<PresetDialog>(form);
                await Task.Delay(400);
                Save(presets, "presets");
                var edit = Find(presets, L.T("수정..."));
                Later(() => Click(edit));
                var editor = await Window<PresetEditor>(form);
                await Task.Delay(400);
                Save(editor, "preset-editor");
                editor.DialogResult = DialogResult.Cancel;
                await Task.Delay(300);
                presets.DialogResult = DialogResult.Cancel;
                await Task.Delay(300);
                Check("pictures drawn in " + language, true);
            }
            catch (Exception ex)
            {
                Check("pictures stopped: " + ex.GetType().Name + " " + ex.Message, false);
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
