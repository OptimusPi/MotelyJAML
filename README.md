# MotelyJAML

The fastest Balatro seed searcher with JAML (or JSON) support and an interactive TUI.

Based on [@tacodiva](https://github.com/tacodiva)'s incredible [Motely](https://github.com/tacodiva/Motely) - a blazing-fast SIMD-powered seed searcher. MotelyJAML extends it with multiple configuration formats (JAML, JSON) and a Terminal User Interface for easy filter creation.

## Installation

**[📥 Download Latest Release](https://github.com/OptimusPi/MotelyJAML/releases/latest)**

Available for Windows (x64), Linux (x64), macOS (Intel & Apple Silicon)

### Build from Source
```bash
git clone https://github.com/OptimusPi/MotelyJAML.git
cd MotelyJAML
dotnet run --project Motely
```

## Quick Start

### Launch the TUI (Terminal User Interface)
```bash
# Launch interactive TUI (default when no arguments provided)
dotnet run
```

The TUI provides an interactive menu for:
- Building custom filters
  - It's really simple, supports all item types
  - TODO: add support for edition, stickers/sealts, etc.
- Loading config files and searching
  - P.S. Sorry about the BalatroShaderBackground, I just had to
  - shader background PAUSES during API Host or Search
- Starting the API server
  - Optional:[Install cloudflared](https://github.com/cloudflare/cloudflared?tab=readme-ov-file#installing-cloudflared)
  - Navigate to SETTINGS and change search/api as needed.
  - Host
    - set the host. usually `127.0.0.1` or `localhost`
    - host is sometimes `+` on some linux distros/VMs.
    - TODO: I'm new to Apple so still learning pitfalls of local networking?
    - set the port to whatever you want. 
  - Search
    - lower the search to avoid 100% CPU load
    - Example: if you have a 16-core CPU but you are playing a game, doing homework, browsing the web, you could set `--threads 8` to save a lot of system resources for yourself. 
    - Example: if you have a really tiny box to put this on, maybe it needs less threads to save a little wiggle room for SSH/VNC/Parsec/Steamlink/ to still function with good performance
    - Example: if you have an 8 core CPU but want 2 different search instances running you can run 2 different terminals each with `-- threads 4`
    - NOTE: Never set the threads past what your CPU has. You'll lower performance.
    - NOTE: if your filter is very permissive, you'll bottleneck on console.writeline()
    - NOTE: see below for more options; 

### Command Line Usage

#### Search with JSON/JAML filters
```bash
dotnet run -- --json PerkeoObservatory --threads 16 --cutoff 2
dotnet run -- --jaml MyFilter --threads 16 --cutoff 2
```

#### Use Native C# Filters
Built-in filters from [tacodiva's original Motely](https://github.com/tacodiva/Motely):
```bash
dotnet run -- --native PerkeoObservatory --threads 16
```

#### Analyze Seeds
Following the `analyzer.cl` convention from [Immolate](https://github.com/SpectralPack/Immolate), [TheSoul](https://github.com/SpectralPack/TheSoul), and [Blueprint](https://miaklwalker.github.io/Blueprint/):
```bash
dotnet run -- --analyze 5SC1HR14
```

### Available Options

**Core:**
- `--json <name>` - Load filter from JsonFilters/
- `--jaml <name>` - Load filter from JamlFilters/
- `--native <name>` - Use built-in C# filter
- `--analyze <SEED>` - Analyze specific seed
- _(no args)_ - Launch TUI

**Performance:**
- `--threads <N>` - Thread count (default: CPU cores)
- `--batchSize <1-8>` - Batch size for vectorization
- `--startBatch <N>` / `--endBatch <N>` - Search range

**Filtering:**
- `--cutoff <N|auto>` - Minimum score (JSON/JAML only)
- `--deck <DECK>` - Override filter's deck
- `--stake <STAKE>` - Override filter's stake

## Filter Formats

### JSON Filter Format

Create in `JsonFilters/`:
```json
{
  "name": "Example",
  "must": [{
    "type": "Voucher",
    "value": "Telescope",
    "antes": [1, 2, 3]
  }],
  "should": [{
    "type": "Joker",
    "value": "Blueprint",
    "antes": [1, 2, 3, 4],
    "score": 100
  }]
}
```

### JAML Filter Format

Create in `JamlFilters/`:
```
name: Example
description: Example filter using JAML
author: YourName
dateCreated: 2025-01-01T00:00:00Z

must:
  - type: Voucher
    value: Telescope
    antes: [1, 2, 3]

should:
  - type: Joker
    value: Blueprint
    antes: [1, 2, 3, 4]
    score: 100
```

Both formats support the same filter logic - choose whichever you prefer!

### Using Defaults in JAML

Configure defaults for your filter to avoid repetition:

```
name: My Filter
defaults:
  # Default antes (1-8)
  antes: [1, 2, 3, 4, 5, 6, 7, 8]

  # Default pack/shop slots (auto-adjusted for ante 1)
  packSlots: [0, 1, 2, 3, 4, 5]
  shopSlots: [0, 1, 2, 3, 4, 5]

  # Default score
  score: 1

must:
  # Uses defaults.antes automatically
  - voucher: Telescope

  # Override with specific antes
  - voucher: Observatory
    antes: [2, 3]

should:
  # Uses defaults.score = 1
  - joker: Blueprint

  # Override score
  - joker: Brainstorm
    score: 50
```

**Note:** Ante 1 automatically limits pack slots to [0-3] and shop slots to [0-3] (4 slots max in ante 1).

## Native Filters
- `ErraticFinder`: Erratic Deck with 32+ cards of a specific suit
- `negativecopy`: Showman + copy jokers with negatives
- `PerkeoObservatory`: Telescope/Observatory + soul jokers
- `trickeoglyph`: Cartomancer + Hieroglyph

## Tweak the Batch Size
1. `--batchSize 1`: 35^1 = 35 seeds per batch
2. `--batchSize 2`: 35^2 = 1,225 seeds per batch
3. `--batchSize 3`: 35^3 = 42,875 seeds per batch
4. `--batchSize 4`: 35^4 = 1,500,625 seeds per batch
5. `--batchSize 5`: 35^5 = 52,521,875 seeds per batch
6. `--batchSize 6`: 35^6 = 1,838,265,625 seeds per batch
7. `--batchSize 7`: 35^7 = 64,339,296,875 seeds per batch

Higher batch sizes = less responsive updates but faster overall search. Recommended: 2-4 for interactive use, 4-5 for overnight searches.