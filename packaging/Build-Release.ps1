param([string]$IsccPath, [string]$Version='2.1.0')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $root
if($Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Version must use major.minor.patch format.'}
if(!$IsccPath){$IsccPath=Join-Path $root '.build-tools/InnoSetup/ISCC.exe'}
if(!(Test-Path -LiteralPath $IsccPath)){throw 'Pass -IsccPath with the path to the Inno Setup compiler.'}
$stage=Join-Path $root '.release'
$dist=Join-Path $root 'dist'
# Only clear this build script's staging directory, never personal app settings.
if([IO.Path]::GetFullPath($stage) -ne (Join-Path $root '.release')){throw 'Unsafe staging directory'}
if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}
New-Item -ItemType Directory -Force $stage,$dist,(Join-Path $root '.build-tools') | Out-Null
$installed=Join-Path $stage 'installed'
& dotnet publish WinUI3Prototype/WallpaperStudio.csproj -c Release --no-restore -o $installed -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false -v minimal
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
Copy-Item README.md,THIRD-PARTY-NOTICES.md -Destination $installed
$assetsManifest=Get-Content WinUI3Prototype/obj/project.assets.json -Raw | ConvertFrom-Json
$packageRoot=@($assetsManifest.packageFolders.PSObject.Properties.Name)[0]
$licenses=Join-Path $installed 'Licenses'
New-Item -ItemType Directory -Force $licenses | Out-Null
$packages=@($assetsManifest.libraries.PSObject.Properties.Name)
$runtime=Get-Content (Join-Path $installed 'WallpaperStudio.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach($framework in $runtime.runtimeOptions.includedFrameworks){
    if($framework.name -eq 'Microsoft.NETCore.App'){$packages+='microsoft.netcore.app.runtime.win-x64/'+$framework.version}
}
foreach($package in $packages){
    $folder=Join-Path $packageRoot $package.ToLowerInvariant()
    if(Test-Path -LiteralPath $folder){
        Get-ChildItem -LiteralPath $folder -File | Where-Object {$_.Name -match '^(license|notice|third-party-notices)'} | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $licenses ($package.Replace('/','-')+'-'+$_.Name))
        }
    }
}
$forbidden=@('initial-config.json','config.json','Cafe.png','Home.png','Default.png','LockScreen.png','reference-photo.png','*.pdb')
foreach($pattern in $forbidden){if(Get-ChildItem -LiteralPath $installed -Recurse -File -Filter $pattern){throw "Private or development file in release: $pattern"}}
$portable=Join-Path $stage 'R-Wallpaper-Studio-Portable'
Copy-Item -LiteralPath $installed -Destination $portable -Recurse
Set-Content -LiteralPath (Join-Path $portable 'portable.flag') -Value 'Keep this file beside WallpaperStudio.exe to store settings in Data and disable startup registration.' -Encoding UTF8
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Zip-Directory([string]$Source,[string]$Name){
    $target=Join-Path $dist $Name
    if(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target -Force}
    [IO.Compression.ZipFile]::CreateFromDirectory($Source,$target,[IO.Compression.CompressionLevel]::Optimal,$false)
}
Zip-Directory $portable "R-Wallpaper-Studio-$Version-win-x64-portable.zip"
$bootstrap=Join-Path $root '.build-tools/MicrosoftEdgeWebview2Setup.exe'
if(!(Test-Path -LiteralPath $bootstrap)){
    Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrap
}
$signature=Get-AuthenticodeSignature -LiteralPath $bootstrap
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){
    throw 'WebView2 bootstrapper must have a valid Microsoft signature.'
}
$installerOutput=Join-Path ([IO.Path]::GetTempPath()) ('RWallpaperInstaller-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $installerOutput | Out-Null
# Compile executable resources outside cloud-synced folders, then copy the completed file.
& $IsccPath "/DAppVersion=$Version" "/O$installerOutput" packaging/WallpaperStudio.iss
if($LASTEXITCODE -ne 0){throw "Installer compilation failed. Compiler output: $installerOutput"}
Copy-Item -LiteralPath (Join-Path $installerOutput "R-Wallpaper-Studio-$Version-win-x64-Setup.exe") -Destination $dist -Force

# Explicit source allowlist: never export the development workspace wholesale.
$source=Join-Path $stage 'source'
New-Item -ItemType Directory -Force $source,(Join-Path $source 'WinUI3Prototype') | Out-Null
Copy-Item WallpaperService.cs,StudioPaths.cs,ModernTrayMenu.cs,BackendChecks.cs,BackendTransitionChecks.cs,app_icon.ico,app_icon.png,README.md,THIRD-PARTY-NOTICES.md,.gitignore -Destination $source
Copy-Item -LiteralPath packaging -Destination $source -Recurse
New-Item -ItemType Directory -Force (Join-Path $source 'WallpaperWorker') | Out-Null
Get-ChildItem WallpaperWorker -File | Where-Object {$_.Extension -in '.cs','.csproj'} | Copy-Item -Destination (Join-Path $source 'WallpaperWorker')
Get-ChildItem WinUI3Prototype -File | Where-Object {$_.Extension -in '.cs','.xaml','.csproj','.manifest' -or $_.Name -eq 'NuGet.Config'} | Copy-Item -Destination (Join-Path $source 'WinUI3Prototype')
$assets=Join-Path $root 'WinUI3Prototype/Assets'
Get-ChildItem -LiteralPath $assets -Recurse -File | Where-Object {$_.Name -notin @('Cafe.png','Home.png','Default.png','LockScreen.png')} | ForEach-Object {
    $relative=$_.FullName.Substring($assets.Length).TrimStart('\')
    $destination=Join-Path $source ('WinUI3Prototype/Assets/'+$relative)
    New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $destination
}
Zip-Directory $source "R-Wallpaper-Studio-$Version-Source.zip"
$artifacts=@("R-Wallpaper-Studio-$Version-win-x64-Setup.exe","R-Wallpaper-Studio-$Version-win-x64-portable.zip","R-Wallpaper-Studio-$Version-Source.zip")
$artifacts | ForEach-Object { $hash=Get-FileHash -LiteralPath (Join-Path $dist $_) -Algorithm SHA256; $hash.Hash.ToLowerInvariant()+'  '+$_ } | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ASCII
Get-ChildItem -LiteralPath $dist | Select-Object Name,Length
