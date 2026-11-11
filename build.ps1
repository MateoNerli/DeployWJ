<#
.SYNOPSIS
    Genera DeployWebJobs.exe (consola) y DeployWebJobsUI.exe (ventanas): un único archivo cada uno,
    self-contained, para Windows x64 (no necesitan .NET instalado).

.DESCRIPTION
    1) Verifica que haya .NET SDK 10 o superior.
    2) Corre los tests (se puede saltear con -SkipTests).
    3) Publica con:
         dotnet publish -c Release -r win-x64 --self-contained true
                        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
    4) Deja el resultado en .\dist\

.PARAMETER Only
    Cli = solo la consola, Ui = solo la versión con ventanas, All = las dos (por defecto).

.EXAMPLE
    .\build.ps1
    .\build.ps1 -SkipTests
    .\build.ps1 -Only Ui
    .\build.ps1 -OutputDir C:\Herramientas\DeployWebJobs
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = (Join-Path $PSScriptRoot "dist"),
    [ValidateSet("All", "Cli", "Ui")]
    [string]$Only = "All",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$solution = Join-Path $PSScriptRoot "DeployWebJobs.slnx"

# Qué se publica: nombre visible, proyecto y exe resultante
$targets = @()
if ($Only -eq "All" -or $Only -eq "Cli") {
    $targets += @{ Name = "consola";  Project = Join-Path $PSScriptRoot "src\DeployWebJobs.Cli\DeployWebJobs.Cli.csproj"; Exe = "DeployWebJobs.exe" }
}
if ($Only -eq "All" -or $Only -eq "Ui") {
    $targets += @{ Name = "ventanas"; Project = Join-Path $PSScriptRoot "src\DeployWebJobs.Ui\DeployWebJobs.Ui.csproj";   Exe = "DeployWebJobsUI.exe" }
}

function Invoke-Step([string]$title, [scriptblock]$action) {
    Write-Host ""
    Write-Host "==> $title" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) { throw "Falló el paso: $title (código $LASTEXITCODE)" }
}

# ---- 1) .NET SDK
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "No se encontró 'dotnet'. Instalá el .NET SDK 10 desde https://dotnet.microsoft.com/download"
}
$sdk = (& dotnet --version).Trim()
if ([int]($sdk.Split('.')[0]) -lt 10) {
    throw "Se necesita .NET SDK 10 o superior (tenés $sdk)."
}

# Versión que se va a generar (sale de Directory.Build.props)
[xml]$props = Get-Content (Join-Path $PSScriptRoot "Directory.Build.props")
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
Write-Host "DeployWebJobs v$version  (SDK $sdk)" -ForegroundColor Green

# ---- 2) Tests
if (-not $SkipTests) {
    Invoke-Step "Corriendo tests" { dotnet test $solution -c $Configuration --nologo }
}

# ---- 3) Publicar
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }

foreach ($t in $targets) {
    $project = $t.Project
    Invoke-Step "Publicando versión $($t.Name) (win-x64, self-contained, un solo archivo)" {
        dotnet publish $project `
            -c $Configuration `
            -r win-x64 `
            --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:EnableCompressionInSingleFile=true `
            -p:DebugType=None `
            -p:DebugSymbols=false `
            -o $OutputDir `
            --nologo
    }
}

# ---- 4) Resultado
Write-Host ""
Write-Host "Listo." -ForegroundColor Green
foreach ($t in $targets) {
    $exe = Join-Path $OutputDir $t.Exe
    if (-not (Test-Path $exe)) { throw "No se generó $exe" }
    $sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    $fileVersion = (Get-Item $exe).VersionInfo.ProductVersion
    Write-Host "  $($t.Exe)  ($sizeMb MB, versión $fileVersion)"
}
Write-Host "  Carpeta : $OutputDir"
Write-Host ""
Write-Host "Copiá los .exe donde quieras y ejecutalos. No requieren instalar .NET." -ForegroundColor Gray
