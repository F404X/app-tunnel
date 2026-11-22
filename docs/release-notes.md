<!-- Notes for the next stable or beta release. Edit this file in the release commit; nightly builds list their
     commits automatically. -->
**Updating from 0.13.0 or 0.13.1:** in those versions **Check for updates** downloads the update but fails at the "Install now?" question with *"Invisible or disabled control cannot be activated"*. Automatic updates are not affected. If the button fails for you, download **AppTunnel-Setup-x64.exe** below and run it once; updates work normally from 0.13.2 on.

- **Updates install again:** the "Install now?" confirmation opens correctly, and if it ever cannot open, the installer's own setup wizard asks instead, so a dialog can no longer block an update.
- **Remembers your connection:** if the VPN was on when you last used AppTunnel, it reconnects on the next start, including after restarting Windows. Your apps and server choice were already kept.
- **Servers page:** only the pinned servers scroll. Automatic, **Choose route**, **More options** and the Proton account line always stay visible, and an open **Advanced** section scrolls on its own.
- **Flags follow the country, not the name:** each saved server keeps its country, so renaming it no longer changes its flag. Servers saved earlier get their country once, automatically.
- **Update WireSock works:** it compares your installed WireSock with the latest one on WinGet and installs it when newer. Before, WinGet never recognised the installed copy, so nothing was ever updated.
- **Settings:** more space between **Update channel** and the update buttons.
