# VertexAILibrary — ODC External Library

Google Vertex AI integration for OutSystems Developer Cloud (ODC).  
Exposes calls as **Server Actions** you can drag into any ODC app or library.

---

## Setup

### 1. Create a Google Cloud Service Account

```bash
# Create the service account
gcloud iam service-accounts create odc-vertex-ai \
    --display-name="ODC Vertex AI"

# Grant Vertex AI User role
gcloud projects add-iam-policy-binding YOUR_PROJECT_ID \
    --member="serviceAccount:odc-vertex-ai@YOUR_PROJECT_ID.iam.gserviceaccount.com" \
    --role="roles/aiplatform.user"

# Download the JSON key
gcloud iam service-accounts keys create sa-key.json \
    --iam-account=odc-vertex-ai@YOUR_PROJECT_ID.iam.gserviceaccount.com
```

Store the **full contents of `sa-key.json`** in an **ODC Secret** or **Settings** value.  
Never hard-code it in your app.

### 2. Enable the Vertex AI API

```bash
gcloud services enable aiplatform.googleapis.com
```

### 3. Build & Package for ODC

```bash
# Restore packages
dotnet restore

# Publish (self-contained, no runtime needed in ODC)
dotnet publish -c Release -o ./publish

# Zip the publish output — this is what you upload to ODC Portal
cd publish
zip -r ../VertexAILibrary.zip .
```

### 4. Upload to ODC Portal

1. Open **ODC Portal → Integrate → External Libraries**
2. Click **Upload**
3. Select `VertexAILibrary.zip`
4. Wait for validation and publishing to complete
