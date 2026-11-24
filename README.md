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

### 3. Choose a region (data residency)

Call `GetSupportedRegions` to list valid values. Besides single regions (e.g. `europe-west4`),
the library supports the multi-regions `eu` and `us`, and the `global` endpoint:

| Region | Host | Request path |
|---|---|---|
| `europe-west4` | `europe-west4-aiplatform.googleapis.com` | `projects/<PROJECT_ID>/locations/europe-west4/...` |
| `eu` | `aiplatform.eu.rep.googleapis.com` | `projects/<PROJECT_ID>/locations/eu/...` |
| `us` | `aiplatform.us.rep.googleapis.com` | `projects/<PROJECT_ID>/locations/us/...` |
| `global` | `aiplatform.googleapis.com` | `projects/<PROJECT_ID>/locations/global/...` |

Use **`eu`** when EU data residency is mandatory. **`global`** does **not** guarantee data
residency (requests may be served from any region), so don't use it when residency is required.

### 4. Choose a publisher

Every generation action takes a `Publisher` parameter (call `GetSupportedPublishers` for valid values):

| Publisher | Models (examples) | Transport |
|---|---|---|
| `google` | `gemini-2.0-flash-001` | gRPC `GenerateContent` |
| `anthropic` | `claude-sonnet-4@20250514` | `rawPredict` (Anthropic Messages format) |

Model availability varies by publisher and region; Claude models must be enabled in the
Vertex AI Model Garden for your project, and are not offered in every region.

### Token usage outputs

`GenerateContent`, `GenerateContentWithSystemPrompt`, `GenerateContentWithFiles` and `GenerateContentWithSystemPromptAndFiles` return the token usage of the call as extra outputs, for cost allocation:

| Output | Google (`usageMetadata`) | Anthropic (`usage`) |
|---|---|---|
| `InputTokens` | `promptTokenCount` | `input_tokens` |
| `OutputTokens` | `candidatesTokenCount` | `output_tokens` |
| `CachedInputTokens` | 0 (not exposed by the SDK) | `cache_read_input_tokens` |
| `CacheCreationInputTokens` | 0 | `cache_creation_input_tokens` |
| `ThinkingTokens` | `totalTokenCount` − `promptTokenCount` − `candidatesTokenCount` | 0 (included in `OutputTokens`) |
| `TotalTokens` | `totalTokenCount` | sum of all of the above input/output counts |

### Multi-turn chat

`Chat` takes a list of `ChatMessage` (`Role` = `system`, `user`, `assistant` or `tool`, plus `Content`) and returns the next assistant reply.
It also returns `Usage` and `FinishReason`. `FinishReason` is publisher-specific (`Stop`/`MaxTokens` for `google`, `end_turn`/`max_tokens` for `anthropic`).
System messages are combined into the system instruction for both publishers.

### Tool calling (Anthropic publisher only)

`Chat` supports function/tool calling, with every tool payload passed as OpenAI-format JSON text (the library converts to and from Anthropic's format internally):

| Parameter | Direction | Meaning |
|---|---|---|
| `ToolsJson` | in | OpenAI `tools` array (`[{"type":"function","function":{"name","description","parameters"}}]`). Empty string = no tools. |
| `ToolChoice` | in | Empty or `auto` = the model decides (default). `none` = no tool calls. `required` = must call some tool. Any other value = a tool name it must call. Anything but empty/`auto` needs `ToolsJson`. |
| `ToolCallsJson` | out | OpenAI `tool_calls` array (`[{"id","type":"function","function":{"name","arguments"}}]`) when the model wants to call tools, otherwise empty. |
| `FinishReason` | out | `tool_use` when the model stopped to call tools. `ResponseText` may be empty in that case. |
| `ChatMessage.ToolCallsJson` | in | On an `assistant` message: the calls that turn made (replay `ToolCallsJson` from the previous reply). |
| `ChatMessage.ToolCallId` | in | On a `tool` message: the `id` of the call it answers; `Content` is the tool result. |

Loop: call `Chat` -> if `FinishReason` is `tool_use` (`ToolCallsJson` is not empty), run the tools -> call `Chat` again with the earlier messages, the assistant message (`ToolCallsJson`) and one `tool` message per call -> repeat until `FinishReason` is no longer `tool_use`. A `max_tokens` finish never returns `ToolCallsJson`; raise `MaxOutputTokens` and retry.

Passing tools (or `tool` messages) with the `google` publisher raises an error; it is not supported yet.

> **Breaking change:** `Chat` gained the mandatory `ToolsJson` and `ToolChoice` inputs and `ToolCallsJson` output, and `ChatMessage` gained two fields. Consumers must refresh the library reference and pass `""` for `ToolsJson` and `ToolChoice` to keep the old behaviour.

## Using the library as a custom AI model on ODC

ODC's custom AI model connections call a **REST endpoint**, not a Server Action. The library can't be registered directly.
Build a small ODC app that exposes `POST /custom/rest/aimodel/chat/completions` (the `aimodel` contract, `resources/swagger.json` in the `docs-odc` repo) and calls `VertexAI.Chat` internally:

1. Map the request: `messages` → `Messages`, `maxTokens` → `MaxOutputTokens`, `temperature` → `Temperature`, `model` → `ModelId`.
2. Read `ProjectId`, the service account, `Region` and `Publisher` from ODC Secrets / Settings, never from the request.
3. Map the response: `Usage` → `usage` (`prompt_tokens`, `completion_tokens`, `total_tokens`, `prompt_tokens_details.cached_tokens`), `FinishReason` → `choices[].finish_reason`.
4. Return standard HTTP error codes for bad input or upstream failures.
5. In the ODC Portal, add the model as **Custom connection** with the endpoint URL and authentication.

`stop`, `extraBody` and `extraHeaders` from the contract are not supported in this version.

## Running the unit tests

```bash
dotnet test tests/VertexAILibrary.UnitTests
```
### Sending files

`GenerateContentWithFiles` and `GenerateContentWithSystemPromptAndFiles` send one or more files
(images, PDFs, text and, for `google`, audio / video) together with the prompt. They take the same
inputs as `GenerateContent` / `GenerateContentWithSystemPrompt`, plus a `Files` list placed right
after the prompt:

| Action                                    | Inputs                                                                                                                         |
|-------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------|
| `GenerateContentWithFiles`                | `ProjectId`, `ServiceAccount`, `Region`, `Publisher`, `ModelId`, `Prompt`, `Files`, `MaxOutputTokens`, `Temperature`           |
| `GenerateContentWithSystemPromptAndFiles` | `ProjectId`, `ServiceAccount`, `Region`, `Publisher`, `ModelId`, `SystemInstruction`, `UserPrompt`, `Files`, `MaxOutputTokens` |

Both return `ResponseText` and the `Usage` output described above.

All generate actions also return `OutputFiles`, a list of `OutputFile` (`FileName`, `MimeType`, `Content`) holding any files the model returns (for example generated images from a Gemini image model). The list is empty when the model returns no file, and always empty for the `anthropic` publisher. The combined size of the returned files must stay under 4 MB, otherwise the action fails.

Each item of `Files` is an `InputFile` structure:

| Field      | Type        | Description                                                                                        |
|------------|-------------|----------------------------------------------------------------------------------------------------|
| `FileName` | Text        | File name. Used to infer the MIME type when `MimeType` is empty, and as the Claude document title. |
| `MimeType` | Text        | MIME type (e.g. `application/pdf`, `image/png`). Optional if `FileName` has a known extension.     |
| `Content`  | Binary Data | File content (mandatory, must not be empty).                                                       |

**Rules**

- At least one file is required, and the prompt (`Prompt` / `UserPrompt`) must not be empty.
- Files are sent **before** the prompt text, in list order.
- MIME type resolution: the explicit `MimeType` wins (case-insensitive, parameters such as
  `; charset=utf-8` are ignored, `image/jpg` is treated as `image/jpeg`); otherwise it is inferred
  from the `FileName` extension; otherwise the call fails.
- The MIME type must be supported by the chosen publisher. All validation errors are raised before
  any credential or network call, and never include file content.

**Supported MIME types per publisher**

| Publisher   | Accepted MIME types                                                                                                                                                                                                                                                                                                      |
|-------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `google`    | image/png, image/jpeg, image/webp, image/heic, image/heif, application/pdf, text/plain, audio/wav, audio/mp3, audio/mpeg, audio/aiff, audio/aac, audio/ogg, audio/flac, video/mp4, video/mpeg, video/mpg, video/mpegps, video/mov, video/quicktime, video/avi, video/x-flv, video/flv, video/webm, video/wmv, video/3gpp |
| `anthropic` | image/jpeg, image/png, image/gif, image/webp, application/pdf, text/plain                                                                                                                                                                                                                                                |

Sources: [Vertex AI – Send multimodal prompts](https://docs.cloud.google.com/vertex-ai/generative-ai/docs/multimodal/send-multimodal-prompts)
and [Claude – PDF support](https://platform.claude.com/docs/en/build-with-claude/pdf-support).
The model API stays the final arbiter: a model may reject a type it does not support.

**Extension → MIME type inference**

| Extension(s)   | MIME type       | Extension(s)   | MIME type  |
|----------------|-----------------|----------------|------------|
| `.png`         | image/png       | `.aac`         | audio/aac  |
| `.jpg` `.jpeg` | image/jpeg      | `.ogg`         | audio/ogg  |
| `.gif`         | image/gif       | `.flac`        | audio/flac |
| `.webp`        | image/webp      | `.mp4`         | video/mp4  |
| `.heic`        | image/heic      | `.mpeg` `.mpg` | video/mpeg |
| `.heif`        | image/heif      | `.mov`         | video/mov  |
| `.pdf`         | application/pdf | `.avi`         | video/avi  |
| `.txt`         | text/plain      | `.flv`         | video/flv  |
| `.mp3`         | audio/mpeg      | `.webm`        | video/webm |
| `.wav`         | audio/wav       | `.wmv`         | video/wmv  |
| `.aiff`        | audio/aiff      | `.3gp`         | video/3gpp |

**How files are sent**

| Publisher   | MIME type         | Request part                                                                     |
|-------------|-------------------|----------------------------------------------------------------------------------|
| `google`    | any accepted type | `Part.InlineData` (`mimeType` + bytes)                                           |
| `anthropic` | `image/*`         | `image` block, base64 source                                                     |
| `anthropic` | `application/pdf` | `document` block, base64 source, `title` = `FileName`                            |
| `anthropic` | `text/plain`      | `document` block, text source (UTF-8 decoded, BOM removed), `title` = `FileName` |

**Size limits**

- ODC limits each call's serialized inputs to **5.5 MB**. That covers all files plus the prompt and
  the other inputs, before the library's own base64 / JSON encoding, so the usable file size is a bit
  lower. Larger files are not supported; the library doesn't add its own cap.
- Claude also enforces its own limits (for example at most 100 PDF pages and 100 images per request).

## 5. Build & Package for ODC

The library lives in `src/VertexAILibrary/` and the unit tests in `tests/VertexAILibrary.UnitTests/`.
`VertexAILibrary.slnx` at the repository root groups both projects.

### Build and test

```bash
# Build the library and the test project
dotnet build

# Run the unit tests
dotnet test --project tests/VertexAILibrary.UnitTests
```

### Package the library for ODC

Run these from the repository root. Each step clears output from earlier runs, so the upload
contains only the library and its runtime dependencies.

```bash
# 1. Clear the previous publish output
rm -rf publish

# 2. Publish the library for the Linux x64 runtime ODC uses
dotnet publish src/VertexAILibrary/VertexAILibrary.csproj -c Release -r linux-x64 --self-contained false -o ./publish

# 3. Remove the previous archive (zip adds to an existing archive instead of replacing it)
rm -f VertexAILibrary.zip

# 4. Zip the publish output — this is the file you upload to ODC Portal
cd publish && zip -r ../VertexAILibrary.zip . && cd ..
```

> **Always pass the library project to `dotnet publish`.** Running `dotnet publish` at the
> repository root publishes the whole solution, including the xUnit test project, and puts the
> test assemblies in the upload.

### 6. Upload to ODC Portal

1. Open **ODC Portal → Integrate → External Libraries**
2. Click **Upload**
3. Select `VertexAILibrary.zip`
4. Wait for validation and publishing to complete
