# _common.ps1 - gemeinsame Helfer fuer alle Stream-Deck-Skripte
# Wird per Dot-Sourcing eingebunden:  . "$PSScriptRoot\lib\_common.ps1"
# Kompatibel mit Windows PowerShell 5.1 und PowerShell 7 (pwsh).

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Get-RepoRoot {
    # lib/ liegt unter scripts/ -> zwei Ebenen hoch ist das Repo
    return (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

function Get-Config {
    param([Parameter(Mandatory)][string]$Name)
    $path = Join-Path (Get-RepoRoot) "config\$Name.json"
    if (-not (Test-Path $path)) { throw "Config nicht gefunden: $path" }
    return Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Get-Project {
    param([string]$Name)
    $cfg = Get-Config 'projects'
    $projects = $cfg.projects
    if ($Name) {
        $p = $projects | Where-Object { $_.name -eq $Name } | Select-Object -First 1
        if (-not $p) { throw "Projekt '$Name' nicht in config/projects.json gefunden." }
        return $p
    }
    $def = $projects | Where-Object { $_.default -eq $true } | Select-Object -First 1
    if ($def) { return $def }
    return $projects | Select-Object -First 1
}

function Confirm-Action {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host ""
    Write-Host "  $Message" -ForegroundColor Yellow
    $answer = Read-Host "  Fortfahren? (j/N)"
    return ($answer -match '^(j|ja|y|yes)$')
}

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Pause-End {
    param([string]$Message = 'Fertig. Enter zum Schliessen...')
    Write-Host ""
    Read-Host $Message | Out-Null
}

# Findet sqlcmd.exe (PATH oder typische SQL-Tools-Pfade). Wirft, wenn nicht gefunden.
function Find-Sqlcmd {
    $cmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = Get-ChildItem -Path `
        "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\*\Tools\Binn\sqlcmd.exe", `
        "C:\Program Files (x86)\Microsoft SQL Server\Client SDK\ODBC\*\Tools\Binn\sqlcmd.exe" `
        -ErrorAction SilentlyContinue | Sort-Object FullName -Descending
    if ($candidates) { return $candidates[0].FullName }
    throw "sqlcmd.exe nicht gefunden. Bitte SQL Server Command Line Tools installieren oder in den PATH legen."
}

# Findet Ssms.exe in den ueblichen Installationspfaden.
function Find-Ssms {
    $cmd = Get-Command ssms -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = Get-ChildItem -Path `
        "C:\Program Files (x86)\Microsoft SQL Server Management Studio*\Common7\IDE\Ssms.exe", `
        "C:\Program Files\Microsoft SQL Server Management Studio*\Common7\IDE\Ssms.exe" `
        -ErrorAction SilentlyContinue | Sort-Object FullName -Descending
    if ($candidates) { return $candidates[0].FullName }
    throw "Ssms.exe nicht gefunden. Bitte SQL Server Management Studio installieren."
}

# Startet einen langlaufenden Befehl in einem EIGENEN, sichtbaren Fenster,
# das offen bleibt (z. B. dotnet watch run fuer den Blazor-Host).
function Start-DevWindow {
    param(
        [Parameter(Mandatory)][string]$WorkingDir,
        [Parameter(Mandatory)][string]$Command,
        [string]$Title
    )
    if (-not (Test-Path $WorkingDir)) { throw "Verzeichnis nicht gefunden: $WorkingDir" }
    $shell = if (Get-Command pwsh -ErrorAction SilentlyContinue) { 'pwsh' } else { 'powershell' }
    # Hintergrund auf Schwarz (statt PowerShell-Blau), dann Fenster leeren.
    $theme = "`$Host.UI.RawUI.BackgroundColor='Black'; `$Host.UI.RawUI.ForegroundColor='Gray'; Clear-Host; "
    $titleCmd = if ($Title) { "`$Host.UI.RawUI.WindowTitle='$Title'; " } else { '' }
    $inner = $theme + $titleCmd + $Command
    Start-Process $shell -WorkingDirectory $WorkingDir `
        -ArgumentList '-NoExit','-NoProfile','-Command', $inner
}
