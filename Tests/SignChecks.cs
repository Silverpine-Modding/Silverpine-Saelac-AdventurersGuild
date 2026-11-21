using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AdventurersGuild.Checks;

internal static class SignChecks
{
    internal static IEnumerator Exercise()
    {
        var prefab = SerializationManager.GetPrefabFromName("prefab_furniture_sign");
        var signArt = prefab.GetComponent<SpriteRenderer>().sprite;
        var marketArt = SerializationManager.GetPrefabFromName("prefab_furniture_board_bulletin").GetComponent<SpriteRenderer>().sprite;
        var plot = Plot.allPlots.First(p => p.owners.Count > 0 && p.bounds.size.z > 0);
        var owner = plot.owners.First();
        bool temporarilyOwned = WorldInfoManager.Instance.ownedPlots.Add(owner);
        var board = Object.Instantiate(prefab, plot.bounds.min, Quaternion.identity).GetComponent<Sign>();
        var renderer = board.GetComponent<SpriteRenderer>();
        board.OnInteract(Player.Instance.gameObject);
        var root = Traverse.Create(DialogBox.Instance).Field("optionsRoot").GetValue<GameObject>();
        float until = Time.realtimeSinceStartup + 5;
        Button edit;
        while (!(edit = root.GetComponentsInChildren<Button>().FirstOrDefault(b => b.GetComponentInChildren<TextMeshProUGUI>()?.text == "Edit"))
            && Time.realtimeSinceStartup < until) yield return null;
        Runner.Verify(edit, "Player can edit a sign on owned property");
        edit.onClick.Invoke();
        var input = Traverse.Create(TextInputUI.Instance).Field("inputField").GetValue<TMP_InputField>();
        input.text = "  adventurers GUILD  ";
        input.onSubmit.Invoke(input.text);
        Runner.Verify(renderer.sprite == signArt, "Sign keeps its original art while the player writes");
        FinishInteraction();
        Runner.Verify(signArt != marketArt && renderer.sprite == marketArt && SerializationManager.GetAssociatedPrefab(board.gameObject) == prefab
            && !board.GetComponent<MarketBulletinBoard>(), "Finishing the edit changes the original sign's sprite to the market bulletin board sprite");

        var blob = SerializationManager.SerializeSerializable(new[] { board.gameObject });
        var loaded = SerializationManager.DeserializeSerializable(blob).Single().GetComponent<Sign>();
        var loadedArt = loaded.GetComponent<SpriteRenderer>();
        Runner.Verify(loadedArt.sprite == marketArt && loaded.GetComponent<Deconstructable>().playerPropertyOnly,
            "Native object save/load restores the art and property removal action before the sign is read");
        using var bytes = new MemoryStream();
        var writer = new BinaryWriter(bytes);
        board.Serialize(writer);
        writer.Flush();
        loaded.GetComponent<Deconstructable>().OnInteract(Player.Instance.gameObject);
        FinishInteraction();
        Runner.Verify(!loaded && board && renderer.sprite == marketArt, "Native Deconstruct removes only the selected guild sign");
        board.OnInteract(Player.Instance.gameObject);
        Runner.Verify(Object.FindObjectOfType<GuildWindow>(), "Reading the sign with the market art opens the guild window");

        var ordinary = Object.Instantiate(prefab).GetComponent<Sign>();
        using var replacement = new MemoryStream();
        var storage = new VersionSafeStorage();
        storage.AddToStorage("message", "Garden");
        storage.Serialize(new BinaryWriter(replacement));
        replacement.Position = 0;
        board.Deserialize(new BinaryReader(replacement));
        Runner.Verify(renderer.sprite == signArt && !board.GetComponent<Deconstructable>() && !board.GetComponent<Targetable>()
            && ordinary.GetComponent<SpriteRenderer>().sprite == signArt, "Changing the inscription restores the original sign and leaves other signs alone");
        var existing = ordinary.gameObject.AddComponent<Deconstructable>();
        existing.playerPropertyOnly = false;
        bytes.Position = 0;
        ordinary.Deserialize(new BinaryReader(bytes));
        Runner.Verify(!string.IsNullOrEmpty(ordinary.GetComponent<NPCVisibleObject>().GetSimplified()),
            "The native removal chooser can label a marked sign sharing a tile with other furniture");
        replacement.Position = 0;
        ordinary.Deserialize(new BinaryReader(replacement));
        Runner.Verify(ordinary.GetComponent<Deconstructable>() == existing && !existing.playerPropertyOnly
            && ordinary.GetComponent<Targetable>() && !ordinary.GetComponent<NPCVisibleObject>(),
            "A sign with its own removal component keeps the original rules through marking and unmarking");
        Object.DestroyImmediate(ordinary.gameObject);
        Object.DestroyImmediate(board.gameObject);
        if (temporarilyOwned) WorldInfoManager.Instance.ownedPlots.Remove(owner);
    }

    private static void FinishInteraction()
    {
        var effects = Player.Instance.statusEffectsTarget;
        var action = effects.statusEffects.OfType<StatusEffect_Interacting>().Single();
        effects.ReduceRemainingTurns(action, action.remainingTurns);
    }
}
