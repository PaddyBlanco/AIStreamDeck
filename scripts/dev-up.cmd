@echo off
rem Wrapper: ruft das gleichnamige .ps1 auf (bevorzugt pwsh, sonst Windows PowerShell).
where pwsh >nul 2>nul && (set "PS=pwsh") || (set "PS=powershell")
"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev-up.ps1" %*
