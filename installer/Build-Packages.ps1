param(
    [Parameter(Mandatory)]
    [string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = (Resolve-Path -LiteralPath $InnoCompiler).Path
$language = Join-Path (Split-Path -Parent $compiler) 'Languages\ChineseSimplified.isl'
if (-not (Test-Path -LiteralPath $language -PathType Leaf)) {
    throw 'Place the Inno Setup 6.7.4 Simplified Chinese translation in the compiler Languages directory.'
}

$project = Join-Path $root 'src\TempAutoCleaner\TempAutoCleaner.csproj'
$binary = Join-Path (Split-Path -Parent $project) 'bin\Release\net48\TempAutoCleaner.exe'
dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { throw "Release build failed ($LASTEXITCODE)." }

$version = (Get-Item -LiteralPath $binary).VersionInfo.ProductVersion.Split('+')[0]
if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw 'Unexpected cleaner version.' }
$output = (New-Item -ItemType Directory -Force -Path (Join-Path $root 'dist')).FullName
& $compiler /Qp "/DAppVersion=$version" "/O$output" (Join-Path $PSScriptRoot 'TempAutoCleaner.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed ($LASTEXITCODE)." }

$files = @(
    $binary
    (Join-Path $root 'LICENSE')
    (Join-Path $root 'README.md')
    (Join-Path $root 'README.zh-CN.md')
)
$portable = Join-Path $output 'TempAutoCleaner-Portable-x64.zip'
Compress-Archive -LiteralPath $files -DestinationPath $portable -Force
$setup = Join-Path $output 'TempAutoCleaner-Setup-x64.exe'
Get-FileHash -LiteralPath $setup,$portable -Algorithm SHA256 |
    ForEach-Object { '{0}  {1}' -f $_.Hash.ToLowerInvariant(), (Split-Path -Leaf $_.Path) } |
    Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding Ascii
Get-Item -LiteralPath $setup,$portable,(Join-Path $output 'SHA256SUMS.txt') | Select-Object Name,Length
