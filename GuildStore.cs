using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Silverpine.ModdingTools;
using UnityEngine;

namespace AdventurersGuild;

public enum Work { Procurement, Courier, Survey, Bounty, Escort, Rescue }
public enum Stage { Offered, Active, Stranded, Escorting, Returning, Delivered, Closed, Cancelled, Failed, Travelling }
public enum Roster { Mixed, Named, Simulated }

public sealed class Contract
{
    public string Id = Guid.NewGuid().ToString("N"), Issuer, Beneficiary, Worker, Reserved, Item, Prize, PrizeName, Region;
    public string WorkerName, ClientName, RecipientName, Parent, Patient, LastReport;
    public string TargetPrefab;
    public string Applicant;
    public int ApplicationExpires;
    public Work Work;
    public Stage Stage;
    public int Gold, Quantity = 1, Progress, Created, Due, NextWork, X, Y, Remaining;
    public int NextGather, GatherInterval;
    public GuildRank? RequiredRank;
    public int SkillGold, SkillMerit;
    internal int Payout => Gold+SkillGold;
    public bool Reported, Paid, Trouble, ParcelCollected;
    public List<string> Events = new();
    public List<string> Cargo = new();
    public bool Open => Stage is Stage.Active or Stage.Returning or Stage.Stranded or Stage.Escorting or Stage.Travelling;
    public bool Named => Worker != null && (Worker.StartsWith("npc:") || Worker.StartsWith("custom:"));
    public bool Funded => Issuer == Guild.PlayerId;
    internal bool EscortInField => Work == Work.Escort && (Worker != Guild.PlayerId || Stage != Stage.Returning);
    internal string RequestDescription() => Work switch
    {
        Work.Procurement => $"I asked the guild to bring me {Quantity} × {Item}.",
        Work.Courier => $"I left a parcel with the guild for delivery to {RecipientName}.",
        Work.Escort when Issuer == Patient => $"I asked the guild for an escort from {Region} back to Silverpine.",
        _ => $"I posted “{Title}” through the guild."
    };
    internal string RequestAccount()
    {
        string claim = Worker == null
            ? (Stage == Stage.Offered ? "Nobody has taken the request yet." : "It closed without a claimant.")
            : WorkerName + " accepted the contract.";
        string outcome = Paid ? " The guild paid the reward." : Stage == Stage.Failed ? " The assignment failed." : "";
        return $"{RequestDescription()} The reward was {Payout} gold. {claim}{outcome}";
    }
    public string Title => Work switch
    {
        Work.Procurement => $"Supply {Quantity} × {Item}",
        Work.Courier => "Deliver a parcel to " + RecipientName,
        Work.Survey => "Survey " + Region,
        Work.Bounty => TargetPrefab == null ? "Defeat a hostile in " + Region : "Hunt the " + Bounties.Name(TargetPrefab),
        Work.Escort => "Escort " + RecipientName + " to Silverpine",
        _ => "Rescue " + RecipientName + " in " + Region
    };
}

public sealed class Charter
{
    public Dictionary<string, FieldPost> Posts = new();
    public string Representative;
    public Roster Roster;
    public HashSet<string> Members = new();
    // Replace the defaults on load so explicitly allowing Acacia survives a round trip.
    [JsonProperty(ObjectCreationHandling=ObjectCreationHandling.Replace,NullValueHandling=NullValueHandling.Ignore)]
    public HashSet<string> BlockedGivers = new() { "npc:Acacia" };
    [JsonProperty(ObjectCreationHandling=ObjectCreationHandling.Replace,NullValueHandling=NullValueHandling.Ignore)]
    public HashSet<string> BlockedTargets = new() { "npc:Acacia" };
    public List<Contract> Jobs = new();
    public int LastBoardDay = -1;
    public int NextDispatch, NextSimulatedClaim;
    public int Treasury = 300;
    // Rebuilt from paid contracts on load, including contracts signed before ranks existed.
    internal long PlayerMerit;
    internal int VeteranClears;
    internal GuildStanding Standing => new(PlayerMerit,VeteranClears);

    internal bool Permits(Contract j) => !BlockedGivers.Contains(j.Issuer) &&
        !BlockedTargets.Contains(j.Beneficiary) && !BlockedTargets.Contains(j.Patient);

    internal void WithdrawBlockedOffers()
    {
        foreach (var j in Jobs.Where(j => j.Stage == Stage.Offered && !Permits(j)))
        {
            j.Applicant = null;
            j.Stage = j.Funded ? Stage.Failed : Stage.Cancelled;
            if (!j.Funded) Treasury += j.Gold;
            j.LastReport = j.Funded ? "Withdrawn under the charter. Reclaim your deposit in the journal at the guild counter." :
                "The guild withdrew this notice under its charter.";
            j.Events.Add(j.LastReport);
        }
    }
}

public sealed class FieldPost
{
    public string Contract, Phase;
    public int HomeX, HomeY, TileX, TileY;
    public int NextRoute, LastRouteX, LastRouteY;
}

public sealed class GuildStore : MonoBehaviour, ISerializableMonoBehavior
{
    private const string Prefab = "adventurersguild_charter";
    private static GameObject template;
    private static SerializablePrefabRegistration registration;
    internal static GuildStore Instance;
    internal Charter Data = new();
    internal static Charter Current => Instance ? Instance.Data : null;

    internal static void Register()
    {
        if (template) return;
        // The registry is managed, but its template is a Unity object. A world
        // bootstrap can destroy the latter without disposing the registration.
        registration?.Unregister();
        template = new GameObject(Prefab);
        template.SetActive(false);
        template.AddComponent<TurfRegistrar>().preventRegistration = true;
        template.AddComponent<PersistentEntity>();
        template.AddComponent<GuildStore>();
        registration = SerializablePrefabs.Register(Plugin.Id, Prefab, template, new SerializablePrefabOptions {
            ExcludeTemplateFromSaves = true, ActivateLoadedInstances = true, ClearLoadedHideFlags = true,
            OnInstanceRestored = go => { Instance = go.GetComponent<GuildStore>(); go.transform.SetParent(WorldInfoManager.Instance.transform, false); }
        });
    }

    internal static Charter Create()
    {
        if (!Guild.Ready) throw new InvalidOperationException("Load a world first.");
        if (Instance) return Instance.Data;
        Register();
        var go = Instantiate(template);
        go.name = Prefab;
        go.hideFlags = HideFlags.None;
        go.transform.SetParent(WorldInfoManager.Instance.transform, false);
        Instance = go.GetComponent<GuildStore>();
        go.SetActive(true);
        return Instance.Data;
    }
    public void Serialize(BinaryWriter w) { w.Write(1); w.Write(JsonConvert.SerializeObject(Data)); }
    public void Deserialize(BinaryReader r)
    {
        if (r.ReadInt32() != 1) throw new InvalidDataException("Unknown guild save format.");
        Data = JsonConvert.DeserializeObject<Charter>(r.ReadString()) ?? throw new InvalidDataException("Missing charter.");
        GuildRanks.Restore(Data);
        Data.WithdrawBlockedOffers();
        var registrar = GetComponent<TurfRegistrar>();
        registrar.OnObjectPoolDestroy();
        registrar.preventRegistration = true;
        Instance = this;
    }
    private void OnDestroy() { if (ReferenceEquals(this, Instance)) Instance = null; }
}
