# Example mods

Copy a folder into your game's `Mods` folder (or zip it and use **Add mod (.zip)…** in the mod manager).

| Mod | Shows |
|---|---|
| `BloodMoon` | text and data merging, a new enemy (prefab variant + registry entry + waves), a replaced image |
| `CrazyDave` | replacing the hero with a rigged glTF model, driven by the hero's own animations |
| `SunflowerQueen` | the same for Nobunaga with a non-humanoid model (`alignRest = no`) |
| `ZombotDrones` | a model with its own animations (`follow`, `[animations]`, `[clips]`), lasers, and turning a melee enemy into a ranged one |

## Files you need to add

The 3D models and the edited game image aren't included: they belong to Electronic Arts (Plants vs.
Zombies) and Glu (Samurai vs Zombies). Make them from your own copies with Blender 4.x and the
scripts in `work/`, then put each one next to the variant file that uses it:

| File | How to make it |
|---|---|
| `CrazyDave/Resources/Characters/CrazyDave.glb` | `blender -b --python work/crazydave/convert.py -- <crazy dave .fbx> CrazyDave.glb` |
| `ZombotDrones/Resources/Characters/Zombot.glb` | `blender -b --python work/build_zombot.py -- <engineer drone .fbx> <its diffuse .png> Zombot.glb` |
| `SunflowerQueen/Resources/Characters/SunflowerQueen.glb` | `blender -b --python work/build_queen.py -- <sunflower queen .obj> SunflowerQueen.glb` |
| `BloodMoon/Resources/Sprites/Menus/bg_start_screen_01.png` | any 1024x768 image (the example was a red-tinted copy of the game's title background) |

If a model file is missing, the loader logs an error and the game uses its own model instead. Don't
enable `ZombotDrones` without `Zombot.glb`: its data change makes the tengu shoot, and the attack
animation for that comes with the model.
