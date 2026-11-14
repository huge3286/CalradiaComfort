# Calradia Comfort / 舒适卡拉迪亚

Local test candidate **v0.23.0** for Mount & Blade II: Bannerlord **1.5.7**. War Sails **1.3.7** is optional. English and Simplified Chinese follow the game's text language.

| Settings group | Defaults | Features |
| --- | --- | --- |
| Convenience | On | Skip the opening story; recruit eligible common or noble troops with one click. Recruitment keeps normal costs, party capacity and relations. |
| Cheats | Off by default. Enable individually in Mod Options. | Max out clan heroes' skills and attributes, unlock both perks, earn Steam achievements with mods, share troop XP, upgrade troops to max tier in one click, set custom clan party and companion limits, or speed up friendly parties. One-click troop upgrades require Shared Troop XP and charge the normal cost for each step. |

Story skip is selected when starting a new campaign and preserves the Dragon Banner decision. With War Sails enabled it also skips the DLC's opening rescue story. The mod keeps its existing `StorySkip` module ID and story save keys. This candidate uses a separate settings profile so previously enabled cheats are not imported automatically. Turning cheats off doesn't undo gains already written to a save.

## Install for testing

1. Close Bannerlord and its launcher. Move the existing `StorySkip` folder out of the game's `Modules` folder as a backup. Use only one copy with the `StorySkip` ID.
2. Extract the candidate's `StorySkip` folder into `Modules`. Enable it with [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2859188632), [UIExtenderEx](https://steamcommunity.com/sharedfiles/filedetails/?id=2859222409), [ButterLib](https://steamcommunity.com/sharedfiles/filedetails/?id=2859232415), [MCM](https://steamcommunity.com/sharedfiles/filedetails/?id=2859238197) and the base game modules. Enable War Sails only when testing that configuration.
3. Open **Mod Options → Calradia Comfort**. Check both setting groups, then start a fresh campaign or load a **copy** of a save from the Workshop release. Use a separate save slot when testing. Saves from the full local v0.22.7 build may refer to features or items omitted from this candidate.
4. To roll back, close the game and launcher, move the candidate folder out of `Modules`, and restore the original `StorySkip` folder.

Test the English settings and recruitment screens, Convenience/Cheats defaults, new campaign and old save loading, and progression with the cheat explicitly enabled. Test with War Sails on and off if available.

On Bannerlord 1.5.7, the game started and loaded a test save in English and Simplified Chinese with War Sails disabled. Playtesting also covered story skip in a new campaign, one-click recruitment, and max hero growth. Isolated startup checks covered both DLC configurations and all ten option defaults. Loading a legacy War Sails save and earning a Steam achievement still need playtesting.

## Build from source

Use Visual Studio 2022 MSBuild and the .NET Framework 4.8 Developer Pack. The build references assemblies from your own Bannerlord installation, including War Sails for compilation, and installed Workshop dependencies. The example assumes one Steam library for both:

```powershell
$game = 'D:/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord'
$workshop = 'D:/SteamLibrary/steamapps/workshop/content/261550'
msbuild src/StorySkip.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64 "/p:GameRoot=$game" "/p:HarmonyRoot=$workshop/2859188632" "/p:UiExtenderRoot=$workshop/2859222409" "/p:McmRoot=$workshop/2859238197"
```

The module is built in `outputs/StorySkip`. Game and third-party binaries are not part of this source snapshot.

Credits: [Enable Achievement](https://github.com/admiralnelson/EnableAchievement) by AdmiralNelson and [Both Perks](https://steamcommunity.com/sharedfiles/filedetails/?id=3615340073) by Elias served as references.

## 中文说明

本地候选版 **v0.23.0** 适配《霸主》1.5.7，《战帆》1.3.7 可选；Harmony、UIExtenderEx、ButterLib 和 MCM 仍为前置。模组文字跟随游戏语言，提供英文和简体中文。

- **便利功能，默认开启：**剧情跳过、分类一键招募。招募费用、关系和部队上限按原版处理。
- **作弊功能，默认关闭：**满成长、双 Perk、允许模组下获得 Steam 成就、士兵共享经验、一键升到最高级、家族部队上限、同伴上限、己方地图加速。在菜单里按项开启。满成长不属于便利组。

安装前退出游戏和启动器，把旧 `StorySkip` 整个目录移出 `Modules` 备份，再解压候选版。启动器里只启用一份。建议新开档验证剧情跳过，并用工坊旧版的存档副本验证读档；本地完整版旧档可能引用候选版没有的物品和功能。测试另存新档，回退时换回原来的模块目录。
