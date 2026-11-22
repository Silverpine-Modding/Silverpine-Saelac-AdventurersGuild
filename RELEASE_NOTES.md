# Adventurers’ Guild 1.7.0

Appoint a guild representative, post gold-backed commissions, and take requests from named residents. Enrolled adventurers sign in person and travel to their assignments. Companion Party adds personal rewards and combat on loaded tiles.

- Climb the Silverpine branch ranks described by Thena. Harder contracts earn more merit and require a higher rank to sign. The Journal tracks your standing and the receptionist recognizes promotions at payout.
- Older worlds receive merit for recorded player completions. Already signed assignments retain their original objectives and pay.
- Skills mods can read player standing, add gold or merit to a player's signed contract, and respond to acceptance, payout and promotion through the public `GuildApi`. See the integration example in the README.

- Open **Charter**, search for a resident, and toggle **Quest giver** and **Quest target** independently. Acacia starts excluded from both roles in new and existing worlds. Each save remembers your choices.
- The board and player commissions respect target exclusions for beneficiaries, parcel recipients and escort travelers. Enrolled adventurers can still take work. The guild arranges assistance privately if an adventurer excluded as a target needs rescuing.
- Blocking a role withdraws conflicting unsigned notices. The guild returns its own posting fees to the treasury. Player commissions hold their deposits for collection in the journal. Signed jobs and their rewards remain available.

Requires Silverpine with BepInEx 5 and Silverpine Modding Tools 1.13.0 or later. Extract the release ZIP into the game directory or place the standalone DLL in `BepInEx/plugins/AdventurersGuild/`. [Modding Tools releases](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases/latest)
