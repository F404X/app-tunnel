(() => {
    if (!['https://account.protonvpn.com', 'https://account.proton.me'].includes(location.origin)) return false;
    const field = document.getElementById('certificate-device-name');
    if (!field) return false;
    document.getElementById('wg-platform-Windows')?.click();
    field.scrollIntoView({ block: 'center' });
    field.focus({ preventScroll: true });
    return true;
})()
