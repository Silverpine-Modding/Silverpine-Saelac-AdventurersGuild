using System.Linq;
using UnityEngine;

namespace AdventurersGuild;

internal static partial class Expeditions
{
    private static NeuralNPC CourierContact(Charter c, Contract j) => Guild.Actor(j.ParcelCollected ? j.Beneficiary : c.Representative);

    private static void AdvanceCourier(Charter c, Contract j, NeuralNPC worker)
    {
        if (PartyBridge.IsDown(worker))
        {
            var tile = DungeonGenerationManager.Instance.GetPositionWorldTile(worker.transform.GetVector2IntPosition());
            j.X = tile.position.x; j.Y = tile.position.y;
            j.Region = tile.GetDisplayName() + $" ({j.X}, {j.Y})";
            Strand(c,j);
            return;
        }
        var contact = CourierContact(c,j);
        bool arrived = contact && worker.GetComponent<NPCRoutineExecutor>().currentRoutine.id == Prefix + j.Id &&
            (worker.transform.GetVector2IntPosition() - contact.transform.GetVector2IntPosition()).sqrMagnitude <= 1;
        if (arrived) AnswerGuildVisitor(contact,worker);
        if (!arrived || contact.sleeping)
        {
            if (Guild.Now > j.Due + 2880) Fail(j);
            return;
        }
        if (!j.ParcelCollected)
        {
            j.ParcelCollected = true;
            j.Due = Guild.Now + 420;
            Guild.Event(j,$"{j.WorkerName} collected the parcel from {contact.GetFinalName()} for {j.RecipientName}.",true);
            return;
        }
        contact.AddThingThatHappened(j.WorkerName + " delivered the guild parcel to me.");
        j.Progress = j.Quantity;
        j.Stage = Stage.Returning;
        Guild.Settle(j);
    }

    private static void DirectCourier(Charter c, Contract j, NeuralNPC worker)
    {
        string activity = j.ParcelCollected ? "delivering a parcel to " + j.RecipientName : "collecting a guild parcel";
        DirectTravel(c,j,worker,activity,CourierContact(c,j));
    }

    private static bool AtField(NeuralNPC worker,Contract j)
    {
        var offset = worker.transform.GetVector2IntPosition()-new Vector2Int(j.X,j.Y)*100;
        return Mathf.Abs(offset.x)<=47 && Mathf.Abs(offset.y)<=47;
    }

    private static void DirectTravel(Charter c,Contract j,NeuralNPC worker,string activity,NeuralNPC contact)
    {
        var executor = worker.GetComponent<NPCRoutineExecutor>();
        if (executor?.currentRoutine == null || PartyBridge.Unavailable(worker)) return;
        var origin = worker.transform.GetVector2IntPosition();
        string key = Guild.Key(worker);
        if (!CanDirect(executor,Prefix+j.Id)) return;
        if (!c.Posts.TryGetValue(key,out var post))
            c.Posts[key] = post = new FieldPost { Contract = j.Id, HomeX = origin.x, HomeY = origin.y };
        bool changed = post.Phase != activity || executor.currentRoutine.id != Prefix + j.Id;
        if (executor.currentRoutine.id != Prefix + j.Id)
        {
            executor.StartOverrideRoutine(new NPCRoutine(activity,activity,activity,origin) { id = Prefix + j.Id });
            PartyBridge.Prepare(worker);
        }
        if (!changed && Guild.Now < post.NextRoute) return;
        post.Phase = activity;
        executor.currentRoutine.activity = executor.currentRoutine.visibleActivity = executor.currentRoutine.pathingActivity = activity;
        worker.currentVisibleActivity = activity;
        post.NextRoute = Guild.Now + 8;
        var map = DungeonGenerationManager.Instance;
        var from = map.GetPositionWorldTile(origin);
        post.TileX = from.position.x; post.TileY = from.position.y;
        if (j.Stage == Stage.Travelling)
        {
            if (AtField(worker,j)) return;
            var previous = new Vector2Int(post.LastRouteX,post.LastRouteY);
            post.LastRouteX=origin.x; post.LastRouteY=origin.y;
            if (!changed && previous != origin && !executor.pathfindingEntity.HasReachedTargetPosition) return;
            var center = map.GetTravelTargetPosition(new Vector2Int(j.X,j.Y));
            var edge = new Vector2Int(Mathf.Clamp(origin.x,center.x-45,center.x+45),Mathf.Clamp(origin.y,center.y-45,center.y+45));
            // World tiles share one turf coordinate system. NPC pathing can walk across an
            // ungenerated tile without triggering the player's world-tile transition.
            if (!from.IsDungeonInstance())
            {
                foreach (var point in new[]{edge,center}.Concat(PathfindingEntity.FindFreeTurfsAroundPosition(edge)))
                    if (TryAim(worker,executor,origin,point)) return;
            }
            Aim(worker,origin);
            return;
        }
        if (!contact) { Aim(worker,origin); return; }
        var destination = contact.transform.GetVector2IntPosition();
        var to = map.GetPositionWorldTile(destination);
        if (from == to) AnswerGuildVisitor(contact,worker);
        if ((origin-destination).sqrMagnitude <= 1 || from != to && (from.IsDungeonInstance() || to.IsDungeonInstance()))
        { Aim(worker,origin); return; }
        // A person occupies the destination. Use a reachable adjacent square, including
        // when the recipient has walked elsewhere since the last path was calculated.
        foreach (var goal in PathfindingEntity.FindFreeTurfsAroundPosition(destination).OrderBy(p=>(p-origin).sqrMagnitude))
            if (TryAim(worker,executor,origin,goal)) return;
        Aim(worker,origin);
    }

    private static void AnswerGuildVisitor(NeuralNPC contact, NeuralNPC visitor)
    {
        if (!contact.sleeping || PartyBridge.Unavailable(contact)) return;
        var executor = contact.GetComponent<NPCRoutineExecutor>();
        if (!CanStartRoutine(executor) || executor.currentRoutine.GetRoutineArgument<RoutineArgument_Sleep>() == null) return;
        var origin = contact.transform.GetVector2IntPosition();
        var visitorPosition = visitor.transform.GetVector2IntPosition();
        int distanceSquared = (origin-visitorPosition).sqrMagnitude;
        if (distanceSquared > 12 * 12) return;

        // The resident can open their own locked door to meet the visitor.
        var meeting = origin;
        if (distanceSquared > 1)
        {
            foreach (var spot in PathfindingEntity.FindFreeTurfsAroundPosition(visitorPosition).OrderBy(p => (p-origin).sqrMagnitude))
            {
                if (!CanWalkTo(contact,origin,spot)) continue;
                meeting = spot;
                break;
            }
            if (meeting == origin) return;
        }
        const string activity = "answering a guild visitor";
        executor.StartOverrideRoutine(new NPCRoutine(activity,activity,activity,meeting,new RoutineArgument_EndOverrideIf_AfterTurns(18)));
    }

    private static bool CanWalkTo(NeuralNPC npc,Vector2Int origin,Vector2Int goal) =>
        !Turfs.IsOccupiedByCharacter(goal) && !Turfs.TryGetTurfComponent<Trap>(goal,out _) && !TurfCollider.HasImpassableCollider(goal) &&
        Pathfinding.FindPath(origin,goal,npc.gameObject,false,ignorePenalty:false,printNoPath:false).Count > 0;

    private static bool TryAim(NeuralNPC worker,NPCRoutineExecutor executor,Vector2Int origin,Vector2Int goal)
    {
        if (!CanWalkTo(worker,origin,goal)) return false;
        executor.currentRoutine.targetPosition = goal;
        executor.pathfindingEntity.SetTargetPosition(goal);
        return true;
    }
}
