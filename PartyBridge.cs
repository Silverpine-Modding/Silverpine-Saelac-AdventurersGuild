using System;
using System.Reflection;
using BepInEx.Bootstrap;

namespace AdventurersGuild;

internal static class PartyBridge
{
    internal const string Id = "silverpine.companionparty";
    internal static Func<NeuralNPC, bool> Unavailable = _ => false, IsDown = _ => false;
    internal static Action<NeuralNPC> Prepare = _ => { }, Assist = _ => { };
    internal static Func<NeuralNPC, string, int, string, bool> Pay;
    internal static void Bind()
    {
        if (!Chainloader.PluginInfos.TryGetValue(Id, out var info)) return;
        var api = info.Instance.GetType().Assembly.GetType("CompanionParty.ExpeditionApi");
        if (api == null) { Plugin.Log.LogWarning("Companion Party lacks the expedition API. The guild will hold member earnings."); return; }
        T Method<T>(string name) where T : Delegate => (T)api.GetMethod(name, BindingFlags.Static | BindingFlags.Public).CreateDelegate(typeof(T));
        var register = Method<Action<Func<NeuralNPC,bool>, Func<NeuralNPC,bool>>>("Register");
        Unavailable = Method<Func<NeuralNPC,bool>>("Unavailable");
        IsDown = Method<Func<NeuralNPC,bool>>("IsDown");
        Prepare = Method<Action<NeuralNPC>>("Prepare");
        Assist = Method<Action<NeuralNPC>>("Assist");
        Pay = Method<Func<NeuralNPC,string,int,string,bool>>("Pay");
        register(Expeditions.Assigned, Expeditions.CanFight);
    }
}
