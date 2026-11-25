<!-- Notes for the next stable or beta release. Edit this file in the release commit; nightly builds list their
     commits automatically. -->
AppTunnel 1.0.0 beta 3 fixes a leftover WireSock service that blocked every new connection. Below it, the fixes from beta 2, what is new since 0.13.2 and everything AppTunnel does.

### Fixed in 1.0.0 beta 3

- **"A different WireSock service is installed" no longer blocks connecting forever:** when a failed connection attempt was rolled back, AppTunnel could remove its session files and record while the WireSock service it had installed stayed registered. AppTunnel then treated its own service as another app's and refused every connection. AppTunnel now removes such a leftover by itself when you connect, disconnect or update WireSock: only a stopped service that runs WireSock from Program Files, points to an AppTunnel session file that no longer exists and that no AppTunnel session of any Windows user still uses.
- **Rollback no longer leaves the service behind:** a failed route is matched to its service by its session file, so the service is removed together with its files even when WireSock registered the executable path in a different form.

### Fixed in 1.0.0 beta 2

- **Connecting no longer fails on every route when the handshake cannot be read:** in beta 1, Automatic protocol and server switching removed each route whose handshake did not show up in the WireSock log within 12 seconds, so a working tunnel could end in "Could not connect using the available routes" with "Handshake status is unavailable" for every route. A route is now rejected only when WireSock reports an error or sends a handshake that is never answered. When there is no handshake record at all, the first connection is kept with a note in the log, and switching to another route still requires a confirmed handshake before leaving the current one.
- **WireSock logs are found in more installs:** the handshake status now follows every log file in the WireSock Secure Connect and WireSock VPN Client log folders instead of one fixed file name, and a log created after connecting is read from its start.
- **A late handshake gets its full wait:** a handshake sent near the end of the check now has its own 12 seconds to be answered before the route counts as failed.

### New in 1.0.0

- **Modern menus and lists:** the menus and dropdown lists (language, Add app, More options, the server menu, Protocol, update channel, theme, app restart, text fields and the tray) has roomier rows, an icon on each option, a rounded highlight, the chosen option marked with a check and rounded corners on Windows 11. Selection fields show the chosen option's icon too.
- **Browsers in Explore common apps:** Google Chrome, Microsoft Edge, Brave, Firefox, Opera, Opera GX, Vivaldi, Yandex Browser, Chromium, LibreWolf and Waterfox are detected and can be sent through the VPN like any other app. The setup assistant still leaves the browser direct.
- **What changed, before and after updating:** before installing an update you see a short list of its changes, and after updating, the first time you open AppTunnel, the complete notes of what is new. Read more shows the full update notes before installing and the entire changelog after.
- **Version always visible:** the window title and the bottom of Settings show the installed AppTunnel version and its update channel.

### Everything in AppTunnel

- **Split tunneling per app:** only the apps you switch on go through Proton VPN, TCP and UDP; everything else stays on your normal connection. Add apps from a running one, an .exe, a whole folder or by dragging them in, or pick them in Explore common apps (Discord, PTB, Canary, Serein, Telegram, Signal, WhatsApp, Element, Steam, Spotify, Viber, Zoom, other Discord clients and the browsers above).
- **Proton servers without a browser:** Choose route lists the servers your Proton account can use, with country, city, load and ping, and creates the WireGuard connection in one click. Proton account profiles can be imported, created in the Proton window or added from a .conf file.
- **Automatic server:** connects to the fastest saved server, measures latency in the background and moves to a faster one when the current route stays slow or fails, keeping the new route only after a confirmed handshake.
- **Protocols for filtered networks:** WireGuard, WireGuard with padding, AmneziaWG, the profile's own protocol, or Automatic, which tries several protocol and endpoint combinations.
- **Fine control of destinations:** IPs and ranges always through the VPN or never through it, local network kept direct, the profile's DNS on request, IPv6 when the profile supports it and a firewall guard that blocks public IPv6 for routed apps when the tunnel has no IPv6 route.
- **Changes apply by themselves:** while connected, changes to apps, protocol or filters apply about 1.5 seconds after the last one, or by hand with Apply changes. Switching an app off or removing it can be undone.
- **Discord restarts when needed:** Discord, PTB and Canary can be restarted after joining the route so calls and streams use the VPN; ask every time, always or never.
- **Remembers your connection:** if the VPN was on when you last used AppTunnel, it reconnects on the next start, including after restarting Windows.
- **Home, Servers and Diagnostics:** a connect button with the server and handshake status, flags for every saved server, ping tests, and diagnostics that tell a missing handshake from endpoint and route errors.
- **Tray and Windows startup:** closing the window keeps AppTunnel in the tray with quick actions; Start with Windows opens it in the tray at sign-in. The VPN service keeps running if you exit the interface.
- **First-time setup assistant:** checks WireSock and WebView2, signs you in to Proton, picks your apps and connects. It can be run again from Settings.
- **Updates:** Check for updates downloads and verifies the installer from GitHub; automatic updates check daily and install in the background. Choose Stable, Beta or Nightly as the update channel. Update WireSock installs the latest WireSock through WinGet.
- **Looks and language:** light, dark or follow Windows; English, Portuguese and Russian.
- **Privacy you control:** anonymous statistics, automatic error reports and detailed diagnostics are separate opt-in choices, off by default, with a preview of exactly what is sent. VPN profiles are encrypted with Windows DPAPI.
- **Installer and portable edition:** the installer includes .NET and can install WireSock and WebView2; a portable ZIP is also available.

**Updating from 0.13.0 or 0.13.1:** if Check for updates stops at the "Install now?" question, download **AppTunnel-Setup-x64.exe** below and run it once. Automatic updates are not affected.
