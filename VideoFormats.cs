namespace Viewcut;

internal static class VideoFormats
{
    internal static string? Muxer(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp4" or ".m4v" => "mp4",
        ".mov" => "mov",
        ".mkv" => "matroska",
        ".webm" => "webm",
        ".avi" => "avi",
        ".ts" or ".m2ts" => "mpegts",
        _ => null
    };

    internal static bool Supports(string path) => Muxer(path) is not null;
}
