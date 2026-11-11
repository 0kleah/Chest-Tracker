"""
Double-click launcher for the .exe: menu for the chest counter and odds calculator.
Any command-line options are passed straight to the chest counter
(e.g. PirateTokenOdds.exe --key F --debug).
"""

import sys
import traceback

import chest_counter
import pirate_token_odds


def pause():
    try:
        input("\nPress Enter to go back to the menu...")
    except EOFError:
        pass


def run(func, argv):
    old = sys.argv
    sys.argv = [old[0]] + argv
    try:
        func()
    except SystemExit:
        pass
    except Exception:
        traceback.print_exc()
        print("\nSomething went wrong - the error is above.")
    finally:
        sys.argv = old


def menu():
    while True:
        counted = pirate_token_odds.load_counter_counts()
        total = f"{sum(counted):,}" if counted else "0"
        print()
        print("=" * 50)
        print("   FORTNITE PIRATE TOKEN ODDS")
        print(f"   Chests counted so far: {total}")
        print("=" * 50)
        print("   1) Start the chest counter (play Fortnite)")
        print("   2) Show my Pirate Token odds")
        print("   3) Odds calculator (type in your own numbers)")
        print("   4) Test a screenshot (if chests aren't detected)")
        print("   5) Reset my chest count to zero")
        print("   6) Quit")
        try:
            choice = input("\nPick 1-6: ").strip()
        except EOFError:
            return

        if choice == "1":
            print("\nTip: play in Windowed Fullscreen. Press Ctrl+C here to stop counting.\n")
            run(chest_counter.main, [])
            pause()
        elif choice == "2":
            if counted:
                pirate_token_odds.report(counted[0], counted[1], pirate_token_odds.DEFAULT_BOOST, None)
            else:
                print("\nNo chests counted yet - start the chest counter first.")
            pause()
        elif choice == "3":
            run(pirate_token_odds.main, [])
        elif choice == "4":
            path = input("Drag the screenshot file onto this window, then press Enter: ").strip().strip('"')
            if path:
                run(chest_counter.main, ["--test-image", path])
            pause()
        elif choice == "5":
            if input("Really reset your chest count to zero? (y/n): ").strip().lower() == "y":
                chest_counter.save_counts({"normal": 0, "treasure": 0, "log": []})
                print("Reset.")
        elif choice == "6":
            return


if __name__ == "__main__":
    if len(sys.argv) > 1:
        run(chest_counter.main, sys.argv[1:])
        pause()
    else:
        menu()
