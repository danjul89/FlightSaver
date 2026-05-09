# FlightSaver one-liner installer
# Usage from PowerShell:
#   iwr -useb https://raw.githubusercontent.com/danjul89/FlightSaver/main/install.ps1 | iex

$ErrorActionPreference = 'Stop'
$repo    = 'danjul89/FlightSaver'
$tempScr = Join-Path $env:TEMP 'FlightSaver-install.scr'
$dest    = "$env:SystemRoot\System32\FlightSaver.scr"

Write-Host ''
Write-Host '  FlightSaver installer' -ForegroundColor Cyan
Write-Host '  ---------------------'

Write-Host '  Looking up latest release...'
$headers = @{ 'User-Agent' = 'FlightSaver-Install' }
$latest  = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers $headers
$asset   = $latest.assets | Where-Object { $_.name -like '*.scr' } | Select-Object -First 1
if (-not $asset) { throw "No .scr asset in latest release ($($latest.tag_name))" }

$sizeMb = [math]::Round($asset.size / 1MB, 1)
Write-Host "  Downloading $($latest.tag_name) ($sizeMb MB) ..."
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $tempScr -UseBasicParsing -Headers $headers

Write-Host '  Closing any running FlightSaver processes...'
Get-Process -Name 'FlightSaver', 'FlightSaver.scr' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host "  Installing to $dest"
Write-Host '  (Windows will show a UAC prompt - click Yes to allow)' -ForegroundColor Yellow
$copyCmd = "Copy-Item -LiteralPath '$tempScr' -Destination '$dest' -Force"
$proc = Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile', '-WindowStyle', 'Hidden', '-Command', $copyCmd -Wait -PassThru
if ($proc.ExitCode -ne 0) { throw "Installation failed (exit $($proc.ExitCode))" }

Remove-Item -LiteralPath $tempScr -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host '  Done!' -ForegroundColor Green
Write-Host '  Opening Windows screensaver settings with FlightSaver pre-selected...'
Write-Host ''

Start-Process rundll32 -ArgumentList "desk.cpl,InstallScreenSaver $dest"
