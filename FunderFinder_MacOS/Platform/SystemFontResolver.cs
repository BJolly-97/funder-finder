using PdfSharp.Fonts;

namespace FunderFinder.Mac.Platform;

/// <summary>
/// Supplies fonts to the PDF exporter. PDFsharp ships none, so this hands it Arial from
/// the system (bundled with every macOS since Catalina), falling back to Helvetica-like
/// equivalents elsewhere. The exporter asks for "Segoe UI"; every family maps here.
/// </summary>
internal sealed class SystemFontResolver : IFontResolver
{
    // Candidate folders, in order: macOS, then Windows and Linux (for running this build there).
    private static readonly string[] Folders =
    {
        "/System/Library/Fonts/Supplemental",
        "/Library/Fonts",
        Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
        "/usr/share/fonts/truetype/msttcorefonts",
        "/usr/share/fonts/truetype/liberation",
    };

    private static readonly Dictionary<string, string[]> Faces = new()
    {
        ["regular"] = new[] { "Arial.ttf", "arial.ttf", "LiberationSans-Regular.ttf" },
        ["bold"] = new[] { "Arial Bold.ttf", "arialbd.ttf", "LiberationSans-Bold.ttf" },
        ["italic"] = new[] { "Arial Italic.ttf", "ariali.ttf", "LiberationSans-Italic.ttf" },
        ["bolditalic"] = new[] { "Arial Bold Italic.ttf", "arialbi.ttf", "LiberationSans-BoldItalic.ttf" },
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
        => new((bold, italic) switch
        {
            (true, true) => "bolditalic",
            (true, false) => "bold",
            (false, true) => "italic",
            _ => "regular",
        });

    public byte[]? GetFont(string faceName)
    {
        foreach (var folder in Folders.Where(f => !string.IsNullOrEmpty(f) && Directory.Exists(f)))
            foreach (var file in Faces[faceName])
            {
                var path = Path.Combine(folder, file);
                if (File.Exists(path)) return File.ReadAllBytes(path);
            }
        return null;
    }
}
