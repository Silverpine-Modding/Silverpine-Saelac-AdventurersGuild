using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using Silverpine.ModdingTools;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AdventurersGuild.Checks;

internal static class Lifecycle
{
    private static T Field<T>(object owner,string name) => Traverse.Create(owner).Field(name).GetValue<T>();
    private static readonly Type Return = AccessTools.TypeByName("Silverpine.QuitToMainMenu.SessionReturn");
    private static IEnumerator Until(Func<bool> ready,string label)
    {
        float deadline = Time.realtimeSinceStartup+25;
        while (!ready())
        {
            if (Time.realtimeSinceStartup > deadline) throw new TimeoutException(label);
            yield return null;
        }
    }
    private static IEnumerator Frames(int n) { while (n-- > 0) yield return null; }
    private static IEnumerator NewGame(string name)
    {
        var menu=MainMenuUI.Instance;
        Field<Button>(menu,"newGameButton").onClick.Invoke();
        Field<TMP_InputField>(menu,"nameInputField").text=name;
        Field<Button>(menu,"finishButton").onClick.Invoke();
        yield return Until(()=>!menu.IsOpen() && !BlackScreen.Instance.IsActive(),"Complete character creation");
        yield return Frames(20);
        if (DialogBox.Instance.isOpen) DialogBox.Instance.CloseBox();
        Runner.Verify(Guild.Ready && Player.Instance.playerName == name,"Native New Game created "+name+" (actual "+Player.Instance.playerName+", ready "+Guild.Ready+")");
    }
    private static IEnumerator ToMenu()
    {
        if (!PauseMenuManager.Instance.open) PauseMenuManager.Instance.Toggle();
        InventoryUI.Instance.OnRootEnabled();
        yield return Until(()=> (bool)AccessTools.Property(Return,"CanReturn").GetValue(null),"Enable return to main menu");
        yield return (IEnumerator)AccessTools.Method(Return,"Return").Invoke(null,null);
        yield return Frames(20);
        Runner.Verify(MainMenuUI.Instance.IsOpen() && !Guild.Ready && GuildStore.Current == null && GuildApi.PlayerStanding==null,"Return removes the old charter and pauses guild work at main menu");
    }
    internal static IEnumerator Exercise()
    {
        yield return Until(()=>MainMenuUI.Instance && MainMenuUI.Instance.GetComponent<CanvasGroup>().interactable &&
            Field<List<string>>(MainMenuUI.Instance,"customContentErrors")==null,"Initialize main menu");
        yield return Frames(12);
        foreach (string id in new[]{Plugin.Id,PartyBridge.Id})
        {
            var bootstrap=Chainloader.PluginInfos[id].Instance;
            if (bootstrap) Object.Destroy(bootstrap);
        }
        yield return Frames(2);
        Runner.Verify(!Chainloader.PluginInfos[Plugin.Id].Instance && !Chainloader.PluginInfos[PartyBridge.Id].Instance,"Both original plugin components have been destroyed");
        yield return NewGame("Rowan");
        var first=GuildStore.Create();
        var worker=Guild.People.First(n=>n.npcName==NPCName.Darian);
        first.Representative=Guild.Key(worker);
        Guild.Stock(first);
        string contract=first.Jobs.First().Id;
        var supply=first.Jobs.Single(j=>j.Work==Work.Procurement);
        Runner.Verify(Guild.Accept(supply,Guild.PlayerId),"Player can sign after guild bootstrap destruction");
        supply.Stage=Stage.Returning; Guild.Settle(supply);
        Runner.Verify(PartyBridge.Pay(worker,"lifecycle-test",120,null),"Optional companion API works after bootstrap destruction");
        var runtimeType=AccessTools.TypeByName("CompanionParty.PartyRuntime");
        var old=Player.Instance.GetComponent(runtimeType);
        Runner.Verify(old,"Companion runtime created on first player");
        Object.Destroy(old);
        yield return Frames(4);
        Runner.Verify(!old && Player.Instance.GetComponent(runtimeType),"Companion runtime is recreated when its Unity component is destroyed");
        Field<TMP_InputField>(SaveUI.Instance,"saveNameInputField").text="Guild lifecycle";
        var saveType=typeof(SaveUI).GetNestedType("SaveType",BindingFlags.NonPublic);
        AccessTools.Method(typeof(SaveUI),"Save").Invoke(SaveUI.Instance,new[]{Enum.ToObject(saveType,0)});
        yield return Frames(8);
        Runner.Verify(Directory.GetFiles(Path.Combine(Application.persistentDataPath,"Saves"),"*.savmeta").Length>0,"Native save slot created in isolated profile");
        yield return ToMenu();
        var state=AccessTools.TypeByName("CompanionParty.State");
        var people=(IDictionary)FieldInfoForPeople(state);
        Runner.Verify(people.Count==0,"Returning to main menu clears old companion balances and actors");
        yield return NewGame("Aster");
        Runner.Verify(GuildStore.Current==null && Guild.Bag.gold!=120,"Second game starts independently");
        var regOk=SerializablePrefabs.TryGetRegistration("adventurersguild_charter",out var registration);
        Runner.Verify(regOk && registration.Template,"Charter prefab survives return and New Game");
        Object.Destroy(registration.Template);
        yield return Frames(2);
        Runner.Verify(GuildStore.Create().Jobs.Count==0 && SerializablePrefabs.TryGetRegistration("adventurersguild_charter",out registration) && registration.Template,
            "Lost charter template is recreated before a new world needs it");
        Runner.Verify(GuildApi.PlayerStanding.Merit==0,"A new world's player starts with a fresh guild standing");
        yield return ToMenu();
        SaveUI.Instance.Continue();
        yield return Until(()=>!MainMenuUI.Instance.IsOpen() && !SerializationManager.loadingSave && !BlackScreen.Instance.IsActive(),"Continue first save");
        yield return Frames(25);
        Runner.Verify(Guild.Find(contract)!=null && Player.Instance.playerName=="Rowan","Continue restores first charter after two menu returns");
        Runner.Verify(GuildApi.PlayerStanding.Merit==2,"The skills API follows the original player's standing after Continue");
        worker=Guild.Actor(GuildStore.Current.Representative);
        var person=AccessTools.Method(state,"Get").Invoke(null,new object[]{worker});
        Runner.Verify((int)person.GetType().GetProperty("Gold").GetValue(person)==120,"Continue restores personal gold on the restored NPC");
        Runner.Verify(Player.Instance.GetComponent(runtimeType) && InventoryModTools.TryOpen(Plugin.Id+".desk") && Object.FindObjectOfType<GuildWindow>(),
            "Party driver and registered guild menu operate in the continued game");
    }
    private static object FieldInfoForPeople(Type state)
    {
        var current=AccessTools.Field(state,"Save").GetValue(null);
        return AccessTools.Field(current.GetType(),"People").GetValue(current);
    }
}
