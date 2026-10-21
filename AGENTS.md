# Agent notes

Start with README.md. It covers how to run the CLI, the MCP server, seed files and configuration.

- To check a seed or a filter, run it. Replaying a seed list takes milliseconds:
  - `dotnet run --project Motely.CLI -- --jaml <filter> --seeds <file|list|-> --cutoff off`
  - `dotnet run --project Motely.CLI -- --analyze <seeds|file>`
- Fix the code as it stands now. Don't dig through git history to justify a change.
- Seed lists are plain files (motely CSV output: `seed,score,...`). There is no database.
- After editing a filter, run `motely check <filter>`.
- Tests: `dotnet test Motely.Tests`. Build the CLI, MCP and test projects one by one, because `Motely.Wasm` needs a local package feed.
- Search-mode parsing shared by the CLI and MCP lives in `Motely/CliSearchMode.cs`.
- MCP tools are in `Motely.MCP/Tools/MotelyTools.cs`, and modes are parsed in `Motely.MCP/Search/SearchRunner.cs`.
