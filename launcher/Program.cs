using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Viewcut.Web;

internal static class Program
{
    internal const string Version = "0.6.1";
    internal const string Identity = "Viewcut-Web-Launcher/1";
    private static async Task<int> Main(string[] args)
    {
        try
        {
            // Finder tracks the short-lived app launcher. Its background child
            // serves the page, so another double-click can open the browser again.
            if (OperatingSystem.IsMacOS() && args.Length == 0)
            {
                var background = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                background.ArgumentList.Add("--background");
                Process.Start(background);
                return 0;
            }
            var options = Options.Parse(args);
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromMilliseconds(450) };
            // Binding the preferred port also arbitrates simultaneous double-clicks.
            for (var port = options.Port; port < options.Port + 24; port++)
            {
                var url = $"http://127.0.0.1:{port}";
                if (await Existing(http, url)) { OpenBrowser(url, options); Ready(options, url, reused: true); return 0; }
                var listener = new TcpListener(IPAddress.Loopback, port);
                try { listener.Start(32); }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    await Task.Delay(180);
                    if (await Existing(http, url)) { OpenBrowser(url, options); Ready(options, url, reused: true); return 0; }
                    continue;
                }
                using var host = new LocalHost(listener, url, options.IdleSeconds);
                var serving = host.RunAsync();
                if (!await Existing(http, url)) throw new IOException("The local service did not start.");
                OpenBrowser(url, options);
                Ready(options, url, reused: false);
                await serving;
                return 0;
            }
            throw new IOException("No local port is available. Close an earlier Viewcut Web session and try again.");
        }
        catch (Exception ex)
        {
            var message = "Viewcut Web 无法启动 / Could not start Viewcut Web\n\n" + ex.Message;
            if (OperatingSystem.IsWindows()) MessageBoxW(IntPtr.Zero, message, "Viewcut Web", 0x10);
            else if (OperatingSystem.IsMacOS())
            {
                var info = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false };
                foreach (var value in new[] { "-e", "on run argv\n display alert \"Viewcut Web\" message (item 1 of argv) as critical\nend run", message }) info.ArgumentList.Add(value);
                Process.Start(info)?.WaitForExit();
            }
            Console.Error.WriteLine(message);
            return 1;
        }
    }

    private static async Task<bool> Existing(HttpClient client, string url)
    {
        try { return await client.GetStringAsync(url + "/__viewcut/health") == Identity + "\n" + Version; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }
    private static void Ready(Options options, string url, bool reused)
    {
        if (options.ReadyFile is { } file)
            File.WriteAllText(file, $"{url}\n{Environment.ProcessId}\n{(reused ? "reused" : "started")}");
    }
    private static void OpenBrowser(string url, Options options)
    {
        if (options.Browser == "none") return;
        if (OperatingSystem.IsWindows())
        {
            var candidates = options.Browser switch
            {
                "chrome" => new[] { Chrome() }, "edge" => new[] { Edge() },
                _ => Array.Empty<string>()
            };
            foreach (var executable in candidates.Where(File.Exists)) { Launch(executable, url, options); return; }
            if (options.Browser != "auto") throw new FileNotFoundException("Install " + options.Browser + " and try again.");
            // Honor the user's default browser when it supports the app.
            using var association = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            var id = association?.GetValue("ProgId") as string ?? "";
            if (id.StartsWith("ChromeHTML", StringComparison.OrdinalIgnoreCase) || id.StartsWith("MSEdgeHTM", StringComparison.OrdinalIgnoreCase))
            { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return; }
            foreach (var executable in new[] { Edge(), Chrome() }.Where(File.Exists)) { Launch(executable, url, options); return; }
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        else if (OperatingSystem.IsMacOS())
        {
            var info = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            if (options.Browser is "chrome" or "edge" or "safari") { info.ArgumentList.Add("-a"); info.ArgumentList.Add(options.Browser switch { "chrome" => "Google Chrome", "edge" => "Microsoft Edge", _ => "Safari" }); }
            info.ArgumentList.Add(url); Process.Start(info);
        }
        else Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false });
    }
    private static string Chrome()
    {
        var system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe");
        return File.Exists(system) ? system : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe");
    }
    private static string Edge() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe");
    private static void Launch(string executable, string url, Options options)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        if (options.Profile is { } profile) { info.ArgumentList.Add("--user-data-dir=" + Path.GetFullPath(profile)); info.ArgumentList.Add("--no-first-run"); info.ArgumentList.Add("--no-default-browser-check"); }
        if (options.Headless) { info.ArgumentList.Add("--headless=new"); info.ArgumentList.Add("--remote-debugging-port=0"); }
        info.ArgumentList.Add(url);
        var process = Process.Start(info) ?? throw new IOException("Could not open the browser.");
        if (options.BrowserPidFile is { } file) File.WriteAllText(file, process.Id.ToString());
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string message, string title, uint flags);

    private sealed record Options(int Port = 4173, int IdleSeconds = 60, string Browser = "auto", string? ReadyFile = null,
        string? Profile = null, bool Headless = false, string? BrowserPidFile = null)
    {
        public static Options Parse(string[] args)
        {
            var result = new Options();
            for (var i = 0; i < args.Length; i++)
            {
                string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
                result = args[i] switch
                {
                    "--port" => result with { Port = int.Parse(Value()) },
                    "--idle-seconds" => result with { IdleSeconds = int.Parse(Value()) },
                    "--browser" => result with { Browser = Value() },
                    "--ready-file" => result with { ReadyFile = Value() },
                    "--profile" => result with { Profile = Value() },
                    "--headless" => result with { Headless = true },
                    "--background" => result,
                    "--browser-pid-file" => result with { BrowserPidFile = Value() },
                    _ => throw new ArgumentException("Unknown launch option.")
                };
            }
            if (result.Port < 1024 || result.Port > 65480 || result.IdleSeconds < 3 || result.Browser is not ("auto" or "none" or "chrome" or "edge" or "safari")) throw new ArgumentException("Invalid launch option.");
            return result;
        }
    }
}

internal sealed class LocalHost : IDisposable
{
    private readonly TcpListener listener;
    private readonly string url;
    private readonly int idleSeconds;
    private readonly string nonce = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, (byte[] Data, string Type)> assets = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim connections = new(32);
    private long lastPulse = Environment.TickCount64;
    private readonly Dictionary<string, long> clients = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public LocalHost(TcpListener listener, string url, int idleSeconds)
    {
        this.listener = listener; this.url = url; this.idleSeconds = idleSeconds;
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("web.", StringComparison.Ordinal)))
        {
            using var source = assembly.GetManifestResourceStream(name)!; using var output = new MemoryStream(); source.CopyTo(output);
            var file = name[4..];
            assets["/" + file] = (output.ToArray(), Path.GetExtension(file) switch { ".html" => "text/html; charset=utf-8", ".js" => "text/javascript; charset=utf-8", ".css" => "text/css; charset=utf-8", ".svg" => "image/svg+xml", _ => "application/octet-stream" });
        }
        if (!assets.ContainsKey("/index.html")) throw new IOException("The web interface is missing.");
    }
    public async Task RunAsync()
    {
        _ = IdleAsync();
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token);
                await connections.WaitAsync(stop.Token);
                _ = ServeAsync(client);
            }
        }
        catch (OperationCanceledException) { }
    }
    private async Task IdleAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try { await Task.Delay(1000, stop.Token); } catch (OperationCanceledException) { return; }
            lock (gate)
            {
                foreach (var id in clients.Where(p => Environment.TickCount64 - p.Value > Math.Max(6, idleSeconds * 2) * 1000L).Select(p => p.Key).ToArray()) clients.Remove(id);
                if (clients.Count == 0 && Environment.TickCount64 - lastPulse > idleSeconds * 1000L) stop.Cancel();
            }
        }
    }
    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var line = await reader.ReadLineAsync(timeout.Token); if (line is null || line.Length > 2048) return;
                var request = line.Split(' '); if (request.Length != 3) return;
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var length = line.Length;
                while ((line = await reader.ReadLineAsync(timeout.Token)) is { Length: > 0 })
                {
                    if ((length += line.Length) > 8192) return;
                    var colon = line.IndexOf(':'); if (colon > 0) headers[line[..colon]] = line[(colon + 1)..].Trim();
                }
                if (!headers.TryGetValue("Host", out var host) || "http://" + host != url) { await Response(stream, 403, "Forbidden", "text/plain", timeout.Token); return; }
                if (request[0] is not ("GET" or "HEAD")) { await Response(stream, 405, "Method not allowed", "text/plain", timeout.Token); return; }
                var uri = new Uri(new Uri(url), request[1]); var path = uri.AbsolutePath;
                if (path == "/__viewcut/health") { await Response(stream, 200, Program.Identity + "\n" + Program.Version, "text/plain", timeout.Token); return; }
                if (path == "/__viewcut/runtime") { await Response(stream, 200, "{\"pulse\":\"/__viewcut/pulse/" + nonce + "\",\"interval\":" + Math.Clamp(idleSeconds * 1000 / 3, 1000, 15000) + ",\"version\":\"" + Program.Version + "\"}", "application/json", timeout.Token); return; }
                if (path.StartsWith("/__viewcut/pulse/" + nonce + "/", StringComparison.Ordinal))
                {
                    if (headers.GetValueOrDefault("Sec-Fetch-Site") is { } site && site != "same-origin") { await Response(stream, 403, "Forbidden", "text/plain", timeout.Token); return; }
                    if (headers.GetValueOrDefault("Origin") is { } origin && origin != url) { await Response(stream, 403, "Forbidden", "text/plain", timeout.Token); return; }
                    var id = path.Split('/').Last(); if (id.Length != 32 || id.Any(c => !char.IsAsciiHexDigit(c))) { await Response(stream, 400, "Bad client", "text/plain", timeout.Token); return; }
                    lock (gate) { lastPulse = Environment.TickCount64; if (uri.Query == "?close=1") clients.Remove(id); else clients[id] = lastPulse; }
                    await Response(stream, 204, "", "text/plain", timeout.Token); return;
                }
                if (path == "/") path = "/index.html";
                if (!assets.TryGetValue(path, out var asset)) { await Response(stream, 404, "Not found", "text/plain", timeout.Token); return; }
                await Response(stream, 200, asset.Data, asset.Type, timeout.Token, request[0] == "HEAD");
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or UriFormatException or SocketException) { }
            finally { connections.Release(); }
        }
    }
    private static Task Response(NetworkStream stream, int status, string body, string type, CancellationToken token) => Response(stream, status, Encoding.UTF8.GetBytes(body), type, token);
    private static async Task Response(NetworkStream stream, int status, byte[] body, string type, CancellationToken token, bool head = false)
    {
        const string csp = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' blob: data:; media-src 'self' blob:; connect-src 'self'; worker-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
        var header = $"HTTP/1.1 {status} OK\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-cache\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: {csp}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), token);
        if (!head && body.Length > 0) await stream.WriteAsync(body, token);
    }
    public void Dispose() { stop.Cancel(); listener.Stop(); stop.Dispose(); }
}
