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
                : body.Contains("description") ? """{"data":{"legacyModsByDomain":{"nodes":[{"modCategory":{"name":"Visuals"},"description":"STEPS"}]}}}""".Replace("STEPS", Steps)
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
        const string v1 = "Old 1.0 501 1.0 2026-01-01T00-00Z a.zip";
        File.WriteAllText(Path.Combine(manager.StateDir, "meta.json"), System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [v1] = new { Registered = true, Url = "https://www.nexusmods.com/phantombladezero/mods/501", InstallNotes = "old steps" },
            ["Wuchang Volice 2.zip"] = new { Registered = true, Url = "https://www.nexusmods.com/phantombladezero/mods/260" },
            ["PBZ FemaleAnimation for 2B 385 5 2026-11-06T19-35Z a.zip"] = new { Registered = true, Url = "https://www.nexusmods.com/phantombladezero/mods/385" },
            ["PBZ FemaleAnimation For SoulVerR 385 5 2026-11-06T19-36Z b.zip"] = new { Registered = true, Url = "https://www.nexusmods.com/phantombladezero/mods/385" },
        }));
        Zip(Path.Combine(manager.ModDir, "PBZ FemaleAnimation for 2B 385 5 2026-11-06T19-35Z a.zip"), ("ProjectPBZ/Content/Paks/~mods/Anim_P.pak", "2b"));
        // A main file and its optional file, as Nexus files them; the info was entered for the main one only.
        const string aio = "SoulVerR AIO 414 1.02 2026-11-06T14-04Z a.zip", lite = "SoulVerR Lite Cloth Physics 414 1.00p1 2026-11-04T11-52Z b.zip";
        Zip(Path.Combine(manager.ModDir, aio), ("ProjectPBZ/Content/Paks/~mods/Soul_P.pak", "s"));
        Zip(Path.Combine(manager.ModDir, lite), ("ProjectPBZ/Content/Paks/~mods/SoulCloth_P.pak", "c"));
        Zip(Path.Combine(manager.ModDir, "UUEPBS.zip"), ("UUEPBS/Scripts/main.lua", "lua"));
        var metaNow = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ModMeta>>(File.ReadAllText(Path.Combine(manager.StateDir, "meta.json")))!;
        metaNow[aio] = new ModMeta { Registered = true, Category = "Characters", Url = "https://www.nexusmods.com/phantombladezero/mods/414" };
        metaNow[lite] = new ModMeta { Registered = true };
        metaNow["UUEPBS.zip"] = new ModMeta { Registered = true, Url = "https://github.com/XTGMods/UUEPBS" };
        File.WriteAllText(Path.Combine(manager.StateDir, "meta.json"), System.Text.Json.JsonSerializer.Serialize(metaNow));
        File.WriteAllText(Path.Combine(manager.StateDir, "remote.json"), System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, RemoteInfo>
        {
            [ModIdentity.KeyOfZip(Path.Combine(manager.ModDir, aio))] = new() { Version = "1.02", MatchedFile = true, FileCategory = "MAIN" },
            [ModIdentity.KeyOfZip(Path.Combine(manager.ModDir, lite))] = new() { Version = "1.00p1", MatchedFile = true, FileCategory = "OPTIONAL" },
            [ModIdentity.KeyOfZip(Path.Combine(manager.ModDir, "UUEPBS.zip"))] = new() { Version = "2.7.1", Source = "github", LocalVersion = "2.7.1" },
        }));
        Zip(Path.Combine(manager.ModDir, "PBZ FemaleAnimation For SoulVerR 385 5 2026-11-06T19-36Z b.zip"), ("ProjectPBZ/Content/Paks/~mods/Anim_P.pak", "soul"));
        Zip(Path.Combine(manager.ModDir, "Wuchang Volice 2.zip"), ("ProjectPBZ/Content/Paks/~mods/Voice_P.pak", "v"));
        // The next version, downloaded elsewhere.
        var v2Source = Path.Combine(workDir, "Old 2.0 501 2.0 2026-02-01T00-00Z b.zip");
        Zip(v2Source, ("ProjectPBZ/Content/Paks/~mods/Old_P.pak", "o2"));
        const string v2 = "Old 2.0 501 2.0 2026-02-01T00-00Z b.zip";
        standIn.Steps = "Installation:<br />1. Steps for two";

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
                var notes = All(form).OfType<TextBox>().First(b => b.Multiline && b.ReadOnly);

                // Adding the new version: its steps are fetched for it, the old version keeps its own.
                var adding = (Task)typeof(MainForm).GetMethod("AddZips", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(form, new object[] { new[] { v2Source } })!;
                var offer = await Window<Form>(form, f => Shows(f, "새 버전을 추가했습니다"));
                Find(offer, "아니요").PerformClick();
                await adding;
                var meta = manager.LoadMeta();
                Check("new version got the page's steps, the old one kept its own: " + meta[v2].InstallNotes.Replace("\n", " / "),
                    meta[v2].InstallNotes == "Installation:\n1. Steps for two" && meta[v1].InstallNotes == "old steps");
                Check("the panel shows the newest version's steps", notes.Text.Contains("Steps for two"));
                var versions = All(form).OfType<DarkComboBox>().First(c => c.Items.Cast<object>().Any(i => i.ToString()!.StartsWith("1.0")));
                versions.SelectedIndex = versions.Items.Cast<object>().ToList().FindIndex(i => i.ToString()!.StartsWith("1.0"));
                await Task.Delay(300);
                Check("picking the old version shows its steps: " + notes.Text, notes.Text == "old steps");
                versions.SelectedIndex = 0;
                await Task.Delay(300);

                // The page changes, then the update: the steps are fetched again for the version put in.
                standIn.Steps = "Installation:<br />1. Steps at update time";
                All(form).OfType<Button>().First(b => b.Text.StartsWith("업데이트")).PerformClick();
                var plan = await Window<Form>(form, f => f.Text == "업데이트 확인 - Old");
                Find(plan, "업데이트").PerformClick();
                await Task.Delay(1500);
                Check("steps fetched again after the update: " + manager.LoadMeta()[v2].InstallNotes.Replace("\n", " / "),
                    manager.LoadMeta()[v2].InstallNotes.Contains("Steps at update time") && manager.LoadMeta()[v1].InstallNotes == "old steps"
                    && notes.Text.Contains("Steps at update time"));

                // A mod number alone in the address becomes its page.
                using (var dialog = new ModInfoDialog("New 1.0.zip", new ModMeta(), new List<string>(), false, _ => { }, _ => { }))
                {
                    dialog.ShowInTaskbar = false;
                    dialog.Show(form);
                    await Task.Delay(300);
                    var editor = All(dialog).OfType<ModInfoEditor>().Single();
                    var url = All(editor).OfType<TextBox>().First(b => !b.Multiline && b.MaxLength != 200);
                    url.Text = "300";
                    Check("number taken at once", editor.Url == "https://www.nexusmods.com/phantombladezero/mods/300");
                    await Task.Delay(1500);
                    Check("the field shows the page: " + url.Text, url.Text == "https://www.nexusmods.com/phantombladezero/mods/300");
                    Check("and its steps came: " + editor.InstallNotes.Replace("\n", " / "), editor.InstallNotes.Contains("Steps at update time"));
                    dialog.Close();
                }

                // A file without a version in its name: the user gives one, and the lists use it.
                var zipList = All(form).OfType<DarkListView>().First();
                foreach (ListViewItem i in zipList.Items) i.Selected = i.SubItems[0].Text.Trim() == "Wuchang Volice 2";
                await Task.Delay(400);
                var mine = All(form).OfType<TextBox>().Single(b => b.MaxLength == 40);
                Check("version field shown for a name without a version", mine.Parent!.Parent!.Visible);
                mine.Text = "v1.5";
                await Task.Delay(400);
                var row = zipList.Items.Cast<ListViewItem>().First(i => i.SubItems[0].Text.Trim() == "Wuchang Volice 2");
                Check("saved and listed: " + row.SubItems[1].Text, manager.LoadMeta()["Wuchang Volice 2.zip"].Version == "1.5" && row.SubItems[1].Text == "(1.5)");
                foreach (ListViewItem i in zipList.Items) i.Selected = i.SubItems[0].Text.Trim() == "Old";
                await Task.Delay(400);
                Check("hidden for a name with a version", !mine.Parent!.Parent!.Visible);

                // Several main files of one page: a heading, the files side by side under it.
                var texts = zipList.Items.Cast<ListViewItem>().Select(i => i.SubItems[0].Text).ToList();
                var head = texts.IndexOf("PBZ FemaleAnimation");
                Check("page heading with its files under it: " + string.Join(" | ", texts),
                    head >= 0 && texts[head + 1] == "      for 2B" && texts[head + 2] == "      For SoulVerR"
                    && zipList.Items[head].SubItems[4].Text == "파일 2개" && zipList.Items[head].Tag is PageHeading);
                foreach (ListViewItem i in zipList.Items) i.Selected = i.Index == head;
                await Task.Delay(300);
                Check("the heading is not a mod: nothing to install", !All(form).OfType<Button>().First(b => b.Text.StartsWith("설치")).Enabled);
                // The heading shows the page's shared info only, and an edit there reaches every file.
                var pageEditor = All(form).OfType<ModInfoEditor>().Single();
                var title = All(pageEditor).OfType<Label>().First();
                var pageUrl = All(pageEditor).OfType<TextBox>().First(b => !b.Multiline && b.MaxLength != 200 && b.MaxLength != 40);
                var memoBox = All(pageEditor).OfType<TextBox>().First(b => b.MaxLength == 200);
                Check("heading shows its page: " + title.Text + " / " + pageUrl.Text, title.Text == "PBZ FemaleAnimation" && pageUrl.Text.EndsWith("/mods/385"));
                Check("each file's own fields are hidden", !memoBox.Parent!.Visible && !All(pageEditor).OfType<DarkComboBox>().Any(c => c.Visible && c.Items.Cast<object>().Any(i => i.ToString()!.StartsWith("자동"))));
                pageUrl.Text = "https://www.nexusmods.com/phantombladezero/mods/385?tab=description";
                await Task.Delay(300);
                var after = manager.LoadMeta();
                Check("the edit reached both files", after["PBZ FemaleAnimation for 2B 385 5 2026-11-06T19-35Z a.zip"].Url.EndsWith("?tab=description")
                    && after["PBZ FemaleAnimation For SoulVerR 385 5 2026-11-06T19-36Z b.zip"].Url.EndsWith("?tab=description"));
                // One main file with an optional one: a tree, the main file on top.
                var soul = texts.IndexOf("SoulVerR AIO");
                Check("main and optional file as a tree", soul >= 0 && texts[soul + 1] == "   └ SoulVerR Lite Cloth Physics" && !texts.Contains("SoulVerR"));
                foreach (ListViewItem i in zipList.Items) i.Selected = i.Index == soul + 1;
                await Task.Delay(400);
                var category = All(form).OfType<DarkComboBox>().First(c => c.Items.Contains("Characters") && !c.Items.Contains("전체"));
                var address = All(form).OfType<TextBox>().First(b => b.Text.Contains("nexusmods.com") || b.Text.Length == 0 && !b.Multiline && b.MaxLength != 200 && b.MaxLength != 40);
                Check("the optional file uses the page's info: " + category.SelectedItem + " / " + address.Text,
                    (string)category.SelectedItem! == "Characters" && address.Text == "https://www.nexusmods.com/phantombladezero/mods/414");
                foreach (ListViewItem i in zipList.Items) i.Selected = i.SubItems[0].Text.Trim() == "UUEPBS";
                await Task.Delay(400);
                Check("no version field when GitHub told the version", !mine.Parent!.Parent!.Visible);

                foreach (ListViewItem i in zipList.Items) i.Selected = i.Index == head + 1;
                await Task.Delay(300);
                using (var shot = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(shot, new Rectangle(Point.Empty, form.Size));
                    shot.Save(Path.Combine(workDir, "list.png"));
                }

                // Taking a mod off the list says the file stays, and offers to delete it as well.
                async Task Unlist(string name, string choice)
                {
                    foreach (ListViewItem i in zipList.Items) i.Selected = i.SubItems[0].Text.Trim() == name;
                    await Task.Delay(300);
                    Click(Find(form, "목록에서 빼기"));
                    var ask = await Window<Form>(form, f => f.Text == "목록에서 빼기");
                    Check("says the file stays in .manager\\archives", Shows(ask, ".manager\\archives에 그대로 남습니다"));
                    Find(ask, choice).PerformClick();
                    await Task.Delay(800);
                }
                var voice = Path.Combine(manager.ArchiveDir, "Wuchang Volice 2.zip");
                await Unlist("Wuchang Volice 2", "취소");
                Check("cancel keeps it listed", zipList.Items.Cast<ListViewItem>().Any(i => i.SubItems[0].Text.Trim() == "Wuchang Volice 2"));
                await Unlist("Wuchang Volice 2", "목록에서만 빼기");
                Check("only off the list, the file kept", !zipList.Items.Cast<ListViewItem>().Any(i => i.SubItems[0].Text.Trim() == "Wuchang Volice 2") && File.Exists(voice));
                var soulFile = Path.Combine(manager.ArchiveDir, "PBZ FemaleAnimation For SoulVerR 385 5 2026-11-06T19-36Z b.zip");
                await Unlist("For SoulVerR", "파일도 휴지통으로");
                Check("off the list and the file gone", !zipList.Items.Cast<ListViewItem>().Any(i => i.SubItems[0].Text.Trim() == "For SoulVerR") && !File.Exists(soulFile));
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
