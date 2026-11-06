using System.IO.Pipes;
using System.Security.Principal;

namespace Viewcut;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var input = args.Length switch
        {
            1 => args[0],
            2 when args[0] is "--file" or "--folder" => args[1],
            _ => null
        };
        if (input is not null) input = Path.GetFullPath(input);

        using var identity = WindowsIdentity.GetCurrent();
        var key = $"Viewcut.1.{identity.User!.Value}";
        using var mutex = new Mutex(false, @"Local\" + key);
        bool owns;
        try { owns = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { owns = true; }
        if (!owns)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", key, PipeDirection.Out);
                client.Connect(3000);
                using var writer = new StreamWriter(client);
                writer.WriteLine(input);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException)
            {
                MessageBox.Show(L.T("无法联系正在运行的 Viewcut。请稍后再试。"), "Viewcut");
            }
            return;
        }

        using var installationGuard = new Mutex(false, @"Local\Viewcut.Running");
        ApplicationConfiguration.Initialize();
        try
        {
            using var window = new MainWindow(new SessionStore(), input);
            using var stop = new CancellationTokenSource();
            window.Shown += (_, _) => _ = ListenAsync(key, window, stop.Token);
            try { Application.Run(window); }
            finally { stop.Cancel(); }
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            MessageBox.Show(ex.Message, L.T("Viewcut 启动失败"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static async Task ListenAsync(string key, MainWindow window, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(key, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                using var reader = new StreamReader(server);
                var path = await reader.ReadLineAsync(token).ConfigureAwait(false);
                window.BeginInvoke(() => window.ActivateInput(path));
            }
            catch (OperationCanceledException) { return; }
            catch (InvalidOperationException) when (window.IsDisposed || !window.IsHandleCreated) { return; }
            catch (IOException) { }
        }
    }
}
