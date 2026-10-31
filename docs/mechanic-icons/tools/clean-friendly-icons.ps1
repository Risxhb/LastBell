$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $PSScriptRoot 'IconTool.cs') -ReferencedAssemblies System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'FriendlyIconAudit.cs') -ReferencedAssemblies System.Drawing
function P($rel) { Join-Path $root $rel }
New-Item -ItemType Directory -Force (P 'export/friendly'),(P 'qa/friendly') | Out-Null
$ids = 'order_i','order_ii','order_iii','order_iv','order_v','order_vi','order_vii','order_viii','team_brass','team_cobalt','carried_mark','tether_partner'
$names = 'I','II','III','IV','V','VI','VII','VIII','Brass / round','Cobalt / diamond','Carried mark','Tether partner'
$original = Get-Content (P 'friendly-original-prompts.json') -Raw | ConvertFrom-Json
$regens = Get-Content (P 'friendly-regeneration-prompts.json') -Raw | ConvertFrom-Json
$files = @(); $manifest = @(); $validation = @()
# Human approval (APPROVAL.md) lives only in the manifest, so carry it over from the existing file. It stays valid only
# while the source is byte-identical (same sourceSha256); new art starts unapproved again.
$approvalFields = 'humanApproved','integrated','approvedBy','approvedAt','approvalRecord'
$previous = @{}
if (Test-Path (P 'friendly-manifest.json')) { foreach ($e in (Get-Content (P 'friendly-manifest.json') -Raw | ConvertFrom-Json)) { $previous[$e.id] = $e } }
for ($i=0; $i -lt $ids.Count; $i++) {
    $id = $ids[$i]; $filename = "friendly_$id.png"
    $sourceName = if ($id -in @('order_vii','team_brass')) { "friendly_$id-v2.png" } else { $filename }
    $source = P "source/$sourceName"; $target = P "export/friendly/$filename"
    $sourceAudit = [FriendlyIconAudit]::Inspect($source) | ConvertFrom-Json
    if ($sourceAudit.alphaMin -ne 0 -or $sourceAudit.alphaMax -ne 255) { throw "Source alpha failed: $sourceName" }
    $cleanup = [IconTool]::CleanChroma($source,$target,'#00FF00') | ConvertFrom-Json
    $audit = [FriendlyIconAudit]::Inspect($target) | ConvertFrom-Json
    if ($audit.width -ne 256 -or $audit.height -ne 256 -or $audit.alphaMin -ne 0 -or $audit.alphaMax -ne 255 -or $audit.borderPixels -ne 0) { throw "Export failed: $filename" }
    $files += $target
    $prompt = if ($sourceName -ne $filename) { ($regens | Where-Object file -eq $sourceName).prompt } else { ($original | Where-Object file -eq $filename).prompt }
    $entry = [ordered]@{ id=$id; label=$names[$i]; source="source/$sourceName"; export="export/friendly/$filename"; sourceSha256=(Get-FileHash $source -Algorithm SHA256).Hash; exportSha256=(Get-FileHash $target -Algorithm SHA256).Hash; prompt=$prompt; generated=$true; humanApproved=$false; integrated=$false }
    $old = $previous[$id]
    if ($old -and $old.sourceSha256 -eq $entry.sourceSha256) {
        foreach ($k in $approvalFields) { if ($null -ne $old.$k) { $entry[$k] = $old.$k } }
    } elseif ($old -and $old.humanApproved) { Write-Warning "$id source changed since approval: reset to humanApproved=false" }
    $manifest += $entry
    $validation += [ordered]@{ id=$id; source=$sourceAudit; cleanup=$cleanup; export=$audit }
}
# Same layout as the committed manifest (2-space indent, CRLF, UTF-8 without BOM), so an unchanged rerun is a no-op diff.
$lines = @('[')
for ($n = 0; $n -lt $manifest.Count; $n++) {
    $keys = @($manifest[$n].Keys); $lines += '  {'
    for ($j = 0; $j -lt $keys.Count; $j++) {
        $v = $manifest[$n][$keys[$j]]
        $json = if ($v -is [bool]) { $v.ToString().ToLower() } else { ConvertTo-Json ([string]$v) -Compress }
        $lines += '    "' + $keys[$j] + '": ' + $json + $(if ($j -lt $keys.Count - 1) { ',' } else { '' })
    }
    $lines += '  }' + $(if ($n -lt $manifest.Count - 1) { ',' } else { '' })
}
$lines += ']'
[IO.File]::WriteAllText((P 'friendly-manifest.json'), ($lines -join "`r`n") + "`r`n", (New-Object Text.UTF8Encoding $false))
$validation | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (P 'qa/friendly/validation.json')
$f=[string[]]$files; $l=[string[]]$names
[IconTool]::Sheet($f,$l,(P 'qa/friendly/contact-cobalt.png'),128,4,'#17263F','#F4E9D1',$false)
[IconTool]::Sheet($f,$l,(P 'qa/friendly/contact-ivory.png'),128,4,'#F4E9D1','#17263F',$false)
[IconTool]::Sheet($f,$l,(P 'qa/friendly/64-color-cobalt.png'),64,4,'#17263F','#F4E9D1',$false)
[IconTool]::Sheet($f,$l,(P 'qa/friendly/64-color-ivory.png'),64,4,'#F4E9D1','#17263F',$false)
[IconTool]::Sheet($f,$l,(P 'qa/friendly/64-gray-cobalt.png'),64,4,'#17263F','#F4E9D1',$true)
[IconTool]::Sheet($f,$l,(P 'qa/friendly/64-gray-ivory.png'),64,4,'#F4E9D1','#17263F',$true)
# Read only: hostile final exports. Never substitute known-failed provisional art.
$hostileIds='spread','soak_tower','line_stack','stretch_tether','pass_the_mark','movement_stop','movement_move','one_side_cleave','player_mirror','pull','shrinking_edge','cone','ring','lane','delayed_circle','low_bar'
$compareFiles=@($files); $compareLabels=@($names | ForEach-Object { "F $_" }); $hostileEvidence=@()
foreach($id in $hostileIds){
    $path=P "export/hostile/hostile_$id.png"
    if(Test-Path $path){ $compareFiles+=$path; $compareLabels+="H $id"; $hostileEvidence += @{id=$id;state='present';sha256=(Get-FileHash $path -Algorithm SHA256).Hash} }
    else { $compareFiles+=$null; $compareLabels+="H $id MISSING"; $hostileEvidence+=@{id=$id;state='missing'} }
}
[IconTool]::Sheet([string[]]$compareFiles,[string[]]$compareLabels,(P 'qa/friendly/friendly-vs-hostile-64-gray.png'),64,4,'#314C69','#F4E9D1',$true)
$hostileEvidence | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (P 'qa/friendly/hostile-comparison-inputs.json')
Write-Output 'Exported 12 friendly markers; alpha, size and transparent borders validated. Comparison uses only final hostile exports, with explicit missing slots.'
