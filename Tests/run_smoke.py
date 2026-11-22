"""Exercise guild contracts with optional CompanionParty present and absent in disposable game copies."""
from pathlib import Path
import os
import shutil
import subprocess
import sys
import tempfile
import time

PROJECT = Path(__file__).resolve().parents[1]
ROOT = PROJECT.parents[1]
sys.path.insert(0, str(PROJECT.parent / "CompanionParty" / "Tests"))
from run_smoke import copy_tree

def run():
    isolation=PROJECT.parent/"QuitToMainMenu/Tests/Isolation"
    sprite_fix=PROJECT.parent/"RandomSpriteTileFix"
    for proj in (PROJECT / "Tests/Checks.csproj", PROJECT.parent / "CompanionParty/CompanionParty.csproj", sprite_fix/"RandomSpriteTileFix.csproj", isolation/"Isolation.csproj"):
        subprocess.run(["dotnet", "build", str(proj), "-c", "Release", "--nologo"], check=True)
    base = ROOT / "artifacts"
    previous = os.environ.get("GUILD_QA_REUSE")
    directory = (base / previous).resolve() if previous else Path(tempfile.mkdtemp(prefix="GuildQA-", dir=base))
    if directory.parent != base.resolve() or not directory.name.startswith("GuildQA-") or previous and not (directory/"guild-check-only").exists():
        raise ValueError("Not a disposable guild test player")
    print(directory, flush=True)
    for name in ("Silverpine.exe","UnityPlayer.dll","winhttp.dll","doorstop_config.ini"):
        shutil.copy2(ROOT/name, directory/name)
    for name in ("MonoBleedingEdge","Silverpine_Data/Managed","BepInEx/core","Silverpine_Data/Plugins","Silverpine_Data/Resources"):
        if not (directory/name).exists(): copy_tree(ROOT/name,directory/name)
    for path in (ROOT/"Silverpine_Data").iterdir():
        if path.is_file() and not (directory/"Silverpine_Data"/path.name).exists():
            try:
                os.link(path,directory/"Silverpine_Data"/path.name)
            except OSError:
                shutil.copy2(path,directory/"Silverpine_Data"/path.name)
    plugins=directory/"BepInEx/plugins"
    if not (plugins/"ModdingTools").exists(): copy_tree(ROOT/"BepInEx/plugins/ModdingTools",plugins/"ModdingTools")
    for output in (PROJECT/"bin/Release/netstandard2.1/AdventurersGuild.dll", sprite_fix/"bin/Release/netstandard2.1/RandomSpriteTileFix.dll",
                   PROJECT/"Tests/bin/Release/netstandard2.1/AdventurersGuild.Checks.dll"):
        shutil.copy2(output,plugins)
    (directory/"guild-check-only").touch()
    (directory/"quit-to-menu-test.instance").touch()
    patchers=directory/"BepInEx/patchers"
    patchers.mkdir(exist_ok=True)
    shutil.copy2(isolation/"bin/Release/netstandard2.1/QuitToMenuTestIsolation.dll",patchers)
    (directory/"Silverpine_Data/StreamingAssets").mkdir(exist_ok=True)
    for mode in ("guild-ranks","role-exclusions","escort-return","guild-conversation","guild-board","standalone","companion","menu-lifecycle","structure-import"):
        if len(sys.argv)>1 and mode not in sys.argv[1:]: continue
        optional = (
            (PROJECT.parent/"CompanionParty/bin/Release/netstandard2.1/CompanionParty.dll", mode in ("companion","menu-lifecycle")),
            (ROOT/"BepInEx/plugins/QuitToMainMenu/QuitToMainMenu.dll", mode == "menu-lifecycle"),
            (ROOT/"BepInEx/plugins/StructureHandler/StructureHandler.dll", mode == "structure-import"),
        )
        for dll, enabled in optional:
            if enabled:
                shutil.copy2(dll,plugins)
            else:
                (plugins/dll.name).unlink(missing_ok=True)
        report=directory/"guild-results"
        report.unlink(missing_ok=True)
        env=os.environ.copy()
        cmd=[str(directory/"Silverpine.exe"),"-batchmode","-logFile",str(directory/"unity.log")]
        if mode in ("guild-board","guild-conversation","menu-lifecycle","role-exclusions","guild-ranks"):
            cmd.extend(["-guild-lifecycle" if mode=="menu-lifecycle" else "-"+mode,"-screen-width","1280","-screen-height","800","-screen-fullscreen","0"])
        else: cmd.append("-nographics")
        if mode=="structure-import": cmd.append("-guild-structure-import")
        if mode=="escort-return": cmd.append("-guild-escort-return")
        with subprocess.Popen(cmd,
                cwd=directory,env=env,creationflags=getattr(subprocess,"CREATE_NO_WINDOW",0)) as game:
            until=time.monotonic()+150
            while not report.exists() and game.poll() is None and time.monotonic()<until:
                time.sleep(.3)
            if game.poll() is None: game.kill()
            game.wait()
        result=report.read_text(encoding="utf-8-sig") if report.exists() else "No test report"
        print(mode,result,flush=True)
        if not result.strip().endswith("PASS"):
            raise RuntimeError(f"Inspect {directory} for logs")
        log=(directory/"unity.log").read_text(encoding="utf-8",errors="replace")
        if "No such key in VersionSafeStorage: silverpine.adventurersguild.bounty" in log or (
            "An item with the same key has already been added" in log and "RandomSprite.Awake" in log
        ):
            raise RuntimeError(f"Tile or bounty restore regression in {directory/'unity.log'}")
        if mode=="guild-board":
            shutil.copy2(directory/"guild-board.png",base/"GuildBoardPreview.png")
    if directory.resolve().parent == base.resolve() and directory.name.startswith("GuildQA-"):
        shutil.rmtree(directory)

if __name__=="__main__":
    run()
