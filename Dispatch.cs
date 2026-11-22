using System;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AdventurersGuild;

internal static partial class Expeditions
{
    private const int Hour = WorldInfoManager.DAY_TURN_LENGTH / 24;
    private const string ToDesk = "visiting the guild representative";

    internal static bool CanSign(Contract job,string key)
    {
        var charter = GuildStore.Current;
        var worker = Guild.Actor(key);
        var rep = Guild.Actor(charter.Representative);
        if (!charter.Members.Contains(key) || !worker || !rep || worker == rep ||
            PartyBridge.Unavailable(worker) || (worker.transform.GetVector2IntPosition()-rep.transform.GetVector2IntPosition()).sqrMagnitude > 1) return false;
        bool eligible = job.Applicant == key
            ? worker.GetComponent<NPCRoutineExecutor>().currentRoutine?.id == Prefix + job.Id
            : Free(worker);
        if (!eligible) return false;
        AnswerGuildVisitor(rep,worker);
        return !rep.sleeping;
    }

    internal static void Dispatch(Charter c)
    {
        if (Guild.Now < c.NextDispatch || c.Representative == null) return;
        var offers = c.Jobs.Where(j => j.Stage == Stage.Offered).ToArray();
        bool Protect(Contract j) => !j.Funded && j.Work != Work.Rescue && j.Reserved == null;
        int choices = offers.Count(j => Protect(j) && Guild.Eligible(j,Guild.PlayerId));
        bool Available(Contract j) => !Protect(j) || choices > 2 || c.Standing.Rank<GuildRanks.Requirement(j);
        var pending = offers.FirstOrDefault(j => j.Applicant != null);
        if (pending != null)
        {
            var key = pending.Applicant;
            if (c.Roster == Roster.Simulated || !c.Members.Contains(key) || !Available(pending) ||
                !Guild.Eligible(pending,key) || Guild.Now >= pending.ApplicationExpires)
            {
                pending.Applicant = null;
                c.NextDispatch = Guild.Now + Hour;
            }
            else if (Guild.Accept(pending,key)) c.NextDispatch = Guild.Now + 6 * Hour;
            return;
        }

        var jobs = offers.Where(Available).OrderByDescending(j => j.Funded || j.Work == Work.Rescue)
            .ThenBy(j => j.Created).ToArray();
        int Wait(Contract j) => j.Funded || j.Work == Work.Rescue ? 180 - 2 * Math.Min(j.Gold,75) : 4 * Hour;
        if (c.Roster != Roster.Simulated && c.Members.Count > 0 && Guild.Actor(c.Representative))
        {
            var workers = c.Members.Where(k => Free(Guild.Actor(k))).ToArray();
            foreach (var j in jobs.Where(j => Guild.Now-j.Created >= Wait(j)))
            {
                var eligible = workers.Where(k => Guild.Eligible(j,k)).ToArray();
                if (eligible.Length == 0) continue;
                j.Applicant = eligible[Random.Range(0,eligible.Length)];
                j.ApplicationExpires = Guild.Now + 12 * Hour;
                return;
            }
        }
        if (c.Roster != Roster.Named && Guild.Now >= c.NextSimulatedClaim)
        {
            var hire = Hires.Select(s => "hire:" + s).FirstOrDefault(k => !c.Jobs.Any(j => j.Open && j.Worker == k));
            if (hire != null)
            {
                var job = jobs.FirstOrDefault(j => j.Reserved == null && Guild.Eligible(j,hire) &&
                    Guild.Now-j.Created >= (j.Funded || j.Work == Work.Rescue ? Wait(j)+2*Hour : 12*Hour));
                if (job != null && Guild.Accept(job,hire))
                {
                    c.NextDispatch = Guild.Now + 6 * Hour;
                    c.NextSimulatedClaim = Guild.Now + 24 * Hour;
                    return;
                }
            }
        }
        // Revisit idle staffing on game time. Active assignments still update each turn.
        c.NextDispatch = Guild.Now + 12;
    }
}
