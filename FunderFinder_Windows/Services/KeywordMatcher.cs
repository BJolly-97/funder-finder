using System.Text.RegularExpressions;
using FunderFinder.Models;

namespace FunderFinder.Services;

/// <summary>Scores calls against a <see cref="KeywordProfile"/>, using title and summary only.</summary>
public static class KeywordMatcher
{
    private const int OwnGroupWeight = 3;
    private const int BoostGroupWeight = 1;
    private const int TitleMultiplier = 2;

    private record CompiledTerm(Regex Pattern, KeywordGroup Group);

    /// <param name="showAllSources">
    /// Sources whose calls all count as matches regardless of keywords (still scored,
    /// so they rank sensibly against the rest).
    /// </param>
    public static void Apply(KeywordProfile profile, IEnumerable<FundingCall> calls, ISet<string>? showAllSources = null)
    {
        var terms = profile.Groups
            .SelectMany(g => g.Terms.Select(t => (Term: t, Group: g)))
            .Select(x => BuildPattern(x.Term) is { } re ? new CompiledTerm(re, x.Group) : null)
            .OfType<CompiledTerm>()
            .ToList();

        foreach (var call in calls)
            Score(call, terms, showAllSources?.Contains(call.SourceName) == true);
    }

    private static void Score(FundingCall call, List<CompiledTerm> terms, bool showAll)
    {
        var score = 0;
        var ownGroupsHit = new HashSet<KeywordGroup>();
        // Show what actually matched ("metallurgy", not "metallurg*"), own-group hits first.
        var ownHits = new List<string>();
        var boostHits = new List<string>();

        foreach (var term in terms)
        {
            var match = term.Pattern.Match(call.Title);
            var inTitle = match.Success;
            if (!inTitle) match = term.Pattern.Match(call.Summary);
            if (!match.Success) continue;

            var weight = term.Group.CountsOnItsOwn ? OwnGroupWeight : BoostGroupWeight;
            score += inTitle ? weight * TitleMultiplier : weight;

            var shown = Regex.Replace(match.Value, @"\s+", " ");
            // Keep acronyms as written (AI, HPC); lower-case ordinary words.
            if (shown.Any(char.IsLower)) shown = shown.ToLowerInvariant();

            if (term.Group.CountsOnItsOwn)
            {
                ownGroupsHit.Add(term.Group);
                ownHits.Add(shown);
            }
            else
            {
                boostHits.Add(shown);
            }
        }

        var keywordMatch = ownGroupsHit.Count > 0;
        var isStrong = ownGroupsHit.Count >= 2;
        var shownTerms = ownHits.Concat(boostHits).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        call.SetMatch(score, keywordMatch || showAll, isStrong, shownTerms, shownBySource: showAll && !keywordMatch);
    }

    /// <summary>
    /// Whole word, case-insensitive, singular or plural; spaces/hyphens interchangeable;
    /// a trailing * matches any word ending.
    /// </summary>
    private static Regex? BuildPattern(string term)
    {
        var t = term.Trim();
        var isPrefix = t.EndsWith('*');
        if (isPrefix) t = t[..^1];
        // Plural terms match their singular too: "critical minerals" → "critical mineral".
        // (Not "-ss" words like "glass", where the s isn't a plural.)
        else if (t.Length > 3 && t.EndsWith('s') && !t.EndsWith("ss")) t = t[..^1];

        var parts = Regex.Split(t, @"[\s\-]+").Where(p => p.Length > 0).Select(Regex.Escape).ToList();
        if (parts.Count == 0) return null;

        var core = string.Join(@"[\s\-]+", parts);
        var ending = isPrefix ? @"\w*" : "(?:s|es)?";
        return new Regex($@"(?<!\w){core}{ending}(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
