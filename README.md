# VertexAILibrary — ODC External Library

Google Vertex AI integration for OutSystems Developer Cloud (ODC).  
Exposes Gemini model calls as **Server Actions** you can drag into any ODC app or library.

---

## Prerequisites

| Requirement | Details |
|---|---|
| .NET SDK | **6.0** (required by ODC External Libraries) |
| Google Cloud project | Vertex AI API must be enabled |
| Service Account | Needs the **Vertex AI User** IAM role |
| ODC Portal access | Manage Connections / External Libraries permissions |

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

---

## Exposed Server Actions

### `GenerateContent`

Sends a single prompt and returns the model's text response.

| Parameter | Type | Description |
|---|---|---|
| `ProjectId` | Text | GCP project ID |
| `ServiceAccountJson` | Text | Full SA key JSON (use ODC Secret) |
| `Region` | Text | GCP region (see `GetSupportedRegions`) |
| `ModelId` | Text | e.g. `gemini-2.0-flash-001` |
| `Prompt` | Text | The prompt to send |
| `MaxOutputTokens` | Integer | Max tokens; 0 = use default (1024) |
| `Temperature` | Decimal | 0–1 sampling temp; -1 = use default (0.4) |
| **`ResponseText`** | Text | ← Output: generated text |

---

### `GenerateContentWithSystemPrompt`

Sends a system instruction + user prompt. Useful for personas and constrained behavior.

| Parameter | Type | Description |
|---|---|---|
| `ProjectId` | Text | GCP project ID |
| `ServiceAccountJson` | Text | Full SA key JSON |
| `Region` | Text | GCP region |
| `ModelId` | Text | Model ID |
| `SystemInstruction` | Text | Persona / behavior rules for the model |
| `UserPrompt` | Text | The user's message |
| `MaxOutputTokens` | Integer | Max tokens; 0 = default |
| **`ResponseText`** | Text | ← Output: generated text |

---

### `TestConnection`

Quick smoke-test to verify credentials and region without a real prompt.

| Parameter | Type | Description |
|---|---|---|
| `ProjectId` | Text | GCP project ID |
| `ServiceAccountJson` | Text | Full SA key JSON |
| `Region` | Text | GCP region to test |
| `ModelId` | Text | Model ID |
| **`Status`** | Text | ← `"OK"` or `"ERROR: <message>"` |

---

### `GetSupportedRegions`

Returns all regions supported by this library as a JSON array string.  
Use this to populate a region-selection dropdown in your ODC app.

| Output | Type | Example |
|---|---|---|
| **`RegionsJson`** | Text | `["us-central1","europe-west4",...]` |

---

## Supported Regions

| Region | Location |
|---|---|
| `us-central1` | Iowa (default) |
| `us-east4` | N. Virginia |
| `us-west1` | Oregon |
| `us-west4` | Las Vegas |
| `northamerica-northeast1` | Montréal |
| `southamerica-east1` | São Paulo 🇧🇷 |
| `europe-west1` | Belgium |
| `europe-west2` | London |
| `europe-west3` | Frankfurt |
| `europe-west4` | Netherlands |
| `europe-west9` | Paris |
| `asia-east1` | Taiwan |
| `asia-east2` | Hong Kong |
| `asia-northeast1` | Tokyo |
| `asia-northeast3` | Seoul |
| `asia-southeast1` | Singapore |
| `australia-southeast1` | Sydney |

---

## Recommended Models

| Model ID | Use case |
|---|---|
| `gemini-2.0-flash-001` | Fast, cost-efficient — best for most tasks |
| `gemini-1.5-pro-002` | Long context (1M tokens), complex reasoning |
| `gemini-1.5-flash-002` | Balanced speed and quality |

---

## Security Recommendations

- Store `ServiceAccountJson` in **ODC Secrets** (Settings → Secrets)
- Use the principle of least privilege: `roles/aiplatform.user` only
- Rotate Service Account keys periodically
- Consider using **Workload Identity** instead of JSON keys for GKE deployments
