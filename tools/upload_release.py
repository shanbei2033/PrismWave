import requests
import json
import os

# Release metadata
TAG_NAME = "v1.0.6"
RELEASE_TITLE = "v1.0.6 - Playback Reliability Fix"
REPO = "shanbei2033/PrismWave"
ZIP_PATH = "artifacts/PrismWave-v1.0.6-win-x64.zip"
NOTES_FILE = "release/release_notes_v1.0.6.md"

print("=" * 50)
print("PrismWave v1.0.6 Release Upload")
print("=" * 50)

# Read release notes
with open(NOTES_FILE, 'r', encoding='utf-8') as f:
    release_body = f.read()

# Create release payload
payload = {
    "tag_name": TAG_NAME,
    "name": RELEASE_TITLE,
    "body": release_body,
    "draft": False,
    "prerelease": False
}

headers = {
    "Authorization": "token YOUR_GITHUB_TOKEN_HERE",  # Replace with your token
    "Accept": "application/vnd.github+json",
    "X-GitHub-Api-Version": "2022-11-28"
}

base_url = "https://api.github.com"

print("\n📦 Creating Release...")
try:
    # Step 1: Create release
    create_url = f"{base_url}/repos/{REPO}/releases"
    response = requests.post(create_url, json=payload, headers=headers)
    
    if response.status_code == 201:
        release_data = response.json()
        print(f"✅ Release created successfully!")
        print(f"🔗 URL: {release_data['html_url']}")
        
        # Step 2: Upload asset
        upload_url = release_data['upload_url'].split('{?')[0]
        filename = os.path.basename(ZIP_PATH)
        
        print(f"\n🚀 Uploading artifact ({filename})...")
        
        with open(ZIP_PATH, 'rb') as f:
            file_data = f.read()
        
        upload_headers = dict(headers)
        upload_headers["Content-Type"] = "application/octet-stream"
        upload_headers["Content-Length"] = str(len(file_data))
        
        upload_response = requests.post(
            upload_url,
            data=file_data,
            headers=upload_headers,
            params={"name": filename}
        )
        
        if upload_response.status_code == 201:
            print(f"✅ Asset uploaded successfully!")
            print(f"📥 Download URL: {upload_response.json()['browser_download_url']}")
        else:
            print(f"❌ Upload failed: {upload_response.text}")
            
    else:
        print(f"❌ Failed to create release: {response.status_code}")
        print(response.text)
        
except Exception as e:
    print(f"❌ Error: {str(e)}")
    print("\n💡 Please check your GITHUB_TOKEN and try again.")

print("\n" + "=" * 50)
print("Done!")
print("=" * 50)
