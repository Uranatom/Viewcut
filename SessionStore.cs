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

internal sealed record ClipDraft(string Source, SourceStamp Stamp, double Start, double End)
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

internal sealed record ExportJob(string Source, SourceStamp Stamp, string Folder, double Start, double End)
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? ClipId { get; set; }
    public JobState State { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
}

internal sealed class Session
{
    public int Version { get; set; }
    public string? Destination { get; set; }
    public string? BrowserFolder { get; set; }
    public bool BrowserLargeIcons { get; set; } = true;
    public double SidebarFraction { get; set; } = 0.4;
    public List<ClipDraft> Clips { get; init; } = [];
    public List<ExportJob> Jobs { get; init; } = [];

    public ClipDraft[] LatestClips(string? source, SourceStamp? stamp) => Clips
        .Where(clip => clip.Stamp == stamp && string.Equals(clip.Source, source, StringComparison.OrdinalIgnoreCase))
        .ToArray();

    public ExportJob? ExportFor(ClipDraft clip, string? folder) => Jobs.LastOrDefault(job =>
        job.ClipId == clip.Id && job.Stamp == clip.Stamp && job.Start == clip.Start && job.End == clip.End
        && string.Equals(job.Source, clip.Source, StringComparison.OrdinalIgnoreCase)
        && string.Equals(job.Folder, folder, StringComparison.OrdinalIgnoreCase));

    public ExportJob[] QueueExport(string source, SourceStamp stamp, string folder)
    {
        var batch = LatestClips(source, stamp)
            .Where(clip => ExportFor(clip, folder) is not { State: JobState.Waiting or JobState.Running })
            .Select(clip => new ExportJob(source, stamp, folder, clip.Start, clip.End) { ClipId = clip.Id })
            .ToArray();
        Jobs.AddRange(batch);
        return batch;
    }
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
            Data = new Session { Version = 1 };
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

        if (Data.Version == 0)
        {
            Data.Clips.AddRange(Data.Jobs.GroupBy(job => job.ClipId ?? job.Id).Select(group => group.Last())
                .Select(job => new ClipDraft(job.Source, job.Stamp, job.Start, job.End) { Id = job.ClipId ?? job.Id }));
            foreach (var job in Data.Jobs)
            {
                job.ClipId ??= job.Id;
                if (job.State != JobState.Waiting) continue;
                job.State = JobState.Failed;
                job.Error = "旧版标记已恢复，请点击导出当前素材全部片段。";
            }
        }
        Data.Version = 1;
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
