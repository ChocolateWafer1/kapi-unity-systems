# Kapi Unity Systems

Drop-in Unity systems made for **Kapi**, a VR Gorilla Tag–style game: text effects, glass and mirrors, a VR menu
player, physics ropes, water with swimming, and a seamless world-space floor. Each one is a `.unitypackage` you can
import into any project.

**Made for Unity 6.3 (6000.3) with URP 17.3.** Other versions may need changes.

## Download

Grab the packages from the [latest release](https://github.com/ChocolateWafer1/kapi-unity-systems/releases/latest),
or use these direct links:

| Package | What it is | Needs |
| --- | --- | --- |
| [Kapi-Text-Effects](https://github.com/ChocolateWafer1/kapi-unity-systems/releases/latest/download/Kapi-Text-Effects.unitypackage) | Stackable TextMeshPro effects: Floating Text (letters bob like they're on water) and Moving Gradient (2- or 4-color, any direction). Animates live in the editor. | TextMeshPro (built into Unity 6) |
| [Kapi-Glass-and-Mirror](https://github.com/ChocolateWafer1/kapi-unity-systems/releases/latest/download/Kapi-Glass-and-Mirror.unitypackage) | Realistic window glass (rippled, hammered, reeded, clear, flat; frost, dirt, raindrops, thick edges) and pixel-perfect planar mirrors that work in VR, the Scene view and Play mode. | URP |
| [Kapi-VR-Menu-Player](https://github.com/ChocolateWafer1/kapi-unity-systems/releases/latest/download/Kapi-VR-Menu-Player.unitypackage) | One-click VR player for menus: tracked head, sphere hands, laser pointer, satisfying menu buttons, fade-to-black scene loading, opening distortion effect. Also has **Hand Hit Feedback** (hit sound + vibration when a hand hits something). Includes Text Effects (menu buttons speed them up on hover). | Input System. Installs and sets up OpenXR, XR Plug-in Management and the XR Interaction Toolkit for you. |
| [Kapi-Rope](https://github.com/ChocolateWafer1/kapi-unity-systems/releases/latest/download/Kapi-Rope.unitypackage) | Gorilla Tag–style physics rope you can grab, swing on, climb and slide down. Braided rope materials, exact segment counts, lots of settings. Includes the VR player and Text Effects packages. | [GorillaLocomotion](https://github.com/Another-Axiom/GorillaLocomotion) `Player` in your project |
| [Kapi-Water-and-Swimming](https://github.com/ChocolateWafer1/kapi-unity-systems/releases/latest/download/Kapi-Water-and-Swimming.unitypackage) | Realistic water with waves, reflections and an underwater view, plus arm-stroke swimming, pixel splash particles, sounds and haptics. Presets: lagoon, pool, deep ocean, murky lake. Includes the rope, VR player and Text Effects packages. | [GorillaLocomotion](https://github.com/Another-Axiom/GorillaLocomotion) `Player` in your project |
| [Kapi-World-Space-Floor](https://github.com/ChocolateWafer1/kapi-unity-systems/releases/latest/download/Kapi-World-Space-Floor.unitypackage) | `Kapi/World Space Lit`: URP Lit that lays textures out in world space, so floors made of many objects line up with no seams. Comes with a Rustic Grey Oak floor material (4K color + normal map). | URP |
| [Kapi-Everything](https://github.com/ChocolateWafer1/kapi-unity-systems/releases/latest/download/Kapi-Everything.unitypackage) | All of the above in one package. | All of the above |

## Install

1. Download a `.unitypackage`.
2. In Unity: **Assets › Import Package › Custom Package…** and pick the file (or double-click it with your project open).
3. Click **Import**.

Importing several packages is fine: shared files are the same, so they just update in place.

## How to use

- **Text Effects:** add **Text Effects** (Add Component › Text Effects) to a TextMeshPro object, then add effects to its list. Effects stack, e.g. Floating Text + Moving Gradient.
- **Glass:** put one of the `Window Glass` materials on a flat object. If you can't see through it, the material shows a button to turn on URP's Opaque Texture.
- **Mirror:** put a `Mirror` material on a Quad, Plane or thin Cube. That's it; add the **Kapi/Mirror** component only to change quality settings.
- **VR Menu Player:** **Tools › Kapi VR › Add VR Player to Scene**. Add **VR Menu Button** to UI buttons for the hover/click feel. The player starts exactly where you put it and doesn't carry into the next scene. Put **Hand Hit Feedback** on each hand controller of your gameplay rig for hit sounds and vibration.
- **Rope:** **Tools › Kapi Rope › Add Rope to Scene**, then **Tools › Kapi Rope › Add Rope Climber to Gorilla Player** so you can grab it.
- **Water:** **GameObject › Kapi Water › Water** (or drag in a prefab from `KapiWater/Presets`), then **Tools › Kapi Water › Add Gorilla Swimmer To Player**.
- **World Space Floor:** put `Rustic Grey Oak Floor` on your floors. Tiling means repeats per meter; tick **Turn 90 Degrees On Floors** to run the planks the other way. Any texture works with the `Kapi/World Space Lit` shader.

## This repository

The repo is itself a small Unity project with every system in `Assets/`, so you can open it to try or change them.
After a change, rebuild the packages with **Tools › Kapi › Export Unity Packages** (they go to `Builds/`) and attach
them to a new release.

`Assets/Scripts/NewGorillaLocomotionScripts` is a copy of GorillaLocomotion, kept only so this project compiles. It
is not part of any package; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
