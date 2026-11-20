(function (action, name) {
    if (!['https://account.protonvpn.com', 'https://account.proton.me'].includes(location.origin) ||
        !/^\/(?:u\/\d+\/)?(?:downloads|my-vpn\/download-apps)\/?$/.test(location.pathname) ||
        typeof name !== 'string' || !name || name.length > 100 || /[\u0000-\u001f\u007f-\u009f]/.test(name)) return false;

    const field = document.getElementById('certificate-device-name');
    const form = field?.form;
    const windows = document.getElementById('wg-platform-Windows');
    if (!form || field.disabled || windows?.form !== form || windows.type !== 'radio' || windows.disabled) return false;

    if (action === 'prepare') {
        if (!windows.checked) windows.click();
        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(field, name);
        field.dispatchEvent(new Event('input', { bubbles: true }));
        return windows.checked && field.value === name;
    }
    if (!windows.checked || field.value !== name) return false;

    if (action === 'choose') {
        const headings = form.querySelectorAll(':scope > h3');
        if (headings.length < 4) return false;
        headings[3].scrollIntoView({ block: 'start' });
        return true;
    }

    if (action !== 'submit' || document.querySelector('[role="dialog"]') || !form.checkValidity()) return false;
    const buttons = form.querySelectorAll('button[type=submit]');
    if (buttons.length !== 1 || buttons[0].matches(':disabled,[aria-disabled="true"],[aria-busy="true"]')) return false;
    const existing = [...document.querySelectorAll('details[data-certificate-id] summary .text-ellipsis')];
    if (existing.some(entry => entry.textContent.trim().toLowerCase() === name.toLowerCase())) return false;

    // The official form owns the authenticated request and key generation.
    form.requestSubmit(buttons[0]);
    return true;
})
