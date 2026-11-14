#!/usr/bin/env bash
# Runs the Aether CLI from a checkout. Needed because `aether` on PATH may be another program
# (e.g. the "aether" desktop theming app on Omarchy); release zips ship their own ./aether binary.
exec dotnet run -c Release --project "$(cd "$(dirname "$0")/.." && pwd)/tools/Aether.CLI" -- "$@"
