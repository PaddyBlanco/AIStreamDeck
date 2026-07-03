# dotnet-build.ps1 - baut die Solution des Projekts.
# Aufruf:  dotnet-build.cmd [Projektname]
param([string]$Project)

. "$PSScriptRoot\lib\_common.ps1"

$p = Get-Project $Project
$target = if ($p.sln) { $p.sln } elseif ($p.backendProject) { $p.backendProject } else { $p.path }
Write-Step "dotnet build: $target"
dotnet build $target
Pause-End "Build fertig. Enter zum Schliessen..."
