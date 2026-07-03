# claude-here.ps1 - oeffnet ein CMD-Fenster im Projektordner und startet Claude Code.
# Aufruf:  claude-here.cmd [Projektname]
# Startet in CMD und OHNE ANTHROPIC_API_KEY -> Claude Code nutzt das Abo, keine API-Credits.
param([string]$Project)

. "$PSScriptRoot\lib\_common.ps1"

$p = Get-Project $Project
if (-not (Test-Path $p.path)) { throw "Projektordner nicht gefunden: $($p.path)" }

Write-Step "Claude Code (CMD, ohne API-Key) in: $($p.path)"

# In der CMD-Sitzung den API-Key leeren, dann 'claude' starten (Abo statt API).
Start-Process 'cmd' -WorkingDirectory $p.path -ArgumentList '/k', 'set "ANTHROPIC_API_KEY=" && claude'
