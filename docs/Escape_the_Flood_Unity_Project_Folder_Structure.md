# Escape the Flood – Unity Project Folder Structure

This document defines the folder structure for the **Escape the Flood** Unity project. It explains the purpose of each folder and subfolder, gives examples of the files stored there, and identifies the primary team member responsible for each area.

The goal is to keep project assets organized, make responsibilities clear, and help every team member find and manage files easily.

All project-specific files should be stored inside `Assets/Project/`. Third-party asset packs (`LowPolyInteriorsFree`, `Male3`, `Alstra Infinite`) stay in their own folders at the top of `Assets/`.

## 1. Complete folder structure

```text
Assets/
│
└── Project/
    │
    ├── Scenes/
    │   ├── GameMenu.unity
    │   ├── Main.unity
    │   └── TestScenes/
    │       ├── GameCore_Test.unity
    │       ├── Environment_Test.unity
    │       ├── Environment.unity
    │       ├── Demo_FlatCompact.unity
    │       ├── TestingBuilding.unity
    │       ├── Basement_Blockout.unity
    │       ├── Water_Test.unity
    │       ├── Shark_Test.unity
    │       └── Player_Test.unity
    │
    ├── Scripts/
    │   ├── GameCore/
    │   ├── Player/
    │   ├── Environment/
    │   ├── Water/
    │   ├── Enemy/
    │   ├── Rescue/
    │   └── UI/
    │
    ├── Prefabs/
    │   ├── Player/
    │   ├── Environment/
    │   ├── Water/
    │   ├── Enemy/
    │   ├── Rescue/
    │   └── UI/
    │
    ├── Materials/
    │   ├── Environment/
    │   ├── Water/
    │   ├── Player/
    │   └── Enemy/
    │
    ├── Models/
    │   ├── Characters/
    │   ├── Environment/
    │   ├── Furniture/
    │   └── Props/
    │
    ├── Textures/
    │   ├── Environment/
    │   ├── Water/
    │   └── Characters/
    │
    ├── Audio/
    │   ├── Music/
    │   ├── SFX/
    │   └── Ambient/
    │
    └── UI/
        ├── Fonts/
        ├── Icons/
        └── Sprites/
```

## 2. Folder explanations

### A. `Scenes/` — Game levels and environments

**Primary owner: Member 1 – Game Core & Integration**

| File / folder | Responsibility |
|---|---|
| `GameMenu.unity` | Main menu where players start the game (build index 0). |
| `Main.unity` | Main gameplay scene containing the building, player, water, shark, rescue pad, and the Pause / Game Over / Victory panels (build index 1). |
| `TestScenes/` | Separate scenes where members develop and test features without affecting the main game. Not included in the build. |
| `GameCore_Test.unity` | Test game logic, player, camera, and game states. |
| `Environment_Test.unity`, `Environment.unity`, `Demo_FlatCompact.unity`, `TestingBuilding.unity`, `Basement_Blockout.unity` | Test building structures, furniture, and platforms. |
| `Water_Test.unity` | Test rising water and underwater mechanics. |
| `Shark_Test.unity` | Test shark AI. |
| `Player_Test.unity` | Test player movement. |

Game Over and Victory are UI panels inside `Main.unity` (controlled by `GameManager`), not separate scenes.

**Important:** Each member should work in their own test scene. Member 1 manages the final integration of `Main.unity`.

### B. `Scripts/` — C# programming logic

**Ownership: Each member owns their assigned system.**

| Folder | Responsibility | Example scripts |
|---|---|---|
| `GameCore/` | Controls the overall game flow and states. | `GameManager.cs`, `GameState.cs`, `SceneLoader.cs` |
| `Player/` | Handles player movement, health, and animation. | `PlayerController.cs`, `PlayerHealth.cs`, `PlayerMovement.cs`, `PlayerAnimation.cs` |
| `Environment/` | Handles interactive furniture and platforms. | `FloatingPlatform.cs`, `CrumblingPlatform.cs`, `PlatformSpawner.cs` |
| `Water/` | Controls rising water and underwater mechanics. | `WaterController.cs`, `WaterRise.cs`, `UnderwaterDetector.cs`, `BreathController.cs` |
| `Enemy/` | Controls shark behavior and attacks. | `SharkAI.cs`, `SharkPatrol.cs`, `PlayerDetection.cs`, `SharkAttack.cs` |
| `Rescue/` | Handles helicopter and player rescue mechanics. | `HelicopterController.cs`, `RescueLadder.cs`, `RescueTrigger.cs` |
| `UI/` | Controls the game interface and player information. | `MainMenuUI.cs`, `HealthUI.cs`, `BreathUI.cs`, `GameOverUI.cs` |

**Examples:**
- `WaterRise.cs` makes the water level increase.
- `PlayerMovement.cs` allows the player to move.
- `SharkAI.cs` controls shark behavior.

### C. `Prefabs/` — Reusable GameObjects

**Ownership: Each member creates prefabs for their system.**

| Folder | Responsibility | Examples |
|---|---|---|
| `Player/` | Reusable player objects. | Player and player camera setup. |
| `Environment/` | Reusable building and environment objects. | Walls, floors, sofa, table, bookshelf, platforms. |
| `Water/` | Reusable water objects. | Water surface and water volume. |
| `Enemy/` | Reusable enemy objects. | Shark and shark spawn point. |
| `Rescue/` | Reusable rescue objects. | Helicopter, ladder, rescue zone. |
| `UI/` | Reusable interface components. | Health bar, breath bar, buttons, panels. |

A **Prefab** is a reusable GameObject that can be configured once and placed multiple times in scenes.

For example, create one sofa prefab and reuse it in different rooms without rebuilding it.

### D. `Materials/` — Object appearance

| Folder | Responsibility | Examples |
|---|---|---|
| `Environment/` | Materials for buildings and furniture. | Wood, concrete, metal, glass. |
| `Water/` | Materials for water appearance. | Blue water and transparent water. |
| `Player/` | Materials for the player. | Player clothing and skin materials. |
| `Enemy/` | Materials for enemies. | Shark skin and eyes. |

Materials define how an object looks, including its color, smoothness, transparency, and surface properties.

For example, apply a wood material to a table to make it look wooden.

### E. `Models/` — 3D objects and meshes

| Folder | Responsibility | Examples |
|---|---|---|
| `Characters/` | 3D character models. | Player and shark. |
| `Environment/` | Building and structural models. | Walls, doors, windows, stairs. |
| `Furniture/` | Furniture models. | Sofa, table, bookshelf, chair, bed. |
| `Props/` | Small decorative or interactive objects. | Boxes, bottles, wood pieces, lamps. |

Models contain the actual 3D shapes of objects, usually imported from tools such as Blender or downloaded as `.fbx` files.

### F. `Textures/` — Images applied to 3D objects

| Folder | Responsibility | Examples |
|---|---|---|
| `Environment/` | Textures for buildings and furniture. | Wood texture, concrete texture, wall texture. |
| `Water/` | Textures for water effects. | Water surface texture and water normal map. |
| `Characters/` | Textures for characters. | Player clothing texture and shark skin texture. |

Textures are image files used by materials to add surface details to 3D objects.

For example, a plain cube can look like a wooden box after a wood texture is applied through a material.

### G. `Audio/` — Sound and music

| Folder | Responsibility | Examples |
|---|---|---|
| `Music/` | Background music. | Main menu music and gameplay background music. |
| `SFX/` | Sound effects triggered by actions. | Water splash, footsteps, object falling, shark attack. |
| `Ambient/` | Environmental background sounds. | Rain, wind, storm, flooding atmosphere. |

This folder stores all audio files used in the game.

### H. `UI/` — Interface resources

| Folder | Responsibility | Examples |
|---|---|---|
| `Fonts/` | Fonts used in the game interface. | Main menu font and gameplay font. |
| `Icons/` | Small interface symbols. | Health icon, water icon, settings icon. |
| `Sprites/` | 2D images used in the interface. | Button backgrounds, panel images, logos. |

This folder stores visual resources for the game's user interface, rather than the scripts that control it.

## 3. Team member responsibilities

The following table defines the recommended ownership for the four-member team.

### Work responsibility distribution

**Member 1 – Game Core & Integration (Mr. Kheng Sokthearith)** — Integration lead, responsible for the overall game architecture.
- `GameManager`
- Game states (Playing, Victory, Game Over)
- Player movement and health/breath integration
- Main game loop
- Scene integration and testing

**Member 2 – Environment & Platform (Mr. Yeat David)** — Responsible for building the game world.
- Building interior and floors
- Basement, living room, bedrooms, attic and rooftop
- Furniture 3D assets
- Platform collision
- Floating and crumbling furniture

**Member 3 – Water System (Mr. Thang Saoly)** — Responsible for the flood mechanics.
- `WaterBody`
- Continuously rising water
- Water surface and visual effects
- Underwater detection
- Player breath depletion
- Water collision boundaries

**Member 4 – Enemy & Rescue System (Mr. Tiek Chhunhour)** — Responsible for hazards and the escape objective.
- Shark 3D model
- Shark patrol AI
- Player detection and attack
- Helicopter and rescue ladder
- Win-condition trigger

### Folder ownership

| Member | Assigned Student | Main responsibility | Primary folders owned |
|---|---|---|---|
| **Member 1** | Mr. Kheng Sokthearith | Game Core & Integration | `Scripts/GameCore/`, `Scripts/Player/`, `Scripts/UI/`, `Scenes/`, `Prefabs/Player/`, `Prefabs/UI/` |
| **Member 2** | Mr. Yeat David | Environment & Platform | `Scripts/Environment/`, `Prefabs/Environment/`, `Models/Environment/`, `Models/Furniture/`, `Models/Props/`, `Materials/Environment/`, `Textures/Environment/` |
| **Member 3** | Mr. Thang Saoly | Water System | `Scripts/Water/`, `Prefabs/Water/`, `Materials/Water/`, `Textures/Water/` |
| **Member 4** | Mr. Tiek Chhunhour | Enemy & Rescue System | `Scripts/Enemy/`, `Scripts/Rescue/`, `Prefabs/Enemy/`, `Prefabs/Rescue/`, `Models/Characters/`, `Materials/Enemy/`, `Textures/Characters/` |

### Shared resources

- `Audio/` — Members can contribute sounds for their systems. One person should coordinate naming and organization.
- `UI/` — Interface images and fonts should be coordinated with Member 1.
- `Models/Characters/` — Member 4 can manage shark assets, while Member 1 coordinates the player model integration.

**Ownership means coordination, not technical access restriction.** Team members should discuss changes to another member's files before editing them.

### Important system ownership note

To avoid duplicated logic:
- Member 1 owns the player's canonical health and breath data (for example, through `PlayerHealth.cs`).
- Member 3 owns water detection and underwater behavior, and communicates with the player's health/breath component through an agreed public method or interface.
- Member 4 uses the agreed player damage interface and GameManager API rather than creating a second health or game-state system.
- Member 1 manages final scene integration.

## 4. Example: How folders work together

Suppose Member 2 creates a wooden table for the building.

```text
Models/Furniture/
└── WoodenTable.fbx
    The 3D shape of the table.

Textures/Environment/
└── WoodTexture.png
    The image used to add wood surface details.

Materials/Environment/
└── Wood.mat
    Uses the texture and defines the table's appearance.

Prefabs/Environment/
└── WoodenTable.prefab
    Combines the model, material, collider, and required components
    into a reusable object.

Scenes/Main.unity
    The table prefab is placed into the actual game scene.
```

This is how the different folders work together to create one complete game object.

## 5. Important rules for our team

1. **Do not put everything into one folder.** Always use the correct category.
2. **Use meaningful names.** For example, use `WoodenTable.prefab`, not `Cube (12)`.
3. **Use prefabs for reusable objects.** Avoid rebuilding the same furniture repeatedly.
4. **Keep test scenes separate.** Do not use the main gameplay scene for individual experiments.
5. **Do not delete `.meta` files.** Unity uses them to identify assets.
6. **Commit related assets together.** If you add a model, material, prefab, and texture, include all required files in your Git commit.
7. **Avoid editing the same scene or prefab simultaneously.** This helps prevent Git merge conflicts.
8. **Keep the Unity version consistent.** All team members should use the same project Unity version.
9. **Coordinate shared systems.** Agree on public methods, component references, and prefab requirements before integrating features.

## 6. Recommended setup workflow

1. Create the folder structure inside `Assets/Project/`.
2. Configure Unity Version Control settings consistently across the team:
   - **Edit → Project Settings → Editor → Version Control Mode:** Visible Meta Files.
   - **Asset Serialization Mode:** Force Text.
3. Commit the initial folder structure and project settings to the shared `develop` branch.
4. Ask every team member to pull the latest `develop` branch before starting.
5. Each member creates or uses their assigned feature branch and works in their own test scene.
6. Members commit and push their scripts, prefabs, models, materials, textures, and other required assets.
7. Review and merge completed work into `develop`.
8. Member 1 integrates and tests the systems in `Main.unity`.

**You do not need to create every script or asset file immediately.** Start by creating the folder hierarchy, then add files as each feature is developed.

---

**Project:** Escape the Flood  
**Engine:** Unity 6.3 LTS  
**Purpose:** Shared folder organization and team ownership reference
