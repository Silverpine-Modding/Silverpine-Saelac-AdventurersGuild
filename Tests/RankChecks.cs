using System;
using System.Collections;
using System.IO;
using System.Linq;
using BepInEx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace AdventurersGuild.Checks;

internal static class RankChecks
{
    internal static IEnumerator Exercise(Charter c,NeuralNPC rep,NeuralNPC worker)
    {
        Runner.Verify(c.Jobs.Count(j=>Guild.Eligible(j,Guild.PlayerId))>=2,"A new member has at least two signable notices on the first board");
        c.Jobs.Clear(); c.NextDispatch=int.MaxValue;
        var near=Guild.Regions.OrderBy(t=>t.position.sqrMagnitude).First();
        var far=Guild.Regions.OrderByDescending(t=>t.position.sqrMagnitude).First();
        var hard=Guild.Offer(Work.Survey,far,null,1,40,Guild.GuildId,Guild.GuildId);
        var rescue=Guild.Offer(Work.Rescue,far,null,1,40,Guild.GuildId,Guild.Key(worker));
        rescue.Patient=Guild.Key(worker);
        c.Jobs.Add(hard); c.Jobs.Add(rescue);
        Runner.MoveNear(worker);
        Runner.Verify(GuildApi.PlayerStanding.Rank==GuildRank.Beginner && hard.RequiredRank==GuildRank.Veteran &&
            !Guild.Accept(hard,Guild.PlayerId) && Expeditions.Interact(worker).Contains("Veteran") && rescue.Stage==Stage.Offered,
            "Veteran notices reject a Beginner at the board and at a rescue encounter");
        GuildWindow.Visit(null,true);
        var win=Object.FindObjectOfType<GuildWindow>();
        var card=win.GetComponentsInChildren<RectTransform>().Single(r=>r.name==hard.Title);
        Runner.Verify(!card.GetComponentInChildren<Button>() && card.GetComponentsInChildren<TextMeshProUGUI>().Any(t=>t.text.Contains("Requires Veteran")) &&
            !win.GetComponentsInChildren<TextMeshProUGUI>().Any(t=>t.text.Contains("Hero")),"Locked work is visible and the secret rank is absent from the public board");
        GuildWindow.CloseActive();
        Runner.Verify(Guild.Accept(hard,"hire:Wren"),"Player rank restrictions leave simulated adventurers eligible");
        hard.Stage=Stage.Returning; Guild.Settle(hard);
        Runner.Verify(c.PlayerMerit==0,"Hired work earns no player merit");

        var quarry=new Contract {Work=Work.Bounty, TargetPrefab="prefab_enemy_giantrat"};
        var weak=GuildRanks.Grade(quarry,1); quarry.TargetPrefab="prefab_enemy_cultist";
        Runner.Verify(weak==GuildRank.Beginner && GuildRanks.Grade(quarry,1)==GuildRank.Intermediate &&
            GuildRanks.Grade(quarry,5)==GuildRank.Veteran,"Weak bounties start at Beginner and the same cultist hunt is harder farther from town");
        var player=Guild.PlayerId;
        string repKey=Guild.Key(rep);
        var supply=Guild.Offer(Work.Procurement,far,"Lavender",1,40,Guild.GuildId,Guild.GuildId);
        c.Jobs.Add(supply);
        int calls=0,accepted=0,completed=0,promotions=0;
        GuildQuest last=null;
        GuildSkillBonus Broken(GuildQuest _) => throw new InvalidOperationException("Test skill failure");
        GuildSkillBonus Terms(GuildQuest quest) { calls++; return new GuildSkillBonus(int.MaxValue,int.MaxValue); }
        void Accept(GuildQuest _) => accepted++;
        void Paid(GuildQuest receipt) { completed++; last=receipt; }
        void Promoted(GuildQuest _) => promotions++;
        GuildApi.PlayerSigning+=Broken; GuildApi.PlayerSigning+=Terms;
        GuildApi.PlayerAccepted+=Accept; GuildApi.PlayerCompleted+=Paid; GuildApi.PlayerPromoted+=Promoted;
        try
        {
            c.Treasury=3;
            Runner.Verify(Guild.Accept(supply,player) && !Guild.Accept(supply,player) && calls==1 && accepted==1 &&
                supply.SkillGold==3 && c.Treasury==0 && supply.SkillMerit==2,"A failing skills provider cannot prevent another from quoting bounded terms at signing");
            GuildApi.PlayerSigning-=Terms; GuildApi.PlayerSigning-=Broken;
            var id=supply.Id;
            string path=Path.Combine(Paths.GameRootPath,"guild-ranks.sav");
            SerializationManager.Save(path); SerializationManager.Load(path);
            yield return null; yield return null; yield return null;
            c=GuildStore.Current; supply=Guild.Find(id); rep=Guild.Actor(repKey); Runner.MoveNear(rep);
            Runner.Verify(supply.RequiredRank==GuildRank.Beginner && supply.SkillGold==3 && supply.SkillMerit==2 && c.PlayerMerit==0,
                "The signed terms survive a native reload with the skills provider removed");
            Guild.Bag.TryAddItem(ItemLibrary.GetItemByName("Lavender",true));
            Guild.Bag.gold=int.MaxValue-40; Guild.PlayerAction(supply,rep);
            Runner.Verify(!supply.Paid && completed==0 && c.PlayerMerit==0,"A purse too full for the skill bonus cannot trigger completion or merit");
            Guild.Bag.gold=0; Guild.PlayerAction(supply,rep); Guild.PlayerAction(supply,rep);
            Runner.Verify(Guild.Bag.gold==43 && completed==1 && c.PlayerMerit==4 && last.Id==id && last.Supply=="Lavender" &&
                last.BonusGold==3 && last.Gold==40 && last.Merit==4,"Real turn-in pays the skill bonus and emits one item-specific completion receipt");
            near=Guild.Regions.OrderBy(t=>t.position.sqrMagnitude).First();
            void Finish(int quantity)
            {
                var j=Guild.Offer(Work.Procurement,near,"Lavender",quantity,5,Guild.GuildId,Guild.GuildId);
                c.Jobs.Add(j);
                if (!Guild.Accept(j,player)) throw new InvalidOperationException("Rank prevented a qualifying contract.");
                j.Stage=Stage.Returning;
                Guild.Settle(j);
            }
            Finish(3); Finish(3);
            Runner.Verify(c.Standing.Rank==GuildRank.Intermediate && c.PlayerMerit==8 && promotions==1 &&
                Plugin.Brief(rep,"").Contains("Intermediate · 8/24 merit"),"The receptionist knows the player's exact promotion and progress");
            for(int i=0;i<4;i++) Finish(8);
            Runner.Verify(c.Standing.Rank==GuildRank.Veteran && c.PlayerMerit==24 && promotions==2,"Intermediate work promotes the player to Veteran");
            Finish(20); Finish(20);
            for(int i=0;i<3;i++) Finish(8);
            Runner.Verify(c.PlayerMerit==50 && c.Standing.Rank==GuildRank.Veteran,"Merit alone cannot reveal the secret distinction");
            Finish(20);
            Runner.Verify(c.Standing.Rank==GuildRank.Hero && c.VeteranClears==3 && promotions==3,"Three Veteran clears and sufficient merit earn the secret Hero promotion");
            int before=completed;
            SerializationManager.Save(path); SerializationManager.Load(path);
            yield return null; yield return null; yield return null;
            Runner.Verify(GuildApi.PlayerStanding.Rank==GuildRank.Hero && GuildApi.PlayerStanding.Merit==57 && promotions==3 && completed==before,
                "A native reload restores standing without replaying skill or promotion events");
            c=GuildStore.Current;
            near=Guild.Regions.OrderBy(t=>t.position.sqrMagnitude).First();
            var cancelled=Guild.Offer(Work.Survey,near,null,1,40,Guild.GuildId,Guild.GuildId);
            c.Jobs.Add(cancelled); c.Treasury=100;
            GuildApi.PlayerSigning+=Terms;
            Guild.Accept(cancelled,player); Expeditions.Fail(cancelled); Expeditions.Fail(cancelled);
            GuildApi.PlayerSigning-=Terms;
            Runner.Verify(cancelled.SkillGold==20 && c.Treasury==140 && completed==before && c.PlayerMerit==57,
                "A failed player job returns its base fee and capped skill bonus once and grants no merit or experience");
            var stock=new Charter {Representative=c.Representative,PlayerMerit=57,VeteranClears=3};
            stock.BlockedTargets.UnionWith(Guild.People.Select(Guild.Key));
            var sizes=new System.Collections.Generic.HashSet<int>();
            var random=UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(711);
                for(int i=0;i<30 && sizes.Count<3;i++)
                {
                    stock.Jobs.RemoveAll(j=>j.Work==Work.Procurement);
                    stock.LastBoardDay=-1; stock.Treasury=1500; Guild.Stock(stock);
                    sizes.Add(stock.Jobs.Single(j=>j.Work==Work.Procurement).Quantity);
                }
            }
            finally { UnityEngine.Random.state=random; }
            Runner.Verify(sizes.SetEquals(new[]{3,8,20}),"Veteran boards can generate all three sizes of supply order");
            using var legacy=new MemoryStream();
            using var writer=new BinaryWriter(legacy);
            writer.Write(1); writer.Write("{\"Jobs\":[{\"Work\":2,\"X\":7,\"Worker\":\"player\",\"Paid\":true},{\"Work\":2,\"X\":7,\"Worker\":\"player\",\"Stage\":1}]}");
            writer.Flush(); legacy.Position=0; GuildStore.Instance.Deserialize(new BinaryReader(legacy));
            var old=GuildStore.Current;
            Runner.Verify(old.PlayerMerit==7 && old.Jobs.Last().Open && old.Jobs.Last().RequiredRank==GuildRank.Veteran &&
                completed==before,"An older save credits past player work and keeps signed higher-rank contracts playable");
        }
        finally
        {
            GuildApi.PlayerSigning-=Terms; GuildApi.PlayerSigning-=Broken; GuildApi.PlayerAccepted-=Accept;
            GuildApi.PlayerCompleted-=Paid; GuildApi.PlayerPromoted-=Promoted;
        }
    }
}
