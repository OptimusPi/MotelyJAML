#!/usr/bin/env bash
# seedhunt — Waterbear's Balatro seed-finder game.
#
#   tools/seedhunt.sh <filter.yaml> [count] [deck] [stake] [random-pool]
#
# Sweeps seeds with the JAML filter, keeps the best `count` matches (written
# back into the filter's top-level seeds: block, best score first — and every
# other match too, sorted by score), then reads the winning seed's antes aloud
# with --analyze. That last part is the yell test.
#
# random-pool: how many random seeds to draw from (default count * 5000 — at
# ~1/410 for a bare Perkeo-ante-1 that's ~12x overshoot). Aesthetic families
# only cover a tiny corner of seed space; random is the real wilderness.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

FILTER="${1:?usage: seedhunt.sh <filter.yaml> [count] [deck] [stake] [random-pool]}"
COUNT="${2:-5}"
DECK="${3:-Red}"
STAKE="${4:-White}"
POOL="${5:-$((COUNT * 5000))}"

echo "🃏 seedhunt: drawing $POOL random seeds, keeping the best $COUNT — $FILTER"
dotnet run --project Motely.CLI -- --jaml "$FILTER" --random "$POOL" --collect "$COUNT" --cutoff off -q

# The CLI writes matches back into the filter's seeds: block, best score first.
BEST="$(awk '/^seeds:/{flag=1; next} flag && /- [1-9A-Z]{8}\r?$/{print $2; exit}' "$FILTER")"
if [ -z "$BEST" ]; then
    echo "😿 No seed matched. Loosen the must: block or sweep longer."
    exit 1
fi

echo
echo "🏆 Best seed: $BEST — reading its antes:"
echo
dotnet run --project Motely.CLI -- --analyze "$BEST" --deck "$DECK" --stake "$STAKE"
