# open-sln.ps1 - oeffnet die .sln des Projekts in Visual Studio.
# Aufruf:  open-sln.cmd [Projektname]
param([string]$Project)

. "$PSScriptRoot\lib\_common.ps1"

$p = Get-Project $Project
if (-not $p.sln) { throw "Fuer Projekt '$($p.name)' ist keine 'sln' in config/projects.json hinterlegt." }
if (-not (Test-Path $p.sln)) { throw "Solution nicht gefunden: $($p.sln)" }

Write-Step "Oeffne Solution: $($p.sln)"
# Standard-Verknuepfung fuer .sln (Visual Studio) verwenden.
Start-Process $p.sln
