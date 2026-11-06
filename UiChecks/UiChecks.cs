// Checks the real window on a hidden desktop: it cannot take focus, appear on screen, or touch the
// mouse, so the PC stays usable while it runs. Keep this to whatever feature is being worked on.
// Nothing is installed to a real game folder; the work dir holds a throwaway one.
// Usage: UiChecks <empty work dir>
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
            return Run(args[0]);
        }
        return StartOnHiddenDesktop(args[0]);
    }

    /// <summary>Runs this program again with a desktop of its own, which nothing on screen can see or be affected by.</summary>
    static int StartOnHiddenDesktop(string workDir)
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
        var commandLine = new StringBuilder($"\"{Environment.ProcessPath}\" \"{workDir}\" --on-hidden-desktop");
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
            var json = url.Contains("api.github.com")
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

    static int Run(string workDir)
    {
        Manager.IgnoreGameForTests = Manager.GameRunning();
        var standIn = new StandIn();
        NexusApi.UseHandlerForChecks(standIn);
        var root = Path.Combine(workDir, "game");
        Directory.CreateDirectory(Path.Combine(root, "ProjectPBZ"));
        Directory.CreateDirectory(Path.Combine(root, "platform", "steam"));
        var manager = new Manager(root);
        const string old = "Old 1.0 501 1.0 2026-01-01T00-00Z a.zip";
        const string uuepbs = "UUEPBS.zip";
        Zip(Path.Combine(manager.ModDir, old), ("ProjectPBZ/Content/Paks/~mods/Old_P.pak", "o"));
        Zip(Path.Combine(manager.ModDir, uuepbs), ("UUEPBS/Scripts/main.lua", "lua"));
        using (var s = File.OpenRead(Path.Combine(manager.ModDir, uuepbs)))
            standIn.LocalHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s));
        // Notes saved the old way: install steps inside a memo of several lines.
        File.WriteAllText(Path.Combine(manager.StateDir, "meta.json"), System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [old] = new { Registered = true, Memo = "[Nexus 설명의 설치 방법]\nInstallation:\n1. Close the game.", Url = "https://www.nexusmods.com/phantombladezero/mods/501" },
            [uuepbs] = new { Registered = true, Memo = "body slider\nneeds UE4SS", Url = "https://github.com/XTGMods/UUEPBS" },
        }));
        var migrated = manager.LoadMeta();
        Check("old install steps moved out of the memo", migrated[old].Memo == "" && migrated[old].InstallNotes.StartsWith("Installation:"));
        Check("a memo of several lines becomes one", migrated[uuepbs].Memo == "body slider · needs UE4SS");
        manager.Install(manager.BuildPlan(Path.Combine(manager.ModDir, old)));
        manager.Install(manager.BuildPlan(Path.Combine(manager.ModDir, uuepbs)));

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
                await Task.Delay(700);
                var lists = All(form).OfType<DarkListView>().ToList();
                var zipList = lists[0];
                var installedList = lists.First(l => l.Columns[0].Text == "순번");
                ListViewItem Installed(string name) => installedList.Items.Cast<ListViewItem>().First(i => i.SubItems[1].Text == name);
                var link = All(form).OfType<Label>().First(l => l.Text.StartsWith("GitHub · v"));
                typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(link, new object[] { EventArgs.Empty });
                Check("GitHub link opens the project page: " + link.Text, opened.SingleOrDefault() == "https://github.com/GameStdio77/PBZModManager");
                Check("installed list has a memo column", installedList.Columns[3].Text == "메모" && Installed("UUEPBS").SubItems[3].Text == "body slider · needs UE4SS");

                // After the check, UUEPBS's version known from GitHub shows in the installed list too.
                Find(form, "최신 버전 확인").PerformClick();
                var report = await Window<Form>(form, f => f.Text == "최신 버전 확인");
                Find(report, "확인").PerformClick();
                await Task.Delay(400);
                Check("installed list shows the GitHub-known version: " + Installed("UUEPBS").SubItems[2].Text, Installed("UUEPBS").SubItems[2].Text == "(2.7.1)");

                // The info panel: a one-line memo, and install steps kept apart and read-only.
                foreach (ListViewItem i in zipList.Items) i.Selected = i.SubItems[0].Text.Trim() == "Old";
                await Task.Delay(300);
                var boxes = All(form).OfType<TextBox>().ToList();
                var memo = boxes.First(b => !b.Multiline && b.MaxLength == 200);
                var notes = boxes.First(b => b.Multiline && b.ReadOnly);
                Check("install steps shown apart: " + notes.Text.Replace("\r\n", " / "), notes.Text.StartsWith("Installation:"));
                memo.Text = "graphics fix";
                await Task.Delay(300);
                Check("memo saved and shown in the installed list", manager.LoadMeta()[old].Memo == "graphics fix" && Installed("Old").SubItems[3].Text == "graphics fix");
                Find(form, "다시 가져오기").PerformClick();
                await Task.Delay(800);
                Check("fetched again from the page", notes.Text.StartsWith("Installation:") && notes.Text.Contains("1. Step one") && manager.LoadMeta()[old].Memo == "graphics fix");

                // A short info panel folds the steps away; the button opens them in a window.
                var split = All(form).OfType<SplitContainer>().First(s => s.Orientation == Orientation.Horizontal && s.Panel1.Controls.OfType<Control>().Any(c => All(c).Contains(zipList)));
                split.SplitterDistance = split.Height - 230;
                await Task.Delay(400);
                Check("short panel: steps folded, button says so", !notes.Parent!.Visible && Find(form, "설치 방법 보기...").Enabled);
                Find(form, "설치 방법 보기...").PerformClick();
                var window = await Window<Form>(form, f => f.Text == "설치 방법 - Old");
                Check("window shows the steps", All(window).OfType<TextBox>().Single().Text.Contains("1. Step one"));
                Find(window, "닫기").PerformClick();
                await Task.Delay(200);
                split.SplitterDistance = Math.Max(200, split.Height - 520);
                await Task.Delay(400);
                Check("tall panel: steps inline again", notes.Parent!.Visible && Find(form, "크게 보기...").Enabled);
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
}
