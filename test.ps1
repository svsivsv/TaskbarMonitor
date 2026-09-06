param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$testRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $SkipBuild) { & (Join-Path $testRoot 'build.ps1') }
$testOutput = Join-Path ([IO.Path]::GetTempPath()) ('TaskbarMonitor-regression-' + [Guid]::NewGuid().ToString('N') + '.json')
$testExe = Join-Path $testRoot 'release\TaskbarMonitor.exe'
$testProcess = Start-Process -FilePath $testExe -ArgumentList @('--regression-test', ('"' + $testOutput + '"')) -WindowStyle Hidden -PassThru
if (-not $testProcess.WaitForExit(60000)) {
    $testProcess.Kill()
    throw '회귀 검사 전용 프로세스가 60초 내에 종료되지 않았습니다.'
}
$testProcess.Refresh()
$testResult = Get-Content -LiteralPath $testOutput -Raw | ConvertFrom-Json
$testResult
Write-Output "검사 결과: $testOutput"
if ($testProcess.ExitCode -ne 0 -or -not $testResult.success) { throw '회귀 검사 실패' }
