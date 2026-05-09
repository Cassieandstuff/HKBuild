# HKBuild — Verification Process

## Methodology

Every compiled behavior is verified by compiling YAML→XML→HKX and comparing the output binary against the vanilla Skyrim SE HKX file. The goal is **0 semantic diffs** (identical data when both binaries are decompiled back to XML via HKX2E).

### Pipeline
1. **Compile YAML→XML:** `HKBuild <source.hkx> -o build/test_<name>.xml`
2. **Pack XML→HKX:** `HKBuild pack build/test_<name>.xml -o build/test_<name>.hkx`
3. **Binary diff:** Compare output HKX byte-for-byte against vanilla binary
4. **Semantic diff:** Decompile both binaries via `HKBuild convert`, normalize IDs with `#\d+` → `#XX`, diff line-by-line

### HKX2E Float Precision Baseline

HKX2E (the XML↔HKX serializer) introduces small float rounding diffs during text→binary conversion. This is measured by packing the vanilla reference XML and comparing to the original binary. These diffs are **inherent** to HKX2E and represent the floor — our output cannot be better than this.

### Acceptable Diffs

- **Float precision diffs** from HKX2E text→binary conversion (measured per-behavior as "HKX2E baseline")
- **0 semantic diffs** required: when both binaries are decompiled back to XML, they must be identical (after ID normalization)

---

## Current Results

### Binary-Verified Behaviors (0 semantic diffs)

| Behavior | Objects | Size | Byte Diffs | HKX2E Baseline | Status |
|---|---|---|---|---|---|
| `1hm_locomotion.hkx` | 799 | 165,600 ✅ | 177 | 177 | **Perfect** — matches HKX2E baseline |
| `bashbehavior.hkx` | 127 | 35,744 ✅ | 17 | 17 | **Perfect** — matches HKX2E baseline |
| `sprintbehavior.hkx` | 126 | 33,200 ✅ | 5,035 | 5,035 | **Perfect** — matches HKX2E baseline |
| `staggerbehavior.hkx` | 96 | 25,552 ✅ | 4,160 | 4,160 | **Perfect** — matches HKX2E baseline |
| `bow_direction_behavior.hkx` | 177 | 46,064 ✅ | — | — | Exact size match |
| `crossbow_direction_behavior.hkx` | 177 | 46,160 ✅ | — | — | Exact size match |
| `shoutmounted_behavior.hkx` | 139 | 34,192 ✅ | — | — | Exact size match |

### Compiles But Has YAML Data Diffs

These behaviors compile and pack successfully. Remaining diffs are from YAML extraction issues (missing `bone_count`, omitted non-default field values), not emitter bugs.

| Behavior | Objects | Size (ref→ours) | Notes |
|---|---|---|---|
| `shout_behavior.hkx` | 274 | 66,848 → 66,976 | bone_count YAML data |
| `blockbehavior.hkx` | 712 | 193,568 → 194,176 | bone_count YAML data |
| `idlebehavior.hkx` | 1209 | 318,992 → 319,504 | bone_count YAML data |
| `weapequip.hkx` | 238 | 63,616 → 63,056 | YAML data issues |
| `0_master.hkx` | 4256 | 580,896 → 590,192 | YAML data issues |
| `mt_behavior.hkx` | 3978 | 1,149,536 → 1,148,112 | YAML data issues |
| `magicbehavior.hkx` | 1217 | 276,960 → 280,656 | YAML data issues |
| `magicmountedbehavior.hkx` | 520 | 121,488 → 123,472 | YAML data issues |
| `magic_readied_direction_behavior.hkx` | 249 | 67,152 → 2,016 | Major YAML data issue |

### Known YAML Extraction Issues

1. **Missing `bone_count`:** The extraction script expanded named bone weight arrays to 99 elements without preserving the original count. Behaviors that use truncated bone weight arrays (82, 85, 97 elements) need explicit `bone_count:` in their YAML.
2. **Missing `blendParameter: 0.000000`:** The extraction script omitted `blendParameter` when it was `1.0` (the default), but some blenders have `blendParameter=0.0` that was not extracted.
3. **Missing `flags: 0`:** The extraction script omitted transition `flags` when they were `FLAG_DISABLE_CONDITION` (the default), but some transitions have `flags=0` that was not extracted.

---

## Reference Binaries

```
.reference/vanilla skyrim behavior source/binaries/meshes/actors/character/behaviors/
  1hm_locomotion.hkx, bashbehavior.hkx, staggerbehavior.hkx, ... (16 total)
```

These are the original vanilla Skyrim SE HKX files used as ground truth for binary comparison.

---

## Running Verification

### One-command verify (recommended)
```bash
# Compile + pack + binary diff + semantic diff in one step:
hkbuild verify behavior_src/vanilla/character/behaviors/bashbehavior.hkx
hkbuild verify behavior_src/vanilla/character/behaviors/1hm_locomotion.hkx

# With explicit reference binary:
hkbuild verify behavior_src/vanilla/character/behaviors/bashbehavior.hkx --ref path/to/vanilla.hkx
```

Output:
```
  Size:          ref=35,744  ours=35,744  ✓ EXACT MATCH
  Byte diffs:    17
  Semantic diffs: 0  ✓ PERFECT
```

### Manual pipeline (for debugging)
```powershell
$hkb = "hkbuild\src\bin\Release\net9.0\HKBuild.exe"

# Compile YAML to XML
& $hkb "behavior_src\vanilla\character\behaviors\1hm_locomotion.hkx" -o "build\test\1hm_test.xml"

# Pack XML to HKX
& $hkb pack "build\test\1hm_test.xml" -o "build\test\1hm_test.hkx"

# Binary size + byte diff
$ref = [System.IO.File]::ReadAllBytes(".reference\vanilla skyrim behavior source\binaries\meshes\actors\character\behaviors\1hm_locomotion.hkx")
$our = [System.IO.File]::ReadAllBytes("build\test\1hm_test.hkx")
$diffs = 0; for ($i = 0; $i -lt $ref.Length; $i++) { if ($ref[$i] -ne $our[$i]) { $diffs++ } }
Write-Output "Sizes: ref=$($ref.Length) ours=$($our.Length), byte diffs: $diffs"
```

### Batch extraction
```bash
# Convert all vanilla HKX binaries to XML (for extraction):
hkbuild convert-all
hkbuild convert-all --force  # overwrite existing

# Extract a behavior XML to YAML source tree:
hkbuild extract bashbehavior
hkbuild extract 1hm_locomotion --force
```

---

## Next Steps

1. **Fix bone_count YAML data** in remaining behaviors (shout, block, idle, weapequip) — same pattern as 1hm_locomotion fixes
2. **Investigate major YAML issues** in 0_master, mt_behavior, magicbehavior, magicmountedbehavior
3. **Diagnose magic_readied_direction** — output is 2K vs 67K reference, likely missing YAML data

See `class_catalog.md` for the full class type inventory and implementation priority.
