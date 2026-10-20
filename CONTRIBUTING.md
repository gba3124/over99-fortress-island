# Contributing

Thanks for helping. This mod is played in co-op, so a few rules keep everyone on the same version of the game.

## Before you start

- **Bugs and small fixes:** just open a PR.
- **New features, balance changes, new enemies or bosses:** open an issue first and wait for a reply. It saves you from writing something that won't fit.
- **Translations:** strings live in `HtfFortress/L10n.cs` (English, Traditional Chinese, Simplified Chinese). Fixing wording is welcome as a PR.
- Reporting a bug? Include the mod version (bottom of the **I** panel), whether you were host or client, and `BepInEx/LogOutput.log`.

## Rules

1. **Keep PRs small and about one thing.** Explain what and why in the description.
2. **Run `tests/run.sh` and make sure it passes.** Add a check there for any rule or number you add. If it touches gameplay, run the matching `tools/autotest.sh` scenario and say what you saw.
3. **The host decides everything.** Enemies, damage, money, drops and the vault are host-only; clients just send input. Don't move that logic to clients.
4. **Don't change a network message's format without bumping the version** in `FortressPlugin.cs`. Mismatched versions break connections. The tests pin the message formats on purpose.
5. **Edit the generator, not the output.** The `.obj` and `.mtl` files in `HtfFortress/Models/` come from the scripts in `tools/` (Blender). Change the script and commit the regenerated files with it.
6. **Original work only.** Models, icons and text must be your own or under a licence that allows MIT redistribution. Never commit game files, decompiled game code, or anything from other mods without permission.
7. **Match the surrounding code:** naming, comment style, and the three languages for any player-facing text.
8. **No secrets or personal paths** in commits.

## Commits and PRs

- One clear subject line in plain English, then a short body for the why. Example: `Fix thief dropping fish at the wrong spot`.
- Branch from `main`. Rebase on `main` before asking for review.
- By submitting a PR you agree your work is released under the project's [MIT licence](LICENSE), and you keep the credit line described in the README for any fork.

## Be decent

Be kind and assume good faith. Harassment, spam and rude behaviour get comments removed and people blocked.
