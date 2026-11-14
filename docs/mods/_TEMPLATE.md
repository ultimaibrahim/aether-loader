# `<Mod Name>` — Mod Contract

| Field | Value |
| :--- | :--- |
| **ID** | `<aether_id>` |
| **Version** | `<x.y.z>` |
| **Author** | `<author>` |
| **Assembly** | `<Aether.Mods.X.dll>` |
| **Status** | `<✅ / 🟡 / 🧱 / 🧪 / ⛔>` |
| **Canonical runtime** | `<path or assembly>` |
| **Last reviewed** | `<YYYY-MM-DD>` |

## 1. Scope

What this mod owns. What it explicitly does **not** own.

## 2. Entry points and public surface

| Type | Role |
| :--- | :--- |
| `<Type>` | `<role>` |

## 3. Runtime map

Where the code that actually runs lives. If a runner is declared but never instantiated, say so.

## 4. Configuration

| Surface | File | Keys |
| :--- | :--- | :--- |
| `AetherConfig` | `BepInEx/config/aether/<id>.json` | `<Category.Key>` |
| Suite cfg | `BepInEx/config/com.ultimaibrahim.blockstory.suite.cfg` | `[<Section>] <Key>` |

## 5. Hotkeys

| Action | Default | Owner |
| :--- | :--- | :--- |
| `<action>` | `<key>` | `<module>` |

## 6. Invariants (must never break)

- `<rule + why>`

## 7. Performance budget

| Metric | Budget | How measured |
| :--- | :--- | :--- |
| `<metric>` | `<value>` | `<method>` |

## 8. QA gates

| Gate | Kind | Command / check |
| :--- | :--- | :--- |
| `<test>` | Automated | `dotnet test ...` |
| `<check>` | Live | `<scene + action + expected>` |

## 9. Known debt / limits

| Item | Impact | Next action |
| :--- | :--- | :--- |
| `<item>` | `<impact>` | `<action>` |

## 10. Refinement history

| Date | Symptom | Root cause | Rule / change |
| :--- | :--- | :--- | :--- |
| `<date>` | `<what was wrong>` | `<why>` | `<what changed>` |
