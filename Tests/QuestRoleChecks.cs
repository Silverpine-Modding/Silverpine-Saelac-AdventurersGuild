using System.Collections;
using System.IO;
using System.Linq;
using BepInEx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AdventurersGuild.Checks;

internal static class QuestRoleChecks
{
    internal static IEnumerator Exercise(Charter c, NeuralNPC worker)
    {
        var acacia=Guild.People.Single(n=>n.npcName==NPCName.Acacia);
        var a=Guild.Key(acacia);
        var w=Guild.Key(worker);
        Runner.Verify(c.BlockedGivers.SetEquals(new[]{a}) && c.BlockedTargets.SetEquals(new[]{a}) && c.Jobs.All(c.Permits),
            "Base-game Acacia is excluded in both roles on the first board");
        GuildWindow.Visit(null,true);
        var win=Object.FindObjectOfType<GuildWindow>();
        Button Button(string name)=>win.GetComponentsInChildren<Button>().Single(b=>b.name==name);
        Button("Charter").onClick.Invoke();
        win.GetComponentInChildren<TMP_InputField>().text=acacia.GetFinalName();
        Button("Quest target · blocked").onClick.Invoke();
        Runner.Verify(c.BlockedGivers.Contains(a) && !c.BlockedTargets.Contains(a),"The charter can allow Acacia as a target while keeping her from posting");
        Button("Quest target · allowed").onClick.Invoke();
        Button("Quest giver · blocked").onClick.Invoke();
        Runner.Verify(!c.BlockedGivers.Contains(a) && c.BlockedTargets.Contains(a),"Searchable charter can allow Acacia to post while blocking her as a target");
        Button("Post").onClick.Invoke();
        var beneficiary=win.GetComponentsInChildren<RectTransform>().Single(r=>r.name=="Beneficiary" && r.GetComponent<HorizontalLayoutGroup>());
        beneficiary.GetComponentInChildren<Button>().onClick.Invoke();
        var picker=win.transform.Find("Choose Beneficiary");
        picker.GetComponentInChildren<TMP_InputField>().text=acacia.GetFinalName();
        Runner.Verify(picker.GetComponentsInChildren<TextMeshProUGUI>().Any(t=>t.text.StartsWith("No matches")),"Blocked target is absent from the player commission picker");
        GuildWindow.CloseActive();
        string save=Path.Combine(Paths.GameRootPath,"quest-role-checks.sav");
        SerializationManager.Save(save); SerializationManager.Load(save);
        yield return null; yield return null; yield return null;
        c=GuildStore.Current;
        Runner.Verify(c.BlockedGivers.Count==0 && c.BlockedTargets.SetEquals(new[]{a}),
            "An explicit opt-in for Acacia and the independent target exclusion survive native save/load");

        var residents=Guild.People.Select(Guild.Key).ToArray();
        c.Representative=residents.First(k=>k!=a && k!=w && k!="npc:None");
        c.Treasury=1500;
        c.BlockedGivers.UnionWith(residents.Where(k=>k!=a));
        c.BlockedTargets.UnionWith(residents.Where(k=>k!=w));
        c.Jobs.Clear(); c.LastBoardDay=-1; Guild.Stock(c);
        var courier=c.Jobs.Single(j=>j.Work==Work.Courier);
        var escort=c.Jobs.Single(j=>j.Work==Work.Escort);
        Runner.Verify(courier.Issuer==a && courier.Beneficiary==w && escort.Issuer==Guild.GuildId && escort.Patient==w && c.Jobs.All(c.Permits),
            "Distinct giver and target pools support a sender-only Acacia and a target-only traveler");
        c.PlayerMerit=24; // This fixture signs an escort at a random distance.
        Runner.Verify(Guild.Accept(escort,Guild.PlayerId),"Previously permitted escort can be signed");
        Guild.Bag.gold=200;
        var bonus=ItemLibrary.GetItemByName("Lavender",true);
        Runner.Verify(Guild.Bag.TryAddItem(bonus),"Refund fixture adds a physical bonus");
        int jobs=c.Jobs.Count;
        Guild.Post(Work.Courier,null,"Lavender",1,30,a,null,bonus);
        Runner.Verify(c.Jobs.Count==jobs && Guild.Bag.gold==200 && Guild.Bag.ContainsItem(bonus),"A stale or direct commission for a blocked target cannot debit escrow");
        Guild.Post(Work.Courier,null,"Lavender",1,30,w,null,bonus);
        var funded=c.Jobs.Last();
        courier.Applicant=w;
        var blocked=c.Jobs.Where(j=>j.Stage==Stage.Offered && !j.Funded && (j.Beneficiary==w || j.Issuer==a)).ToArray();
        int before=c.Treasury;
        c.BlockedGivers.Add(a); c.BlockedTargets.Add(w);
        c.WithdrawBlockedOffers(); c.WithdrawBlockedOffers();
        Runner.Verify(escort.Stage==Stage.Active && blocked.All(j=>j.Stage==Stage.Cancelled) && courier.Applicant==null &&
            c.Treasury==before+blocked.Sum(j=>j.Gold),"New exclusions withdraw pending offers, release applicants, refund treasury once, and preserve signed work");
        Runner.Verify(funded.Stage==Stage.Failed && Guild.Bag.gold==170 && !Guild.Accept(funded,"hire:Wren"),"A withdrawn commission reserves the original player deposit");
        int stock=Guild.Bag.items.Count(i=>i.name=="Lavender");
        Guild.Cancel(funded); Guild.Cancel(funded);
        Runner.Verify(funded.Stage==Stage.Cancelled && Guild.Bag.gold==200 && Guild.Bag.items.Count(i=>i.name=="Lavender")==stock+1,"The journal refunds exactly one deposit and bonus item");
        c.Jobs.Clear(); c.LastBoardDay=-1; Guild.Stock(c);
        Runner.Verify(c.Jobs.Count==3 && c.Jobs.All(j=>j.Issuer==Guild.GuildId && j.Beneficiary==Guild.GuildId),
            "Guild supply, survey and bounty notices remain available when every resident is excluded");

        var tile=Guild.Regions.First();
        var parent=Guild.Offer(Work.Survey,tile,null,1,25,Guild.GuildId,Guild.GuildId);
        parent.Worker=w; parent.WorkerName=Guild.Name(w); parent.Stage=Stage.Active; parent.Trouble=true; parent.Due=Guild.Now;
        c.Jobs.Add(parent); c.NextDispatch=int.MaxValue;
        Expeditions.Reset(); Expeditions.Tick();
        Runner.Verify(parent.Stage==Stage.Active && !parent.Trouble && !c.Jobs.Any(j=>j.Work==Work.Rescue),
            "An enrolled target-blocked adventurer keeps their assignment without becoming a rescue target");
        c.BlockedTargets.Remove(w); parent.Trouble=true; parent.Due=Guild.Now;
        Expeditions.Reset(); Expeditions.Tick();
        var rescue=c.Jobs.Single(j=>j.Work==Work.Rescue);
        c.BlockedTargets.Add(w); c.WithdrawBlockedOffers();
        Expeditions.Reset(); Expeditions.Tick();
        Runner.Verify(rescue.Stage==Stage.Cancelled && parent.Stage==Stage.Active,"Withdrawing a pending rescue releases the stranded parent assignment");

        using var legacy=new MemoryStream();
        using var writer=new BinaryWriter(legacy);
        writer.Write(1); writer.Write("{\"Treasury\":11,\"Jobs\":[{\"Issuer\":\"npc:Acacia\",\"Gold\":23}]}");
        writer.Flush(); legacy.Position=0;
        GuildStore.Instance.Deserialize(new BinaryReader(legacy));
        c=GuildStore.Current;
        Runner.Verify(c.BlockedGivers.SetEquals(new[]{a}) && c.BlockedTargets.SetEquals(new[]{a}) && c.Jobs.Single().Stage==Stage.Cancelled && c.Treasury==34,
            "An older charter receives the Acacia defaults and recovers guild funds from a conflicting notice");
    }
}
