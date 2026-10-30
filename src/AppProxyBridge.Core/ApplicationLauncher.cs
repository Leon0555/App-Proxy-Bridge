using System.Diagnostics;

namespace AppProxyBridge.Core;

public static class ApplicationLauncher
{
    public static ProcessStartInfo CreateStartInfo(ApplicationEntry app, int port)
    {
        var path = app.Components.FirstOrDefault()?.Path ?? "";
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请选择有效的 EXE 程序路径。");
        if (!Enum.IsDefined(app.LaunchMethod) || port is < 1 or > 65535)
            throw new ArgumentException("启动方式或本地端口无效。");
        string workingDirectory = string.IsNullOrWhiteSpace(app.WorkingDirectory) ? Path.GetDirectoryName(path)! : app.WorkingDirectory;
        if (!Path.IsPathFullyQualified(workingDirectory) || !Directory.Exists(workingDirectory))
            throw new ArgumentException("工作目录不存在或不是完整路径。");
        var info = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = workingDirectory };
        string proxy = $"http://127.0.0.1:{port}";
        foreach (var key in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY" }) info.Environment[key] = proxy;
        info.Environment["NO_PROXY"] = "localhost,127.0.0.1,::1";
        foreach (var argument in app.Arguments) {
            if (app.LaunchMethod == LaunchMethod.Chromium && new[] { "--proxy-server", "--proxy-pac-url", "--no-proxy-server", "--proxy-auto-detect", "--proxy-bypass-list" }.Any(key => argument.Equals(key, StringComparison.OrdinalIgnoreCase) || argument.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("自定义启动参数不能覆盖代理设置。");
            info.ArgumentList.Add(argument);
        }
        if (app.LaunchMethod == LaunchMethod.Chromium) {
            info.ArgumentList.Add("--proxy-server=" + proxy);
            info.ArgumentList.Add("--proxy-bypass-list=localhost;127.0.0.1;[::1]");
            info.ArgumentList.Add("--disable-quic");
        }
        return info;
    }

    public static Process Start(ApplicationEntry app, int port) =>
        Process.Start(CreateStartInfo(app, port)) ?? throw new IOException("无法启动目标软件。");
}

