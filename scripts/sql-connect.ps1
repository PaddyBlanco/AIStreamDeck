# sql-connect.ps1 - oeffnet SSMS und verbindet per Windows-Auth (Integrated Security)
# mit dem in config/sql.json hinterlegten Server/DB. KEINE Credentials.
. "$PSScriptRoot\lib\_common.ps1"

$cfg = Get-Config 'sql'
$ssms = Find-Ssms
Write-Step "SSMS -> Server '$($cfg.server)', DB '$($cfg.database)' (Windows-Auth)"
# -S Server  -d Datenbank  -E Integrated Security (Windows-Authentifizierung)
Start-Process $ssms -ArgumentList @('-S', $cfg.server, '-d', $cfg.database, '-E')
