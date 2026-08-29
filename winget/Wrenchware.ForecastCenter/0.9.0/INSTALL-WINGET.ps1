$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

Write-Host 'Installing the Microsoft WinGet PowerShell module...'
Install-PackageProvider -Name NuGet -Force | Out-Null
Install-Module -Name Microsoft.WinGet.Client -Force -Repository PSGallery | Out-Null

Write-Host 'Bootstrapping the stable WinGet client...'
Repair-WinGetPackageManager -AllUsers

Write-Host ''
Write-Host 'WinGet is installed. Close this Terminal window, open Terminal as administrator again, and follow TEST-IN-SANDBOX.txt.' -ForegroundColor Green
