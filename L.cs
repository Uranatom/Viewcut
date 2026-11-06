using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Viewcut;

internal static class L
{
    private static readonly Dictionary<string, string> English = JsonSerializer.Deserialize<Dictionary<string, string>>(
        typeof(L).Assembly.GetManifestResourceStream("Viewcut.Localization.en.json")!)!;
    private static readonly Dictionary<string, string> Originals = English.GroupBy(pair => pair.Value)
        .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);
    private static readonly ConditionalWeakTable<Control, Bindings> Bound = new();
    private static readonly (Regex Pattern, string Key, bool FromEnglish)[] Templates = BuildTemplates();
    private static volatile string language = "zh-CN";
    public static string Language => language;
    public static bool IsEnglish => language == "en-US";
    public static event Action? Changed;

    public static void SetLanguage(string? value)
    {
        var next = value == "en-US" ? "en-US" : "zh-CN";
        if (language == next) return;
        language = next;
        Changed?.Invoke();
    }

    public static string T(string key) => IsEnglish && English.TryGetValue(key, out var text) ? text : key;
    public static string F(string key, params object?[] values) => string.Format(
        CultureInfo.GetCultureInfo(language), T(key), values);
    public static string? Key(string? text) => text is null ? null
        : English.ContainsKey(text) ? text : Originals.GetValueOrDefault(text);

    // Translate existing status messages without changing captured filenames or user-entered names.
    public static string Translate(string text)
    {
        if (Key(text) is { } key) return T(key);
        foreach (var template in Templates)
        {
            if (template.FromEnglish == IsEnglish) continue;
            var match = template.Pattern.Match(text);
            if (!match.Success) continue;
            return Regex.Replace(T(template.Key), @"\{(\d+)(?:[^}]*)\}", part =>
            {
                var value = match.Groups["a" + part.Groups[1].Value].Value;
                return template.Key is "已撤销：{0}。" or "已重做：{0}。" or "撤销：{0}" or "重做：{0}"
                    ? T(Key(value) ?? value) : value;
            });
        }
        return text;
    }

    public static void ApplyTree(Control root)
    {
        var binding = Bound.GetValue(root, control => new Bindings(
            control is TextBoxBase or ComboBox ? null : Key(control.Text),
            Key(control.AccessibleName), Key(control.AccessibleDescription),
            control is TextBox input ? Key(input.PlaceholderText) : null));
        if (binding.Text is { } text) root.Text = T(text);
        if (binding.Name is { } name) root.AccessibleName = T(name);
        if (binding.Description is { } description) root.AccessibleDescription = T(description);
        if (root is TextBox box && binding.Placeholder is { } placeholder) box.PlaceholderText = T(placeholder);
        if (root is ListView list)
            foreach (ColumnHeader column in list.Columns)
                if (Key(column.Text) is { } title) column.Text = T(title);
        if (root.ContextMenuStrip is { } menu) ApplyMenu(menu.Items);
        foreach (Control child in root.Controls) ApplyTree(child);
    }

    public static void ApplyMenu(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.Text = Translate(item.Text ?? "");
            if (item is ToolStripDropDownItem dropdown) ApplyMenu(dropdown.DropDownItems);
        }
    }

    private sealed record Bindings(string? Text, string? Name, string? Description, string? Placeholder);

    private static (Regex, string, bool)[] BuildTemplates()
    {
        var result = new List<(Regex, string, bool)>();
        foreach (var pair in English.Where(pair => pair.Key.Contains("{0}")))
            foreach (var fromEnglish in new[] { false, true })
            {
                var pattern = Regex.Escape(fromEnglish ? pair.Value : pair.Key);
                pattern = Regex.Replace(pattern, @"\\\{(\d+)(?:[^}]*?)\}", match => "(?<a" + match.Groups[1].Value + ">.*?)");
                result.Add((new Regex("\\A" + pattern + "\\z", RegexOptions.Singleline | RegexOptions.CultureInvariant), pair.Key, fromEnglish));
            }
        return result.ToArray();
    }
}
