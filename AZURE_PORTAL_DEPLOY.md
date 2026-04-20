# Deploying Word-to-PDF Service to Azure (Portal walkthrough)

This guide walks you through deploying the service to **Azure App Service for Containers** in **UK South**, using the **Azure Portal** for clicks and **Azure Cloud Shell** for the one command that has to run in a terminal (building the Docker image — your laptop doesn't need Docker installed).

**Estimated cost**: ~£14/month total (Basic B1 App Service ~£10 + Basic ACR ~£4). Easily covered by your free trial credit.

**Time needed**: 25–35 minutes the first time.

---

## Phase 0 — Prepare the source for upload

You already have the project folder `word-to-pdf-service/` and a sibling file `word-to-pdf-service.zip`. Keep that zip handy — you'll upload it to Cloud Shell in Phase 2.

Before you start, **change the API key** so it isn't the placeholder:

1. Open `word-to-pdf-service/src/WordToPdfService/appsettings.json`.
2. Find `"Keys": "replace-me-with-a-strong-key"`.
3. Replace the value with a long random string (e.g. run `openssl rand -hex 32` or use any password generator). Save it somewhere — you'll set it as an env var in Azure (so you can also leave the file as-is and just set it via env var; env vars override the JSON).
4. (If you edit the file, re-zip the folder.)

---

## Phase 1 — Create the resource group

1. Sign in to <https://portal.azure.com>.
2. Search bar (top) → type **Resource groups** → click it.
3. Click **+ Create**.
4. Fill in:
   - **Subscription**: your free trial subscription.
   - **Resource group**: `rg-word-to-pdf`
   - **Region**: `UK South`
5. **Review + create** → **Create**. Wait ~10 seconds.

---

## Phase 2 — Create an Azure Container Registry (ACR)

The registry stores your Docker image. App Service will pull from it.

1. Search bar → **Container registries** → **+ Create**.
2. Fill in:
   - **Subscription**: same.
   - **Resource group**: `rg-word-to-pdf`.
   - **Registry name**: `acrwordtopdf<your-initials><3-digit-number>` (must be globally unique, lowercase, letters+digits only). Example: `acrwordtopdfax742`.
   - **Location**: `UK South`.
   - **Pricing plan**: `Basic`.
3. **Review + create** → **Create**. Takes ~30 seconds.
4. When it's done, click **Go to resource**.
5. Left menu → **Settings → Access keys**.
6. Toggle **Admin user** = **Enabled**. (This lets App Service authenticate with username + password — easiest path. We'll lock this down later by switching to managed identity if you want.)
7. Note the values shown (you don't need to copy them — App Service will read them automatically when wired up).

---

## Phase 3 — Build the image in ACR using Cloud Shell

You don't need Docker on your laptop. ACR has a built-in build service called **ACR Tasks** that compiles the image in the cloud.

1. In the Portal top bar, click the **Cloud Shell** icon (looks like `>_`).
2. Choose **Bash** when prompted. If it asks to create a storage account for Cloud Shell, accept the defaults — that's free for trivial use.
3. In the Cloud Shell toolbar click the **Upload/Download files** icon (the one with an arrow), then **Upload**, and pick `word-to-pdf-service.zip` from your computer.
4. After upload, in the Cloud Shell prompt run:

   ```bash
   unzip -o word-to-pdf-service.zip
   cd word-to-pdf-service
   ```

5. Build the image directly into ACR (replace `<acr-name>` with whatever you named your registry, e.g. `acrwordtopdfax742`):

   ```bash
   az acr build \
     --registry <acr-name> \
     --image word-to-pdf:1.0 \
     --file Dockerfile \
     .
   ```

   This streams build logs into Cloud Shell. It takes 5–8 minutes the first time (mostly downloading the LibreOffice apt packages). When it ends with `Run ID: ... was successful`, the image is in your registry.

6. (Optional sanity check) verify the image is there:

   ```bash
   az acr repository show-tags \
     --name <acr-name> \
     --repository word-to-pdf
   ```

   You should see `1.0`.

---

## Phase 4 — Create the App Service Plan + Web App

1. Portal search → **App services** → **+ Create** → **Web App**.
2. **Basics** tab:
   - **Subscription / Resource group**: same (`rg-word-to-pdf`).
   - **Name**: `app-word-to-pdf-<your-initials><digits>` (becomes `https://<name>.azurewebsites.net`, must be globally unique).
   - **Publish**: `Container`.
   - **Operating System**: `Linux`.
   - **Region**: `UK South`.
   - **Linux Plan**: click **Create new** → name `asp-word-to-pdf-uksouth` → **OK**.
   - **Pricing plan**: click **Explore pricing plans** → choose **Basic B1** (`Production` tab). Click **Select**.
3. Click **Next: Container**.
4. **Container** tab:
   - **Image source**: `Azure Container Registry`.
   - **Registry**: pick the ACR you created.
   - **Image**: `word-to-pdf`.
   - **Tag**: `1.0`.
   - **Startup command**: leave blank.
5. Click **Next: Networking** — keep defaults (public access enabled for now; we'll restrict in Phase 6).
6. Click **Next: Monitoring** — turn **Application Insights** **On** (free at this scale, very useful). Accept the suggested workspace.
7. **Review + create** → **Create**. Provisioning takes 1–3 minutes.
8. When done, click **Go to resource**.

---

## Phase 5 — Configure environment variables

The service reads its config from environment variables. The Portal calls these "App settings".

1. In your Web App's left menu: **Settings → Environment variables** (older portals call this **Configuration → Application settings**).
2. Click **+ Add** and create each of the following (one at a time). Click **Apply** after the last one.

   | Name | Value | Notes |
   |---|---|---|
   | `ASPNETCORE_URLS` | `http://+:8080` | Container listens on 8080 |
   | `WEBSITES_PORT` | `8080` | Tells App Service which port to forward |
   | `AUTH__APIKEY__KEYS` | `<your-strong-key>` | The key Creatio will send in `X-API-Key` |
   | `CONVERTER__TIMEOUTSECONDS` | `90` | LibreOffice hard timeout |
   | `CONVERTER__MAXUPLOADMB` | `50` | Reject larger uploads |
   | `EnableSwagger` | `false` | Set to `true` only while debugging |

   The double underscore `__` is how ASP.NET maps env vars to nested JSON config (`Auth:ApiKey:Keys` → `AUTH__APIKEY__KEYS`).

3. Click **Apply** at the bottom and confirm — the app will restart.

---

## Phase 6 — Lock it down

1. Left menu → **Settings → Configuration → General settings** (or **Configuration → General settings** in newer Portals).
2. Set:
   - **HTTPS Only**: `On`
   - **Minimum TLS Version**: `1.2`
   - **FTP state**: `Disabled`
3. **Save**.

(Optional, recommended) Restrict callers to Creatio's outbound IPs:

4. Left menu → **Settings → Networking → Access restrictions**.
5. **Site access and rules** → **+ Add rule**.
6. **Action** = `Allow`, **Priority** = `100`, **Name** = `creatio`, **Type** = `IPv4`, **IP Address Block** = `<creatio-egress-ip>/32` (you can find this in your Creatio admin panel or ask Creatio support). Add a rule per IP if there are multiple.
7. After your allow rules are in place, the implicit final `Deny all` (priority 2147483647) blocks everything else. Save.

---

## Phase 7 — Smoke test

In Cloud Shell, replace placeholders and run:

```bash
APP_URL="https://<your-app-name>.azurewebsites.net"
API_KEY="<your-strong-key>"

# Health check (no auth)
curl -i $APP_URL/health
# Expect: HTTP/1.1 200 OK and body "Healthy"
```

To test a real conversion, upload a `.docx` to Cloud Shell first, then:

```bash
curl -i \
  -H "X-API-Key: $API_KEY" \
  -F "file=@sample.docx" \
  -o sample.pdf \
  $APP_URL/api/v1/convert

ls -la sample.pdf      # should be a real PDF
file sample.pdf        # should report: PDF document
```

If the first conversion is slow (10–15 seconds), that's LibreOffice cold-starting; subsequent calls will be much quicker.

---

## Phase 8 — Wire up Creatio

1. In Creatio's admin: **System Designer → System settings → New**.
   - Code: `WordToPdfServiceUrl`, Type: `Text (250 characters)`, Default: `https://<your-app-name>.azurewebsites.net`
   - Code: `WordToPdfApiKey`, Type: `Text (250 characters)`, Default: `<your-strong-key>` (mark as encrypted if your Creatio version supports it)
2. Add the C# files from `creatio/` into a Configuration package (e.g. `UsrIntegrations`):
   - `WordToPdfClient.cs`
   - Use `ScriptTask_Example.cs` as the body of a Script Task in any business process.

---

## Updating later

When you change code:

1. Re-zip the folder (or pull from Git in Cloud Shell).
2. In Cloud Shell:
   ```bash
   cd word-to-pdf-service
   az acr build --registry <acr-name> --image word-to-pdf:1.1 .
   ```
3. In the Web App → **Deployment Center** → change **Tag** to `1.1` → **Save**. The site restarts on the new image.

You can also keep the tag as `:latest` and just restart the Web App after each build, but using explicit version tags makes rollback trivial.

---

## Troubleshooting

- **Web App returns 503 / "Application Error"** — check **Log stream** (left menu → **Monitoring → Log stream**). Most common causes: wrong `WEBSITES_PORT` (must be `8080`), or the container failed to start because `AUTH__APIKEY__KEYS` wasn't set.
- **First request times out** — increase `CONVERTER__TIMEOUTSECONDS` (try 120). LibreOffice can take ~15s to warm up on the first invocation.
- **Conversion produces a blank/garbled PDF** — usually a missing font for the document's language. Add the relevant `fonts-noto-*` package in the Dockerfile and rebuild.
- **B1 keeps OOM'ing** — bump to **B2** (more RAM). LibreOffice + a complex doc can briefly need 1+ GB.
- **ACR pull fails (401)** — make sure **Admin user** is enabled in the registry (Phase 2 step 6); App Service uses those creds automatically.

---

## Cost summary (UK South, list prices)

| Resource | SKU | Approx £/month |
|---|---|---|
| App Service Plan | Basic B1 (Linux) | ~£10 |
| Container Registry | Basic | ~£4 |
| Application Insights | First 5 GB free | £0 at low volume |
| **Total** | | **~£14/month** |

Free trial gives you $200 credit (~£160) for 30 days, so you have ~10× headroom.
