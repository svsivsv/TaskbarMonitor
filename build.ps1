param(
    [switch]$DebugBuild
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$releaseDirectory = Join-Path $projectRoot 'release'
$outputDirectory = $releaseDirectory
$compilerCandidates = @(
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
    'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
)
$compilerPath = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $compilerPath) {
    throw 'Windows .NET Framework C# compiler(csc.exe)를 찾을 수 없습니다.'
}

$resolvedProjectRoot = [IO.Path]::GetFullPath($projectRoot).TrimEnd('\')
$resolvedReleaseDirectory = [IO.Path]::GetFullPath($releaseDirectory).TrimEnd('\')
if (-not $resolvedReleaseDirectory.StartsWith($resolvedProjectRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "빌드 출력 경로가 프로젝트 폴더 밖입니다: $resolvedReleaseDirectory"
}

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$finalOutputPath = Join-Path $outputDirectory 'TaskbarMonitor.exe'
$runningOutput = Get-Process -Name TaskbarMonitor -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $finalOutputPath }
if ($runningOutput) {
    throw '이 EXE가 실행 중입니다. 트레이 메뉴에서 종료한 뒤 다시 빌드하세요. 기존 EXE와 설정은 변경하지 않았습니다.'
}
$buildDirectory = Join-Path $releaseDirectory ('.build-' + [Guid]::NewGuid().ToString('N'))
$outputPath = Join-Path $buildDirectory 'TaskbarMonitor.exe'
$manifestPath = Join-Path $projectRoot 'src\app.manifest'
$licensePath = Join-Path $projectRoot 'LICENSE'
$sourceFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -File
    Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests') -Filter '*.cs' -File
) | Sort-Object FullName | ForEach-Object { $_.FullName }
$gacRoot = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL'
$uiAutomationClient = Join-Path $gacRoot 'UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll'
$uiAutomationTypes = Join-Path $gacRoot 'UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll'
$windowsBase = Join-Path $gacRoot 'WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll'
$compilerOptions = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    "/win32manifest:$manifestPath",
    "/resource:$licensePath,TaskbarMonitor.LICENSE.txt",
    "/out:$outputPath",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Web.Extensions.dll',
    "/reference:$uiAutomationClient",
    "/reference:$uiAutomationTypes",
    "/reference:$windowsBase"
)

if ($DebugBuild) {
    $compilerOptions += @('/debug:full', '/optimize-')
} else {
    $compilerOptions += @('/debug-', '/optimize+')
}

New-Item -ItemType Directory -Path $buildDirectory | Out-Null
try {
    & $compilerPath @compilerOptions @sourceFiles
    if ($LASTEXITCODE -ne 0) {
        throw "컴파일에 실패했습니다. 기존 EXE는 유지됩니다. 종료 코드: $LASTEXITCODE"
    }
    if (Test-Path -LiteralPath $finalOutputPath) {
        [IO.File]::Replace($outputPath, $finalOutputPath, [NullString]::Value)
    } else {
        [IO.File]::Move($outputPath, $finalOutputPath)
    }
} finally {
    $resolvedBuildDirectory = [IO.Path]::GetFullPath($buildDirectory).TrimEnd('\')
    if (-not $resolvedBuildDirectory.StartsWith($resolvedReleaseDirectory + '\.build-', [StringComparison]::OrdinalIgnoreCase)) {
        throw "임시 빌드 경로 검증에 실패했습니다: $resolvedBuildDirectory"
    }
    if (Test-Path -LiteralPath $resolvedBuildDirectory) {
        Remove-Item -LiteralPath $resolvedBuildDirectory -Recurse -Force
    }
}

$builtFile = Get-Item -LiteralPath $finalOutputPath
$hash = Get-FileHash -LiteralPath $finalOutputPath -Algorithm SHA256
[pscustomobject]@{
    File = $builtFile.FullName
    SizeBytes = $builtFile.Length
    SHA256 = $hash.Hash
}
