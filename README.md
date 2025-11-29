# Motely

The fastest Balatro seed searcher with JSON, JAML support and an interactive TUI.

Based on [@tacodiva](https://github.com/tacodiva)'s incredible [Motely](https://github.com/tacodiva/Motely) - a blazing-fast SIMD-powered seed searcher. This fork extends it with multiple configuration formats (JSON, JAML) and a Terminal User Interface for easy filter creation.

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
- Building custom filters visually
- Quick search with predefined filters
- Loading config files
- Starting the API server

### Command Line Usage
```bash
# Search with a JSON filter
dotnet run -- --json PerkeoObservatory --threads 16 --cutoff 2

# Search with a JAML filter
dotnet run -- --jaml MyFilter --threads 16 --cutoff 2

# Use a native filter
dotnet run -- --native PerkeoObservatory --threads 16

# Analyze a specific seed
is
```

## Command Line Options

### Core Options
- `--json <filename>`: JSON config from JsonItemFilters/ (without .json extension)
- `--jaml <filename>`: JAML config from JamlFilters/ (without .jaml extension)
- `--native <filter name>`: Built-in native filter (without .cs extension)
- `--analyze <SEED>`: Analyze specific seed
- **Note:** No args launches TUI by default

### Performance Options
- `--threads <N>`: Thread count (default: CPU cores)
- `--batchSize <1-8>`: Vectorization batch size
- `--startBatch/--endBatch`: Search range control

### Filter Options
- `--cutoff <N|auto>`: Minimum score threshold
- `--deck <DECK>`: Deck selection
- `--stake <STAKE>`: Stake level

## Filter Formats

### JSON Filter Format

Create in `JsonItemFilters/`:
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
- `negativecopy`: Showman + copy jokers with negatives
- `PerkeoObservatory`: Telescope/Observatory + soul jokers
- `trickeoglyph`: Cartomancer + Hieroglyph

## Tweak the Batch Size 
1. For the most responsive option, Use `--batchSize 1` to batch by one character count (35^1 = 35 seeds) 
2. Use `--batchSize 2` to batch by two character count (35^2 = 1225 seeds)
3. Use `--batchSize 3` to batch by three character count (35^3 = 42875 seeds)
4. Use `--batchSize 4` to batch by four character count (35^4 = 1500625 seeds)

Above this is senseless and not recommended.
Use a higher batch size for less responsive CLI updates but faster searching!
I like to use --batchSize 2 or maybe 3 usually for a good balance, but I would use --batchSize 4 for overnight searches.