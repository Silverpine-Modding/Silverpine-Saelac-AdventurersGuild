using System;

namespace AdventurersGuild;

public enum GuildRank { Beginner, Intermediate, Veteran, Hero }

public sealed class GuildStanding
{
    public long Merit { get; }
    public int VeteranClears { get; }
    public GuildRank Rank { get; }

    internal GuildStanding(long merit,int veteranClears)
    {
        Merit=merit;
        VeteranClears=veteranClears;
        Rank=merit>=50 && veteranClears>=3 ? GuildRank.Hero : merit>=24 ? GuildRank.Veteran :
            merit>=8 ? GuildRank.Intermediate : GuildRank.Beginner;
    }

    internal string Summary => Rank switch {
        GuildRank.Beginner => $"Beginner · {Merit}/8 merit toward Intermediate",
        GuildRank.Intermediate => $"Intermediate · {Merit}/24 merit toward Veteran",
        _ => $"{Rank} · {Merit} merit"
    };
}

internal static class GuildRanks
{
    internal static GuildRank Grade(Contract job,long crossings)
    {
        if (job.Work==Work.Procurement)
            return job.Quantity>=20 ? GuildRank.Veteran : job.Quantity>=8 ? GuildRank.Intermediate : GuildRank.Beginner;
        // Treat a dangerous quarry or a passenger as extra travel risk.
        int hazard=job.Work switch {
            Work.Bounty when Bounties.HazardPay(job.TargetPrefab)>=10 => 3,
            Work.Escort or Work.Rescue => 2,
            _ => 0
        };
        long difficulty=crossings+hazard;
        return difficulty>=7 ? GuildRank.Veteran : difficulty>=4 || hazard==3 ? GuildRank.Intermediate : GuildRank.Beginner;
    }

    internal static GuildRank Requirement(Contract j)
    {
        // Older guild-priced parcels saved their distance in the quoted fee, not in X/Y.
        long crossings=j.Work==Work.Courier ? (j.Funded ? 0 : Math.Max(0,(j.Gold-16L)/5)) :
            Math.Abs((long)j.X)+Math.Abs((long)j.Y);
        return j.RequiredRank ?? Grade(j,crossings);
    }

    internal static int Merit(Contract j) => (Requirement(j) switch {
        GuildRank.Beginner => 2, GuildRank.Intermediate => 4, _ => 7
    }) + j.SkillMerit;

    internal static void Credit(Charter charter,Contract j)
    {
        if (j.Worker!=Guild.PlayerId || j.Funded || !j.Paid) return;
        charter.PlayerMerit+=Merit(j);
        if (Requirement(j)>=GuildRank.Veteran) charter.VeteranClears++;
    }

    internal static void Restore(Charter charter)
    {
        charter.PlayerMerit=0;
        charter.VeteranClears=0;
        foreach (var job in charter.Jobs)
        {
            job.RequiredRank ??= Requirement(job);
            Credit(charter,job);
        }
    }
}
