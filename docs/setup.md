# Setup

This page assumes you have nothing installed and have never seen the project.
Follow it top to bottom and you will end up with the server, the admin UI and
these docs all running on your machine.

Every step has a **check it worked** box. If a check fails, stop there — the
next step will not fix it.

## Pick how you want to run it

There are three ways, and they are not alternatives so much as different jobs:

| Mode | What you get | Use it when |
|---|---|---|
| **A — Full Docker** | Everything in containers, one command | You just want it running, or you are testing the real image |
| **B — Dev loop** | Postgres in Docker, server and UI on your machine with hot reload | You are writing code (this is the normal one) |
| **C — Docs** | This wiki, live-reloading | You are editing documentation |

Mode B is what you will use day to day. Do the installs below, then jump to
whichever mode you need.

---

## Step 1 — Git, and the repo

=== "Windows"

    Install [Git for Windows](https://git-scm.com/download/win). Accept the
    defaults; they include Git Bash, which some of the commands in these docs
    assume.

=== "macOS"

    ```bash
    xcode-select --install   # ships git
    ```

=== "Linux"

    ```bash
    sudo apt update && sudo apt install -y git   # Debian/Ubuntu
    ```

Then clone:

```bash
git clone https://github.com/Bamboo01/Hakutaku.git
cd Hakutaku
```

!!! success "Check it worked"
    `git log --oneline -1` prints a commit. Everything from here on runs from
    the repo root unless a command says otherwise.

---

## Step 2 — Docker

Docker runs the database, and in Mode A the whole stack.

=== "Windows"

    1. Install [Docker Desktop](https://www.docker.com/products/docker-desktop/).
    2. When it asks, leave the **WSL 2 backend** enabled. It is faster, and it is
       what everyone else is using.
    3. Reboot if it asks. It will ask.
    4. Launch Docker Desktop and wait for the whale icon in the tray to stop
       animating. Nothing works until it has fully started.

    !!! warning "Virtualisation has to be on"
        If Docker Desktop refuses to start with a message about virtualisation,
        enable **Intel VT-x** / **AMD-V** in your BIOS, and make sure
        *Virtual Machine Platform* is ticked under **Windows Features**.

=== "macOS"

    Install [Docker Desktop](https://www.docker.com/products/docker-desktop/),
    picking the **Apple Silicon** or **Intel** build to match your machine.
    Launch it and wait for the tray icon to settle.

=== "Linux"

    ```bash
    curl -fsSL https://get.docker.com | sh
    sudo usermod -aG docker "$USER"
    ```

    Log out and back in, otherwise every `docker` command needs `sudo`.

!!! success "Check it worked"
    ```bash
    docker --version
    docker compose version
    docker run --rm hello-world
    ```

    All three must succeed. Note it is `docker compose` (a subcommand), not the
    older `docker-compose` — if only the hyphenated one exists, your Docker is
    too old.

---

## Step 3 — The .NET SDK (Mode B only)

The server targets **.NET 10**. Mode A does not need this, because the
Dockerfile builds inside a container.

=== "Windows"

    Download the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
    installer and run it. Then **open a new terminal** — the installer edits
    `PATH`, and your existing terminal still has the old copy.

=== "macOS"

    ```bash
    brew install --cask dotnet-sdk
    ```

    Or use the installer from the
    [download page](https://dotnet.microsoft.com/download/dotnet/10.0).

=== "Linux"

    ```bash
    sudo apt install -y dotnet-sdk-10.0   # Debian/Ubuntu
    ```

    If your distro does not package it yet, use the
    [install script](https://dotnet.microsoft.com/download/dotnet/scripts).

!!! success "Check it worked"
    ```bash
    dotnet --list-sdks     # a 10.x line must appear
    cd server && dotnet build
    ```

    `dotnet build` should end with `Build succeeded`. The first run downloads
    NuGet packages and takes a minute.

---

## Step 4 — Node (Mode B only)

The UI needs **Node 22 or newer**.

=== "Windows / macOS"

    Install the **LTS** build from [nodejs.org](https://nodejs.org/). On macOS
    `brew install node` works too.

=== "Linux"

    Distro packages are usually too old. Use
    [nvm](https://github.com/nvm-sh/nvm):

    ```bash
    curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.40.1/install.sh | bash
    nvm install 22
    ```

!!! success "Check it worked"
    ```bash
    node --version     # v22.x or higher
    npm --version
    ```

---

## Step 5 — The docs toolchain (Mode C only)

MkDocs is Python tooling. The project does not otherwise use Python, so there
are two ways in — and **you do not have to install Python at all**.

=== "Docker (no install)"

    ```bash
    docker run --rm -p 5020:5020 -v "${PWD}:/docs" \
      squidfunk/mkdocs-material serve --dev-addr 0.0.0.0:5020
    ```

    !!! warning "Run this from the repo root"
        `${PWD}` is your *current* directory, so running it anywhere else mounts
        the wrong folder and the container exits with
        `Config file 'mkdocs.yml' does not exist.` Either `cd` to the repo first,
        or pass the path outright:

        ```bash
        docker run --rm -p 5020:5020 -v "D:/Work/Hakutaku:/docs"           squidfunk/mkdocs-material serve --dev-addr 0.0.0.0:5020
        ```

    Best if you only occasionally touch docs.

=== "Python + pip"

    Needs Python 3.9 or newer from
    [python.org](https://www.python.org/downloads/) (tick **Add Python to PATH**
    on Windows).

    ```bash
    python -m pip install --user -r requirements-docs.txt
    ```

    `--user` keeps it out of system site-packages, so it cannot break anything
    else on your machine. Starts faster and is nicer to work with than the
    Docker route.

    !!! tip "`mkdocs: command not found` right after installing?"
        `--user` installs the executable into a per-user Scripts directory that
        is frequently **not on `PATH`** (on Windows, something like
        `%APPDATA%\Python\Python310\Scripts`). You do not need to fix
        `PATH` — just run it as a module instead:

        ```bash
        python -m mkdocs serve
        ```

        `python -m mkdocs` works anywhere `mkdocs` does, and is the safer form
        to use in scripts.

!!! success "Check it worked"
    ```bash
    python -m mkdocs --version
    ```

    Or, for the Docker route, the container starts without an error.

---

## Mode A — Full Docker

One command builds the UI, builds the server, and starts all four containers:

```bash
docker compose -f compose.dev.yaml up --build
```

The first run takes a few minutes, because it downloads base images and
compiles everything. Then open:

**<http://localhost:8090>**

!!! warning "Not `http://localhost` — port 8090"
    Caddy is on `:80`, but the Caddyfile is **default-deny**: it serves
    `/Health` and answers `404` for everything else, including the UI itself.
    That is [deliberate](components/caddy.md), and local dev shares the same
    file as production so the two cannot drift. `compose.dev.yaml` publishes the
    app directly on `127.0.0.1:8090` so you can still reach the UI.

A **502** in the first couple of seconds is normal — Caddy starts before the
server has finished booting. Refresh.

Stop it with `Ctrl+C`, or:

```bash
docker compose -f compose.dev.yaml down       # keeps the database
docker compose -f compose.dev.yaml down -v    # deletes the database too
```

---

## Mode B — Dev loop (the normal one)

Three terminals. Postgres stays in Docker; the server and UI run on your
machine so they hot-reload.

**Terminal 1 — database:**

```bash
docker compose -f compose.dev.yaml up -d postgres
```

**Terminal 2 — server on `:5008`:**

```bash
cd server
dotnet watch
```

It applies any pending migrations on startup, so you never run
`dotnet ef database update` by hand.

**Terminal 3 — UI on `:5173`:**

```bash
cd web
npm install     # first time only
npm run dev
```

Open **<http://localhost:5173>**.

!!! tip "Why there is no CORS problem"
    Vite proxies `/api` to `http://localhost:5008` (see `web/vite.config.ts`),
    so the browser only ever talks to one origin. This matters more than it
    looks: the session cookie is `SameSite=Strict`, so splitting the UI and API
    across two origins would break login entirely. See
    [Web UI](components/web.md).

!!! success "Check it worked"
    ```bash
    curl http://localhost:5008/Health
    # {"health":"ok"}
    ```

---

## Mode C — Docs

From the repo root:

=== "Python + pip"

    ```bash
    mkdocs serve
    # or, if the mkdocs executable is not on PATH:
    python -m mkdocs serve
    ```

=== "Docker"

    ```bash
    docker run --rm -p 5020:5020 -v "${PWD}:/docs" \
      squidfunk/mkdocs-material serve --dev-addr 0.0.0.0:5020
    ```

Open **<http://localhost:5020>**. Edit any file under `docs/` and the browser
reloads itself.

The port and bind address come from `dev_addr: 0.0.0.0:5020` in `mkdocs.yml`.
`0.0.0.0` means **anyone who can reach your machine on the network can read the
docs** — intentional, so you can hand a teammate a link.

!!! note "It is a dev server"
    `mkdocs serve` is single-threaded, with no TLS and no access control. Fine
    for a team on a LAN or over a tunnel. If the wiki ever needs a permanent
    home, `mkdocs build` emits a static `site/` directory that any web server
    can host.

---

## Your first login

The admin UI needs an account, and the app creates one for you — **once**.

On the very first start against an empty database, the server sees no admins and
seeds an owner with the username `admin`. The password comes from the
`HAKUTAKU_ADMIN_PASSWORD` environment variable; if that is unset, it
**generates one and prints it to the log exactly once**.

=== "Mode B (dotnet watch)"

    Set it yourself before the first start, which is much less annoying:

    ```powershell
    # PowerShell
    $env:HAKUTAKU_ADMIN_PASSWORD = "devpassword"
    dotnet watch
    ```

    ```bash
    # bash
    HAKUTAKU_ADMIN_PASSWORD=devpassword dotnet watch
    ```

=== "Mode A (full Docker)"

    `compose.dev.yaml` does not forward that variable to the app, so read the
    generated password out of the log instead:

    ```bash
    docker compose -f compose.dev.yaml logs app | grep -i password
    ```

Then sign in at the UI with username `admin` and that password.

!!! danger "Missed the password?"
    The seeder only runs while the `admin_users` table is empty, so restarting
    will not print it again. Either wipe the database:

    ```bash
    docker compose -f compose.dev.yaml down -v
    ```

    …or delete just the admins and let it re-seed:

    ```bash
    docker compose -f compose.dev.yaml exec postgres \
      psql -U hakutaku -d hakutaku -c "DELETE FROM admin_users;"
    ```

    Both are safe locally. Neither is something you do in production — see
    [Operations](operations/deploy.md).

---

## Troubleshooting

??? failure "The server cannot connect to Postgres on startup"
    Almost always a stale volume. Postgres applies
    `POSTGRES_USER`/`POSTGRES_PASSWORD` **only when its data directory is
    empty**, so if you ever ran an older build with different credentials,
    changing them now does nothing.

    ```bash
    docker compose -f compose.dev.yaml down -v
    ```

    That deletes all local data and lets it initialise cleanly.

??? failure "`http://localhost` gives a 404 in Mode A"
    Working as designed — use **<http://localhost:8090>**. See the warning under
    [Mode A](#mode-a-full-docker).

??? failure "Port already in use"
    The ports in play are `5008` (server), `5173` (UI), `5020` (docs), `5432`
    (Postgres), `8090` (app in Docker) and `80` (Caddy). On Windows, `:80`
    commonly collides with IIS.

    ```powershell
    netstat -ano | findstr :5432     # Windows
    ```

    ```bash
    lsof -i :5432                    # macOS/Linux
    ```

??? failure "`dotnet watch` says the SDK was not found"
    Open a new terminal. The installer edited `PATH` and your current shell has
    the old copy.

??? failure "`npm run build` fails on types but `npm run dev` is fine"
    `build` runs `vue-tsc` first, and the TypeScript config is strict on
    purpose — `noUnusedLocals`, `noUnusedParameters` and `erasableSyntaxOnly`
    are all on. `erasableSyntaxOnly` is the surprising one: it bans TypeScript
    syntax that has runtime behaviour, so constructor parameter properties
    (`constructor(public x: number)`) are rejected. Declare the field
    separately. There is a worked example in
    [Web UI](components/web.md#the-strict-typescript-settings).

??? failure "A 502 right after `up`"
    Caddy comes up before the server finishes booting. It clears on its own in a
    second or two.

??? failure "Docker Desktop will not start on Windows"
    Enable virtualisation in the BIOS (**Intel VT-x** / **AMD-V**) and tick
    *Virtual Machine Platform* in Windows Features, then reboot.

??? failure "`Config file 'mkdocs.yml' does not exist.` from the Docker route"
    You are not in the repo root, so `${PWD}` mounted the wrong directory.

    ```bash
    cd /d/Work/Hakutaku     # or wherever you cloned it
    docker run --rm -p 5020:5020 -v "${PWD}:/docs"       squidfunk/mkdocs-material serve --dev-addr 0.0.0.0:5020
    ```

    Check you are in the right place first — `ls mkdocs.yml` should find it.

??? failure "`port is already allocated` on 5020"
    Something is already serving the docs — most likely an `mkdocs serve` you
    forgot about in another terminal. Open <http://localhost:5020> before
    starting a second one.

    To run anyway, map a different **host** port (the container side stays 5020,
    because that is what `dev_addr` binds):

    ```bash
    docker run --rm -p 5021:5020 -v "${PWD}:/docs"       squidfunk/mkdocs-material serve --dev-addr 0.0.0.0:5020
    ```

    Then open <http://localhost:5021>.
