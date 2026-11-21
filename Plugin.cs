using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Silverpine.ModdingTools;
using UnityEngine;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("AdventurersGuild.Checks")]
namespace AdventurersGuild;

[BepInPlugin(Id,"Adventurers’ Guild","1.5.2")]
[BepInDependency("Saelac.Silverpine.ModdingTools","1.13.0")]
[BepInDependency(PartyBridge.Id,BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "silverpine.adventurersguild";
    internal static ManualLogSource Log;
    private void Awake()
    {
        Log = Logger;
        GuildStore.Register();
        PartyBridge.Bind();
        InventoryModTools.RegisterSession(Id + ".desk","Adventurers’ Guild",(_,s) => GuildWindow.Open(s), order:151);
        Register("business","Guild business",Guild.AtCounter);
        Register("field","Guild assignment",HasFieldBusiness);
        DialoguePromptTransforms.Register(Id,new DialoguePromptTransformDefinition {
            Id = Id + ".briefing", TransformText = (c,section,text) => section == DialoguePromptTextSection.Environment ? Brief(c.Npc,text) : text
        });
        SerializationManager.On2FramesAfterFinishedLoading += () => Expeditions.PlaceAll();
        new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("Adventurers’ Guild ready. Companion Party expedition support: " + (PartyBridge.Pay != null));
    }

    private static void Register(string id,string label,Func<NeuralNPC,bool> visible) =>
        DialogueActions.Register(Id,new DialogueActionDefinition { Id = Id+"."+id, Label = label, Order = 152,
            RequireNpc = true, IsVisible = c => Guild.Ready && c.Npc && visible(c.Npc),
            OnSelected = c => { if (Guild.Ready && c.Npc && visible(c.Npc)) GuildWindow.Visit(c.Npc,conversation:c.DialogBox); } });

    internal static bool HasFieldBusiness(NeuralNPC n)
    {
        string key = Guild.Key(n);
        return Expeditions.Assigned(n) || GuildStore.Current?.Jobs.Any(j => j.Issuer == key ||
            j.Worker == Guild.PlayerId && j.Stage == Stage.Active && j.Work == Work.Courier && j.Beneficiary == key) == true;
    }

    internal static IEnumerable<Contract> RequestsBy(Charter c,string key) => c.Jobs.Where(j => j.Issuer == key).Reverse();

    internal static string Brief(NeuralNPC n,string text)
    {
        var c = GuildStore.Current;
        if (!n || c == null) return text;
        var k = Guild.Key(n);
        var lines = c.Jobs.AsEnumerable().Reverse().Where(j => j.Issuer != k && j.Stage != Stage.Offered &&
            (j.Worker == k || j.Patient == k || j.Beneficiary == k && (j.Paid || j.Work == Work.Courier && j.Progress > 0))).Take(4)
            .Select(j => $"{j.Title}. Commissioned by {j.ClientName} for {j.RecipientName}. Assigned to {j.WorkerName} for {j.Gold} gold. " +
                (j.Worker == k ? $"Assignment: {j.Stage}. Progress {j.Progress}/{j.Quantity}. " + string.Join(" ",j.Events.Skip(Math.Max(0,j.Events.Count-3))) :
                j.Patient == k ? "My status: " + j.Stage : j.LastReport));
        var requests = RequestsBy(c,k);
        var recent = requests.Where(j => !j.Open && j.Stage != Stage.Offered).Take(2);
        var known = requests.Where(j => j.Open || j.Stage == Stage.Offered).Concat(recent);
        var add = string.Join("\n",known.Select(j => j.RequestAccount()).Concat(lines));
        var application = c.Jobs.Find(j => j.Stage == Stage.Offered && j.Applicant == k);
        if (application != null) add += "\nI am walking to the guild representative to ask for “" + application.Title + "”. The notice is still open.";
        if (k == c.Representative) add = GuildLedger.Brief(n,c) + "\n" + add;
        return add.Length == 0 ? text : text + "\n" + add;
    }
}
[HarmonyPatch(typeof(NeuralNPC),nameof(NeuralNPC.OnInteract))]
internal static class OfflineCounter
{
    static bool Prefix(NeuralNPC __instance, GameObject sender)
    {
        if (!Guild.Ready || sender != Player.Instance.gameObject || InferenceServerSetupHandler.Instance.modelLoaded ||
            Player.Instance.inCombat || SaveUI.Instance.IsSavingBlocked() || !Guild.Near(__instance)) return true;
        bool participant = Guild.AtCounter(__instance) || Plugin.HasFieldBusiness(__instance);
        return !participant || !GuildWindow.Visit(__instance);
    }
}
[HarmonyPatch(typeof(WorldInfoManager),"OnPostProcessTurn")]
internal static class GuildClock
{
    static void Postfix(WorldInfoManager __instance)
    {
        if (__instance == WorldInfoManager.Instance) Expeditions.Tick();
    }
}
[HarmonyPatch(typeof(MainMenuUI),"Start")]
internal static class MenuStart
{
    static void Postfix() { GuildStore.Register(); Expeditions.Reset(); }
}
[HarmonyPatch(typeof(SerializationManager),nameof(SerializationManager.Load))]
internal static class LoadStart
{
    static void Prefix() { GuildWindow.CloseActive(); GuildStore.Register(); GuildStore.Instance = null; Expeditions.Reset(); }
}
[HarmonyPatch(typeof(DungeonGenerationManager),nameof(DungeonGenerationManager.EnterWorldTile))]
internal static class TileArrival
{
    static void Prefix(out System.Collections.Generic.HashSet<string> __state) => __state = Expeditions.TravellingEscorts();
    static void Postfix(System.Collections.Generic.HashSet<string> __state) => Expeditions.PlaceAll(true,__state);
}

// Guild jobs own movement while this exact override is on top. A jail, meeting,
// or other override above it continues through the game's normal executor.
[HarmonyPatch(typeof(NPCRoutineExecutor),"OnPostProcessTurn")]
internal static class FieldRoutine
{
    static bool Prefix(NPCRoutineExecutor __instance) => !Expeditions.Owned(__instance) ||
        GuildStore.Current == null || !Expeditions.Assigned(__instance.neuralNPC);
}

[HarmonyPatch(typeof(Sign),nameof(Sign.OnInteract))]
internal static class GuildSign
{
    static bool Prefix(Sign __instance,GameObject sender,string ___message)
    {
        GuildSignAppearance.Refresh(__instance, ___message);
        if (!Guild.Ready || sender != Player.Instance.gameObject ||
            !GuildSignAppearance.IsMarker(___message) ||
            Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return true;
        return !GuildWindow.Visit(null,true);
    }
}

// TakeDamage enqueues an iterator; capture the caller at construction, then
// scope kill credit to the exact iterator step which actually applies damage.
[HarmonyPatch(typeof(Damagable),nameof(Damagable.TakeDamage))]
internal static class DamageSource
{
    [ThreadStatic] internal static GameObject Caller;
    static void Prefix(GameObject caller,out GameObject __state) { __state = Caller; Caller = caller; }
    static void Finalizer(GameObject __state) { Caller = __state; }
}
[HarmonyPatch(typeof(Damagable),"AnimateDamage")]
internal static class DamageStep
{
    [ThreadStatic] internal static GameObject Dealing;
    static void Postfix(Damagable __instance,ref IEnumerator __result)
    {
        var caller = DamageSource.Caller;
        if (caller && __result != null && GuildStore.Current != null && __instance.GetComponent<Enemy>())
            __result = Observe(__result,caller);
    }
    private static IEnumerator Observe(IEnumerator steps,GameObject caller)
    {
        try
        {
            while (true)
            {
                var previous = Dealing;
                object next;
                try { Dealing = caller; if (!steps.MoveNext()) break; next = steps.Current; }
                finally { Dealing = previous; }
                yield return next;
            }
        }
        finally { (steps as IDisposable)?.Dispose(); }
    }
}
[HarmonyPatch(typeof(Damagable),nameof(Damagable.Kill))]
internal static class BountyDeath
{
    static void Postfix(Damagable __instance)
    {
        var source = DamageStep.Dealing;
        if (!source || !__instance.TryGetComponent<Enemy>(out var e) || Traverse.Create(e).Field("passive").GetValue<bool>()) return;
        Expeditions.Kill(source.GetComponent<NeuralNPC>(),source == Player.Instance.gameObject,e);
    }
}

// The native harvest deposits the fully rolled item synchronously into the NPC's pack.
// Observe that deposit regardless of whether the guild routine or NPC interaction initiated it.
[HarmonyPatch(typeof(ResourceNode),nameof(ResourceNode.OnNPCInteract))]
internal static class GuildHarvest
{
    private static void Prefix(ResourceNode __instance, NeuralNPC neuralNPC,string ___itemName,
        out (Contract Job, Inventory Bag, HashSet<Item> Before) __state)
    {
        __state=default;
        var c=GuildStore.Current;
        if (c==null || !Guild.Ready || !neuralNPC) return;
        var key=Guild.Key(neuralNPC);
        var job=c.Jobs.Find(j=>j.Stage==Stage.Active && j.Work==Work.Procurement && j.Worker==key && j.Item==___itemName && Guild.InRegion(j));
        if (job==null || DungeonGenerationManager.Instance.GetPositionWorldTile(__instance.transform.GetVector2IntPosition())?.position!=new Vector2Int(job.X,job.Y)) return;
        var bag=neuralNPC.GetComponent<WorldInventory>().inventory;
        __state=(job,bag,new HashSet<Item>(bag.items));
    }
    private static void Postfix((Contract Job, Inventory Bag, HashSet<Item> Before) __state)
    {
        var (job,bag,before)=__state;
        if (job==null || job.Stage!=Stage.Active) return;
        foreach (var item in bag.items.Where(i=>i.name==job.Item && !before.Contains(i)).Take(job.Quantity-job.Progress).ToArray())
        {
            job.Cargo.Add(Guild.Pack(item));
            bag.RemoveItem(item); job.Progress++;
        }
        if (job.Progress>=job.Quantity) Expeditions.CompleteField(job);
    }
}
