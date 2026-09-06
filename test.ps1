param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$testRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
foreach ($requiredFile in @('src\Program.cs', 'src\app.manifest', 'tests\RegressionTests.cs', 'LICENSE')) {
    if (-not (Test-Path -LiteralPath (Join-Path $testRoot $requiredFile) -PathType Leaf)) { throw "필수 프로젝트 파일이 없습니다: $requiredFile" }
}
if (@(Get-ChildItem -LiteralPath $testRoot -Filter '*.cs' -File).Count -ne 0) { throw '원본 코드는 src 또는 tests 폴더에 넣어 주세요.' }
[xml]$manifest = Get-Content -LiteralPath (Join-Path $testRoot 'src\app.manifest') -Raw
$supportedIds = @($manifest.SelectNodes("//*[local-name()='supportedOS']") | ForEach-Object { $_.Id })
if ($supportedIds.Count -ne 1 -or $supportedIds[0] -ne '{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}') { throw '지원 OS 선언이 Windows 10/11 공식 식별자와 다릅니다.' }
$executionLevel = $manifest.SelectSingleNode("//*[local-name()='requestedExecutionLevel']")
if ($executionLevel.level -ne 'asInvoker' -or $executionLevel.uiAccess -ne 'false') { throw '실행 권한 선언이 기본 정책과 다릅니다.' }
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
$testResult | ConvertTo-Json -Depth 4
Write-Output "검사 결과: $testOutput"
if ($testProcess.ExitCode -ne 0 -or -not $testResult.success) { throw '회귀 검사 실패' }
