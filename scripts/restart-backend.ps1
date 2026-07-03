# restart-backend.ps1 - gibt den Kestrel-Port frei und startet das Backend neu (dotnet watch).
# [DESTRUKTIV beim Beenden] Fragt vor dem Kill nach Bestaetigung.
# Aufruf:  restart-backend.cmd [Projektname]
param([string]$Project)

. "$PSScriptRoot\lib\_common.ps1"

$p = Get-Project $Project
if (-not $p.kestrelPort) { throw "Kein 'kestrelPort' fuer Projekt '$($p.name)' konfiguriert." }
$port = [int]$p.kestrelPort

Write-Step "Backend-Neustart fuer $($p.name) (Port $port)"

$pids = (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue).OwningProcess | Sort-Object -Unique
if ($pids) {
    foreach ($procId in $pids) {
        $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
        if ($proc) { Write-Host ("  laeuft: PID {0} {1}" -f $procId, $proc.ProcessName) }
    }
    if (Confirm-Action "Aktuelles Backend auf Port $port beenden?") {
        foreach ($procId in $pids) {
            try { Stop-Process -Id $procId -Force -ErrorAction Stop; Write-Host "  beendet: PID $procId" -ForegroundColor Green }
            catch { Write-Host "  fehlgeschlagen: PID $procId - $($_.Exception.Message)" -ForegroundColor Red }
        }
    } else {
        Write-Host "Abgebrochen - Backend nicht neu gestartet." -ForegroundColor DarkYellow
        Pause-End; return
    }
} else {
    Write-Host "  Kein laufendes Backend auf Port $port gefunden." -ForegroundColor DarkGray
}

# Neu starten
$profileArg = if ($p.launchProfile) { " --launch-profile `"$($p.launchProfile)`"" } else { '' }
if ($p.backendProject) {
    Start-DevWindow -WorkingDir (Split-Path $p.backendProject) `
        -Command "dotnet watch --project `"$($p.backendProject)`" run$profileArg" -Title "Blazor - $($p.name)"
} elseif ($p.backendDir) {
    Start-DevWindow -WorkingDir $p.backendDir -Command "dotnet watch run$profileArg" -Title "Blazor - $($p.name)"
} else {
    throw "Weder 'backendProject' noch 'backendDir' fuer '$($p.name)' konfiguriert."
}
Write-Step "Backend neu gestartet."
