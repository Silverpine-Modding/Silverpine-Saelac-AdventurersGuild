# Adventurers’ Guild

A BepInEx plugin for Silverpine. Silverpine Modding Tools supplies the in-game menu, conversation actions, and prefab save support. Companion Party optionally gives named adventurers personal rewards and combat on loaded tiles. [Companion Party releases](https://github.com/Silverpine-Modding/Silverpine-Saelac-CompanionParty/releases/latest)

Version **1.7.0**, by **Saelac and ChatGPT**. [Download the latest release](https://github.com/Silverpine-Modding/Silverpine-Saelac-AdventurersGuild/releases/latest)

## Guild ranks

Thena describes Silverpine as a branch of the Thalassar Adventuring Guild. The board follows her public ranks. Open **Journal** to see your standing. Each notice lists the rank needed to sign and the merit it pays. The representative records the merit when you collect your reward.

| Rank | Promotion | Examples | Merit per completion |
| --- | --- | --- | --- |
| Beginner | Starting rank | Small supply orders, local parcels, nearby weak monsters | 2 |
| Intermediate | 8 total merit | Longer routes, medium supply orders, nearby cultists | 4 |
| Veteran | 24 total merit | Distant escorts and rescues, remote hunts, large supply orders | 7 |

Supply difficulty follows the requested quantity. Up to 7 items qualify as Beginner, 8–19 as Intermediate and 20 or more as Veteran. As you advance, the board can post larger orders alongside the small ones. Travel difficulty counts world-map borders from Silverpine. Four crossings normally require Intermediate and seven require Veteran. Escorts and rescues count two extra crossings. A cultist counts three extra crossings and requires at least Intermediate. The guild fixes the grade at posting time. A player's rank governs their own signatures. Hiring adventurers or funding a commission earns the player no merit.

<details><summary>Hidden guild distinction</summary>

Thena's fourth rank is **Hero**. The representative awards it at 50 total merit with at least three paid Veteran completions. Veterans already have access to the most difficult public notices.

</details>

Older worlds receive credit for previously paid player contracts in their ledger. Players can finish work they signed before the update, at any rank.

## Skills mod integration

Reference `AdventurersGuild.dll` and use the public `AdventurersGuild.GuildApi`. A small BepInEx bridge can depend on both mods so each main mod works alone. The guild plugin GUID is `silverpine.adventurersguild`. Use version 1.7.0 or later. Subscribe once on plugin load and unsubscribe on unload. Call the API on Unity's game thread.

| Hook | When it runs |
| --- | --- |
| `GuildApi.PlayerStanding` | Returns the loaded world's standing, or null outside a playable world with a charter. |
| `PlayerSigning` | Each provider receives a read-only `GuildQuest` and returns a `GuildSkillBonus` in extra gold and merit. Read skill levels here. |
| `PlayerAccepted` | The player's signature and final terms have been recorded. |
| `PlayerCompleted` | The guild has paid the player and credited their merit. Grant work-specific skill XP here. |
| `PlayerPromoted` | Follows completion when the player advances. The receipt includes the new standing. |

For example, a skills bridge can register these handlers. The two skill functions in the example belong to the consuming mod.

```csharp
using AdventurersGuild;

// Subscribe in Awake and remove both handlers in OnDestroy.
GuildApi.PlayerSigning += SigningBonus;
GuildApi.PlayerCompleted += GrantExperience;

static GuildSkillBonus SigningBonus(GuildQuest quest) =>
    quest.Objective == Work.Procurement && GetGatheringLevel() >= 5
        ? new GuildSkillBonus(gold: 4, merit: 1)
        : default;

static void GrantExperience(GuildQuest receipt) =>
    AwardSkillXpOnce(receipt.Id, receipt.Objective, receipt.Merit);
```

Snapshots include the contract ID, objective, issuer and beneficiary keys, supply or quarry, quantity, required rank, base gold, bonus gold, total merit and the player's standing. Bonuses add across providers. The guild reserves up to half the posted gold from its available treasury, and providers can add up to the contract's base merit. The signed terms survive removing the skills mod. Completion and promotion events fire for successful player payouts. Loading a save does not replay them. Store XP receipts by contract ID alongside the same world save if the skills mod persists XP separately.

## Installation

Install BepInEx 5 and Silverpine Modding Tools 1.13.0 or later. With Silverpine closed, extract the release ZIP into the game directory. For a manual installation, place `AdventurersGuild.dll` in `BepInEx/plugins/AdventurersGuild/`. Start the game and open the charter to appoint a representative. [Silverpine Modding Tools releases](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases/latest)

## Start a guild

In **Charter**, search for a resident and click **Quest giver** or **Quest target** to toggle that role. A target can receive goods or parcels, be escorted, or be named in a rescue notice. Acacia starts blocked in both roles. Enrollment as an adventurer is a separate choice. Each world remembers its settings, including an explicit decision to allow Acacia. Blocking a role withdraws affected unclaimed notices. At the counter, open **Journal** to reclaim the gold and bonus item from a withdrawn player commission. Signed contracts can still be completed.

Open **Inventory → Mods → Adventurers’ Guild → Charter** in a loaded world. Appoint a representative and enroll any residents you want available for assignments. **Mixed** gives enrolled residents the first opportunity and lets simulated adventurers take unreserved postings later. **Named** and **Simulated** limit new claims to the selected roster. Retiring a resident lets them finish their current assignment. You can change the representative while other members are away.

The guild leaves two ordinary public notices available to the player. New public jobs give you four in-game hours before a resident starts walking to the counter and twelve hours before a simulated hire can sign. Automatic signings are at least six hours apart, and simulated hires can take at most one job per 24 in-game hours across the guild. Rescue requests and player commissions can attract applicants sooner and can still be filled when only two public notices remain.

Talk to the representative and select **Guild business**. You can also build a normal Sign and write `Adventurers Guild` on it. When writing finishes, the sign takes on the market bulletin board's appearance. Structure Handler imports recognize the same inscription in the editor’s **Sign text** field as soon as the sign is placed. Read it to open the guild board, or use **Deconstruct** to remove it on your property. Hold Shift while reading to edit the sign again. Changing the inscription restores its original appearance. Reading the sign, speaking to the representative, or asking an adventurer about an assignment opens the guild directly. The inventory shortcut provides the journal and charter away from the counter.

## Contracts and rewards

During a conversation, **Guild business** and **Guild assignment** open over the current exchange. Close the guild window to hear the speaker react to the actions you took. Browsing returns you to the same conversation and preserves an unsent reply. In **Charter**, filter the resident list by name or guild role.

The representative can consult the guild ledger when you ask about a named traveler, a requester, or an assignment. For a rescue, they know who missed a check-in, the last reported tile, the original assignment when recorded, and who took the search. This knowledge comes with the office, so a newly appointed representative can discuss requests already in your save.

- **Find work.** Accept an open guild contract at the board or through the representative. The guild offers procurement, parcel delivery, wilderness surveys, hostile bounties, and escorts. A missing adventurer can generate a rescue contract.
- **Read the signature.** Each notice names its poster. Residents can request supplies for themselves, send a parcel to another resident, or ask for an escort home. The guild treasury covers these rewards. Talk to the poster about their request or open **Guild assignment** to see what they asked for and who took it. Supplies turned in for a resident go to their inventory. Any goods they cannot carry wait at the counter.
- **Complete the objective.** For a delivery, talk to the recipient and select **Guild assignment**. For a survey, visit the listed map tile. New guild bounties name a specific enemy. After accepting one, visit its hunting ground and defeat the enemy described as wanted. The assignment follows that enemy through saves and revisits and supplies a replacement if midnight clears the unloaded tile. Older generic bounties and player-posted bounties accept a hostile defeated on their designated tile. An escort or rescue requires finding the named traveler, beginning the escort through **Guild assignment**, and returning together to Silverpine. Stay within six squares of your traveling companion when crossing a border.
- **Bring a traveler home.** When you arrive together in Silverpine, an ordinary escort traveler resumes their daily routine from the arrival point. You can collect the reward later. A rescued guild adventurer resumes their interrupted assignment.
- **Collect pay.** Open **Guild business → Journal** with the representative. A procurement turn-in consumes the indicated supplies. The representative pays gold and any bonus item once. If your pack is full, make room and return.
- **Read the gold offer.** The guild prices new notices when it posts them. Count the shortest number of north, south, east, or west border crossings from Silverpine. A survey pays 16 gold plus 5 per crossing. A bounty pays 26 plus 5 per crossing, with extra hazard pay of 2 for a green ooze, 4 for a giant bat, or 12 for a cultist. An escort pays 24 plus 7 per crossing and a rescue pays 36 plus 7, reflecting the trip back with the traveler. A parcel pays 16 plus 5 per crossing to the recipient’s known surface tile at posting. Supply requests pay 10 plus the base item value and 1 gold in handling per unit, since you can deliver supplies from your pack. Player commissions use the price you enter.
- **Guild funding.** The treasury receives 120 gold at the daily board refresh. Each ordinary posting reserves its full reward. An emergency rescue can draw against future guild income so the full fee is available immediately.
- **Bounty bonuses.** Each new guild bounty draws one item from the live inventory catalog, including items registered by mods. Quality-capable items roll a native tier from Normal through Legendary. Enchantable gear with a positive base value rolls one native enchantment. The notice shows the rolled properties and the guild holds that exact item until payout. Hidden entries and the Shed Key that grants property on receipt are excluded.
- **Hire an adventurer.** At the board or counter, choose **Post**. For a supply request, choose a biome. The board and journal show the exact search tile once a named resident accepts. A reservation, player claim, or simulated hire keeps those coordinates hidden. For surveys, bounties and escorts, search for a destination by name or coordinates. Set the beneficiary and reward, and optionally reserve the posting for a guild member or add an unequipped item from your pack. **Review posting** shows the cost before payment. Confirming transfers the deposit into guild custody. A higher offer becomes available for dispatch sooner.
- **Deliver a parcel.** Choose a recipient when posting a delivery. A named courier walks to the current guild representative, collects the parcel, then follows the recipient and hands it over when they meet. The recipient remembers the delivery, the guild pays the courier, and the courier resumes their usual schedule. You can watch either handoff or let the delivery proceed while you are elsewhere. A simulated hire completes the trip on the expedition timer. If you take the job yourself, accepting it gives you the parcel as a journal objective. Go straight to the recipient, choose **Guild assignment → Deliver parcel**, then return to the representative for payment. The parcel takes no inventory slot.
- **Read a supply notice.** The gathering area is a world-map tile whose biome can produce the requested herb or ore in the current season. A player taking the job can bring matching items from any source to the representative. Named workers gather individual units over in-game time while the tile is unloaded. On a loaded tile, each matching plant they actually harvest enters the same cargo with its item properties. The journal shows the combined count.
- **Manage a commission.** Raise an open reward or withdraw an unclaimed posting. Failed player-funded contracts can be refunded from the journal. For a fulfilled supply order, release the goods to the selected beneficiary. Items that exceed the receiving inventory’s capacity stay at the counter.
- **Meet the worker.** A named adventurer uses their existing character and a temporary guild routine. They can harvest a real resource, search the area, discuss their assignment, and accept matching supplies from the player. The guild stores the world-map tile, progress, field events, and client. On arrival it chooses a reachable position in the terrain currently present. Away from the player, expeditions advance on world turns. A simulated worker has a name and a report on the board.

An enrolled resident first walks to the current representative. A sleeping applicant gets out of bed when dispatched. A sleeping representative or parcel recipient can wake to meet a nearby guild visitor, then resume their usual schedule. The notice stays open until the applicant and representative meet and sign. For a wilderness assignment, the journal then shows **Travelling to the assignment**. Their character walks across the world map, including tiles the player has never visited. Arrival starts the fieldwork clock. For a courier, the parcel handoff at the counter starts the trip to the recipient.

## Companion Party

With expedition support installed, an enrolled resident can fight nearby hostiles on a loaded quest tile with their own stats, equipment, ammunition, and essential health. They can be downed and rescued. They remain outside the player's party during a guild assignment.

Named adventurers earn gold in their personal Companion Party purse. Bonus items go into their personal pack. Both survive dismissal, recruitment, and ordinary vendor restocks. Residents can subsequently spend coins, consume supplies, or change equipment under Companion Party’s usual rules. A full pack leaves an item award waiting for space. Without Companion Party, the guild records the earnings and holds them for delivery when the integration becomes available.

## Saves and verification

The charter, contract records, held rewards, and field assignments are embedded in the native world save. Companion Party stores personal inventories and reward receipts in the matching `.moddingtools` companion file. Keep both when copying or backing up a world.

Clone the repository into `Plugin Development/AdventurersGuild` under your Silverpine installation. From the game directory, build with `dotnet build "Plugin Development/AdventurersGuild/AdventurersGuild.csproj" -c Release`. The project reads the game, BepInEx, and Modding Tools assemblies from that installation.

For the full Windows integration suite, place the Companion Party, Random Sprite Tile Fix, and Quit to Main Menu source checkouts alongside the guild project. Install Structure Handler and Quit to Main Menu in the test installation, create an `artifacts` directory in the game directory, and run `python "Plugin Development/AdventurersGuild/Tests/run_smoke.py"`. The suite runs disposable Unity players with a separate save profile.
