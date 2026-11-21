using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace AdventurersGuild;

public sealed partial class GuildWindow
{
    private static readonly AccessTools.FieldRef<DialogBox,bool> NpcMode =
        AccessTools.FieldRefAccess<DialogBox,bool>("isNPCDIalog");
    private static readonly AccessTools.FieldRef<DialogBox,TMP_InputField> ReplyField =
        AccessTools.FieldRefAccess<DialogBox,TMP_InputField>("inputField");
    private static int releasedFrame = -1;
    private DialogBox dialog;
    private CanvasGroup dialogControls;
    private bool controlsWereInteractable, handlingAction;
    private List<string> actions;
    private List<Contract> discussed;

    private static bool SameConversation(DialogBox box,NeuralNPC npc) =>
        box && box==DialogBox.Instance && box.isOpen && NpcMode(box) && !box.endDialogQueued &&
        npc && NeuralNPC.currentActiveDialogNeuralNPC==npc;

    private void AttachConversation(DialogBox box)
    {
        dialog=box;
        dialogControls=box.GetComponent<CanvasGroup>();
        controlsWereInteractable=dialogControls.interactable;
        dialogControls.interactable=false;
        actions=new List<string>();
        discussed=new List<Contract>();
    }

    internal static void RecordAction(string fact,Contract job=null)
    {
        if (!active || !active.handlingAction || active.actions==null) return;
        active.actions.Add(fact);
        if (job!=null && !active.discussed.Contains(job)) active.discussed.Add(job);
    }

    private void Close()
    {
        if (closing) return;
        var box=dialog;
        var npc=speaker;
        var facts=actions;
        var jobs=discussed;
        Dismiss();
        if (!Guild.Ready || !SameConversation(box,npc)) return;
        if (facts.Count>0)
        {
            var charter=GuildStore.Current;
            string record=jobs.Count>0 && charter?.Representative==Guild.Key(npc)
                ? $"{GuildLedger.DeskHeading} kept by {npc.GetFinalName()}:\n{GuildLedger.Describe(charter,jobs)}\n" : "";
            npc.GenerateSystemDialog(record+string.Join(" ",facts)+" Respond in character to what just happened.");
            return;
        }
        var input=ReplyField(box);
        if (input.isActiveAndEnabled && input.IsInteractable()) input.ActivateInputField();
    }

    private void ReleaseConversation()
    {
        if (dialogControls) dialogControls.interactable=controlsWereInteractable;
        if (dialog) releasedFrame=Time.frameCount;
        dialog=null; dialogControls=null; actions=null; discussed=null;
    }

    // Silverpine checks Enter and the scroll wheel even when its canvas cannot receive clicks.
    [HarmonyPatch]
    private static class ConversationInput
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(DialogBox),"Update");
            yield return AccessTools.Method(typeof(DialogBox),nameof(DialogBox.SendInput));
        }
        static bool Prefix(DialogBox __instance) =>
            releasedFrame!=Time.frameCount && !(active && active.dialog==__instance);
    }

    [HarmonyPatch(typeof(DialogBox),nameof(DialogBox.CloseBox))]
    private static class ConversationEnded
    {
        static void Prefix(DialogBox __instance)
        {
            if (active && active.dialog==__instance) active.Dismiss();
        }
    }
}
