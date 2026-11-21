using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace AdventurersGuild.Checks;

internal static class BoardChecks
{
    internal static IEnumerator Exercise(Charter c,NeuralNPC rep)
    {
        var player=Player.Instance;
        CheckGoldOffers(c);
        CheckDispatch(c);
        var counter=Traverse.Create(player).Field("inputBlockCount");
        int before=counter.GetValue<int>();
        var job=c.Jobs.Single(j=>j.Work==Work.Bounty);
        Runner.Verify(job.TargetPrefab!=null,"New generated bounty has a specific quarry");
        Runner.Verify(!string.IsNullOrEmpty(job.Prize),"Stocked bounty carries a serialized item bonus");
        CheckBonusRolls(job);
        var advertisedPrize=job.Prize;
        int advertisedGold=job.Gold;
        job.TargetPrefab="prefab_enemy_cultist";
        Guild.Bag.gold=120;
        Runner.Verify(GuildWindow.Visit(null,true) && !PauseMenuManager.Instance.open,"Guild board opens directly from the world");
        var win=Object.FindObjectOfType<GuildWindow>();
        Button Button(string label)=>win.GetComponentsInChildren<Button>().Single(b=>b.name==label);
        string Notice(Contract j)=>string.Join("\n",win.GetComponentsInChildren<RectTransform>().Single(r=>r.name==j.Title)
            .GetComponentsInChildren<TextMeshProUGUI>().Select(t=>t.text));
        string Coordinates(Contract j)=>$"({j.X}, {j.Y})";
        yield return null; yield return null;
        Canvas.ForceUpdateCanvases();
        var art=Traverse.Create(win).Field("art").GetValue<GuildBoardArt>();
        Runner.Verify(art.Wood && art.Paper,"Board uses Silverpine wood and letter artwork");
        var cards=win.GetComponentsInChildren<TextMeshProUGUI>();
        Runner.Verify(Notice(job).Contains(job.PrizeName) && cards.Any(t=>t.text.Contains("from any source")) && cards.Any(t=>t.text.Contains("wanted cultist")),
            "Notices explain the gathering area and name the bounty target");
        var supply=c.Jobs.Single(j=>j.Work==Work.Procurement);
        var courier=c.Jobs.Single(j=>j.Work==Work.Courier);
        var escort=c.Jobs.Single(j=>j.Work==Work.Escort);
        Runner.Verify(supply.Issuer==supply.Beneficiary && Guild.Actor(supply.Issuer) && Guild.Actor(courier.Issuer) &&
            courier.Issuer!=courier.Beneficiary && escort.Issuer==escort.Patient && job.Issuer==Guild.GuildId &&
            c.Jobs.All(j=>Notice(j).Contains("Posted by "+j.ClientName)) && Notice(courier).Contains("For "+courier.RecipientName),
            "Board credits residents and the guild by name and distinguishes parcel sender from recipient");
        Runner.Verify(c.Jobs.Sum(j=>j.Gold)+c.Treasury==300 && !supply.Funded && !Guild.Accept(supply,supply.Issuer),
            "Guild guarantees resident rewards and prevents a requester from claiming their own posting");
        var sender=Guild.Actor(courier.Issuer);
        var traveler=Guild.Actor(escort.Patient);
        var previousRelation=rep.relationships[traveler.npcName];
        rep.relationships[traveler.npcName]=NPCRelationship.Stranger;
        var ledger=Plugin.Brief(rep,"");
        Runner.Verify(ledger.Contains(escort.RecipientName+" is the traveler") && ledger.Contains(escort.Region) &&
            ledger.Contains(traveler.GetFinalName()+" is listed as ") && ledger.Contains("intended recipient of the parcel") && ledger.Contains("the wanted cultist") &&
            rep.relationships[traveler.npcName]==NPCRelationship.Stranger,
            "Representative knows the escort traveler, waypoints, parcel recipient and bounty target through the office, even for a stranger");
        rep.relationships[traveler.npcName]=previousRelation;
        Runner.Verify(!GuildLedger.Describe(c,new[]{supply}).Contains(Coordinates(supply)) &&
            !GuildLedger.Describe(c,new[]{courier}).Contains(Coordinates(courier)),
            "Ledger observes the same gathering and delivery location rules as the board");
        Runner.Verify(Memories(sender).Contains(courier.RequestDescription()) &&
            Plugin.Brief(sender,"").Contains(courier.RequestAccount()) && courier.RequestAccount().Contains("Nobody has taken") &&
            !Plugin.Brief(Guild.Actor(courier.Beneficiary),"").Contains(courier.RequestAccount()),
            "An unclaimed parcel is known to its sender as their own request");
        Runner.Verify(!Notice(supply).Contains(Coordinates(supply)) && !Notice(courier).Contains(Coordinates(courier)) &&
            c.Jobs.Where(j=>j!=supply && j!=courier).All(j=>Notice(j).Contains(j.Region)),
            "Offered supply and courier notices hide coordinates while required destinations stay visible");
        yield return Capture(win);
        Button("Post").onClick.Invoke();
        var location=Traverse.Create(win).Field("region").GetValue<WorldTile>();
        Button(location.GetDisplayName()).onClick.Invoke();
        var areas=win.transform.Find("Choose Gathering area");
        var search=areas.GetComponentInChildren<TMP_InputField>();
        search.text=$"{location.position.x}, {location.position.y}";
        Runner.Verify(areas.GetComponentsInChildren<TextMeshProUGUI>().Any(t=>t.text.Contains("No matches")),
            "Coordinate searches cannot expose a gathering tile in the posting draft");
        search.text=location.GetDisplayName();
        search.onSubmit.Invoke(search.text);
        Button("Supply request").onClick.Invoke();
        search=win.transform.Find("Choose Objective").GetComponentInChildren<TMP_InputField>();
        search.text="parce";
        Runner.Verify(!win.transform.Find("Choose Objective").GetComponentsInChildren<Button>().Any(b=>b.name=="Bounty"),"Search filters objectives immediately");
        Button("Parcel delivery").onClick.Invoke();
        Runner.Verify(!Traverse.Create(win).Field("picking").GetValue<bool>() && Traverse.Create(win).Field("kind").GetValue<Work>()==Work.Courier,
            "Choosing the filtered result resumes the posting draft");
        var worker=c.Members.Single();
        var recipient=Guild.People.First(n=>Guild.Key(n)!=worker && n!=rep);
        Button(Guild.Name(Traverse.Create(win).Field("recipient").GetValue<string>())).onClick.Invoke();
        win.transform.Find("Choose Beneficiary").GetComponentsInChildren<Button>().Single(b=>b.name==recipient.GetFinalName()).onClick.Invoke();
        Button("Any available adventurer").onClick.Invoke();
        Button(Guild.Name(worker)).onClick.Invoke();
        Button("Review posting").onClick.Invoke();
        Runner.Verify(Button("Confirm and fund contract") && Guild.Bag.gold==120 &&
            win.GetComponentsInChildren<TextMeshProUGUI>().Any(t=>t.text=="Pickup   ·   "+rep.GetFinalName()),
            "Courier review names the representative and holds payment until confirmation");
        Button("Confirm and fund contract").onClick.Invoke();
        var posted=c.Jobs.Last();
        Runner.Verify(posted.Work==Work.Courier && posted.Reserved==worker && posted.Region==null && Guild.Bag.gold==96,
            "Posted courier commission debits the agreed reward without assigning a wilderness tile");
        Button("Journal").onClick.Invoke();
        Runner.Verify(!Notice(posted).Contains(Coordinates(posted)),"Reserving a courier keeps the waypoint hidden");
        Runner.Verify(Guild.Accept(courier,"hire:Wren") && Guild.Accept(supply,Guild.PlayerId) &&
            !GuildWindow.Instructions(courier).Contains(Coordinates(courier)) && !GuildWindow.Instructions(supply).Contains(Coordinates(supply)),
            "Simulated and player claims keep coordinates hidden");
        Runner.Verify(Memories(sender).Contains(courier.LastReport) && Memories(Guild.Actor(supply.Issuer)).Contains(supply.LastReport) &&
            Plugin.Brief(sender,"").Contains("Wren accepted the contract.") && Plugin.Brief(Guild.Actor(supply.Issuer),"").Contains(player.playerName+" accepted the contract."),
            "Each poster receives the actual simulated or player claimant in memory and dialogue");
        var spaces=Expeditions.Spaces(new Vector2Int(12,1),Vector2Int.zero,14).ToArray();
        var pickup=spaces.First();
        var start=spaces.First(p=>(p-pickup).sqrMagnitude>=16);
        var doorstep=spaces.First(p=>(p-pickup).sqrMagnitude>=64 && (p-start).sqrMagnitude>=25);
        HoldAt(rep,pickup); HoldAt(recipient,doorstep);
        Guild.Actor(worker).GetComponent<EntityMover>().Teleport(start);
        posted.Created=Guild.Now-500;
        c.NextDispatch=0;
        Expeditions.Reset(); Expeditions.Tick();
        yield return null; yield return null;
        Runner.Verify(posted.Worker==null && posted.Applicant==worker && Guild.Actor(worker).transform.GetVector2IntPosition()==start,
            "Courier leaves for the counter while the contract is still unsigned");
        Runner.Verify(Notice(supply).Contains("Posted by "+supply.ClientName) && Notice(supply).Contains("Accepted by "+player.playerName) &&
            Notice(posted).Contains("Posted by "+player.playerName),"Journal labels player and resident posters independently of their workers");
        GuildWindow.CloseActive();
        Runner.Verify(!PauseMenuManager.Instance.open && counter.GetValue<int>()==before,"Closing the board returns player input exactly once");
        var nearby=PathfindingEntity.FindFreeTurfsAroundPosition3x3(rep.transform.GetVector2IntPosition()).First();
        player.Teleport(nearby);
        Runner.Verify(GuildWindow.Visit(rep) && !PauseMenuManager.Instance.open,"Representative opens the desk without inventory");
        GuildWindow.CloseActive();
        player.Teleport(spaces.Last(p=>(p-start).sqrMagnitude>36 && (p-pickup).sqrMagnitude>36 && (p-doorstep).sqrMagnitude>9));
        posted.Due=Guild.Now-1;
        for (int turns=0;turns<70 && !posted.ParcelCollected;turns++) yield return Turn();
        Runner.Verify(posted.ParcelCollected && !posted.Paid && (Guild.Actor(worker).transform.GetVector2IntPosition()-pickup).sqrMagnitude<=1,
            $"Named courier physically walks to the representative for pickup (worker {Guild.Actor(worker).transform.GetVector2IntPosition()}, representative {rep.transform.GetVector2IntPosition()}, pickup {pickup}, stage {posted.Stage})");
        var map=DungeonGenerationManager.Instance;
        var tile=new Vector2Int(job.X,job.Y);
        Runner.Verify(Guild.Accept(job,Guild.PlayerId),"Player signs the cultist contract");
        var id=job.Id;
        BountyMark[] Targets()=>Object.FindObjectsOfType<BountyMark>().Where(m=>m.ContractId==id).ToArray();
        Runner.Verify(Targets().Length==0,"No target is created on the unloaded tile");
        map.EnterWorldTile(tile,tile*100);
        Expeditions.PlaceAll();
        Runner.Verify(Targets().Length==1 && SerializationManager.GetPrefabName(Targets()[0].gameObject)==job.TargetPrefab,
            "Arrival and repeated placement produce one wanted cultist");
        string save=Path.Combine(Paths.GameRootPath,"guild-bounty.sav");
        SerializationManager.Save(save); SerializationManager.Load(save);
        yield return null; yield return null; yield return null;
        player=Player.Instance;
        job=Guild.Find(id);
        Runner.Verify(job?.TargetPrefab=="prefab_enemy_cultist" && Targets().Length==1 && job.Prize==advertisedPrize && job.Gold==advertisedGold,"Native save/load restores the marked cultist and its quoted gold and item reward");
        posted=Guild.Find(posted.Id); courier=Guild.Find(courier.Id); supply=Guild.Find(supply.Id);
        sender=Guild.Actor(courier.Issuer);
        var prompt=(System.Threading.Tasks.Task<string>)AccessTools.Method(typeof(NeuralNPC),"GetEnvironmentString").Invoke(sender,new object[]{true});
        while (!prompt.IsCompleted) yield return null;
        Runner.Verify(prompt.GetAwaiter().GetResult().Contains(courier.RequestAccount()) && Memories(sender).Contains(courier.LastReport),
            "Restored sender knows the recipient and claimant in Silverpine's native dialogue prompt and memory");
        Runner.Verify(posted.ParcelCollected && posted.Stage==Stage.Active && !GuildWindow.Instructions(supply).Contains(Coordinates(supply)),
            "Native save/load preserves the parcel in transit and the player gatherer's hidden tile");
        map=DungeonGenerationManager.Instance;
        map.EnterWorldTile(Vector2Int.zero,new Vector2Int(12,1));
        map.EnterWorldTile(tile,tile*100);
        Runner.Verify(Targets().Length==1,"Same-day return does not duplicate a target");
        map.EnterWorldTile(Vector2Int.zero,new Vector2Int(12,1));
        AccessTools.Method(typeof(DungeonGenerationManager),"OnNewDay").Invoke(map,new object[]{2});
        map.EnterWorldTile(tile,tile*100);
        Runner.Verify(Targets().Length==1,"Regenerated wilderness produces one replacement target");
        var quarry=Targets()[0].GetComponent<Enemy>();
        var unrelated=Object.Instantiate(SerializationManager.GetPrefabFromName(job.TargetPrefab),quarry.transform.position+Vector3.right,Quaternion.identity).GetComponent<Enemy>();
        Expeditions.Kill(null,true,unrelated);
        Runner.Verify(job.Stage==Stage.Active,"An unmarked cultist cannot satisfy the bounty");
        Object.DestroyImmediate(unrelated.gameObject);
        var queue=Traverse.Create(ActionQueue.Instance).Field("queueActions").GetValue<List<QueueAction>>();
        var damage=quarry.GetComponent<Damagable>();
        damage.TakeDamage(Player.Instance.gameObject,new Damage(10000,DamageType.Normal));
        var hit=queue.Last(q=>q.queuer==Player.Instance.gameObject && q.longAction!=null);
        queue.Remove(hit);
        yield return hit.longAction;
        Runner.Verify(job.Stage==Stage.Returning,"Defeating the marked cultist through the native damage queue completes fieldwork");
        Expeditions.PlaceAll();
        yield return null;
        Runner.Verify(Targets().Length==0,"No bounty target respawns after the kill");
        recipient=Guild.Actor(posted.Beneficiary);
        var moved=spaces.First(p=>(p-doorstep).sqrMagnitude>=25 && (p-pickup).sqrMagnitude>=25 && (p-start).sqrMagnitude>=9);
        var recipientExecutor=recipient.GetComponent<NPCRoutineExecutor>();
        recipientExecutor.StopOverrideRoutine(recipientExecutor.currentRoutine);
        recipient.GetComponent<EntityMover>().Teleport(moved);
        Runner.NapHere(recipient);
        Runner.Verify(recipient.sleeping,"Parcel recipient falls asleep at a new location before the courier arrives");
        for (int turns=0;turns<100 && !posted.Paid;turns++) yield return Turn();
        var memories=Traverse.Create(recipient).Field("thingsThatHappened").GetValue<List<NeuralNPC.AdvancedMemoryElement>>();
        Runner.Verify(posted.Paid && posted.Stage==Stage.Closed && memories.Count(m=>m.memory.Contains(posted.WorkerName+" delivered the guild parcel"))==1 &&
            (Guild.Actor(worker).transform.GetVector2IntPosition()-recipient.transform.GetVector2IntPosition()).sqrMagnitude<=1 &&
            !recipient.sleeping && !Expeditions.Assigned(Guild.Actor(worker)),
            "Courier wakes the moved recipient, hands over the parcel in person and receives one payout while the player is away");
        map.EnterWorldTile(Vector2Int.zero,new Vector2Int(12,1));
        rep=Guild.Actor(GuildStore.Current.Representative);
        player.Teleport(PathfindingEntity.FindFreeTurfsAroundPosition(rep.transform.GetVector2IntPosition()).First());
        int purseBeforeBounty=Guild.Bag.gold;
        Guild.PlayerAction(job,rep);
        Guild.PlayerAction(job,rep);
        Runner.Verify(job.Paid && Guild.Bag.gold==purseBeforeBounty+advertisedGold && Guild.Bag.items.Count(i=>Guild.Pack(i)==advertisedPrize)==1,
            "Turning in the bounty pays the advertised gold and enchanted item exactly once");
        var requester=Guild.Actor(supply.Issuer);
        var destination=requester.GetComponent<WorldInventory>().inventory;
        for (int i=0;i<supply.Quantity;i++)
        {
            var unit=ItemLibrary.GetItemByName(supply.Item,true);
            Runner.Verify(Guild.Bag.TryAddItem(unit),"Add requested supply to player inventory");
        }
        var submitted=Guild.Bag.items.Where(it=>it.name==supply.Item).Take(supply.Quantity).ToArray();
        int gold=Guild.Bag.gold, received=destination.items.Count(it=>it.name==supply.Item);
        int capacity=destination.maxItems;
        destination.maxItems=destination.items.Count+1;
        Guild.PlayerAction(supply,rep);
        Runner.Verify(supply.Paid && supply.Remaining==supply.Quantity-1 && Guild.Bag.gold==gold+supply.Gold &&
            submitted.All(it=>!Guild.Bag.items.Contains(it)) && destination.items.Count(it=>it.name==supply.Item)==received+1,
            "Player turns in goods for the resident and receives guild pay while excess cargo stays at the counter");
        destination.maxItems=System.Math.Max(capacity,destination.items.Count+supply.Remaining);
        Guild.Collect(supply); Guild.Collect(supply); Guild.PlayerAction(supply,rep);
        Runner.Verify(supply.Stage==Stage.Closed && Guild.Bag.gold==gold+supply.Gold && destination.items.Count(it=>it.name==supply.Item)==received+supply.Quantity,
            "Resident receives the rest of the order once");
        var address=requester.transform.GetVector2IntPosition();
        var requesterTile=map.GetPositionWorldTile(address).position;
        if (requesterTile!=map.currentPlayerWorldTilePosition)
        {
            map.EnterWorldTile(requesterTile,address);
            yield return null;
        }
        Runner.MoveNear(requester);
        requester.OnInteract(player.gameObject);
        var field=Object.FindObjectOfType<GuildWindow>();
        Runner.Verify(field && field.GetComponentsInChildren<TextMeshProUGUI>().Any(t=>t.text==supply.RequestAccount()),
            $"Talking to {requester.GetFinalName()} shows their completed request and claimant (window {field!=null}, combat {player.inCombat}, dialogue {DialogBox.Instance.isOpen}, save blocked {SaveUI.Instance.IsSavingBlocked()})");
        GuildWindow.CloseActive();
        if (!Guild.InTown) map.EnterWorldTile(Vector2Int.zero,new Vector2Int(12,1));
        c=GuildStore.Current;
        var scout=Guild.People.First(n=>Expeditions.Free(n));
        c.Members.Add(Guild.Key(scout));
        scout.GetComponent<EntityMover>().Teleport(PathfindingEntity.FindFreeTurfsAroundPosition(rep.transform.GetVector2IntPosition()).First());
        var outbound=Guild.Offer(Work.Bounty,map.worldTiles[tile],null,1,24,Guild.GuildId,Guild.GuildId);
        c.Jobs.Add(outbound);
        Runner.Verify(Guild.Accept(outbound,Guild.Key(scout)),"Enrolled scout signs a bounty beside the clerk");
        Expeditions.PlaceAll();
        map.EnterWorldTile(tile,tile*100);
        var mark=Bounties.Find(outbound.Id);
        Runner.Verify(mark && map.GetPositionWorldTile(scout.transform.GetVector2IntPosition()).position==Vector2Int.zero,
            "Arriving ahead of the scout spawns the wanted enemy and leaves the scout on their route");
        Expeditions.Kill(scout,false,mark.GetComponent<Enemy>());
        Runner.Verify(outbound.Stage==Stage.Travelling && outbound.Progress==0,"A travelling scout cannot earn early bounty credit");
    }
    private static IEnumerable<string> Memories(NeuralNPC npc) =>
        Traverse.Create(npc).Field("thingsThatHappened").GetValue<List<NeuralNPC.AdvancedMemoryElement>>().Select(m=>m.memory);
    internal static IEnumerator Turn()
    {
        while (ActionQueue.Instance.turnBeingProcessed) yield return null;
        ActionQueue.Instance.ProcessTurn();
        while (ActionQueue.Instance.turnBeingProcessed) yield return null;
    }
    internal static void HoldAt(NeuralNPC npc,Vector2Int position)
    {
        npc.GetComponent<EntityMover>().Teleport(position);
        npc.GetComponent<NPCRoutineExecutor>().StartOverrideRoutine(new NPCRoutine("waiting","waiting","waiting",position));
    }

    private static void CheckBonusRolls(Contract bounty)
    {
        var library=ItemLibrary.Items;
        var originals=library.ToArray();
        var rng=Random.state;
        try
        {
            var blade=ItemLibrary.GetItemByName("Iron Sword",true);
            blade.name="Guild test blade";
            var herb=ItemLibrary.GetItemByName("Lavender",true);
            var hidden=ItemLibrary.GetItemByName("Iron Sword",true); hidden.name="HIDDEN_Test";
            var unlock=ItemLibrary.GetItemByName("Shed Key",true);
            library.Clear(); library.AddRange(new[]{blade,herb,hidden,unlock});
            var template=Guild.Pack(blade);
            var qualities=new HashSet<Quality>();
            var enchants=new HashSet<ItemComponent_Enchantment.EnchantmentType>();
            bool sawHerb=false;
            Random.InitState(98431);
            for (int i=0;i<64;i++)
            {
                var sample=new Contract();
                Guild.RollBonus(sample);
                using var reader=new BinaryReader(new MemoryStream(System.Convert.FromBase64String(sample.Prize)));
                var item=new Item(); item.Deserialize(reader);
                if (item.name==herb.name)
                {
                    sawHerb=true;
                    if (item.TryGetItemComponent<ItemComponent_Enchantment>(out _)) throw new System.Exception("Herb was enchanted");
                    continue;
                }
                if (item.name!=blade.name) throw new System.Exception("Internal item selected");
                var quality=item.GetItemComponent<IQualityItemComponent>().Quality;
                var enchant=item.GetItemComponents<ItemComponent_Enchantment>().Single().enchantmentType;
                qualities.Add(quality); enchants.Add(enchant);
                if (!sample.PrizeName.Contains(quality.ToString()) || !sample.PrizeName.Contains(enchant.ToString())) throw new System.Exception("Notice missed rolled properties");
                bounty.Prize=sample.Prize; bounty.PrizeName=sample.PrizeName;
            }
            Runner.Verify(qualities.Count>1 && enchants.Count>1 && sawHerb && Guild.Pack(blade)==template,
                "Live item pool yields varied item types, quality and eligible enchantments on independent copies");
        }
        finally { library.Clear(); library.AddRange(originals); Random.state=rng; }
    }

    private static void CheckGoldOffers(Charter charter)
    {
        var tiles=DungeonGenerationManager.Instance.worldTiles;
        var near=tiles[new Vector2Int(1,0)];
        var far=tiles[new Vector2Int(-4,-3)];
        Contract Quote(Work work,WorldTile tile,int quantity=1) => Guild.Offer(work,tile,"Lavender",quantity,null,Guild.GuildId,Guild.GuildId);
        Runner.Verify(Quote(Work.Survey,near).Gold==21 && Quote(Work.Survey,far).Gold==51 &&
            Quote(Work.Escort,far).Gold==73 && Quote(Work.Rescue,far).Gold==85,
            "Seven map crossings pay more than one, with higher fees for escort and rescue duties");
        Runner.Verify(Quote(Work.Procurement,near,3).Gold==25 && Quote(Work.Procurement,far,3).Gold==25 &&
            Quote(Work.Procurement,far,1).Gold==15 && Guild.Offer(Work.Survey,far,null,1,7,Guild.PlayerId,Guild.PlayerId).Gold==7,
            "Supply pay follows the goods and quantity, and a player's commission keeps its chosen amount");
        var bounty=charter.Jobs.Single(j=>j.Work==Work.Bounty);
        Runner.Verify(bounty.Gold==26+5*(System.Math.Abs(bounty.X)+System.Math.Abs(bounty.Y))+Bounties.HazardPay(bounty.TargetPrefab) &&
            Bounties.HazardPay("prefab_enemy_cultist")>Bounties.HazardPay("prefab_enemy_giantrat"),
            "Stocked bounty pays for its posted hunting ground and actual quarry");
        var courier=charter.Jobs.Single(j=>j.Work==Work.Courier);
        var actor=Guild.Actor(courier.Beneficiary);
        var mover=actor.GetComponent<EntityMover>();
        var original=actor.transform.GetVector2IntPosition();
        try
        {
            mover.Teleport(near.position*100);
            var parcel=Guild.Offer(Work.Courier,null,null,1,null,courier.Issuer,courier.Beneficiary);
            mover.Teleport(far.position*100);
            Runner.Verify(parcel.Gold==21 && Guild.Offer(Work.Courier,null,null,1,null,courier.Issuer,courier.Beneficiary).Gold==51 &&
                parcel.Region==null,"Parcel fee uses the recipient's surface tile at posting and stays fixed when they move");
        }
        finally { mover.Teleport(original); }
        var poor=new Charter{Treasury=10,Representative=charter.Representative};
        Guild.Stock(poor);
        Runner.Verify(poor.Jobs.Count==0 && poor.Treasury==10,"Ordinary work is posted only when the full quoted fee is funded");
    }

    private static void CheckDispatch(Charter original)
    {
        int start=Guild.Now;
        var clock=AccessTools.PropertySetter(typeof(WorldInfoManager),"TotalTurnCount");
        void At(int hours)=>clock.Invoke(WorldInfoManager.Instance,new object[]{start+hours*120});
        var c=new Charter{Representative=original.Representative,Roster=Roster.Simulated};
        var tile=Guild.Regions.First();
        GuildStore.Instance.Data=c;
        try
        {
            for(int i=0;i<5;i++) c.Jobs.Add(Guild.Offer(Work.Survey,tile,null,1,21,Guild.GuildId,Guild.GuildId));
            Guild.Accept(c.Jobs[0],Guild.PlayerId);
            int Offers()=>c.Jobs.Count(j=>j.Stage==Stage.Offered);
            At(11); Expeditions.Dispatch(c);
            Runner.Verify(Offers()==4,"A player can accept a quest and browse all four remaining notices during the simulated hiring delay");
            At(12); Expeditions.Dispatch(c); Expeditions.Dispatch(c);
            Runner.Verify(Offers()==3,"Only one simulated hire signs when a batch of notices comes of age");
            At(35); Expeditions.Dispatch(c);
            Runner.Verify(Offers()==3,"A simulated hire cannot take a second contract inside the rolling day");
            At(36); Expeditions.Dispatch(c);
            At(96); Expeditions.Dispatch(c);
            Runner.Verify(Offers()==2,"NPC dispatch leaves two public choices even after several days");
            var funded=Guild.Offer(Work.Survey,tile,null,1,20,Guild.PlayerId,Guild.PlayerId);
            c.Jobs.Add(funded);
            At(101); Expeditions.Dispatch(c);
            Runner.Verify(funded.Worker!=null && Offers()==2,"A commission can be filled while the final two public choices are protected");
            var first=c.Jobs.First(j=>j.Stage==Stage.Offered);
            var key=original.Members.First();
            c.Members.Add(key); c.Roster=Roster.Mixed;
            c.Jobs.Add(Guild.Offer(Work.Survey,tile,null,1,20,Guild.GuildId,Guild.GuildId));
            At(130); Expeditions.Dispatch(c);
            Runner.Verify(first.Applicant==key && first.Worker==null && Guild.Accept(first,Guild.PlayerId),
                "A named applicant takes priority in Mixed mode and the player can sign before the resident reaches the desk");
        }
        finally { GuildStore.Instance.Data=original; At(0); }
    }
    // Batch players have no presented swap chain. Render the live canvas into a camera target.
    private static IEnumerator Capture(GuildWindow win)
    {
        var canvas=win.GetComponent<Canvas>();
        var camera=new GameObject("Board capture").AddComponent<Camera>();
        var rt=new RenderTexture(1280,800,24);
        var image=new Texture2D(1280,800,TextureFormat.RGB24,false);
        var previous=RenderTexture.active;
        var nodes=win.GetComponentsInChildren<Transform>(true);
        var layers=nodes.Select(n=>n.gameObject.layer).ToArray();
        try
        {
            foreach (var node in nodes) node.gameObject.layer=30;
            camera.cullingMask=1<<30; camera.orthographic=true;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black; camera.targetTexture=rt;
            camera.transform.position=new Vector3(0,0,-2000);
            canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=8;
            yield return null; Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,1280,800),0,0); image.Apply();
            File.WriteAllBytes(Path.Combine(Paths.GameRootPath,"guild-board.png"),image.EncodeToPNG());
            Runner.Verify(image.GetPixels32().Any(p=>p.r>120 && p.g>100),"Captured rendered parchment and board");
        }
        finally
        {
            RenderTexture.active=previous; canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.worldCamera=null;
            for(int i=0;i<nodes.Length;i++) if(nodes[i]) nodes[i].gameObject.layer=layers[i];
            camera.targetTexture=null; rt.Release(); Object.Destroy(rt); Object.Destroy(image); Object.Destroy(camera.gameObject);
        }
    }
}
