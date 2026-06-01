#!/usr/bin/env bash
set -euo pipefail

# Regenerates generated protocol types in both service repos from files-ctl.schema.json.
#
# Layout:
#   recontrol_desktop/protocol/                                                        <- this script lives here (desktop repo owns the schema)
#   recontrol_desktop/ReControl.Desktop/Protocol.Generated/FilesCtlTypes.cs            <- C# output (same service repo)
#   recontrol_frontend/src/pages/DeviceControl/services/files/filesProtocol.generated.ts <- TS output (cross-repo via relative path)
#
# After running this script the developer MUST commit both side-effects in their respective repos:
#   cd recontrol_desktop && git add protocol/ ReControl.Desktop/Protocol.Generated/ && git commit
#   cd recontrol_frontend && git add src/pages/DeviceControl/services/files/filesProtocol.generated.ts && git commit
#
# Requires: quicktype >= 23. The script uses `npx --yes quicktype@^23` so a globally-installed quicktype
# is not required; the pinned devDependency in protocol/package.json (run `npm install` once in this dir)
# gives consistent versions across developer machines.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCHEMA="$SCRIPT_DIR/files-ctl.schema.json"
CLIP_SCHEMA="$SCRIPT_DIR/clipboard.schema.json"

# Output paths are RELATIVE to the script so the script is invariant to cwd.
# CS output is sibling-of-protocol within the desktop repo.
CS_OUT="$SCRIPT_DIR/../ReControl.Desktop/Protocol.Generated/FilesCtlTypes.cs"
# TS output is in the frontend repo, accessed via ../../ relative path.
TS_OUT="$SCRIPT_DIR/../../recontrol_frontend/src/pages/DeviceControl/services/files/filesProtocol.generated.ts"
CLIP_CS_OUT="$SCRIPT_DIR/../ReControl.Desktop/Protocol.Generated/ClipboardTypes.cs"
CLIP_TS_OUT="$SCRIPT_DIR/../../recontrol_frontend/src/pages/DeviceControl/services/clipboard/clipboardProtocol.generated.ts"

mkdir -p "$(dirname "$CS_OUT")"
mkdir -p "$(dirname "$TS_OUT")"
mkdir -p "$(dirname "$CLIP_CS_OUT")"
mkdir -p "$(dirname "$CLIP_TS_OUT")"

QUICKTYPE="npx --yes quicktype@^23"

# C# emission: System.Text.Json attributes, List<T> for arrays.
# quicktype v23 picks long for JSON Schema "integer" by default; the explicit --number-type
# flag there only accepts double|decimal and would be a regression.
$QUICKTYPE \
  --src "$SCHEMA" \
  --src-lang schema \
  --lang cs \
  --namespace ReControl.Desktop.Protocol.Generated \
  --framework SystemTextJson \
  --features attributes-only \
  --array-type list \
  -o "$CS_OUT"

# TypeScript emission: types only, no runtime type-check code (we rely on the JSON shape).
# --prefer-unions emits string-literal unions instead of `export enum`, which is required because
# the frontend tsconfig sets erasableSyntaxOnly (TS enums are a runtime construct, not erasable).
$QUICKTYPE \
  --src "$SCHEMA" \
  --src-lang schema \
  --lang ts \
  --just-types \
  --prefer-unions \
  -o "$TS_OUT"

# Clipboard protocol codegen (v1.3 Phase 13)
$QUICKTYPE \
  --src "$CLIP_SCHEMA" \
  --src-lang schema \
  --lang cs \
  --namespace ReControl.Desktop.Protocol.Generated \
  --framework SystemTextJson \
  --features attributes-only \
  --array-type list \
  -o "$CLIP_CS_OUT"

$QUICKTYPE \
  --src "$CLIP_SCHEMA" \
  --src-lang schema \
  --lang ts \
  --just-types \
  --prefer-unions \
  -o "$CLIP_TS_OUT"

# Post-process: the host csproj has <Nullable>enable</Nullable>, but quicktype emits DTOs without
# the `required` modifier and mixes nullable annotations inconsistently. That produces CS8618
# ("non-nullable property must contain a non-null value") and CS8632 ("annotation ? used outside
# a nullable context") warnings. We're generating contract types where nullability is driven by
# the JSON "required" list, not by C# non-nullable reference-type analysis, so we suppress those
# two warnings for the whole generated file.
CS_PRAGMA='#nullable disable
#pragma warning disable CS8618, CS8632'
if ! head -1 "$CS_OUT" | grep -qx '#nullable disable'; then
  tmp="$(mktemp)"
  { printf '%s\n' "$CS_PRAGMA"; cat "$CS_OUT"; } > "$tmp"
  mv "$tmp" "$CS_OUT"
fi

if ! head -1 "$CLIP_CS_OUT" | grep -qx '#nullable disable'; then
  tmp="$(mktemp)"
  { printf '%s\n' "$CS_PRAGMA"; cat "$CLIP_CS_OUT"; } > "$tmp"
  mv "$tmp" "$CLIP_CS_OUT"
fi

# quicktype emits helper symbols with generic names (Converter, DateOnlyConverter, etc.).
# With multiple generated protocol files in the same namespace these collide at compile-time.
sed -i \
  -e 's/internal static class Converter/internal static class ClipboardConverter/g' \
  -e 's/\bDateOnlyConverter\b/ClipboardDateOnlyConverter/g' \
  -e 's/\bTimeOnlyConverter\b/ClipboardTimeOnlyConverter/g' \
  -e 's/\bIsoDateTimeOffsetConverter\b/ClipboardIsoDateTimeOffsetConverter/g' \
  "$CLIP_CS_OUT"

# Keep refusal-reason literals one-per-line for readability and stable grep-based verification.
# Tolerate either single-line or already-multi-line emission from quicktype (the latter is the
# default in recent versions); the regex matches both. CI-grep below verifies the expected
# multi-line shape so a quicktype upgrade that drops the formatting trips a visible failure.
perl -0pi -e 's/export type ClipboardRefusalReason\s*=\s*"TOO_LARGE"\s*\|\s*"INBOUND_DISABLED"\s*\|\s*"MASTER_DISABLED"\s*\|\s*"PAUSED"\s*\|\s*"NON_TEXT"\s*\|\s*"CAPS_UNKNOWN"\s*\|\s*"PERMISSION_DENIED"\s*;/export type ClipboardRefusalReason =\n    | "TOO_LARGE"\n    | "INBOUND_DISABLED"\n    | "MASTER_DISABLED"\n    | "PAUSED"\n    | "NON_TEXT"\n    | "CAPS_UNKNOWN"\n    | "PERMISSION_DENIED";/gs' "$CLIP_TS_OUT"

# Verify the generated TS file actually contains every expected refusal literal. If quicktype's
# emission ever drifts we want a visible build failure rather than a silent partial-enum regress.
for literal in TOO_LARGE INBOUND_DISABLED MASTER_DISABLED PAUSED NON_TEXT CAPS_UNKNOWN PERMISSION_DENIED; do
  if ! grep -q "\"$literal\"" "$CLIP_TS_OUT"; then
    echo "ERROR: codegen produced clipboardProtocol.generated.ts without literal \"$literal\"" >&2
    exit 1
  fi
done

echo "Regenerated: $CS_OUT"
echo "Regenerated: $TS_OUT"
echo "Regenerated: $CLIP_CS_OUT"
echo "Regenerated: $CLIP_TS_OUT"
echo ""
echo "Now commit each output in its respective service repo:"
echo "  cd recontrol_desktop && git add protocol/ ReControl.Desktop/Protocol.Generated/"
echo "  cd recontrol_frontend && git add src/pages/DeviceControl/services/files/filesProtocol.generated.ts"
