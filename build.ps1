param(
    [switch]$DebugBuild
)

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishDirectory = Join-Path $projectRoot 'publish'
$outputDirectory = Join-Path $publishDirectory 'app'
$releaseZip = Join-Path $publishDirectory 'TaskbarMonitor.zip'
$compilerCandidates = @(
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
    'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
)
$compilerPath = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $compilerPath) {
    throw 'Windows .NET Framework C# compiler(csc.exe)를 찾을 수 없습니다.'
}

$resolvedProjectRoot = [IO.Path]::GetFullPath($projectRoot).TrimEnd('\')
$resolvedOutputDirectory = [IO.Path]::GetFullPath($outputDirectory).TrimEnd('\')
if (-not $resolvedOutputDirectory.StartsWith($resolvedProjectRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "빌드 출력 경로가 프로젝트 폴더 밖입니다: $resolvedOutputDirectory"
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $outputDirectory -Force | Remove-Item -Recurse -Force -ErrorAction Stop
foreach ($legacyName in @('TaskbarMonitor.exe', 'TaskbarMonitor.pdb', 'README.md')) {
    $legacyPath = Join-Path $publishDirectory $legacyName
    if (Test-Path -LiteralPath $legacyPath) {
        Remove-Item -LiteralPath $legacyPath -Force -ErrorAction Stop
    }
}
$outputPath = Join-Path $outputDirectory 'TaskbarMonitor.exe'
$sourceFiles = Get-ChildItem -LiteralPath $projectRoot -Filter '*.cs' | ForEach-Object { $_.FullName }
$gacRoot = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL'
$uiAutomationClient = Join-Path $gacRoot 'UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll'
$uiAutomationTypes = Join-Path $gacRoot 'UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll'
$windowsBase = Join-Path $gacRoot 'WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll'
$compilerOptions = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/win32manifest:app.manifest',
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

& $compilerPath @compilerOptions @sourceFiles
if ($LASTEXITCODE -ne 0) {
    throw "컴파일에 실패했습니다. 종료 코드: $LASTEXITCODE"
}

$readmeSource = Join-Path $projectRoot 'README.md'
$readmeOutput = Join-Path $outputDirectory 'README.md'
if (Test-Path -LiteralPath $readmeSource) {
    Copy-Item -LiteralPath $readmeSource -Destination $readmeOutput -Force
}

Compress-Archive -Path (Join-Path $outputDirectory '*') -DestinationPath $releaseZip -CompressionLevel Optimal -Force

$builtFile = Get-Item -LiteralPath $outputPath
$hash = Get-FileHash -LiteralPath $outputPath -Algorithm SHA256
[pscustomobject]@{
    File = $builtFile.FullName
    SizeBytes = $builtFile.Length
    SHA256 = $hash.Hash
    ReleaseZip = $releaseZip
}
