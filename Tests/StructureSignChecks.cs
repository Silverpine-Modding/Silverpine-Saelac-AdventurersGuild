using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AdventurersGuild.Checks;

internal static class StructureSignChecks
{
    internal static IEnumerator Exercise()
    {
        const string marker = "Adventurers Guild";
        var transfer = AccessTools.TypeByName("StructureHandler.StructureTransfer");
        var fileType = AccessTools.TypeByName("StructureHandler.StructureFile");
        var file = Activator.CreateInstance(fileType);
        var records = (IList)AccessTools.Field(fileType, "objects").GetValue(file);
        var prefab = SerializationManager.GetPrefabFromName("prefab_furniture_sign");
        var source = Object.Instantiate(prefab, Player.Instance.transform.position, Quaternion.identity).GetComponent<Sign>();
        var cells = PathfindingEntity.FindFreeTurfsAroundPosition3x3(Player.Instance.transform.GetVector2IntPosition()).Distinct().Take(4).ToList();
        Runner.Verify(cells.Count == 4, "Four destination cells for the local structure import");
        var remoteTile = Guild.Regions.OrderBy(tile => tile.position.sqrMagnitude).First();
        cells.Add(remoteTile.position * 100 + Vector2Int.right);
        var cases = new[]
        {
            (saved: marker, edited: marker, textOnly: false, board: true),
            (saved: "", edited: "  ADVENTURERS GUILD  ", textOnly: true, board: true),
            (saved: "Garden", edited: marker, textOnly: false, board: true),
            (saved: marker, edited: "Garden", textOnly: false, board: false),
            (saved: "", edited: marker, textOnly: true, board: true),
        };
        for (int index = 0; index < cases.Length; index++)
        {
            var entry = cases[index];
            Traverse.Create(source).Field("message").SetValue(entry.saved);
            var exported = (IList)AccessTools.Method(transfer, "CreateObjectRecords")
                .Invoke(null, new object[] { new[] { source.gameObject } });
            var record = Traverse.Create(exported[0]);
            record.Field("signMessage").SetValue(entry.edited);
            if (entry.textOnly) record.Field("components").GetValue<IList>().Clear();
            record.Field("x").SetValue((float)cells[index].x);
            record.Field("y").SetValue((float)cells[index].y);
            records.Add(exported[0]);
        }
        Object.DestroyImmediate(source.gameObject);

        string path = Path.Combine(Application.persistentDataPath, "custom structures", "Guild Sign Test.json");
        File.WriteAllText(path, StringSerializationAPI.Serialize(fileType, file));
        AccessTools.Method(transfer, "ImportSelected").Invoke(null, new object[] { new[] { path } });
        var marketArt = SerializationManager.GetPrefabFromName("prefab_furniture_board_bulletin").GetComponent<SpriteRenderer>().sprite;
        var signArt = prefab.GetComponent<SpriteRenderer>().sprite;
        Sign At(int i) => Turfs.GetTurfComponent<Sign>(cells[i]);
        for (int i = 0; i < cases.Length - 1; i++)
        {
            var sign = At(i);
            Runner.Verify(sign && Traverse.Create(sign).Field("message").GetValue<string>() == cases[i].edited,
                "Structure Handler imported the final inscription for sign " + (i + 1));
            Runner.Verify(sign.GetComponent<SpriteRenderer>().sprite == (cases[i].board ? marketArt : signArt) &&
                (bool)sign.GetComponent<Deconstructable>() == cases[i].board,
                "Correct artwork and removal before first read for sign " + (i + 1));
        }
        yield return null;
        yield return null;
        Runner.Verify(At(1).GetComponent<SpriteRenderer>().sprite == marketArt && At(3).GetComponent<SpriteRenderer>().sprite == signArt,
            "Imported appearances survive Unity component startup");
        if (DialogBox.Instance.isOpen) DialogBox.Instance.CloseBox();
        Runner.Verify(!At(4), "Structure Handler queues a guild sign on an unloaded wilderness tile");
        DungeonGenerationManager.Instance.EnterWorldTile(remoteTile.position, remoteTile.position * 100);
        float deadline = Time.realtimeSinceStartup + 10;
        while (!At(4) && Time.realtimeSinceStartup < deadline) yield return null;
        Runner.Verify(At(4) && At(4).GetComponent<SpriteRenderer>().sprite == marketArt && At(4).GetComponent<Deconstructable>(),
            "The queued sign has the market board art and removal component on arrival");
        At(4).OnInteract(Player.Instance.gameObject);
        Runner.Verify(Object.FindObjectOfType<GuildWindow>(), "Reading an editor-authored imported sign opens the guild board");
    }
}
