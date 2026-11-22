using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace AdventurersGuild.Checks;

internal static class TileReturnChecks
{
    internal static IEnumerator Exercise(Charter charter, NeuralNPC traveler, NeuralNPC rep)
    {
        CheckSceneryRestore();
        var map=DungeonGenerationManager.Instance;
        var tile=Guild.Regions.OrderBy(t=>t.position.sqrMagnitude).First().position;
        var key=Guild.Key(traveler);
        var clerk=Guild.Key(rep);
        var job=Guild.Offer(Work.Escort,map.worldTiles[tile],null,1,37,Guild.GuildId,key);
        job.Patient=key;
        charter.Jobs.Add(job);
        charter.NextDispatch=int.MaxValue;
        traveler.GetComponent<EntityMover>().Teleport(tile*100);
        Runner.Verify(Guild.Accept(job,Guild.PlayerId),"Player accepts an ordinary escort for a resident in the wilderness");
        map.EnterWorldTile(tile,tile*100);
        Player.Instance.Teleport(PathfindingEntity.FindFreeTurfsAroundPosition(traveler.transform.GetVector2IntPosition()).First());
        Expeditions.Interact(traveler);
        Runner.Verify(job.Stage==Stage.Escorting,"Traveler joins the player at the rendezvous");
        var wilderness=traveler.transform.GetVector2IntPosition();
        Player.Instance.Teleport(wilderness+Vector2Int.right*10);
        map.EnterWorldTile(Vector2Int.zero,new Vector2Int(12,1));
        Runner.Verify(job.Stage==Stage.Escorting && traveler.transform.GetVector2IntPosition()==wilderness,"Crossing alone leaves the escort open");
        map.EnterWorldTile(tile,wilderness);
        Player.Instance.Teleport(PathfindingEntity.FindFreeTurfsAroundPosition(traveler.transform.GetVector2IntPosition()).First());
        map.EnterWorldTile(Vector2Int.zero,new Vector2Int(12,1));
        var arrival=traveler.transform.GetVector2IntPosition();
        bool Released(NeuralNPC n) => !Expeditions.Assigned(n) &&
            n.GetComponent<NPCRoutineExecutor>().GetCurrentAndPreOverrideRoutines().All(r=>r?.id!=Expeditions.Prefix+job.Id);
        Runner.Verify(job.Stage==Stage.Returning && !job.Paid && Guild.Near(traveler) && Released(traveler),
            "The traveler starts their normal routine at the Silverpine arrival point before payout");
        Expeditions.PlaceAll(); Expeditions.Reset(); Expeditions.Tick();
        Runner.Verify(traveler.transform.GetVector2IntPosition()==arrival,"Field placement leaves the delivered traveler at their arrival square");
        var save=Path.Combine(Paths.GameRootPath,"escort-return.sav");
        SerializationManager.Save(save); SerializationManager.Load(save);
        yield return null; yield return null; yield return null;
        job=Guild.Find(job.Id); traveler=Guild.Actor(key); rep=Guild.Actor(clerk); map=DungeonGenerationManager.Instance;
        Runner.Verify(job.Stage==Stage.Returning && !job.Paid && Released(traveler) && traveler.transform.GetVector2IntPosition()==arrival,
            "An unclaimed escort reward survives reload with the traveler still released in town");
        map.EnterWorldTile(tile,tile*100);
        map.EnterWorldTile(Vector2Int.zero,new Vector2Int(12,1));
        Player.Instance.Teleport(PathfindingEntity.FindFreeTurfsAroundPosition(rep.transform.GetVector2IntPosition()).First());
        int gold=Guild.Bag.gold;
        Guild.PlayerAction(job,rep); Guild.PlayerAction(job,rep); Expeditions.PlaceAll();
        Runner.Verify(job.Paid && Guild.Bag.gold==gold+37 && Released(traveler) && traveler.transform.GetVector2IntPosition()==arrival,
            "Revisiting the pickup tile and collecting the fee once leave the traveler in town");
        for (int turn=0;turn<24 && traveler.transform.GetVector2IntPosition()==arrival;turn++) yield return BoardChecks.Turn();
        Runner.Verify(traveler.transform.GetVector2IntPosition()!=arrival && Released(traveler) &&
            map.GetPositionWorldTile(traveler.transform.GetVector2IntPosition()).position==Vector2Int.zero,
            "Native turns move the released traveler on their town schedule");

        var legacy=Guild.Offer(Work.Escort,map.worldTiles[tile],null,1,37,Guild.GuildId,key);
        legacy.Patient=key;
        GuildStore.Current.Jobs.Add(legacy);
        traveler.GetComponent<EntityMover>().Teleport(tile*100);
        Runner.Verify(Guild.Accept(legacy,Guild.PlayerId),"A second escort can use the released resident");
        Expeditions.PlaceAll();
        legacy.Stage=Stage.Returning; legacy.Progress=legacy.Quantity;
        SerializationManager.Save(save); SerializationManager.Load(save);
        yield return null; yield return null; yield return null;
        traveler=Guild.Actor(key); job=Guild.Find(legacy.Id);
        Runner.Verify(job.Stage==Stage.Returning && !job.Paid && Released(traveler) &&
            DungeonGenerationManager.Instance.GetPositionWorldTile(traveler.transform.GetVector2IntPosition()).position==Vector2Int.zero,
            "Loading an older unpaid escort with its stranded field routine restores the traveler to Silverpine");

        var hired=Guild.Offer(Work.Escort,DungeonGenerationManager.Instance.worldTiles[tile],null,1,37,Guild.GuildId,key);
        hired.Patient=key;
        GuildStore.Current.Jobs.Add(hired);
        traveler.GetComponent<EntityMover>().Teleport(tile*100);
        Runner.Verify(Guild.Accept(hired,"hire:Wren"),"Simulated escort signs for a wilderness traveler");
        Expeditions.PlaceAll(); Expeditions.CompleteField(hired);
        hired.Due=Guild.Now; Expeditions.Reset(); Expeditions.Tick();
        Runner.Verify(hired.Paid && !Expeditions.Assigned(traveler) &&
            DungeonGenerationManager.Instance.GetPositionWorldTile(traveler.transform.GetVector2IntPosition()).position==Vector2Int.zero,
            "An offscreen escort also delivers the resident to their Silverpine routine");
    }

    private static void CheckSceneryRestore()
    {
        var native=Traverse.Create(typeof(RandomSprite)).Field("forceDictionary").GetValue<Dictionary<Vector2Int,RandomSprite>>();
        // The starting town may have no naturally spawned, non-pooled propagating scenery.
        var prefabs=Traverse.Create(typeof(SerializationManager)).Field("prefabs").GetValue<Dictionary<string,GameObject>>();
        var scenery=prefabs.Values.First(p=>p && p.TryGetComponent<RandomSprite>(out var sprite) &&
            Traverse.Create(sprite).Field("forcePropagation").GetValue<bool>());
        var empty=Expeditions.Spaces(Player.Instance.transform.GetVector2IntPosition(),Vector2Int.zero,9).First(p=>!native.ContainsKey(p));
        var original=Object.Instantiate(scenery,new Vector3(empty.x,empty.y,0),Quaternion.identity).GetComponent<RandomSprite>();
        var cell=original.transform.GetVector2IntPosition();
        var record=SerializationManager.SerializeSerializable(new[]{original.gameObject});
        RandomSprite Restore() => SerializationManager.DeserializeSerializable(record).Single().GetComponent<RandomSprite>();
        var first=Restore(); var second=Restore();
        Runner.Verify(native[cell]==original && first && second,"Two restored scenery objects can share a coordinate with a live sprite");
        Object.DestroyImmediate(first.gameObject);
        Runner.Verify(native[cell]==original,"Removing an overlapping sprite keeps the original registered");
        original.gameObject.SetActive(false);
        Runner.Verify(native[cell]==second,"Disabling the original promotes the remaining live sprite");
        original.gameObject.SetActive(true);
        Object.DestroyImmediate(second.gameObject);
        Runner.Verify(native[cell]==original,"Reactivated scenery remains registered after the restored object unloads");
        Object.DestroyImmediate(original.gameObject);

        var prefab=SerializationManager.GetPrefabFromName("prefab_enemy_cultist");
        var ordinary=Object.Instantiate(prefab);
        var unmarked=SerializationManager.DeserializeSerializable(SerializationManager.SerializeSerializable(new[]{ordinary})).Single();
        Runner.Verify(!unmarked.GetComponent<BountyMark>(),"Ordinary enemies load from records without a guild bounty field");
        ordinary.AddComponent<BountyMark>().ContractId="saved-quarry";
        var wanted=SerializationManager.DeserializeSerializable(SerializationManager.SerializeSerializable(new[]{ordinary})).Single();
        Runner.Verify(wanted.GetComponent<BountyMark>()?.ContractId=="saved-quarry","An existing wanted enemy restores its exact contract ID");
        Object.DestroyImmediate(wanted); Object.DestroyImmediate(unmarked); Object.DestroyImmediate(ordinary);
    }
}
