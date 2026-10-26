# Genera la versión empaquetada de Claude Multi-Account: autocontenida (lleva su propio .NET) y un .zip portátil.
# Solo usa el SDK de .NET y PowerShell; no descarga ni ejecuta nada de terceros (dotnet trae los paquetes
# oficiales de Microsoft que necesita para empaquetar).
#   powershell -NoProfile -File scripts\publish.ps1
param([string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
$out = Join-Path $artifacts 'publish\ClaudeMultiAccount'
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version

# Nada se empaqueta si la solución no compila sin avisos o falla alguna prueba.
& dotnet test (Join-Path $root 'ClaudeMultiAccount.slnx') -nologo
if ($LASTEXITCODE -ne 0) { throw 'Fallan las pruebas: no se empaqueta.' }

if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $out | Out-Null

foreach ($project in 'src\App\App.csproj', 'src\Cli\Cli.csproj') {
    & dotnet publish (Join-Path $root $project) -c Release -r $Runtime --self-contained true -o $out -p:DebugType=None -nologo
    if ($LASTEXITCODE -ne 0) { throw "Falló la publicación de $project." }
}

Copy-Item (Join-Path $root 'docs\GUIA-DE-USO.md') (Join-Path $out 'LEEME - Guia de uso.md')

$zip = Join-Path $artifacts "ClaudeMultiAccount-$version-$Runtime.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
# -LiteralPath: una carpeta con [ o ] en el nombre no se toma como comodín.
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $out | ForEach-Object FullName) -DestinationPath $zip

# Huella SHA-256 para comprobar que el zip que alguien recibe es el mismo que se generó (sin firma de código,
# es la forma de verificarlo; ver docs\GUIA-DE-USO.md).
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii

$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
"Listo: $zip ($size MB)"
"SHA-256: $hash"
"Carpeta portátil: $out"
