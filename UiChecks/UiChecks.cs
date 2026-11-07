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
                : body.Contains("description") ? """{"data":{"legacyModsByDomain":{"nodes":[{"name":"Stand-in Mod","modCategory":{"name":"Visuals"},"description":"STEPS"}]}}}""".Replace("STEPS", Steps)
                : """{"data":{"legacyModsByDomain":{"nodes":[]}}}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.Replace("LOCALHASH", LocalHash), Encoding.UTF8, "application/json") };
        }

        public string LocalHash = "";
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
        const string withDocs = "Readme Mod 600 1.0 2026-11-07T00-00Z r.zip";
        Zip(Path.Combine(manager.ModDir, withDocs),
            ("~mods/Readme_P.pak", "p"),
            ("README.txt", "설치 방법\r\n1. ~mods 폴더에 넣으세요.\r\n" + string.Join("\r\n", Enumerable.Range(2, 80).Select(i => i + ". 줄"))),
            ("Changelog.md", "# 1.0\r\n- first"),
            ("Guide.pdf", "%PDF-1.4"));
        // Installing leaves the readmes out and says so.
        var plan = manager.BuildPlan(Path.Combine(manager.ModDir, withDocs));
        Check("install leaves readmes out: " + string.Join(", ", plan.Items.Select(i => i.Path)), plan.Items.Count == 1 && plan.Warnings.Any(w => w.Contains("설명서 3개")));
        manager.Install(plan);
        var opened = new List<string>();
        var form = new MainForm(manager, Array.Empty<string>(), opened.Add)
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
                async Task Pick(string name)
                {
                    foreach (ListViewItem i in zipList.Items) i.Selected = i.SubItems[0].Text.Trim() == name;
                    await Task.Delay(400);
                }
                var names = zipList.Items.Cast<ListViewItem>().Select(i => i.SubItems[0].Text.Trim()).ToList();
                var docsName = names.First(n => n.StartsWith("Readme"));
                await Pick("Old");
                Check("no readme, no button", !All(form).OfType<Button>().Any(b => b.Visible && b.Text.StartsWith("설명서 보기")));
                await Pick(docsName);
                var button = All(form).OfType<Button>().SingleOrDefault(b => b.Visible && b.Text.StartsWith("설명서 보기"));
                Check("readme button: " + button?.Text, button?.Text == "설명서 보기 (3)...");
                Check("no pictures, no picture button", !All(form).OfType<Button>().Any(b => b.Visible && b.Text.StartsWith("이미지 보기")));

                Click(button!);
                var window = await Window<DocumentWindow>(form);
                Check("listed: " + string.Join(", ", window.Names), window.Names.SequenceEqual(new[] { "Changelog.md", "Guide.pdf", "README.txt" }));
                Check("first shown: " + window.ShownText, window.ShownText.StartsWith("# 1.0"));
                var list = All(window).OfType<ListBox>().Single();
                list.SelectedIndex = 2;
                await Task.Delay(300);
                Check("readme shown", window.ShownText.Contains("~mods 폴더에 넣으세요"));
                Check("the app's scrollbars, not the native ones", All(window).OfType<DarkScrollBar>().Count() == 2 && All(window).OfType<ScrollHost>().Count() == 2);
                var pdfButton = Find(window, "PDF 프로그램으로 열기");
                Check("pdf button only for a pdf", !pdfButton.Visible);
                list.SelectedIndex = 1;
                await Task.Delay(300);
                Check("pdf: told to open it outside", pdfButton.Visible && window.ShownText.Contains("PDF는 여기서 보여 줄 수 없습니다"));
                Click(pdfButton);
                await Task.Delay(300);
                Check("pdf copied out and opened: " + opened.LastOrDefault(), opened.LastOrDefault() is { } pdf && pdf.EndsWith("Guide.pdf") && File.ReadAllText(pdf) == "%PDF-1.4");
                list.SelectedIndex = 2;
                await Task.Delay(300);
                foreach (var host in All(window).OfType<ScrollHost>())
                {
                    var inner = host.Controls.Cast<Control>().Single(c => c is not DarkScrollBar);
                    var bar = host.Controls.OfType<DarkScrollBar>().Single();
                    Check($"native bar clipped, app bar on top: inner right {inner.Right} > host {host.ClientSize.Width}, bar at {bar.Left}",
                        inner.Right > host.ClientSize.Width && bar.Right == host.ClientSize.Width && host.Controls.GetChildIndex(bar) == 0);
                }
                // A click on the track below the thumb pages the text down.
                var textBox = All(window).OfType<RichTextBox>().Single();
                var textBar = All(window).OfType<DarkScrollBar>().Single(b => b.Parent!.Controls.Contains(textBox));
                await Task.Delay(200);
                typeof(Control).GetMethod("OnMouseDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(textBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 6, textBar.Height - 10, 0) });
                await Task.Delay(200);
                var top = textBox.GetCharIndexFromPosition(new Point(4, 4));
                Check("the app's bar scrolls the text: first shown line " + textBox.GetLineFromCharIndex(top), textBox.GetLineFromCharIndex(top) > 10);
                // Drawn as the screen shows it: DrawToBitmap leaves child windows unclipped.
                using (var shot = new Bitmap(window.Width, window.Height))
                {
                    using (var g = Graphics.FromImage(shot))
                    {
                        var dc = g.GetHdc();
                        PrintWindow(window.Handle, dc, 2);
                        g.ReleaseHdc(dc);
                    }
                    shot.Save(Path.Combine(workDir, "docs.png"));
                }
                window.Close();
                await Task.Delay(200);

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
