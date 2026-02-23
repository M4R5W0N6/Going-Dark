$ErrorActionPreference = "Stop"

$agentBasePrefabPath = "Assets/TPSBR/Prefabs/Agents/AgentBase.prefab"
$agentBaseMetaPath   = "Assets/TPSBR/Prefabs/Agents/AgentBase.prefab.meta"
$agentBaseSofPath    = "Assets/TPSBR/Prefabs/Agents/AgentBase_SOF_A.prefab"
$agentBaseSofMeta    = "Assets/TPSBR/Prefabs/Agents/AgentBase_SOF_A.prefab.meta"
$sofAgentPrefabPath  = "Assets/TPSBR/Prefabs/Agents/SOF_A.prefab"

function Get-MetaGuidMap {
	$map = @{}
	Get-ChildItem -Path Assets -Recurse -File -Filter "*.meta" | ForEach-Object {
		$line = (Get-Content $_.FullName -TotalCount 3 | Select-String "^guid:\s*([0-9a-f]{32})").Line
		if ($line -match "^guid:\s*([0-9a-f]{32})") {
			$guid = $matches[1]
			$assetPath = $_.FullName.Replace("\", "/")
			if ($assetPath.EndsWith(".meta")) {
				$assetPath = $assetPath.Substring(0, $assetPath.Length - 5)
			}

			$workspace = (Get-Location).Path.Replace("\", "/")
			if ($workspace.EndsWith("/") -eq $false) {
				$workspace += "/"
			}
			if ($assetPath.StartsWith($workspace)) {
				$assetPath = $assetPath.Substring($workspace.Length)
			}

			$map[$guid] = $assetPath
		}
	}
	return $map
}

function Get-ClipRefsFromGoingDarkAnimation {
	$clipRefs = @{}

	$fbxMetas = Get-ChildItem -Path "Assets/_GoingDark/Animation" -Recurse -File -Filter "*.fbx.meta" | Sort-Object `
		@{ Expression = { if (($_.FullName.Replace("\", "/") -like "*/RifleAnimsetPro/Animations/*")) { 0 } else { 1 } } }, `
		@{ Expression = { $_.FullName } }

	$fbxMetas | ForEach-Object {
		$metaLines = Get-Content $_.FullName
		$guidLine = ($metaLines | Select-String "^guid:\s*([0-9a-f]{32})" | Select-Object -First 1).Line
		if ($guidLine -notmatch "^guid:\s*([0-9a-f]{32})") {
			return
		}
		$fbxGuid = $matches[1]

		# Preferred: read exact clip sub-asset fileIDs from FBX meta tables.
		# Newer Unity metas use internalIDToNameTable (with type key 74 for clips).
		# Older metas use fileIDToRecycleName.
		$pendingClipId = $null
		foreach ($line in $metaLines) {
			if ($line -match "^\s+74:\s*(\d+)\s*$") {
				$pendingClipId = [int64]$matches[1]
				continue
			}
			if ($pendingClipId -ne $null -and $line -match "^\s+second:\s*(.+)\s*$") {
				$clipName = $matches[1].Trim()
				if ($pendingClipId -ge 7400000 -and [string]::IsNullOrWhiteSpace($clipName) -eq $false -and $clipRefs.ContainsKey($clipName) -eq $false) {
					$clipRefs[$clipName] = [pscustomobject]@{
						Guid    = $fbxGuid
						FileID  = $pendingClipId
						RefType = 3
					}
				}
				$pendingClipId = $null
				continue
			}
			if ($line -match "^\s+(740\d+):\s*(.+)\s*$") {
				$fileID = [int64]$matches[1]
				$clipName = $matches[2].Trim()
				if ([string]::IsNullOrWhiteSpace($clipName) -eq $false -and $clipRefs.ContainsKey($clipName) -eq $false) {
					$clipRefs[$clipName] = [pscustomobject]@{
						Guid    = $fbxGuid
						FileID  = $fileID
						RefType = 3
					}
				}
			}
		}
	}

	Get-ChildItem -Path "Assets/_GoingDark/Animation" -Recurse -File -Filter "*.anim" | ForEach-Object {
		$metaPath = $_.FullName + ".meta"
		if (Test-Path $metaPath) {
			$guidLine = (Get-Content $metaPath -TotalCount 3 | Select-String "^guid:\s*([0-9a-f]{32})").Line
			if ($guidLine -match "^guid:\s*([0-9a-f]{32})") {
				$animGuid = $matches[1]
				$clipName = [System.IO.Path]::GetFileNameWithoutExtension($_.Name)
				$clipRefs[$clipName] = [pscustomobject]@{
					Guid    = $animGuid
					FileID  = 7400000
					RefType = 2
				}
			}
		}
	}

	return $clipRefs
}

function Resolve-TargetClipName([string]$oldClipName) {
	switch -Regex ($oldClipName) {
		"^MOB1_Stand_Relaxed_Idle"             { return "Pistol_Idle_Relaxed" }
		"^MOB1_Stand_Rlx_Turn_In_Place_L"      { return $null }
		"^MOB1_Stand_Rlx_Turn_In_Place_R"      { return $null }
		"^MOB1_Stand_Relaxed_Look_U90"         { return $null }
		"^MOB1_Stand_Relaxed_Look_D90"         { return $null }
		"^MOB1_Stand_Relaxed_Death"            { return "Rifle_Death_L" }
		"^MOB1_Jog_F_Loop"                     { return "Pistol_RunFwdLoop" }
		"^MOB1_Jog_B_Loop"                     { return "Pistol_RunBwdLoop" }
		"^MOB1_Jog_L_Loop"                     { return "Pistol_StrafeRunLeftLoop" }
		"^MOB1_Jog_R_Loop"                     { return "Pistol_StrafeRunRightLoop" }
		"^MOB1_Jog_FL_Loop"                    { return "Pistol_StrafeRun45LeftLoop" }
		"^MOB1_Jog_FR_Loop"                    { return "Pistol_StrafeRun45RightLoop" }
		"^MOB1_Jog_BL_BkPd_Loop"               { return "Pistol_StrafeRun135LeftLoop" }
		"^MOB1_Jog_BR_BkPd_Loop"               { return "Pistol_StrafeRun135RightLoop" }
		"^MOB1_.*Jump.*Start"                  { return "Pistol_Jump_Platformer_Start" }
		"^MOB1_.*Jump.*Air"                    { return "Pistol_Jump_Platformer_Fall" }
		"^MOB1_.*Jump.*Land"                   { return "Pistol_Jump_Platformer_Land" }
		"^W1_Stand_Aim_Idle"                   { return "Pistol_Idle" }
		"^W1_Stand_Aim_Turn_In_Place_L"        { return $null }
		"^W1_Stand_Aim_Turn_In_Place_R"        { return $null }
		"^W1_Stand_Aim_Point_U90"              { return "Pistol_Look_90U_Additive" }
		"^W1_Stand_Aim_Point_D90"              { return "Pistol_Look_90D_Additive" }
		"^W1_Stand_Fire_Single"                { return "Pistol_ShootOnce" }
		"^W1_Stand_Aim_Reload"                 { return "Pistol_Reload_2" }
		"^W1_(Stand_)?Aim_Equip_Get_From_MOB"  { return $null }
		"^W1_(Stand_)?Aim_Equip_Return_To_MOB" { return $null }
		"^W1_Stand_Relaxed_Death"              { return $null }
		"^W1_Jog_Aim_F_Loop"                   { return "Pistol_WalkFwdLoop" }
		"^W1_Jog_Aim_B_Loop"                   { return "Pistol_WalkBwdLoop" }
		"^W1_Jog_Aim_L_Loop"                   { return "Pistol_StrafeLeftLoop" }
		"^W1_Jog_Aim_R_Loop"                   { return "Pistol_StrafeRightLoop" }
		"^W1_Jog_Aim_FL_Loop"                  { return "Pistol_StrafeLeft45Loop" }
		"^W1_Jog_Aim_FR_Loop"                  { return "Pistol_StrafeRight45Loop" }
		"^W1_Jog_Aim_BL_BkPd_Loop"             { return "Pistol_StrafeLeft135Loop" }
		"^W1_Jog_Aim_BR_BkPd_Loop"             { return "Pistol_StrafeRight135Loop" }
		"^W1_.*Jump.*Start"                    { return "Pistol_Jump_Platformer_Start" }
		"^W1_.*Jump.*Air"                      { return "Pistol_Jump_Platformer_Fall" }
		"^W1_.*Jump.*(End|Land)"               { return "Pistol_Jump_Platformer_Land" }
		"^W2_Stand_Aim_Idle"                   { return "Rifle_Idle" }
		"^W2_Stand_Aim_Turn_In_Place_L"        { return $null }
		"^W2_Stand_Aim_Turn_In_Place_R"        { return $null }
		"^W2_Stand_Aim_Point_U90"              { return "Rifle_Look_90U_Additive" }
		"^W2_Stand_Aim_Point_D90"              { return "Rifle_Look_90D_Additive" }
		"^W2_Stand_Fire_Single"                { return "Rifle_ShootOnce" }
		"^W2_Stand_Aim_Reload"                 { return "Rifle_Reload_2" }
		"^W2_(Stand_)?Aim_Equip_Get_From_MOB"  { return $null }
		"^W2_(Stand_)?Aim_Equip_Return_To_MOB" { return $null }
		"^W2_Stand_Relaxed_Death"              { return $null }
		"^W2_Jog_Aim_F_Loop"                   { return "Rifle_WalkFwdLoop" }
		"^W2_Jog_Aim_B_Loop"                   { return "Rifle_WalkBwdLoop" }
		"^W2_Jog_Aim_L_Loop"                   { return "Rifle_StrafeLeftLoop" }
		"^W2_Jog_Aim_R_Loop"                   { return "Rifle_StrafeRightLoop" }
		"^W2_Jog_Aim_FL_Loop"                  { return "Rifle_StrafeLeft45Loop" }
		"^W2_Jog_Aim_FR_Loop"                  { return "Rifle_StrafeRight45Loop" }
		"^W2_Jog_Aim_BL_BkPd_Loop"             { return "Rifle_StrafeLeft135Loop" }
		"^W2_Jog_Aim_BR_BkPd_Loop"             { return "Rifle_StrafeRight135Loop" }
		"^W2_.*Jump.*Start"                    { return "Rifle_Jump_Platformer_Start" }
		"^W2_.*Jump.*Air"                      { return "Rifle_Jump_Platformer_Fall" }
		"^W2_.*Jump.*(End|Land)"               { return "Rifle_Jump_Platformer_Land" }
		"^GrenadeArm$"                         { return $null }
		"^GrenadeEquip$"                       { return $null }
		"^GrenadeHold$"                        { return $null }
		"^GrenadeThrow$"                       { return $null }
		"^GrenadeReload$"                      { return $null }
		"^EquipRifle$"                         { return $null }
		"^HolsterRifle$"                       { return $null }
		"^JetpackIdle$"                        { return $null }
		"^JetpackUnpack$"                      { return $null }
		default                                 { return $null }
	}
}

if ((Test-Path $agentBasePrefabPath) -eq $false) { throw "Missing $agentBasePrefabPath" }
if ((Test-Path $agentBaseMetaPath) -eq $false)   { throw "Missing $agentBaseMetaPath" }
if ((Test-Path $sofAgentPrefabPath) -eq $false)  { throw "Missing $sofAgentPrefabPath" }

$guidToAsset = Get-MetaGuidMap
$newClipRefs = Get-ClipRefsFromGoingDarkAnimation

$agentBaseMetaLines = Get-Content $agentBaseMetaPath
$oldBaseGuidLine = ($agentBaseMetaLines | Select-String "^guid:\s*([0-9a-f]{32})").Line
if ($oldBaseGuidLine -notmatch "^guid:\s*([0-9a-f]{32})") {
	throw "Failed to resolve AgentBase guid from $agentBaseMetaPath"
}
$oldBaseGuid = $matches[1]
$newBaseGuid = [Guid]::NewGuid().ToString("N")

$sofSourceText = Get-Content $sofAgentPrefabPath -Raw
$sofBaseGuidMatch = [regex]::Match($sofSourceText, "target:\s*\{fileID:\s*57946675724828624,\s*guid:\s*([0-9a-f]{32}),\s*type:\s*3\}")
$oldSofBaseGuid = $null
if ($sofBaseGuidMatch.Success) {
	$oldSofBaseGuid = $sofBaseGuidMatch.Groups[1].Value
} else {
	$oldSofBaseGuid = $oldBaseGuid
}

$agentBaseText = Get-Content $agentBasePrefabPath -Raw
$oldType2Guids = Select-String -Path $agentBasePrefabPath -Pattern "guid:\s*([0-9a-f]{32}),\s*type:\s*2" | ForEach-Object {
	if ($_.Line -match "guid:\s*([0-9a-f]{32}),\s*type:\s*2") {
		$matches[1]
	}
} | Sort-Object -Unique

$replacementCount = 0
$missingTargets = @()

foreach ($oldGuid in $oldType2Guids) {
	if ($guidToAsset.ContainsKey($oldGuid) -eq $false) {
		continue
	}

	$oldAssetPath = $guidToAsset[$oldGuid]
	if ($oldAssetPath -notlike "*.anim") {
		continue
	}

	$oldClipName = [System.IO.Path]::GetFileNameWithoutExtension($oldAssetPath)
	$targetClipName = Resolve-TargetClipName $oldClipName
	if ([string]::IsNullOrWhiteSpace($targetClipName)) {
		continue
	}

	if ($newClipRefs.ContainsKey($targetClipName) -eq $false) {
		$missingTargets += "$oldClipName -> $targetClipName"
		continue
	}

	$newRef = $newClipRefs[$targetClipName]
	$oldRefPattern = "fileID:\s*-?\d+,\s*guid:\s*$oldGuid,\s*type:\s*2"
	$newRefValue = "fileID: $($newRef.FileID), guid: $($newRef.Guid), type: $($newRef.RefType)"
	$newAgentBaseText = [System.Text.RegularExpressions.Regex]::Replace($agentBaseText, $oldRefPattern, $newRefValue)
	if ($newAgentBaseText -ne $agentBaseText) {
		$replacementCount++
		$agentBaseText = $newAgentBaseText
	}
}

Set-Content -Path $agentBaseSofPath -Value $agentBaseText -NoNewline

$newMetaText = [regex]::Replace(($agentBaseMetaLines -join "`n"), "(?m)^guid:\s*[0-9a-f]{32}\s*$", "guid: $newBaseGuid", 1)
Set-Content -Path $agentBaseSofMeta -Value $newMetaText -NoNewline

$sofText = Get-Content $sofAgentPrefabPath -Raw
$sofText = $sofText -replace $oldSofBaseGuid, $newBaseGuid
Set-Content -Path $sofAgentPrefabPath -Value $sofText -NoNewline

Write-Host "Created: $agentBaseSofPath"
Write-Host "Created: $agentBaseSofMeta"
Write-Host "Updated: $sofAgentPrefabPath (repointed base guid $oldSofBaseGuid -> $newBaseGuid)"
Write-Host "Animation guid replacements applied in base clone: $replacementCount"
if ($missingTargets.Count -gt 0) {
	Write-Host "Missing target clips:"
	$missingTargets | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" }
}
