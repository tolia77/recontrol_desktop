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

# Output paths are RELATIVE to the script so the script is invariant to cwd.
# CS output is sibling-of-protocol within the desktop repo.
CS_OUT="$SCRIPT_DIR/../ReControl.Desktop/Protocol.Generated/FilesCtlTypes.cs"
# TS output is in the frontend repo, accessed via ../../ relative path.
TS_OUT="$SCRIPT_DIR/../../recontrol_frontend/src/pages/DeviceControl/services/files/filesProtocol.generated.ts"

mkdir -p "$(dirname "$CS_OUT")"
mkdir -p "$(dirname "$TS_OUT")"

QUICKTYPE="npx --yes quicktype@^23"

# C# emission: System.Text.Json attributes, List<T> for arrays, long for integers.
$QUICKTYPE \
  --src "$SCHEMA" \
  --src-lang schema \
  --lang cs \
  --namespace ReControl.Desktop.Protocol.Generated \
  --framework SystemTextJson \
  --features attributes-only \
  --array-type list \
  --number-type long \
  -o "$CS_OUT"

# TypeScript emission: types only, no runtime type-check code (we rely on the JSON shape).
$QUICKTYPE \
  --src "$SCHEMA" \
  --src-lang schema \
  --lang ts \
  --just-types \
  --runtime-typecheck-ignore-unknown-properties \
  -o "$TS_OUT"

echo "Regenerated: $CS_OUT"
echo "Regenerated: $TS_OUT"
echo ""
echo "Now commit each output in its respective service repo:"
echo "  cd recontrol_desktop && git add protocol/ ReControl.Desktop/Protocol.Generated/"
echo "  cd recontrol_frontend && git add src/pages/DeviceControl/services/files/filesProtocol.generated.ts"
