# kill-ports.ps1 - gibt den belegten Blazor-Dev-Port (Kestrel) frei.
# [DESTRUKTIV] Fragt vor dem Beenden nach Bestaetigung.
# Aufruf:  kill-ports.cmd [Projektname]   ODER   kill-ports.cmd -Ports 7060
param(
    [string]$Project,
    [int[]]$Ports
)

. "$PSScriptRoot\lib\_common.ps1"

if (-not $Ports) {
    $p = Get-Project $Project
    $Ports = @()
    if ($p.kestrelPort) { $Ports += [int]$p.kestrelPort }
}
$Ports = $Ports | Sort-Object -Unique
if (-not $Ports) { Write-Host "Keine Ports angegeben/konfiguriert."; Pause-End; return }

Write-Step ("Suche Prozesse auf Ports: " + ($Ports -join ', '))

$pids = @()
foreach ($port in $Ports) {
    $conns = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    foreach ($c in $conns) { $pids += $c.OwningProcess }
}
$pids = $pids | Sort-Object -Unique

if (-not $pids) { Write-Host "Keine lauschenden Prozesse auf diesen Ports gefunden." -ForegroundColor Green; Pause-End; return }

Write-Host ""
foreach ($procId in $pids) {
    $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
    if ($proc) { Write-Host ("  PID {0,-7} {1}" -f $procId, $proc.ProcessName) }
}

if (Confirm-Action "Diese Prozesse werden BEENDET (Stop-Process).") {
    foreach ($procId in $pids) {
        try { Stop-Process -Id $procId -Force -ErrorAction Stop; Write-Host "  beendet: PID $procId" -ForegroundColor Green }
        catch { Write-Host "  fehlgeschlagen: PID $procId - $($_.Exception.Message)" -ForegroundColor Red }
    }
} else {
    Write-Host "Abgebrochen." -ForegroundColor DarkYellow
}
Pause-End
