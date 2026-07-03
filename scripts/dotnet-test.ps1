# dotnet-test.ps1 - fuehrt die Tests der Solution aus.
# Aufruf:  dotnet-test.cmd [Projektname]
param([string]$Project)

. "$PSScriptRoot\lib\_common.ps1"

$p = Get-Project $Project
# 'testTarget' bevorzugen: manche Testprojekte liegen NICHT in der .sln.
$target = if ($p.testTarget) { $p.testTarget } elseif ($p.sln) { $p.sln } else { $p.path }
Write-Step "dotnet test: $target"
dotnet test $target
Pause-End "Tests fertig. Enter zum Schliessen..."
