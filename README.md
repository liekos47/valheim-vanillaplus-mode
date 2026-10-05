# Valheim Vanilla Plus (BepInEx plugin)

Client-side convenience for the unmodded game. No cheats: nothing here changes what your character
can do, what the world gives you or what gets saved. Every feature is something you could do by
hand in vanilla, with fewer clicks. Because of that there is no admin / host requirement.

## Build & install

```
dotnet build -c Release
```

The build copies `ValheimVanillaPlus.dll` into `Valheim\BepInEx\plugins\ValheimVanillaPlus\`.
If Valheim is installed elsewhere: `dotnet build -c Release -p:ValheimDir="D:\...\Valheim"`.

## Hotkeys (in game)

| Key | Toggle |
|-----|--------|
| Insert | **Options menu**: theme, search and tabs (frees the cursor, blocks player input while open) |

Defaults and keys are editable in `BepInEx\config\local.valheimvanillaplus.cfg`, or from the menu's
More tab (Open config file / Reload config file).

## Features

- **Clear button** – a "Clear" button in the game's "Enter text" box (signs, names, portal tags),
  left of Cancel, that empties the field and keeps it focused. `[Interface] ClearButton`, default on.

## Layout

| Folder | What lives there |
|--------|------------------|
| `Core/` | The plugin (`VanillaPlusPlugin.cs`: config entries, update loop), the options menu (`MenuWindow.cs`, `MenuTheme.cs`) and input blocking while typing in it (`SearchInputBlock.cs`) |
| `Interface/` | Changes to the game's own screens |

Adding a feature: one file in the folder for its area, a `ConfigEntry` bound in
`VanillaPlusPlugin.Awake`, and a `Toggle(...)` line in the matching tab of `MenuWindow.cs`.

## How it works

- **Clear button** – `TextInput.Show` postfix clones the box's Cancel button (same look), sizes it
  like OK, places it one button-step to the left of Cancel, relabels it "Clear" and replaces its
  click with: empty `m_inputField`, re-focus it. Hidden again when the option is turned off. If
  Valheim God Mode is installed too, its own Clear button is used and this one stays hidden.
- **Menu** – IMGUI window drawn from `OnGUI`. While open, `GameCamera.UpdateMouseCapture` is
  skipped (cursor free) and `Player.TakeInput` / `PlayerController.TakeInput` return false. While
  the search box has focus, `ZInput` key / button reads return false so typing doesn't trigger
  game keys.

## After a Valheim update

1. Rebuild against the new game DLLs (`dotnet build -c Release`) and fix any compile errors.
2. Open a sign's text box and check the Clear button is there, in line with Cancel / OK.
