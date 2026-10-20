# Modding Samurai vs Zombies Defense (PC)

## Playing with mods

1. Put **SvZ Mod Manager.exe** in the game folder (next to `SvZ Defense Cheats.exe`) and run it,
   or run it anywhere and click **Browse…** to pick the game folder.
2. Click **Install loader** once. It adds BepInEx and the SvZ mod loader; no game file is changed.
   **Uninstall loader…** removes them again and keeps your `Mods` folder.
3. Add mods with **Add mod (.zip)…**, or copy a mod's folder into `<game>\Mods\`.
4. Tick the mods you want and set their order with **▲ Up / ▼ Down**: mods load top to bottom, and where two
   change the same thing the lower one wins.
5. For the selected mod, **Use from this mod** turns individual parts on or off: game data, text,
   textures, sounds, and models.
6. **Load mods when the game starts** off = the game starts exactly as shipped.
7. Click **Play**. Unity's *Configuration* window appears first as usual; click *Play!* there.

Choices are saved to `Mods\modlist.txt` as you make them and take effect the next time the game starts.

## Making a mod

Working examples are in [`examples\Mods`](examples) (the mods named below).

A mod is a folder in `Mods\`. Everything is optional:

```
Mods\MyMod\
  mod.txt                      name, version, author, description (shown in the manager)
  Resources\...                files named after the game's own resource paths (see below)
  Textures\<texture name>.png  replaces that texture everywhere the game uses it
  Bundles\<file>               Unity asset bundles (built with Unity 2017.4.40f1)
```

`mod.txt`:

```
name = My Mod
version = 1.0
author = Me
description = What it changes. Use \n for a new line.
```

**Finding the game's paths:** set `DumpOriginalData = true` in `BepInEx\config\svz.modloader.cfg`
and play for a bit. Every data and text file the game loads is written to `BepInEx\dump\Resources\`
as shipped. Its path there is the path your mod uses under `Resources\`.

### Game data and text (`.txt`) - merged, not replaced

A mod's `Resources\Registry\Enemies.txt` is merged into the game's: only what you write changes.

```
/ lines starting with / are comments; indentation is tabs
[H_Zombie_Warhammer]
	health = 900              / change one value of an existing enemy

[Giant_Zombie]                / a new section is added...
	_inherit = H_Zombie_Warhammer   / ...starting as a copy of an existing one
	prefab = Characters/PFZombieGiant
	health = 2500
```

- `key = value` lines override or add values; `[sections]` merge recursively.
- Bare list lines (like the enemies in a wave's `[Commands]`) are **appended** to the game's list.
- `_replace = yes` inside a section replaces the whole section instead of merging.
- A file the game doesn't have (a new wave, a new ability) is used as-is.
- Text works the same way: `Resources\Text\Default.txt` with `waveselect_inst_pc = Click to choose your battle`.

### Images and sounds - replaced

- `Resources\Sprites\Menus\bg_start_screen_01.png` replaces that game image (`.png` or `.jpg`).
- `Textures\<name>.png` replaces the texture called `<name>` on 3D models and in scenes.
- `Resources\<path>.wav` replaces a sound (8/16/24/32-bit or float PCM).

### New models and enemies - prefab variants

`Resources\Characters\PFZombieGiant.variant.txt` creates a new prefab from an existing one:

```
base = Characters/PFHzombieWarH   / existing prefab (as named in the dumped data)
scale = 1.7                       / or "x, y, z"
tint = 0.7, 1.0, 0.6              / multiplies the material colors
texture = skin.png                / optional new main texture, next to this file
```

Reference it from data (`prefab = Characters/PFZombieGiant`) to make a new enemy. A variant may also use
its base's own path (`PFHero01.variant.txt` with `base = Characters/PFHero01`) to change that character.

### Replacing a character's model with your own (`model = file.glb`)

Any rigged model exported from Blender as **glTF binary (.glb)** can replace a character. The character's
own skeleton keeps playing the game's animations (hidden), and your model's bones copy its motion.
See `Mods\CrazyDave` for a working example.

```
base = Characters/PFHero01
model = CrazyDave.glb            / next to this file
modelHeight = 1                  / optional: 1 = as tall as the original
modelRotation = 0, 180, 0        / optional: if the model doesn't face Blender's -Y (glTF +Z)

[bones]                          / original bone = your model's bone (names ignore case and "Ns:" prefixes)
	Hero_spine_low_jnt = Hips
	Hero_lf_upArm_jnt = LeftArm
	...
[attach]                         / the original bone moves onto your bone each frame, so the
	Hero_rt_wrist_jnt = RightHand  / weapon/effects the game puts there follow your model
```

- Map at least the hips, spine, neck/head, legs and arms. The topmost mapped bone (the hips) also
  carries the character's movement.
- Rest poses don't have to match (T-pose vs A-pose); each limb is lined up using the next mapped bone
  down the chain. For shapes that aren't people (a curved stem, splayed roots) add `alignRest = no`
  so your model keeps its own rest pose and just copies the motion - see `Mods\SunflowerQueen`.
- A static model (an OBJ, say) has to be rigged first. `work\build_queen.py` is a Blender script that
  places bones in the Sunflower Queen and weights her mesh to them; use it as a starting point.
- To convert another format, import it in Blender, then *File > Export > glTF 2.0*: format *glTF Binary*,
  *+Y Up*, skinning on. Only the base color texture is used.
- Bone names: the hero's are in `Mods\CrazyDave`, Nobunaga's in `Mods\SunflowerQueen`. If a name is
  wrong, the log says which bone wasn't found.

### A model with its own animations (`follow` + `[animations]`)

Instead of copying the character's skeleton, a model can play animations you made for it in Blender
(each Blender action or NLA track becomes an animation in the .glb). It moves with one bone of the
character, and the game plays your animations by name whenever it would play the character's own.
See `Mods\ZombotDrones` (made by `work\build_zombot.py`):

```
base = Characters/PFTengushingenair
model = Zombot.glb
modelHeight = 0.6
follow = Tengu_spine_low_jnt     / the model is centred on this bone and moves with it
modelOffset = 0, 10, 0           / optional nudge, in the character's units

[animations]                     / the game's animation = your model's animation
	idle = Hover
	walk = Fly
	attack01 = Shoot
	die = Crash

[clips]                          / new name = one of the character's own animations, copied
	rangedattack = attack01
```

The game's animation names for a character are listed in its prefab. Typical ones: `idle`, `walk`,
`attack01`..`attack03`, `rangedattack`, `cast`, `die`, `knockback`, `knockbackland`, `cower`, `victory`.
`[clips]` matters when you give a character a new kind of attack: ranged attacks play `rangedattack`,
and the moment the hit lands comes from that animation, so copying the melee one gives it that timing.

### Lasers

A character with a ranged attack whose `projectile = Bullet` (bullets hit instantly) can fire lasers
instead: add a `[laser]` section to its variant. The bullet is hidden and a beam is drawn from your
model's bone to the target.

```
[laser]
	from = Muzzle                / bone of your model the beam starts at
	color = 1, 0.15, 0.1
	width = 8
	duration = 0.4               / seconds the beam stays visible
```

And in `Registry\Enemies.txt`, turn the melee attacker into a ranged one:

```
[Tengu_air]
	meleeRange = 0
	bowRange = 420
	bowDamage = 60
	projectile = Bullet
```

### Asset bundles

For models with their own materials or shaders, build an asset bundle with **Unity 2017.4.40f1**
(the game's version; newer bundles won't load). Any asset under a folder named `Resources` inside the
bundle answers that path: `Assets/MyMod/Resources/Characters/PFBoss.prefab` is `Characters/PFBoss`.

## Loader settings (`BepInEx\config\svz.modloader.cfg`)

| Setting | Default | |
|---|---|---|
| `ModsFolder` | `Mods` | where mods live, relative to the game folder |
| `DumpOriginalData` | `false` | write the game's data/text files to `BepInEx\dump\` |
| `LogOverrides` | `true` | log every merge and replaced asset to `BepInEx\LogOutput.log` |
| `AutoScreenshotSeconds` | `0` | debugging: save a screenshot every N seconds |
| `RunInBackground` | `false` | debugging: keep running when the window isn't focused |

**Something not working?** `BepInEx\LogOutput.log` lists the mods loaded, which parts are turned off,
every file merged and every asset replaced, plus any errors.

## Building from source

See the README for setup (a copy of the game in `game\`, BepInEx's zip in `downloads\`).

- `SvZModLoader\` - the BepInEx plugin (.NET 3.5, compiled against the game's own DLLs in `game\`).
  `dotnet build` also copies it into `game\BepInEx\plugins`.
- `SvZModManager\` - the manager (.NET 8 WinForms). It builds the loader and embeds it together with
  the BepInEx zip from `downloads\`.
  Publish: `dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish`
- `Shared\ModLayout.cs` - the mod folder layout and `modlist.txt` format, compiled into both.
- `devtools\SvZTestDriver\` - test-only plugin for scripted taps/screenshots; never ship it.
