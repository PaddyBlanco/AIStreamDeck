# dev-up.ps1 - Cold-Start einer Blazor-Dev-Umgebung:
#   VS Code im Repo + dotnet watch (Blazor-Host) + Browser-Tab.
# Aufruf:  dev-up.cmd [Projektname]   (ohne Name -> Default-Projekt aus config)
param([string]$Project)

. "$PSScriptRoot\lib\_common.ps1"

$p = Get-Project $Project
Write-Step "Starte Dev-Umgebung: $($p.name)"

# 1) VS Code im Repo
if (Get-Command code -ErrorAction SilentlyContinue) {
    Write-Step "VS Code -> $($p.path)"
    code $p.path
} else {
    Write-Host "  (Hinweis: 'code' nicht im PATH - VS Code uebersprungen)" -ForegroundColor DarkYellow
}

# 2) Blazor-Host: dotnet watch (Hot Reload)
$profileArg = if ($p.launchProfile) { " --launch-profile `"$($p.launchProfile)`"" } else { '' }
if ($p.backendProject) {
    Write-Step "Host: dotnet watch ($($p.backendProject))"
    Start-DevWindow -WorkingDir (Split-Path $p.backendProject) `
        -Command "dotnet watch --project `"$($p.backendProject)`" run$profileArg" `
        -Title "Blazor - $($p.name)"
} elseif ($p.backendDir) {
    Write-Step "Host: dotnet watch ($($p.backendDir))"
    Start-DevWindow -WorkingDir $p.backendDir -Command "dotnet watch run$profileArg" -Title "Blazor - $($p.name)"
}

# 3) Browser-Tab - nur, wenn das Launch-Profil den Browser NICHT selbst oeffnet.
#    (launchBrowser im Profil oeffnet den Tab bereits -> kein doppelter Tab)
if ($p.url -and -not $p.launchProfile) {
    Write-Step "Browser: $($p.url) (in 4s)"
    Start-Sleep -Seconds 4
    Start-Process $p.url
}

Write-Step "Dev-Umgebung gestartet."
