#Requires -Version 7.0
param(
    [Parameter(Mandatory = $true)][string]$GameManagedPath,
    [Parameter(Mandatory = $true)][string]$BepInExCorePath,
    [string]$PluginPath
)

# Run in a fresh PowerShell 7 process after building the plugin. These tests
# exercise its real restore methods and PEAK's managed TryGetDataEntry cast without
# creating Unity objects, launching the game, or reading any player saves.
$ErrorActionPreference = 'Stop'
if (!$PluginPath) { $PluginPath = Join-Path $PSScriptRoot '../build/PeakSaveManager.dll' }
$GameManagedPath = (Resolve-Path -LiteralPath $GameManagedPath).Path
$BepInExCorePath = (Resolve-Path -LiteralPath $BepInExCorePath).Path
$resolver = [ResolveEventHandler]{
    param($sender, $eventArgs)
    $file = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll'
    foreach ($directory in @($GameManagedPath, $BepInExCorePath)) {
        $candidate = Join-Path $directory $file
        if (Test-Path -LiteralPath $candidate) {
            return [Reflection.Assembly]::LoadFrom($candidate)
        }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)

try {
    $plugin = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $PluginPath).Path)
    $game = [Reflection.Assembly]::LoadFrom((Join-Path $GameManagedPath 'Assembly-CSharp.dll'))
    $dataType = $game.GetType('ItemInstanceData', $true)
    $optionalType = $game.GetType('OptionableIntItemData', $true)
    $intType = $game.GetType('IntItemData', $true)
    $keyType = $game.GetType('DataEntryKey', $true)
    $usesKey = [Enum]::Parse($keyType, 'ItemUses')
    $petterKey = [Enum]::Parse($keyType, 'PetterItemUses')
    $entryType = $plugin.GetType('PEAKSaveManager.ItemDataEntrySnapshot', $true)
    $restoreMethods = $plugin.GetType('PEAKSaveManager.SaveManagerPlugin', $true).GetMethods(
        [Reflection.BindingFlags]'NonPublic,Static')
    $tryGet = ($dataType.GetMethods() | Where-Object Name -eq 'TryGetDataEntry').MakeGenericMethod($optionalType)
    $caseCount = 0

    function Assert($Condition, [string]$Message) {
        if (!$Condition) { throw $Message }
    }

    function New-Entry([string]$Type, [int]$Value, [bool]$HasValue = $true, [bool]$NumericKey = $false) {
        $entry = [Activator]::CreateInstance($entryType)
        $entry.keyName = if ($NumericKey) { $null } else { 'ItemUses' }
        $entry.keyValue = if ($NumericKey) { [int]$usesKey } else { 0 }
        $entry.valueType = $Type
        $entry.intValue = $Value
        $entry.hasValue = $HasValue
        return $entry
    }

    function Assert-Uses($Data, [bool]$HasData, [int]$Value, [string]$Context) {
        # This is the same generic cast that threw in gameplay before the fix.
        $arguments = [object[]]@($usesKey, $null)
        Assert ($tryGet.Invoke($Data, $arguments)) "$Context`: missing ItemUses"
        $entry = $arguments[1]
        Assert ($entry.GetType() -eq $optionalType) "$Context`: wrong stored type"
        Assert ($entry.HasData -eq $HasData) "$Context`: HasData changed"
        Assert ($entry.Value -eq $Value) "$Context`: expected $Value, got $($entry.Value)"
    }

    foreach ($snapshotName in @('ItemSlotSnapshot', 'GroundItemSnapshot')) {
        $snapshotType = $plugin.GetType("PEAKSaveManager.$snapshotName", $true)
        $restore = @($restoreMethods | Where-Object {
            $_.Name -eq 'ApplyItemUsageData' -and
            $_.GetParameters()[0].ParameterType -eq $dataType -and
            $_.GetParameters()[1].ParameterType -eq $snapshotType
        })
        Assert ($restore.Count -eq 1) "Missing managed restore path for $snapshotName"
        $restore = $restore[0]

        $cases = @(
            @{ Name = 'legacy finite'; Legacy = 3; HasData = $true; Value = 3 },
            @{ Name = 'legacy exhausted'; Legacy = 0; HasData = $true; Value = 0 },
            @{ Name = 'legacy unlimited'; Legacy = -1; HasData = $false; Value = -1 },
            @{ Name = 'legacy invalid negative'; Legacy = -5; HasData = $false; Value = -1 },
            @{ Name = 'generic wins'; Legacy = 99; Entry = (New-Entry 'OptionableIntItemData' 5); HasData = $true; Value = 5 },
            @{ Name = 'generic absent wins'; Legacy = 99; Entry = (New-Entry 'OptionableIntItemData' 7 $false); HasData = $false; Value = 7 },
            @{ Name = 'generic exhausted wins'; Legacy = 99; Entry = (New-Entry 'OptionableIntItemData' 0); HasData = $true; Value = 0 },
            @{ Name = 'old wrongly typed save'; Legacy = 99; Entry = (New-Entry 'IntItemData' 2); HasData = $true; Value = 2 },
            @{ Name = 'old wrongly typed unlimited'; Entry = (New-Entry 'IntItemData' -1); HasData = $false; Value = -1 },
            @{ Name = 'numeric key and qualified type'; Legacy = 99; Entry = (New-Entry $optionalType.AssemblyQualifiedName 6 $true $true); HasData = $true; Value = 6 },
            @{ Name = 'unsupported generic falls back'; Legacy = 4; Entry = (New-Entry 'FloatItemData' 8); HasData = $true; Value = 4 },
            @{ Name = 'unknown generic falls back'; Legacy = 4; Entry = (New-Entry 'MissingItemData' 8); HasData = $true; Value = 4 }
        )
        foreach ($case in $cases) {
            $snapshot = [Activator]::CreateInstance($snapshotType)
            $snapshot.itemUses = $case.Legacy
            $snapshot.petterItemUses = 4
            $snapshot.dataEntries.Add($null)
            if ($case.Entry) { $snapshot.dataEntries.Add($case.Entry) }
            $data = [Activator]::CreateInstance($dataType)
            $restore.Invoke($null, [object[]]@($data, $snapshot))
            Assert-Uses $data $case.HasData $case.Value "$snapshotName / $($case.Name)"
            Assert ($data.data[$petterKey].GetType() -eq $intType) 'PetterItemUses must remain IntItemData'
            Assert ($data.data[$petterKey].Value -eq 4) 'PetterItemUses value changed'
            $caseCount++
        }

        # Missing fields must not manufacture a present value (or erase existing data).
        $snapshot = [Activator]::CreateInstance($snapshotType)
        $snapshot.dataEntries = $null
        $data = [Activator]::CreateInstance($dataType)
        $restore.Invoke($null, [object[]]@($data, $snapshot))
        Assert (!$data.data.ContainsKey($usesKey)) "$snapshotName`: absent value was invented"
        $caseCount++

        $existing = [Activator]::CreateInstance($optionalType)
        $existing.HasData = $false
        $data.data[$usesKey] = $existing
        $restore.Invoke($null, [object[]]@($data, $snapshot))
        Assert ([object]::ReferenceEquals($existing, $data.data[$usesKey])) 'Absent fields replaced existing data'
        $caseCount++

        # A legacy ground snapshot must replace pre-existing live item defaults.
        $snapshot.itemUses = 9
        $restore.Invoke($null, [object[]]@($data, $snapshot))
        Assert-Uses $data $true 9 "$snapshotName / live defaults"
        $caseCount++

        $data.data = $null
        $restore.Invoke($null, [object[]]@($data, $snapshot))
        Assert-Uses $data $true 9 "$snapshotName / uninitialized dictionary"
        $caseCount++

        $restore.Invoke($null, [object[]]@($null, $snapshot))
        $restore.Invoke($null, [object[]]@($data, $null))
        $caseCount += 2
        Write-Output "$snapshotName managed restore checks passed."
    }
    Write-Output "$caseCount item usage regression cases passed using the built plugin and real PEAK managed data types. Unity/Photon gameplay was not exercised."
}
finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
