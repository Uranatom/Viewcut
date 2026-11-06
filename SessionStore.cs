using System.Text.Json;

namespace Viewcut;

internal enum JobState { Waiting, Running, Done, Failed }

internal sealed record SourceStamp(long Size, DateTime ModifiedUtc)
{
    public static SourceStamp Read(string path)
    {
        var file = new FileInfo(path);
        return new(file.Length, file.LastWriteTimeUtc);
    }
}

internal sealed record ExportJob(string Source, SourceStamp Stamp, string Folder,
    double Start, double End, bool Exact)
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public JobState State { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
}

internal sealed class Session
{
    public string? Destination { get; set; }
    public List<ExportJob> Jobs { get; init; } = [];
    public Dictionary<string, SourceStamp> Skipped { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class SessionStore
{
    private readonly string path;
    public Session Data { get; }

    public SessionStore(string? path = null)
    {
        var dataDirectory = Environment.GetEnvironmentVariable("VIEWCUT_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Viewcut");
        this.path = Path.GetFullPath(path ?? Path.Combine(dataDirectory, "session.json"));
        if (!File.Exists(this.path))
        {
            Data = new Session();
            return;
        }

        try
        {
            Data = JsonSerializer.Deserialize<Session>(File.ReadAllText(this.path)) ?? new Session();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"无法读取粗筛记录，请检查文件：{this.path}", ex);
        }

        Data.Skipped = new Dictionary<string, SourceStamp>(Data.Skipped, StringComparer.OrdinalIgnoreCase);
        foreach (var job in Data.Jobs.Where(job => job.State == JobState.Running))
        {
            job.State = JobState.Failed;
            job.Error = "上次导出中断。确认目标目录后可重试。";
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Data));
        File.Move(temporary, path, overwrite: true);
    }
}
