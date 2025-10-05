# Copilot Instructions for Arronix

## Project Overview

Arronix is a modular, extensible media automation platform that enables different media types (TV, movies, music, books) to operate as plugins on a shared, unified core. This is an architectural restructuring of the Sonarr v5 codebase, evolving the *-arr* family into a single, coherent framework.

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

- **Backend**: C# / .NET (see `global.json` for version)
- **Frontend**: TypeScript/React with Webpack
- **Database**: Relational DB (with planned multi-backend support)
- **Build System**: .NET SDK, yarn for frontend

## Development Setup

### Required Tools
- Visual Studio Code (with recommended extensions)
- Git
- .NET SDK (version specified in `global.json`)
- Node.js and yarn

### Getting Started
1. Install node dependencies: `yarn install`
2. Start webpack for frontend development: `yarn start`
3. Build the project: `dotnet build`
4. Run the application: `dotnet run`
5. Open browser to `http://localhost:8989`

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

### TypeScript/Frontend
- Follow existing React patterns
- Use TypeScript for type safety
- CSS files have auto-generated `.css.d.ts` files - don't edit these
- Follow component structure conventions

### Naming Conventions
- C# classes: PascalCase
- C# methods: PascalCase
- C# private fields: camelCase with underscore prefix (`_fieldName`)
- TypeScript/JavaScript: camelCase for variables/functions, PascalCase for components
- Configuration keys: PascalCase

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

### Database Migrations
- Use appropriate migration tooling
- Test migrations with existing data
- Support rollback scenarios
- Document schema changes

## Important Files and Locations

- **Main documentation**: `README.md`, `ARCHITECTURE.md`, `CONTRIBUTING.md`, `GLOSSARY.md`
- **Config**: `global.json`, `.editorconfig`, `tsconfig.json`
- **Backend source**: `src/` directory
- **Frontend source**: `frontend/src/` directory
- **Tests**: `*.Test` directories
- **Workflows**: `.github/workflows/`

## Glossary Quick Reference

Key terms (see GLOSSARY.md for full definitions):
- **Capability**: Functional area a plugin declares
- **Policy Chain**: Ordered pipeline for processing (parsing, matching, etc.)
- **Token**: Placeholder in naming templates
- **Manifest**: Plugin's declarative descriptor
- **Media Store**: Persistence abstraction layer
- **Job**: Schedulable background work unit
- **Contract**: Versioned abstraction between core and plugins

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
