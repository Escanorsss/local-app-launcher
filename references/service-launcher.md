# 配置驱动的服务托盘启动器

只有服务需要此实现。桌面 GUI 软件应优先直接使用其 EXE。

## 构建与安装

1. 从已验证的启动方式填写配置。运行程序和浏览器写绝对路径；原项目代码、环境与数据不移动。
2. 将配置保存到当前事务目录，调用 `scripts/Build-ServiceLauncher.ps1 -ConfigPath <配置> -OutputDirectory <程序目录>`。输出为 Windows GUI 子系统的 `.exe`、同名 `.exe.config` 与同目录 `apps.json`。`.exe.config` 是高 DPI 支持所必需的交付文件。
3. 使用 `scripts/New-AppShortcut.ps1` 为每个应用创建入口，`TargetPath` 指向生成的 EXE，`Arguments` 为该应用的 `Id`，`IconPath` 使用应用原图标或独立 ICO。
4. 启动一个应用会驻留一个托盘进程。第一次启动自动启动服务并打开浏览器；再次执行相同 Id 通知原进程打开网页。配置改动需先退出该托盘进程再重新启动。

编译脚本不负责安装原软件、创建业务数据或打开网页。它不修改同名安装的进程状态；编译时若目标 EXE 正在使用，编译失败并停止。不要复制代码中的固定运行时版本路径来推断另一台机器的布局。

## 配置示例与字段

下面的路径和端口只是示例，必须替换为本机检查所得；`worker.exe` 表示已编译的服务，而非桌面窗口或安装器。

```json
{
  "DataDirectory": "D:\\SoftwareData\\LocalAppLauncher",
  "Apps": [
    {
      "Id": "example-service",
      "Name": "示例服务",
      "Executable": "D:\\Softwares\\ExampleService\\worker.exe",
      "Arguments": "--host 127.0.0.1 --port 8080",
      "WorkingDirectory": "D:\\SoftwareData\\ExampleService",
      "ProjectDirectory": "D:\\Softwares\\ExampleService",
      "Url": "http://127.0.0.1:8080/",
      "Browser": "C:\\Program Files\\BraveSoftware\\Brave-Browser\\Application\\brave.exe",
      "Icon": "D:\\Softwares\\ExampleService\\app.ico",
      "Ports": [8080],
      "Checks": [{"Url": "http://127.0.0.1:8080/health", "Marker": "example-service"}],
      "Environment": {},
      "LogDirectory": "D:\\SoftwareData\\LocalAppLauncher\\example-service-logs"
    }
  ]
}
```

- `DataDirectory`：启动器 PID 状态文件所在目录。每个应用 Id 唯一，状态和互斥量都按 Id 区分。
- `Executable` / `Arguments`：直接启动的程序和 Windows 参数字符串。Python 用该项目的 `.venv\Scripts\python.exe`，Node 用本机已经验证的 Node 路径，参数含脚本绝对路径。参数中的含空格路径用双引号，不经过二次 shell 拼接。
- `WorkingDirectory`：实际启动工作目录，可能是项目目录，也可能是应用支持的独立数据目录。不要凭该字段声称改变了软件的数据根目录。
- `ProjectDirectory`：托盘“打开项目目录”的目标。
- `Url` / `Browser`：就绪后打开的网页和指定浏览器可执行文件。
- `Ports`：该实例依赖的全部监听端口，用于冲突和停止后的释放检查。仅端口监听不能证明目标服务身份。
- `Checks`：全部检查均需 HTTP 200 且响应包含 `Marker`。短小健康端点优先；复杂身份校验应修改 `Ready()`，不能只配置含糊的通用 HTML 标记。
- `Icon`：可选 ICO 路径。无文件时托盘使用系统应用图标；开始菜单快捷方式可直接取原 EXE 的图标。源 EXE 的嵌入图标不能直接当作 `new Icon(path)` 的 ICO 文件。
- `Environment`：仅该子进程的环境覆盖，不改变系统环境。服务若派生子进程，需明确它的退出控制方式。
- `LogDirectory`：启动器捕获的 stdout/stderr 与操作记录，UTF-8、滚动日志。应用自己的日志不受此字段支配。
- `EnvFile` 与 `UrlTokenKey`：可选，网页鉴权 token 从原 `.env` 按 `KEY=VALUE` 读取，支持首尾引号；读取方式与服务原有解析器应保持一致。模板考虑同名继承环境变量覆盖。用于打开网页，**不会**将 EnvFile 的全部内容注入服务环境，服务仍需自己的配置加载机制。
- `PasswordFile` 与 `PasswordEnvKey`：可选，从原凭据文件读取一个值并作为子进程环境变量传入；沿用原软件机制，不生成或改密码。

模板按 `TOKEN`、`PASSWORD`、`SECRET`、`API_KEY` 对已加载的敏感值做日志替换，并遮盖 `token=` URL 查询值。这不等于对任意格式敏感数据的完整识别；新增鉴权形式时扩展脱敏并验证，不把凭据输出到工具结果。

Docker 等前台包装命令不能直接套用“结束进程树=停止服务”。`docker compose up -d` 的 CLI 会退出，容器仍运行。需要适配相应就绪检查、命名容器/项目状态和授权范围内的 `stop`；不要自动 `down -v` 或删除卷。

## 生命周期与验证边界

支持菜单：打开页面/启动、重启服务、停止服务、查看日志、打开项目目录、停止服务并退出。关闭网页保持服务运行；停止服务保留托盘，停止并退出关闭两者。

保存 PID、UTC 创建时间和可执行路径，停止时再核对，防止旧记录误杀重用 PID。外部服务通过身份检查时可以打开页面；因没有所有权记录，不提供自动停止。启动前端口冲突不结束占用者，错误时保留托盘方便查看日志。服务进程启动失败或 60 秒内未就绪会报错；新创建的失败进程会清理。

**限制**：默认停止是经过所有权检查后的强制终止进程树。优雅停止需要按软件接口另行适配；不因带有托盘菜单就承诺事务完整性。就绪检查确认响应身份，不能抵御另一个本机程序刻意伪造相同内容。

命令行验证接口（结果路径参数须按 Windows 参数规则加引号）：

```text
LocalAppLauncher.exe <Id> --verify <结果JSON绝对路径>
LocalAppLauncher.exe <Id> --ui-check <结果JSON绝对路径>
LocalAppLauncher.exe <Id> --self-test <结果JSON绝对路径>
```

`--verify` 检查路径和 token 配置，不启动业务服务，也不证明页面正常。`--self-test` 会真实启动、验证重复 Start 保持 PID、停止、重复停止、再次启动并最后停止。它会触发软件真实启动副作用，必须在已授权、当前未运行且可以停止的环境中使用；遇到已运行托盘实例时拒绝测试。不要拿此模式测试正在采集、同步或写入的重要业务。

实际交付仍需检查普通入口的单实例信号、自动在指定浏览器打开页面。系统搜索和托盘菜单的点击只有使用可用的系统 UI 控制才能声称已验证。无法交互时明确留下这两项验证边界。

## 高 DPI 与图标

模板包含 .NET Framework 4.8 目标声明、Windows 10 兼容 manifest 和 `DpiAwareness=PerMonitorV2` 应用配置。DPI 由 WinForms 配置启用，不在 manifest 同时定义互相覆盖的 DPI 设置。入口先调用 `EnableVisualStyles()`，再创建控件。依据：[微软 WinForms 高 DPI 配置说明](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/high-dpi-support-in-windows-forms)。

`--ui-check` 在创建菜单控件后查询 Windows 的真实线程 DPI 上下文，输出 `awareness=2` 和 `perMonitorV2=true` 才算成功。此模式不启动服务、不显示托盘，也不打开浏览器；它验证运行时 DPI 和字体，不代替实际屏幕视觉检查。

菜单使用微软雅黑 UI、留白和浅色扁平配色，仍是 WinForms 菜单，不承诺它等同于 Windows 11 原生 Fluent 菜单。托盘读取符合当前 `SmallIconSize` 的图标帧。`New-LetterIcon.ps1` 可生成 16/20/24/32/40/48/64/256 八个独立绘制的 PNG 帧组成 ICO，覆盖常见缩放比例；生成后确认 `new Icon(path, desiredSize)` 真正选到了相应尺寸。
