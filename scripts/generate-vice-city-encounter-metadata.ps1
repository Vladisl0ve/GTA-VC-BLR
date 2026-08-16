[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ReferenceJson,

    [Parameter(Mandatory)]
    [string] $ReviewJson,

    [string] $AmericanSource,

    [string] $OutputDirectory
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot "..\Assets\ViceCity"
}

function Read-JsonDocument {
    param([string] $Path)

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    return [IO.File]::ReadAllText($resolvedPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
}

function ConvertTo-Slug {
    param([string] $Value)

    $slug = $Value.ToLowerInvariant()
    $slug = $slug -replace "&", " and "
    $slug = $slug -replace "[^a-z0-9]+", "-"
    return $slug.Trim("-")
}

$referenceDocument = Read-JsonDocument $ReferenceJson
$reviewDocument = Read-JsonDocument $ReviewJson
$referenceEntries = @($referenceDocument.entries)

if ($referenceEntries.Count -ne 4707) {
    throw "Expected 4,707 Vice City reference entries, found $($referenceEntries.Count)."
}

$blocks = [System.Collections.Generic.List[object]]::new()
$blockIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$entryByKey = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
$referenceIdentities = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$metadataEntries = [System.Collections.Generic.List[object]]::new()
$sourcePositionByIdentity = [System.Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
$sourceSha256 = $null

foreach ($referenceEntry in $referenceEntries) {
    if ([string]::IsNullOrWhiteSpace($referenceEntry.table) -or
        [string]::IsNullOrWhiteSpace($referenceEntry.key)) {
        throw "The reference inventory contains an entry without table/key."
    }

    $identity = "$($referenceEntry.table)$([char]31)$($referenceEntry.key)"
    if (-not $referenceIdentities.Add($identity)) {
        throw "Duplicate reference identity: $($referenceEntry.table)/$($referenceEntry.key)."
    }

    if ($entryByKey.ContainsKey($referenceEntry.key)) {
        throw "The generator requires globally unique GXT keys; duplicate: $($referenceEntry.key)."
    }

    $metadataEntry = [pscustomobject][ordered]@{
        table       = [string]$referenceEntry.table
        key         = [string]$referenceEntry.key
        occurrences = [System.Collections.Generic.List[object]]::new()
    }
    $metadataEntries.Add($metadataEntry)
    $entryByKey.Add($metadataEntry.key, $metadataEntry)
}

if (-not [string]::IsNullOrWhiteSpace($AmericanSource)) {
    $resolvedAmericanSource = (Resolve-Path -LiteralPath $AmericanSource).Path
    $sourceLines = [IO.File]::ReadAllLines($resolvedAmericanSource, [Text.Encoding]::UTF8)
    $sourcePosition = 0
    foreach ($line in $sourceLines) {
        if ($line -notmatch '^\[(?<key>[^:\]]+)(?::(?<table>[^\]]+))?\]$') {
            continue
        }

        $sourcePosition++
        $sourceTable = if ([string]::IsNullOrWhiteSpace($Matches.table)) { "MAIN" } else { $Matches.table }
        $sourceIdentity = "$sourceTable$([char]31)$($Matches.key)"
        if (-not $sourcePositionByIdentity.ContainsKey($sourceIdentity)) {
            $sourcePositionByIdentity.Add($sourceIdentity, $sourcePosition)
        }
    }

    foreach ($referenceIdentity in $referenceIdentities) {
        if (-not $sourcePositionByIdentity.ContainsKey($referenceIdentity)) {
            throw "The English GXT source does not contain a reference identity."
        }
    }

    $sourceSha256 = (Get-FileHash -LiteralPath $resolvedAmericanSource -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Add-Block {
    param(
        [string] $Id,
        [string] $Type,
        [string] $Name,
        [string] $Description,
        [int] $Order
    )

    if ($Order -le 0) {
        throw "Block '$Id' has invalid order $Order."
    }
    if (-not $blockIds.Add($Id)) {
        throw "Duplicate block id '$Id'."
    }

    $blocks.Add([pscustomobject][ordered]@{
        id          = $Id
        type        = $Type
        name        = $Name
        description = $Description
        order       = $Order
    })
}

function Add-Occurrence {
    param(
        [string] $Key,
        [string] $BlockId,
        [int] $Order,
        [string] $Context
    )

    if (-not $blockIds.Contains($BlockId)) {
        throw "Unknown block '$BlockId' for key '$Key'."
    }
    $entry = $null
    if (-not $entryByKey.TryGetValue($Key, [ref]$entry)) {
        throw "Research data references missing GXT key '$Key'."
    }
    if ($Order -le 0) {
        throw "Occurrence '$BlockId/$Key' has invalid order $Order."
    }

    $duplicate = $entry.occurrences | Where-Object {
        $_.blockId -eq $BlockId -and $_.order -eq $Order -and $_.context -eq $Context
    }
    if ($null -ne $duplicate) {
        return
    }

    $entry.occurrences.Add([pscustomobject][ordered]@{
        blockId = $BlockId
        order   = $Order
        context = $Context
    })
}

function Add-ResearchItems {
    param(
        [object[]] $Items,
        [string] $BlockId,
        [int] $StartOrder,
        [string] $Context
    )

    $offset = 0
    foreach ($item in @($Items)) {
        if ($null -eq $item) {
            continue
        }

        Add-Occurrence $item.key $BlockId ($StartOrder + $offset) $Context
        $offset += 10
    }
}

$missionOrders = @{
    "In The Beginning..."          = 1000
    "An Old Friend"                = 2000
    "The Party"                    = 3000
    "Back Alley Brawl"             = 4000
    "Jury Fury"                    = 5000
    "Riot"                         = 6000
    "Treacherous Swine"            = 7000
    "Mall Shootout"                = 8000
    "Guardian Angels"              = 9000
    "Four Iron"                    = 10000
    "The Chase"                    = 10000
    "Sir, Yes Sir!"                = 10000
    "Demolition Man"               = 11000
    "Phnom Penh '86"               = 11000
    "Two Bit Hit"                  = 12000
    "The Fastest Boat"             = 12000
    "All Hands On Deck!"           = 12000
    "Supply & Demand"              = 13000
    "Death Row"                    = 14000
    "Rub Out"                      = 15000
    "Shakedown"                    = 16000
    "Bar Brawl"                    = 17000
    "Cop Land"                     = 18000
    "Love Juice"                   = 30000
    "Alloy Wheels of Steel"        = 30000
    "Stunt Boat Challenge"         = 30000
    "Juju Scramble"                = 30000
    "Road Kill"                    = 30000
    "Psycho Killer"                = 31000
    "Messing With The Man"         = 31000
    "Cannon Fodder"                = 31000
    "Bombs Away!"                  = 31000
    "Waste the Wife"               = 31000
    "Hog Tied"                     = 32000
    "Naval Engagement"             = 32000
    "Dirty Lickin's"               = 32000
    "Autocide"                     = 32000
    "Publicity Tour"               = 33000
    "Trojan Voodoo"                = 33000
    "Check Out at the Check In"    = 33000
    "Loose Ends"                   = 34000
    "No Escape?"                   = 50000
    "V.I.P."                       = 50000
    "Checkpoint Charlie"           = 50000
    "Distribution"                 = 50000
    "Recruitment Drive"            = 50000
    "Spilling the Beans"           = 50000
    "The Shootist"                 = 51000
    "Friendly Rivalry"             = 51000
    "Dildo Dodo"                   = 51000
    "Hit the Courier"              = 51000
    "The Driver"                   = 52000
    "Cabmageddon"                  = 52000
    "Martha's Mug Shot"            = 52000
    "The Job"                      = 53000
    "G-Spotlight"                  = 53000
    "Gun Runner"                   = 54000
    "Boomshine Saigon"             = 55000
    "Cap the Collector"            = 70000
    "Keep Your Friends Close..."   = 71000
}

$missionBlockByTitle = @{}
foreach ($mission in @($reviewDocument.recommended_story_review_route)) {
    $title = [string]$mission.mission
    $slug = ConvertTo-Slug $title
    $isIntroduction = $title -in @("In The Beginning...", "An Old Friend")
    $isAsset = [string]$mission.order_type -like "asset*"
    $type = if ($isIntroduction) { "story" } elseif ($isAsset) { "asset" } else { "mission" }
    $prefix = if ($isIntroduction) { "story" } elseif ($isAsset) { "asset" } else { "mission" }
    $blockId = "$prefix.$slug"
    $description = switch -Wildcard ([string]$mission.order_type) {
        "asset*" { "Asset mission."; break }
        "final"  { "Final mission."; break }
        "fixed"  { "Story mission."; break }
        default  { "Optional mission." }
    }

    if (-not $missionOrders.ContainsKey($title)) {
        throw "No block order is defined for mission '$title'."
    }

    Add-Block $blockId $type $title $description $missionOrders[$title]
    $missionBlockByTitle[$title] = $blockId
    Add-Occurrence $mission.mission_title.key $blockId 10 "Mission title."

    $sequenceIndex = 0
    foreach ($sequence in @($mission.text_sequences)) {
        $prefixName = [string]$sequence.prefix
        $sequenceBase = 10000 * ($sequenceIndex + 1)
        Add-ResearchItems @($sequence.dialogue) $blockId ($sequenceBase + 10) "$prefixName dialogue."
        Add-ResearchItems @($sequence.interface) $blockId ($sequenceBase + 5000) "$prefixName mission UI; runtime order may vary."
        Add-ResearchItems @($sequence.legacy_or_unused) $blockId ($sequenceBase + 9000) "Unused/legacy."
        $sequenceIndex++
    }
}

$assetEvents = @{
    "Kaufman Cabs - purchase" = @("asset.kaufman-cabs-purchase", "Kaufman Cabs - Purchase")
    "Sunshine Autos - purchase" = @("asset.sunshine-autos-purchase", "Sunshine Autos - Purchase")
    "Cherry Popper - purchase" = @("asset.cherry-popper-purchase", "Cherry Popper - Purchase")
    "Print Works - purchase/setup" = @("asset.print-works-purchase", "Print Works - Purchase")
    "Malibu Club - purchase" = @("asset.malibu-club-purchase", "Malibu Club - Purchase")
    "Boatyard - purchase" = @("asset.boatyard-purchase", "Boatyard - Purchase")
    "InterGlobal Studios - purchase" = @("asset.interglobal-studios-purchase", "InterGlobal Studios - Purchase")
    "Pole Position - purchase" = @("asset.pole-position-purchase", "Pole Position - Purchase")
}

foreach ($event in @($reviewDocument.asset_purchase_and_setup_events)) {
    $eventName = ([string]$event.event) -replace [char]0x2014, "-"
    if (-not $assetEvents.ContainsKey($eventName)) {
        throw "Unknown asset event '$eventName'."
    }

    $blockId, $blockName = $assetEvents[$eventName]
    Add-Block $blockId "asset" $blockName "Asset purchase or setup." 49000

    $sequenceIndex = 0
    foreach ($sequence in @($event.sequences)) {
        $prefixName = [string]$sequence.prefix
        $sequenceBase = 10000 * ($sequenceIndex + 1)
        Add-ResearchItems @($sequence.dialogue) $blockId ($sequenceBase + 10) "$prefixName purchase dialogue."
        Add-ResearchItems @($sequence.interface) $blockId ($sequenceBase + 5000) "$prefixName setup UI; runtime order may vary."
        Add-ResearchItems @($sequence.legacy_or_unused) $blockId ($sequenceBase + 9000) "Unused/legacy."
        $sequenceIndex++
    }
    Add-ResearchItems @($event.interface) $blockId 90000 "Purchase UI."
}

foreach ($call in @($reviewDocument.phone_calls)) {
    $number = [int]$call.internal_call_number
    $numberText = $number.ToString("00", [Globalization.CultureInfo]::InvariantCulture)
    $blockId = "phone.mob-$numberText"
    Add-Block $blockId "phone" "Phone Call $numberText" "Progress-triggered phone call." 40000
    Add-ResearchItems @($call.entries) $blockId 10 "Phone dialogue; trigger varies."
}

Add-Block "interface.frontend" "interface" "Frontend and Controls" "Menus, settings and control labels." 100
Add-Block "interface.hud-and-status" "interface" "HUD and Status" "Recurring gameplay status text." 800
Add-Block "interface.tutorial" "interface" "Tutorial and Help" "Gameplay tutorials and first-use help." 900
Add-Block "interface.statistics" "interface" "Statistics" "Statistics screen labels." 800000
Add-Block "interface.weapon-names" "interface" "Weapon Names" "Weapon names shown by the interface." 800000
Add-Block "interface.vehicle-names" "interface" "Vehicle Names" "Vehicle names shown by the interface." 800000
Add-Block "interface.outfits" "interface" "Outfits" "Outfit names and clothing prompts." 800000
Add-Block "world.areas" "world" "Areas and Districts" "Area and district labels." 2500
Add-Block "world.properties-and-venues" "world" "Properties and Venues" "Property, safehouse and venue labels." 2500
Add-Block "world.map-legend" "world" "Map Legend" "Map legend labels." 2500
Add-Block "mission.paramedic" "mission" "Paramedic" "Optional vehicle mission." 45000
Add-Block "mission.firefighter" "mission" "Firefighter" "Optional vehicle mission." 45000
Add-Block "mission.vigilante" "mission" "Vigilante" "Optional vehicle mission." 45000
Add-Block "mission.pizza-boy" "mission" "Pizza Boy" "Optional delivery mission." 45000
Add-Block "mission.taxi-driver" "mission" "Taxi Driver" "Optional vehicle mission." 45000
Add-Block "mission.sunshine-autos-street-races" "mission" "Sunshine Autos Street Races" "Optional race series." 50000
Add-Block "mission.rc-raider-pickup" "mission" "RC Raider Pickup" "Optional RC challenge." 45000
Add-Block "mission.rc-baron-race" "mission" "RC Baron Race" "Optional RC challenge." 45000
Add-Block "mission.rc-bandit-race" "mission" "RC Bandit Race" "Optional RC challenge." 45000
Add-Block "mission.hotring" "mission" "Hotring" "Hyman Memorial Stadium event." 45000
Add-Block "mission.trial-by-dirt" "mission" "Trial by Dirt" "Optional dirt-bike challenge." 45000
Add-Block "mission.test-track" "mission" "Test Track" "Optional dirt-bike challenge." 45000
Add-Block "mission.cone-crazy" "mission" "Cone Crazy" "Optional checkpoint challenge." 45000
Add-Block "mission.pcj-playground" "mission" "PCJ Playground" "Optional checkpoint challenge." 45000
Add-Block "mission.bloodring" "mission" "Bloodring" "Hyman Memorial Stadium event." 45000
Add-Block "mission.rampages" "mission" "Rampages" "Optional rampage challenges." 45000
Add-Block "credits" "credits" "Credits" "End credits." 900000
Add-Block "misc.unclassified" "misc" "Miscellaneous / Unclassified" "No reliable encounter context." 999000

foreach ($checkpoint in @($reviewDocument.global_interface_checkpoints)) {
    $key = [string]$checkpoint.key
    $blockId = if ($key -in @("BRID_CL", "BRID_OP")) {
        "interface.hud-and-status"
    }
    elseif ($key -match "^(SAVE|WANT_|HEAL_|ANSWER|AMMU|BRIBE|CLOHELP)") {
        "interface.tutorial"
    }
    else {
        "interface.hud-and-status"
    }

    Add-Occurrence $key $blockId 100 "Runtime order may vary."
}

$tableBlockMap = @{
    "INTRO"   = $missionBlockByTitle["In The Beginning..."]
    "HOTEL"   = $missionBlockByTitle["An Old Friend"]
    "LAWYER1" = $missionBlockByTitle["The Party"]
    "LAWYER2" = $missionBlockByTitle["Back Alley Brawl"]
    "LAWYER3" = $missionBlockByTitle["Jury Fury"]
    "LAWYER4" = $missionBlockByTitle["Riot"]
    "GENERA1" = $missionBlockByTitle["Treacherous Swine"]
    "GENERA2" = $missionBlockByTitle["Mall Shootout"]
    "GENERA3" = $missionBlockByTitle["Guardian Angels"]
    "GENERA4" = $missionBlockByTitle["Sir, Yes Sir!"]
    "GENERA5" = $missionBlockByTitle["All Hands On Deck!"]
    "SERG1"   = $missionBlockByTitle["Four Iron"]
    "SERG2"   = $missionBlockByTitle["Two Bit Hit"]
    "SERG3"   = $missionBlockByTitle["Demolition Man"]
    "BARON1"  = $missionBlockByTitle["The Chase"]
    "BARON2"  = $missionBlockByTitle["Phnom Penh '86"]
    "BARON3"  = $missionBlockByTitle["The Fastest Boat"]
    "BARON4"  = $missionBlockByTitle["Supply & Demand"]
    "BARON5"  = $missionBlockByTitle["Rub Out"]
    "KENT1"   = $missionBlockByTitle["Death Row"]
    "PROT1"   = $missionBlockByTitle["Shakedown"]
    "PROT2"   = $missionBlockByTitle["Bar Brawl"]
    "PROT3"   = $missionBlockByTitle["Cop Land"]
    "ROCK1"   = $missionBlockByTitle["Love Juice"]
    "ROCK2"   = $missionBlockByTitle["Psycho Killer"]
    "ROCK3"   = $missionBlockByTitle["Publicity Tour"]
    "BIKE1"   = $missionBlockByTitle["Alloy Wheels of Steel"]
    "BIKE2"   = $missionBlockByTitle["Messing With The Man"]
    "BIKE3"   = $missionBlockByTitle["Hog Tied"]
    "CUBAN1"  = $missionBlockByTitle["Stunt Boat Challenge"]
    "CUBAN2"  = $missionBlockByTitle["Cannon Fodder"]
    "CUBAN3"  = $missionBlockByTitle["Naval Engagement"]
    "CUBAN4"  = $missionBlockByTitle["Trojan Voodoo"]
    "HAIT1"   = $missionBlockByTitle["Juju Scramble"]
    "HAIT2"   = $missionBlockByTitle["Bombs Away!"]
    "HAIT3"   = $missionBlockByTitle["Dirty Lickin's"]
    "ASSIN1"  = $missionBlockByTitle["Road Kill"]
    "ASSIN2"  = $missionBlockByTitle["Waste the Wife"]
    "ASSIN3"  = $missionBlockByTitle["Autocide"]
    "ASSIN4"  = $missionBlockByTitle["Check Out at the Check In"]
    "ASSIN5"  = $missionBlockByTitle["Loose Ends"]
    "BANKJ1"  = $missionBlockByTitle["No Escape?"]
    "BANKJ2"  = $missionBlockByTitle["The Shootist"]
    "BANKJ3"  = $missionBlockByTitle["The Driver"]
    "BANKJ4"  = $missionBlockByTitle["The Job"]
    "PHIL1"   = $missionBlockByTitle["Gun Runner"]
    "PHIL2"   = $missionBlockByTitle["Boomshine Saigon"]
    "TAXIWA1" = $missionBlockByTitle["V.I.P."]
    "TAXIWA2" = $missionBlockByTitle["Friendly Rivalry"]
    "TAXIWA3" = $missionBlockByTitle["Cabmageddon"]
    "ICECRE1" = $missionBlockByTitle["Distribution"]
    "PORN1"   = $missionBlockByTitle["Recruitment Drive"]
    "PORN2"   = $missionBlockByTitle["Dildo Dodo"]
    "PORN3"   = $missionBlockByTitle["Martha's Mug Shot"]
    "PORN4"   = $missionBlockByTitle["G-Spotlight"]
    "COUNT1"  = $missionBlockByTitle["Spilling the Beans"]
    "COUNT2"  = $missionBlockByTitle["Hit the Courier"]
    "CAP_1"   = $missionBlockByTitle["Cap the Collector"]
    "FINALE"  = $missionBlockByTitle["Keep Your Friends Close..."]
    "TAXICUT" = "asset.kaufman-cabs-purchase"
    "CARBUY"  = "asset.sunshine-autos-purchase"
    "ICECUT"  = "asset.cherry-popper-purchase"
    "BOATBUY" = "asset.boatyard-purchase"
    "AMBULAE" = "mission.paramedic"
    "FIRETRK" = "mission.firefighter"
    "COPCAR"  = "mission.vigilante"
    "PIZZA"   = "mission.pizza-boy"
    "TAXI1"   = "mission.taxi-driver"
    "RACES"   = "mission.sunshine-autos-street-races"
    "RCHELI1" = "mission.rc-raider-pickup"
    "RCPLNE1" = "mission.rc-baron-race"
    "RCRACE1" = "mission.rc-bandit-race"
    "OVALRIG" = "mission.hotring"
    "BMX_1"   = "mission.trial-by-dirt"
    "CARPAR1" = "mission.cone-crazy"
    "MIAMI_1" = "mission.pcj-playground"
    "MM"      = "mission.bloodring"
}

$areaKeys = [System.Collections.Generic.HashSet[string]]::new([string[]]@(
    "IND_ZON", "COM_ZON", "BEACH1", "BEACH2", "BEACH3", "GOLFC", "STARI",
    "DOCKS", "HAVANA", "HAITI", "PORNI", "DTOWN", "VICE_C", "A_PORT", "JUNKY"
), [StringComparer]::Ordinal)
$propertyKeys = [System.Collections.Generic.HashSet[string]]::new([string[]]@(
    "MALIBU", "MANSION", "TMANS", "STRIP", "MALL1", "BANKINT", "RANGE", "POL_HQ", "TAX_1"
), [StringComparer]::Ordinal)
$weaponKeys = [System.Collections.Generic.HashSet[string]]::new([string[]]@(
    "PISTOL", "PYTHON", "UZI", "TEC9", "M4", "INGRAM", "MP5", "RUGER", "SNIPE",
    "GRENADE", "SHOTGN1", "SHOTGN2", "SHOTGN3", "ARMOUR", "LASER", "BASEBAT", "HAMMER",
    "SCREWD", "CLEVER", "MACHETE", "KNIFE", "KATANA", "CHAINSA"
), [StringComparer]::Ordinal)
$tutorialKeys = [System.Collections.Generic.HashSet[string]]::new([string[]]@(
    "IN_VEH", "HEY", "TIMER", "HORN", "AMMU", "ANSWER", "BRIBE1", "CLOHELP"
), [StringComparer]::Ordinal)
$hudKeys = [System.Collections.Generic.HashSet[string]]::new([string[]]@(
    "REWARD", "M_FAIL", "M_PASS", "DEAD", "BUSTED", "NUMBER", "LOADCAR", "CARSOFF",
    "CARS_ON", "TEXTXYZ", "CHEATON", "CHEATOF", "R_TIME", "A_TIME", "DODO_FT", "BRID_CL", "BRID_OP",
    "NOMONEY", "IMPORT1", "PAGEB11", "PAGEB13", "PAGEB14", "G_COST"
), [StringComparer]::Ordinal)
$vehicleKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$collectVehicleKeys = $false
foreach ($referenceEntry in $referenceEntries) {
    if ($referenceEntry.table -ne "MAIN") {
        continue
    }
    if ($referenceEntry.key -eq "LANDSTK") {
        $collectVehicleKeys = $true
    }
    if ($collectVehicleKeys) {
        $vehicleKeys.Add([string]$referenceEntry.key) | Out-Null
    }
    if ($referenceEntry.key -eq "BLISTAC") {
        break
    }
}
$collectAdditionalVehicleKeys = $false
foreach ($referenceEntry in $referenceEntries) {
    if ($referenceEntry.table -ne "MAIN") {
        continue
    }
    if ($referenceEntry.key -eq "WHEEL01") {
        $collectAdditionalVehicleKeys = $true
    }
    if ($collectAdditionalVehicleKeys) {
        $vehicleKeys.Add([string]$referenceEntry.key) | Out-Null
    }
    if ($referenceEntry.key -eq "BLOODRB") {
        break
    }
}
$vehicleKeys.Add("CAR_1") | Out-Null
$vehicleKeys.Add("MESA") | Out-Null

function Get-MainFallbackBlock {
    param([string] $Key)

    if ($Key -match "^(FIN_|FINKILL$)") { return $missionBlockByTitle["Keep Your Friends Close..."] }
    if ($Key -match "^COL3_") { return $missionBlockByTitle["Guardian Angels"] }
    if ($Key -match "^(SEG3_|SERG3_)") { return $missionBlockByTitle["Demolition Man"] }
    if ($Key -match "^LAW_1[A-Z]$") { return $missionBlockByTitle["The Party"] }
    if ($Key -match "^LAW_2[A-Z]$") { return $missionBlockByTitle["Back Alley Brawl"] }
    if ($Key -match "^BNK") { return $missionBlockByTitle["The Job"] }
    if ($Key -match "^ASM_1$") { return $missionBlockByTitle["Road Kill"] }
    if ($Key -match "^ASM_2$") { return $missionBlockByTitle["Waste the Wife"] }
    if ($Key -match "^ASM_3$") { return $missionBlockByTitle["Autocide"] }
    if ($Key -match "^ASM_4$") { return $missionBlockByTitle["Check Out at the Check In"] }
    if ($Key -match "^ASM_5$") { return $missionBlockByTitle["Loose Ends"] }
    if ($Key -match "^BANK1$") { return $missionBlockByTitle["No Escape?"] }
    if ($Key -match "^BANK2$") { return $missionBlockByTitle["The Shootist"] }
    if ($Key -match "^BANK3$") { return $missionBlockByTitle["The Driver"] }
    if ($Key -match "^COUNT1$") { return $missionBlockByTitle["Spilling the Beans"] }
    if ($Key -match "^COUNT2$") { return $missionBlockByTitle["Hit the Courier"] }
    if ($Key -match "^BIKE1$") { return $missionBlockByTitle["Alloy Wheels of Steel"] }
    if ($Key -match "^BIKE2$") { return $missionBlockByTitle["Messing With The Man"] }
    if ($Key -match "^BIKE3$") { return $missionBlockByTitle["Hog Tied"] }
    if ($Key -match "^HAIT1$") { return $missionBlockByTitle["Juju Scramble"] }
    if ($Key -match "^HAIT2$") { return $missionBlockByTitle["Bombs Away!"] }
    if ($Key -match "^HAIT3$") { return $missionBlockByTitle["Dirty Lickin's"] }
    if ($Key -match "^CUBAN1$") { return $missionBlockByTitle["Stunt Boat Challenge"] }
    if ($Key -match "^CUBAN2$") { return $missionBlockByTitle["Cannon Fodder"] }
    if ($Key -match "^CUBAN3$") { return $missionBlockByTitle["Naval Engagement"] }
    if ($Key -match "^CUBAN4$") { return $missionBlockByTitle["Trojan Voodoo"] }
    if ($Key -match "^PORN1$") { return $missionBlockByTitle["Recruitment Drive"] }
    if ($Key -match "^PORN2$") { return $missionBlockByTitle["Dildo Dodo"] }
    if ($Key -match "^PORN3$") { return $missionBlockByTitle["Martha's Mug Shot"] }
    if ($Key -match "^PORN4$") { return $missionBlockByTitle["G-Spotlight"] }
    if ($Key -match "^PHIL1$") { return $missionBlockByTitle["Gun Runner"] }
    if ($Key -match "^PHIL2$") { return $missionBlockByTitle["Boomshine Saigon"] }
    if ($Key -eq "ROCK_4") { return $missionBlockByTitle["Love Juice"] }
    if ($Key -in @("LAW", "LAWYER")) { return $missionBlockByTitle["The Party"] }
    if ($Key -eq "GENERAL") { return $missionBlockByTitle["Treacherous Swine"] }
    if ($Key -eq "COKE") { return $missionBlockByTitle["The Chase"] }
    if ($Key -eq "AVERY") { return $missionBlockByTitle["Four Iron"] }
    if ($Key -eq "ASM") { return $missionBlockByTitle["Road Kill"] }
    if ($Key -eq "BANK") { return $missionBlockByTitle["No Escape?"] }
    if ($Key -eq "KENT" -or $Key -eq "KENT1") { return $missionBlockByTitle["Death Row"] }
    if ($Key -eq "COUNT") { return $missionBlockByTitle["Spilling the Beans"] }
    if ($Key -eq "BIKE") { return $missionBlockByTitle["Alloy Wheels of Steel"] }
    if ($Key -eq "HAIT") { return $missionBlockByTitle["Juju Scramble"] }
    if ($Key -eq "ROCK") { return $missionBlockByTitle["Love Juice"] }
    if ($Key -eq "ROCK3") { return $missionBlockByTitle["Publicity Tour"] }
    if ($Key -eq "CUBANM") { return $missionBlockByTitle["Stunt Boat Challenge"] }
    if ($Key -eq "PROT") { return $missionBlockByTitle["Shakedown"] }
    if ($Key -eq "PORN") { return $missionBlockByTitle["Recruitment Drive"] }
    if ($Key -eq "PHIL") { return $missionBlockByTitle["Gun Runner"] }
    if ($Key -eq "TAXWAR") { return $missionBlockByTitle["V.I.P."] }
    if ($Key -eq "KICK") { return "mission.trial-by-dirt" }
    if ($Key -match "^RCH1_") { return "mission.rc-raider-pickup" }
    if ($Key -match "^(RCRC1_|RCR1_)") { return "mission.rc-bandit-race" }
    if ($Key -match "^RCPL1_") { return "mission.rc-baron-race" }
    if ($Key -match "^(RACE[1-5]$|FIRST$|SECOND$|THIRD$|FOURTH$|RACETM|RACEFA)") { return "mission.sunshine-autos-street-races" }
    if ($Key -match "^(TAXIBUY$|TAXI_NO$)") { return "asset.kaufman-cabs-purchase" }
    if ($Key -match "^(PRNT_NO$)") { return "asset.print-works-purchase" }
    if ($Key -match "^(CAR_NO$)") { return "asset.sunshine-autos-purchase" }
    if ($Key -match "^(PORN_NO$)") { return "asset.interglobal-studios-purchase" }
    if ($Key -match "^(ICE_NO$)") { return "asset.cherry-popper-purchase" }
    if ($Key -match "^(BANK_NO$)") { return "asset.malibu-club-purchase" }
    if ($Key -match "^CAR_AS") { return "asset.sunshine-autos-purchase" }
    if ($Key -match "^(CRED|CRD)\d+[A-Z]?$" ) { return "credits" }
    if ($areaKeys.Contains($Key)) { return "world.areas" }
    if ($propertyKeys.Contains($Key) -or $Key -match "^STPR_\d+$") { return "world.properties-and-venues" }
    if ($Key -match "^(SUNSHIN$|CHERRYP$|KAUFCAB$|BOATYAR$|HOTRNG$|BLODRNG$|DIRTRNG$|SKUMBUY$|SKUM_[LTC]$)") { return "world.properties-and-venues" }
    if ($weaponKeys.Contains($Key)) { return "interface.weapon-names" }
    if ($vehicleKeys.Contains($Key) -or $Key -in @("MAFIACR", "VCNMAV")) { return "interface.vehicle-names" }
    if ($Key -match "^(OUTFT|CLOTH|SCARF$)") { return "interface.outfits" }
    if ($Key -match "^(LG_|MAP_LEG$|MAP_YAH$)") { return "world.map-legend" }
    if ($Key -match "^(BUYSAVE$|BUYGAR|STRPBUY$|STRP_NO$|NBMN|LNKV|HYCO|OCHE|WASH|VCPT|TAXI_[LTC]$|HOTEL$)") { return "world.properties-and-venues" }
    if ($tutorialKeys.Contains($Key) -or $Key -match "^(HELP|WANT_|HEAL_|SAVE|TTUTOR|FTUTOR|CTUTOR|GUN_H|AMMUHLP|S_PROMP|HELI_|HORN[1-3]$|CINCAM$|TYREPOP$|TYRESLA$|HLPSN_|PLANE_|RCCANX$|CLT_HL2$|BOLLOX$)") { return "interface.tutorial" }
    if ($hudKeys.Contains($Key) -or $Key -match "^(WEATHE|PROP_|CHASE\d*$|GA_|PAGEB|PAGE_|PU_|CO_|CHEAT\d|BONUS$|WELCOME$|PBOAT_|PICK|LOADCOL$|NEW_REC$|BMXREW|BMXRAIN$|RELOAD$|APACHE$|CUNTY$|GOODBOY$|NEWCONT$)") { return "interface.hud-and-status" }
    if ($Key -match "^(ST_|PL_STAT$|PE_|TM_|GNG_|DED_|PER_COM$|KGS_|ACCURA$|TOP_SHO$|SHO_RAN$|SEAGULL$|PROPOWN$|DAYSPS$|NUMSHV$|MX|BUL_|SPRAYIN$|BSTSTU$|INSTUN$|PRINST$|DBINST$|DBPINS$|TRINST$|PRTRST$|QUINST$|PQUINS$|NOSTUC$|NOUNIF$|NMISON$|PASDRO$|MONTAX$|DAYPLC$|CRIMRA$|FEST_|TOT_DIS$|TOTDISM$|DISTHEL$|DISTHEM$|DISTBOA$|DISTBOM$|RATNG|USJ|HJSTA|HJ_|C_KILLS$|DISTGOL$|DISTGOM$|DISTBIK$|DISTBIM$|CAR_EXP$|BOA_EXP$|HEL_DST$|STFT_|STHC_|FST_MFR$|FST_LFR$)") { return "interface.statistics" }
    if ($Key -match "^(FE|WIN_|CVT_|PCRESRT$|REPLAY$|NO_PCCD$|SET1EN$|GMSAVE$|WRONGCD$|NOCD$|OPENCD$|CDERROR$|RESTART$|LEGAL$|PERPIC$|JAN$|MAR$|APR$|MAY$|JUN$|JUL$|AUG$|SEP$|OCT$|NOV$|DEC$|DEFDT$)") { return "interface.frontend" }
    if ($Key -match "^(FIRELVL|FIREPRO|F_PASS|F_FAIL|F_CANC|F_EXTIN|F_START|F_STAR|SPRAY_)") { return "mission.firefighter" }
    if ($Key -match "^(ALEVEL|A_FULL|A_FAIL|A_PASS|A_COMP|A_CANC|A_SAVES|ATUTOR)") { return "mission.paramedic" }
    if ($Key -match "^(CLEVEL|C_BREIF|COPCART|C_CANC|C_TIME|C_COMP|C_PASS|KILLS$)") { return "mission.vigilante" }
    if ($Key -match "^(PIZ1_|PIZ_WON)") { return "mission.pizza-boy" }
    if ($Key -match "^(FARES$|FARE\d|MFARE|WFARE|TAXIH|TSCORE|IN_ROW$)") { return "mission.taxi-driver" }
    if ($Key -match "^(TAXI2$|TAXI_M$)") { return "mission.taxi-driver" }
    if ($Key -match "^(COP_M|C_FAIL$|C_ESCP$|C_VIGIL$)") { return "mission.vigilante" }
    if ($Key -eq "FIRE_M") { return "mission.firefighter" }
    if ($Key -eq "AMBUL_M") { return "mission.paramedic" }
    if ($Key -match "^(RACES|RACEHLP)") { return "mission.sunshine-autos-street-races" }
    if ($Key -match "^(HOTR_)") { return "mission.hotring" }
    if ($Key -eq "T4X4_1" -or $Key -match "^T4X4_" -or $Key -in @("GETBIK1", "GETBIK3")) { return "mission.pcj-playground" }
    if ($Key -eq "BMX_2") { return "mission.test-track" }
    if ($Key -match "^(BMX_|BMXFAIL$|BMX_REC$|KICK1_)") { return "mission.trial-by-dirt" }
    if ($Key -match "^KICK_") { return "mission.test-track" }
    if ($Key -eq "MM_1") { return "mission.cone-crazy" }
    if ($Key -match "^(RAMPAGE$|RAMP_)") { return "mission.rampages" }
    if ($Key -in @("ASSET_C", "ASSET_D")) { return "asset.pole-position-purchase" }
    return "misc.unclassified"
}

$entryPosition = 0
foreach ($entry in $metadataEntries) {
    $entryPosition++
    if ($entry.occurrences.Count -gt 0) {
        continue
    }

    $blockId = if ($entry.table -eq "KICKSTT") {
        if ($entry.key -match "^KICK_") { "mission.test-track" } else { "mission.trial-by-dirt" }
    }
    elseif ($tableBlockMap.ContainsKey($entry.table)) {
        $tableBlockMap[$entry.table]
    }
    elseif ($entry.table -eq "MAIN") {
        Get-MainFallbackBlock $entry.key
    }
    else {
        "misc.unclassified"
    }

    $context = if ($blockId -eq "misc.unclassified") {
        "Unclassified; original GXT order."
    }
    elseif ($blockId -eq "credits") {
        "End credits."
    }
    elseif ($blockId -like "world.*") {
        "Free-roam label."
    }
    elseif ($blockId -like "interface.*" -or $blockId -like "mission.paramedic" -or
            $blockId -like "mission.firefighter" -or $blockId -like "mission.vigilante" -or
            $blockId -like "mission.pizza-boy" -or $blockId -like "mission.taxi-driver" -or
            $blockId -like "mission.*race*" -or $blockId -like "mission.hotring" -or
            $blockId -like "mission.trial-by-dirt" -or $blockId -like "mission.cone-crazy" -or
            $blockId -like "mission.pcj-playground" -or $blockId -like "mission.bloodring") {
        "Runtime order may vary."
    }
    else {
        "Original GXT table order."
    }

    $identity = "$($entry.table)$([char]31)$($entry.key)"
    $fallbackPosition = $entryPosition
    if ($sourcePositionByIdentity.ContainsKey($identity)) {
        $fallbackPosition = $sourcePositionByIdentity[$identity]
    }
    Add-Occurrence $entry.key $blockId ($fallbackPosition * 10) $context
}

$blockOrderById = @{}
foreach ($block in $blocks) {
    $blockOrderById[$block.id] = $block.order
}

foreach ($entry in $metadataEntries) {
    if ($entry.occurrences.Count -eq 0) {
        throw "Entry '$($entry.table)/$($entry.key)' has no occurrence."
    }

    $entry.occurrences = @($entry.occurrences | Sort-Object `
        @{ Expression = { $blockOrderById[$_.blockId] } }, `
        @{ Expression = { $_.order } }, `
        @{ Expression = { $_.blockId } }, `
        @{ Expression = { $_.context } })
}

$metadata = [pscustomobject][ordered]@{
    format  = "GXT_ENTRY_METADATA"
    version = 1
    blocks  = @($blocks | Sort-Object order, id)
    entries = @($metadataEntries)
}

$referenceInventory = [pscustomobject][ordered]@{
    format     = "GXT_REFERENCE_ENTRIES"
    version    = 1
    game       = "GTA Vice City"
    platform   = "PC"
    source     = "Sergeanur/GXT VC PC american.txt"
    sourceSha256 = $sourceSha256
    entryCount = $referenceEntries.Count
    entries    = @($referenceEntries | ForEach-Object {
        [pscustomobject][ordered]@{
            table = [string]$_.table
            key   = [string]$_.key
        }
    })
}

$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($outputPath) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText(
    (Join-Path $outputPath "encounter-order.metadata.json"),
    ($metadata | ConvertTo-Json -Depth 100),
    $utf8)
[IO.File]::WriteAllText(
    (Join-Path $outputPath "vice-city-pc.reference-entries.json"),
    ($referenceInventory | ConvertTo-Json -Depth 10),
    $utf8)

Write-Host "Generated $($metadata.blocks.Count) blocks and $($metadata.entries.Count) entries."
