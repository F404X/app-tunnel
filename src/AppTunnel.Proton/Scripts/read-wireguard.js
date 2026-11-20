(() => {
    if (!['https://account.protonvpn.com', 'https://account.proton.me'].includes(location.origin)) {
        return { ready: false, profiles: [] };
    }

    const form = document.getElementById('certificate-device-name');
    const entries = [...document.querySelectorAll('details[data-certificate-id]')].slice(0, 512);
    if (!form && entries.length === 0) {
        const link = [...document.querySelectorAll('a[href]')]
            .find(anchor => anchor.origin === location.origin && anchor.pathname !== location.pathname &&
                /^\/(?:u\/\d+\/)?(?:downloads|my-vpn\/download-apps)\/?$/.test(anchor.pathname));
        return { ready: false, profiles: [], downloadsUrl: link?.href || null };
    }

    function readProfile(area, label) {
        const config = area?.value;
        if (typeof config !== 'string' || config.length > 65536 ||
            !config.trimStart().startsWith('[Interface]') ||
            !/^\s*PrivateKey\s*=/m.test(config) || !/^\[Peer\]/m.test(config)) return null;
        const name = config.match(/^# Key for ([^\r\n]{1,120})/m)?.[1] || label || 'Proton VPN';
        const server = config.split(/^\[Peer\]\s*$/m)[1]?.trimStart().match(/^#\s*([^\r\n]{1,60})/)?.[1];
        return { name: name.trim(), server: server?.trim() || null, configuration: config };
    }

    const profiles = entries.map(entry => {
        const name = entry.querySelector('summary .text-ellipsis')?.textContent?.trim();
        return readProfile(entry.querySelector('textarea[readonly]'), name) ||
            { name: name || 'Proton VPN', server: null, configuration: null };
    });

    for (const area of document.querySelectorAll('[role="dialog"] textarea[readonly]')) {
        const profile = readProfile(area, null);
        if (profile && !profiles.some(entry => entry.configuration === profile.configuration)) profiles.push(profile);
    }
    // The official catalog places its read-only paging control immediately before the creation form.
    const paging = form?.closest('form')?.previousElementSibling;
    const more = paging?.matches('[aria-busy="true"]') && paging.querySelector(':scope > button[type=button][title]');
    if (more && entries.length < 512 && !more.disabled && more.dataset.appTunnelCount !== String(entries.length)) {
        more.dataset.appTunnelCount = String(entries.length);
        more.click();
    }
    return { ready: true, profiles };
})()
