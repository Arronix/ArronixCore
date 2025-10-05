# How to Contribute

## Development

### Tools required

- **Visual Studio Code** - it will automatically prompt you to install the recommended extensions when you open the repository.
- **Git** - for version control
- **DotNet SDK** (see version in global.json)

### Getting started

> TODO: This hasn't been updated for Arronix yet, it will be as the Sonarr refactor completes.

1. Install node `yarn install`
2. Start webpack to monitor your dev environment for any frontend changes that need post processing using `yarn start` command.
3. Build the project in Visual Studio, Setting startup project to `Sonarr.Console` and framework to `x86`
4. Debug the project in Visual Studio
5. Open [Sonarr](http://localhost:8989)
  
### Contributing Code

- All PRs must be made against a [GitHub Arronix issue](https://github.com/Arronix/ArronixCore/issues).
- Add tests (unit/integration)
- Commit with `LF` line endings
- Use the Visual Studio Code `.editorconfig` to follow adopted coding styles

### Branching and Pull Requests

> The **default branch** is currently `dev`.

- Always fork from the default branch as a feature branch using `feature/feature-name` naming convention
- Only make pull requests back to the default branch

### Definition of Done

- Any contributions or changes to the architecture are documented with updates to [Architecture](./ARCHITECTURE.md) or [GLOSSARY](./GLOSSARY.md) as appropriate.
- All tests pass
- Additional tests are added for any new or changed functionality
- Code has been reviewed and approved by at least one other contributor
