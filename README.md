# Local App Launcher

一个 Codex skill，将 Windows 本地软件与自部署网页服务做成系统搜索可见的一键应用入口。

| 软件类型 | 入口方式 |
|---|---|
| 便携桌面 EXE | 开始菜单快捷方式，沿用原图标与应用自身生命周期 |
| 已编译的后台服务 EXE | 托盘管理，等待就绪后打开指定浏览器 |
| Python / Node 本地网页项目 | 托盘管理，同时复用既有运行环境 |

托盘提供打开页面、停止、重启、查看日志和退出。重复启动复用实例，停止前核对 PID、创建时间与可执行路径。支持高 DPI 菜单和多尺寸图标。

## 安装与调用

将整个仓库放入 Codex skills 目录，例如：

```powershell
git clone https://github.com/Escanorsss/local-app-launcher.git "$env:USERPROFILE\.codex\skills\local-app-launcher"
```

使用自定义 `CODEX_HOME` 时，选择它对应的 skills 目录。不要覆盖已有同名 skill。安装后在新会话中使用：

```text
使用 $local-app-launcher，把这个本地软件做成系统搜索可见的一键启动入口。
```

流程见 [SKILL.md](SKILL.md)，配置见 [服务启动器说明](references/service-launcher.md)。

## 直接使用脚本

桌面应用：

```powershell
./scripts/New-AppShortcut.ps1 -Name 'MyApp' -TargetPath 'D:\Apps\MyApp\MyApp.exe'
```

默认写入当前用户开始菜单，支持 `-WhatIf`。同名入口若目标或参数不同，默认拒绝覆盖。保留 EXE 的配套目录；创建入口不会迁移数据。

本地网页服务：

```powershell
./scripts/Build-ServiceLauncher.ps1 -ConfigPath './my-apps.json' -OutputDirectory 'D:\Apps\LocalAppLauncher'
./scripts/New-AppShortcut.ps1 -Name 'MyService' -TargetPath 'D:\Apps\LocalAppLauncher\LocalAppLauncher.exe' -Arguments 'my-service'
```

先按配置说明填写已验证的路径、端口、就绪标记和浏览器路径。构建依赖 Windows x64 与 .NET Framework 4.8 或更高版本，使用系统已有 C# 编译器，无需额外 SDK。生成的 EXE 必须与同名 `.exe.config` 和 `apps.json` 一起保留。

生成简单多尺寸图标：

```powershell
./scripts/New-LetterIcon.ps1 -OutputPath './my-app.ico' -Letter 'M' -Color '#3769A8'
```

## 验证与限制

```powershell
./tests/Verify.ps1
```

验证快捷方式幂等、冲突保护、WhatIf、模板编译、配置检查和运行时 PerMonitorV2。测试使用临时目录，不启动业务服务或浏览器。系统搜索、托盘视觉效果与具体软件运行仍需在目标机器验证。

默认停止通过 `taskkill /PID /T /F` 终止核对过的进程树，属于强制退出。有关闭 API 或重要事务的软件应先适配优雅退出。关闭网页不会停止服务。Docker 需要适配容器停止命令，不能把结束 CLI 等同于停止容器。

仓库仅包含 skill、源码和脚本。运行配置、凭据、日志与编译产物在本机管理。
