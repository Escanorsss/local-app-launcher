$ErrorActionPreference = 'Stop'
$skillRoot = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $PSScriptRoot 'skill-checks'
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$shortcutScript = Join-Path $skillRoot 'scripts\New-AppShortcut.ps1'
$cmd = Join-Path $env:WINDIR 'System32\cmd.exe'
$powershellExe = (Get-Process -Id $PID).Path
$common = @{Name='Fixture app';TargetPath=$cmd;DestinationDirectory=$testRoot;Description='Generated test fixture; do not launch'}
$first = & $shortcutScript @common
$second = & $shortcutScript @common
if ($first.Target -ne $cmd -or $second.Target -ne $cmd) { throw '幂等入口验证失败。' }
$conflictRefused = $false
try { & $shortcutScript -Name 'Fixture app' -TargetPath $powershellExe -DestinationDirectory $testRoot | Out-Null }
catch { if ($_.Exception.Message -like '*同名入口已有不同目标*') { $conflictRefused = $true } else { throw } }
if (-not $conflictRefused) { throw '同名入口冲突未拒绝。' }
$shell = New-Object -ComObject WScript.Shell
if ($shell.CreateShortcut($first.Shortcut).TargetPath -ne $cmd) { throw '冲突改变了已有入口。' }
& $shortcutScript -Name 'DryRun app' -TargetPath $cmd -DestinationDirectory $testRoot -WhatIf
if (Test-Path -LiteralPath (Join-Path $testRoot 'DryRun app.lnk')) { throw 'WhatIf 写入了文件。' }
$config = @{
    DataDirectory = (Join-Path $testRoot 'state')
    Apps = @(@{
        Id='skill-fixture';Name='验证配置';Executable=$cmd;Arguments='';WorkingDirectory=$testRoot;ProjectDirectory=$testRoot
        Browser=$cmd
        Url='http://127.0.0.1:1/';Ports=@(1);Checks=@(@{Url='http://127.0.0.1:1/';Marker='fixture-only'})
        LogDirectory=(Join-Path $testRoot 'logs');Environment=@{}
    })
}
$configPath = Join-Path $testRoot 'fixture-config.json'
$config | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $configPath -Encoding utf8
$build = & (Join-Path $skillRoot 'scripts\Build-ServiceLauncher.ps1') -ConfigPath $configPath -OutputDirectory (Join-Path $testRoot 'build')
$verifyPath = Join-Path $testRoot 'fixture-verify.json'
$verifier = Start-Process -FilePath $build.Executable -ArgumentList @('skill-fixture','--verify',('"' + $verifyPath + '"')) -WindowStyle Hidden -Wait -PassThru
$verification = Get-Content -LiteralPath $verifyPath -Raw | ConvertFrom-Json
if ($verifier.ExitCode -ne 0 -or -not $verification.success -or $verification.status.owned) { throw '模板编译/配置验证失败。' }
$dpiPath = Join-Path $testRoot 'ui-verify.json'
$dpiProcess = Start-Process -FilePath $build.Executable -ArgumentList @('skill-fixture','--ui-check',('"' + $dpiPath + '"')) -WindowStyle Hidden -Wait -PassThru
$dpi = Get-Content -LiteralPath $dpiPath -Raw | ConvertFrom-Json
if ($dpiProcess.ExitCode -ne 0 -or -not $dpi.perMonitorV2 -or $dpi.awareness -ne 2) { throw '模板运行时高 DPI 模式未启用。' }
$report = [ordered]@{ShortcutIdempotent=$true;ConflictingTargetRefused=$true;ExistingTargetPreserved=$true;WhatIfWritesNothing=$true;TemplateCompiled=$true;TemplateConfigurationVerified=$true;PerMonitorV2Verified=$true;BusinessServiceStarted=$false}
$report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'skill-verification.json') -Encoding utf8
$report | ConvertTo-Json
# Delete only the disposable directory created by this test after verifying containment.
$resolvedTest = [IO.Path]::GetFullPath($testRoot)
$expectedParent = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
if (-not $resolvedTest.StartsWith($expectedParent,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolvedTest -Leaf) -ne 'skill-checks') { throw '测试清理路径检查失败。' }
Remove-Item -LiteralPath $resolvedTest -Recurse -Force

