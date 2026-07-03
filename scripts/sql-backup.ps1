# sql-backup.ps1 - erstellt ein COPY_ONLY-Vollbackup der Datenbank.
# [BRAUCHT FREIGABE] Schwergewichtige, produktionsnahe Aktion!
#   - Sehr grosse DB => Backup kann sehr gross/lang sein und I/O erzeugen.
#   - Doppelte Bestaetigung. Windows-Auth. KEINE Credentials im Skript.
# Aufruf: sql-backup.cmd
. "$PSScriptRoot\lib\_common.ps1"

$cfg    = Get-Config 'sql'
$sqlcmd = Find-Sqlcmd

$backupDir = if ($cfg.backupDir) { $cfg.backupDir } else { $null }
if (-not $backupDir) { throw "Bitte 'backupDir' in config/sql.json setzen (Zielordner fuer das Backup)." }
if (-not (Test-Path $backupDir)) { throw "backupDir existiert nicht: $backupDir (muss fuer den SQL-Server-Dienst erreichbar/beschreibbar sein)." }

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$file  = Join-Path $backupDir "$($cfg.database)_COPYONLY_$stamp.bak"

Write-Host ""
Write-Host "  ACHTUNG: Vollbackup einer SEHR GROSSEN Datenbank." -ForegroundColor Red
Write-Host "  Server  : $($cfg.server)" -ForegroundColor Yellow
Write-Host "  DB      : $($cfg.database)" -ForegroundColor Yellow
Write-Host "  Ziel    : $file" -ForegroundColor Yellow
Write-Host "  Das kann lange dauern und viel Speicher/IO belegen." -ForegroundColor Red

if (-not (Confirm-Action "Backup wirklich starten?")) { Write-Host "Abgebrochen." -ForegroundColor DarkYellow; Pause-End; return }
if (-not (Confirm-Action "Letzte Bestaetigung - jetzt BACKUP DATABASE ausfuehren?")) { Write-Host "Abgebrochen." -ForegroundColor DarkYellow; Pause-End; return }

$query = @"
BACKUP DATABASE [$($cfg.database)]
TO DISK = N'$file'
WITH COPY_ONLY, COMPRESSION, INIT, STATS = 5,
     NAME = N'$($cfg.database) COPY_ONLY Full';
"@

Write-Step "Starte Backup..."
& $sqlcmd -S $cfg.server -d 'master' -E -b -Q $query
if ($LASTEXITCODE -eq 0) { Write-Host "Backup erfolgreich: $file" -ForegroundColor Green }
else { Write-Host "Backup FEHLGESCHLAGEN (ExitCode $LASTEXITCODE)." -ForegroundColor Red }
Pause-End
