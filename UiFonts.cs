namespace Viewcut;

// Windows 10 does not ship the Windows 11 variable and Fluent icon fonts.
internal static class UiFonts
{
    private static readonly HashSet<string> Installed = FontFamily.Families
        .Select(font => font.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static bool Classic => Environment.GetEnvironmentVariable("VIEWCUT_TEST_CLASSIC_FONTS") == "1";
    public static string Text => !Classic && Installed.Contains("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
    public static string Display => !Classic && Installed.Contains("Segoe UI Variable Display Semib") ? "Segoe UI Variable Display Semib" : "Segoe UI";
    public static string Icons => !Classic && Installed.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    public static string Glyph(string glyph) => Icons == "Segoe MDL2 Assets" ? glyph switch
    {
        "\uEDE1" => "\uE898", // Export: use a Windows 10 era glyph.
        "\uEDA2" => "\uE7F1", // Drive.
        _ => glyph
    } : glyph;
}
