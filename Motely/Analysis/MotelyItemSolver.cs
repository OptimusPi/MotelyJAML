using Motely.Filters.Jaml;

namespace Motely.Analysis;

public sealed record MotelySolverStep(int Ante, string Action, string Detail);

public sealed record MotelyItemPath(string Seed, MotelyItem Target, IReadOnlyList<MotelySolverStep> Steps, int Cost)
{
    public string Source => Steps.Count == 0 ? "" : Steps[^1].Action;
    public int Ante => Steps.Count == 0 ? 0 : Steps[^1].Ante;
}

public sealed record MotelySolverBudget(
    int ShopSlots = 2,
    int MaxRerolls = 12,
    int MaxDepth = 3,
    int EventRolls = 20
);

public static class MotelyItemSolver
{
    public static IReadOnlyList<MotelyItemPath> Solve(
        string seed,
        string targetName,
        int firstAnte,
        int lastAnte,
        MotelyDeck deck = MotelyDeck.Red,
        MotelyStake stake = MotelyStake.White,
        MotelySolverBudget? budget = null
    )
    {
        if (!MotelyItem.TryParse(targetName, out var target))
            throw new ArgumentException($"Unknown item '{targetName}'.", nameof(targetName));

        budget ??= new MotelySolverBudget();
        var config = new JamlConfig
        {
            Id = "solver",
            Deck = deck,
            Stake = stake,
            Seeds = [seed],
        };
        var results = MotelyJamlyzer.Analyze(config, budget.EventRolls);
        if (results.Count == 0)
            return [];

        var antes = results[0].Antes.Where(a => a.Ante >= firstAnte && a.Ante <= lastAnte).ToList();
        var paths = new List<MotelyItemPath>();
        var seen = new HashSet<string>();
        SolveInto(seed, target, antes, budget, depth: 1, paths, seen, prefix: []);
        return paths.OrderBy(p => p.Cost).ThenBy(p => p.Ante).ToList();
    }

    private static bool SameType(MotelyItem a, MotelyItem b) => a.Type == b.Type;

    private static void SolveInto(
        string seed,
        MotelyItem target,
        List<MotelyJamlyzerAnteResult> antes,
        MotelySolverBudget budget,
        int depth,
        List<MotelyItemPath> paths,
        HashSet<string> seen,
        List<MotelySolverStep> prefix
    )
    {
        if (depth > budget.MaxDepth)
            return;

        foreach (var ante in antes)
        {
            for (int i = 0; i < ante.ShopItems.Count; i++)
            {
                if (!SameType(ante.ShopItems[i], target)) continue;
                int reroll = i / budget.ShopSlots, slot = i % budget.ShopSlots;
                if (reroll > budget.MaxRerolls) break;
                Emit(seed, target, paths, seen, prefix, ante.Ante, "shop",
                    $"reroll {reroll}, slot {slot}" + (ante.ShopItems[i].Edition != MotelyItemEdition.None ? $" ({ante.ShopItems[i].Edition})" : ""),
                    cost: reroll + depth);
            }

            for (int p = 0; p < ante.Packs.Count; p++)
            {
                var pack = ante.Packs[p];
                for (int j = 0; j < pack.Items.Count; j++)
                {
                    if (!SameType(pack.Items[j], target)) continue;
                    Emit(seed, target, paths, seen, prefix, ante.Ante, "pack",
                        $"{pack.Pack} (pack #{p}), card {j}", cost: 1 + depth);
                }
            }

            EmitTagPull(seed, target, paths, seen, prefix, ante, MotelyTag.UncommonTag, ante.Pulls.UncommonTagJokers, depth);
            EmitTagPull(seed, target, paths, seen, prefix, ante, MotelyTag.RareTag, ante.Pulls.RareTagJokers, depth);

            EmitActivated(seed, target, antes, budget, depth, paths, seen, prefix, ante,
                "Judgement", "use Judgement", ante.Pulls.JudgementJokers);
            EmitActivated(seed, target, antes, budget, depth, paths, seen, prefix, ante,
                "Wraith", "use Wraith", ante.Pulls.WraithJokers);
            EmitActivated(seed, target, antes, budget, depth, paths, seen, prefix, ante,
                "TheSoul", "use The Soul", ante.Pulls.LegendaryJokers);
            EmitActivated(seed, target, antes, budget, depth, paths, seen, prefix, ante,
                "TheEmperor", "use The Emperor", ante.Pulls.EmperorTarots);

            EmitActivated(seed, target, antes, budget, depth, paths, seen, prefix, ante,
                "RiffRaff", "select a blind with Riff-Raff", ante.Pulls.RiffRaffJokers);
            EmitActivated(seed, target, antes, budget, depth, paths, seen, prefix, ante,
                "SixthSense", "play a lone 6 with Sixth Sense", ante.Pulls.SixthSenseSpectrals);
            EmitActivated(seed, target, antes, budget, depth, paths, seen, prefix, ante,
                "Seance", "play a straight flush with Seance", ante.Pulls.SeanceSpectrals);

            for (int i = 0; i < ante.Pulls.PurpleSealTarots.Count; i++)
            {
                if (!SameType(ante.Pulls.PurpleSealTarots[i], target)) continue;
                Emit(seed, target, paths, seen, prefix, ante.Ante, "purple seal",
                    $"discard a purple-sealed card, pull {i} (needs a purple seal you already have)",
                    cost: 2 + i + depth);
            }
        }
    }

    private static void EmitTagPull(
        string seed, MotelyItem target, List<MotelyItemPath> paths, HashSet<string> seen,
        List<MotelySolverStep> prefix, MotelyJamlyzerAnteResult ante, MotelyTag tag,
        IReadOnlyList<MotelyItem> pulls, int depth)
    {
        bool small = ante.SmallBlindTag == tag, big = ante.BigBlindTag == tag;
        if (!small && !big) return;
        if (pulls.Count == 0 || !SameType(pulls[0], target)) return;
        string blind = small ? "small" : "big";
        Emit(seed, target, paths, seen, prefix, ante.Ante, $"{tag}",
            $"skip the {blind} blind for {tag}; the next shop gives it free", cost: 1 + depth);
    }

    private static void EmitActivated(
        string seed, MotelyItem target, List<MotelyJamlyzerAnteResult> antes, MotelySolverBudget budget,
        int depth, List<MotelyItemPath> paths, HashSet<string> seen, List<MotelySolverStep> prefix,
        MotelyJamlyzerAnteResult ante, string activatorName, string action, IReadOnlyList<MotelyItem> pulls)
    {
        int idx = -1;
        for (int i = 0; i < pulls.Count; i++)
            if (SameType(pulls[i], target)) { idx = i; break; }
        if (idx < 0) return;

        if (!MotelyItem.TryParse(activatorName, out var activator))
            return;
        if (activator.Type == target.Type) return;

        var earlier = antes.Where(a => a.Ante <= ante.Ante).ToList();
        var sub = new List<MotelyItemPath>();
        var subSeen = new HashSet<string>();
        SolveInto(seed, activator, earlier, budget, depth + 1, sub, subSeen, prefix: []);
        if (sub.Count == 0) return;

        var best = sub.OrderBy(p => p.Cost).First();
        var steps = new List<MotelySolverStep>(prefix);
        steps.AddRange(best.Steps);
        steps.Add(new MotelySolverStep(ante.Ante, action, $"pull {idx} is {target.Type}"));
        var key = string.Join("|", steps.Select(s => $"{s.Ante}:{s.Action}:{s.Detail}"));
        if (!seen.Add(key)) return;
        paths.Add(new MotelyItemPath(seed, target, steps, best.Cost + idx + 1));
    }

    private static void Emit(
        string seed, MotelyItem target, List<MotelyItemPath> paths, HashSet<string> seen,
        List<MotelySolverStep> prefix, int ante, string action, string detail, int cost)
    {
        var steps = new List<MotelySolverStep>(prefix) { new(ante, action, detail) };
        var key = string.Join("|", steps.Select(s => $"{s.Ante}:{s.Action}:{s.Detail}"));
        if (!seen.Add(key)) return;
        paths.Add(new MotelyItemPath(seed, target, steps, cost));
    }

    public static string Format(IReadOnlyList<MotelyItemPath> paths)
    {
        if (paths.Count == 0) return "no path within budget";
        var sb = new System.Text.StringBuilder();
        foreach (var p in paths)
        {
            sb.Append($"[{p.Cost,2}] ");
            sb.AppendLine(string.Join("  ->  ", p.Steps.Select(s => $"A{s.Ante} {s.Action}: {s.Detail}")));
        }
        return sb.ToString();
    }
}
