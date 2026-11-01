param([Parameter(Mandatory=$true)][string]$InstallDirectory, [switch]$Uninstall)
$ErrorActionPreference='Stop'
$directory=[IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
$targets=@((Join-Path $directory 'WallpaperStudio.exe'),(Join-Path $directory 'WallpaperService.exe'),(Join-Path $directory 'WallpaperWorker.exe'))
# Never stop another copy, a portable copy, or an unrelated process with the same name.
Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $targets -contains $_.ExecutablePath } | ForEach-Object {
    Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
}
if($Uninstall){
    $key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $value=(Get-ItemProperty -LiteralPath $key -Name WallpaperService -ErrorAction SilentlyContinue).WallpaperService
    if($value -eq ('"'+$targets[1]+'"')){ Remove-ItemProperty -LiteralPath $key -Name WallpaperService }
}
