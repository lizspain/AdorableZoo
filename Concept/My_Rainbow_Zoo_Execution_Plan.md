# My Rainbow Zoo — Implementation Roadmap

## Current Status (as of 2026-08-24)

Phases 0–8 are implemented. **Phase 9 (Performance Pass) is next.**

| Phase | Status |
|---|---|
| 0 — Project & Environment Housekeeping | ✅ Done |
| 1 — Core Data Layer | ✅ Done |
| 2 — Habitat Base Prefab, Zoo Grid & Placement Skeleton | ✅ Done |
| 3 — Offer Tableau & Acquisition Loop | ✅ Done |
| 4 — Per-Animal Idle/Wander via NavMesh, Habitat Containment | ✅ Done |
| 5 — Input, Interactions, Shared Toy, Care Meter, Celebration | ✅ Done |
| 6 — Camera Rig | ✅ Done |
| 7 — Audio Layer | ✅ Done |
| 8 — Save System | ✅ Done |
| 9 — Performance Pass | 🟡 Partial — see notes below for what still needs your hands-on Editor/device work |
| 10 — Vertical Slice Content | 🟡 Partial — 6 placeholder `AnimalDefinition` assets (Cat, Zebra, Ostrich, Tiger, Possum, Elephant) + 1 mythical stand-in; full Cute Zoo 1–4 roster not yet authored |
| 11 — QA & Testing Pass | 🟡 Partial — Edit Mode tests exist for `ZooEconomyConfig`, `GridPlacementPlanner`, `OfferGenerator`, `SaveSystem`; no Play Mode tests yet, no moderated playtesting yet |

## Context

The design doc ([My_Rainbow_Zoo_Design_Doc_Draft.docx](My_Rainbow_Zoo_Design_Doc_Draft.docx)) fully specifies the game and its architecture (ZooManager, OfferGenerator, AnimalController, InputRouter, SaveSystem, CameraRig, AudioDirector). This roadmap breaks the doc down into an ordered sequence of implementation phases, each producing something testable in-editor, so the whole game doesn't have to be built as one big-bang integration.

This is a revised pass after review. Two foundational calls changed from the first draft based on that feedback:
- **Animal wander now uses Suriyun's NavMeshAgent-based `ControllerPetZoo`**, not a custom bounds-clamped mover — so habitats can contain navigable obstacle props (e.g. a tree an animal walks around), not just empty boxes. Physical BoxCollider walls stay as the containment mechanism regardless, for simplicity.
- **UI is built in UI Toolkit (UXML/USS)**, not uGUI/Canvas — USS's CSS-like styling is the more direct path for translating Figma design tokens (the dyed-birchwood jewel-tone palette, spacing, corner radii) into Unity.

Also folded in: a single shared Toy object (not one per habitat) that re-skins per species but is mechanically identical everywhere, a dedicated Habitat base prefab as its own step, concrete audio scene-wiring, and the starter roster is the full Cute Zoo 1–4 species list (not a curated subset). Settings/Parental Gate and real mythical-creature assets are explicitly out of scope for this plan — see the bottom section.

## Phase 0 — Project & Environment Housekeeping ✅

Get the project into a mobile-correct, buildable baseline before any gameplay code exists.

- Set `Mobile_RPAsset` as the active Render Pipeline Asset in Graphics settings (currently `PC_RPAsset` — see `ProjectSettings/GraphicsSettings.asset`).
- Import TextMeshPro Essential Resources (still useful for incidental/debug text even with UI Toolkit as the primary UI framework).
- Add the **UI Builder** package (visual UXML/USS authoring) alongside the built-in UI Toolkit runtime module.
- Add the **`com.unity.ai.navigation`** package (NavMeshSurface / runtime baking) — needed now that animal wander uses NavMeshAgent.
- Rename `productName` from "AdorableZoo" to "My Rainbow Zoo" in Player Settings.
- Create the first-party folder skeleton: `Assets/_Game/Scripts/{Core,Animals,UI,Save,Audio}`, `Assets/_Game/Data`, `Assets/_Game/UI/{UXML,USS}`.
- Create asmdefs:
  - `Game.Runtime` (`Assets/_Game/Scripts`), `Game.Editor` (`Assets/_Game/Scripts/Editor`) — neither references `Assembly-CSharp`.
  - `Vendor.PetZoo`, scoped to `Assets/Suriyun/Addon-PetZoo/Core/` only (`ControllerPetZoo.cs`, `AgentLinkMover.cs`). `Game.Runtime` references this one narrow assembly instead of blanket `Assembly-CSharp` — preserves the "don't depend on vendor code wholesale" principle while still allowing direct, clean reuse of the one vendor system we actually want.
- Remove the leftover `FreeCamera` dev fly-cam script from Main Camera in `MyRainbowZooMain.unity`.
- Adopt the `RainbowZoo.*` namespace convention for all new scripts.

**Testable output:** project opens with correct mobile shading, correct app name, TMP + UI Toolkit + NavMesh packages ready, clean scaffold, no stray dev-camera behavior.

## Phase 1 — Core Data Layer ✅

Stand up the data model everything else depends on.

- `AnimalDefinition` (ScriptableObject): prefab ref, habitat prefab ref (or base-habitat + decoration override), Animator Controller ref, VFX set, `isMythical` flag, rarity tag, toy Attachment Point transform, toy-appearance reference (mesh/material the shared Toy swaps to for this species).
- `ZooEconomyConfig` (ScriptableObject): Pet/Play/Feed heart values (+1/+2/+1), threshold formula constants, 5% mythical probability, shared Chase speed value — all global, not per-species.
- Plain serializable types: `OfferTableau` (3 candidate slots), `ZooLayoutState` (plot grid), `ZooCareMeterState` (shared heart count + threshold), `AnimalSaveState` (per-plot definition reference).
- Author 2–3 placeholder `AnimalDefinition` assets against existing Suriyun cosmetic prefabs, **plus one placeholder mythical `AnimalDefinition`** (`isMythical = true`, stand-in mesh) so the mythical-roll path is exercisable from Phase 3 onward without waiting on real mythical assets.
- First Edit Mode test: `Threshold(n) = round(10.5 + 1.4×(n−1) + 0.6×(n−1)²)` — no scene dependency, testable immediately.

## Phase 2 — Habitat Base Prefab, Zoo Grid & Placement Skeleton ✅

Prove out the physical habitat and the grid-filling algorithm before wiring real input to them.

- Author a **base Habitat prefab**: floor mesh, food dish 3D object, invisible BoxCollider containment walls, a **Toy Drop Point anchor** (fixed transform at the center of the habitat's lowest/bottom edge — where the animal sets the shared Toy down after carrying it, Phase 5), and an empty anchor point where a species-specific decoration (e.g. a tree/rock obstacle prop) can later be added. Per-species habitat variants extend this base; not required yet for the skeleton test below.
- `ZooLayoutState` fill order: expanding-square (2×2 → 3×3 → columns 4–5 top-to-bottom) within the 5×3 grid, then alternating new-column/new-row growth beyond 15 plots.
- `ZooManager` (MonoBehaviour scene singleton): the sole runtime owner/writer of `ZooLayoutState` and `ZooCareMeterState` (one-way data flow rule), plus plot→world-position mapping and Habitat prefab instantiation.
- Temporary debug trigger ("place next animal" with a placeholder definition) since the real Offer Tableau doesn't exist yet.

**Testable output:** repeatedly trigger placement, confirm habitats (floor + dish + walls visible) fill plots in the correct expanding order in the Scene view.

## Phase 3 — Offer Tableau & Acquisition Loop (first UI Toolkit screen) ✅

First real player-facing loop, and where the Figma → UI Toolkit workflow gets established.

- `OfferGenerator` (plain class): 5% mythical roll, weighted selection (owned animals get half weight) — pool already includes the Phase 1 placeholder mythical entry.
- **Spike:** establish the Figma-to-UI-Toolkit translation approach before building the screen — translate the dyed-birchwood/jewel-tone Figma tokens (colors, spacing, corner radii) into USS custom properties/classes, using Figma's Dev Mode CSS export as a reference for exact values. There's no single canonical automated Figma→UXML converter, so expect this to be a manual/semi-manual translation the first time through; document the pattern once so later screens (Settings, later phases) reuse it.
- Build the 3-slot Offer Tableau as a UXML template + USS stylesheet.
- Wire `ZooManager` to request a tableau from `OfferGenerator` on start and on `OnCareMeterComplete`; slot tap routes to Phase 2's placement.

**Testable output:** see 3 choices (including an occasional placeholder-mythical slot), tap one, watch its habitat appear in the correct plot — full acquisition loop, independent of pet/play/feed.

## Phase 4 — Per-Animal Idle/Wander via NavMesh, Habitat Containment ✅

**Architecture: NavMeshAgent-based wander, built on Suriyun's `ControllerPetZoo`.** Habitats can contain navigable obstacle props (e.g. a tree an animal walks around), which straight-line/bounds-clamped movement can't handle — NavMesh is the right tool once obstacles are in play. Physical BoxCollider walls (from the Phase 2 base prefab) remain the containment mechanism for simplicity; NavMesh handles routing within that space, not the outer boundary itself.

- Use the Suriyun `Agent-` prefab composition (NavMeshAgent + BoxCollider + `ControllerPetZoo` + `AgentLinkMover`) as the animal's actual prefab, referenced via `AnimalDefinition`.
- Each instantiated Habitat gets a `NavMeshSurface` (from `com.unity.ai.navigation`) scoped to its own local area, baked **asynchronously** at placement time — small area/low triangle count, so cost should be minor, but confirm empirically in Phase 9's profiling pass rather than assuming.
- First-party `AnimalController` (MonoBehaviour, one per placed habitat) composes with `ControllerPetZoo` (via the new `Vendor.PetZoo` asmdef reference) rather than duplicating its movement/animator-bridge logic: calls `SetDestination()` with a random point sampled from the habitat's baked NavMesh for wander, replans on arrival or on interaction interrupt, and calls `Jump()` for the heart-gain celebration.
- `ControllerPetZoo`'s trigger-zone-driven Eat/Rest detection is **not used** — interactions are tap-driven (Phase 5), so `AnimalController` sets the Eat/Rest Animator bools directly itself instead.
- `AgentLinkMover` isn't required for the vertical slice (no off-mesh links yet) but stays available in `Vendor.PetZoo` in case a later habitat variant wants a jump-gap prop.

**Testable output:** place 2–3 animals, watch each wander within its own habitat via NavMesh without crossing walls; drop a placeholder obstacle prop into one habitat and confirm the animal routes around it.

*Note: the Phase 2 baking was actually made synchronous, not async — see the comment on `ZooManager.BakeHabitatNavMesh` (correctness over micro-optimization at this stage; Phase 9 revisits async if profiling shows the cost matters).*

## Phase 5 — Input, Interactions, Shared Toy, Care Meter, Celebration ✅

The heart of the game.

- `InputRouter` (plain class, Input System): resolves touches into Pet/Play/Feed per habitat; enforces the screen-edge dead-zone and single-touch-only rule globally before any per-animal logic runs.
- Extend `AnimalController`: Rest (Pet) / Eat (Feed) / Chase-then-Pickup (Play) → Jump (heart-gain) → back to Idle/Wander.
- **Chase speed:** reuse each species' existing Move blend tree at one shared elevated speed value (`ZooEconomyConfig`'s Chase speed) — no new Animator states or per-species authoring needed; species don't need distinct run speeds.
- **Shared Toy:** one Toy GameObject for the entire zoo (Rigidbody-driven), owned by a small `ToyController` — not one per habitat. Full lifecycle for a single Play interaction:
  1. On tap-and-hold, the Toy is activated, re-skinned to the target `AnimalDefinition`'s toy-appearance reference, and **follows the touch position** (acts as the drag cursor) for the duration of the hold.
  2. On release, it's thrown into that habitat exactly like a normal physics throw (arc, bounce, settle — constrained by the habitat's BoxCollider walls).
  3. The animal Chases to it (Phase 4's NavMesh wander state swaps to Chase), picks it up, and the Toy parents to the `AnimalDefinition`'s Attachment Point for the carry.
  4. The animal carries it to the Habitat's **Toy Drop Point** (Phase 2 — center of the habitat's lowest/bottom edge), unparents, and drops it there.
  5. The Toy sits visible on the ground for **3 seconds**, then deactivates and returns to the pool — ready to be reclaimed, repositioned, and re-skinned by the next Play interaction on any habitat.
- Feed dish is already present on every Habitat (Phase 2) — wire its collider to the Feed interaction.
- `AnimalController` reports completed interactions to `ZooManager` (sole writer of `ZooCareMeterState`): applies `ZooEconomyConfig` deltas, compares to `Threshold(n)`, and on fill raises `OnCareMeterComplete`, triggers a zoo-wide Celebration (Jump on every placed animal), then requests the next tableau (loops into Phase 3).

**Testable output:** full core loop playable — tap to Pet/Play/Feed, watch the toy follow the touch while held, get thrown, get chased down and carried to the Toy Drop Point, sit for 3 seconds, and disappear; trigger Play on a different habitat next and confirm the toy re-skins correctly there; watch the shared meter climb, hit threshold, see the celebration, get a new tableau.

## Phase 6 — Camera Rig ✅

- `CameraRig` (MonoBehaviour): fixed-angle auto-zoom-to-fit up to the 5×3 ceiling, then bounded panning (4 edge pan-bars, frustum-limited) beyond that.
- All framing values — FOV/angle, zoom easing, pan-bar hit-box size, pan speed — are `[SerializeField]` Inspector fields with placeholder defaults, not hardcoded constants, so they're tunable without touching code.
- Wire to `ZooManager`'s grid-growth events.

**Testable output:** using a debug shortcut to cross the 15-plot ceiling quickly, confirm the camera transitions from auto-zoom to bounded panning at the right boundary, and confirm every framing value is editable live in the Inspector. Good point to also author the Play Mode camera-boundary tests from the QA strategy.

## Phase 7 — Audio Layer ✅

- `AudioDirector` (plain service class): music ducking under any playing SFX; single-voice SFX (a new request always preempts and starts immediately — see deviation note below).
- **Concrete scene wiring:** confirm exactly one `AudioListener` exists (Main Camera); add a dedicated "MusicPlayer" object with a looping `AudioSource` for background music; add the pooled one-shot SFX voice(s) the chaining rule needs (only one dominant voice plays at a time); add an `AudioSource` to each `AnimalController` instance for that animal's one-shot cues (purr/chirp/giggle/bark/chomp).
- All SFX triggers wired exclusively off `AnimalController`'s Animator state transitions — never raw input.
- Use placeholder/silent stand-in clips for now; real sound assets are a later content pass.

**Testable output:** with placeholder clips, confirm trigger ordering/ducking/chaining and that every source object is present and correctly assigned — doubles as an early dry-run of the QA plan's sound-off methodology.

**Deviations from the original plan, made during implementation:**
- The doc calls for SFX triggers via Animation Events/StateMachineBehaviours on the Animator states. The vendor `.controller` assets can't be safely hand-edited from code, so triggers are instead detected by polling `ControllerPetZoo.GetCurrentState()` for transitions each frame — functionally equivalent (audio can't drift from what's animating), just detected rather than authored.
- The doc's "fade out old SFX, wait 0.15s, then play new" chaining rule was removed after playtesting: it read as input lag (tap → silence → sound). `AudioDirector.PlaySfx` now cuts the previous voice and starts the new one immediately, no fade/gap.
- The zoo-wide Care Meter completion Celebration (every placed animal Jumps together) is visual-only for audio purposes — each animal's own `CelebrationSfx` is suppressed for that specific jump, and `AudioDirector.PlayTableauFanfare()` plays a single dedicated fanfare clip instead, triggered by `OfferTableauController` when the tableau actually appears. This avoids every placed animal's celebration clip layering on top of each other.

## Phase 8 — Save System ✅

- `SaveSystem` (static service class): JSON-serialize `AnimalSaveState` + `ZooCareMeterState` + `ZooLayoutState`; temp-file-then-atomic-rename write; 1-slot rolling backup.
- Autosave on every relevant state change (placement, completed interaction, threshold crossed) and on `OnApplicationPause`.
- Boot load reconstructs from save, falls back to backup, then to a fresh empty zoo on corruption.

**Testable output:** place animals, force a restart, confirm exact restoration; deliberately corrupt the primary save to verify backup fallback.

**Implementation notes:**
- Lives at `Assets/_Game/Scripts/Save/SaveSystem.cs`, saving to `Application.persistentDataPath/zoo_save.json` (+ `.backup.json` / `.tmp.json`). `SaveSystem.SaveDirectory` is a settable static property so Edit Mode tests redirect writes to a throwaway temp folder instead of the real save location.
- `ZooManager.Start()` now loads a save (if `animalRoster` is assigned) and replays placements through the normal `PlaceAnimal` path before restoring the Care Meter's exact hearts/threshold on top -- autosaving is suppressed during that replay so it doesn't trigger N redundant writes of data just loaded.
- Autosave fires after every `PlaceAnimal` and every `ReportInteractionHearts` call, plus on `OnApplicationPause(true)`.
- `AnimalSaveState` gained a parameterless constructor -- `JsonUtility` reconstructs list elements via one, and the class previously only had a 3-arg constructor (which silently removes C#'s implicit default one).
- **No in-game way to clear a save yet** -- Reset Zoo/Parental Gate is still out of scope for this plan (see bottom section). While testing, delete `zoo_save.json`/`.backup.json` directly from `Application.persistentDataPath` to start fresh.
- Edit Mode tests (`SaveSystemTests`) cover round-trip save/load, no-save-yet, and primary-corrupted-falls-back-to-backup.

## Phase 9 — Performance Pass 🟡

- GPU instancing for foliage, texture atlasing on Suriyun materials for URP mobile.
- Pool celebration/heart-gain VFX — no remaining runtime `Instantiate`/`Destroy`.
- Off-camera habitats (driven off `CameraRig`'s bounds): reduced tick rate, paused VFX, disabled containment colliders.
- **Confirm the per-habitat runtime NavMesh bake cost (Phase 4) is actually cheap in practice**, not just assumed — this is the one piece of this plan with a real, previously-unbudgeted performance question mark. (Note: Phase 4's bake ended up synchronous rather than async — see that phase's note above — so this check also covers whether that choice still holds up under load.)
- Profile on the actual test device (see device note below), targeting stable 30fps.

**Testable output:** profiler shows measurable improvement with many animals placed; a device build hits 30fps on the real test device.

**Done so far (code-level, verifiable in-Editor):**
- **Off-camera habitat simplification** — new `HabitatVisibilityLod` (one per placed habitat, checked on a 0.25s timer, not every frame): while a habitat's bounds fall outside the main camera's frustum, its containment Walls are disabled and its `AnimalController` pauses wander/audio polling and stops the NavMeshAgent (`AnimalController.SetSimplified`) rather than continuing to steer toward a destination no one can see.
- **VFX pooling** — `DebugInteractionVfx` (the only VFX system that exists today; real celebration/heart-gain VFX prefabs are still Phase 10 content) now reuses a small pool of particle-burst GameObjects instead of `Instantiate`/`Destroy` per tap.
- **GPU Instancing** — new Editor menu tool, `Rainbow Zoo > Performance > Enable GPU Instancing on Suriyun Materials`, batch-enables the `enableInstancing` flag across every material under `Assets/Suriyun`. Metadata-only, no visual change — **you need to run this once from the menu**, it isn't something that happens automatically.
- **NavMesh bake timing** — `ZooManager.BakeHabitatNavMesh` now logs each bake's actual duration (`[Perf] NavMesh bake for '...' took X.X ms.`) to the Console, so the Phase 4 synchronous-bake assumption is empirically checkable without needing the full Profiler.

**Still needs you, not me:**
- **Texture atlasing** across the Suriyun materials is a real asset-authoring task (rebuilding textures, remapping UVs) that needs visual verification in the Editor — not something safe to script blind against 60+ species' worth of existing materials.
- **Device profiling** fundamentally requires your actual test hardware; I have no way to run the Profiler or a device build myself. Worth doing once you're ready to chase the 30fps target for real.

## Phase 10 — Vertical Slice Content 🟡

- Final `AnimalDefinition` assets for **the full species roster from the Cute Zoo 1–4 packs** (Cute Pet excluded), each using the Agent-style prefab composition from Phase 4.
- Mythical creatures **stay as the Phase 1 placeholder stub** (`isMythical = true`, stand-in mesh) — sourcing and rigging real unicorn/mermaid/dragon models is explicitly out of scope for this plan; that happens after the vertical slice is playable.
- Wire the complete roster (real animals + stubbed mythical) through `OfferGenerator`'s weighting.

**Testable output:** playable start-to-finish with the real Cute Zoo 1–4 roster; the placeholder mythical creature can still appear via the 5% path and behaves identically to any other animal once placed.

**Current state:** 6 placeholder `AnimalDefinition` assets exist (Cat, Zebra, Ostrich, Tiger, Possum, Elephant) plus 1 mythical stand-in — a fraction of the full Cute Zoo 1–4 roster (60 species / 92 prefab variants per the design doc's Appendix A). Full roster authoring is still ahead.

## Phase 11 — QA & Testing Pass 🟡

- Edit Mode tests: threshold formula, offer weighting/mythical-roll probability (statistical sampling), save/backup round-trip.
- Play Mode tests: interaction-lock timing, Care-Meter-complete event firing, camera zoom/pan boundaries.
- Moderated playtesting with actual 3–6 year olds; per-animal "can you pet the dog?" checklist across the full roster.
- Sound-off passes (full playthrough muted).
- Repeat device profiling from Phase 9 against the content-complete build.
- Bug-fix/polish iteration.

**Current state:** Edit Mode tests exist for the threshold formula (`ZooEconomyConfigTests`), grid placement (`GridPlacementPlannerTests`), and offer weighting (`OfferGeneratorTests`). No save/backup tests yet (blocked on Phase 8), no Play Mode tests yet, no playtesting yet.

## Verification

Each phase lists its own testable output above — play-test in the Unity Editor after every phase rather than validating only at the end. Additionally:
- Phase 1 onward: `Threshold(n)` and later `OfferGenerator` weighting/probability get Edit Mode tests runnable via the Unity Test Runner window, independent of any scene.
- Phase 5 onward: the full core loop should be manually played in `MyRainbowZooMain.unity` after every phase touching `ZooManager`/`AnimalController`/`InputRouter`, since these are the systems every later phase builds on.
- Phase 9/11: an actual on-device build against the real test device is required to validate the 30fps target — Editor profiling alone isn't sufficient evidence. **Device note:** referred to as "iPhone 9 Pro" — Apple has no device by that exact name (closest matches are iPhone X or the iPhone SE 2nd gen, sometimes informally called "iPhone 9"); worth double-checking the exact model before the Phase 9 profiling pass so the min-spec target is unambiguous.

## Explicitly Out of Scope for This Plan

- **Settings screen & Parental Gate (incl. Reset Zoo):** deferred to a separate follow-up plan as its own UI module, after this vertical slice is implemented.
- **Real mythical-creature assets:** unicorn/mermaid/dragon stay as placeholder stubs throughout this plan (Phases 1–11); sourcing/modeling/rigging the real models is a later, separate effort once the vertical slice is playable.
