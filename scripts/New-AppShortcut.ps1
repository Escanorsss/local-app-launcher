[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$Name,
    [Parameter(Mandatory)][string]$TargetPath,
    [string]$Arguments = '',
    [string]$WorkingDirectory,
    [string]$IconPath,
    [string]$Description = '',
    [string]$DestinationDirectory = [Environment]::GetFolderPath('Programs'),
    [switch]$ReplaceExisting
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Name) -or $Name.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or $Name -in '.', '..' -or $Name -match '[. ]$') {
    throw 'Name 必须是有效的快捷方式文件名，不能包含路径。'
}
$target = (Get-Item -LiteralPath $TargetPath).FullName
if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw '目标必须是文件。' }
if (-not $WorkingDirectory) { $WorkingDirectory = Split-Path -LiteralPath $target }
$working = (Get-Item -LiteralPath $WorkingDirectory).FullName
if (-not (Test-Path -LiteralPath $working -PathType Container)) { throw '工作目录必须是目录。' }
if (-not $IconPath) { $IconPath = $target }
$icon = (Get-Item -LiteralPath $IconPath).FullName
$destination = [IO.Path]::GetFullPath($DestinationDirectory)
$linkPath = Join-Path $destination ($Name + '.lnk')
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($linkPath)
if ((Test-Path -LiteralPath $linkPath) -and -not $ReplaceExisting) {
    if ($link.TargetPath -ne $target -or $link.Arguments -ne $Arguments) {
        throw "同名入口已有不同目标或参数，未覆盖：$linkPath"
    }
}
if ($PSCmdlet.ShouldProcess($linkPath, '创建或更新应用快捷方式')) {
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    $link.TargetPath = $target
    $link.Arguments = $Arguments
    $link.WorkingDirectory = $working
    $link.IconLocation = $icon + ',0'
    $link.Description = $Description
    $link.Save()
    $saved = $shell.CreateShortcut($linkPath)
    if ($saved.TargetPath -ne $target -or $saved.Arguments -ne $Arguments -or $saved.WorkingDirectory -ne $working) {
        throw '快捷方式保存后的内容不符合预期。'
    }
    [pscustomobject]@{ Shortcut=$linkPath; Target=$saved.TargetPath; Arguments=$saved.Arguments; WorkingDirectory=$saved.WorkingDirectory; Icon=$saved.IconLocation }
}
