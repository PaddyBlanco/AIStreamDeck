# sql-locks.ps1 - zeigt aktuelle Blocking-/Wait-Situationen (read-only DMV).
# Nur lesend. Windows-Auth. Aufruf: sql-locks.cmd
. "$PSScriptRoot\lib\_common.ps1"

$cfg    = Get-Config 'sql'
$sqlcmd = Find-Sqlcmd

$query = @"
SET NOCOUNT ON;
SELECT
    w.session_id            AS WaitingSPID,
    w.blocking_session_id   AS BlockingSPID,
    w.wait_type             AS WaitType,
    w.wait_duration_ms      AS WaitMs,
    w.resource_description  AS Resource,
    DB_NAME(s.database_id)  AS DBName,
    s.login_name            AS WaitingLogin,
    s.host_name             AS WaitingHost,
    bs.login_name           AS BlockingLogin,
    bs.host_name            AS BlockingHost
FROM sys.dm_os_waiting_tasks w
JOIN sys.dm_exec_sessions s  ON s.session_id  = w.session_id
LEFT JOIN sys.dm_exec_sessions bs ON bs.session_id = w.blocking_session_id
WHERE w.blocking_session_id IS NOT NULL
  AND w.blocking_session_id <> w.session_id
ORDER BY w.wait_duration_ms DESC;
"@

Write-Step "Blocking/Locks auf '$($cfg.server)' / '$($cfg.database)'"
& $sqlcmd -S $cfg.server -d $cfg.database -E -W -Q $query
Write-Host "(Leeres Ergebnis = keine Blockaden)" -ForegroundColor DarkGray
Pause-End
