[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ConfigPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$ExecutableName = 'LocalAppLauncher.exe',
    [string]$IconPath
)
$ErrorActionPreference = 'Stop'
if ($ExecutableName -match '[/\\]' -or $ExecutableName -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*\.exe$') { throw 'ExecutableName 必须是英文 EXE 文件名。' }
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
if (-not $config.DataDirectory -or -not [IO.Path]::IsPathRooted($config.DataDirectory) -or -not $config.Apps) { throw '配置必须包含绝对 DataDirectory 和 Apps。' }
$seen = @{}
foreach ($app in $config.Apps) {
    if ($app.Id -notmatch '^[a-z0-9][a-z0-9-]*$' -or $seen.ContainsKey($app.Id)) { throw '应用 Id 必须唯一且只含小写英文、数字和连字符。' }
    $seen[$app.Id] = $true
    foreach ($field in @('Executable','Browser')) {
        if (-not (Test-Path -LiteralPath $app.$field -PathType Leaf)) { throw "应用 $($app.Id) 的 $field 文件不存在。" }
    }
    foreach ($field in @('WorkingDirectory','ProjectDirectory')) {
        if (-not (Test-Path -LiteralPath $app.$field -PathType Container)) { throw "应用 $($app.Id) 的 $field 目录不存在。" }
    }
    if (-not $app.Name -or $app.Name.Length -gt 60 -or -not $app.LogDirectory -or -not [IO.Path]::IsPathRooted($app.LogDirectory)) { throw '应用名称须为 1–60 字符，日志目录须为绝对路径。' }
    if (-not $app.Ports -or -not $app.Checks) { throw '每个服务必须有 Ports 和 Checks。' }
    foreach ($port in $app.Ports) { if ($port -lt 1 -or $port -gt 65535) { throw '端口无效。' } }
    foreach ($url in @($app.Url) + @($app.Checks | ForEach-Object { $_.Url })) {
        $uri = [Uri]$url
        if (-not $uri.IsAbsoluteUri -or $uri.Scheme -notin 'http','https' -or -not $uri.IsLoopback) { throw '该模板仅接受本机 HTTP/HTTPS 地址。' }
    }
    foreach ($check in $app.Checks) { if ([string]::IsNullOrWhiteSpace($check.Marker)) { throw '就绪检查必须有服务身份 Marker。' } }
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '本机 .NET Framework C# 编译器不可用。' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$exe = Join-Path $outputRoot $ExecutableName
$assetRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets'
$compileArgs = @('/nologo','/target:winexe','/optimize+','/platform:x64','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll',('/out:' + $exe))
$compileArgs += '/win32manifest:' + (Join-Path $assetRoot 'Launcher.manifest')
if ($IconPath) { $compileArgs += '/win32icon:' + (Get-Item -LiteralPath $IconPath).FullName }
$compileArgs += Join-Path (Split-Path $PSScriptRoot -Parent) 'assets\Launcher.cs'
& $compiler @compileArgs
if ($LASTEXITCODE -ne 0) { throw '编译失败；已有运行中的 EXE 需先通过托盘退出。' }
Copy-Item -LiteralPath (Join-Path $assetRoot 'Launcher.exe.config') -Destination ($exe + '.config')
# Write configuration only after a successful compile, avoiding changes to a running install.
$config | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $outputRoot 'apps.json') -Encoding utf8
[pscustomobject]@{ Executable=$exe; Config=(Join-Path $outputRoot 'apps.json') }
