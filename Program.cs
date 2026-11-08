using System.Security.Cryptography;
using System.Text;

namespace PBZModManager;

static class Program
{
    static string Title => L.T("PBZ 모드 매니저");

    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // The language comes first: every window and the fonts are made in it.
        L.Use(Manager.SavedLanguage(GameFolder.ModDir));
        Theme.UseDarkSystemMenus();
        RemoveReplacedExe();
        Application.ThreadException += (_, e) => DarkDialog.Show(null, e.Exception.Message, Title, isError: true);

        // Mods and their records live where this program sits; the game folder is saved with the records.
        var modDir = GameFolder.ModDir;
        var i = Array.IndexOf(args, "--root");
        var root = GameFolder.Find(i >= 0 && i + 1 < args.Length ? args[i + 1] : null, modDir) ?? AskForGame();
        if (root == null) return 1;
        GameFolder.ForgetOldPointer(modDir);

        // One instance per mod folder: two managers writing the same records would corrupt them.
        // A restart starts the new copy while the old one is still closing, so it waits a moment.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(modDir.ToLowerInvariant())))[..16];
        using var mutex = new Mutex(false, $@"Local\PBZModManager-{key}");
        bool owned;
        try { owned = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned)
        {
            DarkDialog.Show(null, L.T("모드 매니저가 이미 실행 중입니다."), Title);
            return 1;
        }

        // Files passed on the command line, which is also what dropping them on the exe's icon does.
        var files = args.Where((a, n) => !a.StartsWith("--") && (n == 0 || args[n - 1] != "--root")).ToArray();
        var manager = new Manager(root, modDir);
        try { manager.RememberGameRoot(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* found again next time */ }
        Application.AddMessageFilter(new FullWidthSpaceFilter());
        Application.Run(new MainForm(manager, files));
        mutex.ReleaseMutex();
        return 0;
    }

    /// <summary>Asks for the game folder when it could not be found; null when the user gives up.</summary>
    static string? AskForGame()
    {
        DarkDialog.Show(null, L.T("Phantom Blade Zero 게임 폴더를 찾지 못했습니다.\n다음 창에서 PBZ 폴더(ProjectPBZ와 platform 폴더가 있는 곳)를 골라 주세요."), Title);
        while (true)
        {
            using var picker = new FolderBrowserDialog { Description = L.T("Phantom Blade Zero 게임 폴더 (PBZ)"), UseDescriptionForTitle = true };
            if (picker.ShowDialog() != DialogResult.OK) return null;
            if (GameFolder.IsGameFolder(picker.SelectedPath)) return Path.GetFullPath(picker.SelectedPath);
            DarkDialog.Show(null, L.T("게임 폴더가 아닙니다. ProjectPBZ와 platform 폴더가 있는 PBZ 폴더를 골라 주세요."), Title, isError: true);
        }
    }

    /// <summary>A new build can replace a running exe only by moving it aside; the leftover goes on the next start.</summary>
    static void RemoveReplacedExe()
    {
        if (Environment.ProcessPath is not { } exe) return;
        var old = Path.ChangeExtension(exe, ".old.exe");
        try
        {
            if (File.Exists(old)) File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The old version is still running. It can go next time.
        }
    }
}
