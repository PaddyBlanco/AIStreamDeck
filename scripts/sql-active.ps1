# sql-active.ps1 - zeigt die laengsten laufenden Benutzer-Requests (read-only DMV).
# Nur lesend, kein Sperren. Windows-Auth. Aufruf: sql-active.cmd
. "$PSScriptRoot\lib\_common.ps1"

$cfg     = Get-Config 'sql'
$sqlcmd  = Find-Sqlcmd
$top     = if ($cfg.topRows) { [int]$cfg.topRows } else { 25 }
$minMs   = if ($cfg.longRunningSeconds) { [int]$cfg.longRunningSeconds * 1000 } else { 0 }

$query = @"
SET NOCOUNT ON;
SELECT TOP ($top)
    r.session_id            AS SPID,
    r.status                AS Status,
    r.blocking_session_id   AS BlockedBy,
    r.wait_type             AS WaitType,
    r.total_elapsed_time/1000 AS ElapsedSec,
    DB_NAME(r.database_id)  AS DBName,
    s.login_name            AS LoginName,
    s.host_name             AS HostName,
    s.program_name          AS Program,
    SUBSTRING(t.text, (r.statement_start_offset/2)+1,
        ((CASE r.statement_end_offset WHEN -1 THEN DATALENGTH(t.text)
          ELSE r.statement_end_offset END - r.statement_start_offset)/2)+1) AS Statement
FROM sys.dm_exec_requests r
JOIN sys.dm_exec_sessions s ON s.session_id = r.session_id
CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
WHERE s.is_user_process = 1
  AND r.session_id <> @@SPID
  AND r.total_elapsed_time >= $minMs
ORDER BY r.total_elapsed_time DESC;
"@

Write-Step "Aktive Requests auf '$($cfg.server)' / '$($cfg.database)' (>= ${minMs}ms)"
& $sqlcmd -S $cfg.server -d $cfg.database -E -W -Q $query
Pause-End
