# 桌面应用与服务示例

以下是分类参考，不是当前机器的配置。使用前检查真实路径、版本、原入口和现有数据。

## Ani：带配套目录的桌面 EXE

[Ani / Animeko](https://github.com/open-ani/animeko) 的 Windows 应用包可包含 `Ani.exe`、`app` 和 `runtime`。检查过的 6.2.0 包采用 Java jpackage，启动器指向 Kotlin/Compose 桌面主类，自带 JVM；版本和包结构变化时需重新核实。

开始菜单快捷方式直接指向原 EXE，工作目录设为应用根目录，图标使用原 EXE。无需另加浏览器入口或第二个托盘。薄启动 EXE 不能单独复制出去，必须保留配套运行时和资源。

```powershell
./scripts/New-AppShortcut.ps1 -Name 'Ani' -TargetPath 'D:\Apps\Ani\Ani.exe' -WorkingDirectory 'D:\Apps\Ani'
```

数据目录不能由工作目录推断。该版本通过 Windows Shell32 取得用户目录，再附加组织/应用名及 data/cache。修改“起始位置”不会改变这类目录，覆盖 APPDATA 也未必影响 Shell32。先核实受支持的设置，再考虑迁移；“视频缓存目录”不一定覆盖配置和日志。

## 已编译的服务 EXE

若 EXE 没有桌面界面，而是监听本地端口供浏览器访问，仍使用服务托盘启动器。`Executable` 指向原 EXE；参数、工作目录、健康检查和停止机制依软件确定。编译完成只减少运行时依赖，不会自动解决重复启动或退出管理。

## Python / Node 本地服务

使用项目已验证的虚拟环境 Python 或 Node，参数指向项目入口。检查环境变量、原配置文件、网页鉴权和全部端口。健康检查必须包含目标服务身份，不能只看端口监听。

网页需要 token 时，快捷方式仅包含应用 Id，token 在运行时从原凭据文件读取。不要把实际 token、密码、个人路径清单或业务日志放进公开仓库。
