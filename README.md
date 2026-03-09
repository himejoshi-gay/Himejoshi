<p align="center">
  <img src="./.github/Himejoshi.png">
</p>

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![GitHub stars](https://img.shields.io/github/stars/himejoshi-gay/Himejoshi.svg?style=social&label=Star)](https://github.com/himejoshi-gay/Himejoshi)

Himejoshi is a private server for osu! written in C# & based on sunrise. This repository has endpoints both for client and the
website.


> [!NOTE]
> Want to help? Have a question? :shipit: Feel free to join our [Discord server](https://discord.gg/himejoshi), and ask any of our respective maintainers.


## 🖼️ Preview

![](https://github.com/himejoshi-gay/Apollo/blob/main/.github/preview.jpg)

-----


## Features 🌟

### Core features

- [x] Login and registration system
- [x] Score submission and leaderboards
- [x] Chat implementation
- [x] Chat Bot (as a replacement for Bancho Bot)
- [x] Multiplayer
- [x] !mp commands (mostly)
- [x] Server website (located at [Moonlight](https://github.com/himejoshi-gay/Moonlight))
- [x] Support for non-standard gamemodes (e.g. Relax, Autopilot, ScoreV2)
- [x] Custom beatmap status system
- [x] osu!Direct
- [x] Spectating
- [x] Beatmap hype system
- [x] Achievements (Medals)
- [x] Rank snapshots
- [x] Ability to upload custom server backgrounds

### Additional features

- [x] Automated tests (unit and integration)
- [x] Telemetry system with Prometheus, Loki and Tempo
- [x] Rate limiter for both internal and external requests
- [x] Redis caching for faster response times
- [x] Docker support
- [x] Database migrations
- [x] Database backups

## Installation Using Apollo (Docker, highly recommended) 🚀

If you are planning to host your own instance of Himejoshi, please highly consider using **[Apollo](https://github.com/himejoshi-gay/Apollo)**. In short it keeps everything together easily.

![Apollo](./.github/prefer_apollo.png)

It includes all the required services for a fully functional server out of the box. As a bonus, it also includes **a website** and **a Discord bot**!

**This is the recommended way to set up your server without the need to manually set up each service.**

If you are looking for the official documentation, please refer to [docs.himejoshi.gay](https://docs.himejoshi.gay).


## Standalone installation with self-signed certificate (Docker) 🐳

1. Clone the repository
2. Open the project's folder in any CLI environment.
3. Set up production environment
   - Create the file `Himejoshi.Server/appsettings.Production.json` and fill it following the `Himejoshi.Server/appsettings.Production.json.example` example.

     ```bash
     cp Himejoshi.Server/appsettings.Production.json.example Himejoshi.Server/appsettings.Production.json
     ```

   - Set the environment variables in the `.env` file.

     ```bash
     cp .env.example .env
     ```

> [!WARNING]
> Make sure to update `WEB_DOMAIN` and `API_TOKEN_SECRET` values!

4. Set up the beatmap manager by following the instructions in
   the [Observatory](https://github.com/himejoshi-gay/Observatory). After setting up the beatmap manager,
   you need to set the `General:ObservatoryUrl` in the `Himejoshi.Server/appsettings.Production.json` file to the address of the beatmap manager.
   - **NB:** Make sure that the PORT is defined properly (himejoshi checks port 3333 by default) and POSTGRES_PORT value doesn't conflict with other PC ports.
5. ⚠️ **Please create a `himejoshi.pfx` file and move it to the `Himejoshi/himejoshi.pfx` folder, for more instructions follow** [Local connection ⚙️](##local-connection).
6. Start the server by running:
   ```bash
   docker compose -f docker-compose.yml up -d
   ```
7. (Optional) If you want to connect to the server locally, please refer to
   the [Local connection ⚙️](##local-connection)
   section.

## Development environment ⚒️

1. Clone the repository
2. Open the project's folder in any CLI environment.
3. Set up development environment by running:
   ```bash
   docker compose -f docker-compose.dev.yml up -d
   ```
4. Set up the beatmap manager by following the instructions in
   the [Observatory repository](https://github.com/himejoshi-gay/Observatory). After setting up the beatmap manager,
   you need to set the `General:ObservatoryUrl` in the `Himejoshi.Server/appsettings.{Your Environment}.json` file to the address of the beatmap manager.
   - **NB:** Make sure that the PORT is defined properly (himejoshi checks port 3333 by default) and POSTGRES_PORT value doesn't conflict with other PC ports.
5. ⚠️ **Please create a `himejoshi.pfx` file and move it to the `Himejoshi/himejoshi.pfx` folder, for more instructions follow** [Local connection ⚙️](##local-connection).
6. Run the project
7. (Optional) If you want to connect to the server locally, please refer to
   the [Local connection ⚙️](##local-connection)
   section.

### Local connection ⚙️

#### If you want to connect to the server locally, follow these steps:

1. Add a launch argument `-devserver himejoshi.local` to your osu! shortcut.
2. Open the `hosts` file located in `C:\Windows\System32\drivers\etc\hosts` (C:\ is your system drive) with a text
   editor and add the following line:

   ```hosts
   ... (rest of the file)

   # Himejoshi Web Section
   127.0.0.1 himejoshi.local
   127.0.0.1 api.himejoshi.local
   # Himejoshi osu! Section
   127.0.0.1 osu.himejoshi.local
   127.0.0.1 a.himejoshi.local
   127.0.0.1 c.himejoshi.local
   127.0.0.1 assets.himejoshi.local
   127.0.0.1 cho.himejoshi.local
   127.0.0.1 c4.himejoshi.local
   127.0.0.1 b.himejoshi.local
   ```

> [!WARNING]
> Don't forget to save the file after editing.

3. Generate a self-signed certificate for the domain `himejoshi.local` by running the following commands in the terminal:

   ```bash
   openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes -keyout himejoshi.local.key -out himejoshi.local.crt -subj "/CN=himejoshi.local" -addext "subjectAltName=DNS:himejoshi.local,DNS:*.himejoshi.local,IP:10.0.0.1"
   ```

4. Convert the certificate to the PKCS12 format (for ASP.Net) by running the following command in the terminal:

   ```bash
   openssl pkcs12 -export -out himejoshi.pfx -inkey himejoshi.local.key -in himejoshi.local.crt -password pass:password
   ```

5. Import the certificate to the Trusted Root Certification Authorities store by running the following command in the
   terminal:

   ```bash
   certutil -addstore -f "ROOT" himejoshi.local.crt
   ```

6. Move the generated `himejoshi.pfx` file to the `Himejoshi` directory.

7. Run the server and navigate to `https://api.himejoshi.local/docs` to check if the server is running.

## Dependencies 📦

- [Observatory (beatmap manager)](https://github.com/himejoshi-gay/Observatory)

## Contributing 💖

If you want to contribute to the project, feel free to fork the repository and submit a pull request. We are open to any
suggestions and improvements.
