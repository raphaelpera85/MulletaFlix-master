param(
    [string[]]$Paths = @(
        # API Controllers
        "Jellyfin.Api/Controllers/UserViewsController.cs",
        "Jellyfin.Api/Controllers/UserLibraryController.cs",
        "Jellyfin.Api/Controllers/LibraryController.cs",
        "Jellyfin.Api/Controllers/LiveTvController.cs",
        # Core implementations
        "Emby.Server.Implementations/Library/LibraryManager.cs",
        "Emby.Server.Implementations/Dto/DtoService.cs",
        "Emby.Server.Implementations/Library/UserViewManager.cs",
        "src/Jellyfin.LiveTv/LiveTvManager.cs",
        "src/Jellyfin.LiveTv/Channels/ChannelManager.cs",
        "src/Jellyfin.LiveTv/LiveTvDtoService.cs",
        "src/Jellyfin.LiveTv/Guide/GuideManager.cs",
        "src/Jellyfin.LiveTv/Recordings/RecordingsManager.cs"
    )
)

$root = Split-Path -Parent $PSScriptRoot
$patterns = @(
    '\.GetAwaiter\(\)\.GetResult\(',
    '\.Result\b',
    '\.Wait\('
)

$allowedMatches = @{
    "Emby.Server.Implementations/Dto/DtoService.cs" = @(
        'return GetBaseItemDtosAsync\(items, options, user, owner, skipVisibilityCheck\)\.GetAwaiter\(\)\.GetResult\(\);',
        'return GetBaseItemDtoAsync\(item, options, user, owner\)\.GetAwaiter\(\)\.GetResult\(\);'
    )
    "Emby.Server.Implementations/Library/LibraryManager.cs" = @(
        'BaseItem\.ChannelManager\.DeleteItem\(item\)\.GetAwaiter\(\)\.GetResult\(\);',
        'newPrimary\.UpdateToRepositoryAsync\(ItemUpdateType\.MetadataEdit, CancellationToken\.None\)\.GetAwaiter\(\)\.GetResult\(\);',
        'RerouteLinkedChildReferencesAsync\(video\.Id, newPrimary\.Id\)\.GetAwaiter\(\)\.GetResult\(\);',
        'alternate\.UpdateToRepositoryAsync\(ItemUpdateType\.MetadataEdit, CancellationToken\.None\)\.GetAwaiter\(\)\.GetResult\(\);',
        'RerouteLinkedChildReferencesAsync\(alternateVideo\.Id, alternateVideo\.PrimaryVersionId\.Value\)\.GetAwaiter\(\)\.GetResult\(\);',
        'primaryVideo\.UpdateToRepositoryAsync\(ItemUpdateType\.MetadataEdit, CancellationToken\.None\)\.GetAwaiter\(\)\.GetResult\(\);',
        'rootFolder\.UpdateToRepositoryAsync\(ItemUpdateType\.MetadataImport, CancellationToken\.None\)\.GetAwaiter\(\)\.GetResult\(\);',
        'folder\.UpdateToRepositoryAsync\(ItemUpdateType\.MetadataImport, CancellationToken\.None\)\.GetAwaiter\(\)\.GetResult\(\);',
        'item\.UpdateToRepositoryAsync\(ItemUpdateType\.MetadataImport, CancellationToken\.None\)\.GetAwaiter\(\)\.GetResult\(\);',
        'item\.UpdateToRepositoryAsync\(ItemUpdateType\.MetadataEdit, CancellationToken\.None\)\.GetAwaiter\(\)\.GetResult\(\);',
        'UpdatePeopleAsync\(item, people, CancellationToken\.None\)\.GetAwaiter\(\)\.GetResult\(\);'
    )
    "Emby.Server.Implementations/Library/UserViewManager.cs" = @(
        '\}\)\.GetAwaiter\(\)\.GetResult\(\);',
        'CancellationToken\.None\)\.GetAwaiter\(\)\.GetResult\(\)\.Items;'
    )
    "src/Jellyfin.LiveTv/LiveTvManager.cs" = @(
        'AddChannelInfoAsync\(items, options, user\)\.GetAwaiter\(\)\.GetResult\(\);'
    )
}

$violations = @()

foreach ($relativePath in $Paths) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path $path)) {
        Write-Error "Arquivo não encontrado: $path"
        exit 1
    }

    $content = Get-Content -Path $path
    foreach ($pattern in $patterns) {
        $matches = Select-String -Path $path -Pattern $pattern
        foreach ($match in $matches) {
            $isAllowed = $false
            if ($allowedMatches.ContainsKey($relativePath)) {
                foreach ($allowedPattern in $allowedMatches[$relativePath]) {
                    if ($match.Line -match $allowedPattern) {
                        $isAllowed = $true
                        break
                    }
                }
            }

            if ($isAllowed) {
                continue
            }

            $violations += [pscustomobject]@{
                File = $relativePath
                Line = $match.LineNumber
                Text = $match.Line.Trim()
                Pattern = $pattern
            }
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host "Encontrado sync-over-async nos arquivos verificados:" -ForegroundColor Red
    $violations | Sort-Object File, Line | Format-Table -AutoSize
    exit 1
}

Write-Host "Nenhuma ocorrência de sync-over-async encontrada nos endpoints verificados." -ForegroundColor Green
