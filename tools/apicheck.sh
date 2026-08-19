#!/usr/bin/env bash
# Checks whether Revit API members exist in the 2025 / 2026 / 2027 RevitAPI(.UI).xml docs that
# ship inside the Nice3point NuGet packages (already in the local NuGet cache after a restore).
#
# Usage:
#   tools/apicheck.sh 'M:Autodesk.Revit.DB.Floor.Create(' 'P:Autodesk.Revit.DB.BuiltInFailures.OverlapFailures.WallsOverlap"'
#   tools/apicheck.sh --grep 'Autodesk.Revit.DB.WallUtils\.'      # list matching members (2026)
#   tools/apicheck.sh --doc 'M:Autodesk.Revit.DB.Viewport.Create('  # print the doc block (2026)
#
# Patterns are grep -E regexes matched against <member name="..."> lines. Prefixes:
#   T: type   M: method   P: property   F: field   E: event
# Output per pattern: Y/N for 2025, 2026, 2027 followed by the pattern.

set -u
PKG=~/.nuget/packages
find_xml() {
  local pkg="$1" ver="$2"
  find "$PKG/$pkg" -maxdepth 4 -path "*${ver}*" -name "*.xml" 2>/dev/null | head -1
}
V25=$(ls "$PKG/nice3point.revit.api.revitapi" | grep '^2025' | sort -V | tail -1)
V26=$(ls "$PKG/nice3point.revit.api.revitapi" | grep '^2026' | sort -V | tail -1)
V27=$(ls "$PKG/nice3point.revit.api.revitapi" | grep '^2027' | sort -V | tail -1)
XML25="$(find_xml nice3point.revit.api.revitapi "$V25") $(find_xml nice3point.revit.api.revitapiui "$V25")"
XML26="$(find_xml nice3point.revit.api.revitapi "$V26") $(find_xml nice3point.revit.api.revitapiui "$V26")"
XML27="$(find_xml nice3point.revit.api.revitapi "$V27") $(find_xml nice3point.revit.api.revitapiui "$V27")"

if [ "${1:-}" = "--grep" ]; then
  shift
  grep -hoE "<member name=\"[^\"]*$1[^\"]*\"?" $XML26 | sed 's/<member name="//; s/"$//' | sort -u
  exit 0
fi
if [ "${1:-}" = "--doc" ]; then
  shift
  grep -hE -A25 "<member name=\"[^\"]*$1" $XML26 | sed '/<\/member>/q'
  exit 0
fi

for pat in "$@"; do
  for X in "$XML25" "$XML26" "$XML27"; do
    if grep -qE "<member name=\"[^\"]*${pat}" $X 2>/dev/null; then printf "Y "; else printf "N "; fi
  done
  echo " $pat"
done
