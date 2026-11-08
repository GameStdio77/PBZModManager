// Runs install/rollback against throwaway game folders. Never touches a real game folder.
// Usage: Tests <empty work dir> [real loader zip] [real mod zips...]
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PBZModManager;

Manager.IgnoreGameForTests = Manager.GameRunning();
Manager.DeleteInsteadOfRecycleForTests = true;
var work = Path.GetFullPath(args[0]);
var realZip = args.Length > 1 ? args[1] : null;
var extraZips = args.Skip(2).ToArray();
int failed = 0, passed = 0, caseNo = 0;

void Test(string name, Action<string> body)
{
    // TEST_ONLY=<text> runs just the tests about what changed.
    var only = Environment.GetEnvironmentVariable("TEST_ONLY");
    if (!string.IsNullOrEmpty(only) && !name.Contains(only)) return;

    var root = Path.Combine(work, $"game{++caseNo}");
    try
    {
        MakeGame(root);
        body(root);
        passed++;
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL  {name}\n      {ex.Message.Replace("\n", "\n      ")}");
    }
}

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

void MakeGame(string root)
{
    Write(root, "ProjectPBZ/Binaries/Win64/game.dll", "game");
    Write(root, "platform/steam/PBZ.exe", "exe");
    Write(root, "platform/steam/steam_api64.dll", "steam");
    Directory.CreateDirectory(Path.Combine(root, "[] MOD"));
}

void Write(string root, string rel, string text)
{
    var path = Path.Combine(root, rel.Replace('/', '\\'));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, text);
}

string Zip(string root, string name, params (string Path, string Text)[] entries)
{
    // Where the manager keeps archives.
    Directory.CreateDirectory(Path.Combine(root, "[] MOD", ".manager", "archives"));
    var path = Path.Combine(root, "[] MOD", ".manager", "archives", name);
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var (entryPath, text) in entries)
    {
        var entry = zip.CreateEntry(entryPath);
        if (entryPath.EndsWith('/')) continue;
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(text));
    }
    return path;
}

// Everything in the game folder except the manager's own folder: folders, content hashes and timestamps.
string Snapshot(string root)
{
    var lines = new List<string>();
    var opts = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };
    foreach (var p in Directory.EnumerateFileSystemEntries(root, "*", opts))
    {
        var rel = Path.GetRelativePath(root, p);
        if (rel.StartsWith("[] MOD", StringComparison.OrdinalIgnoreCase)) continue;
        lines.Add(File.Exists(p)
            ? $"{rel} {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))} {File.GetLastWriteTimeUtc(p).Ticks}"
            : $"{rel}\\");
    }
    lines.Sort(StringComparer.OrdinalIgnoreCase);
    return string.Join("\n", lines);
}

void SameTree(string expected, string actual)
{
    if (expected == actual) return;
    var e = expected.Split('\n').ToHashSet();
    var a = actual.Split('\n').ToHashSet();
    throw new Exception("tree differs\n  missing: " + string.Join("\n  ", e.Except(a).Take(10))
        + "\n  unexpected: " + string.Join("\n  ", a.Except(e).Take(10)));
}

Tx Install(Manager m, string zip) => m.Install(m.BuildPlan(zip));
string Read(string root, string rel) => File.ReadAllText(Path.Combine(root, rel.Replace('/', '\\')));
bool Exists(string root, string rel) => File.Exists(Path.Combine(root, rel.Replace('/', '\\')));

if (realZip != null)
    Test("real loader zip: install all files, rollback restores exact tree", root =>
    {
        var zip = Path.Combine(root, "[] MOD", Path.GetFileName(realZip));
        File.Copy(realZip, zip);
        var before = Snapshot(root);
        var m = new Manager(root);

        var plan = m.BuildPlan(zip);
        int fileCount;
        using (var opened = ZipFile.OpenRead(zip)) fileCount = opened.Entries.Count(e => !e.FullName.EndsWith('/'));
        Check(plan.Count(ChangeKind.Add) == fileCount, $"expected only adds, got add={plan.Count(ChangeKind.Add)} over={plan.Count(ChangeKind.Overwrite)} of {fileCount}");
        Check(plan.Count(ChangeKind.Overwrite) == 0, "no overwrites expected on a clean game");
        var tx = m.Install(plan);
        Check(Exists(root, "platform/steam/version.dll"), "version.dll not installed");
        Check(Exists(root, "ProjectPBZ/PBZModLoader/compiler/tsc.exe"), "tsc.exe not installed");
        Check(Directory.Exists(Path.Combine(root, "ProjectPBZ/PBZModLoader/mods")), "empty mods folder not created");
        Check(m.GetFileStatuses(tx).All(s => s.State == "정상"), "fresh install should verify clean");

        // What the loader does at runtime, plus a mod the user added and a config edit.
        Write(root, "ProjectPBZ/Content/JavaScript/PBZModLoader/generated-config.js", "x");
        Write(root, "ProjectPBZ/Content/JavaScript/PBZModLoader/generated-mods/a/main.js", "x");
        Write(root, "ProjectPBZ/PBZModLoader/evidence/run1/loader.log", "log");
        Write(root, "ProjectPBZ/PBZModLoader/mods/my-mod/main.js", "my mod");
        Write(root, "ProjectPBZ/PBZModLoader/mods.ini", "edited");
        Check(m.GetFileStatuses(tx).Single(s => s.File.Path.EndsWith("mods.ini")).State == "변경됨", "edit not detected");

        var rb = m.BuildRollbackPlan(tx.Id);
        Check(rb.ExtraFiles.Count == 4, $"expected 4 extras, got {rb.ExtraFiles.Count}");
        Check(rb.Steps.Single(s => s.Path.EndsWith("mods.ini")).Kind == StepKind.StashDelete, "edited ini should be stashed");

        var leftovers = m.RollbackTo(tx.Id);
        SameTree(before, Snapshot(root));
        Check(m.History().Count == 0, "history should be empty");
        Check(leftovers != null, "leftovers expected");
        Check(File.ReadAllText(Path.Combine(leftovers!, @"ProjectPBZ\PBZModLoader\mods\my-mod\main.js")) == "my mod", "user mod not preserved");
        Check(File.ReadAllText(Path.Combine(leftovers!, @"ProjectPBZ\PBZModLoader\mods.ini")) == "edited", "edited ini not preserved");
        Check(File.Exists(Path.Combine(leftovers!, @"ProjectPBZ\Content\JavaScript\PBZModLoader\generated-config.js")), "cache not preserved");
    });

foreach (var modZip in extraZips)
    Test($"real mod on top of loader: {Path.GetFileName(modZip)[..Math.Min(16, Path.GetFileName(modZip).Length)]}...", root =>
    {
        var m = new Manager(root);
        var loader = Path.Combine(root, "[] MOD", "loader.zip");
        File.Copy(realZip!, loader);
        var mod = Path.Combine(root, "[] MOD", "mod.zip");
        File.Copy(modZip, mod);

        Install(m, loader);
        var afterLoader = Snapshot(root);
        var plan = m.BuildPlan(mod);
        Check(plan.Warnings.Count == 0, "mod should fit the game layout: " + string.Join(";", plan.Warnings));
        Check(plan.Count(ChangeKind.Overwrite) == 0, "mod should only add files");
        var tx = m.Install(plan);

        // A mod writing its own settings next to itself at runtime.
        var modDir = plan.NewDirs.OrderBy(d => d.Length).First();
        Write(root, modDir + "/settings.json", "{}");
        var leftovers = m.RollbackTo(tx.Id);
        SameTree(afterLoader, Snapshot(root));
        Check(File.Exists(Path.Combine(leftovers!, modDir.Replace('/', '\\'), "settings.json")), "settings not kept");
    });

Test("overwritten files are restored with content and timestamp", root =>
{
    Write(root, "platform/steam/version.dll", "other tool");
    File.SetLastWriteTimeUtc(Path.Combine(root, @"platform\steam\version.dll"), new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
    var zip = Zip(root, "a.zip", ("platform/steam/version.dll", "loader"), ("ProjectPBZ/new.txt", "new"));
    var before = Snapshot(root);
    var m = new Manager(root);
    var plan = m.BuildPlan(zip);
    Check(plan.Count(ChangeKind.Overwrite) == 1 && plan.Count(ChangeKind.Add) == 1, "plan kinds wrong");
    var tx = Install(m, zip);
    Check(Read(root, "platform/steam/version.dll") == "loader", "not overwritten");
    m.RollbackTo(tx.Id);
    SameTree(before, Snapshot(root));
});

Test("stacked installs roll back one step at a time, or several at once", root =>
{
    var a = Zip(root, "a.zip", ("f.txt", "A"), ("only-a.txt", "A"));
    var b = Zip(root, "b.zip", ("f.txt", "B"), ("only-b.txt", "B"));
    var c = Zip(root, "c.zip", ("f.txt", "C"));
    var m = new Manager(root);
    var s0 = Snapshot(root);
    var ta = Install(m, a);
    var s1 = Snapshot(root);
    var tb = Install(m, b);
    var s2 = Snapshot(root);
    var tc = Install(m, c);

    Check(m.GetFileStatuses(ta).Single(s => s.File.Path == "f.txt").State == "이후 설치로 덮어씀", "later overwrite not shown");
    m.RollbackTo(tc.Id);
    SameTree(s2, Snapshot(root));
    Check(Read(root, "f.txt") == "B", "should be B");
    var tc2 = Install(m, c);

    var preview = m.BuildRollbackPlan(ta.Id);
    Check(preview.Steps.All(s => s.Kind is StepKind.Delete or StepKind.Restore), "multi-step preview should not see unrelated changes: "
        + string.Join(",", preview.Steps.Select(s => s.Kind)));
    Check(preview.TxIds.SequenceEqual(new[] { tc2.Id, tb.Id, ta.Id }), "newest first expected");
    m.RollbackTo(tb.Id);
    SameTree(s1, Snapshot(root));
    m.RollbackTo(ta.Id);
    SameTree(s0, Snapshot(root));
    Check(m.History().Count == 0, "history should be empty");
});

Test("update keeps the user's mods.ini, rollback brings the old version back", root =>
{
    var v1 = Zip(root, "v1.zip", ("ProjectPBZ/PBZModLoader/mods.ini", "default"), ("ProjectPBZ/PBZModLoader/x.js", "1"));
    var v2 = Zip(root, "v2.zip", ("ProjectPBZ/PBZModLoader/mods.ini", "default2"), ("ProjectPBZ/PBZModLoader/x.js", "2"));
    var m = new Manager(root);
    Install(m, v1);
    Write(root, "ProjectPBZ/PBZModLoader/mods.ini", "mine");
    var plan = m.BuildPlan(v2);
    Check(plan.Count(ChangeKind.Keep) == 1 && plan.Count(ChangeKind.Overwrite) == 1, "keep rule not applied");
    var tx2 = m.Install(plan);
    Check(Read(root, "ProjectPBZ/PBZModLoader/mods.ini") == "mine", "ini was reset");
    Check(Read(root, "ProjectPBZ/PBZModLoader/x.js") == "2", "not updated");
    m.RollbackTo(tx2.Id);
    Check(Read(root, "ProjectPBZ/PBZModLoader/mods.ini") == "mine", "ini lost on rollback");
    Check(Read(root, "ProjectPBZ/PBZModLoader/x.js") == "1", "not rolled back");
});

Test("remove one: a file overwritten by later installs reaches the true original", root =>
{
    Write(root, "f.txt", "orig");
    File.SetLastWriteTimeUtc(Path.Combine(root, "f.txt"), new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
    var s0 = Snapshot(root);
    var m = new Manager(root);
    var ta = Install(m, Zip(root, "a.zip", ("f.txt", "A")));
    var tb = Install(m, Zip(root, "b.zip", ("f.txt", "B")));
    var tc = Install(m, Zip(root, "c.zip", ("f.txt", "C")));

    Check(m.BuildRemovePlan(tb.Id).Steps.Single().Kind == StepKind.KeepForLater, "should defer to later install");
    m.RemoveOne(tb.Id);
    Check(Read(root, "f.txt") == "C", "newest version must stay");
    m.RemoveOne(tc.Id);
    Check(Read(root, "f.txt") == "A", "should fall back to A, skipping removed B");
    m.RemoveOne(ta.Id);
    SameTree(s0, Snapshot(root));
});

Test("remove one: oldest first still ends at the original", root =>
{
    Write(root, "f.txt", "orig");
    var s0 = Snapshot(root);
    var m = new Manager(root);
    var ta = Install(m, Zip(root, "a.zip", ("f.txt", "A")));
    var tb = Install(m, Zip(root, "b.zip", ("f.txt", "B")));
    m.RemoveOne(ta.Id);
    Check(Read(root, "f.txt") == "B", "B must stay");
    Check(m.GetFileStatuses(m.History().Single()).Single().State == "정상", "B should still verify");
    m.RemoveOne(tb.Id);
    SameTree(s0, Snapshot(root));
});

Test("remove one: a new file passes to the later install, which then removes it", root =>
{
    var s0 = Snapshot(root);
    var m = new Manager(root);
    var ta = Install(m, Zip(root, "a.zip", ("new.txt", "A")));
    var tb = Install(m, Zip(root, "b.zip", ("new.txt", "B")));
    m.RemoveOne(ta.Id);
    Check(Read(root, "new.txt") == "B", "B must stay");
    m.RemoveOne(tb.Id);
    SameTree(s0, Snapshot(root));
});

Test("remove one: a file shared unchanged with a later install survives", root =>
{
    var s0 = Snapshot(root);
    var m = new Manager(root);
    var ta = Install(m, Zip(root, "a.zip", ("shared.txt", "same"), ("only-a.txt", "a")));
    var tb = Install(m, Zip(root, "b.zip", ("shared.txt", "same"), ("only-b.txt", "b")));
    Check(tb.Unchanged.Single().Path == "shared.txt", "should record the shared file");

    var again = m.BuildRollbackPlan(tb.Id);
    Check(again.Steps.All(s => s.Path != "shared.txt"), "undoing B must not touch the shared file");

    m.RemoveOne(ta.Id);
    Check(Read(root, "shared.txt") == "same", "shared file deleted while B still needs it");
    Check(!Exists(root, "only-a.txt"), "A's own file should go");
    m.RemoveOne(tb.Id);
    SameTree(s0, Snapshot(root));
});

Test("remove one: removing the old loader keeps the user's mods.ini for the new one", root =>
{
    var m = new Manager(root);
    var v1 = Install(m, Zip(root, "v1.zip", ("ProjectPBZ/PBZModLoader/mods.ini", "default"), ("ProjectPBZ/PBZModLoader/x.js", "1")));
    Write(root, "ProjectPBZ/PBZModLoader/mods.ini", "mine");
    var v2 = Install(m, Zip(root, "v2.zip", ("ProjectPBZ/PBZModLoader/mods.ini", "default2"), ("ProjectPBZ/PBZModLoader/x.js", "2")));
    m.RemoveOne(v1.Id);
    Check(Read(root, "ProjectPBZ/PBZModLoader/mods.ini") == "mine", "ini lost");
    Check(Read(root, "ProjectPBZ/PBZModLoader/x.js") == "2", "new loader changed");
    var leftovers = m.RemoveOne(v2.Id);
    Check(!Exists(root, "ProjectPBZ/PBZModLoader/mods.ini"), "ini should go with the last loader");
    Check(File.ReadAllText(Path.Combine(leftovers!, @"ProjectPBZ\PBZModLoader\mods.ini")) == "mine", "user's ini must be kept aside");
});

Test("remove one: mod inside the loader's folder, either order, keeps folders in use", root =>
{
    var s0 = Snapshot(root);
    var m = new Manager(root);
    var loader = Install(m, Zip(root, "loader.zip", ("ProjectPBZ/PBZModLoader/mods/", ""), ("ProjectPBZ/PBZModLoader/mods.ini", "ini"),
        ("ProjectPBZ/PBZModLoader/compiler/tsc.exe", "tsc"), ("platform/steam/version.dll", "dll"),
        ("ProjectPBZ/Content/JavaScript/PBZModLoader/bootstrap.js", "boot")));
    var modA = Install(m, Zip(root, "a.zip", ("ProjectPBZ/PBZModLoader/mods/a/main.js", "a")));
    var modB = Install(m, Zip(root, "b.zip", ("ProjectPBZ/PBZModLoader/mods/b/main.js", "b")));
    Write(root, "ProjectPBZ/PBZModLoader/mods/a/settings.json", "{}");
    Write(root, "ProjectPBZ/PBZModLoader/mods/my-dev-mod/main.js", "dev");

    // Middle mod: only its own folder goes, with its runtime settings kept aside.
    var preview = m.BuildRemovePlan(modA.Id);
    Check(preview.ExtraFiles.Single().EndsWith("a/settings.json"), "preview should list the settings file");
    Check(!preview.RemovesLoaderUnderOtherMods, "not the loader");
    var left = m.RemoveOne(modA.Id);
    Check(!Directory.Exists(Path.Combine(root, @"ProjectPBZ\PBZModLoader\mods\a")), "mod folder should go");
    Check(Read(root, "ProjectPBZ/PBZModLoader/mods/b/main.js") == "b", "other mod touched");
    Check(Read(root, "ProjectPBZ/PBZModLoader/mods/my-dev-mod/main.js") == "dev", "user's dev mod touched");
    Check(Read(root, "ProjectPBZ/PBZModLoader/mods.ini") == "ini", "loader touched");
    Check(File.Exists(Path.Combine(left!, @"ProjectPBZ\PBZModLoader\mods\a\settings.json")), "settings not kept");

    // Loader under a remaining mod: warned, its own files go, the shared folders stay.
    preview = m.BuildRemovePlan(loader.Id);
    Check(preview.RemovesLoaderUnderOtherMods, "should warn about the loader");
    Check(preview.KeptDirs.Any(d => d.EndsWith("/mods")), "mods folder should be kept");
    m.RemoveOne(loader.Id);
    Check(!Exists(root, "platform/steam/version.dll") && !Exists(root, "ProjectPBZ/PBZModLoader/mods.ini"), "loader files should go");
    Check(!Directory.Exists(Path.Combine(root, @"ProjectPBZ\PBZModLoader\compiler")), "unused folder should go");
    Check(Read(root, "ProjectPBZ/PBZModLoader/mods/b/main.js") == "b", "remaining mod lost");

    // The last install now owns the shared folders, so everything is gone afterwards.
    m.RemoveOne(modB.Id);
    SameTree(s0, Snapshot(root));
    Check(m.History().Count == 0, "history should be empty");
});

Test("remove one: failure keeps it pending, retry and rollback both complete it", root =>
{
    Write(root, "f.txt", "orig");
    var s0 = Snapshot(root);
    var m = new Manager(root);
    var ta = Install(m, Zip(root, "a.zip", ("f.txt", "A"), ("a-locked.txt", "a")));
    var tb = Install(m, Zip(root, "b.zip", ("f.txt", "B")));
    using (new FileStream(Path.Combine(root, "a-locked.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        var threw = false;
        try { m.RemoveOne(ta.Id); } catch (IOException) { threw = true; } catch (UnauthorizedAccessException) { threw = true; }
        Check(threw, "should fail while locked");
    }
    Check(m.Pending()?.Id == ta.Id, "should be pending");
    Check(Read(root, "f.txt") == "B", "B must be intact");

    // Rolling everything back from here must finish the removal and still reach the original.
    m.RollbackTo(tb.Id);
    SameTree(s0, Snapshot(root));
    Check(m.History().Count == 0, "history should be empty");
});

Test("remove one: an interrupted hand-over repeats safely", root =>
{
    Write(root, "f.txt", "orig");
    var s0 = Snapshot(root);
    var m = new Manager(root);
    var ta = Install(m, Zip(root, "a.zip", ("f.txt", "A")));
    var tb = Install(m, Zip(root, "b.zip", ("f.txt", "B")));

    // As if the process died right after copying A's backup over B's, before any record was saved.
    File.Copy(Path.Combine(m.StateDir, @"history\0001\backup\f.txt"), Path.Combine(m.StateDir, @"history\0002\backup\f.txt"), overwrite: true);
    var manifest = Path.Combine(m.StateDir, @"history\0001\manifest.json");
    var doc = JsonSerializer.Deserialize<Tx>(File.ReadAllText(manifest))!;
    doc.State = Tx.Undoing;
    File.WriteAllText(manifest, JsonSerializer.Serialize(doc));

    var reopened = new Manager(root);
    Check(reopened.Pending()?.Id == ta.Id, "pending not detected");
    reopened.RemoveOne(ta.Id);
    Check(Read(root, "f.txt") == "B", "B must stay");
    reopened.RemoveOne(tb.Id);
    SameTree(s0, Snapshot(root));
});

Test("remove one: records written by the previous version still work", root =>
{
    var s0 = Snapshot(root);
    var m = new Manager(root);
    var ta = Install(m, Zip(root, "a.zip", ("ProjectPBZ/mods/", ""), ("ProjectPBZ/mods/a.js", "a")));
    var tb = Install(m, Zip(root, "b.zip", ("ProjectPBZ/mods/b/main.js", "b")));
    foreach (var id in new[] { ta.Id, tb.Id })
    {
        var manifest = Path.Combine(m.StateDir, "history", id.ToString("D4"), "manifest.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
        node.Remove("Unchanged");
        File.WriteAllText(manifest, node.ToJsonString());
    }
    var reopened = new Manager(root);
    Check(reopened.History().All(t => t.Unchanged.Count == 0), "old records should load");
    reopened.RemoveOne(ta.Id);
    Check(Read(root, "ProjectPBZ/mods/b/main.js") == "b", "later mod lost");
    reopened.RemoveOne(tb.Id);
    SameTree(s0, Snapshot(root));
});

Test("notes persist, survive removing the mod, and a damaged file is set aside", root =>
{
    var m = new Manager(root);
    Check(m.LoadMeta().Count == 0, "should start empty");
    var zip = Zip(root, "a.zip", ("f.txt", "A"));
    var meta = m.LoadMeta();
    meta["a.zip"] = new ModMeta { Category = "로더", Url = "https://example.com/mod", Memo = "첫 줄\n둘째 줄" };
    meta["blank.zip"] = new ModMeta();
    m.SaveMeta(meta);

    var tx = Install(m, zip);
    m.RemoveOne(tx.Id);
    var reopened = new Manager(root).LoadMeta();
    Check(reopened["A.ZIP"].Memo == "첫 줄 · 둘째 줄" && reopened["a.zip"].Category == "로더", "notes lost (a memo of several lines is kept as one)");
    Check(!reopened.ContainsKey("blank.zip"), "empty notes should not be stored");

    File.WriteAllText(Path.Combine(m.StateDir, "meta.json"), "{ not json");
    Check(m.LoadMeta().Count == 0, "damaged file should load empty");
    Check(File.Exists(Path.Combine(m.StateDir, "meta.json.broken")), "damaged file should be kept aside");
});

Test("only registered or installed archives are listed; adding validates and copies", root =>
{
    var m = new Manager(root);
    var dev = Zip(root, "dev-template.zip", ("Template/src/main.ts", "x"));
    var kept = Zip(root, "kept.zip", ("kept.txt", "k"));
    var meta = m.LoadMeta();
    Check(m.ListZips(meta).Count == 0, "unregistered files must not be listed");

    var outside = Path.Combine(work, "outside.zip");
    if (File.Exists(outside)) File.Delete(outside);
    using (var z = ZipFile.Open(outside, ZipArchiveMode.Create)) z.CreateEntry("outside.txt");
    var added = m.AddZip(outside);
    Check(added == Path.Combine(m.ArchiveDir, "outside.zip") && File.Exists(added) && !File.Exists(outside), "moved into the archive folder");
    Check(m.AddZip(added) == added, "adding the same file again is fine");
    Check(m.AddZip(kept) == kept, "a file already in the folder stays where it is");

    // Archives an earlier version kept beside the program move in once, names unchanged.
    var old = Path.Combine(m.ModDir, "old place.zip");
    using (var z = ZipFile.Open(old, ZipArchiveMode.Create)) z.CreateEntry("old.txt");
    Check(m.MoveArchivesIn() == 1 && !File.Exists(old) && File.Exists(Path.Combine(m.ArchiveDir, "old place.zip")), "old archives moved in");
    Check(m.MoveArchivesIn() == 0, "nothing left to move");

    meta["outside.zip"] = new ModMeta { Registered = true };
    m.SaveMeta(meta);
    var reloaded = m.LoadMeta();
    Check(m.ListZips(reloaded).Single() == added, "registered file should be listed, the template not");

    // Installed archives stay listed even if never registered, as with older records.
    Install(m, kept);
    Check(m.ListZips(reloaded).Count == 2 && !m.ListZips(reloaded).Contains(dev), "installed file should be listed");

    bool Rejected(string path)
    {
        try { m.AddZip(path); return false; } catch (InvalidOperationException) { return true; }
    }
    var fake = Path.Combine(work, "fake.zip");
    File.WriteAllText(fake, "not a zip");
    var text = Path.Combine(work, "note.txt");
    File.WriteAllText(text, "text");
    Check(Rejected(fake), "a broken zip must be refused");
    Check(Rejected(text), "a non-zip must be refused");
    Check(Rejected(work), "a folder must be refused");

    var otherDir = Directory.CreateDirectory(Path.Combine(work, "other")).FullName;
    var clash = Path.Combine(otherDir, "outside.zip");
    if (File.Exists(clash)) File.Delete(clash);
    using (var z = ZipFile.Open(clash, ZipArchiveMode.Create)) z.CreateEntry("different.txt");
    Check(Rejected(clash), "a different file with the same name must not overwrite");
});

Test("a removed mod stays on the list once it has been installed", root =>
{
    var m = new Manager(root);
    var zip = Zip(root, "a.zip", ("f.txt", "A"));
    var tx = Install(m, zip);
    var meta = m.LoadMeta();
    Check(m.RegisterInstalled(meta), "installed mod should become registered");
    Check(!m.RegisterInstalled(meta), "second pass should change nothing");
    m.SaveMeta(meta);
    m.RemoveOne(tx.Id);
    Check(m.ListZips(m.LoadMeta()).Single() == zip, "mod fell off the list after removal");
});

Test("categories: user-defined, persistent, rename and delete update the mods", root =>
{
    var m = new Manager(root);
    var meta = m.LoadMeta();
    Check(m.LoadCategories(meta).Count == 0, "should start with none; no presets");

    var cats = new List<string> { "지도", "편의성" };
    meta["a.zip"] = new ModMeta { Category = "지도", Registered = true };
    meta["b.zip"] = new ModMeta { Category = "편의성", Registered = true };
    m.SaveCategories(cats);
    m.SaveMeta(meta);
    var again = m.LoadCategories(m.LoadMeta());
    Check(again.SequenceEqual(cats), "order should persist");

    Check(Manager.CategoryNameProblem("", cats) != null, "empty name");
    Check(Manager.CategoryNameProblem("지도", cats) != null, "duplicate name");
    Check(Manager.CategoryNameProblem("미분류", cats) != null && Manager.CategoryNameProblem("전체", cats) != null, "reserved names");
    Check(Manager.CategoryNameProblem("지도", cats, renaming: "지도") == null, "keeping a name while renaming is fine");
    Check(Manager.CategoryNameProblem("외형", cats) == null, "new name");

    Manager.RenameCategory(meta, cats, "지도", "맵");
    Check(cats[0] == "맵" && meta["a.zip"].Category == "맵", "rename must reach the mods");
    Manager.DeleteCategory(meta, cats, "편의성");
    Check(!cats.Contains("편의성") && meta["b.zip"].Category == "" && meta["b.zip"].Registered, "delete must only uncategorize");

    // A category only a note knows about, as older notes have, joins the list instead of vanishing.
    meta["c.zip"] = new ModMeta { Category = "개발 도구", Registered = true };
    Check(m.LoadCategories(meta).Contains("개발 도구"), "used category should be listed");

    File.WriteAllText(Path.Combine(m.StateDir, "categories.json"), "{ not json");
    Check(m.LoadCategories(new Dictionary<string, ModMeta>()).Count == 0, "damaged file should load empty");
    Check(File.Exists(Path.Combine(m.StateDir, "categories.json.broken")), "damaged file should be kept aside");
});

Test("versions: real file names parse, and identity follows the contents", _ =>
{
    var loader = ModIdentity.ParseFileName("PBZModLoader 0.3.6-preview - Game update fix 113 0.3.6-preview 2026-11-04T09-44Z btcAfdDbw.zip");
    Check(loader.Name == "PBZModLoader" && loader.Version == "0.3.6-preview", $"loader: {loader}");
    var tracker = ModIdentity.ParseFileName("Map Collectible Tracker 0.6.7 187 0.6.7 2026-11-04T13-06Z KnCjDVtQt.zip");
    Check(tracker.Name == "Map Collectible Tracker" && tracker.Version == "0.6.7" && tracker.Stamp == "2026-11-04T13-06Z", $"tracker: {tracker}");
    var markers = ModIdentity.ParseFileName("PBZMapMarkers 1.8.0 Fog 134 1.8.0 2026-11-04T13-02Z kMrBiHa5d.zip");
    Check(markers.Name == "PBZMapMarkers" && markers.Version == "1.8.0", $"markers: {markers}");
    // The title carries the SDK version; the version of the file itself is the one after the page id.
    var template = ModIdentity.ParseFileName("PBZModLoader TS Dev Template - SDK 0.3.3 113 0.2.0 2026-11-04T11-13Z vB84Zu5v4.zip");
    Check(template.Version == "0.2.0" && template.Name == "PBZModLoader TS Dev Template - SDK", $"template: {template}");
    var plain = ModIdentity.ParseFileName("CoolMod-v1.2.zip");
    Check(plain.Name == "CoolMod" && plain.Version == "1.2", $"plain: {plain}");
    var bare = ModIdentity.ParseFileName("CoolMod.zip");
    Check(bare.Name == "CoolMod" && bare.Version == "", $"bare: {bare}");

    Check(ModIdentity.CompareVersions("0.6.10", "0.6.9") > 0, "numbers must not compare as text");
    Check(ModIdentity.CompareVersions("0.3.6", "0.3.6-preview") > 0, "release is newer than its pre-release");
    Check(ModIdentity.CompareVersions("1.2", "1.2.0") == 0, "missing parts count as zero");
    Check(ModIdentity.CompareVersions("v1.3", "1.2.9") > 0, "leading v is ignored");
    Check(ModIdentity.CompareFiles("M 1 1.0 2026-11-05T00-00Z a.zip", "M 1 1.0 2026-11-04T00-00Z b.zip") > 0, "same version: later upload is newer");

    Check(ModIdentity.KeyOf(new[] { "ProjectPBZ/PBZModLoader/mods/a/main.js" }, "x 1.0.zip")
        == ModIdentity.KeyOf(new[] { "ProjectPBZ/PBZModLoader/mods/A/other.js" }, "renamed entirely 2.0.zip"), "same folder means same mod");
    Check(ModIdentity.KeyOf(new[] { "ProjectPBZ/Content/JavaScript/PBZModLoader/bootstrap.js" }, "x.zip") == ModIdentity.LoaderKey, "loader by its marker");
    Check(ModIdentity.KeyOf(new[] { "ProjectPBZ/Paks/a.pak" }, "PakMod 1.0.zip") == ModIdentity.KeyOf(new[] { "ProjectPBZ/Paks/b.pak" }, "PakMod 2.0.zip"), "no mods folder: falls back to the name");
    Check(ModIdentity.KeyOf(new[] { "ProjectPBZ/Paks/a.pak" }, "One 1.0.zip") != ModIdentity.KeyOf(new[] { "ProjectPBZ/Paks/a.pak" }, "Two 1.0.zip"), "different names differ");
});

const string ModRoot = "ProjectPBZ/PBZModLoader/mods/m/";

Test("update: stale files go, settings stay, undo returns the old version exactly", root =>
{
    Directory.CreateDirectory(Path.Combine(root, @"ProjectPBZ\PBZModLoader\mods"));
    var s0 = Snapshot(root);
    var m = new Manager(root);
    var v1 = Install(m, Zip(root, "M 1.0.zip", (ModRoot + "main.ts", "old ts"), (ModRoot + "lib.js", "lib1"), (ModRoot + "same.js", "same"), (ModRoot + "old/only.js", "o")));
    Write(root, ModRoot + "settings.json", "{mine}");
    var s1 = Snapshot(root);

    var plan = m.BuildPlan(Zip(root, "M 2.0.zip", (ModRoot + "main.js", "new js"), (ModRoot + "lib.js", "lib2"), (ModRoot + "same.js", "same")));
    Check(plan.ReplacesZip == "M 1.0.zip", "should be recognized as an update");
    Check(plan.Stale.Select(s => s.Path).OrderBy(p => p).SequenceEqual(new[] { ModRoot + "main.ts", ModRoot + "old/only.js" }), "stale: " + string.Join(",", plan.Stale.Select(s => s.Path)));
    Check(plan.Count(ChangeKind.Overwrite) == 1 && plan.Count(ChangeKind.Add) == 1 && plan.Count(ChangeKind.Same) == 1, "kinds wrong");

    var v2 = m.Install(plan);
    Check(!Exists(root, ModRoot + "main.ts"), "leftover main.ts would win over the new main.js");
    Check(Read(root, ModRoot + "main.js") == "new js" && Read(root, ModRoot + "lib.js") == "lib2", "new files missing");
    Check(Read(root, ModRoot + "settings.json") == "{mine}", "settings must survive an update");
    Check(m.GetFileStatuses(v2).Single(s => s.File.Path.EndsWith("main.ts")).State == "새 버전에 없어 제거됨", "deleted file status");

    m.RemoveOne(v2.Id);
    SameTree(s1, Snapshot(root));
    Check(m.History().Single().Id == v1.Id, "only the update should be undone");
    m.RemoveOne(v1.Id);
    Check(!Directory.Exists(Path.Combine(root, ModRoot.Replace('/', '\\'))), "mod folder should go");
    SameTree(s0, Snapshot(root));
});

Test("update: removing the whole mod clears every version and keeps others", root =>
{
    Directory.CreateDirectory(Path.Combine(root, @"ProjectPBZ\PBZModLoader\mods"));
    var s0 = Snapshot(root);
    var m = new Manager(root);
    var v1 = Install(m, Zip(root, "M 1.0.zip", (ModRoot + "main.ts", "t"), (ModRoot + "a.js", "1")));
    var other = Install(m, Zip(root, "Other 1.0.zip", ("ProjectPBZ/PBZModLoader/mods/other/main.js", "o")));
    var v2 = Install(m, Zip(root, "M 2.0.zip", (ModRoot + "main.js", "j"), (ModRoot + "a.js", "2"), (ModRoot + "b.js", "b")));
    Write(root, ModRoot + "settings.json", "{}");

    var ids = new[] { v1.Id, v2.Id };
    var preview = m.BuildRemovePlan(ids);
    var byPath = preview.Steps.ToDictionary(s => s.Path, s => s.Kind);
    Check(preview.Steps.Select(s => s.Path).Distinct().Count() == preview.Steps.Count, "each file should appear once");
    Check(byPath[ModRoot + "a.js"] == StepKind.Delete && byPath[ModRoot + "main.js"] == StepKind.Delete, "preview: " + string.Join(",", byPath));
    Check(preview.ExtraFiles.Single().EndsWith("settings.json"), "settings should be listed for keeping aside");

    var left = m.RemoveMany(ids);
    Check(!Directory.Exists(Path.Combine(root, ModRoot.Replace('/', '\\'))), "mod folder should go");
    Check(Read(root, "ProjectPBZ/PBZModLoader/mods/other/main.js") == "o", "other mod touched");
    Check(File.Exists(Path.Combine(left!, ModRoot.Replace('/', '\\'), "settings.json")), "settings not kept");
    Check(m.History().Single().Id == other.Id, "other install should remain");
    m.RemoveOne(other.Id);
    SameTree(s0, Snapshot(root));
});

Test("update: another mod's file is left alone, and a downgrade works the same way", root =>
{
    var m = new Manager(root);
    Install(m, Zip(root, "M 1.0.zip", (ModRoot + "main.js", "1"), (ModRoot + "shared-with-other.js", "s"), ("shared.txt", "s"), (ModRoot + "extra.js", "e")));
    var other = Install(m, Zip(root, "Other 1.0.zip", ("ProjectPBZ/PBZModLoader/mods/other/main.js", "o"), ("shared.txt", "s")));
    Check(other.Unchanged.Single().Path == "shared.txt", "should share");

    var plan = m.BuildPlan(Zip(root, "M 2.0.zip", (ModRoot + "main.js", "2")));
    Check(plan.Stale.All(s => s.Path != "shared.txt"), "must not remove a file the other mod shares");
    var v2 = m.Install(plan);
    Check(Read(root, "shared.txt") == "s", "shared file removed");
    Check(!Exists(root, ModRoot + "extra.js"), "stale file should go");

    // Going back to the old archive brings its files back and removes what only 2.0 had.
    var down = m.Install(m.BuildPlan(Path.Combine(root, "[] MOD", ".manager", "archives", "M 1.0.zip")));
    Check(Read(root, ModRoot + "main.js") == "1" && Read(root, ModRoot + "extra.js") == "e", "downgrade should restore 1.0 files");
    m.RemoveOne(down.Id);
    Check(Read(root, ModRoot + "main.js") == "2" && !Exists(root, ModRoot + "extra.js"), "undoing the downgrade returns to 2.0");
});

Test("update: edited files are flagged, backed up, and return on undo", root =>
{
    var m = new Manager(root);
    Install(m, Zip(root, "M 1.0.zip", (ModRoot + "main.js", "1"), (ModRoot + "config.js", "default"), (ModRoot + "gone.js", "default")));
    Write(root, ModRoot + "gone.js", "edited");
    var plan = m.BuildPlan(Zip(root, "M 2.0.zip", (ModRoot + "main.js", "2"), (ModRoot + "config.js", "default2")));
    Check(plan.Stale.Single().Modified, "edited stale file should be flagged");
    var v2 = m.Install(plan);
    Check(!Exists(root, ModRoot + "gone.js"), "stale removed");
    m.RemoveOne(v2.Id);
    Check(Read(root, ModRoot + "gone.js") == "edited", "edit lost on undo");
});

Test("update: failure part way restores the old version", root =>
{
    var m = new Manager(root);
    Install(m, Zip(root, "M 1.0.zip", (ModRoot + "main.js", "1"), (ModRoot + "zz-stale.js", "s")));
    var s1 = Snapshot(root);
    var plan = m.BuildPlan(Zip(root, "M 2.0.zip", (ModRoot + "main.js", "2")));
    using (new FileStream(Path.Combine(root, ModRoot.Replace('/', '\\'), "zz-stale.js"), FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        var failed = false;
        try { m.Install(plan); } catch (InvalidOperationException) { failed = true; }
        Check(failed, "locked stale file should fail the update");
    }
    SameTree(s1, Snapshot(root));
    Check(m.History().Count == 1 && m.Pending() == null, "history should be back to one install");
});

Test("update: removing the old install under a newer one keeps the new version working", root =>
{
    Directory.CreateDirectory(Path.Combine(root, @"ProjectPBZ\PBZModLoader\mods"));
    var s0 = Snapshot(root);
    var m = new Manager(root);
    var v1 = Install(m, Zip(root, "M 1.0.zip", (ModRoot + "main.js", "1"), (ModRoot + "stale.js", "s")));
    var v2 = Install(m, Zip(root, "M 2.0.zip", (ModRoot + "main.js", "2")));
    m.RemoveOne(v1.Id);
    Check(Read(root, ModRoot + "main.js") == "2" && !Exists(root, ModRoot + "stale.js"), "new version must stay as it is");
    m.RemoveOne(v2.Id);
    SameTree(s0, Snapshot(root));
});

Test("mod list: versions group by mod, newest first, with update detection", root =>
{
    var m = new Manager(root);
    var meta = m.LoadMeta();
    void Reg(string path) => meta[Path.GetFileName(path)] = new ModMeta { Registered = true };
    var v1 = Zip(root, "M 0.9.zip", (ModRoot + "main.js", "1"));
    var v10 = Zip(root, "M 0.10.zip", (ModRoot + "main.js", "2"));
    var renamed = Zip(root, "Totally Renamed 0.11.zip", (ModRoot + "main.js", "3"));
    var other = Zip(root, "Other 5.0.zip", ("ProjectPBZ/PBZModLoader/mods/other/main.js", "o"));
    var ignored = Zip(root, "M 9.9.zip", (ModRoot + "main.js", "9"));
    foreach (var p in new[] { v1, v10, renamed, other }) Reg(p);

    var groups = m.ListMods(meta);
    Check(groups.Count == 2, "expected two mods, got " + groups.Count);
    var g = groups.Single(x => x.Key == "mods:m");
    Check(g.Versions.Select(x => x.Version).SequenceEqual(new[] { "0.11", "0.10", "0.9" }), "order: " + string.Join(",", g.Versions.Select(x => x.Version)));
    Check(g.InstalledFile == null && !g.UpdateAvailable, "not installed yet");

    Install(m, v10);
    g = m.ListMods(meta).Single(x => x.Key == "mods:m");
    Check(g.InstalledVersion == "0.10" && g.UpdateAvailable, "0.11 should be offered");
    Install(m, renamed);
    g = m.ListMods(meta).Single(x => x.Key == "mods:m");
    Check(g.InstalledVersion == "0.11" && !g.UpdateAvailable && g.Installs.Count == 2, "newest installed: nothing to offer");
    Check(!m.ListMods(meta).SelectMany(x => x.Versions).Any(x => x.Path == ignored), "unregistered file must stay hidden");
});

Test("installed list: one entry per mod, in the user's order, which survives changes", root =>
{
    var m = new Manager(root);
    var a1 = Install(m, Zip(root, "A 1.0.zip", ("ProjectPBZ/PBZModLoader/mods/a/main.js", "1")));
    var b = Install(m, Zip(root, "B 1.0.zip", ("ProjectPBZ/PBZModLoader/mods/b/main.js", "1")));
    var c = Install(m, Zip(root, "C 1.0.zip", ("ProjectPBZ/PBZModLoader/mods/c/main.js", "1")));
    var a2 = Install(m, Zip(root, "A 2.0.zip", ("ProjectPBZ/PBZModLoader/mods/a/main.js", "2")));
    string Names() => string.Join(",", m.ListInstalled().Select(x => x.Name));

    Check(Names() == "A,B,C", "install order by first install: " + Names());
    var a = m.ListInstalled()[0];
    Check(a.Version == "2.0" && a.Installs.Count == 2, "one entry showing the version in effect");

    m.MoveInstalled("mods:c", -1);
    Check(Names() == "A,C,B", "moved up: " + Names());
    m.MoveInstalled("mods:a", 1);
    Check(Names() == "C,A,B", "moved down: " + Names());
    m.MoveInstalled("mods:c", -1);
    m.MoveInstalled("mods:b", 1);
    Check(Names() == "C,A,B", "moving past the ends does nothing: " + Names());
    Check(string.Join(",", new Manager(root).ListInstalled().Select(x => x.Name)) == "C,A,B", "order should persist");

    var d = Install(m, Zip(root, "D 1.0.zip", ("ProjectPBZ/PBZModLoader/mods/d/main.js", "1")));
    Check(Names() == "C,A,B,D", "a new mod goes last: " + Names());
    Install(m, Zip(root, "A 3.0.zip", ("ProjectPBZ/PBZModLoader/mods/a/main.js", "3")));
    Check(Names() == "C,A,B,D", "an update keeps its place: " + Names());
    m.RemoveMany(m.ListInstalled().Single(x => x.Key == "mods:a").Installs.Select(t => t.Id).ToList());
    Check(Names() == "C,B,D", "removed mod leaves the order: " + Names());
    m.RemoveOne(c.Id);
    m.MoveInstalled("mods:d", -1);
    Check(Names() == "D,B", "order works after removals: " + Names());

    var status = m.GetModFileStatuses(m.ListInstalled().Single(x => x.Key == "mods:d").Installs);
    Check(status.Single().State == "정상", "files of the mod");
});

Test("installed list: file view merges a mod's versions, newest record wins", root =>
{
    var m = new Manager(root);
    Install(m, Zip(root, "M 1.0.zip", (ModRoot + "main.js", "1"), (ModRoot + "same.js", "s"), (ModRoot + "old.js", "o")));
    Install(m, Zip(root, "M 2.0.zip", (ModRoot + "main.js", "2"), (ModRoot + "same.js", "s")));
    var statuses = m.GetModFileStatuses(m.ListInstalled().Single().Installs);
    var byName = statuses.ToDictionary(s => s.File.Path[ModRoot.Length..], s => s.State);
    Check(byName.Count == 3, "each path once: " + string.Join(",", byName.Keys));
    Check(byName["main.js"] == "정상" && byName["same.js"] == "정상", "current files read as normal: " + string.Join(",", byName));
    Check(byName["old.js"] == "새 버전에 없어 제거됨", "removed file: " + byName["old.js"]);
});

Test("nexus: the page comes from the address, else from the download's name", _ =>
{
    var download = new[] { "PBZModLoader 1.0.0 113 1.0.0 2026-11-05T02-59Z PUXCTwgEu.zip" };
    Check(NexusApi.LinkFor("", download) == new NexusLink("phantombladezero", "113"), "page number from the file name");
    Check(NexusApi.LinkFor("https://www.nexusmods.com/phantombladezero/mods/134?tab=files", download) == new NexusLink("phantombladezero", "134"), "the address wins");
    Check(NexusApi.LinkFor("HTTPS://NEXUSMODS.COM/OtherGame/mods/7", download)?.Game == "othergame", "address is case-insensitive");
    Check(NexusApi.LinkFor("", new[] { "CoolMod-v1.2.zip" }) == null, "no page number: cannot be checked");
    Check(NexusApi.LinkFor("https://example.com/mods/9", new[] { "CoolMod.zip" }) == null, "other sites are not Nexus");
    Check(NexusApi.LinkFor("", download)!.PageUrl == "https://www.nexusmods.com/phantombladezero/mods/113", "page address");
});

Test("nexus: the right file decides, from real page contents", _ =>
{
    // As returned for page 113: a loader, plus a template and a demo with their own numbering.
    var loaderPage = new List<NexusFile>
    {
        new("PBZModLoader 0.3.6-preview - Game update fix", "0.3.6-preview", "ARCHIVED"),
        new("PBZModLoader 0.3.4-preview - DLL alternatives", "0.3.4-preview", "OLD_VERSION"),
        new("PBZModLoader MOD Demo", "0.1.2", "OLD_VERSION"),
        new("PBZModLoader 1.0.0", "1.0.0", "MAIN"),
        new("PBZModLoader TS Dev Template - SDK 0.3.3", "0.2.0", "OPTIONAL"),
    };
    Check(NexusApi.ChooseVersion("PBZModLoader", loaderPage, "1.0.0") == ("1.0.0", true), "loader");
    Check(NexusApi.ChooseVersion("PBZModLoader TS Dev Template - SDK", loaderPage, "1.0.0") == ("0.2.0", true), "the template must not read as 1.0.0");

    // As returned for page 134: a separate 2.0.0 line for another framework on the same page.
    var markersPage = new List<NexusFile>
    {
        new("PBZMapMarkers 1.8.0 Fog", "1.8.0", "ARCHIVED"),
        new("UE4SS version 2.0.0", "2.0.0", "OPTIONAL"),
        new("Map Collectible Markers", "1.8.1", "MAIN"),
        new("PBZMapMarkers 1.8.1 Fog", "1.8.1", "MAIN"),
        new("PBZMapMarkers 1.8.1 Fog Teleport", "1.8.1", "OPTIONAL"),
    };
    Check(NexusApi.ChooseVersion("PBZMapMarkers", markersPage, "1.8.1") == ("1.8.1", true), "must not offer the unrelated 2.0.0");

    // A page whose name differs from the file's: Map Tracker Plus carries Map Collectible Tracker.
    var trackerPage = new List<NexusFile> { new("Map Collectible Tracker 0.6.5", "0.6.5", "OLD_VERSION"), new("Map Collectible Tracker 0.6.10", "0.6.10", "MAIN"), new("Map Collectible Tracker 0.6.7", "0.6.7", "OLD_VERSION") };
    Check(NexusApi.ChooseVersion("Map Collectible Tracker", trackerPage, "0.6.10") == ("0.6.10", true), "numeric comparison");

    Check(NexusApi.ChooseVersion("Renamed Mod", trackerPage, "9.9") == ("9.9", false), "no matching file: falls back, flagged as the page's version");
    var archivedOnly = new List<NexusFile> { new("Gone 5.0", "5.0", "ARCHIVED") };
    Check(NexusApi.ChooseVersion("Gone", archivedOnly, "1.0") == ("1.0", false), "a withdrawn file must not count");
});

Test("nexus: responses parse, unknown mods are left out, results persist", root =>
{
    const string json = """
    {"data":{"legacyModsByDomain":{"nodes":[
      {"modId":113,"version":"1.0.0","status":"published"},
      {"modId":55,"version":null,"status":"removed"}]},
     "m113":[{"name":"PBZModLoader 1.0.0","version":"1.0.0","category":"MAIN"},{"name":"PBZModLoader 0.9","version":null,"category":"OLD_VERSION"}],
     "m55":[],
     "m999":[]}}
    """;
    var parsed = NexusApi.ParseResponse(json, new List<(string, string, string, string)>
    {
        ("loader", "PBZModLoader", "PBZModLoader 0.9.zip", "113"), ("gone", "Gone", "Gone 1.zip", "55"), ("unknown", "Nope", "Nope 1.zip", "999"),
    });
    Check(parsed["loader"].Version == "1.0.0" && parsed["loader"].MatchedFile && parsed["loader"].Status == "published", "loader parsed");
    Check(!parsed.ContainsKey("gone") && !parsed.ContainsKey("unknown"), "no version known: left out");

    var m = new Manager(root);
    Check(m.LoadRemote().Count == 0, "starts empty");
    m.SaveRemote(parsed);
    Check(new Manager(root).LoadRemote()["LOADER"].Version == "1.0.0", "should persist");
    File.WriteAllText(Path.Combine(m.StateDir, "remote.json"), "{ not json");
    Check(m.LoadRemote().Count == 0 && File.Exists(Path.Combine(m.StateDir, "remote.json.broken")), "damaged file set aside");
});

const string MenuMods = "ProjectPBZ/Content/JavaScript/PBZModsMenu/Mods/";
const string LoaderMods = "ProjectPBZ/PBZModLoader/mods/";
const string MenuRoot = "ProjectPBZ/Content/JavaScript/PBZModsMenu/";

Test("menu layout: a Mods Menu mod installs to the loader's folder, updates and leaves no trace", root =>
{
    Write(root, "ProjectPBZ/Content/JavaScript/PBZModLoader/bootstrap.js", "loader");
    var before = Snapshot(root);
    var m = new Manager(root);
    var v1 = Zip(root, "Glow 1.0.zip", ("Glow-extra.cfg", "r"), (MenuMods, ""), (MenuMods + "Glow/", ""), (MenuMods + "Glow/main.js", "one"), (MenuMods + "Glow/old.js", "old"));

    var plan = m.BuildPlan(v1);
    Check(plan.Relocated.Count == 2 && plan.Warnings.Any(w => w.Contains("Mods Menu")), "the preview says files move");
    Check(plan.Items.All(i => !i.Path.Contains("PBZModsMenu")) && plan.NewDirs.All(d => !d.Contains("PBZModsMenu")), "nothing is planned in Mods Menu's folder");
    var first = m.Install(plan);
    Check(File.ReadAllText(Path.Combine(root, LoaderMods, "Glow", "main.js")) == "one", "main.js is in the loader's folder");
    Check(!Directory.Exists(Path.Combine(root, "ProjectPBZ/Content/JavaScript/PBZModsMenu")), "no Mods Menu folder appears");
    Check(File.Exists(Path.Combine(root, "Glow-extra.cfg")), "other files stay where the archive puts them");
    Check(m.ListInstalled().Single().Key == "mods:glow" && ModIdentity.KeyOfZip(v1) == "mods:glow", "archive and install are the same mod");

    var v2 = Zip(root, "Glow 2.0.zip", ("Glow-extra.cfg", "r"), (MenuMods + "Glow/main.js", "two"));
    var update = m.BuildPlan(v2);
    Check(update.ReplacesZip == "Glow 1.0.zip" && update.Stale.Single().Path == LoaderMods + "Glow/old.js", "an update knows the old version and its leftover");
    var second = m.Install(update);
    Check(File.ReadAllText(Path.Combine(root, LoaderMods, "Glow", "main.js")) == "two" && !File.Exists(Path.Combine(root, LoaderMods, "Glow", "old.js")), "update applied");

    m.RemoveOne(second.Id);
    m.RemoveOne(first.Id);
    Check(Snapshot(root) == before, "removing returns the folder exactly");
});

Test("menu layout: what stays as it is", root =>
{
    Write(root, "ProjectPBZ/Content/JavaScript/PBZModLoader/bootstrap.js", "loader");
    var m = new Manager(root);
    var both = Zip(root, "Both 1.0.zip", (LoaderMods + "Both/main.js", "x"), (MenuMods + "Both/main.js", "x"), (MenuMods + "Both/README.txt", "x"));
    var plan = m.BuildPlan(both);
    Check(plan.SkippedCopies == 2 && plan.Relocated.Count == 0 && plan.Items.Single().Path == LoaderMods + "Both/main.js", "both packaged: only the loader's copy");

    var odd = Zip(root, "Odd 1.0.zip", (MenuMods + "bad name/main.js", "x"), (MenuMods + "loose.js", "x"), ("ProjectPBZ/Content/JavaScript/PBZModsMenu/menu.js", "x"));
    plan = m.BuildPlan(odd);
    Check(plan.Relocated.Count == 0 && plan.Items.Count == 3 && plan.Items.All(i => i.Path.Contains("PBZModsMenu")), "not a usable loader ID, or Mods Menu's own files: left alone");
});

Test("menu layout: reinstalling over an install made before this existed moves it", root =>
{
    Write(root, "ProjectPBZ/Content/JavaScript/PBZModLoader/bootstrap.js", "loader");
    var menuFile = MenuMods + "Glow/main.js";
    var zip = Zip(root, "Glow 1.0.zip", (menuFile, "one"));
    Write(root, menuFile, "one");
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("one")));
    var m = new Manager(root);
    var dir = Path.Combine(m.StateDir, "history", "0001");
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(new Tx
    {
        Id = 1, ZipName = "Glow 1.0.zip", Time = DateTime.Now, State = Tx.Installed,
        Files = new() { new TxFile { Path = menuFile, InstalledHash = hash } },
        CreatedDirs = new() { "ProjectPBZ/Content", "ProjectPBZ/Content/JavaScript", "ProjectPBZ/Content/JavaScript/PBZModsMenu", "ProjectPBZ/Content/JavaScript/PBZModsMenu/Mods", "ProjectPBZ/Content/JavaScript/PBZModsMenu/Mods/Glow" },
    }));

    var plan = m.BuildPlan(zip);
    Check(plan.ReplacesZip == "Glow 1.0.zip" && plan.Stale.Single().Path == menuFile, "the old record is the same mod, its file goes");
    var second = m.Install(plan);
    Check(File.Exists(Path.Combine(root, LoaderMods, "Glow", "main.js")) && !File.Exists(Path.Combine(root, menuFile)), "the file moved");
    Check(m.ListInstalled().Single().Installs.Count == 2, "one mod with two installs");

    m.RemoveOne(second.Id);
    Check(File.Exists(Path.Combine(root, menuFile)) && !File.Exists(Path.Combine(root, LoaderMods, "Glow", "main.js")), "undoing it puts the old place back");
});

Test("menu layout: with Mods Menu present, or the loader absent, nothing moves", root =>
{
    var m = new Manager(root);
    var glow = Zip(root, "Glow 1.0.zip", (MenuMods + "Glow/main.js", "x"));
    var tracker = Zip(root, "Tracker 1.0.zip", (LoaderMods + "tracker/main.js", "x"));
    Check(m.BuildPlan(glow).Items.Single().Path == MenuMods + "Glow/main.js" && m.BuildPlan(glow).Relocated.Count == 0, "no runtime: as packaged");

    Write(root, "ProjectPBZ/Content/JavaScript/PBZModLoader/bootstrap.js", "loader");
    Write(root, "ProjectPBZ/Content/JavaScript/PBZModsMenu/bootstrap.js", "menu");
    Check(m.BuildPlan(glow).Items.Single().Path == MenuMods + "Glow/main.js", "Mods Menu present: a Mods Menu mod stays in its folder");
    Check(m.BuildPlan(tracker).Items.Single().Path == LoaderMods + "tracker/main.js", "Mods Menu present: a loader mod stays where Mods Menu also runs it");

    File.Delete(Path.Combine(root, "ProjectPBZ/Content/JavaScript/PBZModsMenu/bootstrap.js"));
    var menu = Zip(root, "Mods Menu 0.8.1.zip", (MenuRoot + "bootstrap.js", "b"), (MenuMods + "PBZModsMenu/main.js", "m"));
    var plan = m.BuildPlan(menu);
    Check(plan.Relocated.Count == 0 && plan.Items.Any(i => i.Path == MenuMods + "PBZModsMenu/main.js"), "Mods Menu itself never moves, even beside the loader");
});

Test("ue4ss: a package made for PBZ.exe's folder goes there", root =>
{
    var before = Snapshot(root);
    var m = new Manager(root);
    var top = Zip(root, "UE4SS For Phantom Blade Zero 1.0.3.zip", ("dwmapi.dll", "d"), ("ue4ss/", ""), ("ue4ss/UE4SS.dll", "u"), ("ue4ss/UE4SS-settings.ini", "s1"), ("ue4ss/Mods/mods.txt", "m1"));
    var plan = m.BuildPlan(top);
    Check(plan.Items.Select(i => i.Path).OrderBy(p => p).SequenceEqual(new[] { "platform/steam/dwmapi.dll", "platform/steam/ue4ss/Mods/mods.txt", "platform/steam/ue4ss/UE4SS-settings.ini", "platform/steam/ue4ss/UE4SS.dll" }), "at the archive's top: beside PBZ.exe");
    Check(plan.PlacedBesideGame == 4 && plan.Warnings.Any(w => w.Contains("platform/steam")) && plan.Warnings.All(w => !w.Contains("최상위")), "the preview says where");
    var first = m.Install(plan);

    // The user's settings survive an update, and undoing everything leaves no trace.
    File.WriteAllText(Path.Combine(root, "platform/steam/ue4ss/UE4SS-settings.ini"), "mine");
    var next = Zip(root, "UE4SS For Phantom Blade Zero 1.0.4.zip", ("dwmapi.dll", "d2"), ("ue4ss/UE4SS.dll", "u2"), ("ue4ss/UE4SS-settings.ini", "s2"), ("ue4ss/Mods/mods.txt", "m2"));
    var update = m.BuildPlan(next);
    Check(update.ReplacesZip != null && update.Items.Single(i => i.Path.EndsWith("UE4SS-settings.ini")).Kind == ChangeKind.Keep, "settings kept on update");
    var second = m.Install(update);
    Check(File.ReadAllText(Path.Combine(root, "platform/steam/ue4ss/UE4SS-settings.ini")) == "mine" && File.ReadAllText(Path.Combine(root, "platform/steam/dwmapi.dll")) == "d2", "update applied around them");
    m.RemoveOne(second.Id);
    File.WriteAllText(Path.Combine(root, "platform/steam/ue4ss/UE4SS-settings.ini"), "s1");
    m.RemoveOne(first.Id);
    Check(Snapshot(root) == before, "removing returns the folder exactly");

    var wrapped = Zip(root, "UE4SS wrapped.zip", ("UE4SS 1.0.3/", ""), ("UE4SS 1.0.3/dwmapi.dll", "d"), ("UE4SS 1.0.3/ue4ss/UE4SS.dll", "u"));
    Check(m.BuildPlan(wrapped).Items.All(i => i.Path.StartsWith("platform/steam/")), "inside a wrapping folder: the same");
    var proper = Zip(root, "UE4SS proper.zip", ("platform/steam/dwmapi.dll", "d"), ("platform/steam/ue4ss/UE4SS.dll", "u"));
    Check(m.BuildPlan(proper).PlacedBesideGame == 0, "already laid out for the game folder: as packaged");
    var notUe4ss = Zip(root, "Other.zip", ("dwmapi.dll", "d"), ("readme.txt", "r"));
    Check(m.BuildPlan(notUe4ss).Items.All(i => !i.Path.StartsWith("platform/")), "dwmapi.dll alone is not UE4SS");
});

Test("menu layout: real archives", root =>
{
    var m = new Manager(root);
    foreach (var real in extraZips)
    {
        var copy = Path.Combine(root, "[] MOD", Path.GetFileName(real));
        File.Copy(real, copy);
        var name = Path.GetFileName(real);
        var plan = m.BuildPlan(copy);
        if (name.StartsWith("Mods Menu"))
        {
            Write(root, "ProjectPBZ/Content/JavaScript/PBZModLoader/bootstrap.js", "loader");
            plan = m.BuildPlan(copy);
            Check(plan.Relocated.Count == 0 && plan.Items.Any(i => i.Path == MenuMods + "PBZModsMenu/main.js") && plan.Items.Any(i => i.Path == "platform/steam/hid.dll"), "Mods Menu: as packaged, even beside the loader");
            File.Delete(Path.Combine(root, "ProjectPBZ/Content/JavaScript/PBZModLoader/bootstrap.js"));
        }
        else if (name.StartsWith("Brighter"))
        {
            Check(plan.Items.Any(i => i.Path == MenuMods + "PBZLootGlow/main.js"), "Loot Glow without the loader: as packaged");
            Write(root, "ProjectPBZ/Content/JavaScript/PBZModLoader/bootstrap.js", "loader");
            Check(m.BuildPlan(copy).Items.Any(i => i.Path == LoaderMods + "PBZLootGlow/main.js"), "Loot Glow with only the loader: moved");
            File.Delete(Path.Combine(root, "ProjectPBZ/Content/JavaScript/PBZModLoader/bootstrap.js"));
        }
        else Check(plan.Relocated.Count == 0 && plan.SkippedCopies == 0, $"{name}: as packaged");
        Console.WriteLine($"      {name}: {plan.Items.Count} files, moved {plan.Relocated.Count}, skipped {plan.SkippedCopies}");
    }
});

Test("loose layout: a whole archive wrapped in one folder updates an option mod", root =>
{
    var m = new Manager(root);
    var v101 = Zip(root, "Outfit AIO 414 1.01 2026-11-04T10-23Z abc.zip",
        ("Outfit-00-Body(choose one)-01-a/Outfit_Body_P.pak", "body a"), ("Outfit-00-Body(choose one)-02-b/Outfit_Body_P.pak", "body b"),
        ("UUEPBS Presets/Default.json", "old"));
    m.Install(m.BuildPlan(v101, new[] { "Outfit-00-Body(choose one)-02-b" }));

    // 1.02 as its author packed it: one folder around everything, LogicMods inside each option, presets laid out again.
    var v102 = Zip(root, "Outfit AIO 414 1.02 2026-11-06T14-04Z def.zip",
        ("Outfit AIO v1.02/", ""), ("Outfit AIO v1.02/00-leg options.jpg", "img"),
        ("Outfit AIO v1.02/Outfit-00-Body(choose one)-01-stockings/LogicMods/Outfit_Body_P.pak", "new a"),
        ("Outfit AIO v1.02/Outfit-00-Body(choose one)-02-side/LogicMods/Outfit_Body_P.pak", "new b"),
        ("Outfit AIO v1.02/Outfit-00-Body(choose one)-03-bare/LogicMods/Outfit_Body_P.pak", "new c"),
        ("Outfit AIO v1.02/UUEPBS Presets/", ""), ("Outfit AIO v1.02/UUEPBS Presets/platform/steam/UUEPBS Presets/Default.json", "new"));
    Check(m.LayoutChange(v102) is { } why && why.Contains("옵션 구성이 바뀌어"), "a version laid out anew is told apart: " + m.LayoutChange(v102));
    Check(m.LayoutChange(v101) == null, "the installed version itself is no change");
    var v103 = Zip(root, "Outfit AIO 414 1.03 2026-11-07T14-04Z ghi.zip",
        ("Outfit-00-Body(choose one)-01-a/Outfit_Body_P.pak", "a3"), ("Outfit-00-Body(choose one)-02-b/Outfit_Body_P.pak", "b3"),
        ("UUEPBS Presets/Default.json", "3"));
    Check(m.LayoutChange(v103) == null, "an ordinary update is no change");
    var groups = m.OptionGroups(v102);
    Check(groups.Count == 1 && groups[0].Options.Count == 3 && groups[0].Options[1].Folder == "Outfit-00-Body(choose one)-02-side",
        "the options inside the wrapping folder are found: " + string.Join(" | ", groups.SelectMany(g => g.Options).Select(o => o.Folder)));
    var plan = m.BuildPlan(v102, new[] { "Outfit-00-Body(choose one)-02-side" });
    var paths = plan.Items.Select(i => i.Path).OrderBy(p => p).ToList();
    Check(paths.SequenceEqual(new[] { "platform/steam/UUEPBS Presets/Default.json", "ProjectPBZ/Content/Paks/LogicMods/Outfit_Body_P.pak" }.OrderBy(p => p)),
        "the picked option goes to LogicMods, presets beside the game: " + string.Join(", ", paths));
    Check(plan.Stale.Any(s => s.Path == "ProjectPBZ/Content/Paks/~mods/Outfit_Body_P.pak") && plan.ReplacesZip != null, "the old ~mods file goes with the update");
    Check(plan.NewDirs.All(d => !d.Contains("v1.02") && !d.Contains("choose one")), "no wrapping or option folders are made: " + string.Join(", ", plan.NewDirs));
    m.Install(plan);
    Check(File.ReadAllText(Path.Combine(root, "ProjectPBZ/Content/Paks/LogicMods/Outfit_Body_P.pak")) == "new b"
        && !File.Exists(Path.Combine(root, "ProjectPBZ/Content/Paks/~mods/Outfit_Body_P.pak"))
        && File.ReadAllText(Path.Combine(root, "platform/steam/UUEPBS Presets/Default.json")) == "new", "updated in place");
});

Test("loose layout: options, paks, a UE4SS mod and its presets", root =>
{
    var before = Snapshot(root);
    var m = new Manager(root);
    var zip = Zip(root, "Outfit AIO 414 1.01 2026-11-04T10-23Z abc.zip",
        ("00-body options.jpg", "img"),
        ("Outfit-00-Body(choose one)-01-a/", ""), ("Outfit-00-Body(choose one)-01-a/Outfit_Body_P.pak", "body a"), ("Outfit-00-Body(choose one)-01-a/Outfit_Body_P.utoc", "toc a"),
        ("Outfit-00-Body(choose one)-02-b/Outfit_Body_P.pak", "body b"), ("Outfit-00-Body(choose one)-02-b/Outfit_Body_P.utoc", "toc b"),
        ("Outfit-01-Cloth-X(choose one)-01/Outfit_Cloth_P.pak", "cloth x1"),
        ("Outfit-02-Cloth-Y(choose one)-01/Outfit_Cloth_P.pak", "cloth y1"),
        ("UUEPBS Presets/Default.json", "{}"));

    var groups = m.OptionGroups(zip);
    Check(groups.Count == 2 && groups[0].Options.Count == 2 && groups[1].Options.Count == 2, $"two sets found by the files they share: {groups.Count}");
    Check(groups[0].SharedFiles.SequenceEqual(new[] { "Outfit_Body_P.pak", "Outfit_Body_P.utoc" }) && groups[0].Options[1].Label == "Outfit-00-Body -02-b", $"set details: {groups[0].Options[1].Label}");
    Check(groups[1].Options.Select(o => o.Folder).SequenceEqual(new[] { "Outfit-01-Cloth-X(choose one)-01", "Outfit-02-Cloth-Y(choose one)-01" }), "different labels, same file: one set");

    var plan = m.BuildPlan(zip, new[] { "Outfit-00-Body(choose one)-02-b", "Outfit-02-Cloth-Y(choose one)-01" });
    var paths = plan.Items.Select(i => i.Path).OrderBy(p => p).ToList();
    Check(paths.SequenceEqual(new[]
    {
        "platform/steam/UUEPBS Presets/Default.json",
        "ProjectPBZ/Content/Paks/~mods/Outfit_Body_P.pak",
        "ProjectPBZ/Content/Paks/~mods/Outfit_Body_P.utoc",
        "ProjectPBZ/Content/Paks/~mods/Outfit_Cloth_P.pak",
    }.OrderBy(p => p)), "chosen files only, where the game reads them: " + string.Join(", ", paths));
    Check(plan.NewDirs.All(d => !d.Contains("choose one")), "no option folders are made");
    Check(plan.Warnings.Any(w => w.Contains("~mods")) && plan.Warnings.Any(w => w.Contains("고르지 않은 옵션 3개") && w.Contains("설명 그림 1개")) && plan.Warnings.All(w => !w.Contains("최상위")), "the preview says what goes where");

    var first = m.Install(plan);
    Check(File.ReadAllText(Path.Combine(root, "ProjectPBZ/Content/Paks/~mods/Outfit_Body_P.pak")) == "body b", "picked body installed");
    Check(m.PreviousOptions(zip).SequenceEqual(plan.Chosen), "the choice is kept with the install");
    Check(m.BuildPlan(zip).Chosen.SequenceEqual(plan.Chosen), "a later plan starts from the last choice");
    var bodyOnly = m.BuildPlan(zip, new[] { "Outfit-00-Body(choose one)-02-b" });
    Check(bodyOnly.Items.All(i => !i.Path.Contains("Cloth")) && bodyOnly.Chosen.Count == 1, "a set with nothing picked is not installed");

    // Switching an option is a reinstall that swaps the files.
    var swap = m.BuildPlan(zip, new[] { "Outfit-00-Body(choose one)-01-a", "Outfit-02-Cloth-Y(choose one)-01" });
    Check(swap.ReplacesZip != null && swap.Items.Count(i => i.Kind == ChangeKind.Overwrite) == 2, "switching overwrites the body files");
    var second = m.Install(swap);
    Check(File.ReadAllText(Path.Combine(root, "ProjectPBZ/Content/Paks/~mods/Outfit_Body_P.pak")) == "body a", "switched");
    m.RemoveOne(second.Id);
    Check(File.ReadAllText(Path.Combine(root, "ProjectPBZ/Content/Paks/~mods/Outfit_Body_P.pak")) == "body b", "undoing the switch restores the old option");
    m.RemoveOne(first.Id);
    Check(Snapshot(root) == before, "removing returns the folder exactly");

    var lua = Zip(root, "UUEPBS.zip", ("UUEPBS/", ""), ("UUEPBS/enabled.txt", ""), ("UUEPBS/native/", ""), ("UUEPBS/native/UUEPBS.dll", "dll"), ("UUEPBS/Scripts/main.lua", "lua"));
    plan = m.BuildPlan(lua);
    Check(plan.Items.All(i => i.Path.StartsWith("platform/steam/ue4ss/Mods/UUEPBS/")) && plan.Items.Count == 3, "a UE4SS mod goes into UE4SS's Mods folder");
    Check(plan.Ue4ssMods.Single() == "UUEPBS" && plan.Warnings.Any(w => w.Contains("UE4SS가 설치되어 있지 않습니다")), "says UE4SS is missing");
    Check(plan.NewDirs.Contains("platform/steam/ue4ss/Mods/UUEPBS/native"), "its folders come along");

    var plain = Zip(root, "Plain.zip", ("ProjectPBZ/Content/Paks/~mods/Plain_P.pak", "p"), ("readme.jpg", "i"));
    plan = m.BuildPlan(plain);
    Check(plan.Options.Count == 0 && plan.Items.Any(i => i.Path == "readme.jpg") && plan.Items.Any(i => i.Path == "ProjectPBZ/Content/Paks/~mods/Plain_P.pak"), "an archive laid out for the game is left as it is");
});

Test("exclude: unticked files are not installed, the choice carries over, and dropping an installed one removes it", root =>
{
    var before = Snapshot(root);
    var m = new Manager(root);
    var v1 = Zip(root, "Picky 1.0 302 1.0 2026-01-01T00-00Z a.zip", ("ProjectPBZ/Content/Paks/~mods/Picky_P.pak", "p1"), ("Picky-extra.cfg", "r"), ("LICENSE", "l"));
    var plan = m.BuildPlan(v1, excluded: new[] { "Picky-extra.cfg" });
    Check(plan.Excluded.SequenceEqual(new[] { "Picky-extra.cfg" }) && plan.Items.All(i => i.Path != "Picky-extra.cfg"), "left out of the plan");
    var first = m.Install(plan);
    Check(!File.Exists(Path.Combine(root, "Picky-extra.cfg")) && File.Exists(Path.Combine(root, "LICENSE")), "left out of the game folder");
    Check(m.PreviousExcluded(v1).SequenceEqual(new[] { "Picky-extra.cfg" }), "kept with the install");

    var v2 = Zip(root, "Picky 2.0 302 2.0 2026-02-01T00-00Z b.zip", ("ProjectPBZ/Content/Paks/~mods/Picky_P.pak", "p2"), ("Picky-extra.cfg", "r2"), ("LICENSE", "l"));
    var update = m.BuildPlan(v2);
    Check(update.Excluded.SequenceEqual(new[] { "Picky-extra.cfg" }), "an update keeps the choice");

    var dropLicense = m.BuildPlan(v2, excluded: new[] { "Picky-extra.cfg", "LICENSE" });
    Check(dropLicense.Stale.Any(s => s.Path == "LICENSE"), "an installed file now unticked is removed");
    var second = m.Install(dropLicense);
    Check(!File.Exists(Path.Combine(root, "LICENSE")), "removed");

    m.RemoveOne(second.Id);
    m.RemoveOne(first.Id);
    Check(Snapshot(root) == before, "removing returns the folder exactly");
});

Test("github: repository address, releases and the local file told by its checksum", _ =>
{
    var link = GitHubApi.LinkFor("https://github.com/XTGMods/UUEPBS/releases");
    Check(link == new GitHubLink("XTGMods", "UUEPBS"), "address read");
    Check(GitHubApi.LinkFor("https://github.com/a/b.git") == new GitHubLink("a", "b") && GitHubApi.LinkFor("https://nexusmods.com/x") == null, "other forms");
    const string json = """
    [ {"tag_name":"v3.0-beta","prerelease":true,"draft":false,"html_url":"https://github.com/o/r/releases/tag/v3.0-beta","assets":[]},
      {"tag_name":"2.7.1","prerelease":false,"draft":false,"html_url":"https://github.com/o/r/releases/tag/2.7.1","assets":[{"digest":"sha256:AAAA"}]},
      {"tag_name":"v2.6.0","prerelease":false,"draft":false,"html_url":"https://github.com/o/r/releases/tag/v2.6.0","assets":[{"digest":"sha256:bbbb"}]} ]
    """;
    var info = GitHubApi.ParseReleases(json, "BBBB", new GitHubLink("o", "r"))!;
    Check(info.Version == "2.7.1" && info.Source == "github" && info.PageUrl.EndsWith("/tag/2.7.1"), "newest published release, not the pre-release");
    Check(info.LocalVersion == "2.6.0", "local file found among older releases: " + info.LocalVersion);
    Check(GitHubApi.ParseReleases(json, "CCCC", new GitHubLink("o", "r"))!.LocalVersion == "", "an unknown file has no version");
    Check(GitHubApi.ParseReleases("[]", null, new GitHubLink("o", "r")) == null, "no releases");
});

Test("github: live UUEPBS", _ =>
{
    foreach (var real in extraZips.Where(z => Path.GetFileName(z).StartsWith("UUEPBS")))
    {
        var info = GitHubApi.Check(new GitHubLink("XTGMods", "UUEPBS"), real)!;
        Console.WriteLine($"      latest {info.Version}, local file is {(info.LocalVersion.Length > 0 ? info.LocalVersion : "unknown")}, page {info.PageUrl}");
        Check(info.Version.Length > 0, "a version came back");
    }
});

Test("identity: one Nexus page and name is one mod, even across tools", _ =>
{
    var oldJs = ModIdentity.KeyOf(new[] { "ProjectPBZ/PBZModLoader/mods/pbz-map-tracker/main.js" }, "Map Collectible Tracker 0.6.7 187 0.6.7 2026-11-04T13-06Z KnCjDVtQt.zip");
    var newLua = ModIdentity.KeyOf(new[] { "platform/steam/ue4ss/Mods/PbzMapTracker/Scripts/main.lua" }, "Map Collectible Tracker 1.0.3 187 1.0.3 2026-11-06T06-34Z KnCjDVtXl.zip");
    Check(oldJs == newLua, $"{oldJs} / {newLua}");
    var aio = ModIdentity.KeyOf(new[] { "a/x.pak" }, "SoulVerR AIO 414 1.01 2026-11-04T10-23Z a.zip");
    var lite = ModIdentity.KeyOf(new[] { "b/y.pak" }, "SoulVerR Lite Cloth Physics 414 1.00p1 2026-11-04T11-52Z b.zip");
    Check(aio != lite, "other mods on the same page stay apart");
    Check(ModIdentity.KeyOf(new[] { "ProjectPBZ/Content/JavaScript/PBZModLoader/bootstrap.js" }, "PBZModLoader 1.0.0 113 1.0.0 2026-11-05T02-59Z a.zip") == ModIdentity.LoaderKey, "the loader keeps its own identity");
});

Test("identity: updating to a version for another tool removes the old tool's files", root =>
{
    var m = new Manager(root);
    var js = Zip(root, "Tracker 0.6 187 0.6 2026-01-01T00-00Z a.zip", ("ProjectPBZ/PBZModLoader/mods/tracker/main.js", "js"));
    var lua = Zip(root, "Tracker 1.0 187 1.0 2026-02-01T00-00Z b.zip", ("platform/steam/ue4ss/Mods/Tracker/Scripts/main.lua", "lua"));
    Install(m, js);
    var plan = m.BuildPlan(lua);
    Check(plan.ReplacesZip != null && plan.Stale.Single().Path == "ProjectPBZ/PBZModLoader/mods/tracker/main.js", "the JS version is replaced and its file goes");
    m.Install(plan);
    var mods = m.ListMods(new Dictionary<string, ModMeta>());
    Check(mods.Count == 1 && mods[0].Versions.Count == 2 && m.ListInstalled().Single().Installs.Count == 2, "one mod with two versions and two installs");
});

Test("missing file: stays on the list, marked, with the mod it was", root =>
{
    var m = new Manager(root);
    var zip = Zip(root, "Gone 1.0 300 1.0 2026-01-01T00-00Z a.zip", ("ProjectPBZ/Content/Paks/~mods/Gone_P.pak", "g"));
    Install(m, zip);
    var meta = new Dictionary<string, ModMeta> { [Path.GetFileName(zip)] = new() { Registered = true } };
    File.Delete(zip);
    var mod = m.ListMods(meta).Single();
    Check(mod.Versions.Single().Missing && mod.InstalledFile != null, "listed as missing, still installed");
    Check(mod.Key == m.ListInstalled().Single().Key, "same mod as its install");
});

Test("delete file: an installed version is kept, others go to the Recycle Bin", root =>
{
    var m = new Manager(root);
    var old = Zip(root, "Keep 1.0 301 1.0 2026-01-01T00-00Z a.zip", ("ProjectPBZ/Content/Paks/~mods/Keep_P.pak", "1"));
    var newer = Zip(root, "Keep 2.0 301 2.0 2026-02-01T00-00Z b.zip", ("ProjectPBZ/Content/Paks/~mods/Keep_P.pak", "2"));
    Install(m, newer);
    var refused = false;
    try { m.DeleteZip(Path.GetFileName(newer)); } catch (InvalidOperationException) { refused = true; }
    Check(refused && File.Exists(newer), "the installed version's file is kept");
    m.DeleteZip(Path.GetFileName(old));
    Check(!File.Exists(old), "an old version is gone from the folder");
});

Test("loose layout: real archives", root =>
{
    var m = new Manager(root);
    foreach (var real in extraZips)
    {
        var copy = Path.Combine(root, "[] MOD", Path.GetFileName(real));
        File.Copy(real, copy, true);
        var groups = m.OptionGroups(copy);
        var plan = m.BuildPlan(copy);
        var tops = plan.Items.Select(i => string.Join("/", i.Path.Split('/').Take(4))).Distinct().ToList();
        Console.WriteLine($"      {Path.GetFileName(real)}: {groups.Count} option sets ({string.Join(", ", groups.Select(g => g.Options.Count + " options sharing " + string.Join("+", g.SharedFiles)))}), {plan.Items.Count} files → {string.Join(" | ", tops)}");
        foreach (var w in plan.Warnings) Console.WriteLine($"        ! {w}");
        if (Path.GetFileName(real).StartsWith("SoulVerR"))
        {
            Check(groups.Count == 3 && groups.Sum(g => g.Options.Count) == 22, "SoulVerR: Body, Black Scarf cloth and Sanguine cloth sets over 22 folders");
            Check(plan.Items.All(i => i.Path.StartsWith("ProjectPBZ/Content/Paks/~mods/") || i.Path.StartsWith("platform/steam/UUEPBS Presets/")), "SoulVerR: paks and presets only");
        }
        if (Path.GetFileName(real).StartsWith("UUEPBS"))
            Check(plan.Items.All(i => i.Path.StartsWith("platform/steam/ue4ss/Mods/UUEPBS/")), "UUEPBS: into UE4SS's Mods");
    }
});

Test("runtime: the chosen loader decides where script mods go, and installed ones can follow", root =>
{
    var m = new Manager(root);
    var both = Zip(root, "Both 1.0.zip", (LoaderMods + "Both/main.js", "l"), (MenuMods + "Both/main.js", "m"));
    var menuOnly = Zip(root, "Glow 1.0.zip", (MenuMods + "Glow/main.js", "g"));

    m.SetRuntime(ScriptRuntime.Menu);
    var plan = m.BuildPlan(both);
    Check(plan.Items.Single().Path == MenuMods + "Both/main.js" && plan.SkippedCopies == 1 && plan.Warnings.Any(w => w.Contains("모드 로더용 복사본")), "Mods Menu: only its copy");
    var glowTx = m.Install(m.BuildPlan(menuOnly));
    Check(File.Exists(Path.Combine(root, MenuMods, "Glow", "main.js")), "Mods Menu: a Mods Menu mod stays");

    m.SetRuntime(ScriptRuntime.Loader);
    Check(new Manager(root).Runtime == ScriptRuntime.Loader, "the choice is saved");
    Check(m.BuildPlan(both).Items.Single().Path == LoaderMods + "Both/main.js", "PBZModLoader: only its copy");
    var moves = m.PlansToFollowRuntime();
    Check(moves.Count == 1 && moves[0].Plan.Relocated.Count == 1 && moves[0].Plan.Stale.Single().Path == MenuMods + "Glow/main.js", "the installed Mods Menu mod is offered a move");
    m.Install(moves[0].Plan);
    Check(File.Exists(Path.Combine(root, LoaderMods, "Glow", "main.js")) && !File.Exists(Path.Combine(root, MenuMods, "Glow", "main.js")), "moved");
    Check(m.PlansToFollowRuntime().Count == 0, "nothing more to move");

    m.SetRuntime(ScriptRuntime.Auto);
    Check(m.TargetRuntime == ScriptRuntime.Auto, "automatic with neither installed: as packaged");
    Write(root, "ProjectPBZ/Content/JavaScript/PBZModsMenu/bootstrap.js", "menu");
    Check(m.TargetRuntime == ScriptRuntime.Menu, "automatic follows what is installed");

    Write(root, "ProjectPBZ/Content/JavaScript/PBZModLoader/bootstrap.js", "loader");
    Check(m.TargetRuntime == ScriptRuntime.Menu, "automatic with both installed: Mods Menu, which runs the other's mods too");
    var menu = Zip(root, "Mods Menu 0.8.1.zip", (MenuRoot + "bootstrap.js", "b2"));
    Check(m.BuildPlan(menu).Warnings.Any(w => w.Contains("둘 다 두어도 됩니다")) && m.BuildPlan(menu).Warnings.All(w => !w.Contains("제거해 주세요")),
        "installing one loader beside the other says both may stay");
    Check(m.BuildPlan(both).Items.Single().Path == MenuMods + "Both/main.js", "a mod packaged for both goes in once");
});

Test("tool: what an archive is made for", _ =>
{
    Check(ModIdentity.ToolOf(new[] { "ProjectPBZ/PBZModLoader/mods/a/main.js" }) == "PBZModLoader", "loader mod");
    Check(ModIdentity.ToolOf(new[] { MenuMods + "a/main.js" }) == "Mods Menu", "menu mod");
    Check(ModIdentity.ToolOf(new[] { MenuMods + "a/main.js", LoaderMods + "a/main.js" }) == "PBZModLoader · Mods Menu", "both");
    Check(ModIdentity.ToolOf(new[] { "UUEPBS/Scripts/main.lua" }) == "UE4SS" && ModIdentity.ToolOf(new[] { "platform/steam/ue4ss/Mods/X/Scripts/main.lua" }) == "UE4SS", "UE4SS mod");
    Check(ModIdentity.ToolOf(new[] { "dwmapi.dll", "ue4ss/UE4SS.dll" }) == "UE4SS 본체", "UE4SS itself");
    Check(ModIdentity.ToolOf(new[] { MenuRoot + "bootstrap.js", MenuMods + "PBZModsMenu/main.js" }) == "Mods Menu 본체", "Mods Menu itself");
    Check(ModIdentity.ToolOf(new[] { "Body(choose one)/Body_P.pak" }) == "pak" && ModIdentity.ToolOf(new[] { "readme.txt" }) == "", "pak, and nothing known");
});

Test("loose layout: Blueprint paks go to LogicMods", root =>
{
    var m = new Manager(root);
    var zip = Zip(root, "Blueprint.zip", ("LogicMods/BP_Mod.pak", "b"), ("Plain_P.pak", "p"));
    var paths = m.BuildPlan(zip).Items.Select(i => i.Path).OrderBy(p => p, StringComparer.Ordinal).ToList();
    Check(paths.SequenceEqual(new[] { "ProjectPBZ/Content/Paks/LogicMods/BP_Mod.pak", "ProjectPBZ/Content/Paks/~mods/Plain_P.pak" }.OrderBy(p => p, StringComparer.Ordinal)), string.Join(", ", paths));
});

Test("install notes: the install section of a description", _ =>
{
    const string description = "[center][img]https://x/y.png[/img][/center]<br />Intro text<br /><br />[b]Installation:[/b]<br />1. install the [url=https://a]Mod Manager[/url]<br />2.install this mod<br />choose 1 Body option<br /><br />Uninstall:<br />delete it<br />====中文====<br />安装方法：";
    var notes = InstallNotes.Extract(description);
    Check(notes == "Installation:\n1. install the Mod Manager\n2.install this mod\nchoose 1 Body option", "got: " + notes.Replace("\n", " / "));
    Check(InstallNotes.Extract("Just a mod. Have fun.") == "", "no install section: nothing");
    Check(InstallNotes.Extract("[list][*]Install: drop it in[/list]<br />Credits") == "• Install: drop it in", "a short list line starts it");
    // A description in two languages, like YoRHa 2B's: the steps in the program's language, else English, ending at a divider.
    const string twoLanguages = "【中文说明】<br />■ 安装教程<br />[*]打开游戏目录[/*]<br />[*]启动游戏。[/*]<br />-------------------------<br />视频Video：https://v<br />【English Description】<br />Replace Soul.<br />■ Features<br />• Hair<br />■ Installation<br />1. Fully close the game.<br />4. Launch the game.";
    Check(InstallNotes.Extract(twoLanguages) == "■ Installation\n1. Fully close the game.\n4. Launch the game.", "Korean program, no Korean steps: English ones: " + InstallNotes.Extract(twoLanguages).Replace("\n", " / "));
    L.Use("zh");
    Check(InstallNotes.Extract(twoLanguages) == "■ 安装教程\n• 打开游戏目录\n• 启动游戏。", "Chinese program: Chinese steps, up to the divider: " + InstallNotes.Extract(twoLanguages).Replace("\n", " / "));
    L.Use("ko");
    Check(NexusApi.FullAddress("300") == "https://www.nexusmods.com/phantombladezero/mods/300" && NexusApi.FullAddress(" 0300 ") == "https://www.nexusmods.com/phantombladezero/mods/300"
        && NexusApi.FullAddress("abc") == "abc" && NexusApi.FullAddress("https://github.com/a/b") == "https://github.com/a/b", "a mod number alone is its page");
    // A long install section, like PBZModLoader's, comes whole: 60 steps of about 90 characters each.
    var steps = Enumerable.Range(1, 60).Select(i => $"Step {i}: put the files where the loader reads them, then start the game and check the log.");
    var whole = InstallNotes.Extract("Installation<br />" + string.Join("<br />", steps) + "<br />Credits<br />me");
    Check(whole.Split('\n').Length == 61 && whole.EndsWith("Step 60: put the files where the loader reads them, then start the game and check the log.") && !whole.Contains('…'),
        $"a long section is not cut: {whole.Length} chars");
});

Test("sharing: a list round-trips as a file and as pasted text, anything else refused", _ =>
{
    var list = new SharedList
    {
        App = "1.4.0",
        Mods =
        {
            new SharedMod { Key = "page:300:YoRHa 2B", Name = "YoRHa 2B", Url = "https://www.nexusmods.com/phantombladezero/mods/300", Version = "2.1", Pinned = true, Category = "Characters", Memo = "스커트 옵션", Installed = true },
            new SharedMod { Name = "UUEPBS", Url = "https://github.com/XTGMods/UUEPBS", Version = "2.7.1" },
        },
        Order = { "sep:캐릭터", "page:300:YoRHa 2B" },
    };
    string Flat(SharedList l) => string.Join("|", l.Mods.Select(m => $"{m.Key},{m.Name},{m.Url},{m.Version},{m.Pinned},{m.Category},{m.Memo},{m.Installed}")) + "#" + string.Join(",", l.Order);
    Check(Flat(ModListShare.Read(ModListShare.ToJson(list))) == Flat(list), "file round-trip");
    var text = ModListShare.ToText(list);
    Check(text.Contains("YoRHa 2B 2.1 고정") && Flat(ModListShare.Read("chat said:\n" + text + "\nthanks")) == Flat(list), "pasted text round-trip, with words around it: " + text.Split('\n')[1]);
    foreach (var bad in new[] { "hello", "{\"format\":\"other\",\"version\":1,\"mods\":[]}", "pbzmods:@@@", "" })
    {
        var refused = false;
        try { ModListShare.Read(bad); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "refused: " + bad);
    }
});

Test("sharing: the file of a pinned version, old versions included", _ =>
{
    var files = new[]
    {
        new NexusFile("YoRHa 2B Replace Soul", "2.0", "OLD_VERSION", "900"), new NexusFile("YoRHa 2B Replace Soul", "2.1", "MAIN", "950"),
        new NexusFile("Other file", "2.0", "MAIN", "999"),
    };
    Check(NexusApi.ChooseFileForVersion("YoRHa 2B Replace Soul", files, "2.0") == "900", "old version's file");
    Check(NexusApi.ChooseFileForVersion("YoRHa 2B Replace Soul", files, "2.1") == "950", "current file");
    Check(NexusApi.ChooseFileForVersion("YoRHa 2B Replace Soul", files, "1.0") == "", "no such version");
    Check(NexusApi.ChooseFileForVersion("Page title", files, "2.1") == "950", "a name the files do not use: any file of that version");
});

Test("sharing: a shared order places mods as the user installs them", root =>
{
    var m = new Manager(root);
    var a = Zip(root, "Alpha 1.0 11 1.0 2026-01-01T00-00Z a.zip", ("ProjectPBZ/Content/Paks/~mods/A_P.pak", "a"));
    var b = Zip(root, "Beta 1.0 12 1.0 2026-01-01T00-00Z b.zip", ("ProjectPBZ/Content/Paks/~mods/B_P.pak", "b"));
    var c = Zip(root, "Gamma 1.0 13 1.0 2026-01-01T00-00Z c.zip", ("ProjectPBZ/Content/Paks/~mods/C_P.pak", "c"));
    Install(m, a);
    m.SaveSharedOrder(new[] { "sep:Graphics", ModIdentity.KeyOfZip(b), ModIdentity.KeyOfZip(a), "sep:UI", ModIdentity.KeyOfZip(c) });
    Install(m, b);
    Install(m, c);
    string Rows() => string.Join(", ", m.InstalledRows().Select(r => r.Text));
    Check(Rows() == "Graphics, Beta, Alpha, UI, Gamma", "placed with the shared separators: " + Rows());
    m.MoveInstalled(ModIdentity.KeyOfZip(c), -4);
    m.ApplySharedOrder();
    Check(Rows() == "Graphics, Beta, Alpha, UI, Gamma", "put back in the shared order: " + Rows());
    Check(m.OrderToShare(new HashSet<string> { ModIdentity.KeyOfZip(a) }).SequenceEqual(new[] { "sep:Graphics", ModIdentity.KeyOfZip(a), "sep:UI" }), "order shared for the mods chosen");
});

Test("nexus: a file's category, main or optional, from its current version", _ =>
{
    var files = new[]
    {
        new NexusFile("SoulVerR AIO", "1.01", "OLD_VERSION"), new NexusFile("SoulVerR AIO", "1.02", "MAIN"),
        new NexusFile("SoulVerR Lite Cloth Physics", "1.00p1", "OPTIONAL"), new NexusFile("Other", "1", "MAIN"),
    };
    Check(NexusApi.FileCategoryOf("SoulVerR AIO", files) == "MAIN" && NexusApi.FileCategoryOf("SoulVerR Lite Cloth Physics", files) == "OPTIONAL"
        && NexusApi.FileCategoryOf("Unknown", files) == "", "main, optional, unknown");
});

Test("install notes: live Nexus descriptions", _ =>
{
    foreach (var id in new[] { "414", "149", "5", "187", "113", "300" })
    {
        var (category, notes) = NexusApi.GetPageInfo(new NexusLink("phantombladezero", id));
        Console.WriteLine($"      --- mod {id} [{category}] {notes.Length} chars\n" + string.Join("\n", notes.Split('\n').Select(l => "      | " + l)));
    }
});

Test("housekeeping: the log drops lines over half a year old and keeps at most 2000", root =>
{
    var m = new Manager(root);
    var log = Path.Combine(m.StateDir, "log.txt");
    var now = new DateTime(2026, 11, 6, 12, 0, 0);
    var lines = new List<string> { "2025-01-01 10:00:00  old install", "2026-10-01 10:00:00  recent install", "a line without a date" };
    File.WriteAllLines(log, lines);
    m.TrimLog(now);
    var kept = File.ReadAllLines(log);
    Check(kept.SequenceEqual(new[] { "2026-10-01 10:00:00  recent install", "a line without a date" }), "old line gone: " + string.Join(" / ", kept));
    File.WriteAllLines(log, Enumerable.Range(0, 2500).Select(i => $"2026-11-01 10:00:00  line {i}"));
    m.TrimLog(now);
    kept = File.ReadAllLines(log);
    Check(kept.Length == 2000 && kept[^1].EndsWith("line 2499"), "newest 2000 kept");
});

Test("housekeeping: the keep folder is measured and emptied to the Recycle Bin", root =>
{
    var m = new Manager(root);
    Directory.CreateDirectory(Path.Combine(m.LeftoversDir, "20260101-000000-from-0001"));
    File.WriteAllText(Path.Combine(m.LeftoversDir, "20260101-000000-from-0001", "settings.txt"), "kept setting");
    var (runs, files, bytes) = m.LeftoversSize();
    Check(runs == 1 && files == 1 && bytes == 12, $"measured: {runs} {files} {bytes}");
    Check(m.ClearLeftovers().Count == 0 && !Directory.EnumerateFileSystemEntries(m.LeftoversDir).Any(), "emptied");
});

Test("category from an entered address", _ =>
{
    Check(NexusApi.ParseCategory("""{"data":{"legacyModsByDomain":{"nodes":[{"modCategory":{"name":"Tools"}}]}}}""") == "Tools", "category read");
    Check(NexusApi.ParseCategory("""{"data":{"legacyModsByDomain":{"nodes":[{"modCategory":null}]}}}""") == "", "page without a category");
    Check(NexusApi.ParseCategory("""{"data":{"legacyModsByDomain":{"nodes":[]}}}""") == "", "unknown page");
});

Test("update link: the newest file in the user's variant, and the address it opens", _ =>
{
    var page = new List<NexusFile>
    {
        new("PBZMapMarkers 1.8.0 Fog", "1.8.0", "ARCHIVED", "1"),
        new("UE4SS version 2.0.0", "2.0.0", "OPTIONAL", "2"),
        new("PBZMapMarkers 1.8.1 Teleport", "1.8.1", "OPTIONAL", "3"),
        new("PBZMapMarkers 1.8.1 Fog", "1.8.1", "MAIN", "4"),
        new("PBZMapMarkers 1.7.0 Fog", "1.7.0", "OLD_VERSION", "5"),
    };
    Check(NexusApi.ChooseFileId("PBZMapMarkers", "PBZMapMarkers 1.8.0 Fog 134 1.8.0 2026-11-04T13-02Z kMrBiHa5d.zip", page) == "4", "Fog stays Fog");
    Check(NexusApi.ChooseFileId("PBZMapMarkers", "PBZMapMarkers 1.8.0 Teleport 134 1.8.0 2026-11-04T13-02Z abc.zip", page) == "3", "Teleport stays Teleport");
    Check(NexusApi.ChooseFileId("PBZMapMarkers", "PBZMapMarkers 1.8.0 Other 134 1.8.0 2026-11-04T13-02Z abc.zip", page) is "3" or "4", "unknown variant: still the newest version");
    Check(NexusApi.ChooseFileId("Renamed Mod", "Renamed Mod 1.0.zip", page) == "", "no file of this name: no file number");

    const string json = """
    {"data":{"legacyModsByDomain":{"nodes":[{"modId":113,"version":"1.0.0","status":"published"}]},
     "m113":[{"fileId":1832,"name":"PBZModLoader 1.0.0","version":"1.0.0","category":"MAIN"}]}}
    """;
    var parsed = NexusApi.ParseResponse(json, new List<(string, string, string, string)> { ("loader", "PBZModLoader", "PBZModLoader 0.3.6-preview 113 0.3.6-preview 2026-01-01T00-00Z a.zip", "113") });
    Check(parsed["loader"].FileId == "1832", "file number comes through the response");

    var link = new NexusLink("phantombladezero", "113");
    Check(link.DownloadUrl("1832") == "https://www.nexusmods.com/phantombladezero/mods/113?tab=files&file_id=1832", "file address");
    Check(link.DownloadUrl("") == "https://www.nexusmods.com/phantombladezero/mods/113?tab=files", "file list when no number is known");
});

Test("files renamed on Nexus: the new name is followed, else the page's newest (live 385)", _ =>
{
    // Mod 385 as Nexus answered on 2026-11-07: "for 2B" became "For 2B(Original)", "For SoulVerR" became "For Ver.R".
    const string json = """
    {"data": {"legacyModsByDomain": {"nodes": [{"modId": 385, "version": "7", "status": "published"}]}, "m385": [{"fileId": 1048, "name": "PBZ FemaleAnimation", "version": "1", "category": "OLD_VERSION"}, {"fileId": 1322, "name": "PBZ FemaleAnimation OtherBodies", "version": "1", "category": "OLD_VERSION"}, {"fileId": 1323, "name": "PBZ FemaleAnimation for 2B", "version": "2", "category": "OLD_VERSION"}, {"fileId": 1342, "name": "PBZ FemaleAnimation for 2B", "version": "3", "category": "OLD_VERSION"}, {"fileId": 1343, "name": "PBZ FemaleAnimation OtherBodies", "version": "3", "category": "OLD_VERSION"}, {"fileId": 1509, "name": "PBZ FemaleAnimation OtherBodies", "version": "4", "category": "OLD_VERSION"}, {"fileId": 1510, "name": "PBZ FemaleAnimation Original Idle", "version": "1", "category": "OLD_VERSION"}, {"fileId": 2304, "name": "PBZ FemaleAnimation Original Idle", "version": "6", "category": "OLD_VERSION"}, {"fileId": 2305, "name": "PBZ FemaleAnimation OtherBodies", "version": "6", "category": "OLD_VERSION"}, {"fileId": 2306, "name": "PBZ FemaleAnimation for 2B(my personal edit)", "version": "6", "category": "OLD_VERSION"}, {"fileId": 2307, "name": "PBZ FemaleAnimation For SoulVerR", "version": "5", "category": "OLD_VERSION"}, {"fileId": 2333, "name": "PBZ FemaleAnimation For Ver.R", "version": "6", "category": "OLD_VERSION"}, {"fileId": 2350, "name": "PBZ FemaleAnimation For 2B(Original)", "version": "6", "category": "OLD_VERSION"}, {"fileId": 2354, "name": "PBZ FemaleAnimation For 2B(Original)", "version": "6.1", "category": "OLD_VERSION"}, {"fileId": 2359, "name": "PBZ FemaleAnimation For 2B(Original)", "version": "7", "category": "MAIN"}, {"fileId": 2360, "name": "PBZ FemaleAnimation For Ver.R", "version": "7", "category": "MAIN"}, {"fileId": 2361, "name": "PBZ FemaleAnimation for 2B(my personal edit)", "version": "7", "category": "OPTIONAL"}, {"fileId": 2362, "name": "PBZ FemaleAnimation OtherBodies", "version": "7", "category": "MAIN"}, {"fileId": 2363, "name": "PBZ FemaleAnimation Original Idle", "version": "7", "category": "MAIN"}]}}
    """;
    var parsed = NexusApi.ParseResponse(json, new List<(string, string, string, string)>
    {
        ("2b", "PBZ FemaleAnimation for 2B", "PBZ FemaleAnimation for 2B 385 5 2026-11-06T19-35Z ckMvdFXLI.zip", "385"),
        ("soul", "PBZ FemaleAnimation For SoulVerR", "PBZ FemaleAnimation For SoulVerR 385 5 2026-11-06T19-36Z UXaYxe4HY.zip", "385"),
        ("other", "PBZ FemaleAnimation OtherBodies", "PBZ FemaleAnimation OtherBodies 385 6 2026-11-06T19-36Z a.zip", "385"),
    });
    var b = parsed["2b"];
    Check(b.Version == "7" && b.FileId == "2359" && b.RenamedTo == "PBZ FemaleAnimation For 2B(Original)" && b.FileCategory == "MAIN" && !b.Retired,
        $"2B follows its new name, the main file: {b.Version} {b.FileId} {b.RenamedTo} {b.FileCategory}");
    var s = parsed["soul"];
    Check(s.Retired && s.Version == "7" && s.FileId == "" && !s.MatchedFile && s.RenamedTo == "", $"SoulVerR: only old versions left, the page's newest and its file list: {s.Version} {s.FileId}");
    var o = parsed["other"];
    Check(o.Version == "7" && o.FileId == "2362" && o.RenamedTo == "" && !o.Retired, $"a file still current is as before: {o.Version} {o.FileId}");
});

Test("a file renamed on its page carries on its mod: grouped, updated, remembered", root =>
{
    var m = new Manager(root);
    const string oldName = "PBZ FemaleAnimation for 2B 385 5 2026-11-06T19-35Z ckMvdFXLI.zip";
    const string newName = "PBZ FemaleAnimation For 2B(Original) 385 7 2026-11-08T00-00Z abcdEFGH.zip";
    var oldZip = Zip(root, oldName, ("ProjectPBZ/Content/Paks/~mods/zzzz_Female Animation_P.pak", "5"), ("ProjectPBZ/Content/Paks/~mods/zzzz_Old_Only_P.pak", "o"));
    var newZip = Zip(root, newName, ("ProjectPBZ/Content/Paks/~mods/zzzz_Female Animation_P.pak", "7"));
    var oldKey = ModIdentity.KeyOfZip(oldZip);
    var v5 = Install(m, oldZip);
    Check(ModIdentity.KeyOfZip(newZip) != oldKey, "apart before they are joined");

    var newKey = ModIdentity.PageKey("385", ModIdentity.ParseFileName("PBZ FemaleAnimation For 2B(Original).zip").Name);
    Check(newKey == ModIdentity.KeyOfZip(newZip), "the key made from the new name is the download's: " + newKey);
    Check(m.LinkRename(newKey, oldKey), "joined");
    Check(!m.LinkRename(newKey, oldKey) && !m.LinkRename(oldKey, newKey), "joining again, or back the other way, changes nothing");
    Check(ModIdentity.KeyOfZip(newZip) == oldKey, "the new file's key leads to the mod it was");

    var meta = new Dictionary<string, ModMeta> { [oldName] = new() { Registered = true }, [newName] = new() { Registered = true } };
    var group = m.ListMods(meta).Single();
    Check(group.Key == oldKey && group.Versions.Count == 2 && group.Latest.FileName == newName && group.UpdateAvailable, "one mod, the new file its newest version, an update offered");

    var plan = m.BuildPlan(newZip);
    Check(plan.ReplacesZip == oldName, "installed as an update of the old file");
    Check(plan.Stale.Select(s => s.Path).SequenceEqual(new[] { "ProjectPBZ/Content/Paks/~mods/zzzz_Old_Only_P.pak" }), "the old file's own pak goes: " + string.Join(",", plan.Stale.Select(s => s.Path)));
    m.Install(plan);
    Check(m.ListMods(meta).Single().InstalledFile == newName, "the new file is the installed version");

    var again = new Manager(root);
    Check(ModIdentity.KeyOfZip(newZip) == oldKey && again.LoadRenames().Count == 1, "kept for the next start");
    new Manager(Path.Combine(root, "elsewhere"));
    Check(ModIdentity.KeyOfZip(newZip) == newKey, "another game folder has its own links");
    new Manager(root);
});

Test("page titles: a trailing version is taken off, and the check reads the title", _ =>
{
    Check(NexusApi.TitleAsName("YoRHa 2B Replace Soul_(Outfit Options)_v2.0") == "YoRHa 2B Replace Soul_(Outfit Options)", "trailing _v2.0");
    Check(NexusApi.TitleAsName("Map Tracker Plus - Minimap and Collectibles (UE4SS)") == "Map Tracker Plus - Minimap and Collectibles (UE4SS)", "a bracket that is no version stays");
    Check(NexusApi.TitleAsName("UE4SS for Phantom Blade Zero") == "UE4SS for Phantom Blade Zero", "a number inside a word stays");
    Check(NexusApi.TitleAsName("Cool Mod v1.2.3") == "Cool Mod" && NexusApi.TitleAsName("Cool Mod (1.2)") == "Cool Mod" && NexusApi.TitleAsName("1.2") == "1.2", "other version forms; a title that is only a version stays");
    const string json = """
    {"data":{"legacyModsByDomain":{"nodes":[{"modId":187,"name":"Map Tracker Plus &amp; More","version":"1.0.4","status":"published"}]},
     "m187":[{"fileId":1,"name":"Map Collectible Tracker 1.0.4","version":"1.0.4","category":"MAIN"}]}}
    """;
    var parsed = NexusApi.ParseResponse(json, new List<(string, string, string, string)> { ("t", "Map Collectible Tracker", "Map Collectible Tracker 1.0.4 187 1.0.4 2026-11-07T00-32Z s0mFcMSWX.zip", "187") });
    Check(parsed["t"].PageName == "Map Tracker Plus & More", "the page's title, decoded: " + parsed["t"].PageName);
});

Test("after an update, the version updated from can be deleted; the one in effect cannot", root =>
{
    var m = new Manager(root);
    var v1 = Zip(root, "Ninja 444 1 2026-11-03T14-58Z vB84Zu5C1.zip", ("ProjectPBZ/Content/Paks/~mods/Ninja_P.pak", "1"));
    var v2 = Zip(root, "Ninja 444 2 2026-11-07T18-24Z 8TxGunI7o.zip", ("ProjectPBZ/Content/Paks/~mods/Ninja_P.pak", "2"));
    Install(m, v1);
    var top = Install(m, v2);
    Check(m.ArchivesInEffect().SetEquals(new[] { Path.GetFileName(v2) }), "the update is in effect");
    var meta = m.LoadMeta();
    m.RegisterInstalled(meta);
    meta[Path.GetFileName(v1)] = new ModMeta { Registered = true };

    bool refused = false;
    try { m.DeleteZip(Path.GetFileName(v2)); } catch (InvalidOperationException) { refused = true; }
    Check(refused && File.Exists(v2), "the archive in effect is kept");
    m.DeleteZip(Path.GetFileName(v1));
    Check(!File.Exists(v1), "the one updated from goes");

    meta[Path.GetFileName(v1)].Registered = false;
    Check(m.ListZips(meta).Select(Path.GetFileName).SequenceEqual(new[] { Path.GetFileName(v2) }), "and leaves the list: " + string.Join(", ", m.ListZips(meta).Select(Path.GetFileName)));
    Check(!m.RegisterInstalled(meta) && !meta[Path.GetFileName(v1)].Registered, "it is not listed again by itself");

    // Undoing the update brings the old version back into effect: listed again, though its file is gone.
    m.RemoveOne(top.Id);
    Check(Read(root, "ProjectPBZ/Content/Paks/~mods/Ninja_P.pak") == "1", "undo restores the old version from its backups");
    Check(m.ListZips(meta).Select(Path.GetFileName).Contains(Path.GetFileName(v1)), "the old version is listed again, now in effect");
});

Test("the same file under another name is not added twice", root =>
{
    var m = new Manager(root);
    var first = Zip(root, "Hand downloaded 1.0 113 1.0 2026-01-01T00-00Z abc.zip", ("f.txt", "A"));
    Directory.CreateDirectory(Path.Combine(root, "elsewhere"));
    var outside = Path.Combine(root, "elsewhere", "Fetched 1.0 113 1.0 2026-01-02T00-00Z 9.zip");
    File.Copy(first, outside);
    Check(m.AddZip(outside) == first, "should point at the file already there");
    Check(Directory.GetFiles(m.ArchiveDir, "*.zip").Length == 1 && !File.Exists(outside), "no second copy, the dropped one gone");
});

Test("records from before updates existed still group and remove", root =>
{
    var m = new Manager(root);
    var v1 = Install(m, Zip(root, "M 1.0.zip", (ModRoot + "main.js", "1"), (ModRoot + "stale.js", "s")));
    var manifest = Path.Combine(m.StateDir, "history", v1.Id.ToString("D4"), "manifest.json");
    var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
    node.Remove("Unchanged");
    foreach (var f in node["Files"]!.AsArray()) f!.AsObject().Remove("Deleted");
    File.WriteAllText(manifest, node.ToJsonString());
    var reopened = new Manager(root);
    var plan = reopened.BuildPlan(Zip(root, "M 2.0.zip", (ModRoot + "main.js", "2")));
    Check(plan.ReplacesZip == "M 1.0.zip" && plan.Stale.Single().Path.EndsWith("stale.js"), "old record should be recognized");
});

Test("identical archive has nothing to install", root =>
{
    var zip = Zip(root, "a.zip", ("f.txt", "A"));
    var m = new Manager(root);
    Install(m, zip);
    var plan = m.BuildPlan(zip);
    Check(!plan.HasChanges && plan.Count(ChangeKind.Same) == 1, "should be same");
});

Test("failure mid-install (locked file) restores everything and leaves no history", root =>
{
    Write(root, "b-locked.txt", "orig-b");
    Write(root, "a-first.txt", "orig-a");
    var zip = Zip(root, "a.zip", ("a-first.txt", "new-a"), ("newdir/added.txt", "new"), ("b-locked.txt", "new-b"), ("z-after.txt", "z"));
    var m = new Manager(root);
    var before = Snapshot(root);
    var plan = m.BuildPlan(zip);
    using (new FileStream(Path.Combine(root, "b-locked.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        var failedAsExpected = false;
        try { m.Install(plan); } catch (InvalidOperationException) { failedAsExpected = true; }
        Check(failedAsExpected, "install should fail");
    }
    SameTree(before, Snapshot(root));
    Check(m.History().Count == 0 && m.Pending() == null, "no history expected");
});

Test("failed rollback (locked file) stays pending and succeeds on retry", root =>
{
    var zip = Zip(root, "a.zip", ("a.txt", "a"), ("sub/b.txt", "b"), ("z.txt", "z"));
    var m = new Manager(root);
    var before = Snapshot(root);
    var tx = Install(m, zip);
    using (new FileStream(Path.Combine(root, @"sub\b.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        var threw = false;
        try { m.RollbackTo(tx.Id); } catch (IOException) { threw = true; } catch (UnauthorizedAccessException) { threw = true; }
        Check(threw, "rollback should fail while locked");
    }
    Check(m.Pending()?.Id == tx.Id, "should be pending");
    var blocked = false;
    try { Install(m, Zip(root, "b.zip", ("q.txt", "q"))); } catch (InvalidOperationException) { blocked = true; }
    Check(blocked, "install must be refused while pending");
    m.RollbackTo(tx.Id);
    SameTree(before, Snapshot(root));
    Check(m.Pending() == null, "should be clean");
});

Test("crash mid-install is recovered from the manifest", root =>
{
    Write(root, "over.txt", "orig");
    var zip = Zip(root, "a.zip", ("over.txt", "new"), ("added1.txt", "1"), ("added2.txt", "2"));
    var m = new Manager(root);
    var before = Snapshot(root);
    var tx = Install(m, zip);

    // State as if the process died: flagged unfinished, one new file not yet written.
    File.Delete(Path.Combine(root, "added2.txt"));
    var manifest = Path.Combine(m.StateDir, "history", tx.Id.ToString("D4"), "manifest.json");
    var doc = JsonSerializer.Deserialize<Tx>(File.ReadAllText(manifest))!;
    doc.State = Tx.Installing;
    File.WriteAllText(manifest, JsonSerializer.Serialize(doc));

    var reopened = new Manager(root);
    Check(reopened.Pending()?.Id == tx.Id, "pending not detected");
    reopened.RollbackTo(tx.Id);
    SameTree(before, Snapshot(root));
});

Test("rollback when overwritten file changed afterwards keeps that version", root =>
{
    Write(root, "f.dll", "orig");
    var zip = Zip(root, "a.zip", ("f.dll", "mod"));
    var m = new Manager(root);
    var before = Snapshot(root);
    var tx = Install(m, zip);
    Write(root, "f.dll", "steam update");
    Check(m.BuildRollbackPlan(tx.Id).Steps.Single().Kind == StepKind.StashRestore, "should stash then restore");
    var leftovers = m.RollbackTo(tx.Id);
    SameTree(before, Snapshot(root));
    Check(File.ReadAllText(Path.Combine(leftovers!, "f.dll")) == "steam update", "changed file not kept");
});

Test("preview goes stale when the game folder changes, install refused", root =>
{
    var zip = Zip(root, "a.zip", ("f.txt", "new"));
    var m = new Manager(root);
    var plan = m.BuildPlan(zip);
    Write(root, "f.txt", "appeared after preview");
    var refused = false;
    try { m.Install(plan); } catch (InvalidOperationException) { refused = true; }
    Check(refused, "should refuse");
    Check(Read(root, "f.txt") == "appeared after preview", "file was touched");
});

Test("malicious archive paths are rejected before any change", root =>
{
    var m = new Manager(root);
    var before = Snapshot(root);
    string[] bad =
    {
        "../evil.txt", "..\\evil.txt", "a/../../evil.txt", "/evil.txt", "C:/evil.txt", "C:\\evil.txt",
        "a/file.txt:stream", "CON", "a/NUL.txt", "COM1.dll", "trailing./x.txt", "[] MOD/.manager/history/0001/manifest.json",
    };
    int i = 0;
    foreach (var name in bad)
    {
        var zip = Zip(root, $"bad{i++}.zip", (name, "x"));
        var rejected = false;
        try { m.BuildPlan(zip); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, $"not rejected: {name}");
    }
    var dup = Zip(root, "dup.zip", ("A.txt", "1"), ("a.txt", "2"));
    var dupRejected = false;
    try { m.BuildPlan(dup); } catch (InvalidOperationException) { dupRejected = true; }
    Check(dupRejected, "case-duplicate not rejected");
    SameTree(before, Snapshot(root));
    Check(!File.Exists(Path.Combine(work, "evil.txt")), "escaped");
});

Test("readmes: left out of the install beside paks, kept where a mod may read them, listed to read", root =>
{
    var m = new Manager(root);
    var zip = Zip(root, "Some Mod 900 1.0 2026-11-07T00-00Z a.zip",
        ("~mods/Some_P.pak", "p"),
        ("README.txt", "read me"),
        ("설치 방법.pdf", "%PDF"),
        ("notes.txt", "beside the pak"),
        ("preset.json", "{}"),
        ("Docs/Changelog.md", "# 1.0"),
        ("ProjectPBZ/Content/readme.txt", "laid out by the author"));
    var plan = m.BuildPlan(zip);
    var paths = plan.Items.Select(i => i.Path).ToList();
    Check(!paths.Any(p => p.EndsWith("README.txt") || p.EndsWith(".pdf") || p.EndsWith("notes.txt") || p.EndsWith("Changelog.md")), "readmes left out: " + string.Join(", ", paths));
    Check(paths.Contains("preset.json") && paths.Contains("ProjectPBZ/Content/readme.txt") && paths.Contains("ProjectPBZ/Content/Paks/~mods/Some_P.pak"), "the rest installed: " + string.Join(", ", paths));
    Check(plan.Placements.GetValueOrDefault(Placement.DocumentLeftOut) == 4, "four counted");
    Check(plan.Warnings.Any(w => w.Contains("설명서 4개")), "warned: " + string.Join(" | ", plan.Warnings));

    var lua = Zip(root, "Lua Mod.zip", ("LuaMod/Scripts/main.lua", "x"), ("LuaMod/readme.txt", "kept"), ("LuaMod/Scripts/data.txt", "kept"));
    var luaPaths = m.BuildPlan(lua).Items.Select(i => i.Path).ToList();
    Check(luaPaths.Any(p => p.EndsWith("LuaMod/readme.txt")) && luaPaths.Any(p => p.EndsWith("Scripts/data.txt")), "a UE4SS mod's folder keeps its files: " + string.Join(", ", luaPaths));

    var listed = Manager.DocumentsIn(zip);
    Check(listed.SequenceEqual(new[] { "Docs/Changelog.md", "ProjectPBZ/Content/readme.txt", "README.txt", "설치 방법.pdf", "notes.txt" }), "listed, named first: " + string.Join(", ", listed));
    Check(Manager.DocumentsIn(lua).SequenceEqual(new[] { "LuaMod/readme.txt" }), "script data is not a readme: " + string.Join(", ", Manager.DocumentsIn(lua)));
});

Test("readmes: text is read in its own encoding", _ =>
{
    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    const string words = "설치 방법: ~mods 폴더에 넣으세요";
    Check(Manager.ReadDocumentText(System.Text.Encoding.UTF8.GetBytes(words)) == words, "utf-8");
    Check(Manager.ReadDocumentText(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(words)).ToArray()) == words, "utf-8 with mark");
    Check(Manager.ReadDocumentText(System.Text.Encoding.Unicode.GetPreamble().Concat(System.Text.Encoding.Unicode.GetBytes(words)).ToArray()) == words, "utf-16");
    Check(Manager.ReadDocumentText(System.Text.Encoding.GetEncoding(949).GetBytes(words)) == words, "cp949");
    Check(Manager.PlainFromHtml("<html><style>p{}</style><p>A&amp;B</p><script>x()</script>C<br>D</html>") == "A&B\nC\nD", "html words: " + Manager.PlainFromHtml("<p>A&amp;B</p>C<br>D"));
});

Test("wrapped archive produces a warning", root =>
{
    var m = new Manager(root);
    Check(m.BuildPlan(Zip(root, "wrapped.zip", ("Loader/ProjectPBZ/x.txt", "x"))).Warnings.Count == 1, "should warn");
    Check(m.BuildPlan(Zip(root, "ok.zip", ("ProjectPBZ/x.txt", "x"))).Warnings.Count == 0, "should not warn");
});

Test("install and rollback are refused while the game runs", root =>
{
    var zip = Zip(root, "a.zip", ("f.txt", "new"));
    var m = new Manager(root);
    var tx = Install(m, zip);
    var plan2 = m.BuildPlan(Zip(root, "b.zip", ("g.txt", "g")));

    var fake = Path.Combine(work, "PBZ.exe");
    if (!File.Exists(fake)) File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), fake);
    using var p = Process.Start(new ProcessStartInfo(fake, "-n 30 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })!;
    try
    {
        Thread.Sleep(300);
        Check(Manager.GameRunning(), "game not detected");
        var a = false; var b = false;
        try { m.Install(plan2); } catch (InvalidOperationException) { a = true; }
        try { m.RollbackTo(tx.Id); } catch (InvalidOperationException) { b = true; }
        Check(a && b, "should be refused");
        Check(Read(root, "f.txt") == "new" && !Exists(root, "g.txt"), "files changed");
    }
    finally
    {
        p.Kill();
        p.WaitForExit();
    }
});

Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
