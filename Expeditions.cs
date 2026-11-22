using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AdventurersGuild;

internal static partial class Expeditions
{
    internal const string Prefix = "silverpine.guild.trip.";
    private static readonly string[] Hires = { "Wren", "Ash", "Flint", "Reed" };
    private static readonly Vector2Int[] Steps = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
    private static readonly HashSet<string> credited = new();
    private static readonly HashSet<int> harvested = new();
    private static int harvestFrame;
    private static int lastTurn = -1;
    internal static void Reset() { credited.Clear(); lastTurn = -1; }

    internal static bool Assigned(NeuralNPC n)
    {
        var c = GuildStore.Current;
        if (c == null || !n) return false;
        string k = Guild.Key(n);
        return c.Posts.ContainsKey(k) || c.Jobs.Any(j => j.Stage == Stage.Offered && j.Applicant == k ||
            j.Open && (j.Worker == k || j.EscortInField && j.Patient == k));
    }
    internal static bool Free(NeuralNPC n)
    {
        if (!n || GuildStore.Current?.Representative == Guild.Key(n) || Assigned(n) || PartyBridge.Unavailable(n)) return false;
        var e = n.GetComponent<NPCRoutineExecutor>();
        return CanStartRoutine(e) &&
            !IFollowingPlayerNPCRoutineArgument.DoesNeuralNPCHaveIFollowingPlayerNPCRoutineArgument(n);
    }

    // A bed interaction is a timed nap that Silverpine explicitly ends on override.
    private static bool InterruptibleNap(NPCRoutine routine) => routine?.GetRoutineArgument<RoutineArgument_Sleep>() != null &&
        routine.GetRoutineArgument<RoutineArgument_EndOverrideIf_Overridden>() != null;
    private static bool CanStartRoutine(NPCRoutineExecutor e) => e?.currentRoutine != null &&
        (e.preOverrideRoutines.Count == 0 || e.preOverrideRoutines.Count == 1 && InterruptibleNap(e.currentRoutine));

    private static bool CanDirect(NPCRoutineExecutor e,string id)
    {
        if (e.currentRoutine.id == id || CanStartRoutine(e)) return true;
        if (e.preOverrideRoutines.First?.Value?.id != id || !InterruptibleNap(e.currentRoutine)) return false;
        e.StopOverrideRoutine(e.currentRoutine);
        return e.currentRoutine?.id == id;
    }

    internal static bool Owned(NPCRoutineExecutor e) => e?.currentRoutine?.id?.StartsWith(Prefix,StringComparison.Ordinal) == true;
    internal static bool CanFight(NeuralNPC n)
    {
        var c = GuildStore.Current;
        if (c == null || !Guild.Ready || !c.Posts.TryGetValue(Guild.Key(n),out var post)) return false;
        var j = Guild.Find(post.Contract);
        return j?.Named == true && j.Worker == Guild.Key(n) && j.Stage == Stage.Active && (j.Work == Work.Courier || Guild.InRegion(j)) &&
            Owned(n.GetComponent<NPCRoutineExecutor>()) && OnTile(n,DungeonGenerationManager.Instance.currentPlayerWorldTilePosition);
    }

    internal static void Tick()
    {
        var c = GuildStore.Current;
        if (!Guild.Ready || c == null || lastTurn == Guild.Now) return;
        lastTurn = Guild.Now;
        Guild.Stock(c);
        Dispatch(c);
        foreach (var j in c.Jobs.ToArray())
        {
            if (j.Open) Advance(c,j);
        }
        PlaceAll();
        foreach (var j in c.Jobs.Where(x => x.Named && x.Paid && !credited.Contains(x.Id)))
        {
            var n = Guild.Actor(j.Worker);
            if (n && PartyBridge.Pay != null && PartyBridge.Pay(n,j.Id,j.Gold,j.Prize)) credited.Add(j.Id);
        }
    }

    private static bool Interrupted(Contract j)
    {
        var c = GuildStore.Current;
        foreach (var key in Roles(j))
        {
            var n = Guild.Actor(key);
            if (!n) return true;
            var e = n.GetComponent<NPCRoutineExecutor>();
            if (e?.currentRoutine == null || (!Owned(e) && e.preOverrideRoutines.Count > 0) ||
                (c.Posts.ContainsKey(key) && !Owned(e) && PartyBridge.Unavailable(n) && !PartyBridge.IsDown(n))) return true;
        }
        return false;
    }

    internal static IEnumerable<string> Roles(Contract j)
    {
        if (j.Named) yield return j.Worker;
        if (j.EscortInField) yield return j.Patient;
    }
    private static void Advance(Charter c, Contract j)
    {
        if (j.Work == Work.Courier && !Guild.Actor(j.Beneficiary))
        {
            if (Guild.Now > j.Due + 2880) Fail(j);
            return;
        }
        if (Interrupted(j))
        {
            if (j.GatherInterval > 0) j.NextGather = Guild.Now + j.GatherInterval;
            if (Guild.Now > j.Due + 2880 && Roles(j).Any(k => !Guild.Actor(k))) Fail(j);
            return;
        }
        if (j.Stage == Stage.Stranded)
        {
            // A withdrawn rescue never pays out; its already signed parent can still finish.
            var rescue = c.Jobs.LastOrDefault(r => r.Parent == j.Id);
            if (rescue?.Stage == Stage.Cancelled) ResumeExpedition(j,"The guild arranged assistance for " + j.WorkerName + ".");
            return;
        }
        if (j.Stage == Stage.Travelling)
        {
            var traveller = Guild.Actor(j.Worker);
            if (traveller && !PartyBridge.IsDown(traveller) && AtField(traveller,j) &&
                traveller.GetComponent<NPCRoutineExecutor>().currentRoutine?.id == Prefix+j.Id)
            {
                Guild.StartFieldwork(j);
                Guild.Event(j,j.WorkerName+" reached "+j.Region+" and began the assignment.",true);
            }
            else if (Guild.Now >= j.Due) Fail(j);
            return;
        }
        if (j.Stage == Stage.Escorting && j.Worker == Guild.PlayerId)
        {
            var subject = Guild.Actor(j.Patient);
            if (Guild.InTown && Guild.Near(subject) && OnTile(subject,Vector2Int.zero)) CompleteField(j);
            return;
        }
        if (j.Worker == Guild.PlayerId)
        {
            if (j.Stage == Stage.Active && j.Work == Work.Survey && Guild.InRegion(j)) CompleteField(j);
            return;
        }
        // Older named couriers entered Returning at the wilderness waypoint,
        // before recording a handoff. Finish that leg at the actual recipient.
        if (j.Work == Work.Courier && j.Named && j.Stage == Stage.Returning)
        {
            j.ParcelCollected = true;
            j.Stage = Stage.Active;
            j.Due = Guild.Now + 420;
        }
        if (j.Stage == Stage.Returning)
        {
            if (Guild.Now >= j.Due)
            {
                if (j.Work == Work.Courier && Guild.Actor(j.Beneficiary) is NeuralNPC recipient)
                    recipient.AddThingThatHappened(j.WorkerName + " delivered the guild parcel to me.");
                Guild.Settle(j);
            }
            return;
        }
        if (j.Stage != Stage.Active) return;
        var n = j.Named ? Guild.Actor(j.Worker) : null;
        if (j.Work == Work.Courier && j.Named)
        {
            AdvanceCourier(c,j,n);
            return;
        }
        bool present = n && Guild.InRegion(j);
        if (j.Work != Work.Rescue && j.Named && (PartyBridge.IsDown(n) || j.Trouble && Guild.Now >= j.Due - 60))
        {
            Strand(c,j); return;
        }
        if (!j.Reported && Guild.Now >= j.Due - 150)
        {
            j.Reported = true;
            string[] accounts = {"I had to double back after losing the trail.", "I stopped to repair a strap on my pack.", "I found tracks crossing my route."};
            Guild.Event(j, j.WorkerName + " recorded: " + accounts[Random.Range(0,accounts.Length)]);
        }
        if (Guild.Now < j.NextWork) return;
        j.NextWork = Guild.Now + 6;
        if (j.Work == Work.Rescue && Guild.Find(j.Parent)?.Stage != Stage.Stranded) { Fail(j); return; }
        if (present)
        {
            if (!CanFight(n)) return;
            if (j.Work is Work.Procurement or Work.Bounty && j.TargetPrefab == null && Guild.Now > j.Due + 120)
            {
                var alternatives = Guild.RegionsFor(j.Work,j.Item).Where(t => t.position != new Vector2Int(j.X,j.Y)).ToArray();
                if (alternatives.Length > 0)
                {
                    var next = alternatives[Random.Range(0,alternatives.Length)];
                    j.X = next.position.x; j.Y = next.position.y;
                    j.Region = next.GetDisplayName() + $" ({j.X}, {j.Y})";
                    Guild.StartJourney(j);
                    Guild.Event(j,j.WorkerName + " moved the search to " + j.Region + ".",true);
                    return;
                }
                Fail(j); return;
            }
            if (j.Work == Work.Procurement) { j.NextGather = Guild.Now + Math.Max(1,j.GatherInterval); Gather(j,n); return; }
            if (j.Work == Work.Bounty)
            {
                var target=Bounties.Find(j.Id);
                if (!target) Wander(n);
                else if ((n.transform.position-target.transform.position).sqrMagnitude>2) Aim(n,target.transform.GetVector2IntPosition());
                return;
            }
            if (j.Work is Work.Rescue or Work.Escort)
            {
                var patient = Guild.Actor(j.Patient);
                if (!patient) return;
                if ((patient.transform.position - n.transform.position).sqrMagnitude > 4)
                { Aim(n,patient.transform.GetVector2IntPosition()); return; }
            }
            else Wander(n);
        }
        if (j.Work == Work.Procurement) GatherOffscreen(j);
        else if (Guild.Now >= j.Due) CompleteField(j);
    }

    internal static void ScheduleGather(Contract j)
    {
        int remaining = Math.Max(1,j.Quantity - j.Progress);
        j.GatherInterval = Math.Max(1,(Math.Max(1,j.Due - Guild.Now) + remaining - 1) / remaining);
        j.NextGather = Guild.Now + j.GatherInterval;
    }

    private static void GatherOffscreen(Contract j)
    {
        if (j.GatherInterval <= 0) ScheduleGather(j);
        while (j.Progress < j.Quantity && Guild.Now >= j.NextGather)
        {
            var item = ItemLibrary.GetItemByName(j.Item,true);
            if (item == null) return;
            j.Cargo.Add(Guild.Pack(item));
            j.Progress++;
            j.NextGather += j.GatherInterval;
        }
        if (j.Progress >= j.Quantity) CompleteField(j);
    }

    internal static void CompleteField(Contract j)
    {
        if (!j.Open || j.Stage is Stage.Returning or Stage.Travelling) return;
        if (j.Work == Work.Rescue)
        {
            var parent = Guild.Find(j.Parent);
            if (parent?.Stage != Stage.Stranded) return;
            ResumeExpedition(parent,j.WorkerName + " helped " + parent.WorkerName + " return to their assignment.");
        }
        j.Progress = j.Quantity; j.Stage = Stage.Returning; j.Due = Guild.Now + 45;
        Guild.Event(j,j.WorkerName + " completed the fieldwork for " + j.Title + ".");
    }
    private static void ResumeExpedition(Contract job,string report)
    {
        var worker = Guild.Actor(job.Worker);
        if (worker) PartyBridge.Assist(worker);
        job.Stage = Stage.Active; job.Trouble = false; job.Due = Guild.Now + 180;
        if (job.Work == Work.Procurement) ScheduleGather(job);
        Guild.Event(job,report,true);
    }

    private static void Strand(Charter c, Contract j)
    {
        if (j.Stage != Stage.Active) return;
        if (c.BlockedTargets.Contains(j.Worker))
        {
            ResumeExpedition(j,"The guild arranged assistance for " + j.WorkerName + ".");
            return;
        }
        j.Stage = Stage.Stranded;
        string note = j.WorkerName + " missed their check-in from " + j.Region + ".";
        Guild.Event(j,note,true);
        if (c.Jobs.Any(k => k.Parent == j.Id && (k.Open || k.Stage == Stage.Offered))) return;
        // An emergency can draw on future guild income. Reserve the entire promised fee now.
        var rescue = Guild.Offer(Work.Rescue,DungeonGenerationManager.Instance.worldTiles[new Vector2Int(j.X,j.Y)],null,1,null,Guild.GuildId,j.Worker);
        rescue.Parent = j.Id; rescue.Patient = j.Worker; c.Treasury -= rescue.Gold;
        c.Jobs.Add(rescue);
    }
    internal static void Fail(Contract j)
    {
        if (!j.Open && j.Stage != Stage.Offered) return;
        j.Stage = Stage.Failed;
        if (!j.Funded) GuildStore.Current.Treasury += j.Payout;
        Guild.Event(j,$"The guild could not complete “{j.Title}”.",true);
        foreach (var child in GuildStore.Current.Jobs.Where(k => k.Parent == j.Id && (k.Open || k.Stage == Stage.Offered)).ToArray()) Fail(child);
    }

    internal static string Interact(NeuralNPC n)
    {
        if (!Guild.Near(n)) return "Move closer.";
        var c = GuildStore.Current; string k = Guild.Key(n);
        var rescue = c.Jobs.FirstOrDefault(j => j.Work == Work.Rescue && j.Patient == k && j.Stage == Stage.Offered);
        if (rescue != null && !Guild.Accept(rescue,Guild.PlayerId) && c.Standing.Rank<GuildRanks.Requirement(rescue))
            return $"The guild requires {GuildRanks.Requirement(rescue)} rank to sign this rescue.";
        var pickup = c.Jobs.FirstOrDefault(j => j.Worker == Guild.PlayerId && j.Stage == Stage.Active && j.Patient == k && Guild.InRegion(j));
        if (pickup != null)
        {
            PartyBridge.Assist(n);
            pickup.Stage = Stage.Escorting;
            Guild.Event(pickup,Player.Instance.playerName + " found " + n.GetFinalName() + " and is escorting them to Silverpine.");
            PlaceAll();
            return "Stay together and return to Silverpine.";
        }
        var j = c.Jobs.FirstOrDefault(x => x.Worker == k && x.Work == Work.Procurement && x.Stage == Stage.Active && Guild.InRegion(x));
        var item = j == null ? null : Guild.Bag.items.FirstOrDefault(i => i.name == j.Item);
        if (item == null) return "No matching supplies to contribute.";
        j.Cargo.Add(Guild.Pack(item));
        Guild.Bag.RemoveItem(item); j.Progress++;
        Guild.Event(j,Player.Instance.playerName + " contributed one " + j.Item + ".");
        if (j.Progress >= j.Quantity) CompleteField(j);
        return "Contribution recorded.";
    }
    internal static void Kill(NeuralNPC worker, bool player, Enemy enemy)
    {
        var c = GuildStore.Current;
        if (c == null || !Guild.Ready || !player && !worker) return;
        var key = player ? Guild.PlayerId : Guild.Key(worker);
        var mark = enemy.GetComponent<BountyMark>();
        var job = c.Jobs.FirstOrDefault(j => j.Stage == Stage.Active && j.Work == Work.Bounty && j.Worker == key && Guild.InRegion(j) &&
            OnTile(enemy,new Vector2Int(j.X,j.Y)) && (mark ? j.Id == mark.ContractId : j.TargetPrefab == null));
        if (job != null) CompleteField(job);
    }

    private static void Gather(Contract j, NeuralNPC n)
    {
        if (harvestFrame != Time.frameCount) { harvested.Clear(); harvestFrame = Time.frameCount; }
        var origin = n.transform.GetVector2IntPosition();
        var node = UnityEngine.Object.FindObjectsOfType<ResourceNode>().Where(r => r.isActiveAndEnabled &&
            !harvested.Contains(r.GetInstanceID()) && (string)Guild.ResourceName.GetValue(r) == j.Item && OnTile(r,new Vector2Int(j.X,j.Y))).OrderBy(r => (r.transform.position - n.transform.position).sqrMagnitude).FirstOrDefault();
        if (!node) { Wander(n); return; }
        var target = node.transform.GetVector2IntPosition();
        if ((origin-target).sqrMagnitude > 2) { Aim(n,target); return; }
        var bag = n.GetComponent<WorldInventory>().inventory;
        if (!bag.CanAddItem(ItemLibrary.GetItemByName(j.Item,true))) { Wander(n); return; }
        harvested.Add(node.GetInstanceID());
        node.OnNPCInteract(n).GetAwaiter().GetResult();
    }

    private static void Aim(NeuralNPC n, Vector2Int p)
    {
        var e = n.GetComponent<NPCRoutineExecutor>();
        if (!Owned(e)) return;
        e.currentRoutine.targetPosition = p;
        if (e.pathfindingEntity.GetTargetPosition() != p) e.pathfindingEntity.SetTargetPosition(p);
    }
    private static void Wander(NeuralNPC n)
    {
        var e = n.GetComponent<NPCRoutineExecutor>();
        if (!e.pathfindingEntity.HasReachedTargetPosition) return;
        var options = Spaces(n.transform.GetVector2IntPosition(),DungeonGenerationManager.Instance.currentPlayerWorldTilePosition,3).ToArray();
        if (options.Length > 0) Aim(n,options[Random.Range(0,options.Length)]);
    }
    private static bool OnTile(Component o, Vector2Int tile) => DungeonGenerationManager.Instance.GetPositionWorldTile(o.transform.GetVector2IntPosition())?.position == tile;
    internal static IEnumerable<Vector2Int> Spaces(Vector2Int start, Vector2Int tile, int radius)
    {
        var seen = new HashSet<Vector2Int>{start};
        var q = new Queue<Vector2Int>(); q.Enqueue(start);
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            foreach (var step in Steps)
            {
                var v = p + step;
                if (!seen.Add(v) || (start-v).sqrMagnitude > radius*radius || DungeonGenerationManager.Instance.GetPositionWorldTile(v)?.position != tile ||
                    TurfCollider.HasImpassableCollider(v) || Turfs.TryGetTurfComponent<WorldTileChanger>(v,out _) ||
                    Turfs.TryGetTurfComponent<Trap>(v,out _) || Turfs.TryGetTurfComponent<AutoTurnback>(v,out _)) continue;
                q.Enqueue(v);
                if (!Turfs.IsOccupiedByCharacter(v) && !Turfs.TryGetTurfComponent<Enemy>(v,out _)) yield return v;
            }
        }
    }

    internal static HashSet<string> TravellingEscorts()
    {
        var movers = new HashSet<string>();
        var c = GuildStore.Current;
        if (!Guild.Ready || c == null) return movers;
        var pos = Player.Instance.transform.GetVector2IntPosition();
        foreach (var j in c.Jobs.Where(j => j.Worker == Guild.PlayerId && j.Stage == Stage.Escorting))
        {
            var n = Guild.Actor(j.Patient);
            if (n && Owned(n.GetComponent<NPCRoutineExecutor>()) && (pos-n.transform.GetVector2IntPosition()).sqrMagnitude <= 36 &&
                OnTile(n,DungeonGenerationManager.Instance.currentPlayerWorldTilePosition)) movers.Add(j.Patient);
        }
        return movers;
    }

    internal static void PlaceAll(bool arrival = false, HashSet<string> crossing = null)
    {
        var c = GuildStore.Current;
        if (c == null || !Guild.Ready) return;
        Bounties.ReleaseFinished(c);
        foreach (var bounty in c.Jobs.Where(j => j.TargetPrefab != null && j.Stage is (Stage.Active or Stage.Travelling) && Guild.InRegion(j)))
            Bounties.Place(bounty);
        var wanted = new HashSet<string>();
        foreach (var j in c.Jobs.Where(j => j.Stage == Stage.Offered && j.Applicant != null))
        {
            wanted.Add(j.Applicant);
            var applicant = Guild.Actor(j.Applicant);
            if (applicant) DirectTravel(c,j,applicant,ToDesk,Guild.Actor(c.Representative));
        }
        foreach (var j in c.Jobs.Where(j => j.Open))
        foreach (var k in Roles(j))
        {
            wanted.Add(k);
            var n = Guild.Actor(k);
            if (!n) continue;
            var escort = c.Jobs.FirstOrDefault(r => r.Patient == k && r.Worker == Guild.PlayerId && r.Stage == Stage.Escorting);
            Place(c,n,j,escort != null,arrival,crossing?.Contains(k) == true);
            if (arrival && escort == j && j.Work == Work.Escort && Guild.InTown && OnTile(n,Vector2Int.zero) &&
                Guild.Near(n) && n.GetComponent<NPCRoutineExecutor>()?.currentRoutine?.id == Prefix+j.Id)
            {
                CompleteField(j);
                wanted.Remove(k);
            }
        }
        foreach (var key in c.Posts.Keys.Where(k => !wanted.Contains(k)).ToArray()) Release(c,key);
    }

    private static void Place(Charter c, NeuralNPC n, Contract j, bool follow, bool arrival, bool crossed)
    {
        var key = Guild.Key(n);
        var e = n.GetComponent<NPCRoutineExecutor>();
        if (e?.currentRoutine == null) return;
        string id = Prefix + j.Id;
        if (e.currentRoutine.id != id && PartyBridge.Unavailable(n) && !PartyBridge.IsDown(n)) return;
        if (!CanDirect(e,id)) return;
        if (j.Stage == Stage.Travelling && key == j.Worker && !follow)
        {
            DirectTravel(c,j,n,"travelling to "+j.Region,null);
            return;
        }
        if (j.Work == Work.Courier && j.Named && !follow && j.Stage != Stage.Stranded)
        {
            if (j.Stage == Stage.Active) DirectCourier(c,j,n);
            return;
        }
        var map = DungeonGenerationManager.Instance;
        bool recorded = c.Posts.TryGetValue(key,out var post);
        var tile = new Vector2Int(j.X,j.Y);
        if (follow && recorded) tile = crossed ? map.currentPlayerWorldTilePosition : new Vector2Int(post.TileX,post.TileY);
        var here = tile == map.currentPlayerWorldTilePosition;
        var player = Player.Instance.transform.GetVector2IntPosition();
        if (here && map.GetPositionWorldTile(player)?.position != tile) return;
        var pos = n.transform.GetVector2IntPosition();
        string phase = follow ? "escorting" : j.Stage == Stage.Stranded ? "waiting for rescue" : j.Work == Work.Escort && key == j.Patient ? "waiting for an escort" : j.Work.ToString().ToLowerInvariant();
        bool move = !recorded || !OnTile(n,tile) || here && arrival && (!follow || (player-pos).sqrMagnitude > 16);
        if (move)
        {
            // An unloaded tile only has map coordinates. Terrain is inspected after player arrival.
            var goal = tile * 100;
            if (here)
            {
                var spaces = Spaces(player,tile,follow ? 3 : 9).ToList();
                if (spaces.Count == 0) return;
                goal = follow ? spaces[0] : spaces[Random.Range(spaces.Count/2,spaces.Count)];
            }
            if (!recorded) c.Posts[key] = post = new FieldPost { Contract = j.Id, HomeX = pos.x, HomeY = pos.y };
            n.GetComponent<EntityMover>().Teleport(goal);
            pos = goal;
        }
        if (!recorded || e.currentRoutine.id != id)
        {
            var routine = new NPCRoutine(phase,phase,phase,pos) { id = id };
            e.StartOverrideRoutine(routine);
            PartyBridge.Prepare(n);
        }
        if (post.Phase != phase || move)
        {
            post.Phase = phase; post.TileX = tile.x; post.TileY = tile.y;
            e.currentRoutine.activity = e.currentRoutine.visibleActivity = phase;
            n.currentVisibleActivity = phase;
            Aim(n,pos);
        }
        if (follow && here && (pos-player).sqrMagnitude > 2) Aim(n,player);
    }

    private static void Release(Charter c,string key)
    {
        var post = c.Posts[key];
        var n = Guild.Actor(key);
        if (!n) return;
        var e = n.GetComponent<NPCRoutineExecutor>();
        if (e?.currentRoutine == null) return;
        var own = e.GetCurrentAndPreOverrideRoutines().FirstOrDefault(r => r?.id == Prefix + post.Contract);
        if (own != null && own != e.currentRoutine) { e.preOverrideRoutines.Remove(own); c.Posts.Remove(key); return; }
        if (own != null)
        {
            var job = Guild.Find(post.Contract);
            bool deliveredTraveler = job?.Work == Work.Escort && job.Patient == key && (job.Stage == Stage.Returning || job.Paid);
            bool restorePosition = deliveredTraveler ? !OnTile(n,Vector2Int.zero) :
                post.Phase != ToDesk && (job?.Work != Work.Courier || post.Phase == "courier");
            if (restorePosition)
            {
                var home = new Vector2Int(post.HomeX,post.HomeY);
                // Offscreen escorts and older saves can still have the traveler at the rendezvous.
                var map = DungeonGenerationManager.Instance;
                if (deliveredTraveler && map.GetPositionWorldTile(home)?.position != Vector2Int.zero)
                    home = map.GetTravelTargetPosition(Vector2Int.zero);
                var position = TurfCollider.HasImpassableCollider(home) || Turfs.IsOccupiedByCharacter(home) ? Turfs.FindNearestFreeSpot(home) : home;
                n.GetComponent<EntityMover>().Teleport(position);
            }
            if (e.preOverrideRoutines.Count > 0) e.StopOverrideRoutine(own);
            else e.CheckRoutine(WorldInfoManager.Instance.GetCurrentHourOfDay(),false);
        }
        c.Posts.Remove(key);
    }
}
