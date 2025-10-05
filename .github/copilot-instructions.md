# Copilot Instructions for Arronix

## Project Overview

Arronix is a modular, extensible media automation platform that enables different media types (TV, movies, music, books) to operate as plugins on a shared, unified core. This is an architectural restructuring of the Sonarr v5 codebase, evolving the *-arr* family into a single, coherent framework.

Planned refactor direction (not yet executed in the repository):
* Migrate UI/front end from the current TypeScript/React implementation to a Blazor WebAssembly client.
* Standardize entirely on .NET 10 (preview) across host, plugins, and UI once global tooling alignment is ready.
* Introduce Entity Framework Core (EF Core) as the primary abstraction over the relational database, with a strategy for plugin-contributed model slices.

**Key Objectives:**
- Replace fragmented *-arr* applications with one shared architecture
- Separate core orchestration from media-specific logic
- Provide a plugin interface for extensibility
- Maintain Sonarr's usability while modernising its design

## Architecture

### Core Structure
```
Core
 ├── Abstractions (versioned contracts)
 ├── Host Runtime (DI, plugin loader, schedulers)
 ├── Plugin Loader (AssemblyLoadContext isolation)
 └── Storage Layer (IMediaStore)
Plugins
 ├── TV
 ├── Movies
 ├── Music
 └── Books
```

### Key Components
- **Abstractions**: Stable, versioned interfaces and DTOs (`Arronix.Abstractions`)
- **Host Runtime**: DI composition, plugin lifecycle, scheduling, API hosting (`Arronix.Host`)
- **Plugin System**: Each plugin has a `plugin.json` manifest declaring capabilities, tokens, and policy chains
- **Policy Engine**: Executes declared policy chains for parsing, matching, quality, import, naming
- **Storage**: Polymorphic persistence via `IMediaStore`; plugins own their schema fragments

### Design Principles
1. **Media-agnostic Core**: No TV/Movie types in Core
2. **Stable Contracts**: Versioned abstractions; plugins pin to ranges
3. **Config-First**: Manifests define tokens + policy graphs
4. **Single Scheduler**: Plugins register jobs; host coordinates
5. **Testable by Design**: Golden tests for parsing/renaming; contract tests for providers

## Technology Stack

Current (legacy state still in repo):
* **Backend**: C# / .NET (see `global.json`).
* **Frontend (legacy)**: TypeScript/React with Webpack (`frontend/`).
* **Database**: Relational DB with custom persistence abstractions.
* **Build System**: .NET SDK + Node.js/yarn (only for legacy React assets).

Planned (target architecture):
* **Unified Runtime**: .NET 10 (preview acceptable) for host + plugins + UI (Blazor WASM).
* **Frontend (future)**: Blazor WebAssembly (isomorphic: option for server prerender later). No Webpack/yarn dependency for core UI after migration.
* **Data Access**: Entity Framework Core as the relational abstraction layer; plugin-specific entity mappings contributed via modular configuration.
* **Extensibility**: Continue using `AssemblyLoadContext` for plugin isolation; EF Core model composition per plugin.
* **Testing**: Add bUnit/UI test harness for Blazor components (future); legacy Jest/React tests remain until removal.

Guidance for contributions NOW: do not start the migration unless an issue explicitly authorizes it; preserve existing frontend while preparing abstractions that simplify the later Blazor switch.

## Development Setup

### Required Tools
Current development:
* Visual Studio Code (with recommended extensions)
* Git
* .NET SDK (current stable; will move to .NET 10 preview when scheduled)
* Node.js and yarn (only if modifying legacy React UI)

Forthcoming (post-migration):
* .NET 10 SDK (preview acceptable) — removes mandatory Node/yarn for core workflows.

### Getting Started
Current workflow:
1. (UI changes only) Install node dependencies: `yarn install`
2. (UI changes only) Start webpack dev server: `yarn start`
3. Build backend + tests: `dotnet build`
4. Run the application: `dotnet run`
5. Open browser to `http://localhost:8989`

Future Blazor workflow (reference only, do NOT implement yet):
1. `dotnet workload install wasm-tools` (on first setup, with .NET 10 preview)
2. Build solution (host + Blazor client project) via standard `dotnet build`
3. Single launch profile serves API + WASM assets.

## Code Conventions

### General Guidelines
- Follow the `.editorconfig` settings in the repository
- Use `LF` line endings for all commits
- Add tests (unit/integration) for all new functionality
- All PRs must reference a GitHub issue

### C# Code Style
- Follow existing patterns in the codebase
- Use dependency injection for all service dependencies
- Keep Core media-agnostic; media-specific logic belongs in plugins
- Use interfaces from `Arronix.Abstractions` for contracts
- Implement proper error handling with structured failures

### Frontend Guidelines
Legacy React (while it exists):
* Follow existing patterns; avoid large-scale refactors unrelated to the migration roadmap.
* Keep TypeScript strictness; do not introduce new implicit any usage.
* Preserve `css.d.ts` generation flow.

Planned Blazor UI:
* Organize Razor components by domain capability (e.g., `Library/`, `Jobs/`, `Policies/`).
* Prefer partial classes for complex logic to maintain separation of markup and behavior.
* Minimize JavaScript interop; when needed, encapsulate in a thin service and design for testability.
* Use dependency injection for shared state/services (avoid static singletons).
* Anticipate streaming updates (SignalR or EventSource) via injected abstractions.
* Keep components media-agnostic; plugin-specific UI surfaces register via extension/component registry (design TBD).

### Naming Conventions
* C# classes: PascalCase
* C# methods: PascalCase
* C# private fields: camelCase with underscore prefix (`_fieldName`)
* TypeScript (legacy UI): camelCase for variables/functions, PascalCase for components
* Razor Components: PascalCase
* Configuration keys: PascalCase

## Testing

### Test Structure
- **Unit Tests**: `*.Test` projects for component testing
- **Integration Tests**: `NzbDrone.Integration.Test` for end-to-end scenarios
- **Contract Tests**: Ensure host & plugin interface stability
- **Golden Tests**: Deterministic parsing/renaming scenarios

### Running Tests
- Run all tests: `dotnet test`
- Run specific test project: `dotnet test src/ProjectName.Test`

## Plugin Development

### Plugin Manifest Structure
Each plugin requires a `plugin.json` with:
- `id`: Unique identifier
- `name`: Display name
- `version`: Semantic version
- `contracts`: Compatible core version range
- `identifiers`: External metadata systems used
- `capabilities`: Functional areas (indexing, metadata, parsing, etc.)
- `tokens`: Naming template placeholders
- `policies`: Policy chain definitions per category

### Plugin Guidelines
- Implement `IPluginModule` as composition entrypoint
- Register services via DI during activation
- Use `AssemblyLoadContext` for isolation
- Only expose services defined by abstractions
- Declare all capabilities and tokens in manifest

## Common Workflows

### Adding a New Feature
1. Create or reference an existing GitHub issue
2. Fork from `dev` branch using `feature/feature-name` naming
3. Make changes, following code conventions
4. Add appropriate tests
5. Update documentation (ARCHITECTURE.md, GLOSSARY.md if needed)
6. Ensure all tests pass
7. Create PR back to `dev` branch

### Working with Configuration
- Config file: XML-based (legacy from Sonarr)
- Provider: `ConfigFileProvider` class
- API key generation: GUID without hyphens
- Changes require migration support for existing installations

### API Development
- Use OpenAPI/Swagger for documentation
- API key authentication via header (`X-Api-Key`) or query parameter (`apikey`)
- Maintain backward compatibility where possible
- New endpoints should be media-agnostic when applicable

### Database Migrations / EF Core (Planned)
Current: custom/legacy persistence approach remains.

Target EF Core strategy:
* One primary DbContext for core cross-media/shared entities; plugins contribute model fragments via assembly-scanned IEntityTypeConfiguration implementations or a modular builder hook.
* Plugin migrations isolated per plugin (e.g., `Migrations/<plugin-id>/`), applied conditionally based on activation.
* Use `Owned Entity Types` / `Table-per-Type (TPT)` where polymorphism is required; prefer composition over inheritance when feasible.
* Strict nullability + required attribute usage to surface schema intent.
* Migration verification tests: ensure adding/removing a plugin cleanly applies/reverts its schema slice (idempotency + forward-only path documented).
* Avoid leaking provider-specific types into abstractions; keep portability (Postgres/SQLite baseline) open.

Contributor Rule: Do not introduce EF Core packages or scaffolding until a dedicated migration issue is approved.

## Important Files and Locations

Current:
* **Main documentation**: `README.md`, `ARCHITECTURE.md`, `CONTRIBUTING.md`, `GLOSSARY.md`
* **Config**: `global.json`, `.editorconfig`, `tsconfig.json` (legacy UI)
* **Backend source**: `src/`
* **Legacy Frontend**: `frontend/src/`
* **Tests**: `*.Test` projects
* **CI/Workflows**: `.github/workflows/`

Planned additions (not yet present):
* `src/Arronix.Web.Client/` (Blazor WASM client project)
* `src/Arronix.Web.Shared/` (shared DTOs/contracts for UI if separation needed)
* `src/Arronix.Data/` (EF Core abstractions + core DbContext)
* Plugin-specific data configuration classes collocated with plugin assemblies.

## Glossary Quick Reference

Key terms (see GLOSSARY.md for full definitions): capability, policy chain, token, manifest, media store, job, contract. (List shortened here to reduce duplication.)

## Security Considerations

- Never commit secrets or API keys
- Use structured logging; avoid sensitive data in logs
- Plugin code executes in-process (trust-based initially)
- API authentication required for production
- Validate all user inputs

## Branching and Pull Requests

- **Default branch**: `dev`
- **Feature branches**: `feature/feature-name`
- **PR requirements**:
  - Link to GitHub issue
  - All tests passing
  - Code review approval
  - Documentation updates (if applicable)

## License

Licensed under GNU GPL v3. Portions originated from Sonarr v5 (GPLv3).

## Notes for Contributors

- This is an active refactoring project; architecture is evolving
- Some areas still reflect Sonarr heritage (will be migrated)
- Follow the Definition of Done in CONTRIBUTING.md
- Ask questions if architecture decisions are unclear
- Coordinate on significant changes via issues first

## Migration Context

Arronix is transitioning from Sonarr v4/v5:
1. TV logic is being extracted into first plugin
2. Polymorphic media tables are being introduced
3. Hard-coded TV references in Core are being replaced with abstractions
4. Additional media plugins (Movies, Music) will follow

When working on legacy code, prefer incremental refactoring toward the plugin architecture rather than large rewrites.

### Upcoming Migration Phases (Not Yet Started)
1. Introduce EF Core foundational project with no behavioral change (schema parity tests only).
2. Parallel Blazor WASM prototype consuming existing API (read-only flows) while React UI remains default.
3. Migrate critical UI flows (library view, queue, activity) to Blazor; maintain React for settings/advanced until parity.
4. Remove webpack/yarn pipeline once Blazor reaches feature parity; archive legacy UI.
5. Enable plugin-driven UI surface registration (component discovery / manifest extension).
6. Adopt .NET 10 preview for all projects; validate build + publish; then remove multi-targeting hacks.

Contributor Reminder: Do not begin these phases without an associated tracking issue and architectural approval.

## Documentation Synchronization & Auto-Maintenance Rules

These instructions MUST be kept up to date by Copilot (the assistant) whenever a change transitions from "planned" to "implemented" in the repository.

### Core Rule
When a feature, technology, or structural element mentioned here as "planned", "future", or "not yet started" becomes present in the codebase (e.g., new project folders, new dependencies, updated solution files), update:
1. `.github/copilot-instructions.md` (remove or rephrase the planned status)
2. `ARCHITECTURE.md` (reflect the new reality, adjust diagrams or component descriptions)
3. `GLOSSARY.md` (add/modify terms introduced or changed by the implementation)
4. `CONTRIBUTING.md` (update workflows, build steps, or contribution expectations)
5. `README.md` (only if the top-level positioning or quickstart changes)

### Trigger Conditions (Examples)
Update docs immediately when any of these occur:
| Trigger | Required Doc Updates |
|---------|----------------------|
| Blazor WASM client project (`src/Arronix.Web.Client/`) added | Copilot Instructions, Architecture (frontend stack), Contributing (dev workflow), README (optional quickstart) |
| EF Core project (`src/Arronix.Data/` or similar) introduced | Copilot Instructions (remove "planned"), Architecture (storage model), Contributing (migration workflow), Glossary (new terms if any) |
| Migration of a UI flow from React to Blazor | Architecture (UI evolution status), Copilot Instructions (progress), Possibly README (if React instructions simplified) |
| Adoption of .NET 10 preview in `global.json` | Copilot Instructions (tooling), Contributing (required SDK), README (build snippet) |
| Addition of plugin-driven EF model configuration | Architecture (data composition), Glossary ("Model Fragment" term), Copilot Instructions |
| New policy category introduced | Architecture (policy section), Glossary (term), Copilot Instructions |
| Token system changes (namespace rules, new global tokens) | Glossary (tokens), Architecture (naming section), Copilot Instructions |
| Removal of legacy React build pipeline | Copilot Instructions (remove yarn steps), Contributing (simplify setup), README (quickstart) |

### Update Procedure (Assistant Checklist)
1. Detect change (diff shows new project, package reference, or removed legacy asset).
2. Search for remaining occurrences of terms like "planned", "future", or "not yet started" relevant to that change.
3. Edit affected docs in a single PR (grouped logically) with a concise summary referencing the enabling change (issue/commit if available).
4. Ensure internal consistency: architecture diagrams match new structure; quickstarts match current build steps.
5. Remove obsolete instructions (do NOT leave contradictory parallel workflows unless intentionally transitional—if transitional, label both clearly with timeframe or exit criteria).
6. Add any new glossary terms introduced by the implementation (avoid class/interface names—use conceptual terms).
7. Run lint/format if applicable (respect repository conventions).

### Language & Style Guidance
* Use "Implemented" instead of "Planned" once live.
* Prefer neutral declarative sentences over speculative wording.
* Keep future roadmap items segregated under a clearly marked section (e.g., "Upcoming" or explicit numbered phases).

### Anti-Drift Safeguards
Before closing any PR that adds/removes:
* A top-level `src/Arronix.*` project
* A major framework dependency (Blazor, EF Core packages)
* A build step (yarn removed, wasm workload added)

...the assistant must verify and (if necessary) adjust the five doc targets listed above.

### Glossary Enrichment Rules
* Only add terms that represent durable concepts (avoid temporary codenames or transient migration tasks).
* If a term is removed (concept deprecated), mark it as deprecated in the PR description and delete from `GLOSSARY.md` unless historical retention is explicitly requested.

### Conflict Resolution
If simultaneous changes would make instructions ambiguous (e.g., partial Blazor migration):
* Represent both paths with clear labels: "Legacy React (until removed)" and "Blazor (new)".
* Include an exit criterion (e.g., "Remove React path after Issues #123, #124 close").

### Assistant Responsibility Statement
The assistant is expected to proactively inspect diffs of structural changes and initiate documentation updates without waiting for explicit user prompts.

