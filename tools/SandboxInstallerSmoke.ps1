param(
    [Parameter(Mandatory = $true)] [string] $Installer,
    [Parameter(Mandatory = $true)] [string] $ResultPath
)

$ErrorActionPreference = 'Stop'
$result = [ordered]@{
    StartedAt = (Get-Date).ToString('o')
    Passed = $false
    InstallerExit = $null
    InstalledVersion = $null
    LaunchResponsive = $false
    WebViewDataCreated = $false
    UninstallExit = $null
    InstalledFilesRemaining = $null
    Error = $null
}

try {
    $install = Start-Process -FilePath $Installer -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-' -Wait -PassThru
    $result.InstallerExit = $install.ExitCode
    if ($install.ExitCode -ne 0) { throw "Installer returned $($install.ExitCode)." }

    $installFolder = Join-Path $env:LOCALAPPDATA 'Programs\Forecast Center Public'
    $executable = Join-Path $installFolder 'ForecastCenter.Public.exe'
    if (-not (Test-Path -LiteralPath $executable)) { throw 'Installed executable was not found.' }
    $result.InstalledVersion = (Get-Item -LiteralPath $executable).VersionInfo.ProductVersion.Trim()

    $app = Start-Process -FilePath $executable -PassThru
    Start-Sleep -Seconds 15
    $app.Refresh()
    $result.LaunchResponsive = (-not $app.HasExited) -and $app.Responding
    $result.WebViewDataCreated = Test-Path (Join-Path $env:LOCALAPPDATA 'Forecast Center Public\WebView2')
    if (-not $app.HasExited) {
        $null = $app.CloseMainWindow()
        Start-Sleep -Seconds 2
        if (-not $app.HasExited) { Stop-Process -Id $app.Id }
    }

    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{E37B02AB-5198-48E6-9C55-ECF2295E8C43}_is1'
    $uninstaller = (Get-ItemProperty -LiteralPath $uninstallKey).UninstallString.Trim('"')
    $uninstall = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru
    $result.UninstallExit = $uninstall.ExitCode
    $result.InstalledFilesRemaining = (Get-ChildItem -LiteralPath $installFolder -Force -Recurse -ErrorAction SilentlyContinue | Measure-Object).Count
    $result.Passed = $result.InstallerExit -eq 0 -and
        $result.InstalledVersion -eq '0.8.0' -and
        $result.LaunchResponsive -and
        $result.WebViewDataCreated -and
        $result.UninstallExit -eq 0 -and
        $result.InstalledFilesRemaining -eq 0
}
catch {
    $result.Error = $_.Exception.ToString()
}
finally {
    $result.CompletedAt = (Get-Date).ToString('o')
    $result | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding utf8
    shutdown.exe /s /t 0
}
