# Contributing to Aether Loader

Thanks for helping. This is a small project with one maintainer, so the rules are short.

## Build and test

You need the .NET 9 SDK and Block Story from Steam (the build reads the game's own assemblies; they are never part of this repository).

    dotnet build Aether.sln -c Release      # must finish with 0 warnings and 0 errors

If the game is not in the default Steam folder, see [`docs/DEV_SETUP.md`](docs/DEV_SETUP.md). The website (`site/`) builds with plain Python: `python3 site/build.py`.

## Rules

1. **One branch: `main`.** Short-lived branches are fine; delete them after merging.
2. **Nothing that is not ours goes in the repository.** No game files, third-party DLLs, decompiled code, extracted assets, saves, keys or tokens. `.gitignore` and `RepoHygieneTests` enforce the obvious cases.
3. **No dead code.** A type nobody references is deleted, not parked (`DeadCodeTests` fails the build). Allow-list only deliberate public API, with a reason.
4. **One assembly per mod.** Official mods live in `src/Aether.Mods/*` as their own assemblies with `[assembly: AetherMod]`. Code used by two mods goes to the shared assemblies, with a test. Do not add gameplay code to `Aether.UI`.
5. **Keep the API small.** Additions to `Aether.API` are minimal and versioned with SemVer.
6. **Change the contract with the code.** If you touch a mod, update its page in `docs/mods/`. If you change behaviour, add or update a test.
7. **Say what you did not verify.** A pull request states what was tested in the real game and what was not.
   If an AI assistant wrote a substantial part of the change, say so in the pull request description. It is not a judgement; it only helps trace odd bugs. (Not in commit messages.)
8. **Respect the game's license.** Mods and contributions follow the *Block Story Modding & Community Server License Agreement*: public source and documentation for public mods, nothing malicious, no cheating or unfair multiplayer advantages, honest disclosure of multiplayer incompatibilities, and no game assets or decompiled game code in this repository.
9. **Third-party mod ecosystems are not ours.** Nothing from other modders or loaders (code, reimplemented APIs, files, lists) goes into Aether without their written permission, and we do not claim compatibility we have not measured.

## Commits

English, imperative, one idea per commit (`fix(lens): keep the card on screen edge`). Commits carry the author's own name; do not add tool or assistant attribution lines.

## Reporting a problem

Bugs and ideas: open an issue. Security problems: see [`SECURITY.md`](SECURITY.md) and do not open a public issue.
