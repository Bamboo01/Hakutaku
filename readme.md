# Hakutaku

The backend for the **Fabled** plugin. It stores game data (players, characters, telemetry) like PlayFab does, and comes with an admin website for looking at it.

You don't need to know how any of it works to run it. Follow the steps below in order.

---

## Step 1: Install Docker Desktop (once)

Docker is what runs Hakutaku. It's the only thing you need to install.

1. Download **Docker Desktop** from <https://www.docker.com/products/docker-desktop/> and install it. Keep all the default options.
2. **Restart your computer** if the installer asks you to.
3. Open Docker Desktop and accept its terms. You can skip signing in.
4. Wait until the bottom-left corner says **Engine running**.

## Step 2: Download Hakutaku (once)

1. Go to <https://github.com/Bamboo01/Hakutaku>.
2. Click the green **Code** button, then **Download ZIP**.
3. Unzip it somewhere easy to find, for example your Documents folder. You get a folder called **Hakutaku-master**.

> If you already use Git, `git clone https://github.com/Bamboo01/Hakutaku.git` does the same thing, and the folder is just called `Hakutaku`.

## Step 3: Start it

**Windows:** open the folder that has **`start.bat`** in it and double-click `start.bat`. (Windows sometimes unzips into a `Hakutaku-master` folder *inside* another `Hakutaku-master` folder. If you don't see `start.bat`, open the inner one.)

**Mac or Linux:** open the **Terminal** app. Type `bash` followed by a space, drag **`start.sh`** from the Hakutaku folder into the Terminal window, then press Enter.

A window full of text appears. **The first time takes 5–10 minutes**, because it downloads and builds everything. After that, it starts in a few seconds.

When it's done, your web browser opens the admin site by itself.

## Step 4: Log in

The admin site is at **<http://localhost:8090>**. Note the **`:8090`** at the end. Plain `http://localhost` will not work.

| | |
|---|---|
| Username | `admin` |
| Password | `hakutaku` |

The project wiki (the full documentation) is at <http://localhost:5020>.

## Stopping it

**Windows:** double-click **`stop.bat`**.

**Mac or Linux:** same as starting, but drag in **`stop.sh`** instead.

Your data is saved and will be there next time you start it. After stopping, you can close Docker Desktop too.

## Getting the latest version

Stop Hakutaku, download the ZIP again and replace the old folder with the new one (or run `git pull` if you used Git), then start it again. Your data is kept.

---

## Something went wrong?

### "Docker is not installed" or "Docker is not running"

Go back to [Step 1](#step-1-install-docker-desktop-once). On Windows the script tries to open Docker Desktop for you, but the very first time you have to open it yourself and accept its terms.

### Docker Desktop won't start on Windows (it mentions virtualization or WSL)

Virtualization has to be switched on in your computer's BIOS settings. See the [setup guide](docs/setup.md#step-2--docker) for how, or ask someone on the team.

### "port is already allocated"

Another program on your computer is already using one of the ports Hakutaku needs (80, 5020, 5432 or 8090). The usual culprits are a PostgreSQL install (port 5432) or IIS/Skype (port 80). Close that program, then start Hakutaku again.

### The page can't be reached, or `http://localhost` shows a 404 error

Check the address ends in **`:8090`**. If it does, wait half a minute and refresh, because the server may still be starting.

### The password `hakutaku` doesn't work

You probably ran an older version of Hakutaku before, and its database still has the old password. You can wipe the local database and start fresh. **This deletes all your local Hakutaku data. Make sure you back any important data up.**

Open a terminal in the folder that has `start.bat` in it:

- **Windows:** click the address bar at the top of File Explorer, type `cmd` and press Enter.
- **Mac or Linux:** open Terminal, type `cd` followed by a space, drag the folder into the window and press Enter.

Then run:

```
docker compose -f compose.dev.yaml down -v
```

and start Hakutaku again.

### Anything else

The start window prints what went wrong. Send a screenshot of it to the team.

---

## For developers

The steps above run everything in Docker with no hot reload. To work on the code you'll also want the **.NET 10 SDK** and **Node 22+**, and the dev loop in [docs/setup.md](docs/setup.md#mode-b--dev-loop-the-normal-one).

The scripts are thin wrappers around `docker compose -f compose.dev.yaml up -d --build` and `... down`. `compose.dev.yaml` is **local-only** (hardcoded credentials, no TLS), so never deploy it. Production uses `compose.yaml`; see [Deploy](docs/operations/deploy.md).

Assume the wiki in [`docs/`](docs/) is **slightly out of date**. The project is still very much a **work in progress!!** But below are the important sections you may want to check out if you're planning to develop:

| | |
|---|---|
| [Setup](docs/setup.md) | Every install step in detail, plus troubleshooting |
| [Project structure](docs/architecture/structure.md) | Layout, tech stack, why |
| [API reference](docs/reference/api.md) | Every endpoint and error shape |
| [Auth and sessions](docs/components/auth.md) | Hashing, cookies, expiry, roles |
| [Deploy and CI/CD](docs/operations/deploy.md) | The VM, Jenkins, the SSH tunnel |
| [Contributing](docs/reference/contributing.md) | Branches, migrations, house style |

[TODO.md](TODO.md) is the roadmap.
