using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace FunderFinder.Models;

/// <summary>
/// One editable list of terms. A term matches as a whole word, case-insensitively,
/// with an optional plural "s"/"es"; spaces and hyphens are interchangeable
/// ("thin film" matches "thin-films"); a trailing * matches any ending ("metallurg*").
/// </summary>
public class KeywordGroup
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// True: a hit here is enough for a call to count as a match. False: hits only
    /// boost the ranking of calls that already matched — for words so common
    /// ("research", "engineering") that they'd otherwise let everything through.
    /// </summary>
    public bool CountsOnItsOwn { get; set; }

    public List<string> Terms { get; set; } = new();

    /// <summary>Comma/newline-separated form for the Filter tab's text box.</summary>
    [JsonIgnore]
    public string TermsText
    {
        get => string.Join(", ", Terms);
        set => Terms = value
            .Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public class KeywordProfile
{
    /// <summary>Observable so groups added or removed on the Filter tab show up straight away.</summary>
    public ObservableCollection<KeywordGroup> Groups { get; set; } = new();

    /// <summary>
    /// Starting point, tuned for AI / modelling &amp; simulation in materials science.
    /// A call hitting two different counts-on-its-own groups is a "strong match" —
    /// with these defaults, that's digital methods applied to materials.
    /// </summary>
    public static KeywordProfile CreateDefault() => new()
    {
        Groups =
        {
            new KeywordGroup
            {
                Name = "Materials",
                CountsOnItsOwn = true,
                Terms =
                {
                    "materials", "advanced materials", "materials discovery", "materials 4.0", "materials informatics",
                    "materials by design", "materials modelling", "materials modeling", "functional materials",
                    "smart materials", "sustainable materials", "energy materials", "critical materials",
                    "critical minerals", "rare earth*", "biomaterial*", "metal*", "metallurg*", "alloy*", "superalloy*",
                    "steel*", "aluminium", "aluminum", "titanium", "polymer*", "plastics", "composite*", "ceramic*",
                    "glass", "concrete", "cement", "semiconductor*", "thin film*", "coating*", "surface engineering",
                    "nanomaterial*", "nanotechnolog*", "graphene", "2D materials", "battery", "batteries",
                    "electrode*", "electrolyte*", "cathode*", "anode*", "catalysis", "catalytic", "superconduct*",
                    "magnetic materials", "photovoltaic*", "additive manufacturing", "3D printing", "welding",
                    "corrosion", "characterisation", "characterization", "microscopy", "crystallograph*",
                },
            },
            new KeywordGroup
            {
                Name = "AI, modelling & digital",
                CountsOnItsOwn = true,
                Terms =
                {
                    "AI", "artificial intelligence", "machine learning", "machine intelligence", "deep learning",
                    "neural network*", "foundation model*", "large language model*", "LLM*", "world model*",
                    "agentic", "data science", "data-driven", "modelling", "modeling", "model-based", "simulation*",
                    "simulate*", "multiscale", "computational", "in silico", "high performance computing", "HPC",
                    "supercomput*", "exascale", "digital twin*", "digital engineering", "virtual testing",
                    "finite element*", "CFD", "computational fluid dynamics", "molecular dynamics",
                    "density functional theory", "uncertainty quantification", "inverse design", "high-throughput",
                    "informatics", "software", "coding", "autonomy", "autonomous", "robot*", "automation",
                    "automated", "self-driving lab*", "lab automation", "laboratory automation",
                    "materials acceleration platform*", "digital materials", "digital manufacturing", "quantum",
                    "industry 4.0", "cyber-physical",
                },
            },
            new KeywordGroup
            {
                Name = "Sectors",
                CountsOnItsOwn = false,
                Terms =
                {
                    "aerospace", "aviation", "automotive", "vehicle*", "space", "satellite*", "defence", "defense",
                    "nuclear", "fusion", "energy", "net zero", "hydrogen", "electrification", "electronics",
                    "photonics", "marine", "maritime", "rail", "construction", "manufacturing", "chemicals",
                    "pharmaceutical*",
                },
            },
            new KeywordGroup
            {
                Name = "General",
                CountsOnItsOwn = false,
                Terms =
                {
                    "engineering", "science", "scientific", "research", "R&D", "laboratory", "laboratories", "lab",
                    "sustainability", "sustainable", "circular economy", "circularity", "recycling",
                    "decarbonisation", "decarbonization", "decarbonise*",
                },
            },
        },
    };
}
