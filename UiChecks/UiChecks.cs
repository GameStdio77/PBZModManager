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
        var opened = new List<string>();
        var started = new List<string>();
        var restarts = 0;
        var form = new MainForm(manager, Array.Empty<string>(), opened.Add)
        {
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            ClientSize = new Size(1500, 860),
            StartGame = started.Add,
            RestartApp = () => restarts++,
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
                await Task.Delay(1200);
                // The manager's own newer release shows in the corner, beside the project link.
                var update = All(form).OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("새 버전"));
                Check("newer manager release shown: " + update?.Text, update is { Visible: true, Text: "새 버전 v9.1.0 있음" });
                var link = All(form).OfType<Label>().First(l => l.Text.StartsWith("GitHub · v"));
                Check("project link stays in the corner", update != null && link.Left > update.Left);
                Click(update!);
                Check("the notice opens the release page", opened.LastOrDefault() == "https://github.com/GameStdio77/PBZModManager/releases/tag/v9.1.0");
                Check("an older or same release is not news", AppUpdate.Newer(new RemoteInfo { Version = "1.0.0", PageUrl = "x" }, "1.0.0") == null
                    && AppUpdate.Newer(new RemoteInfo { Version = "1.1", PageUrl = "x" }, "1.0.0") != null
                    && AppUpdate.Newer(new RemoteInfo { Version = "0.9.5", PageUrl = "x" }, "1.0.0") == null);

                // The launch button starts the Steam build's program.
                if (!Manager.GameRunning())
                {
                    Find(form, "게임 실행").PerformClick();
                    await Task.Delay(300);
                    Check("launch starts platform/steam/PBZ.exe", started.SingleOrDefault() == manager.GameExe
                        && manager.GameExe.EndsWith(Path.Combine("platform", "steam", "PBZ.exe")));
                }
                else
                {
                    // The real game is open: the button says so and starts nothing.
                    Find(form, "게임 실행").PerformClick();
                    var said = await Window<Form>(form, f => Shows(f, "게임이 이미 실행 중입니다."));
                    Find(said, "확인").PerformClick();
                    await Task.Delay(200);
                    Check("launch while the game runs starts nothing", started.Count == 0);
                }

                // Picking a language saves it and offers a restart.
                var languageBox = All(form).OfType<DarkComboBox>().Single(c => c.Items.Contains("English"));
                Check("four languages offered, Korean chosen", languageBox.Items.Count == 4 && (string)languageBox.SelectedItem! == "한국어");
                languageBox.SelectedIndex = 1;
                typeof(ComboBox).GetMethod("OnSelectionChangeCommitted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(languageBox, new object[] { EventArgs.Empty });
                var ask = await Window<Form>(form, f => f.Text == "Language");
                Check("restart asked in both languages", Shows(ask, "Restart now?") && Shows(ask, "지금 다시 시작할까요?"));
                Find(ask, "예").PerformClick();
                await Task.Delay(300);
                Check("language saved, restart asked for", Manager.SavedLanguage(manager.ModDir) == "en" && restarts == 1 && L.Current == "ko");

                // A mod just added asks for its address first.
                using (var dialog = new ModInfoDialog("New 1.0.zip", new ModMeta(), new List<string>(), false, _ => { }, _ => { }))
                {
                    dialog.ShowInTaskbar = false;
                    dialog.Show(form);
                    await Task.Delay(400);
                    var editor = All(dialog).OfType<ModInfoEditor>().Single();
                    var address = All(editor).OfType<Label>().Single(l => l.Text == "주소");
                    Check("address is the first field", editor.GetRow(address) == 1 && address.ForeColor == Theme.Info);
                    Check("hint says what the address brings", Shows(dialog, "카테고리와 설치 방법을 자동으로"));
                    var url = All(editor).OfType<TextBox>().First(b => !b.Multiline && b.MaxLength != 200);
                    Check("address box has the focus", url.Focused);
                    dialog.Close();
                }

                // Texts in other languages, and a missing one falling back to Korean.
                L.Use("en");
                Check("English text", L.T("설치") == "Install" && L.F("모드 {0}개 설치 완료", 3) == "Installed 3 mods" && L.T("없는 문장") == "없는 문장");
                Check("every text has all three languages", L.All.Values.All(v => v.En.Length > 0 && v.Zh.Length > 0 && v.Ja.Length > 0));
                L.Use("ko");
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
