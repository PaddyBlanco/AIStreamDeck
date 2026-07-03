using System.Diagnostics;

namespace AIStreamDeck;

internal enum GitSync { Ok, Conflict, Error }

/// <summary>
/// Eine Git-Aktion fuers Arbeits-Repo: stagen + Commit (KI-Message) -> Pull ->
/// bei sauber Push, bei Conflict TortoiseGit zum Loesen oeffnen. Nie force.
/// </summary>
internal static class GitOps
{
    public static async Task<(GitSync result, string msg)> SyncAsync(string repo, IAiBackend ai)
    {
        var add = Run(repo, "add -A");
        if (add.exit != 0) return (GitSync.Error, Short(add.stderr));

        // Commit nur, wenn es etwas zu committen gibt.
        var staged = Run(repo, "diff --staged --stat");
        if (!string.IsNullOrWhiteSpace(staged.stdout))
        {
            var diff = Run(repo, "diff --staged");
            string msg = (ai.Enabled ? await ai.CommitMessageAsync(diff.stdout) : null) ?? "WIP";
            msg = msg.Split('\n')[0].Trim();
            if (msg.Length == 0) msg = "WIP";
            string tmp = Path.GetTempFileName();
            await File.WriteAllTextAsync(tmp, msg);
            try
            {
                var c = Run(repo, $"commit -F \"{tmp}\"");
                if (c.exit != 0) return (GitSync.Error, Short(c.stderr));
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        // Pull (Merge, kein Editor). Conflict -> TortoiseGit.
        var pull = Run(repo, "pull --no-edit");
        if (HasConflict(repo))
        {
            OpenConflictResolver(repo);
            return (GitSync.Conflict, "Conflict -> TortoiseGit");
        }
        if (pull.exit != 0)
            return (GitSync.Error, Short(pull.stderr.Length > 0 ? pull.stderr : pull.stdout));

        // Sauber -> Push.
        var push = Run(repo, "push");
        if (push.exit != 0) return (GitSync.Error, Short(push.stderr));
        return (GitSync.Ok, "commit+pull+push");
    }

    private static bool HasConflict(string repo)
        => !string.IsNullOrWhiteSpace(Run(repo, "diff --name-only --diff-filter=U").stdout);

    private static void OpenConflictResolver(string repo)
    {
        try
        {
            string? exe = TortoiseGitProc();
            if (exe != null)
                Process.Start(new ProcessStartInfo(exe, $"/command:resolve /path:\"{repo}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(repo) { UseShellExecute = true }); // Fallback: Repo oeffnen
        }
        catch { }
    }

    private static string? TortoiseGitProc()
    {
        foreach (var p in new[]
        {
            @"C:\Program Files\TortoiseGit\bin\TortoiseGitProc.exe",
            @"C:\Program Files (x86)\TortoiseGit\bin\TortoiseGitProc.exe",
        })
            if (File.Exists(p)) return p;
        return null;
    }

    private static string Short(string s) { s = s.Trim(); return s.Length > 60 ? s[..60] : s; }

    private static (int exit, string stdout, string stderr) Run(string repo, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", $"-C \"{repo}\" {args}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            string outp = p.StandardOutput.ReadToEnd();
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(120000);
            return (p.ExitCode, outp, err);
        }
        catch (Exception ex) { return (-1, "", ex.Message); }
    }
}
