using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Runtime.InteropServices;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

public class MenuPalette : ProfessionalColorTable {
    public override Color ToolStripDropDownBackground { get { return Color.White; } }
    public override Color MenuBorder { get { return Color.FromArgb(218, 222, 228); } }
    public override Color MenuItemSelected { get { return Color.FromArgb(233, 240, 249); } }
    public override Color MenuItemBorder { get { return Color.FromArgb(233, 240, 249); } }
    public override Color SeparatorDark { get { return Color.FromArgb(231, 234, 238); } }
    public override Color SeparatorLight { get { return Color.White; } }
}

public static class LauncherUi {
    [DllImport("user32.dll")] static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
    public static ContextMenuStrip CreateMenu() {
        return new ContextMenuStrip {
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point),
            ShowImageMargin = false, ShowCheckMargin = false,
            Renderer = new ToolStripProfessionalRenderer(new MenuPalette()) { RoundedEdges = false },
            Padding = new Padding(4), ForeColor = Color.FromArgb(35, 40, 48)
        };
    }
    public static object Verify() {
        using(var menu = CreateMenu()) {
            menu.Items.Add("打开页面 / 启动");
            menu.CreateControl();
            var context = GetThreadDpiAwarenessContext();
            return new { awareness = GetAwarenessFromDpiAwarenessContext(context),
                perMonitorV2 = AreDpiAwarenessContextsEqual(context, new IntPtr(-4)),
                font = menu.Font.Name, fontPoints = menu.Font.SizeInPoints,
                smallIconWidth = SystemInformation.SmallIconSize.Width };
        }
    }
}

public class Check { public string Url; public string Marker; }
public class AppConfig {
    public string Id, Name, Executable, Arguments, WorkingDirectory, Url, Browser, Icon;
    public string EnvFile, UrlTokenKey, PasswordFile, PasswordEnvKey, LogDirectory, ProjectDirectory;
    public int[] Ports;
    public Check[] Checks;
    public Dictionary<string,string> Environment;
}
public class Settings { public string DataDirectory; public AppConfig[] Apps; }
public class OwnedProcess { public int Pid; public long Started; public string Executable; }

public class ServiceController {
    readonly AppConfig app;
    readonly string statePath, logPath;
    readonly object logLock = new object();
    readonly List<string> secrets = new List<string>();
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    public ServiceController(AppConfig config, string data) {
        app = config;
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(app.LogDirectory);
        statePath = Path.Combine(data, app.Id + "-process.json");
        logPath = Path.Combine(app.LogDirectory, "launcher.log");
        foreach (var pair in ReadEnv()) {
            if (pair.Key.IndexOf("TOKEN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pair.Key.IndexOf("PASSWORD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pair.Key.IndexOf("SECRET", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pair.Key.IndexOf("API_KEY", StringComparison.OrdinalIgnoreCase) >= 0)
                if (!String.IsNullOrEmpty(pair.Value)) secrets.Add(pair.Value);
        }
        if (!String.IsNullOrEmpty(app.PasswordFile) && File.Exists(app.PasswordFile))
            secrets.Add(File.ReadAllText(app.PasswordFile).Trim());
    }
    Dictionary<string,string> ReadEnv() {
        var result = new Dictionary<string,string>();
        if (!String.IsNullOrEmpty(app.EnvFile) && File.Exists(app.EnvFile)) {
            foreach (string raw in File.ReadAllLines(app.EnvFile, Encoding.UTF8)) {
                string line = raw.Trim();
                if (line.StartsWith("#") || !line.Contains("=")) continue;
                int split = line.IndexOf('=');
                result[line.Substring(0, split).Trim()] = line.Substring(split+1).Trim().Trim('"', '\'');
            }
        }
        return result;
    }
    string Redact(string value) {
        foreach (string secret in secrets.OrderByDescending(x => x.Length))
            if (!String.IsNullOrEmpty(secret)) value = value.Replace(secret, "[REDACTED]");
        return System.Text.RegularExpressions.Regex.Replace(value, @"(?i)(token=)[^\s&\""']+", "$1[REDACTED]");
    }
    public void Log(string message) {
        lock(logLock) {
            if (File.Exists(logPath) && new FileInfo(logPath).Length > 5 * 1024 * 1024) {
                string previous = logPath + ".previous";
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(logPath, previous);
            }
            File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + Redact(message) + "\r\n", Encoding.UTF8);
        }
    }
    public Process Owned() {
        try {
            if (!File.Exists(statePath)) return null;
            var saved = json.Deserialize<OwnedProcess>(File.ReadAllText(statePath));
            var p = Process.GetProcessById(saved.Pid);
            if (!p.HasExited && p.StartTime.ToUniversalTime().Ticks == saved.Started &&
                String.Equals(p.MainModule.FileName, saved.Executable, StringComparison.OrdinalIgnoreCase) &&
                String.Equals(saved.Executable, app.Executable, StringComparison.OrdinalIgnoreCase)) return p;
            p.Dispose();
        } catch { }
        return null;
    }
    public bool Ready() {
        try {
            foreach (var check in app.Checks) {
                var request = (HttpWebRequest)WebRequest.Create(check.Url);
                request.Proxy = null;
                request.Timeout = 1500;
                request.ReadWriteTimeout = 1500;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream())) {
                    if (response.StatusCode != HttpStatusCode.OK ||
                        reader.ReadToEnd().IndexOf(check.Marker, StringComparison.OrdinalIgnoreCase) < 0) return false;
                }
            }
            return true;
        } catch { return false; }
    }
    bool PortBusy() {
        return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => app.Ports.Contains(x.Port));
    }
    public void Validate() {
        if (!File.Exists(app.Executable)) throw new Exception("找不到运行程序，请检查配置。");
        if (!Directory.Exists(app.WorkingDirectory)) throw new Exception("找不到项目工作目录。");
        if (!File.Exists(app.Browser)) throw new Exception("找不到配置的浏览器，请检查配置。");
        if (!String.IsNullOrEmpty(app.UrlTokenKey) && !ReadEnv().ContainsKey(app.UrlTokenKey))
            throw new Exception("找不到待办台访问凭据，请检查原项目 .env。");
    }
    public void Start() {
        Validate();
        if (Ready()) { Log("复用已运行服务。"); return; }
        Process existing = Owned();
        if (existing != null) {
            using(existing) { WaitReady(existing); }
            return;
        }
        if (PortBusy()) throw new Exception("所需端口被其他服务占用，请先检查端口；没有停止占用进程。");
        var info = new ProcessStartInfo(app.Executable, app.Arguments);
        info.WorkingDirectory = app.WorkingDirectory;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardErrorEncoding = Encoding.UTF8;
        if (app.Environment != null)
            foreach (var pair in app.Environment) info.EnvironmentVariables[pair.Key] = pair.Value;
        if (!String.IsNullOrEmpty(app.PasswordEnvKey)) {
            string secret = File.ReadAllText(app.PasswordFile).Trim();
            info.EnvironmentVariables[app.PasswordEnvKey] = secret;
        }
        var p = new Process();
        p.StartInfo = info;
        p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { SafeLog(e.Data); };
        p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { SafeLog(e.Data); };
        p.Start();
        try {
            var saved = new OwnedProcess { Pid = p.Id, Started = p.StartTime.ToUniversalTime().Ticks, Executable = app.Executable };
            File.WriteAllText(statePath, json.Serialize(saved), Encoding.UTF8);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            Log("启动服务 PID=" + p.Id);
            WaitReady(p);
        } catch {
            // Only this freshly created process can be cleaned up here.
            if (!p.HasExited) KillTree(p);
            if (File.Exists(statePath)) File.Delete(statePath);
            p.Dispose();
            throw;
        }
    }
    void SafeLog(string text) { if (text != null) try { Log(text); } catch { } }
    void WaitReady(Process p) {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < 60) {
            if (p.HasExited) throw new Exception("服务提前退出。请从托盘菜单查看日志。");
            if (Ready()) { Log("服务已就绪。"); return; }
            Thread.Sleep(350);
        }
        throw new Exception("服务在 60 秒内未就绪。请查看日志。");
    }
    static void KillTree(Process p) {
        var info = new ProcessStartInfo(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.System), "taskkill.exe"), "/PID " + p.Id + " /T /F");
        info.UseShellExecute = false; info.CreateNoWindow = true;
        info.RedirectStandardOutput = true; info.RedirectStandardError = true;
        using(var killer = Process.Start(info)) {
            killer.BeginOutputReadLine(); killer.BeginErrorReadLine();
            if (!killer.WaitForExit(10000)) throw new Exception("停止进程超时。");
            if (killer.ExitCode != 0 && !p.HasExited) throw new Exception("停止进程失败。");
        }
        if (!p.WaitForExit(10000)) throw new Exception("服务尚未停止。");
    }
    public void Stop() {
        using(var p = Owned()) {
            if (p == null) {
                if (Ready() || PortBusy()) throw new Exception("服务由其他入口启动，本启动器不会终止它。请先从原入口停止服务。");
            } else { KillTree(p); Log("服务已停止。"); }
        }
        if (File.Exists(statePath)) File.Delete(statePath);
        var watch = Stopwatch.StartNew();
        while (PortBusy() && watch.Elapsed.TotalSeconds < 10) Thread.Sleep(200);
        if (PortBusy()) throw new Exception("服务停止后端口仍被占用，请检查日志。");
    }
    public void Open() {
        if (!Ready()) throw new Exception("服务尚未就绪。");
        string url = app.Url;
        if (!String.IsNullOrEmpty(app.UrlTokenKey)) {
            string token = ReadEnv()[app.UrlTokenKey];
            string inherited = System.Environment.GetEnvironmentVariable(app.UrlTokenKey);
            if (inherited != null) token = inherited;
            url += (url.Contains("?") ? "&" : "?") + "token=" + Uri.EscapeDataString(token);
        }
        Process.Start(new ProcessStartInfo(app.Browser, "\"" + url + "\"") { UseShellExecute = false });
        Log("已使用配置的浏览器打开页面。");
    }
    public object Status() {
        using(var p = Owned()) return new { ready = Ready(), owned = p != null, pid = p == null ? 0 : p.Id };
    }
}

public class TrayContext : ApplicationContext {
    readonly NotifyIcon tray;
    readonly ServiceController service;
    readonly AppConfig app;
    readonly EventWaitHandle openEvent;
    readonly System.Windows.Forms.Timer timer;
    readonly ToolStripMenuItem status;
    readonly List<ToolStripMenuItem> actions = new List<ToolStripMenuItem>();
    bool busy;
    readonly Control dispatcher = new Control();
    public TrayContext(AppConfig config, ServiceController controller, EventWaitHandle signal) {
        app = config; service = controller; openEvent = signal;
        dispatcher.CreateControl();
        var menu = LauncherUi.CreateMenu();
        status = new ToolStripMenuItem(app.Name + " · 准备启动") { Enabled = false };
        menu.Items.Add(status); menu.Items.Add(new ToolStripSeparator());
        Add(menu, "打开页面 / 启动", delegate { service.Start(); service.Open(); });
        Add(menu, "重启服务", delegate { service.Stop(); service.Start(); service.Open(); });
        Add(menu, "停止服务", delegate { service.Stop(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("查看日志", null, delegate { Process.Start("explorer.exe", "\"" + app.LogDirectory + "\""); });
        menu.Items.Add("打开项目目录", null, delegate { Process.Start("explorer.exe", "\"" + app.ProjectDirectory + "\""); });
        menu.Items.Add(new ToolStripSeparator());
        Add(menu, "停止服务并退出", delegate { service.Stop(); dispatcher.BeginInvoke((Action)delegate { ExitThread(); }); });
        foreach(ToolStripItem item in menu.Items) {
            if (!(item is ToolStripSeparator)) item.Padding = new Padding(10, 5, 12, 5);
        }
        tray = new NotifyIcon { Icon = File.Exists(app.Icon) ? new Icon(app.Icon, SystemInformation.SmallIconSize) : SystemIcons.Application,
            Text = app.Name, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += delegate { Run(delegate { service.Start(); service.Open(); }); };
        timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += delegate {
            if (openEvent.WaitOne(0)) Run(delegate { service.Start(); service.Open(); });
        };
        menu.Opening += delegate {
            if (!busy) {
                using(var p = service.Owned()) status.Text = app.Name + (p != null ? " · 服务运行中" : " · 已停止或由外部启动");
            }
        };
        timer.Start();
        Run(delegate { service.Start(); service.Open(); });
    }
    void Add(ContextMenuStrip menu, string title, Action action) {
        var item = new ToolStripMenuItem(title);
        item.Click += delegate { Run(action); };
        menu.Items.Add(item); actions.Add(item);
    }
    async void Run(Action action) {
        if (busy) { tray.ShowBalloonTip(2000, app.Name, "正在处理，请稍候。", ToolTipIcon.Info); return; }
        busy = true;
        foreach(var item in actions) item.Enabled = false;
        status.Text = app.Name + " · 正在处理…";
        try {
            await Task.Run(action);
            using(var p = service.Owned()) status.Text = app.Name + (p != null ? " · 服务运行中" : " · 操作完成");
            tray.ShowBalloonTip(2500, app.Name, "操作完成。右键托盘图标可管理服务。", ToolTipIcon.Info);
        } catch(Exception e) {
            service.Log("操作失败：" + e.GetType().Name);
            status.Text = app.Name + " · 操作失败";
            MessageBox.Show(e.Message, app.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        } finally {
            busy = false;
            foreach(var item in actions) item.Enabled = true;
        }
    }
    protected override void ExitThreadCore() {
        timer.Stop(); timer.Dispose(); tray.Visible = false; tray.Dispose(); dispatcher.Dispose();
        base.ExitThreadCore();
    }
}

static class Program {
    [STAThread]
    static int Main(string[] args) {
        string resultPath = null;
        try {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 0) throw new Exception("请从开始菜单选择已配置的本地应用。");
            if (args.Length > 2) resultPath = args[2];
            var json = new JavaScriptSerializer();
            if (args.Length > 1 && args[1] == "--ui-check") {
                if (resultPath == null) throw new Exception("UI 检查需要结果路径。");
                File.WriteAllText(resultPath, json.Serialize(LauncherUi.Verify()), Encoding.UTF8);
                return 0;
            }
            var settings = json.Deserialize<Settings>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "apps.json"), Encoding.UTF8));
            var app = settings.Apps.Single(x => x.Id == args[0]);
            bool created;
            using(var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LocalAppLauncher.Open." + app.Id))
            using(var mutex = new Mutex(true, "Local\\LocalAppLauncher.Instance." + app.Id, out created)) {
                if (!created) {
                    if (args.Length == 1) { signal.Set(); return 0; }
                    throw new Exception("托盘启动器正在运行，请从托盘管理。");
                }
                var service = new ServiceController(app, settings.DataDirectory);
                if (args.Length > 1) {
                    switch(args[1]) {
                        case "--verify": service.Validate(); break;
                        case "--self-test":
                            if (service.Ready()) throw new Exception("服务已运行，不能执行完整启动测试。");
                            try {
                                service.Start();
                                int first;
                                using (var p = service.Owned()) { if (p == null) throw new Exception("缺少进程记录。"); first = p.Id; }
                                service.Start();
                                using (var p = service.Owned()) if (p == null || p.Id != first) throw new Exception("重复启动创建了新进程。");
                                service.Stop();
                                if (service.Ready()) throw new Exception("停止后服务仍就绪。");
                                service.Stop();
                                service.Start();
                                if (!service.Ready()) throw new Exception("重新启动失败。");
                            } finally { service.Stop(); }
                            break;
                        default: throw new Exception("未知操作。");
                    }
                    if (resultPath != null) File.WriteAllText(resultPath, json.Serialize(new { success = true, status = service.Status() }), Encoding.UTF8);
                    return 0;
                }
                Application.Run(new TrayContext(app, service, signal));
                mutex.ReleaseMutex();
            }
            return 0;
        } catch(Exception e) {
            if (resultPath != null) File.WriteAllText(resultPath, new JavaScriptSerializer().Serialize(new { success = false, error = e.Message }), Encoding.UTF8);
            else MessageBox.Show(e.Message, "本地应用启动器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}

