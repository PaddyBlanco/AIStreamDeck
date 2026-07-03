# kill-dotnet.ps1 - beendet verwaiste dotnet-Prozesse (Blazor-Host / dotnet watch).
# [DESTRUKTIV] Listet erst auf, fragt dann nach Bestaetigung.
# Aufruf:  kill-dotnet.cmd                 (dotnet)
#          kill-dotnet.cmd -Names dotnet,MSBuild
param([string[]]$Names = @('dotnet'))

. "$PSScriptRoot\lib\_common.ps1"

$procs = Get-Process -Name $Names -ErrorAction SilentlyContinue
if (-not $procs) { Write-Host "Keine passenden Prozesse gefunden." -ForegroundColor Green; Pause-End; return }

Write-Step ("Gefundene Prozesse (" + ($Names -join ', ') + "):")
Write-Host ""
$procs | Sort-Object ProcessName, Id | ForEach-Object {
    Write-Host ("  PID {0,-7} {1,-10} Start: {2}" -f $_.Id, $_.ProcessName, $_.StartTime)
}

if (Confirm-Action "ALLE oben gelisteten Prozesse werden beendet.") {
    foreach ($proc in $procs) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop; Write-Host "  beendet: $($proc.ProcessName) ($($proc.Id))" -ForegroundColor Green }
        catch { Write-Host "  fehlgeschlagen: $($proc.Id) - $($_.Exception.Message)" -ForegroundColor Red }
    }
} else {
    Write-Host "Abgebrochen." -ForegroundColor DarkYellow
}
Pause-End
