# Building the image with GitHub Actions (ACR Tasks workaround)

Your Azure free-trial subscription has ACR Tasks disabled, so `az acr build` won't work. This guide replaces **Phase 3** of `AZURE_PORTAL_DEPLOY.md`. Keep Phases 1, 2, and 4–8 from that guide as written — only the "how we build the image" part changes.

Instead of ACR Tasks, a **free GitHub Actions runner** will build the image and push it into your existing ACR. After setup, every `git push` to `main` triggers a rebuild automatically.

**Requirements**: a GitHub account (free). You do **not** need Git installed locally — all steps use the GitHub web UI.

---

## Step 1 — Make sure ACR admin user is enabled

(If you already did Phase 2 step 6, skip this.)

1. Portal → open your registry **DockerConverter**.
2. Left menu → **Settings → Access keys**.
3. **Admin user** toggle = **Enabled**.
4. Note the **Login server** (e.g. `dockerconverter.azurecr.io`), **Username** (usually `DockerConverter`), and either **password** or **password2**. You'll paste these into GitHub in Step 4.

---

## Step 2 — Create a new GitHub repository

1. Go to <https://github.com/new>.
2. **Repository name**: `word-to-pdf-service`.
3. **Visibility**: **Private** (recommended — the repo will contain your Dockerfile but not your API key, which lives in App Service env vars).
4. Leave **"Add a README"** **unchecked**. Leave everything else default.
5. Click **Create repository**.

You'll land on an empty repo page.

---

## Step 3 — Upload the project files

1. On the empty repo page, click **"uploading an existing file"** (it's a link in the "Quick setup" box).
2. On your computer: unzip `word-to-pdf-service.zip`. You'll have a folder `word-to-pdf-service/`.
3. **Open that folder** and select **all of its contents** (not the folder itself — the files and subfolders inside it).
   - **IMPORTANT**: include the hidden `.github` folder. On Windows, enable "Show hidden items" in Explorer's View menu. On macOS, press `Cmd+Shift+.` in Finder.
4. Drag everything into the GitHub upload area.
5. Wait until the file list at the bottom shows `.github/workflows/build-and-push.yml`, `Dockerfile`, `src/...`, etc.
6. Commit message: `Initial upload`. Click **Commit changes**.

> Tip: if you prefer the GitHub Desktop app or Git CLI, the equivalent is `git init && git add . && git commit -m "init" && git remote add origin <repo-url> && git push -u origin main`.

---

## Step 4 — Add the three GitHub secrets

The workflow needs to log in to your ACR. Store the credentials as encrypted repository secrets.

1. In the repo: **Settings** (top-right tab) → **Secrets and variables → Actions**.
2. Click **New repository secret**. Add these three one at a time:

   | Secret name | Value |
   |---|---|
   | `ACR_LOGIN_SERVER` | `dockerconverter.azurecr.io` (the Login server from ACR Access keys) |
   | `ACR_USERNAME` | the Username from ACR Access keys |
   | `ACR_PASSWORD` | either `password` or `password2` from ACR Access keys |

   (Don't include quotes or trailing spaces.)

---

## Step 5 — Run the workflow

The workflow runs automatically on any push to `main`, but since you pushed before the secrets were set, trigger it manually:

1. In the repo → **Actions** tab.
2. If GitHub asks "I understand my workflows, go ahead and enable them", click it.
3. In the left sidebar select **Build and push to ACR**.
4. On the right, click **Run workflow** → **Run workflow** (green button).
5. Click the new run that appears. Expand the **Build and push** step to watch the build.

First run takes **6–10 minutes** (downloading LibreOffice apt packages). Subsequent runs are much faster thanks to the `cache-from: type=gha` line in the workflow.

When the run finishes successfully, the summary shows the two image tags that were pushed, e.g.:

```
dockerconverter.azurecr.io/word-to-pdf:a1b2c3d
dockerconverter.azurecr.io/word-to-pdf:latest
```

Sanity-check in the Portal: **DockerConverter → Services → Repositories → word-to-pdf**. You should see both tags.

---

## Step 6 — Continue with Phase 4 of the main guide

Jump back to `AZURE_PORTAL_DEPLOY.md` **Phase 4 — Create the App Service Plan + Web App**. When it asks for:

- **Image**: `word-to-pdf`
- **Tag**: use `latest` (not `1.0`). Using `:latest` means App Service always pulls the most recently pushed image.

---

## Step 7 — Enable Continuous Deployment (optional but useful)

So that App Service automatically pulls the new `:latest` after each GitHub Actions build:

1. Web App left menu → **Deployment → Deployment Center → Settings** tab.
2. **Continuous deployment**: **On**.
3. **Save**.

Now the update loop is:

```
edit code in GitHub → push to main → Actions builds :latest → App Service pulls it → container restarts
```

No Portal clicks needed for subsequent releases.

---

## Security upgrade (optional)

Admin username/password works but gives broad access. Later, replace it with a GitHub-owned **service principal** scoped only to `AcrPush` on this one registry. Steps (all in Cloud Shell):

```bash
SUB=$(az account show --query id -o tsv)
ACR_ID=$(az acr show --name DockerConverter --query id -o tsv)

az ad sp create-for-rbac \
  --name gh-acr-push \
  --scopes $ACR_ID \
  --role AcrPush \
  --sdk-auth
```

Copy the JSON output, store it as a single GitHub secret `AZURE_CREDENTIALS`, and switch the workflow to use `azure/login@v2` + `az acr login` instead of `docker/login-action`. Not necessary for a trial, but nice for production.

---

## Troubleshooting

- **Actions run fails with "unauthorized: authentication required"** — the three secrets are wrong or have trailing whitespace. Recreate them.
- **Build step fails at `apt-get`** — transient package-mirror issue; re-run the workflow.
- **Works but App Service still serves the old image** — either use a unique tag each time (edit the Web App's **Deployment Center → Tag**), or restart the Web App after each push (**Overview → Restart**), or enable Continuous Deployment (Step 7).
- **First run says "disabled"** — go to repo → Actions tab → click the green "I understand my workflows" button.
