using System;
using System.Collections.Generic;
using System.Linq;
using Silverpine.ModdingTools;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static AdventurersGuild.GuildBoardArt;

namespace AdventurersGuild;

public sealed partial class GuildWindow : ModToolBehaviour
{
    private static GuildWindow active;
    private NeuralNPC speaker;
    private Player blockedPlayer;
    private bool sign, confirm, closing, picking, noticesChanged;
    private string tab, message = "", amount = "24", quantity = "3", item = Guild.Goods[0];
    private string recipient, reserved;
    private string charterSearch="";
    private WorldTile region;
    private Item bonus;
    private int price, count;
    private Work kind;
    private GuildBoardArt art;
    private RectTransform panel, tabs, body, content, picker;
    private ScrollRect scroll;
    private TextMeshProUGUI status, purse;
    private bool Desk => sign || Guild.AtCounter(speaker);

    internal static bool Visit(NeuralNPC npc,bool board=false,DialogBox conversation=null)
    {
        if (!Guild.Ready) return false;
        if (active) return true;
        if (conversation ? !SameConversation(conversation,npc) : npc && DialogBox.Instance.isOpen) return false;
        if (PauseMenuManager.Instance.open) return false;
        Create(null,npc,board,conversation);
        return true;
    }
    internal static void Open(ModToolSession session) => Create(session,null,false);

    private static void Create(ModToolSession session,NeuralNPC npc,bool board,DialogBox conversation=null)
    {
        if (active) { session?.Close(); return; }
        var root=ModUi.CreateOverlayCanvas("Adventurers Guild",15000);
        var win=root.AddComponent<GuildWindow>();
        active=win;
        try
        {
            if (session != null)
            {
                win.AttachSession(session);
                session.EscapeHandler=win.DismissPicker;
            }
            else
            {
                win.blockedPlayer=Player.Instance;
                win.blockedPlayer.SetInputBlock(true);
            }
            win.speaker=npc; win.sign=board;
            if (conversation) win.AttachConversation(conversation);
            win.tab=npc && !Guild.AtCounter(npc) ? "Field" : board || npc ? "Board" : "Charter";
            win.Build();
        }
        catch { win.Dismiss(); throw; }
    }
    internal static void CloseActive() { if (active) active.Dismiss(); }
    internal static void RefreshNotices()
    {
        if (active && active.tab is "Board" or "Journal" or "Field") active.noticesChanged=true;
    }
    private void Dismiss()
    {
        if (closing) return;
        closing=true; gameObject.SetActive(false); Destroy(gameObject);
    }
    private void Update()
    {
        if (!Guild.Ready || (blockedPlayer && blockedPlayer != Player.Instance) || (actions!=null && !SameConversation(dialog,speaker))) { Dismiss(); return; }
        if (noticesChanged) Draw(true);
        if (FrameworkSession == null && (Input.GetKeyDown(KeyCode.Escape) ||
            SettingsUI.Instance && Input.GetKeyDown(SettingsUI.Instance.closeMenuKeyCode)))
        {
            if (!DismissPicker()) Close();
        }
    }
    protected override void OnDisable()
    {
        ReleaseConversation();
        if (blockedPlayer) blockedPlayer.SetInputBlock(false);
        blockedPlayer=null;
        if (ReferenceEquals(active,this)) active=null;
        base.OnDisable();
    }
    protected override void OnDestroy() { base.OnDestroy(); art?.Dispose(); }

    private void Build()
    {
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        art=new GuildBoardArt();
        var scale=GetComponent<CanvasScaler>(); scale.referenceResolution=new Vector2(1280,800);
        scale.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
        var root=(RectTransform)transform;
        var shade=Node(root,"Backdrop"); Fill(shade);
        Paint(shade,FrameworkSession == null ? new Color32(20,19,18,220) : new Color32(26,23,21,255)).raycastTarget=true;
        panel=Node(root,"Oak noticeboard");
        panel.anchorMin=panel.anchorMax=panel.pivot=new Vector2(.5f,.5f);
        panel.sizeDelta=new Vector2(1200,750);
        Paint(panel,new Color32(46,31,24,255)).raycastTarget=true;
        var oak=Node(panel,"Planks"); Fill(oak,5,5,5,5);
        var boards=Paint(oak,art.Wood ? new Color32(205,188,161,255) : new Color32(103,77,53,255));
        if (art.Wood) { boards.sprite=art.Wood; boards.type=Image.Type.Tiled; boards.pixelsPerUnitMultiplier=.25f; }
        var top=Node(panel,"Guild desk"); Position(top,5,5,1190,120);
        Paint(top,new Color32(42,32,27,247));
        var crest=Node(top,"Guild sign"); Position(crest,28,9,62,59);
        var emblem=Paint(crest,Color.white); emblem.preserveAspect=true;
        emblem.sprite=SerializationManager.GetPrefabFromName("prefab_furniture_board_bulletin").GetComponent<SpriteRenderer>().sprite;
        var title=art.Label(top,"Adventurers’ Guild",37,Cream,true);
        Position(title.rectTransform,106,8,678,57);
        purse=art.Label(top,"",17,Brass); Position(purse.rectTransform,768,16,265,45);
        purse.alignment=TextAlignmentOptions.MidlineRight;
        Position((RectTransform)art.Button(top,"Close",Close,Walnut).transform,1054,17,105,39);
        tabs=Node(top,"Navigation"); Position(tabs,26,75,1128,38);
        var rule=Node(top,"Brass rail"); Position(rule,0,118,1190,2); Paint(rule,Brass);
        body=Node(panel,"Page"); Position(body,25,143,1150,547);
        (scroll,content)=Scroller(body);
        var foot=Node(panel,"Counter"); Position(foot,5,699,1190,46); Paint(foot,Walnut);
        status=art.Label(foot,"",16,Cream); Fill(status.rectTransform,22,0,22,0);
        Draw();
    }
    private void Draw(bool keepScroll=false)
    {
        noticesChanged=false;
        float previous=keepScroll ? scroll.verticalNormalizedPosition : 1;
        Clear(content); Clear(tabs);
        int index=0;
        foreach (string page in new[]{"Board","Journal","Post","Charter","Field"})
        {
            if (page=="Post" && !Desk || page=="Field" && (!speaker || Guild.AtCounter(speaker))) continue;
            var button=art.Button(tabs,page,()=>{ tab=page; confirm=false; message=""; Draw(); },page==tab ? Green : Walnut);
            Position((RectTransform)button.transform,index++*146,0,136,38);
        }
        var c=GuildStore.Current;
        purse.text=Guild.Ready ? (c==null ? "" : $"Guild rank · {c.Standing.Rank}\n")+$"{Guild.Bag.gold:N0} gold in purse" : "";
        status.text=message.Length>0 ? message : c?.Representative==null ? "The charter is open for appointments." : "Guild representative   ·   "+Guild.Name(c.Representative);
        if (tab=="Charter") CharterPage(c);
        else if (c?.Representative==null) Note("Guild office","Appoint a representative in Charter to begin posting requests.");
        else if (tab=="Post") PostPage(c);
        else if (tab=="Field") FieldPage(c);
        else BoardPage(c,tab=="Journal");
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        scroll.StopMovement(); scroll.verticalNormalizedPosition=previous;
    }
    private void Run(Func<string> action)
    {
        handlingAction=true;
        try { message=action() ?? ""; }
        catch(Exception ex) { Plugin.Log.LogError(ex); message=ex.GetBaseException().Message; }
        finally { handlingAction=false; }
        Draw(true);
    }
    private RectTransform Note(string title,string text=null)
    {
        var paper=art.Sheet(content,title);
        art.Label(paper,title,26,Ink,true);
        if (!string.IsNullOrEmpty(text)) art.Label(paper,text,18,Faded);
        return paper;
    }
    private Button ActionButton(RectTransform parent,string label,Func<string> action) =>
        art.Button(parent,label,()=>Run(action),Green);

    private void CharterPage(Charter c)
    {
        var info=Note("Guild charter","Appoint a representative and enroll adventurers. Click a quest role to allow or block it. This withdraws affected unclaimed requests. Signed jobs continue. To place a board, write Adventurers Guild on a sign.");
        if (c!=null)
        {
            art.Label(info,c.Treasury < 0 ? $"Outstanding rescue advance  ·  {-(long)c.Treasury} gold" : $"Treasury  ·  {c.Treasury} gold",19,Ink);
            art.Button(info,"Staffing   ·   "+c.Roster,()=>Pick("Staffing policy",(Roster[])Enum.GetValues(typeof(Roster)),r=>r.ToString(),c.Roster,r=>{
                if (c.Roster!=r) actions?.Add($"The player changed guild staffing from {c.Roster} to {r}.");
                c.Roster=r;
            }),Walnut);
        }
        var residents=new List<(GameObject Row,string SearchText)>();
        var empty=art.Label(info,"No residents match your search.",17,Faded);
        void Filter(string query)
        {
            charterSearch=query;
            var words=query.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
            bool any=false;
            foreach (var resident in residents)
            {
                bool match=words.All(word=>resident.SearchText.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0);
                resident.Row.SetActive(match);
                any|=match;
            }
            empty.gameObject.SetActive(!any);
        }
        art.Input(info,charterSearch,"Search residents by name or role…",query=>{
            Filter(query);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scroll.StopMovement(); scroll.verticalNormalizedPosition=1;
        });
        empty.transform.SetAsLastSibling();
        var policy=c ?? new Charter();
        foreach (var npc in Guild.People.OrderBy(n=>n.GetFinalName()).ToArray())
        {
            var k=Guild.Key(npc);
            var row=art.Sheet(content,npc.GetFinalName(),16);
            bool member=c?.Members.Contains(k)==true;
            string role=c?.Representative==k ? "guild representative" : member ? "enrolled adventurer" : "resident";
            bool giverBlocked=policy.BlockedGivers.Contains(k), targetBlocked=policy.BlockedTargets.Contains(k);
            residents.Add((row.gameObject,npc.GetFinalName()+" "+role+(giverBlocked ? " blocked giver" : "")+(targetBlocked ? " blocked target" : "")));
            var controls=Node(row,"Resident"); Stack(controls,true,gap:14);
            long earned=c?.Jobs.Where(j=>j.Paid && j.Worker==k).Sum(j=>(long)j.Gold) ?? 0;
            Size(art.Label(controls,npc.GetFinalName()+(earned>0 ? $"   ·   {earned} gold earned" : ""),20,Ink).rectTransform,grow:true);
            if (c?.Representative==k) art.Label(controls,"Guild representative",17,Faded);
            else
            {
                if (!Expeditions.Assigned(npc)) Size((RectTransform)ActionButton(controls,"Appoint representative",()=>{
                    var data=GuildStore.Create(); data.Members.Remove(k); data.Representative=k; Guild.Stock(data);
                    RecordAction("The player appointed "+npc.GetFinalName()+" as the guild representative.");
                    return npc.GetFinalName()+" now holds the office."; }).transform,226,34);
                Size((RectTransform)ActionButton(controls,member ? "Retire after current job" : "Enroll as adventurer",()=>{
                    var data=GuildStore.Create(); if (member) data.Members.Remove(k); else data.Members.Add(k);
                    RecordAction(member ? npc.GetFinalName()+" will retire from guild work after any current assignment." :
                        "The player enrolled "+npc.GetFinalName()+" as a guild adventurer.");
                    return "Roster updated.";
                }).transform,222,34);
            }
            var rules=Node(row,"Quest roles"); Stack(rules,true,gap:14);
            Size((RectTransform)ActionButton(rules,giverBlocked ? "Quest giver · blocked" : "Quest giver · allowed",
                ()=>ToggleQuestRole(k,true)).transform,228,34);
            Size((RectTransform)ActionButton(rules,targetBlocked ? "Quest target · blocked" : "Quest target · allowed",
                ()=>ToggleQuestRole(k,false)).transform,228,34);
        }
        Filter(charterSearch);
    }
    private static string ToggleQuestRole(string key,bool giver)
    {
        var c=GuildStore.Create();
        var list=giver ? c.BlockedGivers : c.BlockedTargets;
        bool blocked=list.Add(key);
        if (blocked) c.WithdrawBlockedOffers();
        else list.Remove(key);
        string change=Guild.Name(key)+(blocked ? " is excluded" : " is allowed")+(giver ? " as a quest giver." : " as a quest target.");
        RecordAction(change);
        return change;
    }
    private void BoardPage(Charter c,bool journal)
    {
        if (journal) Note("Guild standing",c.Standing.Summary+"\nEarn merit by turning in contracts you complete for the guild. Harder assignments earn more.");
        var jobs=c.Jobs.Where(j=>journal ? j.Worker==Guild.PlayerId || j.Funded || j.Paid : j.Open || j.Stage==Stage.Offered).Reverse().ToArray();
        if (!Desk && !journal && message.Length==0) status.text="Visit the representative or a guild sign to accept or commission work.";
        if (jobs.Length==0) { Note(journal ? "Your journal is empty" : "No requests posted"); return; }
        for (int i=0;i<jobs.Length;i+=2)
        {
            var row=Node(content,"Notices"); Stack(row,true,gap:14);
            for (int slot=i;slot<Math.Min(i+2,jobs.Length);slot++)
            {
                var j=jobs[slot]; var paper=art.Sheet(row,j.Title); Size(paper,560);
                var tack=Node(paper,"Brass tack"); tack.gameObject.AddComponent<LayoutElement>().ignoreLayout=true;
                Position(tack,275,21,9,9); tack.localRotation=Quaternion.Euler(0,0,45); Paint(tack,Walnut);
                var cap=Node(tack,"Head"); Fill(cap,1,1,2,2); Paint(cap,Brass);
                var line=Node(paper,"Notice heading"); Stack(line,true,gap:10);
                var kindLabel=art.Label(line,WorkName(j.Work).ToUpperInvariant(),14,Faded);
                kindLabel.characterSpacing=8; Size(kindLabel.rectTransform,grow:true);
                var reward=art.Label(line,$"{j.Payout} gold"+(j.Prize==null ? "" : " + "+j.PrizeName),18,Ink);
                reward.fontStyle=FontStyles.Bold;
                art.Label(paper,j.Title,23,Ink,true);
                string grade=$"{GuildRanks.Requirement(j)} rank";
                if (!j.Funded && (j.Worker==null || j.Worker==Guild.PlayerId)) grade+=$" · {GuildRanks.Merit(j)} merit";
                if (j.SkillGold>0) grade+=$" · Includes {j.SkillGold} skill bonus gold";
                art.Label(paper,grade,16,Faded);
                art.Label(paper,Instructions(j),17,Ink);
                string byline="Posted by "+j.ClientName;
                if (j.Issuer!=j.Beneficiary) byline+="   ·   For "+j.RecipientName;
                art.Label(paper,byline,17,Ink);
                string state=j.Stage.ToString();
                if (j.Stage==Stage.Travelling) state="Travelling to the assignment";
                if (j.Worker!=null) state+="   ·   Accepted by "+j.WorkerName;
                else if (j.Reserved!=null) state+="   ·   Reserved for "+Guild.Name(j.Reserved);
                if (j.Worker!=null && j.Work==Work.Procurement) state+=$"   ·   {j.Progress}/{j.Quantity} gathered";
                art.Label(paper,state,15,Faded);
                if (j.LastReport!=null) art.Label(paper,j.LastReport,16,Faded);
                if (journal && j.Paid) foreach (var entry in j.Events.Skip(1).Where(s=>s!=j.LastReport)) art.Label(paper,entry,16,Faded);
                if (j.Work==Work.Procurement && j.Worker==Guild.PlayerId && j.Open)
                    art.Label(paper,$"In your pack  ·  {Guild.Bag.items.Count(it=>it.name==j.Item)}/{j.Quantity}",17,Ink);
                if (Desk && j.Stage==Stage.Offered && !j.Funded && j.Reserved==null)
                {
                    var signature=Node(paper,"Signature"); Stack(signature,true);
                    if (c.Standing.Rank<GuildRanks.Requirement(j))
                        art.Label(signature,"Requires "+GuildRanks.Requirement(j)+" rank to sign.",16,Ink);
                    else
                    {
                        var accept=ActionButton(signature,"Accept contract",()=>Guild.Accept(j,Guild.PlayerId) ? "Accepted. Your journal has the details." : "This contract is currently unavailable.");
                        Size((RectTransform)accept.transform,167,34);
                    }
                }
                if (Desk && j.Funded && j.Stage is Stage.Offered or Stage.Failed)
                    ActionButton(paper,"Withdraw and reclaim deposit",()=>Guild.Cancel(j));
                if (Desk && j.Funded && j.Stage==Stage.Offered) ActionButton(paper,"Increase reward by 5 gold",()=>{
                    if (Guild.Bag.gold<5 || j.Gold>int.MaxValue-5) return "Insufficient funds or reward limit reached.";
                    Guild.Bag.gold-=5; j.Gold+=5;
                    RecordAction($"The player added 5 gold to the reward for “{j.Title}”, bringing it to {j.Gold} gold.",j);
                    return "Reward increased."; });
                if (Guild.AtCounter(speaker) && j.Worker==Guild.PlayerId && j.Open)
                    ActionButton(paper,"Turn in to representative",()=>Guild.PlayerAction(j,speaker));
                if (Desk && j.Stage==Stage.Delivered) ActionButton(paper,"Release delivery to "+j.RecipientName,()=>Guild.Collect(j));
                if (j.Paid && j.Named && PartyBridge.Pay==null) art.Label(paper,"The guild holds this adventurer’s personal earnings.",16,Faded);
            }
        }
    }
    internal static string WorkName(Work work) => work switch {
        Work.Procurement=>"Supply request", Work.Courier=>"Parcel delivery", Work.Survey=>"Survey",
        Work.Bounty=>"Bounty", Work.Escort=>"Escort", _=>"Rescue" };
    internal static string WorkerArea(Contract j)
    {
        string region=j.Region ?? "Wilderness";
        string coordinates=$" ({j.X}, {j.Y})";
        return !j.Named && region.EndsWith(coordinates,StringComparison.Ordinal)
            ? region.Substring(0,region.Length-coordinates.Length) : region;
    }
    internal static string Instructions(Contract j) => j.Work switch {
        Work.Procurement=>$"Gathering area  ·  {WorkerArea(j)}. Bring {j.Quantity} × {j.Item} from any source to the representative.",
        Work.Courier when j.Worker==Guild.PlayerId=>$"Find {j.RecipientName} and choose Deliver parcel under Guild assignment. Return to the representative for payment.",
        Work.Courier when j.Named && j.Stage==Stage.Active=>j.ParcelCollected ? $"{j.WorkerName} is carrying the parcel to {j.RecipientName}." :
            $"{j.WorkerName} is collecting the parcel from the guild representative for {j.RecipientName}.",
        Work.Courier when j.Worker!=null=>$"Courier  ·  {j.WorkerName}. Recipient  ·  {j.RecipientName}.",
        Work.Courier=>$"Deliver the parcel to {j.RecipientName}. A named courier collects it from the representative and brings it to the recipient.",
        Work.Survey=>"Visit the world-map tile "+j.Region+" and report to the representative.",
        Work.Bounty=>j.TargetPrefab==null ? "Hunting ground  ·  "+j.Region+". Defeat a hostile on this tile." :
            "Hunting ground  ·  "+j.Region+". Defeat the wanted "+Bounties.Name(j.TargetPrefab)+".",
        Work.Escort when j.Paid => j.RecipientName+" returned safely to Silverpine.",
        Work.Escort when j.Worker==Guild.PlayerId && j.Stage==Stage.Returning =>
            j.RecipientName+" returned safely to Silverpine. See the representative for your reward.",
        _=>"Rendezvous  ·  "+j.Region+". Meet "+j.RecipientName+" there and escort them back to Silverpine." };
    private void FieldPage(Charter c)
    {
        if (!Guild.Near(speaker)) { Note("Field report","The traveler is out of reach."); return; }
        var page=Note(speaker.GetFinalName()); string k=Guild.Key(speaker);
        var application=c.Jobs.Find(j=>j.Stage==Stage.Offered && j.Applicant==k);
        if (application!=null) art.Label(page,"Heading to the representative to ask for “"+application.Title+"”.",17,Ink);
        foreach (var request in Plugin.RequestsBy(c,k))
            art.Label(page,request.RequestAccount(),17,Ink);
        foreach (var j in c.Jobs.Where(j=>j.Open && j.Issuer!=k && (j.Worker==k || j.Patient==k || j.Beneficiary==k)))
        {
            art.Label(page,j.Title,23,Ink,true);
            art.Label(page,$"{j.Stage}   ·   {j.Progress}/{j.Quantity}",17,Faded);
            if (k==j.Worker) foreach (var entry in j.Events) art.Label(page,entry,17,Ink);
            if (j.Work==Work.Courier && j.Worker==Guild.PlayerId && k==j.Beneficiary)
                ActionButton(page,"Deliver parcel",()=>Guild.PlayerAction(j,speaker));
        }
        if (c.Jobs.Any(j=>j.Stage==Stage.Active && (j.Worker==k && j.Work==Work.Procurement || j.Patient==k) || j.Stage==Stage.Stranded && j.Worker==k))
            ActionButton(page,"Contribute supplies or begin escort",()=>Expeditions.Interact(speaker));
    }
}
