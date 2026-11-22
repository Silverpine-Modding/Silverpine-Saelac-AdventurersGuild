using System;

namespace AdventurersGuild;

/// <summary>Immutable snapshot for optional player skill integrations.</summary>
public sealed class GuildQuest
{
    public string Id { get; }
    public Work Objective { get; }
    public GuildRank RequiredRank { get; }
    public string Issuer { get; }
    public string Beneficiary { get; }
    public string Supply { get; }
    public string Quarry { get; }
    public int Quantity { get; }
    public int Gold { get; }
    public int BonusGold { get; }
    public int Merit { get; }
    public GuildStanding Standing { get; }

    internal GuildQuest(Contract j,Charter charter)
    {
        Id=j.Id; Objective=j.Work; RequiredRank=GuildRanks.Requirement(j);
        Issuer=j.Issuer; Beneficiary=j.Beneficiary;
        Supply=j.Work==Work.Procurement ? j.Item : null;
        Quarry=j.TargetPrefab; Quantity=j.Quantity;
        Gold=j.Gold; BonusGold=j.SkillGold; Merit=GuildRanks.Merit(j); Standing=charter.Standing;
    }
}

public readonly struct GuildSkillBonus
{
    public int Gold { get; }
    public int Merit { get; }
    public GuildSkillBonus(int gold,int merit) { Gold=gold; Merit=merit; }
}

public static class GuildApi
{
    public static GuildStanding PlayerStanding => Guild.Ready ? GuildStore.Current?.Standing : null;
    public static event Func<GuildQuest,GuildSkillBonus> PlayerSigning;
    public static event Action<GuildQuest> PlayerAccepted;
    public static event Action<GuildQuest> PlayerCompleted;
    public static event Action<GuildQuest> PlayerPromoted;

    internal static void Sign(Contract j,Charter charter)
    {
        var providers=PlayerSigning;
        if (providers==null) return;
        var contract=new GuildQuest(j,charter);
        long gold=0, merit=0;
        foreach (Func<GuildQuest,GuildSkillBonus> provider in providers.GetInvocationList())
        {
            try
            {
                var bonus=provider(contract);
                gold+=Math.Max(0,bonus.Gold);
                merit+=Math.Max(0,bonus.Merit);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"Guild skill provider {provider.Method} failed: {ex.Message}"); }
        }
        j.SkillGold=(int)Math.Min(gold,Math.Min(int.MaxValue-j.Gold,Math.Min(j.Gold/2,Math.Max(0,charter.Treasury))));
        j.SkillMerit=(int)Math.Min(merit,contract.Merit);
        charter.Treasury-=j.SkillGold;
    }

    internal static void Accepted(Contract j,Charter charter) => Notify(PlayerAccepted,new GuildQuest(j,charter));
    internal static void Completed(Contract j,Charter charter,GuildRank previous)
    {
        var receipt=new GuildQuest(j,charter);
        Notify(PlayerCompleted,receipt);
        if (receipt.Standing.Rank>previous) Notify(PlayerPromoted,receipt);
    }

    private static void Notify(Action<GuildQuest> subscribers,GuildQuest receipt)
    {
        if (subscribers==null) return;
        foreach (Action<GuildQuest> handler in subscribers.GetInvocationList())
        {
            try { handler(receipt); }
            catch (Exception ex) { Plugin.Log.LogWarning($"Guild event handler {handler.Method} failed: {ex.Message}"); }
        }
    }
}
