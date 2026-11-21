using System;
using System.Collections.Generic;
using System.Linq;

namespace AdventurersGuild;

internal static class GuildLedger
{
    internal const string DeskHeading = "From the guild ledger";

    internal static string Brief(NeuralNPC representative, Charter charter)
    {
        var topics = representative.dialogElements.AsEnumerable().Reverse()
            .TakeWhile(e => e.speakerType != SpeakerType.NewDialogMarker)
            .Where(e => e.speakerType == SpeakerType.Player ||
                e.speakerType == SpeakerType.System && e.contents.StartsWith(DeskHeading, StringComparison.Ordinal))
            .Take(3).ToArray();
        var records = charter.Jobs.AsEnumerable().Reverse()
            .Select(j => (Job: j, Topic: TopicRank(topics, j)))
            .OrderBy(r => r.Topic)
            .ThenByDescending(r => r.Job.Open && r.Job.Worker == Guild.PlayerId)
            .ThenByDescending(r => r.Job.Open || r.Job.Work == Work.Rescue && r.Job.Stage == Stage.Offered)
            .ThenByDescending(r => r.Job.Stage == Stage.Offered)
            .Take(6).Select(r => r.Job);
        return "You are the Adventurers’ Guild representative. You know the people and assignments described in the guild ledger through your work, even if you have never met them. Answer guild questions from these records.\n" +
            Describe(charter, records);
    }

    private static string Heading(Contract j) => $"“{j.Title}”. Posted by {j.ClientName} for {j.RecipientName}.";

    private static int TopicRank(NeuralNPC.DialogElement[] topics, Contract j)
    {
        for (int i = 0; i < topics.Length; i++)
        {
            var topic = topics[i];
            if (topic.speakerType == SpeakerType.System)
            {
                if (topic.contents.Contains(Heading(j))) return 2 * i;
            }
            else
            {
                if (Mentions(topic.contents, j)) return 2 * i;
                if (ContainsTerm(topic.contents, GuildWindow.WorkName(j.Work))) return 2 * i + 1;
            }
        }
        return int.MaxValue;
    }

    private static bool Mentions(string text, Contract j) =>
        ContainsTerm(text, j.Title) ||
        j.Work == Work.Procurement && ContainsTerm(text, j.Item) ||
        j.Work == Work.Bounty && j.TargetPrefab != null && ContainsTerm(text, Bounties.Name(j.TargetPrefab)) ||
        NamesPerson(j.Beneficiary, j.RecipientName) && ContainsTerm(text, j.RecipientName) ||
        NamesPerson(j.Issuer, j.ClientName) && ContainsTerm(text, j.ClientName) ||
        NamesPerson(j.Worker, j.WorkerName) && ContainsTerm(text, j.WorkerName);

    private static bool NamesPerson(string key, string name) =>
        !string.IsNullOrEmpty(key) && key != Guild.PlayerId && key != Guild.GuildId && !string.IsNullOrWhiteSpace(name);

    private static bool ContainsTerm(string text, string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return false;
        for (int start = 0; start < text.Length;)
        {
            int index = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return false;
            int end = index + term.Length;
            if ((index == 0 || !char.IsLetterOrDigit(text[index - 1])) &&
                (end == text.Length || !char.IsLetterOrDigit(text[end]))) return true;
            start = index + 1;
        }
        return false;
    }

    internal static string Describe(Charter charter, IEnumerable<Contract> jobs)
    {
        var lines = new List<string>();
        var people = new HashSet<string>();
        foreach (var j in jobs)
        {
            string reward = j.Gold + " gold" + (string.IsNullOrEmpty(j.PrizeName) ? "" : " and " + j.PrizeName);
            string claim = j.Worker == null ? "No claimant." : "Accepted by " + j.WorkerName + ".";
            string status = j.Stage switch {
                Stage.Offered when j.Reserved != null => "Reserved for " + (Guild.Actor(j.Reserved)?.GetFinalName() ?? j.Reserved.Substring(j.Reserved.IndexOf(':') + 1)) + ".",
                Stage.Offered => "Open for claims.",
                Stage.Travelling => j.WorkerName + " signed at the guild counter and is walking to " + j.Region + ". Fieldwork begins on arrival.",
                Stage.Stranded => "The assigned adventurer missed their check-in.",
                Stage.Escorting => j.WorkerName + " found " + j.RecipientName + " and is escorting them back to Silverpine.",
                Stage.Returning => "Fieldwork finished. The reward awaits settlement.",
                Stage.Delivered => "Reward settled. Goods await collection.",
                Stage.Closed => "Completed and settled.",
                Stage.Cancelled => "Withdrawn.",
                Stage.Failed => "Failed.",
                _ => "Underway."
            };
            lines.Add($"{Heading(j)} {Objective(charter, j)} {claim} {status} Reward: {reward}.");
            if (!string.IsNullOrWhiteSpace(j.LastReport)) lines.Add("Last filed report: " + j.LastReport);
            foreach (var key in new[] { j.Beneficiary, j.Issuer, j.Worker })
            {
                if (key == null || !(key.StartsWith("npc:") || key.StartsWith("custom:")) || !people.Add(key)) continue;
                var resident = Guild.Actor(key);
                if (!resident) continue;
                var details = string.Join(", ", new[] { resident.species, resident.occupation }.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (details.Length > 0) lines.Add($"{resident.GetFinalName()} is listed as {details}.");
            }
        }
        return string.Join("\n", lines);
    }

    private static string Objective(Charter charter, Contract j)
    {
        switch (j.Work)
        {
            case Work.Rescue:
                var original = j.Parent == null ? null : charter.Jobs.Find(p => p.Id == j.Parent);
                string assignment = original == null ? "" : $" while working on “{original.Title}”";
                return $"{j.RecipientName} is the guild adventurer who missed a check-in from {j.Region}{assignment}. The rescue calls for finding them there and bringing them back to Silverpine.";
            case Work.Escort:
                return $"{j.RecipientName} is the traveler named in the notice. The agreed rendezvous is {j.Region} for an escort back to Silverpine.";
            case Work.Courier:
                string pickup = j.Named && j.Stage == Stage.Active ? (j.ParcelCollected ? " The courier has collected it." : " The courier is due to collect it.") : "";
                return $"{j.RecipientName} is the intended recipient of the parcel left at the guild for delivery.{pickup}";
            case Work.Procurement:
                return $"The representative takes delivery of {j.Quantity} × {j.Item} from any source. The suggested gathering area is {GuildWindow.WorkerArea(j)}.";
            case Work.Bounty:
                string target = j.TargetPrefab == null ? "a hostile" : "the wanted " + Bounties.Name(j.TargetPrefab);
                return $"The bounty is for defeating {target} in {j.Region}.";
            default:
                return $"The survey calls for a visit to {j.Region} and a report to the representative.";
        }
    }
}
