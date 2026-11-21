using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AdventurersGuild;

// Sign has no text-changed event. Its player edit callback, asynchronous NPC
// edit, and deserializer all commit to the same field.
[HarmonyPatch]
internal static class GuildSignAppearance
{
    private static readonly FieldInfo Message = AccessTools.Field(typeof(Sign), "message");
    private static readonly AccessTools.FieldRef<Sign, string> Text = AccessTools.FieldRefAccess<Sign, string>(Message);
    private static readonly ConditionalWeakTable<Sign, Original> Marked = new();

    private sealed class Original
    {
        internal Sprite Sprite;
        internal Deconstructable Removal;
        internal Targetable Target;
        internal NPCVisibleObject Label;
    }

    internal static bool IsMarker(string value) =>
        string.Equals(value?.Trim(), "Adventurers Guild", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<MethodBase> TargetMethods()
    {
        var types = typeof(Sign).GetNestedTypes(AccessTools.all).Prepend(typeof(Sign));
        return types.SelectMany(AccessTools.GetDeclaredMethods).Where(m => m.GetMethodBody() != null &&
            PatchProcessor.ReadMethodBody(m).Any(i => i.Key == OpCodes.Stfld && Equals(i.Value, Message)));
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var write = AccessTools.Method(typeof(GuildSignAppearance), nameof(Write));
        foreach (var instruction in instructions)
        {
            if (instruction.StoresField(Message))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = write;
            }
            yield return instruction;
        }
    }

    private static void Write(Sign sign, string value)
    {
        Text(sign) = value;
        Refresh(sign, value);
    }

    internal static void Refresh(Sign sign) => Refresh(sign, Text(sign));

    internal static void Refresh(Sign sign, string value)
    {
        bool marked = IsMarker(value);
        if (Marked.TryGetValue(sign, out var original))
        {
            if (marked) return;
            sign.gameObject.GetSpriteRendererExtended().sprite = original.Sprite;
            if (original.Removal) Object.DestroyImmediate(original.Removal);
            if (original.Target) Object.DestroyImmediate(original.Target);
            if (original.Label) Object.DestroyImmediate(original.Label);
            Marked.Remove(sign);
            return;
        }
        if (!marked) return;

        var renderer = sign.gameObject.GetSpriteRendererExtended();
        var market = SerializationManager.GetPrefabFromName("prefab_furniture_board_bulletin");
        var art = market ? market.GetSpriteRendererExtended() : null;
        if (!renderer || !art || !art.sprite) return;
        original = new Original { Sprite = renderer.sprite };
        Marked.Add(sign, original);
        renderer.sprite = art.sprite;

        // The native deconstruction chooser reads this concrete label when
        // several targets occupy one square.
        if (!sign.TryGetComponent<NPCVisibleObject>(out _))
        {
            original.Label = sign.gameObject.AddComponent<NPCVisibleObject>();
            original.Label.description = "an adventurers' guild board";
        }
        // Debug-spawned and modded signs may already supply their own rules.
        if (sign.TryGetComponent<Deconstructable>(out _)) return;
        if (!sign.TryGetComponent<Targetable>(out _)) original.Target = sign.gameObject.AddComponent<Targetable>();
        original.Removal = sign.gameObject.AddComponent<Deconstructable>();
        original.Removal.playerPropertyOnly = true;
    }
}

// Importers can assign the inscription directly before placing the sign.
[HarmonyPatch(typeof(TurfRegistrar), nameof(TurfRegistrar.Register))]
internal static class PlacedGuildSign
{
    private static void Postfix(TurfRegistrar __instance)
    {
        if (!__instance.preventRegistration && __instance.TryGetComponent<Sign>(out var sign))
            GuildSignAppearance.Refresh(sign);
    }
}
