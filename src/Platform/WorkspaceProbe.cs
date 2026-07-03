using System.Collections.Concurrent;

namespace AIStreamDeck;

/// <summary>
/// Ermittelt best-effort, welcher Ordner in einem Editor (VS Code/Cursor/Windsurf) offen ist,
/// und beschreibt kompakt seine Struktur (Projekttyp, Marker-Dateien, Unterordner) — damit die KI
/// gezieltere Tasten vorschlagen kann. Sendet **nur Struktur/Namen**, nie Dateiinhalte.
///
/// Pfad-Aufloesung: VS Code gibt den Pfad nicht direkt raus. 1) Steht ein echter Pfad im Titel
/// (z. B. via `window.title`-Einstellung), wird der genommen. 2) Sonst wird der Ordnername aus dem
/// Titel gegen die bekannten Dev-Wurzeln aufgeloest. Alles gecacht (pro Titel) und bruchsicher.
/// </summary>
internal static class WorkspaceProbe
{
    private static readonly string[] Editors = { "Code", "Cursor", "Windsurf" };
    private static readonly ConcurrentDictionary<string, string?> _cache = new();     // proc|title -> Beschreibung
    private static readonly ConcurrentDictionary<string, string?> _resolved = new();   // name -> Pfad
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
        { "node_modules", ".git", "bin", "obj", ".vs", ".vscode", ".idea", "dist", "packages", ".angular", "TestResults" };

    public static string? Describe(string proc, string title, IReadOnlyList<string> devRoots)
    {
        if (string.IsNullOrEmpty(proc) || !Editors.Contains(proc, StringComparer.OrdinalIgnoreCase)) return null;
        string key = proc + "|" + title;
        if (_cache.TryGetValue(key, out var hit)) return hit;
        string? result;
        try { result = Probe(title, devRoots); } catch { result = null; }
        _cache[key] = result;
        return result;
    }

    private static string? Probe(string title, IReadOnlyList<string> devRoots)
    {
        string? dir = PathFromTitle(title) ?? ResolveByName(RootName(title), devRoots);
        return dir is not null && Directory.Exists(dir) ? Scan(dir) : null;
    }

    /// <summary>Zieht einen echten Windows-Pfad aus dem Titel, falls vorhanden (Ordner oder Datei -> Ordner).</summary>
    private static string? PathFromTitle(string title)
    {
        int c = title.IndexOf(":\\", StringComparison.Ordinal);
        if (c < 1) return null;
        string cand = title[(c - 1)..].Trim();
        foreach (var tail in new[] { " - Visual Studio Code", " — Visual Studio Code", " - Cursor", " - Windsurf" })
        { int t = cand.IndexOf(tail, StringComparison.Ordinal); if (t >= 0) cand = cand[..t]; }
        cand = cand.Trim();
        if (Directory.Exists(cand)) return cand;
        try { if (File.Exists(cand)) return Path.GetDirectoryName(cand); } catch { }
        return null;
    }

    /// <summary>Ordnername = vorletztes Titel-Segment (VS Code: "Datei - Ordner - AppName").</summary>
    private static string? RootName(string title)
    {
        var parts = title.Split(new[] { " - ", " — " }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;                 // nur "Datei - App" -> kein Ordner sicher erkennbar
        string root = parts[^2].Trim().TrimStart('●', '*', ' ').Trim();
        return root.Length == 0 ? null : root;
    }

    private static string? ResolveByName(string? name, IReadOnlyList<string> devRoots)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (_resolved.TryGetValue(name, out var cached)) return cached;
        string? found = null;
        int budget = 600; // begrenzt die FS-Suche
        foreach (var root in devRoots)
        {
            if (found is not null || !Directory.Exists(root)) continue;
            try
            {
                foreach (var lvl1 in Directory.EnumerateDirectories(root))
                {
                    if (--budget < 0) break;
                    if (string.Equals(Path.GetFileName(lvl1), name, StringComparison.OrdinalIgnoreCase)) { found = lvl1; break; }
                    if (Skip.Contains(Path.GetFileName(lvl1)!)) continue;
                    try
                    {
                        foreach (var lvl2 in Directory.EnumerateDirectories(lvl1))
                        {
                            if (--budget < 0) break;
                            if (string.Equals(Path.GetFileName(lvl2), name, StringComparison.OrdinalIgnoreCase)) { found = lvl2; break; }
                        }
                    }
                    catch { }
                    if (found is not null) break;
                }
            }
            catch { }
        }
        _resolved[name] = found;
        return found;
    }

    /// <summary>Kompakte Struktur der Top-Ebene (nur Namen/Typen, keine Inhalte).</summary>
    private static string Scan(string dir)
    {
        string name = Path.GetFileName(dir.TrimEnd('\\', '/'));
        var files = Directory.EnumerateFiles(dir).Select(f => Path.GetFileName(f)!).Take(300).ToList();
        var subdirs = Directory.EnumerateDirectories(dir).Select(d => Path.GetFileName(d)!)
            .Where(n => !Skip.Contains(n)).Take(12).ToList();

        bool Has(Func<string, bool> pred) => files.Any(pred);
        bool Ext(string e) => Has(f => f.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        bool File_(string n) => files.Any(f => string.Equals(f, n, StringComparison.OrdinalIgnoreCase));

        var kind = new List<string>();
        var slns = files.Where(f => f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        if (slns.Count > 0) kind.Add(".NET-Solution " + string.Join(",", slns));
        else if (Ext(".csproj")) kind.Add("C#/.NET");
        if (Ext(".fsproj")) kind.Add("F#");
        if (File_("package.json")) kind.Add("Node/npm");
        if (File_("global.json")) kind.Add("global.json");
        if (File_("Dockerfile") || File_("docker-compose.yml") || File_("compose.yaml")) kind.Add("Docker");
        if (File_("Cargo.toml")) kind.Add("Rust");
        if (File_("go.mod")) kind.Add("Go");
        if (File_("requirements.txt") || File_("pyproject.toml")) kind.Add("Python");

        var notable = files.Where(f =>
                f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                f is "global.json" or "Directory.Build.props" or "Directory.Packages.props" or "package.json")
            .Take(6).ToList();

        var sb = new System.Text.StringBuilder();
        sb.Append("Geöffneter Ordner: ").Append(name).Append(" (Pfad: ").Append(dir).Append(')');
        if (kind.Count > 0) sb.Append(" [").Append(string.Join(", ", kind)).Append(']');
        if (subdirs.Count > 0) sb.Append("; Unterordner: ").Append(string.Join(", ", subdirs));
        if (notable.Count > 0) sb.Append("; Dateien: ").Append(string.Join(", ", notable));
        return sb.ToString();
    }
}
