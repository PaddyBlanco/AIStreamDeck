using System.Diagnostics;

namespace AIStreamDeck;

/// <summary>Liest READ-ONLY den Git-Stand des Arbeits-Repos (kein Schreiben, kein Verzeichniswechsel).</summary>
internal static class GitStatus
{
    public readonly record struct Info(string Branch, bool Dirty, int Ahead, int Behind, bool Ok);

    public static Info Read(string repoPath)
    {
        // --branch + --porcelain: erste Zeile = Branch/Tracking, weitere Zeilen = Aenderungen
        var output = RunGit(repoPath, "status --porcelain=v1 --branch");
        if (output is null) return new Info("?", false, 0, 0, false);

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string branch = "?";
        int ahead = 0, behind = 0;
        bool dirty = false;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("## "))
            {
                var head = line[3..];
                int dots = head.IndexOf("...", StringComparison.Ordinal);
                branch = dots >= 0 ? head[..dots] : head.Split(' ')[0];
                int br = head.IndexOf('[');
                if (br >= 0)
                {
                    var seg = head[(br + 1)..head.IndexOf(']', br)];
                    ahead = ParseAfter(seg, "ahead ");
                    behind = ParseAfter(seg, "behind ");
                }
            }
            else
            {
                dirty = true;
            }
        }
        return new Info(branch, dirty, ahead, behind, true);
    }

    /// <summary>Bis zu <paramref name="max"/> geaenderte Dateinamen (read-only).</summary>
    public static IReadOnlyList<string> ChangedFiles(string repoPath, int max)
    {
        var outp = RunGit(repoPath, "status --porcelain=v1");
        if (outp is null) return Array.Empty<string>();
        var list = new List<string>();
        foreach (var raw in outp.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 4) continue;
            string file = line[3..].Trim();
            int arrow = file.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0) file = file[(arrow + 4)..];
            list.Add(Path.GetFileName(file.Trim('"')));
            if (list.Count >= max) break;
        }
        return list;
    }

    private static int ParseAfter(string seg, string token)
    {
        int idx = seg.IndexOf(token, StringComparison.Ordinal);
        if (idx < 0) return 0;
        int start = idx + token.Length;
        int end = start;
        while (end < seg.Length && char.IsDigit(seg[end])) end++;
        return int.TryParse(seg[start..end], out int v) ? v : 0;
    }

    private static string? RunGit(string repoPath, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", $"-C \"{repoPath}\" {args}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            string outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(4000);
            return outp;
        }
        catch { return null; }
    }
}
