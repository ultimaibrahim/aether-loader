# Third-party notices

Aether Loader is licensed under Apache-2.0 (see `LICENSE` and `NOTICE`). It does not include or redistribute the game or other loaders.

| Component | How it is used | License |
| :--- | :--- | :--- |
| Block Story assemblies (`UnityEngine*`, `Assembly-CSharp`, `Newtonsoft.Json` as shipped by the game) | Referenced at build time from your own installation; never committed or shipped | The game's own terms |
| BepInEx 5.x | Downloaded by the installer from its official release and verified with a SHA-256 checksum; not bundled | LGPL-2.1 (see the BepInEx project) |
| Gloock, Schibsted Grotesk, JetBrains Mono | Website fonts, served from `site/fonts` | SIL Open Font License 1.1 (texts in `site/fonts/OFL-*.txt`) |
| Gloock, Schibsted Grotesk, JetBrains Mono (Latin subsets, static Regular/SemiBold instances) | Embedded in `Aether.Nano.dll` for the in-game UI (`src/Aether.Nano/Brand`) | SIL Open Font License 1.1 (texts in `src/Aether.Nano/Brand/OFL-*.txt`) |

## Trademarks and the game's license

*Block Story* and its logos are the property of Big Cube Interactive LLC. Aether Loader is an independent, unofficial project: it is not affiliated with or endorsed by Big Cube Interactive, and it uses the game's name only to say what it works with. It does not include or redistribute game files, artwork, music, models or textures.

Mods for the game are governed by the *Block Story Modding & Community Server License Agreement*. Among other things it asks that the complete source and documentation of every publicly released mod be public, that mods contain no malicious code, that mods which change multiplayer disclose their incompatibilities, that nothing gives unfair advantages in multiplayer, and that the developer is notified after a public release. Read it before publishing a mod.
