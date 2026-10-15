using System.IO;
using PdfSharp.Fonts;

namespace FunderFinder.Services;

/// <summary>
/// PDFsharp's cross-platform build ships no fonts, so it asks a resolver for them.
/// This hands it Segoe UI from the Windows fonts folder — for every family requested,
/// since the PDF only uses one typeface.
/// </summary>
internal sealed class WindowsFontResolver : IFontResolver
{
    private static readonly string FontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var face = (bold, italic) switch
        {
            (true, true) => "segoeuiz",
            (true, false) => "segoeuib",
            (false, true) => "segoeuii",
            _ => "segoeui",
        };
        return new FontResolverInfo(face);
    }

    public byte[]? GetFont(string faceName)
    {
        var path = Path.Combine(FontsFolder, faceName + ".ttf");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}
