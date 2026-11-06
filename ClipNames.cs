namespace Viewcut;

internal static class ClipNames
{
    public static string? Validate(string? name, Guid clipId, IEnumerable<ClipDraft> clips, string? folder, string extension)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (name.Length > 100 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.EndsWith(' ') || name.EndsWith('.') || name is "." or "..")
            return "名称最多 100 个字符，不能包含文件名非法字符，末尾不能是空格或句点。";
        var firstPart = name.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(firstPart, StringComparer.OrdinalIgnoreCase)
            || (firstPart.Length == 4 && (firstPart.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || firstPart.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(firstPart[3])))
            return "这个名称是 Windows 保留名称，请换一个名称。";
        if (clips.Any(clip => clip.Id != clipId && string.Equals(clip.Name, name, StringComparison.OrdinalIgnoreCase)))
            return "已有片段使用这个名称，请换一个名称。";
        if (folder is not null && OutputExists(Path.Combine(folder, name + extension)))
            return "保存目录中已有同名文件，请换一个名称。";
        return null;
    }

    public static bool OutputExists(string path) => File.Exists(path) || Directory.Exists(path);
}

internal sealed class ClipNameConflictException(string path) : IOException($"保存目录中已有同名文件：{Path.GetFileName(path)}");
