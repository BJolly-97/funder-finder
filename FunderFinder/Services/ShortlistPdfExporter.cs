using FunderFinder.Models;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace FunderFinder.Services;

/// <summary>
/// Writes shortlisted calls to an A4 PDF for sharing: grouped like the app
/// (closing soon, general, closed), each with its title linked to the call's page
/// and the full web address printed underneath.
/// </summary>
public static class ShortlistPdfExporter
{
    private const string FontName = "Segoe UI";

    // The app palette, so the PDF looks like it came from the same place.
    private static readonly Color Bark = Color.FromRgb(0x7F, 0x55, 0x39);
    private static readonly Color Clay = Color.FromRgb(0x9C, 0x66, 0x44);
    private static readonly Color Ink = Color.FromRgb(0x3D, 0x28, 0x17);
    private static readonly Color Secondary = Color.FromRgb(0x5A, 0x3D, 0x27);
    private static readonly Color Brick = Color.FromRgb(0x7A, 0x30, 0x20);
    private static readonly Color Rule = Color.FromRgb(0xDD, 0xB8, 0x92);

    static ShortlistPdfExporter()
    {
        // PDFsharp's cross-platform build ships no fonts; point it at the installed Windows ones.
        GlobalFontSettings.FontResolver ??= new WindowsFontResolver();
    }

    public static void Export(string filePath, IReadOnlyList<FundingCall> calls, DateTime exportedAt)
    {
        var doc = new Document();
        doc.Info.Title = "Funding opportunities shortlist";
        doc.Info.Author = "Funder Finder";

        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = FontName;
        normal.Font.Size = 9.5;
        normal.Font.Color = Ink;

        var section = doc.AddSection();
        section.PageSetup = doc.DefaultPageSetup.Clone();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(2);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2.2);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2.2);

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 8;
        footer.Format.Font.Color = Secondary;
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.AddText("Funder Finder shortlist · page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();

        var title = section.AddParagraph("Funding opportunities shortlist");
        title.Format.Font.Size = 18;
        title.Format.Font.Bold = true;
        title.Format.Font.Color = Bark;

        var subtitle = section.AddParagraph(
            $"{calls.Count} opportunit{(calls.Count == 1 ? "y" : "ies")} · exported {exportedAt:d MMMM yyyy}");
        subtitle.Format.Font.Color = Secondary;
        subtitle.Format.SpaceAfter = Unit.FromPoint(14);

        foreach (var group in calls.GroupBy(c => c.Bucket).OrderBy(g => g.Key))
        {
            var heading = section.AddParagraph(group.Key switch
            {
                DeadlineBucket.ClosingSoon => "Due within two weeks",
                DeadlineBucket.Closed => "Closed",
                _ => "General",
            });
            heading.Format.Font.Size = 12;
            heading.Format.Font.Bold = true;
            heading.Format.Font.Color = group.Key == DeadlineBucket.ClosingSoon ? Brick : Bark;
            heading.Format.SpaceBefore = Unit.FromPoint(6);
            heading.Format.SpaceAfter = Unit.FromPoint(6);

            foreach (var call in group)
                AddCall(section, call);
        }

        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(filePath);
    }

    private static void AddCall(Section section, FundingCall call)
    {
        var hasLink = Uri.TryCreate(call.Url, UriKind.Absolute, out _);

        // Title, linked to the call's page.
        var titlePara = section.AddParagraph();
        titlePara.Format.KeepWithNext = true;
        titlePara.Format.SpaceAfter = Unit.FromPoint(2);
        if (hasLink)
        {
            var link = titlePara.AddHyperlink(call.Url, HyperlinkType.Web);
            var text = link.AddFormattedText(call.Title, TextFormat.Bold);
            text.Font.Size = 11.5;
            text.Font.Color = Clay;
        }
        else
        {
            var text = titlePara.AddFormattedText(call.Title, TextFormat.Bold);
            text.Font.Size = 11.5;
        }

        // Funder · amount · dates.
        var facts = new List<string> { call.Funder };
        if (!string.IsNullOrWhiteSpace(call.Amount)) facts.Add(call.Amount!);
        if (call.Opens.HasValue && call.Opens.Value > DateTime.Now) facts.Add($"opens {call.Opens:d MMM yyyy}");
        facts.Add(call.Deadline.HasValue ? $"closes {call.Deadline:d MMM yyyy}" : $"closing date: {call.DeadlineLabel.ToLowerInvariant()}");
        var meta = section.AddParagraph(string.Join("  ·  ", facts.Where(f => !string.IsNullOrWhiteSpace(f))));
        meta.Format.Font.Color = Secondary;
        meta.Format.Font.Size = 9;
        meta.Format.KeepWithNext = true;
        meta.Format.SpaceAfter = Unit.FromPoint(4);

        if (!string.IsNullOrWhiteSpace(call.Summary))
        {
            var summary = section.AddParagraph(call.Summary);
            summary.Format.SpaceAfter = Unit.FromPoint(3);
            summary.Format.KeepWithNext = true;
        }

        // UKRI summaries already include the eligibility sentence, so don't repeat it.
        if (!string.IsNullOrWhiteSpace(call.Eligibility) && !call.Summary.Contains(call.Eligibility))
        {
            var eligibility = section.AddParagraph();
            eligibility.Format.SpaceAfter = Unit.FromPoint(3);
            eligibility.Format.KeepWithNext = true;
            eligibility.Format.Font.Size = 9;
            eligibility.AddFormattedText("Eligibility: ", TextFormat.Bold);
            eligibility.AddText(call.Eligibility);
        }

        if (call.MatchedTerms.Count > 0)
        {
            var keywords = section.AddParagraph($"Keywords: {call.MatchLabel}");
            keywords.Format.Font.Size = 8.5;
            keywords.Format.Font.Italic = true;
            keywords.Format.Font.Color = Secondary;
            keywords.Format.SpaceAfter = Unit.FromPoint(2);
            keywords.Format.KeepWithNext = true;
        }

        if (hasLink)
        {
            var linkPara = section.AddParagraph();
            linkPara.Format.Font.Size = 8.5;
            linkPara.Format.KeepWithNext = true; // with the rule below, so a call never splits across pages
            var link = linkPara.AddHyperlink(call.Url, HyperlinkType.Web);
            var text = link.AddFormattedText(call.Url);
            text.Font.Color = Clay;
            text.Font.Underline = Underline.Single;
        }

        // Thin rule between calls.
        var rule = section.AddParagraph();
        rule.Format.SpaceBefore = Unit.FromPoint(6);
        rule.Format.SpaceAfter = Unit.FromPoint(8);
        rule.Format.Borders.Bottom.Width = Unit.FromPoint(0.6);
        rule.Format.Borders.Bottom.Color = Rule;
    }
}
