using System.Text.RegularExpressions;

namespace Viewcut;

internal static class ButtonIcons
{
    // Segoe Fluent Icons codepoints: https://learn.microsoft.com/windows/apps/design/iconography/segoe-fluent-icons-font
    public static string Resolve(string text)
    {
        if (text.StartsWith(L.T("导出当前素材全部片段"), StringComparison.Ordinal)) return "\uEDE1";
        if (text.StartsWith(L.T("片段") + "  ", StringComparison.Ordinal)) return "\uE78A";
        if (text.StartsWith(L.T("导出") + "  ", StringComparison.Ordinal)) return "\uE8FD";
        return (L.Key(text) ?? text) switch
        {
            "选择保存目录" => "\uE792", "打开保存目录" => "\uE838", "偏好设置" => "\uE713",
            "播放" => "\uE768", "暂停" => "\uE769", "P  上一条" => "\uE892", "N  下一条" => "\uE893",
            "I  起点" => "mark-in", "O  终点" => "mark-out", "全览" => "\uE9A6",
            "−" => "\uE71F", "+" => "\uE8A3", "撤销" => "\uE7A7", "重做" => "\uE7A6",
            "文件" => "\uE8B7", "片段" => "\uE78A", "导出" => "\uE8FD", "重试选中任务" => "\uE72C",
            "确定" => "\uE73E", "取消" or "关闭" => "\uE711",
            _ => "\uE712"
        };
    }

    public static string Label(string text, string? accessible)
    {
        var value = string.IsNullOrWhiteSpace(text) ? accessible ?? "" : text;
        return value switch
        {
            "−" => L.T("缩小时间轴"), "+" => L.T("放大时间轴"),
            _ when value == L.T("播放") || value == L.T("暂停") => value + "  (Space)",
            _ => value
        };
    }

    public static string? Badge(string text)
    {
        var match = Regex.Match(text, @"(?:\s{2}|\()(\d+)\)?$");
        if (!match.Success) return null;
        var number = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return number > 999 ? "999+" : number.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
