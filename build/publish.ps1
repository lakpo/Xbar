<#
    Construit les livrables de Xbar.

        pwsh build\publish.ps1

    Produit dans dist\ :
      - Xbar-<version>-portable\      version portable (marqueur "portable", données à côté de l'exe)
      - Xbar-<version>-portable.zip
      - install\                      contenu prêt pour l'installateur
      - Xbar-<version>-setup.exe      si Inno Setup (ISCC.exe) est disponible

    Les deux versions sont autonomes (self-contained, fichier unique) : aucun runtime
    .NET n'est requis sur la machine de l'utilisateur.
#>

param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Xbar\Xbar.csproj'
$dist = Join-Path $root 'dist'

$dotnet = 'dotnet'
$userDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (Test-Path $userDotnet) { $dotnet = $userDotnet }

$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { $version = '1.0.0' }
Write-Host "Xbar $version — publication ($Configuration / $Runtime)" -ForegroundColor Cyan

$installDir = Join-Path $dist 'install'
$portableDir = Join-Path $dist "Xbar-$version-portable"
$portableData = Join-Path $portableDir 'Data'

# Une instance lancee depuis dist\ verrouille Xbar.exe : la suppression echouerait, et le
# controle a lieu AVANT de deplacer quoi que ce soit.
$running = @(Get-Process -Name Xbar -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($dist, [StringComparison]::OrdinalIgnoreCase) })
if ($running.Count -gt 0) {
    throw "Xbar est en cours d'execution depuis dist\ (PID $($running.Id -join ', ')). Fermez-le puis relancez."
}

# La reconstruction efface dist\, qui contient aussi la configuration de la version portable
# (Data\ : settings.json, shortcuts.json, history.json, icons\, logs\). On la met de cote,
# et le bloc finally garantit sa restauration meme si la publication echoue en cours de route.
$savedData = $null
if (Test-Path $portableData) {
    $savedData = Join-Path ([System.IO.Path]::GetTempPath()) ("xbar-data-" + [guid]::NewGuid().ToString('N'))
    Move-Item $portableData $savedData
    Write-Host "Configuration portable existante mise de cote, elle sera restauree." -ForegroundColor Yellow
}

try {

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

& $dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded `
    -o $installDir
if ($LASTEXITCODE -ne 0) { throw "La publication a échoué." }

# --- Version portable : même contenu + marqueur "portable" ---
Copy-Item $installDir $portableDir -Recurse
New-Item -ItemType File -Path (Join-Path $portableDir 'portable') -Force | Out-Null

@"
Xbar $version — version portable

Lancez Xbar.exe. La configuration et les journaux sont stockés dans le sous-dossier Data\,
à côté de l'exécutable ; rien n'est écrit ailleurs sur le système.

Le démarrage avec Windows est désactivé par défaut en version portable ; il peut être
activé depuis l'icône de la zone de notification.
"@ | Set-Content (Join-Path $portableDir 'LISEZMOI.txt') -Encoding UTF8

$zip = Join-Path $dist "Xbar-$version-portable.zip"
Compress-Archive -Path (Join-Path $portableDir '*') -DestinationPath $zip -Force
Write-Host "Portable : $zip" -ForegroundColor Green


# --- Installateur (optionnel : nécessite Inno Setup 6) ---
$iscc = @(
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    & $iscc "/DAppVersion=$version" "/DPayload=$installDir" (Join-Path $PSScriptRoot 'Xbar.iss')
    if ($LASTEXITCODE -ne 0) { throw "La compilation de l'installateur a échoué." }
    Write-Host "Installateur : $dist\Xbar-$version-setup.exe" -ForegroundColor Green
}
else {
    Write-Warning "Inno Setup 6 introuvable : l'installateur n'a pas été généré."
    Write-Warning "Installez-le (https://jrsoftware.org/isdl.php) puis relancez ce script."
}

Write-Host "Terminé." -ForegroundColor Cyan

}
finally {
    # Restauration APRES l'archive (la configuration personnelle ne doit pas s'y retrouver)
    # et quoi qu'il arrive : une erreur de publication ne doit jamais la laisser orpheline.
    if ($savedData -and (Test-Path $savedData)) {
        New-Item -ItemType Directory -Path $portableDir -Force | Out-Null
        Move-Item $savedData $portableData -Force
        Write-Host "Configuration portable restauree." -ForegroundColor Green
    }
}
