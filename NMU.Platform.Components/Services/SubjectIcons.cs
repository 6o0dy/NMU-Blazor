namespace NMU.Platform.Components.Services;

/// <summary>
/// Single shared subject-icon catalog for the whole app (Study Materials,
/// Recorded Lectures and Quizzes all resolve through here, so every course
/// shows the SAME icon everywhere).
///
/// Resolution order:
/// 1. Exact course code (e.g. "CSE014") — one dedicated, never-repeated icon
///    per course, taken from the official CE/AIE study plans.
/// 2. Known non-plan names (e.g. "Technical Report").
/// 3. Keyword fallback for anything unknown (content words win over
///    department-code traps like MEC/MAT).
/// </summary>
public static class SubjectIcons
{
    public sealed record SubjectIcon(string Icon, string ColorClass);

    private const string Solid = "fa-solid";

    private static string FA(string name) => $"{Solid} fa-{name}";

    private static readonly Dictionary<string, SubjectIcon> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        // ---------- Core ----------
        ["MEC011"] = new(FA("compass-drafting"), "color-draw"),
        ["CSE014"] = new(FA("code"), "color-prog"),
        ["PHY212"] = new(FA("atom"), "color-phys"),
        ["MAT111"] = new(FA("calculator"), "color-math"),
        ["MAT123"] = new(FA("gears"), "color-mech"),
        ["UC1"] = new(FA("building-columns"), "color-default"),
        ["UE1"] = new(FA("graduation-cap"), "color-default"),
        ["CSE015"] = new(FA("cubes"), "color-prog"),
        ["CSE113"] = new(FA("microchip"), "color-mech"),
        ["PHY211"] = new(FA("magnet"), "color-phys"),
        ["MAT112"] = new(FA("divide"), "color-math"),
        ["MAT131"] = new(FA("chart-pie"), "color-math"),
        ["UC2"] = new(FA("school"), "color-default"),
        ["ELE212"] = new(FA("gauge-high"), "color-mech"),
        ["CSE111"] = new(FA("folder-tree"), "color-prog"),
        ["CSE131"] = new(FA("plug"), "color-mech"),
        ["AIE111"] = new(FA("brain"), "color-prog"),
        ["MAT313"] = new(FA("wave-square"), "color-math"),
        ["UC3"] = new(FA("landmark"), "color-default"),
        ["ELE432"] = new(FA("tower-broadcast"), "color-mech"),
        ["CSE112"] = new(FA("code-branch"), "color-prog"),
        ["CSE132"] = new(FA("server"), "color-prog"),
        ["CSE315"] = new(FA("share-nodes"), "color-math"),
        ["AIE121"] = new(FA("robot"), "color-prog"),
        ["UC4"] = new(FA("book-atlas"), "color-default"),

        // ---------- CE track ----------
        ["CSE211"] = new(FA("globe"), "color-prog"),
        ["CSE233"] = new(FA("desktop"), "color-prog"),
        ["CSE261"] = new(FA("network-wired"), "color-prog"),
        ["CSE281"] = new(FA("image"), "color-prog"),
        ["UE2"] = new(FA("user-graduate"), "color-default"),
        ["CSE221"] = new(FA("database"), "color-prog"),
        ["CSE242"] = new(FA("key"), "color-tech"),
        ["CSE251"] = new(FA("laptop-file"), "color-prog"),
        ["CSE272"] = new(FA("memory"), "color-mech"),
        ["CSE273"] = new(FA("layer-group"), "color-prog"),
        ["CSE291"] = new(FA("briefcase"), "color-tech"),
        ["CSE344"] = new(FA("shield-halved"), "color-tech"),
        ["CSE376"] = new(FA("stopwatch"), "color-mech"),
        ["CSE322"] = new(FA("chart-column"), "color-prog"),
        ["CSE363"] = new(FA("cloud"), "color-prog"),
        ["CSE392"] = new(FA("helmet-safety"), "color-tech"),
        ["CSE464"] = new(FA("house-signal"), "color-prog"),
        ["CSE477"] = new(FA("screwdriver-wrench"), "color-mech"),
        ["CSE493"] = new(FA("medal"), "color-tech"),
        ["CSE446"] = new(FA("shield-cat"), "color-tech"),
        ["CSE466"] = new(FA("wifi"), "color-prog"),
        ["CSE494"] = new(FA("crown"), "color-tech"),
        ["CSE362"] = new(FA("industry"), "color-mech"),
        ["CSE212"] = new(FA("infinity"), "color-prog"),
        ["CSE374"] = new(FA("clone"), "color-prog"),
        ["CSE427"] = new(FA("filter"), "color-prog"),
        ["CSE478"] = new(FA("hard-drive"), "color-prog"),
        ["CSE479"] = new(FA("forward-fast"), "color-prog"),
        ["CSE241"] = new(FA("user-shield"), "color-tech"),
        ["CSE243"] = new(FA("lock"), "color-tech"),
        ["CSE445"] = new(FA("shield"), "color-tech"),
        ["CSE447"] = new(FA("fingerprint"), "color-tech"),
        ["CSE448"] = new(FA("user-secret"), "color-tech"),
        ["CSE271"] = new(FA("terminal"), "color-prog"),
        ["CSE311"] = new(FA("gear"), "color-prog"),
        ["CSE383"] = new(FA("eye"), "color-prog"),
        ["CSE425"] = new(FA("chart-line"), "color-prog"),
        ["CSE465"] = new(FA("cloud-arrow-up"), "color-prog"),
        ["CSE475"] = new(FA("sitemap"), "color-prog"),
        ["CSE382"] = new(FA("bezier-curve"), "color-prog"),
        ["CSE426"] = new(FA("vial"), "color-prog"),
        ["CSE323"] = new(FA("table-list"), "color-prog"),
        ["UC5"] = new(FA("scroll"), "color-default"),
        ["UC6"] = new(FA("certificate"), "color-default"),
        ["UC7"] = new(FA("stamp"), "color-default"),
        ["UE3"] = new(FA("book"), "color-default"),
        ["E1"] = new(FA("1"), "color-tech"),
        ["E2"] = new(FA("2"), "color-tech"),
        ["E3"] = new(FA("3"), "color-tech"),
        ["E4"] = new(FA("4"), "color-tech"),
        ["E5"] = new(FA("5"), "color-tech"),
        ["E6"] = new(FA("6"), "color-tech"),
        ["E7"] = new(FA("7"), "color-tech"),
        ["E8"] = new(FA("8"), "color-tech"),

        // ---------- AIE track ----------
        ["AIE231"] = new(FA("circle-nodes"), "color-prog"),
        ["AIE241"] = new(FA("comments"), "color-prog"),
        ["AIE213"] = new(FA("sliders"), "color-prog"),
        ["AIE291"] = new(FA("suitcase"), "color-tech"),
        ["AIE322"] = new(FA("diagram-project"), "color-prog"),
        ["AIE323"] = new(FA("gem"), "color-prog"),
        ["AIE332"] = new(FA("object-group"), "color-prog"),
        ["AIE351"] = new(FA("toolbox"), "color-mech"),
        ["AIE392"] = new(FA("hard-hat"), "color-tech"),
        ["AIE425"] = new(FA("thumbs-up"), "color-prog"),
        ["AIE493"] = new(FA("award"), "color-tech"),
        ["AIE494"] = new(FA("trophy"), "color-tech"),
        ["AIE315"] = new(FA("toggle-on"), "color-prog"),
        ["AIE316"] = new(FA("dna"), "color-prog"),
        ["AIE342"] = new(FA("table-cells"), "color-prog"),
        ["AIE343"] = new(FA("newspaper"), "color-prog"),
        ["AIE417"] = new(FA("circle-question"), "color-prog"),
        ["AIE418"] = new(FA("lightbulb"), "color-prog"),
        ["AIE426"] = new(FA("shuffle"), "color-prog"),
        ["AIE427"] = new(FA("qrcode"), "color-prog"),
        ["AIE444"] = new(FA("comment-dots"), "color-prog"),
        ["AIE452"] = new(FA("cube"), "color-prog"),
        ["AIE453"] = new(FA("route"), "color-prog"),
        ["AIE454"] = new(FA("person-running"), "color-prog"),
        ["AIE455"] = new(FA("map-location-dot"), "color-prog"),
        ["AIE456"] = new(FA("handshake"), "color-prog"),
        ["AIE457"] = new(FA("truck-fast"), "color-prog"),
        ["AIE419"] = new(FA("gamepad"), "color-prog"),
        ["AIE314"] = new(FA("keyboard"), "color-prog"),
        ["AIE424"] = new(FA("life-ring"), "color-prog"),
        ["AIE212"] = new(FA("book-bookmark"), "color-prog"),

        // ---------- Elective pool / non-core ----------
        ["ELE211"] = new(FA("bolt"), "color-mech"),
        ["ELE331"] = new(FA("satellite"), "color-mech"),
        ["ELE232"] = new(FA("gauge"), "color-mech"),
        ["CHE142"] = new(FA("flask-vial"), "color-phys"),
        ["MAT121"] = new(FA("fire"), "color-math"),
        ["MAT122"] = new(FA("anchor"), "color-math"),
        ["MAT231"] = new(FA("dice"), "color-math"),
        ["BMD462"] = new(FA("leaf"), "color-prog"),
        ["LAN011"] = new(FA("pen-nib"), "color-arabic"),
        ["LAN021"] = new(FA("language"), "color-english"),
        ["LAN022"] = new(FA("spell-check"), "color-english"),
        ["PSC101"] = new(FA("scale-balanced"), "color-english"),
    };

    // Subjects without a plan code (normalized name match).
    private static readonly Dictionary<string, SubjectIcon> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["technicalreport"] = new(FA("file-lines"), "color-tech"),
        ["technicalwriting"] = new(FA("file-lines"), "color-tech"),
        ["communicationskills"] = new(FA("person-chalkboard"), "color-english"),
        ["humanrights"] = new(FA("scale-balanced"), "color-english"),
    };

    /// <summary>Resolve the shared icon+color for a subject.</summary>
    public static SubjectIcon Resolve(string? code, string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(code) && ByCode.TryGetValue(code.Trim(), out var hit))
            return hit;

        var norm = Models.SubjectMatcher.Normalize(displayName ?? "");
        if (!string.IsNullOrEmpty(norm) && ByName.TryGetValue(norm, out var named))
            return named;

        return KeywordFallback($"{displayName} {code}");
    }

    /// <summary>
    /// Keyword fallback for unknown subjects. Distinctive content words win
    /// over short department-code traps (MEC/MAT/...) so a code can never
    /// hijack an unrelated subject's icon.
    /// </summary>
    internal static SubjectIcon KeywordFallback(string name)
    {
        var n = (name ?? "").ToLowerInvariant();
        if (n.Contains("arabic")) return new(FA("pen-nib"), "color-arabic");
        if (n.Contains("english") || n.Contains("communication") || n.Contains("psychology") || n.Contains("history"))
            return new(FA("language"), "color-english");
        if (n.Contains("university") || n.Contains("social") || n.Contains("management") || n.Contains("marketing") || n.Contains("humanities"))
            return new(FA("building-columns"), "color-english");
        if (n.Contains("report") || n.Contains("writ"))
            return new(FA("file-lines"), "color-tech");
        if (n.Contains("draw") || n.Contains("graphic") || n.Contains("vision") || n.Contains("image") || n.Contains("visual") || n.Contains("game") || n.Contains("animation") || n.Contains("reality"))
            return new(FA("compass-drafting"), "color-draw");
        if (n.Contains("mec") || n.Contains("mech") || n.Contains("static") || n.Contains("dynamic") || n.Contains("control") || n.Contains("material"))
            return new(FA("gears"), "color-mech");
        if (n.Contains("math") || n.Contains("calc") || n.Contains("algebra") || n.Contains("diff") || n.Contains("stat") || n.Contains("numerical") || n.Contains("discrete") || n.Contains("optimization") || n.Contains("analysis") || n.Contains("probabilit"))
            return new(FA("calculator"), "color-math");
        if (n.Contains("phy") || n.Contains("phys") || n.Contains("chem") || n.Contains("magnetic") || n.Contains("optic") || n.Contains("field"))
            return new(FA("atom"), "color-phys");
        if (n.Contains("security") || n.Contains("secure") || n.Contains("crypto") || n.Contains("forensic") || n.Contains("cyber"))
            return new(FA("shield-halved"), "color-tech");
        if (n.Contains("robot") || n.Contains("kinematic") || n.Contains("map") || n.Contains("localiz") || n.Contains("autonomous"))
            return new(FA("robot"), "color-mech");
        if (n.Contains("database") || n.Contains("sql"))
            return new(FA("database"), "color-prog");
        if (n.Contains("ele") || n.Contains("electric") || n.Contains("electronic") || n.Contains("circuit") || n.Contains("embedded") || n.Contains("iot") || n.Contains("internet of things") || n.Contains("signal") || n.Contains("measure") || n.Contains("network") || n.Contains("architect") || n.Contains("organization") || n.Contains("logic") || n.Contains("hardware") || n.Contains("sensor"))
            return new(FA("microchip"), "color-mech");
        if (n.Contains("aie") || n.Contains("ai") || n.Contains("intelligen") || n.Contains("learning") || n.Contains("neural") || n.Contains("knowledg") || n.Contains("mining") || n.Contains("data") || n.Contains("nlp") || n.Contains("natural") || n.Contains("cognitive") || n.Contains("recommender") || n.Contains("pattern") || n.Contains("evolution") || n.Contains("reasoning") || n.Contains("fuzzy") || n.Contains("bio-inspired") || n.Contains("decision"))
            return new(FA("brain"), "color-prog");
        if (n.Contains("cse") || n.Contains("prog") || n.Contains("code") || n.Contains("struct") || n.Contains("object") || n.Contains("oop") || n.Contains("web") || n.Contains("soft") || n.Contains("cloud") || n.Contains("parallel") || n.Contains("distribut") || n.Contains("compiler") || n.Contains("comput") || n.Contains("algorithm") || n.Contains("os") || n.Contains("operating") || n.Contains("system") || n.Contains("high performance"))
            return new(FA("laptop-code"), "color-prog");
        if (n.Contains("tech") || n.Contains("search") || n.Contains("project") || n.Contains("training") || n.Contains("grad"))
            return new(FA("file-lines"), "color-tech");
        return new(FA("book-open"), "color-default");
    }
}
