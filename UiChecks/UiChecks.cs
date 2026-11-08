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

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    static int Run(string workDir)
    {
        var standIn = new StandIn();
        var manager = MakeGame(workDir, standIn);
        Manager.DeleteInsteadOfRecycleForTests = true;
        string Archive(string name, string pak)
        {
            var path = Path.Combine(manager.ModDir, name);
            Zip(path, ("ProjectPBZ/Content/Paks/~mods/" + pak, pak));
            return path;
        }
        var alpha = Archive("Alpha 900 1.0 2026-11-01T00-00Z a.zip", "Alpha_P.pak");
        var beta = Archive("Beta 901 1.0 2026-11-01T00-00Z c.zip", "Beta_P.pak");
        var gamma = Archive("Gamma 902 1.0 2026-11-01T00-00Z d.zip", "Gamma_P.pak");
        var look1 = Archive("Look One 910 1.0 2026-11-01T00-00Z f.zip", "Look1_P.pak");
        var look2 = Archive("Look Two 911 1.0 2026-11-01T00-00Z g.zip", "Look2_P.pak");
        foreach (var z in new[] { alpha, beta, look1 }) manager.Install(manager.BuildPlan(z));
        var meta = manager.LoadMeta();
        foreach (var z in new[] { alpha, beta, gamma, look1, look2 }) meta[Path.GetFileName(z)] = new ModMeta { Registered = true };
        manager.SaveMeta(meta);
        manager.AddSeparator("Characters", null);
        manager.AddSeparator("Looks", ModIdentity.KeyOfZip(beta));

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
        string Installed() => string.Join(", ", manager.ListInstalled().Select(m => m.Name).OrderBy(s => s));
        string LinesOf(PresetEditor editor) => string.Join(", ", editor.Lines.Select(l => l.IsSeparator ? "sep:" + l.Separator : l.Name));
        async Task<PresetDialog> OpenDialog()
        {
            ((DarkButton)All(form).OfType<Button>().Single(b => b.Text == "프리셋...")).Press();
            return await Window<PresetDialog>(form);
        }
        void Select(ListView list, Func<object?, bool> wanted)
        {
            foreach (ListViewItem i in list.Items) i.Selected = wanted(i.Tag);
        }
        form.Shown += async (_, _) =>
        {
            try
            {
                await Task.Delay(1000);
                var installedList = All(form).OfType<DarkListView>().ElementAt(1);
                Select(installedList, t => t is InstalledRow { Mod: { } m } && (m.Name == "Alpha" || m.Name == "Look One"));
                await Task.Delay(300);

                // A right click on the picked mods makes a preset of them, each under its separator.
                installedList.ContextMenuStrip!.Show(installedList, new Point(40, 40));
                await Task.Delay(300);
                var presetItem = installedList.ContextMenuStrip.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "프리셋에 넣기");
                installedList.ContextMenuStrip.Close();
                presetItem.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "새 프리셋...").PerformClick();
                var fromPicked = await Window<PresetEditor>(form);
                Check("right click: the picked mods under their separators: " + LinesOf(fromPicked), LinesOf(fromPicked) == "sep:Characters, Alpha, sep:Looks, Look One");
                fromPicked.DialogResult = DialogResult.Cancel;
                await Task.Delay(400);

                // [New...] starts from every installed mod, whatever line the installed list has picked.
                var dialog = await OpenDialog();
                ((DarkButton)Find(dialog, "새로 만들기...")).Press();
                var editor = await Window<PresetEditor>(form);
                Check("new: every installed mod and separator: " + LinesOf(editor), LinesOf(editor) == "sep:Characters, Old, Alpha, Beta, sep:Looks, Look One");
                Select(All(editor).OfType<DarkListView>().ElementAt(1), t => t is PresetLine { Name: "Old" } or PresetLine { Name: "Beta" });
                editor.TakeOut();

                // A mod not installed is put in after Alpha, moved above it, and a separator added before Look One.
                var lists = All(editor).OfType<DarkListView>().ToList();
                var (left, right) = (lists[0], lists[1]);
                Select(left, t => t is PresetChoice { Name: "Gamma" });
                Select(right, t => t is PresetLine { Name: "Alpha" });
                editor.PutIn();
                Select(right, t => t is PresetLine { Name: "Gamma" });
                editor.Move(-1);
                Select(right, t => t is PresetLine { Name: "Look One" });
                var addButton = (DarkButton)Find(editor, "구분선 추가...");
                editor.BeginInvoke(() => addButton.Press());
                var ask = await Window<Form>(form, f => f.Text == "구분선 추가");
                All(ask).OfType<TextBox>().Single().Text = "New";
                ask.DialogResult = DialogResult.OK;
                await Task.Delay(400);
                Check("arranged: " + LinesOf(editor), LinesOf(editor) == "sep:Characters, Gamma, Alpha, sep:Looks, sep:New, Look One");
                var gammaRow = left.Items.Cast<ListViewItem>().Single(i => i.Tag is PresetChoice { Name: "Gamma" });
                Check("a mod in the preset is dimmed on the left", gammaRow.ForeColor == Theme.Dim);
                All(editor).OfType<TextBox>().First().Text = "Mix";
                editor.SetGroup("B");
                ((DarkButton)Find(editor, "저장")).Press();
                await Task.Delay(500);
                var mix = manager.LoadPresets().Single();
                Check("saved with its order and group: " + string.Join(", ", mix.Order.Select(o => o.StartsWith("sep:") ? o : mix.Mods.Single(m => m.Key == o).Name)) + " / " + mix.Group,
                    string.Join(", ", mix.Order.Select(o => o.StartsWith("sep:") ? o : mix.Mods.Single(m => m.Key == o).Name)) == "sep:Characters, Gamma, Alpha, sep:Looks, sep:New, Look One" && mix.Group == "B");
                dialog.Close();
                await Task.Delay(300);

                // Two looks in one group: applying the second takes the first out first.
                var presets = manager.LoadPresets();
                PresetMod Entry(string zip, string name) => new() { Key = ModIdentity.KeyOfZip(Path.Combine(manager.ArchiveDir, Path.GetFileName(zip))), Name = name, Version = "1.0" };
                presets.Add(new Preset { Name = "Look 1", Group = "A", Mods = { Entry(look1, "Look One") } });
                presets.Add(new Preset { Name = "Look 2", Group = "A", Mods = { Entry(look2, "Look Two") } });
                manager.SavePresets(presets);
                dialog = await OpenDialog();
                var presetList = All(dialog).OfType<DarkListView>().First();
                Check("group shown: " + string.Join(", ", presetList.Items.Cast<ListViewItem>().Select(i => i.SubItems[0].Text + "/" + i.SubItems[1].Text)),
                    presetList.Items.Cast<ListViewItem>().Any(i => i.SubItems[0].Text == "Look 2" && i.SubItems[1].Text == "A"));
                Select(presetList, t => t is Preset { Name: "Look 2" });
                await Task.Delay(300);
                dialog.Choose(PresetMode.TurnOn);
                var preview = await Window<PresetPreview>(form);
                var rows = All(preview).OfType<DarkListView>().Single().Items.Cast<ListViewItem>().Select(i => $"{i.SubItems[0].Text}:{i.SubItems[1].Text}:{i.SubItems[2].Text}").ToList();
                Check("preview: the other look goes: " + string.Join(" | ", rows), rows.SequenceEqual(new[] { "제거:Look One:교체 그룹 A: Look 1", "설치:Look Two:1.0" }));
                preview.DialogResult = DialogResult.OK;
                for (int n = 0; n < 40; n++) await Task.Delay(100);
                Check("swapped, the rest kept: " + Installed(), Installed() == "Alpha, Beta, Look Two, Old");

                using (var shot = new Bitmap(1020, 620))
                {
                    var again = new PresetEditor("Mix", "Mix", "B", new List<string> { "A", "B" }, new List<PresetChoice>
                    {
                        new() { Key = "k1", Name = "Alpha", Version = "1.0", Installed = true },
                        new() { Key = "k2", Name = "Gamma", Version = "1.0" },
                    }, new List<PresetLine> { PresetLine.Line("Characters"), new() { Key = "k1", Name = "Alpha", Version = "1.0", Pin = true } });
                    again.Show(form);
                    await Task.Delay(500);
                    again.DrawToBitmap(shot, new Rectangle(Point.Empty, again.Size));
                    shot.Save(Path.Combine(workDir, "editor.png"));
                    again.Close();
                }
            }
            catch (Exception ex)
            {
                Check("check stopped: " + ex.GetType().Name + " " + ex.Message + "\n" + ex.StackTrace, false);
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
