"""
Fortnite Pirate Shop Access Token odds calculator.

Epic's published rate: 1 in 137,500 per chest in normal matches,
boosted 1.5x during Saturday Treasure Hours (some reports say 2x;
use --boost to change it).

Usage:
    python pirate_token_odds.py                 # interactive prompts
    python pirate_token_odds.py --chests 500
    python pirate_token_odds.py --matches 40 --chests-per-match 8
    python pirate_token_odds.py --chests 300 --treasure-chests 100 --boost 2
    python pirate_token_odds.py --counter       # use chests counted by chest_counter.py
"""

import argparse
import math

BASE_ODDS = 137_500          # 1 in N per chest (official)
DEFAULT_BOOST = 1.5          # Treasure Hours multiplier


def chance_at_least_one(normal_chests, treasure_chests, boost):
    p_normal = 1 / BASE_ODDS
    p_treasure = min(1.0, boost / BASE_ODDS)
    p_miss = (1 - p_normal) ** normal_chests * (1 - p_treasure) ** treasure_chests
    return 1 - p_miss


def chests_needed(target, p):
    """Chests needed to reach `target` chance of at least one token."""
    return math.ceil(math.log(1 - target) / math.log(1 - p))


def fmt_pct(x):
    if x == 0:
        return "0%"
    if x < 0.0001:
        return f"{x * 100:.6f}%"
    return f"{x * 100:.4f}%"


def report(normal_chests, treasure_chests, boost, chests_per_match):
    p_normal = 1 / BASE_ODDS
    p_treasure = min(1.0, boost / BASE_ODDS)
    total = normal_chests + treasure_chests
    chance = chance_at_least_one(normal_chests, treasure_chests, boost)

    print()
    print("=" * 56)
    print("  PIRATE SHOP ACCESS TOKEN - ODDS")
    print("=" * 56)
    print(f"  Per chest (normal):         1 in {BASE_ODDS:,}  ({fmt_pct(p_normal)})")
    print(f"  Per chest (Treasure Hours): 1 in {BASE_ODDS / boost:,.0f}  ({fmt_pct(p_treasure)})")
    print("-" * 56)
    print(f"  Chests opened: {normal_chests:,} normal + {treasure_chests:,} Treasure Hours = {total:,}")
    print(f"  Chance you get at least one token: {fmt_pct(chance)}")
    if chance > 0:
        print(f"  That's about 1 in {1 / chance:,.0f}")
    print("-" * 56)
    print("  Chests needed for a ... chance of getting it:")
    for target in (0.10, 0.50, 0.90, 0.99):
        n = chests_needed(target, p_normal)
        nt = chests_needed(target, p_treasure)
        line = f"    {target:>4.0%}:  {n:>9,} normal  |  {nt:>9,} Treasure Hours"
        print(line)
    if chests_per_match:
        n50 = chests_needed(0.5, p_normal)
        print(f"  At {chests_per_match} chests/match, a 50% shot takes ~{math.ceil(n50 / chests_per_match):,} matches.")
    print("=" * 56)
    print("  Every chest is an independent roll - past chests don't")
    print("  make the next one more likely.")
    print()


def app_dir():
    """Folder to keep chest_counts.json in: next to the .exe when packaged, else next to this script."""
    import os, sys
    if getattr(sys, "frozen", False):
        return os.path.dirname(sys.executable)
    return os.path.dirname(os.path.abspath(__file__))


def load_counter_counts():
    """(normal, treasure) chests saved by chest_counter.py, or None."""
    import json, os
    path = os.path.join(app_dir(), "chest_counts.json")
    try:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        return int(data.get("normal", 0)), int(data.get("treasure", 0))
    except (OSError, ValueError):
        return None


def ask_int(prompt, default=0):
    raw = input(f"{prompt} [{default}]: ").strip().lstrip("﻿")
    if not raw:
        return default
    try:
        return max(0, int(raw.replace(",", "")))
    except ValueError:
        print("  Not a number, using", default)
        return default


def main():
    ap = argparse.ArgumentParser(description="Odds of getting the Fortnite Pirate Shop Access Token.")
    ap.add_argument("--chests", type=int, help="normal chests opened")
    ap.add_argument("--treasure-chests", type=int, default=0, help="chests opened during Treasure Hours")
    ap.add_argument("--matches", type=int, help="matches played (used with --chests-per-match)")
    ap.add_argument("--chests-per-match", type=int, default=None, help="average chests per match")
    ap.add_argument("--boost", type=float, default=DEFAULT_BOOST, help="Treasure Hours multiplier (default 1.5)")
    ap.add_argument("--counter", action="store_true", help="use the chests counted by chest_counter.py")
    args = ap.parse_args()

    counted = load_counter_counts()
    if args.counter:
        if counted is None:
            print("No chest_counts.json yet - run chest_counter.py while you play first.")
            return
        report(counted[0], counted[1], args.boost, None)
        return

    if args.chests is None and args.matches is None:
        print("Fortnite Pirate Token odds (press Enter to accept defaults)")
        if counted is not None:
            ans = input(f"Use the {counted[0] + counted[1]:,} chests counted by chest_counter? [Y/n]: ").strip().lower()
            if ans in ("", "y", "yes"):
                report(counted[0], counted[1], args.boost, None)
                input("Press Enter to exit...")
                return
        matches = ask_int("Matches played", 0)
        cpm = ask_int("Average chests opened per match", 6) if matches else None
        normal = matches * cpm if matches else ask_int("Normal chests opened", 100)
        treasure = ask_int("Chests opened during Treasure Hours", 0)
        report(normal, treasure, args.boost, cpm)
        input("Press Enter to exit...")
        return

    cpm = args.chests_per_match
    normal = args.chests or 0
    if args.matches is not None:
        normal += args.matches * (cpm or 6)
        cpm = cpm or 6
    report(normal, args.treasure_chests, args.boost, cpm)


if __name__ == "__main__":
    main()
