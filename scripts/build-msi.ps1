param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot "RegionLivePreview/RegionLivePreview.csproj"
$wixArchitecture = switch ($Runtime) {
    "win-x64" { "x64" }
    "win-arm64" { "arm64" }
    default { throw "Unsupported Windows runtime: $Runtime" }
}
$version = (
    Select-String -Path $project -Pattern "<Version>([^<]+)</Version>" |
    Select-Object -First 1
).Matches[0].Groups[1].Value

if (-not $version) {
    throw "Unable to read project version."
}

$wixCandidates = @(
    $env:WIX,
    "${env:ProgramFiles(x86)}\WiX Toolset v3.14\bin",
    "${env:ProgramFiles}\WiX Toolset v3.14\bin",
    "${env:ProgramFiles(x86)}\WiX Toolset v3.11\bin"
) | Where-Object { $_ -and (Test-Path (Join-Path $_ "candle.exe")) }

$wixBin = $wixCandidates | Select-Object -First 1
if (-not $wixBin) {
    throw "WiX Toolset v3 was not found. Install it with 'choco install wixtoolset -y'."
}

$publishDir = Join-Path $projectRoot "artifacts/msi/$Runtime"
$installerDir = Join-Path $projectRoot "artifacts/installers"
$objectDir = Join-Path $projectRoot "artifacts/msi/obj-$Runtime"
New-Item -ItemType Directory -Force -Path $publishDir, $installerDir, $objectDir | Out-Null

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir

$signTool = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
if (-not $signTool -and $env:WINDOWS_CERTIFICATE_PFX_BASE64) {
    $signTool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" `
        -Filter "signtool.exe" `
        -Recurse `
        -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
}

$certificatePath = $null
function Invoke-CodeSign([string]$Path) {
    if (-not $env:WINDOWS_CERTIFICATE_PFX_BASE64) {
        return
    }

    if (-not $signTool) {
        throw "signtool.exe was not found. Configure the Windows SDK on the runner."
    }

    $signToolPath = $signTool.Source ?? $signTool.FullName
    & $signToolPath sign `
        /fd SHA256 `
        /td SHA256 `
        /tr "http://timestamp.digicert.com" `
        /f $certificatePath `
        /p $env:WINDOWS_CERTIFICATE_PASSWORD `
        $Path
}

if ($env:WINDOWS_CERTIFICATE_PFX_BASE64) {
    $certificatePath = Join-Path $env:RUNNER_TEMP "windows-code-signing.pfx"
    [IO.File]::WriteAllBytes(
        $certificatePath,
        [Convert]::FromBase64String($env:WINDOWS_CERTIFICATE_PFX_BASE64))
}

try {
Invoke-CodeSign (Join-Path $publishDir "RegionLivePreview.exe")

$productWxs = Join-Path $projectRoot "packaging/windows/Product.wxs"
$icon = Join-Path $projectRoot "RegionLivePreview/Assets/app.ico"
$productWixobj = Join-Path $objectDir "Product.wixobj"
$output = Join-Path $installerDir "RegionLivePreview-$version-$Runtime.msi"

& (Join-Path $wixBin "candle.exe") `
    -arch $wixArchitecture `
    "-dSourceDir=$publishDir" `
    "-dProductIcon=$icon" `
    "-dProductVersion=$version" `
    "-dMsiPlatform=$wixArchitecture" `
    -out $productWixobj `
    $productWxs

& (Join-Path $wixBin "light.exe") `
    -ext WixUIExtension `
    -cultures:zh-CN `
    -out $output `
    $productWixobj

Invoke-CodeSign $output

if (-not (Test-Path $output)) {
    throw "MSI build failed."
}

Write-Host "Created: $output"
}
finally {
    if ($certificatePath -and (Test-Path $certificatePath)) {
        Remove-Item -LiteralPath $certificatePath -Force
    }
}
