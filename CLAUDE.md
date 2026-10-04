# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

SonarGame is a Unity 6 (`6000.4.8f1`) URP game about a submarine exploring procedurally generated underwater caves. It uses the new Input System (`Assets/_Project/Input/InputSystem_Actions.inputactions`). The main scene is `Assets/_Project/Scenes/Main/MainScene.unity`; the other scenes in `Scenes/TestBeds/` are test beds (IK crawlers, snakes, tubes).

All project assets live under `Assets/_Project/` (`Scripts/`, `Shaders/` incl. `Compute/`, `Art/`, `Prefabs/`, `Data/` for ScriptableObject instances, `Scenes/`, `Input/`). Unused prototypes are parked in `Assets/_Legacy/`. `Assets/Settings/` holds the URP assets.

## Building / running

There is no CLI build, lint, or test setup. Compiling, running, and profiling all happen in the Unity Editor, and the repo has no test assemblies, even though `com.unity.test-framework` is installed. `dotnet` isn't installed in the WSL environment, so you can't verify compilation from the shell. Keep edits syntactically careful and ask the user to check the Unity console.

- `.aiignore` / `.cursorignore` exclude `*.meta`, `*.prefab`, and `*.asset`. Don't hand-edit serialized Unity files. Scene/prefab wiring (e.g. assigning a new `[SerializeField]` on a host component) has to be done by the user in the Editor, so tell them when it's needed.
- New C# scripts need a matching `.meta`, which Unity generates on import. Don't write one by hand.
- `Assets/Plugins/vFolders`, `vHierarchy`, `vInspector` are third-party editor tools. Leave them alone.

## Architecture

### Service resolver (no Bootstrap)
There is no central Bootstrap. Systems are **MonoBehaviour hosts** placed in the scene. Each host builds its internals (often plain C# classes) in `Awake`, registers itself under narrow interfaces in `OnEnable` (`GameServices.EnsureInitialized().Register<IFoo>(this)`), and unregisters in `OnDisable`. Consumers resolve in `Start` (`Resolve<T>` when required, `TryResolve<T>` when optional, `ResolveWhenReady<T>` for late registrants). `Core/ObjectResolver.cs` + `GameServices.cs` are the whole mechanism. Avoid inspector references to other systems; register or resolve instead (local prefab wiring such as IK bones is fine).

Hosts: `TerrainSystem` (`ISdfSampler`, `ITerrainData`, `ITerrainEditor`, `ITerrainDebug`), `AutomationSystem` (`IInventory`, `IAutomationSystem`), `PathfindingSystem` (`IPathfindingSystem`), `FishSpawnSystem` (`IFishSystem`), `SubController` (`IPlayer`), `FogLightManager` (`IFogLightRegistry`). `ChunkParentAnchor` and `ChunkLoaderTarget` are components that register the chunk parent transform and the streaming focus (exactly one each). Terrain shaders/material/settings come from the `TerrainResources` ScriptableObject. Execution order: anchors -200, `TerrainSystem` -100 (it resolves the anchors in `Awake`); everything else resolves in `Start`.

A new system should be a host MonoBehaviour that registers an interface and ticks itself in `Update`; it is not constructed from any central file.

Tunable parameters live in ScriptableObjects under `Assets/_Project/Scripts/Settings/` (`[CreateAssetMenu(menuName = "Own/...")]`), with asset instances in `Assets/_Project/Data/`. `MCSettings` (chunk dims, voxel scale, noise, biomes) and `ChunkStreamingSettings` (radius, readback budget, water level, atlas slots) are shared by almost everything.

### Terrain pipeline (`Assets/_Project/Scripts/ChunkHandling/`, `Assets/_Project/Shaders/Compute/`)
The terrain is GPU-generated in chunks with marching cubes:
- `ChunkStreamer` decides which chunk coords to load/unload around the target and queues builds.
- `ChunkBuilder` submits per-chunk GPU work: `MCBaker` (density → `MarchingCubes.compute` → `PackForReadback.compute`) for meshes, and `SDFGpu` (`DensityValues.compute` + `EDT.compute`) for a signed distance field. Results come back through **async GPU readbacks**, capped by `maxAsyncReadbacks`. When a chunk finishes it raises `OnChunkReady`, which `SpawnManager` subscribes to. Chunks above `waterLevel` skip the mesh path.
- `ChunkManager` owns `Dictionary<Vector3Int, Chunk>`, handles world↔chunk coordinate math, and does CPU SDF sampling (`TryGetSDFValue`, trilinear across chunk borders). Terraform edits are stored per chunk. When a chunk unloads they move to `_offloadedEdits` and get reapplied when it reloads.
- `SDFAtlas` packs every loaded chunk's SDF into one GPU `ComputeBuffer` (slot per chunk) plus a dense chunk→slot lookup centered on the player. GPU consumers (fish boids, prop placement) sample it via `Compute/Includes/SampleSdfAtlas.hlsl`. **The voxel indexing (`z + y*sz + x*sz*sy`) and border-wrapping logic are duplicated** between `SampleSdfAtlas.hlsl` and `ChunkManager.TryGetSDFValue`, and must be kept in sync.
- Shared HLSL lives in `Compute/Includes/` (noise, Worley biomes, march tables, density sampling, structs).

### Spawning & creatures
- `ChunkHandling/Spawning/`: `SpawnManager` places props on chunk surfaces once a chunk is ready. Instanced prop batches are built on the GPU (`PropInstanceBuild.compute`) and released when their chunk unloads.
- `Fish/FishSpawnSystem.cs` is a fully GPU boid simulation (`FishBoids.compute`). Each frame it builds a spatial grid (count → prefix scan → scatter) and runs the boid update with SDF wall avoidance, then fills matrix/draw lists for indirect instanced rendering. It spawns fish from each chunk's `interiorSpawnPositions`.
- `Enemies/SeaSnakeSpawnSystem` spawns sea snakes only while `AutomationLogicSystem.IsMiningActive`.
- Procedural animation: `IK/` (CCD solver, spider walker), `AnalyticalLegIk*`, `TailController`/`ArmatureTailController`.
- Surface pathfinding (`Pathfinding/`): `PathfindingSystem` hosts `PathfindingGraphSystem`/`PathfindingGraphBuilder` and registers `IPathfindingSystem`. The builder floods outward from a seed on the wall mesh (Dijkstra order, async batched raycasts over a hex probe pattern), producing nodes (`position`, `normal`, `distFromSource`) and edges. `distFromSource` is path distance along the surface to the seed, so the seed is the destination and agents walk "downhill". Candidate points are rejected if a clearance ray along their normal hits a wall. After the flood, a cleanup pass merges crowded same-facing nodes and rebuilds edges **only from the original build's edges** (remapped through merges; never adds a link the flood didn't make), then recomputes distances. Tunables are in `PathfindingGraphSettings` (Grid / Clearance / Cleanup).
- `SpiderGraphMover` moves a spider along the graph: it tracks a current node, hops only along edges, and blends the directions to downhill neighbours (radius search is only for the first node / recovery, so it can't be pulled through walls). It only moves/turns the transform; `SpiderWalkController` still handles wall alignment (body height/tilt from the planted feet's plane, single down-ray as fallback) and leg stepping. Spider distance fields are authored at scale 1 and multiplied by the root's uniform scale. `GraphFollower` is an older, standalone follower.
- `PathfindingGraphDebug` (+ `Editor/PathfindingGraphDebugEditor`) is a debug component with Generate Graph / Spawn Spider inspector buttons (play mode), and gizmos for nodes, edges and clearance-rejected points.

### Tools & automation
- `Tools/ToolModeController` switches between tool modes (Dismantle, Placement, Pipe, Terraform). Tools are only active while the free camera is on. Each mode is a plain handler class in `Tools/` with its own settings SO.
- `AutomationLogic/`: `Machine` (MonoBehaviour with input/output pipe nodes) and `Pipe` map onto the logic graph's `AutomationNode`/`AutomationEdge` in `AutomationLogicSystem`. `Machine`/`Pipe` notify `IAutomationSystem` from `OnDestroy`. A miner connected by pipes to the submarine adds ore to `Inventory` over time, and that counts as "mining active". Machines with `RegisterOnStart` register themselves in `Start`, and placed ones are registered by the placement tool.

### Rendering
URP renderer features live in `Rendering/` (`VolumetricFogRenderFeature`, `BlurToTextureFeature`). `FogLightManager` pushes light data to shaders as globals. Shader Graphs live in `Assets/_Project/Shaders/Graphs/`, shared HLSL in `Shaders/Includes/`.

### Debugging
`Debug/RuntimeDebugController` toggles chunk building, spawning, and fish at runtime. `ChunkSDFVisualizer` and `SDFAtlasTest` check that CPU and GPU SDF sampling agree.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
