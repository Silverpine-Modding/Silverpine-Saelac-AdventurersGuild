using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace AdventurersGuild;

internal sealed class BountyMark : MonoBehaviour, INPCVisiblePrefixProvider
{
    internal string ContractId;
    internal static readonly List<BountyMark> Live = new();
    private void OnEnable() => Live.Add(this);
    private void OnDisable() => Live.Remove(this);
    public string GetPrefix() => "Wanted";
}

internal static class Bounties
{
    private static readonly (string Prefab, string Label, int HazardPay)[] Targets = {
        ("prefab_enemy_cultist","cultist",12), ("prefab_enemy_giantrat","giant rat",0),
        ("prefab_enemy_giantbat","giant bat",4), ("prefab_enemy_ooze_green","green ooze",2)
    };
    internal static string Choose() => Targets[UnityEngine.Random.Range(0,Targets.Length)].Prefab;
    internal static string Name(string prefab) => Targets.FirstOrDefault(t => t.Prefab == prefab).Label ?? "quarry";
    internal static int HazardPay(string prefab) => Targets.FirstOrDefault(t => t.Prefab == prefab).HazardPay;
    internal static BountyMark Find(string id) => BountyMark.Live.Find(m=>m.ContractId==id);
    internal static void ReleaseFinished(Charter c)
    {
        foreach (var mark in BountyMark.Live.ToArray())
        {
            var job=c.Jobs.Find(j=>j.Id==mark.ContractId);
            if (job?.Open==true && job.Stage!=Stage.Returning) continue;
            // Let the native death sequence finish and award its ordinary drops.
            if (mark.GetComponent<Damagable>().HP>0) UnityEngine.Object.Destroy(mark.gameObject);
        }
    }

    internal static void Place(Contract job)
    {
        if (!Guild.Ready || job.Stage is not (Stage.Active or Stage.Travelling) || !Guild.InRegion(job) || job.TargetPrefab == null ||
            job.Worker != Guild.PlayerId && !job.Named) return;
        if (Find(job.Id)) return;
        var prefab = SerializationManager.GetPrefabFromName(job.TargetPrefab);
        if (!prefab || !prefab.GetComponent<Enemy>()) return;
        var player = Player.Instance.transform.GetVector2IntPosition();
        var tile = new Vector2Int(job.X,job.Y);
        if (DungeonGenerationManager.Instance.GetPositionWorldTile(player)?.position != tile) return;
        var spots = Expeditions.Spaces(player,tile,9).Where(p => (p-player).sqrMagnitude >= 9 &&
            Turfs.IsValidTurf(p) && !Turfs.TryGetTurfComponent<Enemy>(p,out _)).ToList();
        if (spots.Count == 0) return;
        var cell = spots[UnityEngine.Random.Range(0,spots.Count)];
        var target = UnityEngine.Object.Instantiate(prefab,new Vector3(cell.x,cell.y,prefab.transform.position.z),prefab.transform.rotation);
        target.AddComponent<BountyMark>().ContractId = job.Id;
        target.GetComponent<TurfRegistrar>().Register();
    }
}

// Carry the assignment through the enemy's native component record. Old saves
// and ordinary enemies have no guild key. Other readers ignore unknown keys.
[HarmonyPatch]
internal static class BountyEnemySave
{
    private const string Key = "silverpine.adventurersguild.bounty";
    private static readonly AccessTools.FieldRef<VersionSafeStorage, Dictionary<string,object>> Fields =
        AccessTools.FieldRefAccess<VersionSafeStorage, Dictionary<string,object>>("storage");
    private static IEnumerable<MethodBase> TargetMethods() => new[] {
        AccessTools.Method(typeof(Enemy),nameof(Enemy.Serialize)),
        AccessTools.Method(typeof(Enemy),nameof(Enemy.Deserialize)) };
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        bool saving = __originalMethod.Name == nameof(Enemy.Serialize);
        var original = AccessTools.Method(typeof(VersionSafeStorage),__originalMethod.Name);
        var wrapper = AccessTools.Method(typeof(BountyEnemySave),saving ? nameof(Save) : nameof(Load));
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(original))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction);
                instruction.opcode = OpCodes.Call;
                instruction.operand = wrapper;
            }
            yield return instruction;
        }
    }
    private static void Save(VersionSafeStorage data, BinaryWriter writer, Enemy enemy)
    {
        if (enemy.TryGetComponent<BountyMark>(out var mark)) data.AddToStorage(Key,mark.ContractId);
        data.Serialize(writer);
    }
    private static void Load(VersionSafeStorage data, BinaryReader reader, Enemy enemy)
    {
        data.Deserialize(reader);
        if (!Fields(data).ContainsKey(Key)) return;
        var id = data.GetString(Key,"");
        if (!string.IsNullOrEmpty(id))
            (enemy.GetComponent<BountyMark>() ?? enemy.gameObject.AddComponent<BountyMark>()).ContractId = id;
    }
}
