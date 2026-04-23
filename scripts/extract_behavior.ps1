## Universal Behavior XML → YAML Extraction Script
## Converts any behavior XML into the YAML source tree format
## consumed by the HKBuild compiler.
##
## Source priority: hkx2e XMLs (ground truth) → hkxcmd XMLs (legacy fallback)
##
## Usage (from repo root):
##   .\hkbuild\scripts\extract_behavior.ps1 -Name bashbehavior
##   .\hkbuild\scripts\extract_behavior.ps1 -Name sprintbehavior
##   .\hkbuild\scripts\extract_behavior.ps1 -Name 0_master
##   .\hkbuild\scripts\extract_behavior.ps1 -Name bashbehavior -Force   # overwrite existing
##
## Or via HKBuild CLI:
##   hkbuild extract bashbehavior
##   hkbuild extract 0_master --force
##
## Custom source/output (e.g. Character Behaviors Enhanced):
##   .\hkbuild\scripts\extract_behavior.ps1 -Name 0_master -XmlDir ".reference\character behaviors enhanced\xml\meshes\actors\character\behaviors" -OutBase "behavior_src\Character Behaviors Enhanced\character\behaviors" -Force

param(
    [Parameter(Mandatory=$true)]
    [string]$Name,
    [string]$XmlDir,
    [string]$OutBase,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

# Search order: hkx2e XML (preferred) → hkxcmd XML (legacy fallback)
$Hkx2eDir = ".reference\vanilla skyrim behavior source\xml\meshes\actors\character\behaviors"
$HkxcmdDir = ".reference\Destructible Behavior XMLs\character\behaviors"

$DefaultOutBase = "behavior_src\vanilla\character\behaviors"
if ($OutBase) { $DefaultOutBase = $OutBase }

$XmlPath = $null
$OutDir  = $null

if ($XmlDir) {
    # Custom XML directory: search there only
    if (Test-Path "$XmlDir\$Name.xml") {
        $XmlPath = "$XmlDir\$Name.xml"
        $OutDir  = "$DefaultOutBase\$Name.hkx"
    } elseif (Test-Path "$XmlDir\${Name}behavior.xml") {
        $XmlPath = "$XmlDir\${Name}behavior.xml"
        $OutDir  = "$DefaultOutBase\${Name}behavior.hkx"
    }
} else {
    # Default: try hkx2e XMLs first (exact name, then with "behavior" suffix)
    foreach ($dir in @($Hkx2eDir, $HkxcmdDir)) {
        if (Test-Path "$dir\$Name.xml") {
            $XmlPath = "$dir\$Name.xml"
            $OutDir  = "$DefaultOutBase\$Name.hkx"
            break
        }
        if (Test-Path "$dir\${Name}behavior.xml") {
            $XmlPath = "$dir\${Name}behavior.xml"
            $OutDir  = "$DefaultOutBase\${Name}behavior.hkx"
            break
        }
    }
}
if ($null -eq $XmlPath) {
    $searchedIn = if ($XmlDir) { $XmlDir } else { "hkx2e and hkxcmd reference directories" }
    Write-Error "Cannot find XML for '$Name'. Searched $searchedIn."
    exit 1
}

if ((Test-Path $OutDir) -and -not $Force) {
    Write-Error "Output directory '$OutDir' already exists. Use -Force to overwrite."
    exit 1
}

Write-Host "Source:  $XmlPath"
Write-Host "Output:  $OutDir"
Write-Host ""

# ═══════════════════════════════════════════════════════════════
#  PARSE XML
# ═══════════════════════════════════════════════════════════════

$xml = [xml](Get-Content $XmlPath -Raw)
$allObjects = $xml.hkpackfile.hksection.hkobject

# Build lookups: ID→object, ID→name, ID→class
$idToObj   = @{}
$idToName  = @{}
$idToClass = @{}
foreach ($obj in $allObjects) {
    $id  = $obj.name
    $cls = $obj.class
    $idToObj[$id]   = $obj
    $idToClass[$id] = $cls
    $nameNode = $obj.hkparam | Where-Object { $_.name -eq "name" }
    if ($nameNode) { $idToName[$id] = $nameNode.'#text' }
}

# ═══════════════════════════════════════════════════════════════
#  HELPERS
# ═══════════════════════════════════════════════════════════════

function P($obj, $pname) {
    $p = $obj.hkparam | Where-Object { $_.name -eq $pname }
    if ($null -eq $p) { return $null }
    return $p.'#text'
}

function PNode($obj, $pname) {
    return $obj.hkparam | Where-Object { $_.name -eq $pname }
}

function RefName($ref) {
    if ($null -eq $ref -or $ref -eq "null") { return "null" }
    if ($idToName.ContainsKey($ref)) { return $idToName[$ref] }
    return "null"
}

function F($v) {
    if ($null -eq $v) { return "0.000000" }
    return ([double]$v).ToString("F6")
}

function Bool($v) {
    if ($v -eq "true") { return "true" } else { return "false" }
}

function SafeFileName($name) {
    # Replace characters that are invalid in file names
    return $name -replace '[<>:"/\\|?*]', '_'
}

# Parse ref list from a hkparam text (space-separated #XXXX refs)
function ParseRefList($paramText) {
    if ($null -eq $paramText -or $paramText.Trim() -eq "") { return @() }
    return @($paramText.Trim() -split '\s+' | Where-Object { $_ -and $_.StartsWith("#") })
}

# ═══════════════════════════════════════════════════════════════
#  SKELETON (for named bone weights)
# ═══════════════════════════════════════════════════════════════

$boneNames = @()
$skelFile = "behavior_src\vanilla\character\character assets\skeleton.yaml"
if (Test-Path $skelFile) {
    foreach ($m in [regex]::Matches((Get-Content $skelFile -Raw), '- "([^"]+)"')) {
        $boneNames += $m.Groups[1].Value
    }
}
$boneIndexToName = @{}
for ($i = 0; $i -lt $boneNames.Count; $i++) { $boneIndexToName[$i] = $boneNames[$i] }

# ═══════════════════════════════════════════════════════════════
#  GRAPH DATA (events + variables for name resolution)
# ═══════════════════════════════════════════════════════════════

$gdObj  = $allObjects | Where-Object { $_.class -eq "hkbBehaviorGraphData" }
$vvsObj = $allObjects | Where-Object { $_.class -eq "hkbVariableValueSet" }
$strObj = $allObjects | Where-Object { $_.class -eq "hkbBehaviorGraphStringData" }

$varNames = @()
$evNames  = @()
$evNameLookup  = @{}
$varNameLookup = @{}

if ($strObj) {
    $vn = $strObj.hkparam | Where-Object { $_.name -eq "variableNames" }
    if ($vn.hkcstring) { $varNames = @($vn.hkcstring) }
    $en = $strObj.hkparam | Where-Object { $_.name -eq "eventNames" }
    if ($en.hkcstring) { $evNames = @($en.hkcstring) }
}
for ($i = 0; $i -lt $evNames.Count; $i++)  { $evNameLookup[$i]  = $evNames[$i] }
for ($i = 0; $i -lt $varNames.Count; $i++) { $varNameLookup[$i] = $varNames[$i] }

# ═══════════════════════════════════════════════════════════════
#  BINDINGS HELPER
# ═══════════════════════════════════════════════════════════════

function ExtractBindings($obj) {
    $ref = P $obj "variableBindingSet"
    if ($null -eq $ref -or $ref -eq "null") { return "" }
    $bindObj = $idToObj[$ref]
    if ($null -eq $bindObj) { return "" }
    $bindParam = $bindObj.hkparam | Where-Object { $_.name -eq "bindings" }
    $binds = @($bindParam.hkobject)
    if ($binds.Count -eq 0) { return "" }
    $y = "bindings:`n"
    foreach ($b in $binds) {
        $mp = ($b.hkparam | Where-Object { $_.name -eq "memberPath" }).'#text'
        $vi = [int]($b.hkparam | Where-Object { $_.name -eq "variableIndex" }).'#text'
        $bt = ($b.hkparam | Where-Object { $_.name -eq "bindingType" }).'#text'
        $bi = [int]($b.hkparam | Where-Object { $_.name -eq "bitIndex" }).'#text'
        $vn = if ($varNameLookup.ContainsKey($vi)) { $varNameLookup[$vi] } else { $null }
        $y += "  - memberPath: $mp`n"
        if ($vn) { $y += "    variable: $vn`n" } else { $y += "    variableIndex: $vi`n" }
        if ($bt -ne "BINDING_TYPE_VARIABLE") { $y += "    bindingType: $bt`n" }
        if ($bi -ne -1) { $y += "    bitIndex: $bi`n" }
    }
    return $y
}

# ═══════════════════════════════════════════════════════════════
#  BONE WEIGHTS HELPER (named format)
# ═══════════════════════════════════════════════════════════════

function ExtractBoneWeightsYaml($bwRef) {
    if ($null -eq $bwRef -or $bwRef -eq "null") { return "" }
    $bwObj = $idToObj[$bwRef]
    if ($null -eq $bwObj) { return "" }
    $bwParam = $bwObj.hkparam | Where-Object { $_.name -eq "boneWeights" }
    $bwText = $bwParam.'#text'
    if ($null -eq $bwText -or $bwText.Trim() -eq "") { return "" }
    $vals = $bwText.Trim() -split '\s+'
    $y = "    boneWeights:`n"
    $y += "      named:`n"
    for ($i = 0; $i -lt $vals.Count; $i++) {
        if ([double]$vals[$i] -ne 0.0) {
            $bn = if ($boneIndexToName.ContainsKey($i)) { $boneIndexToName[$i] } else { "bone$i" }
            $y += "        `"$bn`": $(F $vals[$i])`n"
        }
    }
    return $y
}

# ═══════════════════════════════════════════════════════════════
#  EVENT PROPERTIES HELPER (enter/exit notify, inline events)
# ═══════════════════════════════════════════════════════════════

function ExtractEventProperty($evObj) {
    # Inline hkbEventProperty — has id + payload
    $eid = [int]($evObj.hkparam | Where-Object { $_.name -eq "id" }).'#text'
    $payRef = ($evObj.hkparam | Where-Object { $_.name -eq "payload" }).'#text'
    $evName = if ($evNameLookup.ContainsKey($eid)) { $evNameLookup[$eid] } else { $null }
    $result = @{}
    if ($evName) { $result.event = $evName } else { $result.eventId = $eid }
    if ($payRef -and $payRef -ne "null") {
        $payObj = $idToObj[$payRef]
        if ($payObj) {
            $payData = ($payObj.hkparam | Where-Object { $_.name -eq "data" }).'#text'
            if ($payData) { $result.payload = $payData }
        }
    }
    return $result
}

function ExtractNotifyEvents($obj, $paramName) {
    $ref = P $obj $paramName
    if ($null -eq $ref -or $ref -eq "null") { return "" }
    $evArrayObj = $idToObj[$ref]
    if ($null -eq $evArrayObj) { return "" }
    $evParam = $evArrayObj.hkparam | Where-Object { $_.name -eq "events" }
    $evs = @($evParam.hkobject)
    if ($evs.Count -eq 0) { return "" }
    $y = "${paramName}:`n"
    foreach ($ev in $evs) {
        $ep = ExtractEventProperty $ev
        if ($ep.event) { $y += "  - event: $($ep.event)`n" }
        else { $y += "  - eventId: $($ep.eventId)`n" }
        if ($ep.payload) { $y += "    payload: $($ep.payload)`n" }
    }
    return $y
}

# ═══════════════════════════════════════════════════════════════
#  INLINE hkbEventProperty YAML (for generators/modifiers that have event fields)
# ═══════════════════════════════════════════════════════════════

function ExtractInlineEventYaml($obj, $paramName, $indent) {
    $evNode = PNode $obj $paramName
    if ($null -eq $evNode) { return "" }
    $inner = $evNode.hkobject
    if ($null -eq $inner) { return "" }
    $eid = [int]($inner.hkparam | Where-Object { $_.name -eq "id" }).'#text'
    $payRef = ($inner.hkparam | Where-Object { $_.name -eq "payload" }).'#text'
    $evName = if ($evNameLookup.ContainsKey($eid)) { $evNameLookup[$eid] } else { $null }
    $y = ""
    if ($eid -eq -1) {
        $y += "${indent}${paramName}:`n"
        $y += "${indent}  id: -1`n"
    } else {
        $y += "${indent}${paramName}:`n"
        if ($evName) { $y += "${indent}  event: $evName`n" } else { $y += "${indent}  id: $eid`n" }
    }
    if ($payRef -and $payRef -ne "null") {
        $payObj = $idToObj[$payRef]
        if ($payObj) {
            $payData = ($payObj.hkparam | Where-Object { $_.name -eq "data" }).'#text'
            if ($payData) { $y += "${indent}  payload: $payData`n" }
        }
    }
    return $y
}

# ═══════════════════════════════════════════════════════════════
#  STATE MACHINE → STATE LOOKUP (for toState name resolution)
# ═══════════════════════════════════════════════════════════════

$smStateMap = @{}
foreach ($smObj in ($allObjects | Where-Object { $_.class -eq "hkbStateMachine" })) {
    $smName = P $smObj "name"
    $statesParam = $smObj.hkparam | Where-Object { $_.name -eq "states" }
    $stateRefs = ParseRefList $statesParam.'#text'
    $map = @{}
    foreach ($ref in $stateRefs) {
        $stateObj = $idToObj[$ref]
        if ($stateObj) {
            $sName = P $stateObj "name"
            $sId = [int](P $stateObj "stateId")
            $map[$sId] = $sName
        }
    }
    $smStateMap[$smName] = $map
}

function FindOwnerSM($stateName) {
    foreach ($smName in $smStateMap.Keys) {
        foreach ($sId in $smStateMap[$smName].Keys) {
            if ($smStateMap[$smName][$sId] -eq $stateName) { return $smName }
        }
    }
    return $null
}

# ═══════════════════════════════════════════════════════════════
#  CREATE OUTPUT DIRECTORIES
# ═══════════════════════════════════════════════════════════════

foreach ($dir in @("clips","generators","selectors","states","transitions","modifiers","tagging","references","data")) {
    New-Item -ItemType Directory -Force -Path "$OutDir\$dir" | Out-Null
}

# ═══════════════════════════════════════════════════════════════
#  EXTRACT: behavior.yaml
# ═══════════════════════════════════════════════════════════════

$bgObj = $allObjects | Where-Object { $_.class -eq "hkbBehaviorGraph" }
$bgName = P $bgObj "name"
$bgRoot = P $bgObj "rootGenerator"
$bgData = P $bgObj "data"
$bgMode = P $bgObj "variableMode"

$behaviorYaml = "packfile:`n"
$behaviorYaml += "  classversion: $($xml.hkpackfile.classversion)`n"
$behaviorYaml += "  contentsversion: `"$($xml.hkpackfile.contentsversion)`"`n"
$behaviorYaml += "`nbehavior:`n"
$behaviorYaml += "  name: `"$bgName`"`n"
$behaviorYaml += "  variableMode: $bgMode`n"
$behaviorYaml += "  rootGenerator: $(RefName $bgRoot)`n"
if ($bgData -and $bgData -ne "null") {
    $behaviorYaml += "  data: graphdata`n"
}
Set-Content "$OutDir\behavior.yaml" $behaviorYaml.TrimEnd() -Encoding UTF8
Write-Host "  behavior.yaml"

# ═══════════════════════════════════════════════════════════════
#  EXTRACT: data/graphdata.yaml
# ═══════════════════════════════════════════════════════════════

if ($gdObj) {
    $varInfosParam = $gdObj.hkparam | Where-Object { $_.name -eq "variableInfos" }
    $varInfoObjs = @($varInfosParam.hkobject)
    $evInfosParam = $gdObj.hkparam | Where-Object { $_.name -eq "eventInfos" }
    $evInfoObjs = @($evInfosParam.hkobject)
    $wordParam = $vvsObj.hkparam | Where-Object { $_.name -eq "wordVariableValues" }
    $wordObjs = @($wordParam.hkobject)

    $gd = "variables:`n"
    for ($i = 0; $i -lt $varNames.Count; $i++) {
        $info = $varInfoObjs[$i]
        $roleObj = $info.hkparam | Where-Object { $_.name -eq "role" }
        $roleInner = $roleObj.hkobject
        $role = ($roleInner.hkparam | Where-Object { $_.name -eq "role" }).'#text'
        $flags = ($roleInner.hkparam | Where-Object { $_.name -eq "flags" }).'#text'
        $type = ($info.hkparam | Where-Object { $_.name -eq "type" }).'#text'
        $val = ($wordObjs[$i].hkparam | Where-Object { $_.name -eq "value" }).'#text'
        $gd += "  - name: $($varNames[$i])`n"
        $gd += "    type: $type`n"
        if ($role -ne "ROLE_DEFAULT") { $gd += "    role: $role`n" }
        if ($flags -and $flags -ne "0" -and $flags -ne "FLAG_NONE") { $gd += "    roleFlags: $flags`n" }
        $gd += "    value: $val`n"
    }

    $gd += "`nevents:`n"
    for ($i = 0; $i -lt $evNames.Count; $i++) {
        $info = $evInfoObjs[$i]
        $eflags = ($info.hkparam | Where-Object { $_.name -eq "flags" }).'#text'
        $gd += "  - name: $($evNames[$i])`n"
        if ($eflags -ne "0") { $gd += "    flags: $eflags`n" }
    }

    Set-Content "$OutDir\data\graphdata.yaml" $gd.TrimEnd() -Encoding UTF8
    Write-Host "  data/graphdata.yaml ($($varNames.Count) vars, $($evNames.Count) events)"
}

# ═══════════════════════════════════════════════════════════════
#  EXTRACT: clips/*.yaml
# ═══════════════════════════════════════════════════════════════

$clipCount = 0
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "hkbClipGenerator" })) {
    $cname = P $obj "name"
    $y = "class: hkbClipGenerator`n"
    $y += "name: $cname`n"
    $y += "animationName: $(P $obj 'animationName')`n"
    $y += "mode: $(P $obj 'mode')`n"
    $y += "playbackSpeed: $(F (P $obj 'playbackSpeed'))`n"

    $ud = P $obj "userData"
    if ($ud -and [int]$ud -ne 0) { $y += "userData: $ud`n" }

    $st = P $obj "startTime"
    if ($st -and [double]$st -ne 0.0) { $y += "startTime: $(F $st)`n" }

    $csa = P $obj "cropStartAmountLocalTime"
    if ($csa -and [double]$csa -ne 0.0) { $y += "cropStartAmountLocalTime: $(F $csa)`n" }

    $cea = P $obj "cropEndAmountLocalTime"
    if ($cea -and [double]$cea -ne 0.0) { $y += "cropEndAmountLocalTime: $(F $cea)`n" }

    $ed = P $obj "enforcedDuration"
    if ($ed -and [double]$ed -ne 0.0) { $y += "enforcedDuration: $(F $ed)`n" }

    $uctf = P $obj "userControlledTimeFraction"
    if ($uctf -and [double]$uctf -ne 0.0) { $y += "userControlledTimeFraction: $(F $uctf)`n" }

    $flags = P $obj "flags"
    if ($flags -and $flags -ne "0" -and $flags -ne "FLAG_NONE") { $y += "flags: $flags`n" }

    $abi = P $obj "animationBindingIndex"
    if ($abi -and [int]$abi -ne -1) { $y += "animationBindingIndex: $abi`n" }

    # Triggers
    $trigRef = P $obj "triggers"
    if ($trigRef -and $trigRef -ne "null") {
        $trigObj = $idToObj[$trigRef]
        $trigParam = $trigObj.hkparam | Where-Object { $_.name -eq "triggers" }
        $trigs = @($trigParam.hkobject)
        if ($trigs.Count -gt 0) {
            $y += "triggers:`n"
            foreach ($t in $trigs) {
                $lt = ($t.hkparam | Where-Object { $_.name -eq "localTime" }).'#text'
                $evObj = ($t.hkparam | Where-Object { $_.name -eq "event" }).hkobject
                $ep = ExtractEventProperty $evObj
                $rtec = ($t.hkparam | Where-Object { $_.name -eq "relativeToEndOfClip" }).'#text'
                $acyclic = ($t.hkparam | Where-Object { $_.name -eq "acyclic" }).'#text'
                $isAnnot = ($t.hkparam | Where-Object { $_.name -eq "isAnnotation" }).'#text'

                $y += "  - localTime: $(F $lt)`n"
                if ($ep.event) { $y += "    event: $($ep.event)`n" }
                else { $y += "    eventId: $($ep.eventId)`n" }
                if ($ep.payload) { $y += "    payload: $($ep.payload)`n" }
                if ($rtec -eq "true") { $y += "    relativeToEndOfClip: true`n" }
                if ($acyclic -eq "true") { $y += "    acyclic: true`n" }
                if ($isAnnot -eq "true") { $y += "    isAnnotation: true`n" }
            }
        }
    }

    $bindings = ExtractBindings $obj
    if ($bindings) { $y += $bindings }

    Set-Content "$OutDir\clips\$(SafeFileName $cname).yaml" $y.TrimEnd() -Encoding UTF8
    $clipCount++
}
Write-Host "  $clipCount clips"

# ═══════════════════════════════════════════════════════════════
#  EXTRACT: generators/*.yaml (hkbBlenderGenerator)
# ═══════════════════════════════════════════════════════════════

$blenderCount = 0
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "hkbBlenderGenerator" })) {
    $bname = P $obj "name"
    $y = "class: hkbBlenderGenerator`n"
    $y += "name: $bname`n"

    $ud = P $obj "userData"
    if ($ud -and [int]$ud -ne 0) { $y += "userData: $ud`n" }

    $y += "flags: $(P $obj 'flags')`n"
    $y += "subtractLastChild: $(P $obj 'subtractLastChild')`n"

    $bindings = ExtractBindings $obj
    if ($bindings) { $y += $bindings }

    $childParam = $obj.hkparam | Where-Object { $_.name -eq "children" }
    $childRefs = ParseRefList $childParam.'#text'

    $y += "children:`n"
    foreach ($cref in $childRefs) {
        $childObj = $idToObj[$cref]
        $genRef = ($childObj.hkparam | Where-Object { $_.name -eq "generator" }).'#text'
        $weight = ($childObj.hkparam | Where-Object { $_.name -eq "weight" }).'#text'
        $wmw = ($childObj.hkparam | Where-Object { $_.name -eq "worldFromModelWeight" }).'#text'
        $bwRef = ($childObj.hkparam | Where-Object { $_.name -eq "boneWeights" }).'#text'

        $y += "  - generator: $(RefName $genRef)`n"
        $y += "    weight: $(F $weight)`n"
        $y += "    worldFromModelWeight: $(F $wmw)`n"

        $bw = ExtractBoneWeightsYaml $bwRef
        if ($bw) { $y += $bw }
    }

    Set-Content "$OutDir\generators\$(SafeFileName $bname).yaml" $y.TrimEnd() -Encoding UTF8
    $blenderCount++
}
Write-Host "  $blenderCount blender generators"

# ═══════════════════════════════════════════════════════════════
#  EXTRACT: selectors/*.yaml (hkbManualSelectorGenerator)
# ═══════════════════════════════════════════════════════════════

$selCount = 0
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "hkbManualSelectorGenerator" })) {
    $sname = P $obj "name"
    $y = "class: hkbManualSelectorGenerator`n"
    $y += "name: $sname`n"

    $ud = P $obj "userData"
    if ($ud -and [int]$ud -ne 0) { $y += "userData: $ud`n" }

    $bindings = ExtractBindings $obj
    if ($bindings) { $y += $bindings }

    $genParam = $obj.hkparam | Where-Object { $_.name -eq "generators" }
    $genRefs = ParseRefList $genParam.'#text'
    $y += "generators:`n"
    foreach ($ref in $genRefs) { $y += "  - $(RefName $ref)`n" }

    Set-Content "$OutDir\selectors\$(SafeFileName $sname).yaml" $y.TrimEnd() -Encoding UTF8
    $selCount++
}
Write-Host "  $selCount selectors"

# ═══════════════════════════════════════════════════════════════
#  EXTRACT: states/*.yaml (hkbStateMachine + hkbStateMachineStateInfo)
# ═══════════════════════════════════════════════════════════════

# State machines
$smCount = 0

# Transitions helper (must be defined before SM extraction which uses it for wildcardTransitions)
function ExtractTransitions($obj, $paramName, $ownerSM) {
    $ref = P $obj $paramName
    if ($null -eq $ref -or $ref -eq "null") { return "" }
    $transObj = $idToObj[$ref]
    if ($null -eq $transObj) { return "" }
    $transParam = $transObj.hkparam | Where-Object { $_.name -eq "transitions" }
    $transList = @($transParam.hkobject)
    if ($transList.Count -eq 0) { return "" }

    $stateMap = if ($ownerSM -and $smStateMap.ContainsKey($ownerSM)) { $smStateMap[$ownerSM] } else { @{} }

    $y = "transitions:`n"
    foreach ($t in $transList) {
        $tEvId = [int]($t.hkparam | Where-Object { $_.name -eq "eventId" }).'#text'
        $tToSId = [int]($t.hkparam | Where-Object { $_.name -eq "toStateId" }).'#text'
        $tTransRef = ($t.hkparam | Where-Object { $_.name -eq "transition" }).'#text'
        $tFlags = ($t.hkparam | Where-Object { $_.name -eq "flags" }).'#text'
        $tFromNested = [int]($t.hkparam | Where-Object { $_.name -eq "fromNestedStateId" }).'#text'
        $tToNested = [int]($t.hkparam | Where-Object { $_.name -eq "toNestedStateId" }).'#text'
        $tPrio = [int]($t.hkparam | Where-Object { $_.name -eq "priority" }).'#text'
        $tCondRef = ($t.hkparam | Where-Object { $_.name -eq "condition" }).'#text'

        $toStateName = if ($stateMap.ContainsKey($tToSId)) { $stateMap[$tToSId] } else { $null }
        $evName = if ($evNameLookup.ContainsKey($tEvId)) { $evNameLookup[$tEvId] } else { $null }

        if ($evName) { $y += "  - event: $evName`n" }
        else { $y += "  - eventId: $tEvId`n" }
        if ($toStateName) { $y += "    toState: $toStateName`n" } else { $y += "    toStateId: $tToSId`n" }
        $y += "    transition: $(RefName $tTransRef)`n"
        if ($tFlags -and $tFlags -ne "0" -and $tFlags -ne "FLAG_DISABLE_CONDITION") { $y += "    flags: $tFlags`n" }
        if ($tFromNested -ne 0) { $y += "    fromNestedStateId: $tFromNested`n" }
        if ($tToNested -ne 0) { $y += "    toNestedStateId: $tToNested`n" }
        if ($tPrio -ne 0) { $y += "    priority: $tPrio`n" }

        # Trigger/initiate intervals (only emit when non-default)
        foreach ($intName in @("triggerInterval","initiateInterval")) {
            $intNode = ($t.hkparam | Where-Object { $_.name -eq $intName }).hkobject
            if ($intNode) {
                $eeid = [int]($intNode.hkparam | Where-Object { $_.name -eq "enterEventId" }).'#text'
                $xeid = [int]($intNode.hkparam | Where-Object { $_.name -eq "exitEventId" }).'#text'
                $et = ($intNode.hkparam | Where-Object { $_.name -eq "enterTime" }).'#text'
                $xt = ($intNode.hkparam | Where-Object { $_.name -eq "exitTime" }).'#text'
                if ($eeid -ne -1 -or $xeid -ne -1 -or ([double]$et -ne 0.0) -or ([double]$xt -ne 0.0)) {
                    $y += "    ${intName}:`n"
                    if ($eeid -ne -1) {
                        $eeName = if ($evNameLookup.ContainsKey($eeid)) { $evNameLookup[$eeid] } else { $null }
                        if ($eeName) { $y += "      enterEvent: $eeName`n" } else { $y += "      enterEventId: $eeid`n" }
                    }
                    if ($xeid -ne -1) {
                        $xeName = if ($evNameLookup.ContainsKey($xeid)) { $evNameLookup[$xeid] } else { $null }
                        if ($xeName) { $y += "      exitEvent: $xeName`n" } else { $y += "      exitEventId: $xeid`n" }
                    }
                    if ([double]$et -ne 0.0) { $y += "      enterTime: $(F $et)`n" }
                    if ([double]$xt -ne 0.0) { $y += "      exitTime: $(F $xt)`n" }
                }
            }
        }

        if ($tCondRef -and $tCondRef -ne "null") {
            $condObj = $idToObj[$tCondRef]
            if ($condObj) {
                $condCls = $condObj.class
                if ($condCls -eq "hkbExpressionCondition") {
                    $expr = ($condObj.hkparam | Where-Object { $_.name -eq "expression" }).'#text'
                    $y += "    condition: `"$expr`"`n"
                } elseif ($condCls -eq "hkbStringCondition") {
                    $cs = ($condObj.hkparam | Where-Object { $_.name -eq "conditionString" }).'#text'
                    $y += "    conditionString: `"$cs`"`n"
                }
            }
        }
    }
    return $y
}

foreach ($smObj in ($allObjects | Where-Object { $_.class -eq "hkbStateMachine" })) {
    $smName = P $smObj "name"
    $y = "class: hkbStateMachine`n"
    $y += "name: $smName`n"

    $ud = P $smObj "userData"
    if ($ud -and [int]$ud -ne 0) { $y += "userData: $ud`n" }

    $y += "startStateId: $(P $smObj 'startStateId')`n"

    $retId = P $smObj "returnToPreviousStateEventId"
    if ($retId -and [int]$retId -ne -1) {
        $rn = if ($evNameLookup.ContainsKey([int]$retId)) { $evNameLookup[[int]$retId] } else { $null }
        if ($rn) { $y += "returnToPreviousStateEvent: $rn`n" } else { $y += "returnToPreviousStateEventId: $retId`n" }
    }

    $randId = P $smObj "randomTransitionEventId"
    if ($randId -and [int]$randId -ne -1) {
        $rn = if ($evNameLookup.ContainsKey([int]$randId)) { $evNameLookup[[int]$randId] } else { $null }
        if ($rn) { $y += "randomTransitionEvent: $rn`n" } else { $y += "randomTransitionEventId: $randId`n" }
    }

    $tdId = P $smObj "transitionToNextHigherStateEventId"
    if ($tdId -and [int]$tdId -ne -1) {
        $rn = if ($evNameLookup.ContainsKey([int]$tdId)) { $evNameLookup[[int]$tdId] } else { $null }
        if ($rn) { $y += "transitionToNextHigherStateEvent: $rn`n" } else { $y += "transitionToNextHigherStateEventId: $tdId`n" }
    }

    $tlId = P $smObj "transitionToNextLowerStateEventId"
    if ($tlId -and [int]$tlId -ne -1) {
        $rn = if ($evNameLookup.ContainsKey([int]$tlId)) { $evNameLookup[[int]$tlId] } else { $null }
        if ($rn) { $y += "transitionToNextLowerStateEvent: $rn`n" } else { $y += "transitionToNextLowerStateEventId: $tlId`n" }
    }

    $stm = P $smObj "selfTransitionMode"
    if ($stm -and $stm -ne "SELF_TRANSITION_MODE_NO_TRANSITION") { $y += "selfTransitionMode: $stm`n" }

    $ssm = P $smObj "startStateMode"
    if ($ssm -and $ssm -ne "START_STATE_MODE_DEFAULT") { $y += "startStateMode: $ssm`n" }

    $svi = P $smObj "syncVariableIndex"
    if ($svi -and [int]$svi -ne -1) {
        $svName = if ($varNameLookup.ContainsKey([int]$svi)) { $varNameLookup[[int]$svi] } else { $null }
        if ($svName) { $y += "syncVariable: $svName`n" } else { $y += "syncVariableIndex: $svi`n" }
    }

    $wasi = P $smObj "wrapAroundStateId"
    if ($wasi -eq "true") { $y += "wrapAroundStateId: true`n" }

    $mst = P $smObj "maxSimultaneousTransitions"
    if ($mst -and [int]$mst -ne 32) { $y += "maxSimultaneousTransitions: $mst`n" }

    $bindings = ExtractBindings $smObj
    if ($bindings) { $y += $bindings }

    # Wildcard transitions (on the SM itself)
    $wTransRef = P $smObj "wildcardTransitions"
    if ($wTransRef -and $wTransRef -ne "null") {
        $y += ExtractTransitions $smObj "wildcardTransitions" $smName
    }

    $statesParam = $smObj.hkparam | Where-Object { $_.name -eq "states" }
    $stateRefs = ParseRefList $statesParam.'#text'
    $y += "states:`n"
    foreach ($ref in $stateRefs) { $y += "  - $(RefName $ref)`n" }

    # Use _SM suffix if a state with the same name exists (avoids filename collision).
    $smFileName = SafeFileName $smName
    $stateNames = @($allObjects | Where-Object { $_.class -eq "hkbStateMachineStateInfo" } | ForEach-Object { P $_ "name" })
    if ($smName -in $stateNames) { $smFileName = "${smFileName}_SM" }
    Set-Content "$OutDir\states\${smFileName}.yaml" $y.TrimEnd() -Encoding UTF8
    $smCount++
}
Write-Host "  $smCount state machines"

# States
$stateCount = 0
foreach ($stateObj in ($allObjects | Where-Object { $_.class -eq "hkbStateMachineStateInfo" })) {
    $sName = P $stateObj "name"
    $sId = P $stateObj "stateId"
    $genRef = P $stateObj "generator"

    $y = "class: hkbStateMachineStateInfo`n"
    $y += "name: $sName`n"
    $y += "stateId: $sId`n"
    $y += "generator: $(RefName $genRef)`n"

    $prob = P $stateObj "probability"
    if ($prob -and [double]$prob -ne 1.0) { $y += "probability: $(F $prob)`n" }

    $enable = P $stateObj "enable"
    if ($enable -eq "false") { $y += "enable: false`n" }

    $bindings = ExtractBindings $stateObj
    if ($bindings) { $y += $bindings }

    # Enter/exit notify events
    $enterEvs = ExtractNotifyEvents $stateObj "enterNotifyEvents"
    if ($enterEvs) { $y += $enterEvs }
    $exitEvs = ExtractNotifyEvents $stateObj "exitNotifyEvents"
    if ($exitEvs) { $y += $exitEvs }

    # Transitions
    $ownerSM = FindOwnerSM $sName
    $transRef = P $stateObj "transitions"
    if ($transRef -and $transRef -ne "null") {
        $y += ExtractTransitions $stateObj "transitions" $ownerSM
    }

    Set-Content "$OutDir\states\$(SafeFileName $sName).yaml" $y.TrimEnd() -Encoding UTF8
    $stateCount++
}
Write-Host "  $stateCount states"

# ═══════════════════════════════════════════════════════════════
#  EXTRACT: transitions/*.yaml (hkbBlendingTransitionEffect)
# ═══════════════════════════════════════════════════════════════

$teCount = 0
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "hkbBlendingTransitionEffect" })) {
    $tname = P $obj "name"
    $y = "class: hkbBlendingTransitionEffect`n"
    $y += "name: $tname`n"

    $ud = P $obj "userData"
    if ($ud -and [int]$ud -ne 0) { $y += "userData: $ud`n" }

    $stm = P $obj "selfTransitionMode"
    if ($stm -and $stm -ne "SELF_TRANSITION_MODE_CONTINUE_IF_CYCLIC_BLEND_IF_ACYCLIC") { $y += "selfTransitionMode: $stm`n" }

    $em = P $obj "eventMode"
    if ($em -and $em -ne "EVENT_MODE_DEFAULT") { $y += "eventMode: $em`n" }

    $y += "duration: $(F (P $obj 'duration'))`n"

    $tgstf = P $obj "toGeneratorStartTimeFraction"
    if ($tgstf -and [double]$tgstf -ne 0.0) { $y += "toGeneratorStartTimeFraction: $(F $tgstf)`n" }

    $flags = P $obj "flags"
    if ($flags -and $flags -ne "0" -and $flags -ne "FLAG_NONE") { $y += "flags: $flags`n" }

    $endMode = P $obj "endMode"
    if ($endMode -and $endMode -ne "END_MODE_NONE") { $y += "endMode: $endMode`n" }

    $bc = P $obj "blendCurve"
    if ($bc -and $bc -ne "BLEND_CURVE_SMOOTH") { $y += "blendCurve: $bc`n" }

    $bindings = ExtractBindings $obj
    if ($bindings) { $y += $bindings }

    Set-Content "$OutDir\transitions\$(SafeFileName $tname).yaml" $y.TrimEnd() -Encoding UTF8
    $teCount++
}
Write-Host "  $teCount transition effects"

# ═══════════════════════════════════════════════════════════════
#  EXTRACT: modifiers/*.yaml (hkbModifierGenerator, BSIsActiveModifier, hkbModifierList, etc.)
# ═══════════════════════════════════════════════════════════════

$modCount = 0

# hkbModifierGenerator
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "hkbModifierGenerator" })) {
    $mgName = P $obj "name"
    $y = "class: hkbModifierGenerator`n"
    $y += "name: $mgName`n"
    $ud = P $obj "userData"
    if ($ud -and [int]$ud -ne 0) { $y += "userData: $ud`n" }
    $y += "modifier: $(RefName (P $obj 'modifier'))`n"
    $y += "generator: $(RefName (P $obj 'generator'))`n"
    $bindings = ExtractBindings $obj
    if ($bindings) { $y += $bindings }
    Set-Content "$OutDir\modifiers\$(SafeFileName $mgName).yaml" $y.TrimEnd() -Encoding UTF8
    $modCount++
}

# BSIsActiveModifier
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "BSIsActiveModifier" })) {
    $iamName = P $obj "name"
    $y = "class: BSIsActiveModifier`n"
    $y += "name: $iamName`n"
    $ud = P $obj "userData"
    if ($ud -and [int]$ud -ne 0) { $y += "userData: $ud`n" }
    $enable = P $obj "enable"
    if ($enable -eq "false") { $y += "enable: false`n" }
    $bindings = ExtractBindings $obj
    if ($bindings) { $y += $bindings }
    for ($i = 0; $i -le 4; $i++) {
        $ia = P $obj "bIsActive$i"
        $inv = P $obj "bInvertActive$i"
        if ($ia -eq "true") { $y += "bIsActive${i}: true`n" }
        if ($inv -eq "true") { $y += "bInvertActive${i}: true`n" }
    }
    Set-Content "$OutDir\modifiers\$(SafeFileName $iamName).yaml" $y.TrimEnd() -Encoding UTF8
    $modCount++
}

# hkbModifierList
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "hkbModifierList" })) {
    $mlName = P $obj "name"
    $y = "class: hkbModifierList`n"
    $y += "name: $mlName`n"
    $ud = P $obj "userData"
    if ($ud -and [int]$ud -ne 0) { $y += "userData: $ud`n" }
    $enable = P $obj "enable"
    if ($enable -eq "false") { $y += "enable: false`n" }
    $bindings = ExtractBindings $obj
    if ($bindings) { $y += $bindings }
    $modParam = $obj.hkparam | Where-Object { $_.name -eq "modifiers" }
    $modRefs = ParseRefList $modParam.'#text'
    if ($modRefs.Count -gt 0) {
        $y += "modifiers:`n"
        foreach ($ref in $modRefs) { $y += "  - $(RefName $ref)`n" }
    }
    Set-Content "$OutDir\modifiers\$(SafeFileName $mlName).yaml" $y.TrimEnd() -Encoding UTF8
    $modCount++
}

# hkbEventDrivenModifier
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "hkbEventDrivenModifier" })) {
    $edmName = P $obj "name"
    $y = "class: hkbEventDrivenModifier`n"
    $y += "name: $edmName`n"
    $ud = P $obj "userData"
    if ($ud -and [int]$ud -ne 0) { $y += "userData: $ud`n" }
    $enable = P $obj "enable"
    if ($enable -eq "false") { $y += "enable: false`n" }
    $y += "modifier: $(RefName (P $obj 'modifier'))`n"
    $aeid = [int](P $obj "activateEventId")
    $deid = [int](P $obj "deactivateEventId")
    if ($evNameLookup.ContainsKey($aeid)) { $y += "activateEvent: $($evNameLookup[$aeid])`n" } else { $y += "activateEventId: $aeid`n" }
    if ($evNameLookup.ContainsKey($deid)) { $y += "deactivateEvent: $($evNameLookup[$deid])`n" } else { $y += "deactivateEventId: $deid`n" }
    $abd = P $obj "activeByDefault"
    if ($abd -eq "true") { $y += "activeByDefault: true`n" }
    $bindings = ExtractBindings $obj
    if ($bindings) { $y += $bindings }
    Set-Content "$OutDir\modifiers\$(SafeFileName $edmName).yaml" $y.TrimEnd() -Encoding UTF8
    $modCount++
}

# hkbFootIkControlsModifier — dedicated extractor (nested controlData/gains struct)
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "hkbFootIkControlsModifier" })) {
    $mName = P $obj "name"
    $y = "class: hkbFootIkControlsModifier`n"
    $y += "name: $mName`n"
    $ud = P $obj "userData"
    if ($ud -and [int]$ud -ne 0) { $y += "userData: $ud`n" }
    $enable = P $obj "enable"
    if ($enable -eq "false") { $y += "enable: false`n" }
    $bindings = ExtractBindings $obj
    if ($bindings) { $y += $bindings }
    # controlData → gains (12 floats)
    $cdNode = PNode $obj "controlData"
    if ($null -ne $cdNode -and $null -ne $cdNode.hkobject) {
        $gainsNode = $cdNode.hkobject.hkparam | Where-Object { $_.name -eq "gains" }
        if ($null -ne $gainsNode -and $null -ne $gainsNode.hkobject) {
            $y += "controlData:`n"
            $y += "  gains:`n"
            foreach ($gp in $gainsNode.hkobject.hkparam) {
                $gv = $gp.'#text'
                if ($null -ne $gv) {
                    $y += "    $($gp.name): $(F $gv)`n"
                }
            }
        }
    }
    # legs array
    $legsNode = PNode $obj "legs"
    $legsNum = $legsNode.numelements
    if ($null -ne $legsNode -and $null -ne $legsNode.hkobject -and [int]$legsNum -gt 0) {
        $y += "legs:`n"
        foreach ($leg in @($legsNode.hkobject)) {
            $gp = ($leg.hkparam | Where-Object { $_.name -eq "groundPosition" }).'#text'
            $ve = ($leg.hkparam | Where-Object { $_.name -eq "verticalError" }).'#text'
            $hs = ($leg.hkparam | Where-Object { $_.name -eq "hitSomething" }).'#text'
            $ip = ($leg.hkparam | Where-Object { $_.name -eq "isPlantedMS" }).'#text'
            $y += "  - groundPosition: $gp`n"
            # ungroundedEvent (inline event inside each leg)
            $ueNode = $leg.hkparam | Where-Object { $_.name -eq "ungroundedEvent" }
            if ($null -ne $ueNode -and $null -ne $ueNode.hkobject) {
                $ueId = [int]($ueNode.hkobject.hkparam | Where-Object { $_.name -eq "id" }).'#text'
                $uePayRef = ($ueNode.hkobject.hkparam | Where-Object { $_.name -eq "payload" }).'#text'
                if ($ueId -eq -1) {
                    $y += "    ungroundedEvent:`n"
                    $y += "      id: -1`n"
                } else {
                    $ueName = if ($evNameLookup.ContainsKey($ueId)) { $evNameLookup[$ueId] } else { $null }
                    $y += "    ungroundedEvent:`n"
                    if ($ueName) { $y += "      event: $ueName`n" } else { $y += "      id: $ueId`n" }
                }
                if ($uePayRef -and $uePayRef -ne "null") {
                    $payObj = $idToObj[$uePayRef]
                    if ($payObj) {
                        $payData = ($payObj.hkparam | Where-Object { $_.name -eq "data" }).'#text'
                        if ($payData) { $y += "      payload: $payData`n" }
                    }
                }
            }
            $y += "    verticalError: $(F $ve)`n"
            $y += "    hitSomething: $(Bool $hs)`n"
            $y += "    isPlantedMS: $(Bool $ip)`n"
        }
    }
    $y += "errorOutTranslation: $(P $obj 'errorOutTranslation')`n"
    $y += "alignWithGroundRotation: $(P $obj 'alignWithGroundRotation')`n"
    Set-Content "$OutDir\modifiers\$(SafeFileName $mName).yaml" $y.TrimEnd() -Encoding UTF8
    $modCount++
}

# Generic modifier extractor for simple types with common hkbModifier base
$simpleModifiers = @(
    "BSDirectAtModifier","BSEventOnFalseToTrueModifier","BSEventOnDeactivateModifier",
    "BSEventEveryNEventsModifier","BSInterpValueModifier","BSRagdollContactListenerModifier",
    "BSModifyOnceModifier","BSLookAtModifier","BSSpeedSamplerModifier",
    "hkbDampingModifier","hkbTwistModifier","hkbRotateCharacterModifier",
    "hkbTimerModifier","hkbKeyframeBonesModifier",
    "hkbGetUpModifier","hkbPoweredRagdollControlsModifier","hkbRigidBodyRagdollControlsModifier",
    "hkbEvaluateExpressionModifier","hkbEventsFromRangeModifier"
)
foreach ($cls in $simpleModifiers) {
    foreach ($obj in ($allObjects | Where-Object { $_.class -eq $cls })) {
        $mName = P $obj "name"
        $y = "class: $cls`n"
        $y += "name: $mName`n"
        $ud = P $obj "userData"
        if ($ud -and [int]$ud -ne 0) { $y += "userData: $ud`n" }
        $enable = P $obj "enable"
        if ($enable -eq "false") { $y += "enable: false`n" }
        $bindings = ExtractBindings $obj
        if ($bindings) { $y += $bindings }
        # Emit all non-base hkparam fields
        $baseParams = @("variableBindingSet","userData","name","enable")
        foreach ($p in $obj.hkparam) {
            if ($p.name -in $baseParams) { continue }
            $pText = $p.'#text'
            # Skip SERIALIZE_IGNORED comments (they show as null text)
            if ($null -eq $pText -and $null -eq $p.hkobject) { continue }
            # Inline objects (events, or object arrays like bones/eyeBones)
            if ($p.hkobject) {
                # Detect inline object arrays (numelements attribute or multiple children
                # or first child has fields other than id/payload)
                $numElem = $p.numelements
                $children = @($p.hkobject)
                $isInlineEvent = $false
                if ($children.Count -eq 1 -and $null -eq $numElem) {
                    $fieldNames = @($children[0].hkparam | ForEach-Object { $_.name })
                    if ($fieldNames.Count -eq 2 -and 'id' -in $fieldNames -and 'payload' -in $fieldNames) {
                        $isInlineEvent = $true
                    }
                }
                if ($isInlineEvent) {
                    $evYaml = ExtractInlineEventYaml $obj $p.name ""
                    if ($evYaml) { $y += $evYaml }
                } else {
                    # Inline object array
                    $y += "$($p.name):`n"
                    foreach ($child in $children) {
                        $first = $true
                        foreach ($fp in $child.hkparam) {
                            $fv = $fp.'#text'
                            if ($null -ne $fv) {
                                if ($first) { $y += "  - $($fp.name): $fv`n"; $first = $false }
                                else        { $y += "    $($fp.name): $fv`n" }
                            }
                        }
                    }
                }
                continue
            }
            # References
            if ($pText -and $pText.StartsWith("#")) {
                $y += "$($p.name): $(RefName $pText)`n"
                continue
            }
            # Ref lists
            if ($pText -and $pText.Contains("#")) {
                $refs = ParseRefList $pText
                if ($refs.Count -gt 0) {
                    $y += "$($p.name):`n"
                    foreach ($ref in $refs) { $y += "  - $(RefName $ref)`n" }
                    continue
                }
            }
            # Scalar
            if ($null -ne $pText) { $y += "$($p.name): $pText`n" }
        }
        Set-Content "$OutDir\modifiers\$(SafeFileName $mName).yaml" $y.TrimEnd() -Encoding UTF8
        $modCount++
    }
}

Write-Host "  $modCount modifiers"

# ═══════════════════════════════════════════════════════════════
#  EXTRACT: tagging/*.yaml (BSiStateTaggingGenerator)
# ═══════════════════════════════════════════════════════════════

$stgCount = 0
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "BSiStateTaggingGenerator" })) {
    $stgName = P $obj "name"
    $y = "class: BSiStateTaggingGenerator`n"
    $y += "name: $stgName`n"
    $ud = P $obj "userData"
    if ($ud -and [int]$ud -ne 0) { $y += "userData: $ud`n" }
    $y += "pDefaultGenerator: $(RefName (P $obj 'pDefaultGenerator'))`n"
    $y += "iStateToSetAs: $(P $obj 'iStateToSetAs')`n"
    $y += "iPriority: $(P $obj 'iPriority')`n"
    $bindings = ExtractBindings $obj
    if ($bindings) { $y += $bindings }
    Set-Content "$OutDir\tagging\$(SafeFileName $stgName).yaml" $y.TrimEnd() -Encoding UTF8
    $stgCount++
}
Write-Host "  $stgCount state tagging generators"

# ═══════════════════════════════════════════════════════════════
#  EXTRACT: references/*.yaml (hkbBehaviorReferenceGenerator)
# ═══════════════════════════════════════════════════════════════

$refCount = 0
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "hkbBehaviorReferenceGenerator" })) {
    $brName = P $obj "name"
    $y = "class: hkbBehaviorReferenceGenerator`n"
    $y += "name: $brName`n"
    $ud = P $obj "userData"
    if ($ud -and [int]$ud -ne 0) { $y += "userData: $ud`n" }
    $y += "behaviorName: $(P $obj 'behaviorName')`n"
    $bindings = ExtractBindings $obj
    if ($bindings) { $y += $bindings }
    Set-Content "$OutDir\references\$(SafeFileName $brName).yaml" $y.TrimEnd() -Encoding UTF8
    $refCount++
}
Write-Host "  $refCount behavior references"

# ═══════════════════════════════════════════════════════════════
#  EXTRACT: generators/*.yaml — additional generator types
# ═══════════════════════════════════════════════════════════════

# BSCyclicBlendTransitionGenerator
$cbCount = 0
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "BSCyclicBlendTransitionGenerator" })) {
    $cbName = P $obj "name"
    $y = "class: BSCyclicBlendTransitionGenerator`n"
    $y += "name: $cbName`n"
    $ud = P $obj "userData"
    if ($ud -and [int]$ud -ne 0) { $y += "userData: $ud`n" }
    $y += "pBlenderGenerator: $(RefName (P $obj 'pBlenderGenerator'))`n"
    $y += ExtractInlineEventYaml $obj "EventToFreezeBlendValue" ""
    $y += ExtractInlineEventYaml $obj "EventToCrossBlend" ""
    $y += "fBlendParameter: $(F (P $obj 'fBlendParameter'))`n"
    $y += "fTransitionDuration: $(F (P $obj 'fTransitionDuration'))`n"
    $ebc = P $obj "eBlendCurve"
    if ($ebc -and $ebc -ne "BLEND_CURVE_SMOOTH") { $y += "eBlendCurve: $ebc`n" }
    $bindings = ExtractBindings $obj
    if ($bindings) { $y += $bindings }
    Set-Content "$OutDir\generators\$(SafeFileName $cbName).yaml" $y.TrimEnd() -Encoding UTF8
    $cbCount++
}
if ($cbCount) { Write-Host "  $cbCount cyclic blend generators" }

# BSBoneSwitchGenerator
$bsgCount = 0
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "BSBoneSwitchGenerator" })) {
    $bsgName = P $obj "name"
    $y = "class: BSBoneSwitchGenerator`n"
    $y += "name: $bsgName`n"
    $ud = P $obj "userData"
    if ($ud -and [int]$ud -ne 0) { $y += "userData: $ud`n" }
    $y += "pDefaultGenerator: $(RefName (P $obj 'pDefaultGenerator'))`n"
    $bindings = ExtractBindings $obj
    if ($bindings) { $y += $bindings }
    $childParam = $obj.hkparam | Where-Object { $_.name -eq "ChildrenA" }
    $childRefs = ParseRefList $childParam.'#text'
    if ($childRefs.Count -gt 0) {
        $y += "children:`n"
        foreach ($cref in $childRefs) {
            $childObj = $idToObj[$cref]
            $genRef = ($childObj.hkparam | Where-Object { $_.name -eq "pGenerator" }).'#text'
            $bwRef = ($childObj.hkparam | Where-Object { $_.name -eq "spBoneWeight" }).'#text'
            $y += "  - pGenerator: $(RefName $genRef)`n"
            $bw = ExtractBoneWeightsYaml $bwRef
            if ($bw) { $y += $bw }
            $childBindings = ExtractBindings $childObj
            if ($childBindings) {
                # Indent the bindings block by 4 spaces to nest inside the child list item.
                $indented = ($childBindings -split "`n" | ForEach-Object { if ($_) { "    $_" } }) -join "`n"
                $y += "$indented`n"
            }
        }
    }
    Set-Content "$OutDir\generators\$(SafeFileName $bsgName).yaml" $y.TrimEnd() -Encoding UTF8
    $bsgCount++
}
if ($bsgCount) { Write-Host "  $bsgCount bone switch generators" }

# BSSynchronizedClipGenerator
$syncCount = 0
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "BSSynchronizedClipGenerator" })) {
    $scName = P $obj "name"
    $y = "class: BSSynchronizedClipGenerator`n"
    $y += "name: $scName`n"
    $ud = P $obj "userData"
    if ($ud -and [int]$ud -ne 0) { $y += "userData: $ud`n" }
    $y += "pClipGenerator: $(RefName (P $obj 'pClipGenerator'))`n"
    $y += "SyncAnimPrefix: $(P $obj 'SyncAnimPrefix')`n"
    $bindings = ExtractBindings $obj
    if ($bindings) { $y += $bindings }
    $baseParams = @("variableBindingSet","userData","name","pClipGenerator","SyncAnimPrefix")
    foreach ($p in $obj.hkparam) {
        if ($p.name -in $baseParams) { continue }
        $pText = $p.'#text'
        if ($null -eq $pText) { continue }
        $y += "$($p.name): $pText`n"
    }
    Set-Content "$OutDir\generators\$(SafeFileName $scName).yaml" $y.TrimEnd() -Encoding UTF8
    $syncCount++
}
if ($syncCount) { Write-Host "  $syncCount synchronized clips" }

# BSOffsetAnimationGenerator
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "BSOffsetAnimationGenerator" })) {
    $oagName = P $obj "name"
    $y = "class: BSOffsetAnimationGenerator`n"
    $y += "name: $oagName`n"
    $ud = P $obj "userData"
    if ($ud -and [int]$ud -ne 0) { $y += "userData: $ud`n" }
    $defGen = P $obj "pDefaultGenerator"
    if ($defGen) { $y += "pDefaultGenerator: $(RefName $defGen)`n" }
    $offClip = P $obj "pOffsetClipGenerator"
    if ($offClip) { $y += "pOffsetClipGenerator: $(RefName $offClip)`n" }
    $fov = P $obj "fOffsetVariable"
    if ($null -ne $fov) { $y += "fOffsetVariable: $(F $fov)`n" }
    $fors = P $obj "fOffsetRangeStart"
    if ($null -ne $fors) { $y += "fOffsetRangeStart: $(F $fors)`n" }
    $fore = P $obj "fOffsetRangeEnd"
    if ($null -ne $fore) { $y += "fOffsetRangeEnd: $(F $fore)`n" }
    $bindings = ExtractBindings $obj
    if ($bindings) { $y += $bindings }
    Set-Content -Path "$OutDir\generators\$(SafeFileName $oagName).yaml" -Value $y.TrimEnd() -Encoding UTF8
}

# hkbPoseMatchingGenerator
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "hkbPoseMatchingGenerator" })) {
    $pmName = P $obj "name"
    $y = "class: hkbPoseMatchingGenerator`n"
    $y += "name: $pmName`n"
    $ud = P $obj "userData"
    if ($ud -and [int]$ud -ne 0) { $y += "userData: $ud`n" }
    $bindings = ExtractBindings $obj
    if ($bindings) { $y += $bindings }

    # Blender base params
    $rpwt = P $obj "referencePoseWeightThreshold"
    if ($null -ne $rpwt) { $y += "referencePoseWeightThreshold: $(F $rpwt)`n" }
    $bp = P $obj "blendParameter"
    if ($null -ne $bp) { $y += "blendParameter: $(F $bp)`n" }
    $mcbpMin = P $obj "minCyclicBlendParameter"
    if ($null -ne $mcbpMin) { $y += "minCyclicBlendParameter: $(F $mcbpMin)`n" }
    $mcbpMax = P $obj "maxCyclicBlendParameter"
    if ($null -ne $mcbpMax) { $y += "maxCyclicBlendParameter: $(F $mcbpMax)`n" }
    $ismc = P $obj "indexOfSyncMasterChild"
    if ($null -ne $ismc) { $y += "indexOfSyncMasterChild: $ismc`n" }
    $fl = P $obj "flags"
    if ($null -ne $fl) { $y += "flags: $fl`n" }
    $slc = P $obj "subtractLastChild"
    if ($null -ne $slc) { $y += "subtractLastChild: $slc`n" }

    # Children (same as hkbBlenderGenerator)
    $childParam = $obj.hkparam | Where-Object { $_.name -eq "children" }
    $childText = if ($childParam) { $childParam.'#text' } else { $null }
    if (-not $childText -and $childParam) { $childText = $childParam.InnerText }
    $childRefs = if ($childText) { ParseRefList $childText } else { @() }
    if ($childRefs.Count -gt 0) {
        $y += "children:`n"
        foreach ($cref in $childRefs) {
            $childObj = $idToObj[$cref]
            if ($null -eq $childObj) { $y += "  - generator: MISSING`n"; continue }
            $genRef = ($childObj.hkparam | Where-Object { $_.name -eq "generator" }).'#text'
            $weightP = $childObj.hkparam | Where-Object { $_.name -eq "weight" }
            $weight = if ($weightP) { $weightP.'#text' } else { "1.000000" }
            $wmwP = $childObj.hkparam | Where-Object { $_.name -eq "worldFromModelWeight" }
            $wmw = if ($wmwP) { $wmwP.'#text' } else { "0.000000" }
            $bwRef = ($childObj.hkparam | Where-Object { $_.name -eq "boneWeights" }).'#text'
            $y += "  - generator: $(RefName $genRef)`n"
            $y += "    weight: $(F $weight)`n"
            $y += "    worldFromModelWeight: $(F $wmw)`n"
            $bw = ExtractBoneWeightsYaml $bwRef
            if ($bw) { $y += $bw }
        }
    }

    # Pose matching params
    $wfmr = P $obj "worldFromModelRotation"
    if ($wfmr) { $y += "worldFromModelRotation: $wfmr`n" }
    $bs = P $obj "blendSpeed"
    if ($bs) { $y += "blendSpeed: $(F $bs)`n" }
    $msts = P $obj "minSpeedToSwitch"
    if ($msts) { $y += "minSpeedToSwitch: $(F $msts)`n" }
    $mstne = P $obj "minSwitchTimeNoError"
    if ($mstne) { $y += "minSwitchTimeNoError: $(F $mstne)`n" }
    $mstfe = P $obj "minSwitchTimeFullError"
    if ($mstfe) { $y += "minSwitchTimeFullError: $(F $mstfe)`n" }
    $spei = P $obj "startPlayingEventId"
    if ($null -ne $spei -and [int]$spei -ge 0 -and $evNameLookup.ContainsKey([int]$spei)) {
        $y += "startPlayingEvent: $($evNameLookup[[int]$spei])`n"
    } elseif ($null -ne $spei) {
        $y += "startPlayingEventId: $spei`n"
    }
    $smei = P $obj "startMatchingEventId"
    if ($null -ne $smei -and [int]$smei -ge 0 -and $evNameLookup.ContainsKey([int]$smei)) {
        $y += "startMatchingEvent: $($evNameLookup[[int]$smei])`n"
    } elseif ($null -ne $smei) {
        $y += "startMatchingEventId: $smei`n"
    }
    $rbi = P $obj "rootBoneIndex"
    if ($null -ne $rbi) { $y += "rootBoneIndex: $rbi`n" }
    $obi = P $obj "otherBoneIndex"
    if ($null -ne $obi) { $y += "otherBoneIndex: $obi`n" }
    $abi = P $obj "anotherBoneIndex"
    if ($null -ne $abi) { $y += "anotherBoneIndex: $abi`n" }
    $pi = P $obj "pelvisIndex"
    if ($null -ne $pi) { $y += "pelvisIndex: $pi`n" }
    $mode = P $obj "mode"
    if ($mode) { $y += "mode: $mode`n" }

    Set-Content "$OutDir\generators\$(SafeFileName $pmName).yaml" $y.TrimEnd() -Encoding UTF8
}

# ═══════════════════════════════════════════════════════════════
#  EXTRACT: data/*.yaml — hkbExpressionDataArray
# ═══════════════════════════════════════════════════════════════

$edaCount = 0
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "hkbExpressionDataArray" })) {
    $edaId = $obj.name
    # Find the modifier that references this
    $ownerName = $null
    foreach ($modObj in ($allObjects | Where-Object { $_.class -eq "hkbEvaluateExpressionModifier" })) {
        $exprRef = P $modObj "expressions"
        if ($exprRef -eq $edaId) { $ownerName = P $modObj "name"; break }
    }
    if (-not $ownerName) { $ownerName = "expressionData_$edaId" }

    $edParam = $obj.hkparam | Where-Object { $_.name -eq "expressionsData" }
    $eds = @($edParam.hkobject)
    $y = "class: hkbExpressionDataArray`n"
    $y += "name: $ownerName`n"
    if ($eds.Count -gt 0) {
        $y += "expressionsData:`n"
        foreach ($ed in $eds) {
            $expr = ($ed.hkparam | Where-Object { $_.name -eq "expression" }).'#text'
            $aeid = [int]($ed.hkparam | Where-Object { $_.name -eq "assignmentVariableIndex" }).'#text'
            $aeeid = [int]($ed.hkparam | Where-Object { $_.name -eq "assignmentEventIndex" }).'#text'
            $evMode = ($ed.hkparam | Where-Object { $_.name -eq "eventMode" }).'#text'
            $y += "  - expression: `"$expr`"`n"
            if ($aeid -ne -1) {
                if ($varNameLookup.ContainsKey($aeid)) { $y += "    assignmentVariable: $($varNameLookup[$aeid])`n" }
                else { $y += "    assignmentVariableIndex: $aeid`n" }
            }
            if ($aeeid -ne -1) {
                if ($evNameLookup.ContainsKey($aeeid)) { $y += "    assignmentEvent: $($evNameLookup[$aeeid])`n" }
                else { $y += "    assignmentEventIndex: $aeeid`n" }
            }
            if ($evMode -and $evMode -ne "EVENT_MODE_SEND_ONCE") { $y += "    eventMode: $evMode`n" }
        }
    }
    Set-Content "$OutDir\data\$(SafeFileName $ownerName)_expressions.yaml" $y.TrimEnd() -Encoding UTF8
    $edaCount++
}
if ($edaCount) { Write-Host "  $edaCount expression data arrays" }

# hkbEventRangeDataArray
$erdCount = 0
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "hkbEventRangeDataArray" })) {
    $erdId = $obj.name
    $ownerName = $null
    foreach ($modObj in ($allObjects | Where-Object { $_.class -eq "hkbEventsFromRangeModifier" })) {
        $rangesRef = P $modObj "eventRanges"
        if ($rangesRef -eq $erdId) { $ownerName = P $modObj "name"; break }
    }
    if (-not $ownerName) { $ownerName = "eventRangeData_$erdId" }

    $edParam = $obj.hkparam | Where-Object { $_.name -eq "eventData" }
    $eds = @($edParam.hkobject)
    $y = "class: hkbEventRangeDataArray`n"
    $y += "name: $ownerName`n"
    if ($eds.Count -gt 0) {
        $y += "eventData:`n"
        foreach ($ed in $eds) {
            $upperBound = ($ed.hkparam | Where-Object { $_.name -eq "upperBound" }).'#text'
            $evNode = ($ed.hkparam | Where-Object { $_.name -eq "event" }).hkobject
            $ep = ExtractEventProperty $evNode
            $evMode = ($ed.hkparam | Where-Object { $_.name -eq "eventMode" }).'#text'
            $y += "  - upperBound: $(F $upperBound)`n"
            if ($ep.event) { $y += "    event: $($ep.event)`n" } else { $y += "    eventId: $($ep.eventId)`n" }
            if ($ep.payload) { $y += "    payload: $($ep.payload)`n" }
            if ($evMode -and $evMode -ne "EVENT_MODE_SEND_ONCE") { $y += "    eventMode: $evMode`n" }
        }
    }
    Set-Content "$OutDir\data\$(SafeFileName $ownerName)_ranges.yaml" $y.TrimEnd() -Encoding UTF8
    $erdCount++
}
if ($erdCount) { Write-Host "  $erdCount event range data arrays" }

# hkbBoneIndexArray
$biaCount = 0
# Build a lookup: boneIndexArray ref ID → owner modifier name + param name
$biaOwnerLookup = @{}
foreach ($modObj in ($allObjects | Where-Object { $_.class -match "Modifier|hkbGetUpModifier|hkbFootIk" })) {
    foreach ($p in $modObj.hkparam) {
        $pText = $p.'#text'
        if ($null -ne $pText -and $pText -match '^#\d+$') {
            $referencedObj = $idToObj[$pText]
            if ($null -ne $referencedObj -and $referencedObj.class -eq "hkbBoneIndexArray") {
                $modName = P $modObj "name"
                $biaOwnerLookup[$pText] = "${modName}_$($p.name)"
            }
        }
    }
}
foreach ($obj in ($allObjects | Where-Object { $_.class -eq "hkbBoneIndexArray" })) {
    $biaId = $obj.name
    $ownerKey = if ($biaOwnerLookup.ContainsKey($biaId)) { $biaOwnerLookup[$biaId] } else { "boneIndexArray_$biaId" }
    $biParam = $obj.hkparam | Where-Object { $_.name -eq "boneIndices" }
    $biText = $biParam.'#text'
    $y = "class: hkbBoneIndexArray`n"
    $y += "name: $ownerKey`n"
    if ($null -ne $biText -and $biText.Trim() -ne "") {
        $vals = @($biText.Trim() -split '\s+')
        if ($boneNames.Count -gt 0) {
            $y += "boneIndices:`n"
            foreach ($v in $vals) {
                $idx = [int]$v
                $bn = if ($boneIndexToName.ContainsKey($idx)) { $boneIndexToName[$idx] } else { "bone$idx" }
                $y += "  - `"$bn`"`n"
            }
        } else {
            $y += "boneIndices: $($biText.Trim())`n"
        }
    } else {
        $y += "boneIndices: []`n"
    }
    Set-Content "$OutDir\data\$(SafeFileName $ownerKey)_boneIndex.yaml" $y.TrimEnd() -Encoding UTF8
    $biaCount++
}
if ($biaCount) { Write-Host "  $biaCount bone index arrays" }

# ═══════════════════════════════════════════════════════════════
#  CLEAN UP EMPTY DIRECTORIES
# ═══════════════════════════════════════════════════════════════

foreach ($dir in @("clips","generators","selectors","states","transitions","modifiers","tagging","references","data")) {
    $path = "$OutDir\$dir"
    if ((Test-Path $path) -and (Get-ChildItem $path -File).Count -eq 0) {
        Remove-Item $path -Force
    }
}

# ═══════════════════════════════════════════════════════════════
#  SUMMARY
# ═══════════════════════════════════════════════════════════════

$totalFiles = (Get-ChildItem $OutDir -Recurse -File).Count
Write-Host ""
Write-Host "Done! Extracted $totalFiles files to $OutDir"
