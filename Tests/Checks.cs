using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AdventurersGuild.Checks;
[BepInPlugin("silverpine.guild.checks","Guild checks","1.0.0")]
[BepInDependency(Plugin.Id)]
public sealed class Checks : BaseUnityPlugin
{
    private void Awake()
    {
        if (!Application.isBatchMode || !File.Exists(Path.Combine(Paths.GameRootPath,"guild-check-only"))) return;
        if (!string.Equals(Path.GetFullPath(Application.persistentDataPath), Path.Combine(Paths.GameRootPath,"TestData"),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable persistent data directory required.");
        new Harmony("silverpine.guild.test-isolation").Patch(AccessTools.Method(typeof(InferenceServerSetupHandler),"SetUp"),
            prefix:new HarmonyMethod(typeof(Checks),nameof(SkipInference)));
        SceneManager.sceneLoaded += Begin;
    }
    private static bool SkipInference() => false;
    private void Begin(Scene s,LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= Begin;
        new GameObject("Guild integration runner").AddComponent<Runner>().StartCoroutine(Runner.Run());
    }
}
public sealed class Runner : MonoBehaviour
{
    private static readonly List<string> Results = new();
    private static void Check(bool pass,string label)
    {
        if (!pass) throw new InvalidOperationException(label);
        Results.Add(label);
    }
    public static IEnumerator Run()
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(Environment.GetCommandLineArgs().Contains("-guild-lifecycle") ? Lifecycle.Exercise() : Exercise());
        while (stack.Count > 0)
        {
            object next;
            try
            {
                var steps = stack.Peek();
                if (!steps.MoveNext()) { stack.Pop(); continue; }
                next = steps.Current;
            }
            catch(Exception ex) { Results.Add(ex.ToString()); break; }
            if (next is IEnumerator nested) stack.Push(nested); else yield return next;
        }
        if (stack.Count == 0) Results.Add("PASS");
        var file = Path.Combine(Paths.GameRootPath,"guild-results");
        File.WriteAllLines(file+".tmp",Results);
        File.Move(file+".tmp",file);
    }
    internal static void Verify(bool pass,string label) => Check(pass,label);
    private static IEnumerator Exercise()
    {
        float end = Time.realtimeSinceStartup + 35;
        while ((!Player.Instance || !MainMenuUI.Instance || !WorldInfoManager.Instance || !ActionQueue.Instance || !DungeonGenerationManager.Instance ||
            NeuralNPC.neuralNPCs.Count < 3) && Time.realtimeSinceStartup < end) yield return null;
        yield return null; yield return null;
        MainMenuUI.Instance.Close();
        Player.Instance.playerName = "Guild Tester";
        Check(Guild.Ready,"Fresh scene is playable");
        var c = GuildStore.Create();
        if (Environment.GetCommandLineArgs().Contains("-guild-structure-import"))
        {
            yield return StructureSignChecks.Exercise();
            yield break;
        }
        var members = Guild.People.Where(Expeditions.Free).ToArray();
        Check(members.Length >= 2,"Available named residents");
        var worker = members.First(n => n.npcName == NPCName.Darian); var rep = members.First(n => n != worker);
        c.Representative = Guild.Key(rep); c.Members.Add(Guild.Key(worker));
        Guild.Stock(c);
        Check(c.Jobs.Select(j=>j.Work).Distinct().Count() == 5,"Board stocks five types of contract");
        if (Environment.GetCommandLineArgs().Contains("-guild-ranks"))
        {
            yield return RankChecks.Exercise(c,rep,worker);
            yield break;
        }
        if (Environment.GetCommandLineArgs().Contains("-role-exclusions"))
        {
            yield return QuestRoleChecks.Exercise(c,worker);
            yield break;
        }
        if (Environment.GetCommandLineArgs().Contains("-guild-escort-return"))
        {
            yield return TileReturnChecks.Exercise(c,worker,rep);
            yield break;
        }
        if (Environment.GetCommandLineArgs().Contains("-guild-conversation"))
        {
            yield return ConversationChecks.Exercise(c,rep,worker);
            yield break;
        }
        if (Environment.GetCommandLineArgs().Contains("-guild-board"))
        {
            yield return BoardChecks.Exercise(c,rep);
            yield break;
        }
        yield return SignChecks.Exercise();
        var window = UnityEngine.Object.FindObjectOfType<GuildWindow>();
        Check(window && !PauseMenuManager.Instance.open && Player.Instance.IsInputBlocked(),"Physical guild desk owns input without opening inventory");
        GuildWindow.CloseActive();
        string supply = Guild.RegionsFor(Work.Procurement,"Forest Poppy").Any() ? "Forest Poppy" : "Juniper Berries";
        var tile = Guild.RegionsFor(Work.Procurement,supply).OrderBy(t=>t.position.sqrMagnitude).First();
        var wallet = Guild.Bag;
        wallet.gold=200;
        var gift = ItemLibrary.GetItemByName("Lavender",true);
        Check(wallet.TryAddItem(gift),"Add physical bonus to player pack");
        Guild.Post(Work.Procurement,tile,supply,3,120,Guild.PlayerId,Guild.Key(worker),gift);
        var job = c.Jobs.Last();
        Check(job.Funded && wallet.gold == 80 && !wallet.ContainsItem(gift),"Posting debits exact gold and bonus");
        Check(!GuildWindow.Instructions(job).Contains($"({job.X}, {job.Y})"),"Reserving an herb search keeps its coordinates private");
        rep=Guild.People.First(n=>n.npcName==NPCName.Aldric);
        c.Representative=Guild.Key(rep);
        var bystander=Guild.People.First(n=>n.npcName==NPCName.Rosalyn);
        PutToBed(bystander,NPCRoutineLibrary.GetNPCRoutine(22,bystander.npcName));
        PutToBed(rep,NPCRoutineLibrary.GetNPCRoutine(22,rep.npcName));
        PutToBed(worker,NPCRoutineLibrary.GetNPCRoutine(8,worker.npcName));
        if (PartyBridge.Pay != null) NapHere(worker);
        Check(worker.sleeping && rep.sleeping && bystander.sleeping && Expeditions.Free(worker),"Darian can apply from his native bedtime routine while the representative is also asleep");
        job.Created=Guild.Now-500; c.NextDispatch=0;
        Expeditions.Reset(); Expeditions.Tick();
        Check(!worker.sleeping && Expeditions.Owned(worker.GetComponent<NPCRoutineExecutor>()),"Dispatch wakes the sleeping applicant through the native routine change");
        Check(job.Applicant==Guild.Key(worker) && job.Worker==null && !Guild.Accept(job,Guild.Key(worker)) &&
            !GuildWindow.Instructions(job).Contains($"({job.X}, {job.Y})"),"An approaching resident cannot sign remotely or reveal the search tile");
        for(int turn=0;turn<90 && job.Worker==null;turn++) yield return BoardChecks.Turn();
        Check(job.Worker==Guild.Key(worker) && job.Stage==Stage.Travelling && !Guild.Accept(job,Guild.Key(worker)) &&
            (worker.transform.GetVector2IntPosition()-rep.transform.GetVector2IntPosition()).sqrMagnitude<=1 && !rep.sleeping && bystander.sleeping,
            $"Representative wakes to sign in person while an uninvolved resident sleeps (applicant {worker.transform.GetVector2IntPosition()}, representative {rep.transform.GetVector2IntPosition()}, stage {job.Stage})");
        Check(GuildWindow.Instructions(job).Contains(job.Region),"Named adventurer reveals the precise gathering tile on acceptance");
        job.Trouble=false;
        Expeditions.PlaceAll();
        var executor=worker.GetComponent<NPCRoutineExecutor>();
        var travelRoutine=executor.currentRoutine;
        var depth=executor.preOverrideRoutines.Count;
        Check(Expeditions.Owned(executor) && c.Posts.ContainsKey(Guild.Key(worker)),"Named resident starts guild override");
        NapHere(worker);
        Check(worker.sleeping,"Native bed interaction can interrupt an active guild trip");
        Expeditions.PlaceAll();
        Check(!worker.sleeping && executor.currentRoutine==travelRoutine && executor.preOverrideRoutines.Count==depth,
            "A nap during travel resumes the same route and preserves the original schedule");
        var map = DungeonGenerationManager.Instance;
        var tripSave=Path.Combine(Paths.GameRootPath,"guild-walk.sav");
        string tripId=job.Id;
        var depart=worker.transform.GetVector2IntPosition();
        for(int t=0;t<6;t++) yield return BoardChecks.Turn();
        Check(job.Stage==Stage.Travelling && job.Progress==0 && job.Cargo.Count==0 && job.NextGather==0 && worker.transform.GetVector2IntPosition()!=depart,
            "Native walking moves the resident without awarding any progress on the way");
        SerializationManager.Save(tripSave); SerializationManager.Load(tripSave);
        yield return null; yield return null; yield return null;
        c=GuildStore.Current; job=Guild.Find(tripId); worker=Guild.Actor(job.Worker); rep=Guild.Actor(c.Representative);
        map=DungeonGenerationManager.Instance; wallet=Guild.Bag;
        tile=map.worldTiles[new Vector2Int(job.X,job.Y)];
        Check(job.Stage==Stage.Travelling && job.NextGather==0,"Reload resumes the journey before starting a harvest clock");
        for(int turn=0;turn<380 && job.Stage==Stage.Travelling;turn++) yield return BoardChecks.Turn();
        Check(job.Stage==Stage.Active && map.GetPositionWorldTile(worker.transform.GetVector2IntPosition())==tile && !Guild.InRegion(job),
            "Resident physically reaches an unvisited wilderness tile before starting work");
        Check(!rep.GetComponent<NPCRoutineExecutor>().GetCurrentAndPreOverrideRoutines().Any(r=>r.activity=="answering a guild visitor"),
            "Representative resumes their own schedule after the visit across save/load");
        Check(job.NextGather>Guild.Now && job.Progress==0,"Offscreen gathering starts with an empty order and a future harvest time");
        AccessTools.PropertySetter(typeof(WorldInfoManager),"TotalTurnCount").Invoke(WorldInfoManager.Instance,new object[]{job.NextGather});
        Expeditions.Reset(); Expeditions.Tick();
        Check(job.Progress==1 && job.Cargo.Count==1 && job.Stage==Stage.Active,"First offscreen harvest supplies one item before arrival");
        map.EnterWorldTile(tile.position,tile.position*100);
        Check(Guild.InRegion(job) && Expeditions.CanFight(worker) && (worker.transform.position-Player.Instance.transform.position).sqrMagnitude < 120,"Real resident placed on generated wilderness tile");
        Check(Plugin.Brief(worker,"").Contains(job.ClientName),"Worker receives client and assignment knowledge");
        var workerPos=worker.transform.GetVector2IntPosition();
        var spot=PathfindingEntity.FindFreeTurfsAroundPosition(workerPos).First();
        var herb = supply == "Forest Poppy" ? "prefab_herb_forestpoppy" : "prefab_herb_juniper";
        UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/"+herb),new Vector3(spot.x,spot.y,0),Quaternion.identity);
        job.NextWork=Guild.Now;
        Expeditions.Reset(); Expeditions.Tick();
        Check(job.Progress==2 && job.Cargo.Count==2,"Physical harvesting adds one real item to the same partially filled order");
        if (PartyBridge.Pay != null)
        {
            var enemy=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/prefab_enemy_ooze_green"),new Vector3(spot.x,spot.y,0),Quaternion.identity);
            var queue=Traverse.Create(ActionQueue.Instance).Field("queueActions").GetValue<List<QueueAction>>();
            var existing=queue.Count;
            AccessTools.Method(typeof(PathfindingEntity),"OnPreProcessTurn").Invoke(worker.GetComponent<PathfindingEntity>(),null);
            Check(queue.Skip(existing).Any(q=>q.queuer==worker.gameObject && q.longAction!=null),"Unrecruited guild adventurer attacks a real adjacent hostile through Companion Party");
            queue.RemoveAll(q=>q.queuer==worker.gameObject);
            UnityEngine.Object.DestroyImmediate(enemy);
        }
        job.Due = Guild.Now; job.Trouble = true;
        int savedBalance=c.Treasury; c.Treasury=1;
        Expeditions.Reset(); Expeditions.Tick();
        var rescue = c.Jobs.Single(j=>j.Parent==job.Id);
        Check(rescue.Gold==36+7*(Math.Abs(job.X)+Math.Abs(job.Y)) && c.Treasury==1-rescue.Gold,
            "A low treasury reserves the full distance-based rescue fee as an advance");
        c.Treasury+=savedBalance-1;
        Check(job.Stage == Stage.Stranded && rescue.Patient == Guild.Key(worker),"Linked rescue after distress");
        string path = Path.Combine(Paths.GameRootPath,"guild-test.sav");
        string parentId=job.Id, rescueId=rescue.Id, workerKey=Guild.Key(worker);
        map.EnterWorldTile(Vector2Int.zero,new Vector2Int(12,1));
        AccessTools.Method(typeof(DungeonGenerationManager),"OnNewDay").Invoke(map,new object[]{2});
        SerializationManager.Save(path);
        SerializationManager.Load(path);
        yield return null; yield return null; yield return null;
        c=GuildStore.Current; job=Guild.Find(parentId); rescue=Guild.Find(rescueId);
        Check(job?.Stage==Stage.Stranded && Guild.Bag.gold==80 && rescue?.Patient==workerKey,"Embedded charter, escrow and rescue survive native save/load");
        var restoredBrief=Plugin.Brief(Guild.Actor(c.Representative),"");
        Check(restoredBrief.Contains(rescue.RecipientName+" is the guild adventurer who missed a check-in from "+rescue.Region) &&
            restoredBrief.Contains(job.Title),"Representative reconstructs the rescue subject and original assignment from the native save");
        Check(GuildWindow.Instructions(job).Contains(job.Region),"Named gathering tile remains visible after save/load");
        tile=DungeonGenerationManager.Instance.worldTiles[new Vector2Int(job.X,job.Y)];
        DungeonGenerationManager.Instance.EnterWorldTile(tile.position,tile.position*100);
        worker=Guild.Actor(workerKey);
        Check(Guild.People.Count(n=>Guild.Key(n)==workerKey)==1 && Expeditions.Owned(worker.GetComponent<NPCRoutineExecutor>()),"One named actor after midnight regeneration");
        MoveNear(worker);
        var assistance=Expeditions.Interact(worker);
        Check(assistance.Contains("Silverpine") && rescue.Stage==Stage.Escorting,$"Field assistance starts rescue escort ({assistance}, {rescue.Stage})");
        DungeonGenerationManager.Instance.EnterWorldTile(Vector2Int.zero,new Vector2Int(12,1));
        Expeditions.Reset(); Expeditions.Tick();
        Check(rescue.Stage==Stage.Returning && job.Stage==Stage.Active,"Rescue returns both actors and resumes parent");
        map.EnterWorldTile(tile.position,tile.position*100);
        worker=Guild.Actor(workerKey);
        object WorkerPurse() => AccessTools.Method(AccessTools.TypeByName("CompanionParty.State"),"Get").Invoke(null,new object[]{worker});
        (int Gold,int BonusCount) RewardBalance(object person) => (
            (int)person.GetType().GetProperty("Gold").GetValue(person),
            ((IList)person.GetType().GetField("Items").GetValue(person)).Cast<object>()
                .Count(entry=>(string)entry.GetType().GetField("Name").GetValue(entry)==job.PrizeName));
        var openingBalance=PartyBridge.Pay==null ? (Gold:0,BonusCount:0) : RewardBalance(WorkerPurse());
        var paidBalance=(openingBalance.Gold+job.Gold,openingBalance.BonusCount+1);
        spot=PathfindingEntity.FindFreeTurfsAroundPosition(worker.transform.GetVector2IntPosition()).First();
        var finalPlant=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/"+herb),new Vector3(spot.x,spot.y,0),Quaternion.identity).GetComponent<ResourceNode>();
        finalPlant.OnNPCInteract(worker).GetAwaiter().GetResult();
        Check(job.Stage==Stage.Returning && job.Progress==3 && job.Cargo.Count==3,"Native NPC harvest finishes the partially simulated order after save/load and rescue");
        map.EnterWorldTile(Vector2Int.zero,new Vector2Int(12,1));
        job.Due=Guild.Now;
        Expeditions.Reset(); Expeditions.Tick();
        Check(job.Paid && job.Stage==Stage.Delivered && !Expeditions.Owned(worker.GetComponent<NPCRoutineExecutor>()),"Paid expedition restores original schedule");
        int beforeDelivery=Guild.Bag.items.Count(i=>i.name==supply);
        Check(Guild.Collect(job).Contains("received") && job.Stage==Stage.Closed && job.Remaining==0 &&
            Guild.Bag.items.Count(i=>i.name==supply)==beforeDelivery+3,"Three physical and offscreen goods collected exactly once");
        if (PartyBridge.Pay != null)
        {
            var p=WorkerPurse();
            Check(RewardBalance(p)==paidBalance,"Settlement adds exactly the promised gold and bonus to the worker's existing inventory");
            Check(PartyBridge.Pay(worker,job.Id,job.Gold,job.Prize),"Companion reward receipt can be replayed");
            Check(RewardBalance(p)==paidBalance,"Replaying the receipt leaves the worker's gold and bonus count unchanged");
            Check(!(bool)p.GetType().GetField("Recruited").GetValue(p),"Field assignment never recruits worker");
            c.Members.Remove(workerKey);
            var world=WorldInfoManager.Instance;
            int initialDay=world.GetCurrentDay();
            for(int day=1; day<=8; day++)
            {
                AccessTools.PropertySetter(typeof(WorldInfoManager),"TotalTurnCount").Invoke(world,new object[]{(initialDay+day)*2880});
                Traverse.Create(world).Field("lastHour").SetValue(23);
                AccessTools.Method(typeof(WorldInfoManager),"AttemptToInvokeTimeEvents").Invoke(world,null);
            }
            Check(world.GetCurrentDay()==initialDay+8,"Eight native day-change events elapsed");
            SerializationManager.Save(path); SerializationManager.Load(path);
            yield return null; yield return null; yield return null;
            c=GuildStore.Current; worker=Guild.Actor(workerKey); job=Guild.Find(parentId);
            tile=DungeonGenerationManager.Instance.worldTiles[new Vector2Int(job.X,job.Y)];
            Expeditions.Reset(); Expeditions.Tick();
            var api=AccessTools.TypeByName("CompanionParty.PartyApi");
            Check((bool)AccessTools.Method(api,"Recruit").Invoke(null,new object[]{worker}),"Darian recruited eight days after his guild payout");
            Check(RewardBalance(WorkerPurse())==paidBalance,"Personal 120 gold and one bonus item survive eight midnights, save/load and recruitment without purchases");
            AccessTools.Method(api,"Dismiss").Invoke(null,new object[]{worker});
        }
        else Check(PartyBridge.Pay==null,"Guild works without Companion Party");
        Guild.Post(Work.Survey,tile,"Lavender",1,10,Guild.PlayerId,null,null);
        var cancelled=c.Jobs.Last();
        Guild.Cancel(cancelled); Guild.Cancel(cancelled);
        Check(Guild.Bag.gold==80 && cancelled.Stage==Stage.Cancelled,"Double withdrawal refunds once");
        foreach (var other in c.Jobs.Where(j=>j.Open || j.Stage==Stage.Offered).ToArray()) Expeditions.Fail(other);
        var sim=Guild.Offer(Work.Procurement,tile,supply,2,10,Guild.GuildId,Guild.GuildId);
        c.Jobs.Add(sim); c.Roster=Roster.Simulated; sim.Created=Guild.Now-2000;
        for(int i=0;i<2;i++) c.Jobs.Add(Guild.Offer(Work.Survey,tile,null,1,25,Guild.GuildId,Guild.GuildId));
        c.NextSimulatedClaim=0; c.NextDispatch=0;
        Expeditions.Reset(); Expeditions.Tick();
        Check(sim.Worker?.StartsWith("hire:")==true,"Simulated roster dispatch");
        Check(!GuildWindow.Instructions(sim).Contains($"({sim.X}, {sim.Y})"),"Simulated hire keeps the gathering tile private");
        c.LastBoardDay=-1;
        Guild.Stock(c);
        var away=c.Jobs.Single(j=>!j.Funded && j.Work==Work.Procurement && j.Stage==Stage.Offered);
        var gatherer=Guild.People.First(n=>Guild.Key(n)!=away.Issuer && Expeditions.Free(n));
        c.Members.Add(Guild.Key(gatherer));
        rep=Guild.Actor(c.Representative);
        gatherer.GetComponent<EntityMover>().Teleport(PathfindingEntity.FindFreeTurfsAroundPosition(rep.transform.GetVector2IntPosition()).First());
        Check(Guild.Accept(away,Guild.Key(gatherer)),"Named worker accepts a supply order at the counter");
        var requester=Guild.Actor(away.Issuer);
        Check(away.Issuer==away.Beneficiary && Plugin.Brief(requester,"").Contains(gatherer.GetFinalName()+" accepted the contract.") &&
            Traverse.Create(requester).Field("thingsThatHappened").GetValue<List<NeuralNPC.AdvancedMemoryElement>>().Any(m=>m.memory==away.LastReport),
            "Resident who ordered supplies remembers the named adventurer who took their posting");
        away.Trouble=false; Expeditions.PlaceAll();
        gatherer.GetComponent<EntityMover>().Teleport(new Vector2Int(away.X,away.Y)*100);
        Expeditions.Reset(); Expeditions.Tick();
        AccessTools.PropertySetter(typeof(WorldInfoManager),"TotalTurnCount").Invoke(WorldInfoManager.Instance,new object[]{away.NextGather});
        Expeditions.Reset(); Expeditions.Tick();
        Check(away.Progress==1 && away.Stage==Stage.Active,"Unvisited tile reports partial cargo");
        AccessTools.PropertySetter(typeof(WorldInfoManager),"TotalTurnCount").Invoke(WorldInfoManager.Instance,new object[]{away.NextGather+away.GatherInterval});
        Expeditions.Reset(); Expeditions.Tick();
        Check(away.Progress==3 && away.Cargo.Count==3 && away.Stage==Stage.Returning && !Guild.InRegion(away),
            "Elapsed time finishes an entire procurement order without visiting its tile");
        var receivingPack=requester.GetComponent<WorldInventory>().inventory;
        if (PartyBridge.Pay!=null)
        {
            var person=AccessTools.Method(AccessTools.TypeByName("CompanionParty.State"),"Get").Invoke(null,new object[]{requester});
            receivingPack=Traverse.Create(person).Field("Pack").GetValue<Inventory>();
        }
        int received=receivingPack.items.Count(i=>i.name==away.Item);
        away.Due=Guild.Now;
        Expeditions.Reset(); Expeditions.Tick();
        Check(away.Paid && away.Stage==Stage.Closed && receivingPack.items.Count(i=>i.name==away.Item)==received+3 &&
            Plugin.Brief(requester,"").Contains(away.RequestAccount()),"Guild pays the worker and delivers the three harvested goods to the named poster");
    }
    private static void PutToBed(NeuralNPC npc, NPCRoutine bedtime)
    {
        npc.GetComponent<EntityMover>().Teleport(bedtime.targetPosition);
        var executor=npc.GetComponent<NPCRoutineExecutor>();
        executor.StartNPCRoutine(bedtime);
        executor.pathfindingEntity.ClearPathAndDeclareSuccess();
    }

    internal static void NapHere(NeuralNPC npc)
    {
        var bed=new GameObject("Guild test bed");
        bed.transform.position=npc.transform.position;
        bed.AddComponent<BedSleepNPCInteractable>().OnNPCInteract(npc).GetAwaiter().GetResult();
        npc.GetComponent<PathfindingEntity>().ClearPathAndDeclareSuccess();
        UnityEngine.Object.DestroyImmediate(bed);
    }

    internal static void MoveNear(NeuralNPC n)
    {
        var position=n.transform.GetVector2IntPosition();
        foreach (var spot in PathfindingEntity.FindFreeTurfsAroundPosition(position).Concat(PathfindingEntity.FindFreeTurfsAroundPosition3x3(position)))
        {
            if (Turfs.IsOccupiedByCharacter(spot)) continue;
            Player.Instance.Teleport(spot);
            if (Guild.Near(n)) return;
        }
        throw new InvalidOperationException("No reachable conversation spot around "+n.GetFinalName()+" at "+position);
    }
}
