using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static AdventurersGuild.GuildBoardArt;

namespace AdventurersGuild;

public sealed partial class GuildWindow
{
    private static readonly Work[] Objectives={Work.Procurement,Work.Courier,Work.Survey,Work.Bounty,Work.Escort};
    private void PostPage(Charter c)
    {
        if (!Desk) return;
        if (confirm)
        {
            var review=Note("Review your commission",WorkName(kind)+" for "+Guild.Name(recipient));
            if (kind==Work.Courier) art.Label(review,"Pickup   ·   "+Guild.Name(c.Representative),19,Ink);
            else art.Label(review,AreaLabel(kind)+"   ·   "+DraftArea(region),19,Ink);
            if (kind==Work.Procurement) art.Label(review,$"{count} × {item}",19,Ink);
            art.Label(review,$"Reward held by the guild   ·   {price} gold"+(bonus==null ? "" : " and "+bonus.name),19,Ink);
            if (reserved!=null) art.Label(review,"Reserved for "+Guild.Name(reserved),18,Ink);
            art.Label(review,$"Purse after posting   ·   {Guild.Bag.gold-price} gold",18,Faded);
            ActionButton(review,"Confirm and fund contract",FundCommission);
            art.Button(review,"Back to draft",()=>{confirm=false; Draw();},Walnut);
            return;
        }
        var form=Note("Commission a request");
        Field(form,"Objective",WorkName(kind),()=>Pick("Objective",Objectives,WorkName,kind,v=>kind=v));
        if (kind==Work.Procurement)
        {
            Field(form,"Supply",item,()=>Pick("Supply",Guild.Goods,s=>s,item,v=>item=v));
            Numeric(form,"Quantity",quantity,2,v=>quantity=v);
        }
        if (kind==Work.Courier)
            art.Label(form,"A named courier collects the parcel from "+Guild.Name(c.Representative)+" and walks it to the beneficiary.",16,Faded);
        else
        {
            var tiles=Guild.RegionsFor(kind,item).OrderBy(t=>t.position.sqrMagnitude).ThenBy(t=>t.position.x).ThenBy(t=>t.position.y).ToArray();
            if (kind==Work.Procurement)
            {
                tiles=tiles.GroupBy(t=>t.GetDisplayName()).Select(g=>g.First()).ToArray();
                region=tiles.FirstOrDefault(t=>region!=null && t.GetDisplayName()==region.GetDisplayName()) ?? tiles.FirstOrDefault();
            }
            else if (!tiles.Contains(region)) region=tiles.FirstOrDefault();
            if (region==null) {art.Label(form,"There is no available destination for this request this season.",18,Faded); return;}
            Field(form,AreaLabel(kind),DraftArea(region),()=>Pick(AreaLabel(kind),tiles,DraftArea,region,v=>region=v));
            if (kind==Work.Procurement) art.Label(form,"This region can yield the requested supply this season. A named adventurer who takes the job will report their search tile.",16,Faded);
        }
        var recipients=Guild.People.OrderBy(n=>n.GetFinalName()).Select(Guild.Key)
            .Where(k=>k!=c.Representative && !c.BlockedTargets.Contains(k)).ToList();
        if (kind is not (Work.Escort or Work.Courier)) recipients.Insert(0,Guild.PlayerId);
        if (!recipients.Contains(recipient)) recipient=recipients.FirstOrDefault();
        if (recipient==null) {art.Label(form,"There is no eligible recipient for this request.",18,Faded); return;}
        Field(form,"Beneficiary",Guild.Name(recipient),()=>Pick("Beneficiary",recipients,Guild.Name,recipient,v=>recipient=v));
        var reserves=c.Members.Where(k=>Guild.Actor(k) && (kind is not (Work.Escort or Work.Courier) || k!=recipient)).OrderBy(Guild.Name).ToList();
        reserves.Insert(0,null);
        if (!reserves.Contains(reserved)) reserved=null;
        string Worker(string k)=>k==null ? "Any available adventurer" : Guild.Name(k);
        Field(form,"Reserve for",Worker(reserved),()=>Pick("Reserve for",reserves,Worker,reserved,v=>reserved=v));
        var prizes=Guild.Bag.items.Where(i=>!Player.Instance.IsItemEquipped(i) && !i.name.Contains("HIDDEN_") && i.GetType()==typeof(Item)).ToList();
        prizes.Insert(0,null);
        if (!prizes.Contains(bonus)) bonus=null;
        string Prize(Item i)=>i?.name ?? "None";
        Field(form,"Bonus item",Prize(bonus),()=>Pick("Bonus item",prizes,Prize,bonus,v=>bonus=v));
        Numeric(form,"Reward in gold",amount,9,v=>amount=v);
        ActionButton(form,"Review posting",()=>{
            count=1;
            if (!int.TryParse(amount,out price) || price<=0 || price>Guild.Bag.gold ||
                kind==Work.Procurement && (!int.TryParse(quantity,out count) || count<1 || count>99))
                return "Enter an affordable reward and a quantity from 1 to 99.";
            confirm=true; return "";
        });
    }
    private static string AreaLabel(Work work)=>work switch {
        Work.Procurement=>"Gathering area",Work.Survey=>"Survey site",
        Work.Bounty=>"Hunting ground",_=>"Rendezvous" };
    private string DraftArea(WorldTile t)=>kind==Work.Procurement ? t.GetDisplayName() : $"{t.GetDisplayName()} ({t.position.x}, {t.position.y})";
    private string FundCommission()
    {
        confirm=false;
        var destination=kind==Work.Courier ? null : region;
        if (kind==Work.Procurement)
        {
            var choices=Guild.RegionsFor(kind,item).Where(t=>t.GetDisplayName()==region.GetDisplayName()).ToArray();
            if (choices.Length==0) return "That region is no longer available. Choose another region.";
            destination=choices[UnityEngine.Random.Range(0,choices.Length)];
        }
        return Guild.Post(kind,destination,item,count,price,recipient,reserved,bonus);
    }
    private RectTransform FormRow(RectTransform form,string label)
    {
        var row=Node(form,label); Stack(row,true,gap:12);
        Size(art.Label(row,label,18,Ink).rectTransform,218,38);
        return row;
    }
    private void Numeric(RectTransform form,string label,string value,int limit,Action<string> update)
    {
        var field=art.Input(FormRow(form,label),value,label,update);
        field.contentType=TMP_InputField.ContentType.IntegerNumber;
        field.characterLimit=limit; Size((RectTransform)field.transform,180,38);
    }
    private void Field(RectTransform form,string heading,string value,Action open)
    {
        var row=FormRow(form,heading);
        var button=art.Button(row,value,open,Walnut,38);
        Size((RectTransform)button.transform,grow:true,height:38);
        var t=button.GetComponentInChildren<TextMeshProUGUI>();
        t.alignment=TextAlignmentOptions.MidlineLeft; Fill(t.rectTransform,15,0,45,0);
        var search=art.Label((RectTransform)button.transform,"Choose…",15,Brass); Fill(search.rectTransform,0,0,14,0); search.alignment=TextAlignmentOptions.MidlineRight;
    }
    private bool DismissPicker()
    {
        if (!picking) return false;
        picking=false;
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        picker.gameObject.SetActive(false); Destroy(picker.gameObject); picker=null;
        panel.GetComponent<CanvasGroup>().interactable=true;
        return true;
    }
    private void Pick<T>(string title,IReadOnlyList<T> values,Func<T,string> label,T current,Action<T> choose)
    {
        if (picking) return;
        picking=true;
        var underlying=panel.GetComponent<CanvasGroup>() ?? panel.gameObject.AddComponent<CanvasGroup>();
        underlying.interactable=false;
        picker=Node(transform,"Choose "+title); Fill(picker); Paint(picker,new Color32(26,21,18,210)).raycastTarget=true;
        var paper=Node(picker,"Selection"); paper.sizeDelta=new Vector2(660,602);
        var background=Paint(paper,art.Paper ? Color.white : Cream);
        if (art.Paper) { background.sprite=art.Paper; background.type=Image.Type.Sliced; background.pixelsPerUnitMultiplier=2; }
        background.raycastTarget=true;
        Position(art.Label(paper,title,28,Ink,true).rectTransform,32,22,437,44);
        Position((RectTransform)art.Button(paper,"Back",()=>DismissPicker(),Walnut).transform,522,28,105,36);
        RectTransform list=Node(paper,"Choices"); Position(list,32,136,596,430);
        var (choices,rows)=Scroller(list);
        var names=values.Select(label).ToArray();
        int limit=40;
        string query="";
        void Select(int i) { choose(values[i]); DismissPicker(); message=""; Draw(true); }
        void Results()
        {
            Clear(rows);
            var matches=Enumerable.Range(0,names.Length).Where(i=>Matches(names[i],query)).ToArray();
            foreach (var i in matches.Take(limit))
            {
                bool selected=EqualityComparer<T>.Default.Equals(values[i],current);
                art.Button(rows,names[i],()=>Select(i),selected ? Green : Walnut,38);
            }
            if (matches.Length==0) art.Label(rows,"No matches. Try a different search.",18,Ink);
            if (matches.Length>limit) art.Button(rows,"Show more matches",()=>{limit+=40; Results();},Green);
            LayoutRebuilder.ForceRebuildLayoutImmediate(rows);
        }
        var input=art.Input(paper,"","Search "+title.ToLowerInvariant()+"…",s=>{
            query=s; limit=40; Results(); choices.verticalNormalizedPosition=1;
        });
        Position((RectTransform)input.transform,32,83,596,40);
        Results(); choices.verticalNormalizedPosition=1;
        input.onSubmit.AddListener(s=>{
            int first=Array.FindIndex(names,name=>Matches(name,s));
            if (!input.wasCanceled && first>=0 && picking) Select(first);
        });
        input.Select(); input.ActivateInputField();
    }
    private static bool Matches(string label,string query)
    {
        var words=query.Trim().Trim('(',')').Split(new[]{' ',','},StringSplitOptions.RemoveEmptyEntries);
        if (words.Length==2 && int.TryParse(words[0],out int x) && int.TryParse(words[1],out int y))
            return label.EndsWith($"({x}, {y})",StringComparison.OrdinalIgnoreCase);
        return words.All(word=>label.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0);
    }
}
