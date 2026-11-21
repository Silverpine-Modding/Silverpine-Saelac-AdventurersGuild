using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AdventurersGuild;

internal static class Guild
{
    internal const string PlayerId = "player", GuildId = "guild";
    internal static readonly string[] Goods = { "Forest Poppy", "Lavender", "Wild Garlic", "Iron Ore", "Juniper Berries" };
    private static readonly Dictionary<(Type, Season),HashSet<string>> resources = new();
    private static readonly System.Reflection.PropertyInfo ores = AccessTools.Property(typeof(WorldTileGenerator),"Ores");
    internal static readonly System.Reflection.FieldInfo ResourceName = AccessTools.Field(typeof(ResourceNode),"itemName");
    internal static bool Ready => !SerializationManager.loadingSave && Player.Instance && WorldInfoManager.Instance &&
        DungeonGenerationManager.Instance && Player.Instance.worldInventory != null && (!MainMenuUI.Instance || !MainMenuUI.Instance.IsOpen());
    internal static int Now => WorldInfoManager.Instance.TotalTurnCount;
    internal static Inventory Bag => Player.Instance.worldInventory.inventory;
    internal static string Key(NeuralNPC n) => n.TryGetComponent<CustomNPCHandler>(out var c) && !string.IsNullOrEmpty(c.customContentName)
        ? "custom:" + c.customContentName : "npc:" + n.npcName;
    internal static IEnumerable<NeuralNPC> People => NeuralNPC.neuralNPCs.Values.Where(n => n && n.isActiveAndEnabled).Distinct();
    internal static NeuralNPC Actor(string key) => key == null ? null : People.FirstOrDefault(n => Key(n) == key);
    internal static string Name(string key) => key == PlayerId ? (string.IsNullOrWhiteSpace(Player.Instance.playerName) ? "the player" : Player.Instance.playerName) :
        key == GuildId ? "Adventurers’ Guild" : Actor(key)?.GetFinalName() ?? key;
    internal static Contract Find(string id) => GuildStore.Current?.Jobs.Find(j => j.Id == id);
    internal static bool InRegion(Contract j) => DungeonGenerationManager.Instance.currentPlayerWorldTilePosition == new Vector2Int(j.X,j.Y);
    internal static bool InTown => DungeonGenerationManager.Instance.currentPlayerWorldTilePosition == Vector2Int.zero;
    internal static bool AtCounter(NeuralNPC n) => n && GuildStore.Current?.Representative == Key(n) && Near(n);
    internal static bool Near(NeuralNPC n)
    {
        if (!n) return false;
        var a = n.transform.GetVector2IntPosition();
        var b = Player.Instance.transform.GetVector2IntPosition();
        return (a-b).sqrMagnitude <= 9 && (a == b || TurfCollider.CanPositionsSeeEachOtherAdvanced(a,b));
    }
    internal static IEnumerable<WorldTile> Regions => DungeonGenerationManager.Instance.worldTiles.Values.Where(t =>
        t.position != Vector2Int.zero && !t.IsDungeonInstance() && string.IsNullOrEmpty(t.generator.centralPrefabName));
    internal static IEnumerable<WorldTile> RegionsFor(Work work,string item) => work != Work.Procurement ? Regions : Regions.Where(t => Supplies(t).Contains(item));
    private static HashSet<string> Supplies(WorldTile tile)
    {
        var key = (tile.generator.GetType(),WorldInfoManager.Instance.currentSeason);
        if (resources.TryGetValue(key,out var found)) return found;
        var entries = tile.generator.GetHerbs().Concat((List<WeightedRandomElement<GameObject>>)ores.GetValue(tile.generator));
        found = new HashSet<string>(entries.Where(e => e.weight > 0 && e.element && e.element.GetComponent<ResourceNode>())
            .Select(e => (string)ResourceName.GetValue(e.element.GetComponent<ResourceNode>())));
        resources[key] = found;
        return found;
    }

    internal static Contract Offer(Work work, WorldTile tile, string item, int quantity, int? gold, string issuer, string beneficiary)
    {
        if (work == Work.Courier) tile = null;
        else if (tile == null) throw new InvalidOperationException("No wilderness destination.");
        var job = new Contract { Work = work, Region = tile == null ? null : tile.GetDisplayName() + $" ({tile.position.x}, {tile.position.y})",
            X = tile?.position.x ?? 0, Y = tile?.position.y ?? 0, Item = item, Quantity = quantity, Issuer = issuer,
            Beneficiary = beneficiary, ClientName = Name(issuer), RecipientName = Name(beneficiary), Created = Now,
            TargetPrefab = work == Work.Bounty && issuer == GuildId ? Bounties.Choose() : null };
        job.Gold = gold ?? Quote(job,tile);
        return job;
    }

    private static int Quote(Contract job,WorldTile destination)
    {
        if (job.Work == Work.Procurement)
        {
            // The player can supply these from anywhere, including their own pack.
            var template = ItemLibrary.Items.Find(i => i != null && i.name == job.Item);
            long unitRate = 1L + Math.Max(0,template?.value ?? 0);
            return (int)Math.Min(int.MaxValue,10 + job.Quantity * unitRate);
        }
        if (job.Work == Work.Courier)
        {
            var recipient = Actor(job.Beneficiary);
            if (recipient) destination = DungeonGenerationManager.Instance.GetPositionWorldTile(recipient.transform.GetVector2IntPosition());
        }
        // Silverpine is (0,0). The player crosses world-map borders in four directions.
        // Dungeon instances have artificial coordinates outside the surface map.
        int crossings = destination == null || destination.IsDungeonInstance() ? 0 :
            Math.Abs(destination.position.x) + Math.Abs(destination.position.y);
        return job.Work switch
        {
            Work.Survey or Work.Courier => 16 + 5 * crossings,
            Work.Bounty => 26 + Bounties.HazardPay(job.TargetPrefab) + 5 * crossings,
            Work.Escort => 24 + 7 * crossings,
            Work.Rescue => 36 + 7 * crossings,
            _ => throw new ArgumentOutOfRangeException(nameof(job.Work))
        };
    }

    internal static void Stock(Charter c)
    {
        int day = WorldInfoManager.Instance.GetCurrentDay();
        if (day == c.LastBoardDay || c.Representative == null) return;
        var tiles = Regions.ToArray();
        if (tiles.Length == 0) return;
        var residents = People.Where(n => !string.IsNullOrWhiteSpace(n.GetFinalName())).Select(Key)
            .Where(k => k != c.Representative && k != "npc:None").Distinct().ToArray();
        if (c.LastBoardDay >= 0) c.Treasury = Math.Min(1500, c.Treasury + 120);
        c.LastBoardDay = day;
        foreach (var kind in new[]{Work.Procurement, Work.Survey, Work.Bounty, Work.Courier, Work.Escort})
        {
            if (c.Jobs.Any(j => j.Stage == Stage.Offered && j.Work == kind && !j.Funded)) continue;
            if (kind is Work.Courier or Work.Escort && residents.Length == 0) continue;
            var supplies = Goods.Where(g => kind != Work.Procurement || RegionsFor(kind,g).Any()).ToArray();
            if (supplies.Length == 0) continue;
            string item = supplies[Random.Range(0,supplies.Length)];
            var destinations = kind == Work.Procurement ? RegionsFor(kind,item).ToArray() : tiles;
            string issuer = GuildId, recipient = GuildId;
            if (residents.Length > 0 && kind is Work.Procurement or Work.Courier or Work.Escort)
            {
                recipient = residents[Random.Range(0,residents.Length)];
                if (kind == Work.Courier)
                {
                    var senders = residents.Where(k => k != recipient).ToArray();
                    if (senders.Length > 0) issuer = senders[Random.Range(0,senders.Length)];
                }
                else issuer = recipient;
            }
            var j = Offer(kind, kind == Work.Courier ? null : destinations[Random.Range(0,destinations.Length)], item, kind == Work.Procurement ? 3 : 1, null, issuer, recipient);
            if (c.Treasury < j.Gold) continue;
            if (kind == Work.Escort) j.Patient = recipient;
            if (kind == Work.Bounty) RollBonus(j);
            c.Treasury -= j.Gold;
            c.Jobs.Add(j);
            if (issuer != GuildId) Actor(issuer).AddThingThatHappened(j.RequestDescription());
        }
        GuildWindow.RefreshNotices();
    }

    internal static string Post(Work work, WorldTile tile, string item, int count, int gold, string recipient, string reserve, Item prize)
    {
        var c = GuildStore.Current;
        if (c?.Representative == null || count < 1 || count > 99 || gold <= 0 || Bag.gold < gold) return "Check the quantity, reward, and your purse.";
        if (work == Work.Rescue || !Goods.Contains(item) || work != Work.Courier && !RegionsFor(work,item).Contains(tile)) return "Choose an available contract and region.";
        if (work is Work.Courier or Work.Escort && (!Actor(recipient) || recipient == c.Representative)) return "Choose an eligible named recipient.";
        if (work is Work.Escort or Work.Courier && reserve == recipient) return "Choose an adventurer other than the recipient.";
        if (prize != null && (!Bag.ContainsItem(prize) || Player.Instance.IsItemEquipped(prize))) return "The bonus item is unavailable.";
        var j = Offer(work,tile,item,work == Work.Procurement ? count : 1,gold,PlayerId,recipient);
        j.Reserved = reserve;
        if (work == Work.Escort) j.Patient = recipient;
        if (prize != null) { j.PrizeName = prize.name; j.Prize = Pack(prize); Bag.RemoveItem(prize); }
        Bag.gold -= gold;
        c.Jobs.Add(j);
        string terms = prize == null ? $"{gold} gold" : $"{gold} gold and {j.PrizeName}";
        GuildWindow.RecordAction($"{j.ClientName} posted “{j.Title}” for {j.RecipientName} and deposited {terms} with the guild." +
            (reserve == null ? "" : $" The contract is reserved for {Name(reserve)}."),j);
        return "Posted. The guild holds the agreed reward.";
    }

    internal static void RollBonus(Contract j)
    {
        var pool = ItemLibrary.Items.Where(i => i != null && i.GetType() == typeof(Item) &&
            !string.IsNullOrWhiteSpace(i.name) && i.name.IndexOf("HIDDEN_",StringComparison.OrdinalIgnoreCase) < 0 &&
            !i.TryGetItemComponent<ItemComponent_ShedKey>(out _)).ToList();
        if (pool.Count == 0) return;
        var prize = pool[Random.Range(0,pool.Count)].DeepClone();
        var details = new List<string>();
        if (prize.TryGetItemComponent<IQualityItemComponent>(out var quality))
        {
            prize.RandomizeQuality(Quality.Normal,Quality.Legendary);
            details.Add(quality.Quality.ToString());
        }
        // Native enchantment valuation divides by base value. Keep valueless gear usable.
        if (prize.value > 0 && ItemComponent_Enchantment.IsEnchantable(prize))
        {
            var types = (ItemComponent_Enchantment.EnchantmentType[])Enum.GetValues(typeof(ItemComponent_Enchantment.EnchantmentType));
            var enchantment = types[Random.Range(0,types.Length)];
            prize.RemoveItemComponentsOfType<ItemComponent_Enchantment>();
            prize.AddItemComponent(new ItemComponent_Enchantment(enchantment));
            details.Add(enchantment.ToString());
        }
        string name = prize.TryGetItemComponent<ItemComponent_Gold>(out var coins) ? coins.ModifyName(prize.name) : prize.name;
        j.PrizeName = details.Count == 0 ? name : name + " (" + string.Join(", ",details) + ")";
        j.Prize = Pack(prize);
    }

    internal static bool Eligible(Contract j, string worker)
    {
        if (j.Stage != Stage.Offered || worker == j.Patient || worker == j.Issuer || j.Work == Work.Courier && worker == j.Beneficiary ||
            (j.Reserved != null && j.Reserved != worker)) return false;
        return j.Work != Work.Escort || Expeditions.Free(Actor(j.Patient));
    }

    internal static bool Accept(Contract j, string worker)
    {
        if (!Eligible(j,worker)) return false;
        bool named = worker != PlayerId && !worker.StartsWith("hire:");
        if (named && !Expeditions.CanSign(j,worker)) return false;
        j.Applicant = null;
        j.Worker = worker;
        j.WorkerName = worker.StartsWith("hire:") ? worker.Substring(5) : Name(worker);
        j.Trouble = j.Named && j.Work is not (Work.Courier or Work.Escort or Work.Rescue) && Random.Range(0,100) < 20;
        if (named && j.Work != Work.Courier) StartJourney(j);
        else StartFieldwork(j);
        var clerk = named ? Actor(GuildStore.Current.Representative) : null;
        string signature = clerk ? $" {clerk.GetFinalName()} took the signature at the guild counter." : "";
        Event(j, $"{j.WorkerName} accepted {j.Title} for {j.ClientName}." + signature, true);
        if (clerk && Key(clerk)!=j.Issuer) clerk.AddThingThatHappened(j.LastReport);
        if (j.TargetPrefab != null) Bounties.Place(j);
        return true;
    }

    internal static void StartJourney(Contract j)
    {
        j.Stage = Stage.Travelling;
        j.Due = Now + WorldInfoManager.DAY_TURN_LENGTH;
        j.NextGather = j.GatherInterval = 0;
    }

    internal static void StartFieldwork(Contract j)
    {
        j.Stage = Stage.Active;
        j.Due = Now + Random.Range(210,420);
        j.NextWork = Now + 12;
        if (j.Work == Work.Procurement && j.Worker != PlayerId) Expeditions.ScheduleGather(j);
    }

    internal static void Event(Contract j, string line, bool reported = false)
    {
        j.Events.Add(line);
        GuildWindow.RecordAction(line,j);
        if (reported)
        {
            j.LastReport = line;
            GuildWindow.RefreshNotices();
        }
        var n = Actor(j.Worker);
        if (n) n.AddThingThatHappened(line);
        if (reported && j.Issuer != GuildId && !j.Funded)
        {
            var requester = Actor(j.Issuer);
            if (requester && requester != n) requester.AddThingThatHappened(line);
        }
    }

    internal static string Cancel(Contract j)
    {
        if (!j.Funded || j.Stage is not (Stage.Offered or Stage.Failed) || j.Gold > int.MaxValue - Bag.gold) return "This posting cannot be withdrawn.";
        if (!Grant(Bag,j.Prize,j.Gold)) return "Make room for the returned reward.";
        Bag.gold += j.Gold;
        j.Stage = Stage.Cancelled; j.Applicant = null;
        GuildWindow.RecordAction($"{j.ClientName} withdrew “{j.Title}”. The guild returned {j.Gold} gold" +
            (j.Prize == null ? "." : $" and {j.PrizeName}."),j);
        return "Posting withdrawn and deposit returned.";
    }

    internal static string PlayerAction(Contract j, NeuralNPC speaker)
    {
        if (j.Worker != PlayerId || !j.Open) return "No active player assignment.";
        if (j.Work == Work.Courier && j.Stage == Stage.Active && speaker && Near(speaker) && Key(speaker) == j.Beneficiary)
        {
            j.Progress = 1; j.Stage = Stage.Returning;
            speaker.AddThingThatHappened(Player.Instance.playerName + " delivered the guild parcel to me.");
            GuildWindow.RecordAction($"{j.WorkerName} delivered the parcel from {j.ClientName} to {j.RecipientName}.",j);
            return "Parcel delivered. Report to the representative.";
        }
        if (!AtCounter(speaker)) return "Speak to the guild representative to turn this in.";
        if (j.Work == Work.Procurement && j.Stage != Stage.Returning)
        {
            var goods = Bag.items.Where(i => i.name == j.Item).Take(j.Quantity).ToList();
            if (goods.Count != j.Quantity) return "You need all the requested supplies.";
            if (j.Gold > int.MaxValue - Bag.gold) return "Your purse is full.";
            // Submit goods only once, even if the reward item has to wait at the counter.
            if (j.Beneficiary != GuildId) j.Cargo.AddRange(goods.Select(Pack).ToArray());
            foreach (var i in goods) Bag.RemoveItem(i);
            j.Progress = j.Quantity; j.Stage = Stage.Returning;
            GuildWindow.RecordAction($"{j.WorkerName} handed in {j.Quantity} × {j.Item} for {j.RecipientName}.",j);
        }
        if (j.Stage != Stage.Returning) return "The objective is still open.";
        return Settle(j);
    }

    internal static string Settle(Contract j)
    {
        if (j.Stage != Stage.Returning || j.Paid) return "Already processed.";
        if (j.Worker == PlayerId)
        {
            if (j.Gold > int.MaxValue - Bag.gold || !Grant(Bag,j.Prize,j.Gold)) return "Make room to receive the reward.";
            Bag.gold += j.Gold;
        }
        j.Paid = true;
        j.Remaining = j.Work == Work.Procurement && (j.Funded || j.Beneficiary != GuildId) ? j.Quantity : 0;
        j.Stage = j.Remaining > 0 ? Stage.Delivered : Stage.Closed;
        Event(j, $"{j.WorkerName} completed {j.Title} for {j.ClientName}. The guild awarded {j.Gold} gold" +
            (j.Prize == null ? "." : $" and {j.PrizeName}."), true);
        var rep = Actor(GuildStore.Current.Representative);
        if (rep && Key(rep) != j.Issuer) rep.AddThingThatHappened(j.LastReport);
        if (j.Stage == Stage.Delivered && !j.Funded) Collect(j);
        return j.LastReport;
    }
    internal static string Collect(Contract j)
    {
        if (j.Stage != Stage.Delivered || j.Remaining <= 0) return "Nothing to collect.";
        var bag = j.Beneficiary == PlayerId ? Bag : Actor(j.Beneficiary)?.GetComponent<WorldInventory>()?.inventory;
        if (bag == null) return "The recipient is unavailable.";
        int before = j.Remaining;
        while (j.Remaining > 0)
        {
            int index = j.Quantity - j.Remaining;
            string packed = index < j.Cargo.Count ? j.Cargo[index] : null;
            if (j.Beneficiary != PlayerId && PartyBridge.Pay != null)
            {
                packed ??= Pack(ItemLibrary.GetItemByName(j.Item,true));
                if (!PartyBridge.Pay(Actor(j.Beneficiary),j.Id+"/delivery/"+index,0,packed)) break;
            }
            else if (!(packed != null ? Grant(bag,packed) : bag.TryAddItem(ItemLibrary.GetItemByName(j.Item,true)))) break;
            j.Remaining--;
        }
        if (j.Remaining == 0) j.Stage = Stage.Closed;
        if (j.Remaining < before) GuildWindow.RecordAction($"The guild released {before-j.Remaining} × {j.Item} to {j.RecipientName}." +
            (j.Remaining == 0 ? "" : $" {j.Remaining} still await collection."),j);
        return j.Remaining == 0 ? "Delivery received by " + j.RecipientName + "." : $"{j.Remaining} still at the counter.";
    }
    internal static string Pack(Item item)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        item.Serialize(w);
        return Convert.ToBase64String(stream.ToArray());
    }
    internal static bool Grant(Inventory bag, string data,int otherGold=0)
    {
        if (string.IsNullOrEmpty(data)) return true;
        using var r = new BinaryReader(new MemoryStream(Convert.FromBase64String(data)));
        var item = new Item(); item.Deserialize(r);
        if (bag == Bag && item.TryGetItemComponent<ItemComponent_Gold>(out var coins))
        {
            coins.ModifyName(item.name); // Realize the native coin amount before the inventory credits it.
            if ((long)bag.gold + otherGold + coins.GetAmount() > int.MaxValue) return false;
        }
        return bag.TryAddItem(item);
    }
}
