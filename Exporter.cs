using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Viewcut;

internal sealed class Exporter(string toolsDirectory)
{
    private readonly string ffmpeg = Path.Combine(toolsDirectory, "ffmpeg.exe");
    private readonly string ffprobe = Path.Combine(toolsDirectory, "ffprobe.exe");

    public async Task<string> ExportAsync(ExportJob job, CancellationToken token)
    {
        if (!double.IsFinite(job.Start) || !double.IsFinite(job.End) || job.Start < 0 || job.End <= job.Start)
            throw new InvalidDataException(L.T("片段终点必须晚于起点。"));
        if (SourceStamp.Read(job.Source) != job.Stamp)
            throw new IOException(L.T("源视频已更换或修改，请重新标记片段。"));
        if (!Directory.Exists(job.Folder))
            throw new DirectoryNotFoundException(L.F("无法访问目标目录：{0}", job.Folder));

        var extension = Path.GetExtension(job.Source).ToLowerInvariant();
        if (extension is not (".mp4" or ".mov"))
            throw new InvalidDataException(L.T("首版支持导出 MP4 和 MOV。"));
        if (job.Name is { } customName)
        {
            if (ClipNames.Validate(customName, job.Id, [], null, extension) is { } error)
                throw new InvalidDataException(error);
            var output = Path.Combine(job.Folder, customName + extension);
            if (ClipNames.OutputExists(output)) throw new ClipNameConflictException(output);
        }

        var staging = Path.Combine(job.Folder, $".viewcut-{job.Id:N}.partial{extension}");
        var arguments = new List<string>
        {
            "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
            "-ss", Seconds(job.Start), "-i", job.Source, "-t", Seconds(job.End - job.Start),
            "-map", "0:V:0", "-map", "0:a?", "-map_chapters", "-1"
        };
        arguments.AddRange(["-c", "copy", "-avoid_negative_ts", "make_zero"]);
        arguments.Add(staging);

        try
        {
            await RunAsync(ffmpeg, arguments, token).ConfigureAwait(false);
            await CheckOutputAsync(staging, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (SourceStamp.Read(job.Source) != job.Stamp)
                throw new IOException(L.T("导出期间源视频发生变化，请重新标记片段。"));
            return Publish(job, staging, extension);
        }
        finally
        {
            try { File.Delete(staging); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task CheckOutputAsync(string output, CancellationToken token)
    {
        var packets = await RunAsync(ffprobe,
            ["-v", "error", "-select_streams", "V:0", "-read_intervals", "%+#1",
                "-show_entries", "packet=flags", "-of", "csv=p=0", output], token).ConfigureAwait(false);
        if (!packets.TrimStart().StartsWith('K'))
            throw new InvalidDataException(L.T("导出结果没有可独立播放的起始视频包，请调整起点后重试。"));
    }

    private static string Publish(ExportJob job, string staging, string extension)
    {
        if (job.Name is { } customName)
        {
            var output = Path.Combine(job.Folder, customName + extension);
            try { File.Move(staging, output); }
            catch (IOException) when (ClipNames.OutputExists(output)) { throw new ClipNameConflictException(output); }
            return output;
        }
        var stem = Path.GetFileNameWithoutExtension(job.Source);
        if (stem.Length > 120) stem = stem[..120];
        var timestamp = TimeSpan.FromSeconds(job.Start).ToString(@"hh\mm\ss\.fff", CultureInfo.InvariantCulture);
        var name = $"{stem}_{timestamp}";
        for (int index = 1; ; index++)
        {
            var suffix = index == 1 ? "" : $"_{index}";
            var output = Path.Combine(job.Folder, name + suffix + extension);
            try
            {
                File.Move(staging, output);
                return output;
            }
            catch (IOException) when (ClipNames.OutputExists(output)) { }
        }
    }

    private static string Seconds(double value) => value.ToString("F6", CultureInfo.InvariantCulture);

    private static async Task<string> RunAsync(string executable, IEnumerable<string> arguments,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(executable))
            throw new FileNotFoundException(L.F("缺少 {0}，请用带依赖的测试版本。", Path.GetFileName(executable)));
        var settings = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) settings.ArgumentList.Add(argument);
        using var process = Process.Start(settings) ?? throw new IOException(L.T("无法启动媒体工具。"));
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(output, error).ConfigureAwait(false);
            throw;
        }

        var detail = (await error.ConfigureAwait(false)).Trim();
        var result = await output.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new IOException(detail.Length == 0 ? L.F("{0} 退出代码：{1}", Path.GetFileName(executable), process.ExitCode) : detail);
        return result;
    }
}
