@echo off
where pwsh >nul 2>nul && (set "PS=pwsh") || (set "PS=powershell")
"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0restart-backend.ps1" %*
