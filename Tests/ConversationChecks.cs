using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AdventurersGuild.Checks;

internal static class ConversationChecks
{
    private static int replies;
    private static bool ModelReply(ref Task<string> __result)
    {
        replies++;
        __result=Task.FromResult("nods.* \"I have entered that in the guild ledger.\"");
        return false;
    }
    private static bool NoQuery(ref Task __result) { __result=Task.CompletedTask; return false; }
    private static bool PlayerNext(ref Task<NeuralNPC> __result) { __result=Task.FromResult<NeuralNPC>(null); return false; }
    private static IEnumerator Until(Func<bool> ready)
    {
        float end=Time.realtimeSinceStartup+12;
        while (!ready() && Time.realtimeSinceStartup<end) yield return null;
        if (!ready()) throw new TimeoutException("Guild conversation did not reach the expected UI state.");
    }

    internal static IEnumerator Exercise(Charter c,NeuralNPC rep,NeuralNPC worker)
    {
        var harmony=new Harmony("silverpine.guild.checks.conversation");
        void Stub(string native,string method)=>harmony.Patch(AccessTools.Method(typeof(NeuralNPC),native),
            prefix:new HarmonyMethod(typeof(ConversationChecks),method));
        Stub("Generate",nameof(ModelReply));
        Stub("QueryExpression",nameof(NoQuery));
        Stub("QueryNPCFunctions",nameof(NoQuery));
        Stub("GetNextMultiSpeaker",nameof(PlayerNext));
        var box=DialogBox.Instance;
        var player=Player.Instance;
        player.Teleport(PathfindingEntity.FindFreeTurfsAroundPosition(rep.transform.GetVector2IntPosition()).First());
        NeuralNPC.currentActiveDialogNeuralNPC=rep;
        box.DisplayLoading("Opening guild conversation",true);
        rep.DoStartNPCMode(DialogBox.SpriteSwitchMode.Instant);
        var prior=rep.dialogElements.AddToDialog(SpeakerType.NPC,"We were discussing the guild.");
        box.DisplayText("Welcome.",_=>throw new InvalidOperationException("A draft was sent behind the guild board."),new List<UpperButtonOption>());
        var input=Traverse.Create(box).Field("inputField").GetValue<TMP_InputField>();
        var nativeText=Traverse.Create(box).Field("textElement").GetValue<TextMeshProUGUI>();
        var controls=box.GetComponent<CanvasGroup>();
        GuildWindow Window()=>Object.FindObjectOfType<GuildWindow>();
        Button Button(string label)=>Window().GetComponentsInChildren<Button>().Single(b=>b.name==label);
        IEnumerator OpenDesk()
        {
            yield return Until(()=>box.isOpen && !box.isAnimatingText && controls.interactable);
            var popout=Traverse.Create(box).Field("popOutButton").GetValue<Button>();
            var layout=Traverse.Create(box).Field("buttonOptionsLayoutGroup").GetValue<LayoutGroup>();
            popout.onClick.Invoke();
            yield return Until(()=>layout.GetComponent<CanvasGroup>().blocksRaycasts);
            layout.GetComponentsInChildren<Button>().Single(b=>b.GetComponentInChildren<TextMeshProUGUI>().text=="Guild business").onClick.Invoke();
            yield return Until(()=>Window());
        }
        yield return Until(()=>!box.isAnimatingText);
        input.text="An unfinished question";
        yield return OpenDesk();
        Runner.Verify(box.isOpen && NeuralNPC.currentActiveDialogNeuralNPC==rep && !controls.interactable,"Guild business covers the existing conversation");
        box.SendInput();
        Button("Charter").onClick.Invoke();
        var window=Window();
        var search=window.GetComponentsInChildren<TMP_InputField>().Single();
        search.text=worker.GetFinalName().Substring(0,3).ToUpperInvariant()+" adven";
        var rows=window.GetComponentsInChildren<RectTransform>().Where(r=>r.name=="Resident").ToArray();
        Runner.Verify(rows.Length==1 && rows[0].parent.name==worker.GetFinalName(),"Charter search matches a partial name and role together");
        search.text="unknown resident";
        Runner.Verify(window.GetComponentsInChildren<TextMeshProUGUI>().Any(t=>t.text=="No residents match your search."),"Charter reports empty results");
        search.text="representative";
        Runner.Verify(window.GetComponentsInChildren<RectTransform>().Single(r=>r.name=="Resident").parent.name==rep.GetFinalName(),"Search can find the representative by role");
        search.text="";
        Runner.Verify(window.GetComponentsInChildren<RectTransform>().Count(r=>r.name=="Resident")==Guild.People.Count(),"Clearing search restores all residents");
        Button("Close").onClick.Invoke();
        Runner.Verify(box.isOpen && controls.interactable && input.text=="An unfinished question" && rep.dialogElements.Last()==prior && replies==0,
            "Closing after browsing restores the same conversation and unsent reply");
        yield return OpenDesk();
        var job=c.Jobs.Single(j=>j.Work==Work.Procurement);
        window=Window();
        window.GetComponentsInChildren<RectTransform>().Single(r=>r.name==job.Title).GetComponentsInChildren<Button>().Single().onClick.Invoke();
        Button("Turn in to representative").onClick.Invoke();
        Runner.Verify(job.Stage==Stage.Active && !job.Paid,"Missing supplies cannot turn in the contract");
        for (int i=0;i<job.Quantity;i++) Guild.Bag.TryAddItem(ItemLibrary.GetItemByName(job.Item,true));
        Button("Turn in to representative").onClick.Invoke();
        Button("Close").onClick.Invoke();
        yield return Until(()=>box.isOpen && !box.isAnimatingText && nativeText.text.Contains("guild ledger"));
        var entries=rep.dialogElements.SkipWhile(e=>e!=prior).Skip(1).ToArray();
        Runner.Verify(job.Paid && replies==1 && entries.Length==2 && entries[0].speakerType==SpeakerType.System &&
            entries[0].contents.Contains(job.Title) && entries[0].contents.Contains("handed in") && entries[0].contents.Contains(job.Gold+" gold") &&
            entries[0].contents.IndexOf("handed in")==entries[0].contents.LastIndexOf("handed in") && entries[1].speakerType==SpeakerType.NPC,
            "Accepting and turning in yields one visible NPC reply in the original history with only committed actions");
        var tile=Guild.Regions.First();
        var original=Guild.Offer(Work.Survey,tile,null,1,20,Guild.GuildId,Guild.GuildId);
        original.Stage=Stage.Stranded; original.Worker="custom:Nessa"; original.WorkerName="Nessa";
        var rescue=Guild.Offer(Work.Rescue,tile,null,1,25,Guild.GuildId,original.Worker);
        rescue.Patient=original.Worker; rescue.RecipientName=original.WorkerName; rescue.Parent=original.Id;
        c.Jobs.InsertRange(0,new[]{original,rescue});
        var distractions=Enumerable.Range(0,7).Select(i=>{
            var other=Guild.Offer(Work.Procurement,tile,"Test supply "+i,1,10,Guild.GuildId,Guild.GuildId);
            other.Stage=Stage.Active; other.Worker=Guild.PlayerId; other.WorkerName=player.playerName;
            return other;
        }).ToList();
        c.Jobs.AddRange(distractions);
        var question=rep.dialogElements.AddToDialog(SpeakerType.Player,"Who is Nessa?");
        string identity="Nessa is the guild adventurer who missed a check-in from "+rescue.Region;
        var prompt=rep.GetCombinedPrompt(rep.dialogElements,false);
        yield return Until(()=>prompt.IsCompleted);
        Runner.Verify(prompt.GetAwaiter().GetResult().Contains(identity) && prompt.Result.Contains(original.Title) &&
            !Plugin.Brief(worker,"").Contains(identity),"An older, unclaimed rescue for an unloaded resident reaches the native dialogue prompt when asked by name");
        rep.dialogElements.Remove(question);
        yield return OpenDesk();
        Window().GetComponentsInChildren<RectTransform>().Single(r=>r.name==rescue.Title).GetComponentInChildren<Button>().onClick.Invoke();
        Button("Close").onClick.Invoke();
        yield return Until(()=>replies==2 && !box.isAnimatingText && rep.dialogElements.Last().speakerType==SpeakerType.NPC);
        string cue=rep.dialogElements.Last(e=>e.speakerType==SpeakerType.System).contents;
        Runner.Verify(cue.Contains(identity) && cue.Contains(original.Title) && cue.Contains("Accepted by "+player.playerName) &&
            cue.Contains("back to Silverpine") && cue.Contains("25 gold") && !cue.Contains(distractions[0].Title),
            "Accepting Nessa's rescue at the desk supplies her identity, last check-in, assignment, claimant, return destination and reward in the reaction");
        question=rep.dialogElements.AddToDialog(SpeakerType.Player,"Where did they last check in?");
        Runner.Verify(Plugin.Brief(rep,"").Contains(identity),"A follow-up without repeating the name keeps the exact rescue in the representative's briefing");
        rep.dialogElements.Remove(question);
        c.Jobs.RemoveAll(distractions.Contains);
        NeuralNPC.multiDialogParticipants=new List<NeuralNPC>{rep,worker};
        NeuralNPC.initialMultiDialogParticipants=new List<NeuralNPC>{rep,worker};
        yield return OpenDesk();
        var survey=c.Jobs.Single(j=>j.Work==Work.Survey && j.Stage==Stage.Offered);
        Window().GetComponentsInChildren<RectTransform>().Single(r=>r.name==survey.Title).GetComponentInChildren<Button>().onClick.Invoke();
        Button("Close").onClick.Invoke();
        yield return Until(()=>replies==3 && !box.isAnimatingText && worker.dialogElements.Last().speakerType==SpeakerType.NPC);
        Runner.Verify(NeuralNPC.currentActiveDialogNeuralNPC==rep && NeuralNPC.multiDialogParticipants.Count==2 &&
            worker.dialogElements.Any(e=>e.speakerType==SpeakerType.System && e.contents.Contains(survey.Title)),"Guild business preserves group participants and delivers the representative's reaction");
        yield return OpenDesk();
        var bounty=c.Jobs.Single(j=>j.Work==Work.Bounty);
        Window().GetComponentsInChildren<RectTransform>().Single(r=>r.name==bounty.Title).GetComponentInChildren<Button>().onClick.Invoke();
        box.CloseBox();
        Runner.Verify(!Window() && replies==3 && !box.isOpen,"External dialogue teardown closes the board without speaking into a different conversation");
    }
}
