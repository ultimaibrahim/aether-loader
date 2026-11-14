# Security

Aether Loader runs code inside the game with the player's permissions, so reports about loading, installation (`aether add`, the installer, downloads) and the website matter.

## Reporting a vulnerability

Please report privately through GitHub: **Security → Report a vulnerability** on this repository. Do not open a public issue.
Include what you found, how to reproduce it and the version (`aether --help` prints it). You will get an answer as soon as the maintainer can; this is a one-person project, so please allow some days.

## Scope

- In scope: the installer and the `aether` CLI (path handling, zip extraction, checksum checks), the loader's discovery of mods, the release packages, and the project website.
- Out of scope: a malicious mod that a user chose to install (mods are full-trust code; the community catalog tries to reduce that risk but cannot remove it), problems in Block Story itself, and other loaders or their mods.
- A vulnerability in **Block Story itself** (especially multiplayer) should be reported privately to its developer, Big Cube Interactive, before anyone discloses it publicly. The game's modding license asks for exactly that.

## Supported versions

Only the latest release receives fixes while the project is in beta.
