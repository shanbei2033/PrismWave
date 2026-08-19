# PrismWave v1.0.6 Release Upload Script
# Run this on your local machine with internet access to upload the GitHub Release

$zipPath = "artifacts\PrismWave-v1.0.6-win-x64.zip"
$notesFile = "release\release_notes_v1.0.6.md"
$tagName = "v1.0.6"
$releaseTitle = "v1.0.6 - Playback Reliability Fix"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "PrismWave v1.0.6 Release Upload" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# Check if release notes file exists
if (!(Test-Path $notesFile)) {
    Write-Host "Error: Release notes file not found at: $notesFile" -ForegroundColor Red
    exit 1
}

# Check if zip file exists
if (!(Test-Path $zipPath)) {
    Write-Host "Error: Release zip file not found at: $zipPath" -ForegroundColor Red
    exit 1
}

# Read release notes content
try {
    $releaseNotes = Get-Content $notesFile -Raw -Encoding UTF8
    Write-Host "`n✓ Release notes loaded successfully" -ForegroundColor Green
} catch {
    Write-Host "Failed to read release notes" -ForegroundColor Red
    exit 1
}

# Build release JSON body
$releaseBody = @"
{
  "tag_name": "$tagName",
  "name": "$releaseTitle",
  "body": $(ConvertFrom-SslString($releaseNotes) | ConvertTo-Json),
  "draft": false,
  "prerelease": false
}
"@

Write-Host "`nCreating GitHub Release..." -ForegroundColor Yellow
Write-Host "Tag: $tagName" -ForegroundColor Gray
Write-Host "Title: $releaseTitle" -ForegroundColor Gray
Write-Host "ZIP: $zipPath (Size: $(((Get-Item $zipPath).Length / 1MB) * 1024) MB)" -ForegroundColor Gray

# Use gh CLI to create release if available
if (Get-Command gh -ErrorAction SilentlyContinue) {
    try {
        Write-Host "`nUsing GitHub CLI (gh.exe)..." -ForegroundColor Yellow
        & gh release create $tagName `
            --title $releaseTitle `
            --notes-file $notesFile `
            --verify `
            $zipPath
        if ($LASTEXITCODE -eq 0) {
            Write-Host "`n✅ SUCCESS! Release created!" -ForegroundColor Green
            Write-Host "View at: https://github.com/shanbei2033/PrismWave/releases/tag/$tagName" -ForegroundColor Cyan
        } else {
            Write-Host "`n❌ Failed to create release via gh CLI" -ForegroundColor Red
        }
    } catch {
        Write-Host "`n⚠️ GitHub CLI failed: $_" -ForegroundColor Yellow
    }
} else {
    Write-Host "`n⚠️ GitHub CLI not found" -ForegroundColor Yellow
    Write-Host "Please run manually:" -ForegroundColor Yellow
    Write-Host "  cd e:\Project\PrismWave" -ForegroundColor Gray
    Write-Host "  gh release create $tagName --title `$releaseTitle --notes-file `$notesFile `$zipPath" -ForegroundColor Gray
}

# Method 2: Direct REST API call as fallback
Write-Host "`nTrying direct GitHub API..." -ForegroundColor Yellow
try {
    # Get token from environment or keyring
    $token = $env:GITHUB_TOKEN
    
    if (-not $token) {
        # Try to get token from CLI config
        $cliConfig = Join-Path $env:LOCALAPPDATA "GitHub\CLI"
        if (Test-Path $cliConfig) {
            $content = Get-Content $cliConfig -Raw -ErrorAction SilentlyContinue
            if ($content) {
                $pattern = '(?<oauth_token>"oauth_token":""[^"]+")'
                $match = Select-String -Pattern $pattern -Input $content
                if ($match) {
                    $token = ($match.Matches.Groups[1].Value -replace '".*?:','').Replace('"','')
                }
            }
        }
    }
    
    if ($token) {
        $headers = @{
            "Authorization" = "Bearer $token"
            "Accept" = "application/vnd.github+json"
            "X-GitHub-Api-Version" = "2022-11-28"
        }
        
        $uri = "https://api.github.com/repos/shanbei2033/PrismWave/releases"
        
        $response = Invoke-RestMethod -Uri $uri -Method Post -Headers $headers -ContentType "application/json" -Body $releaseBody
        
        Write-Host "`n✅ GitHub API Release Created Successfully!" -ForegroundColor Green
        Write-Host "Release URL: $($response.html_url)" -ForegroundColor Cyan
        
        # Now upload the asset
        Write-Host "Uploading asset..." -ForegroundColor Yellow
        $uploadUrl = $response.upload_url -replace '\{.*\}'
        $uploadUri = "$uploadUrl?name=$(Split-Path $zipPath -Leaf)"
        $assetName = Split-Path $zipPath -Leaf
        
        $fileData = [IO.File]::ReadAllBytes($zipPath)
        $headers["Content-Type"] = "application/octet-stream"
        $headers["Content-Length"] = $fileData.Length
        
        $uploadResponse = Invoke-RestMethod -Uri $uploadUri -Method Post -Headers $headers -Body $fileData -TimeoutSec 300
        
        Write-Host "  ✓ Asset uploaded: $($uploadResponse.browser_download_url)" -ForegroundColor Green
        
    } else {
        Write-Host "`n❌ No GitHub token found for API authentication" -ForegroundColor Red
        Write-Host "Set GITHUB_TOKEN environment variable or use gh CLI" -ForegroundColor Gray
    }
} catch {
    Write-Host "`n⚠️ GitHub API error: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host "`n=========================================" -ForegroundColor Cyan
Write-Host "Done!" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "`nNote: If all automated methods failed, please upload manually:" -ForegroundColor Yellow
Write-Host "  1. Go to https://github.com/shanbei2033/PrismWave/releases/new" -ForegroundColor Gray
Write-Host "  2. Tag: v1.0.6" -ForegroundColor Gray
Write-Host "  3. Title: v1.0.6 - Playback Reliability Fix" -ForegroundColor Gray
Write-Host "  4. Upload prismwave-zip file" -ForegroundColor Gray
Write-Host "  5. Copy contents of release/release_notes_v1.0.6.md" -ForegroundColor Gray
Write-Host "" -ForegroundColor Gray

function ConvertFrom-SslString {
    param([string]$inputString)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($inputString)
    return [System.Convert]::ToBase64String($bytes)
}
