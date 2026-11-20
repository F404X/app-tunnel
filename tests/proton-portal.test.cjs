const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { runInNewContext } = require('node:vm');

const scripts = join(__dirname, '../src/AppTunnel.Proton/Scripts');
const scan = readFileSync(join(scripts, 'read-wireguard.js'), 'utf8');
const create = readFileSync(join(scripts, 'create-wireguard.js'), 'utf8');
const origin = 'https://account.protonvpn.com';
const old = '[Interface]\n# Key for Old profile\nPrivateKey = *****\n[Peer]\n# BR-FREE#1';
const fresh = '[Interface]\n# Key for New profile\nPrivateKey = fixture\n[Peer]\n# NL-FREE#2';
const entry = configuration => ({
    querySelector: selector => selector.startsWith('summary') ? { textContent: 'Profile' } : { value: configuration },
});

function inspect(document, source = origin, pathname = '/u/0/downloads') {
    return JSON.parse(JSON.stringify(runInNewContext(scan, { document, location: { origin: source, pathname } })));
}
const dashboard = {
    getElementById: () => null,
    querySelectorAll: selector => selector === 'a[href]' ? [
        { origin: 'https://untrusted.example', pathname: '/downloads', href: 'https://untrusted.example/downloads' },
        { origin, pathname: '/u/0/downloads', href: origin + '/u/0/downloads' },
    ] : [],
};
assert.equal(inspect(dashboard, origin, '/u/0/dashboard').downloadsUrl, origin + '/u/0/downloads');
assert.deepEqual(inspect({}, 'https://account.protonvpn.com.attacker.example'), { ready: false, profiles: [] });
assert.deepEqual(inspect({ getElementById: () => null, querySelectorAll: () => [] }),
    { ready: false, profiles: [], downloadsUrl: null });

let moreClicks = 0;
const more = { dataset: {}, click: () => moreClicks++ };
const form = {
    previousElementSibling: { matches: () => true, querySelector: () => more },
    querySelectorAll: selector => selector === ':scope > h3' ? [1, 2, 3, 4] : [{ matches: () => false }],
};
const windowsRadio = { form, type: 'radio' };
const account = {
    getElementById: id => id === 'wg-platform-Windows' ? windowsRadio : { form },
    querySelectorAll: selector => selector.startsWith('details') ? [entry(old), entry(fresh)] : [{ value: fresh }],
};
const result = inspect(account);
assert.equal(result.ready, true);
assert.equal(result.profiles.length, 2);
assert.equal(result.profiles[0].configuration, old);
assert.equal(result.profiles[1].server, 'NL-FREE#2');
assert.equal(result.canCreate, true);
assert.equal(result.canChooseServer, true);
assert.equal(result.loadingMore, true);
inspect(account);
assert.equal(moreClicks, 1);

const actions = [];
let buttons = [];
let existing = [];
let dialogOpen = false;
const submitter = { matches: () => false };
const creationForm = {
    checkValidity: () => true,
    querySelectorAll: selector => selector === ':scope > h3' ? [null, null, null, {
        scrollIntoView: () => actions.push('choose'),
    }] : buttons,
    requestSubmit: button => {
        assert.equal(button, submitter);
        actions.push('submit');
    },
};
class HTMLInputElement {
    form = creationForm;
    entered = '';
    get value() { return this.entered; }
    set value(value) { this.entered = value; }
    dispatchEvent(event) { assert.equal(event.type, 'input'); }
}
const nameInput = new HTMLInputElement();
const platform = { form: creationForm, type: 'radio', checked: false, click() {
    actions.push('windows');
    this.checked = true;
} };
const createPage = {
    getElementById: id => id === 'wg-platform-Windows' ? platform : nameInput,
    querySelector: () => dialogOpen ? {} : null,
    querySelectorAll: () => existing,
};
function creation(action, name, url = origin + '/u/0/downloads') {
    const location = new URL(url);
    const run = runInNewContext(create, { document: createPage, location, HTMLInputElement, Event });
    return run(action, name);
}
const name = 'AppTunnel "casa" café';
assert.equal(creation('prepare', name, 'https://account.protonvpn.com.evil.example/downloads'), false);
assert.equal(creation('prepare', name, origin + '/login'), false);
assert.equal(creation('prepare', 'AppTunnel\n'), false);
assert.equal(creation('submit', name), false);
assert.equal(creation('prepare', name), true);
assert.equal(nameInput.value, name);
assert.equal(creation('choose', name), true);
assert.deepEqual(actions, ['windows', 'choose']);
assert.equal(creation('submit', name), false);
buttons = [submitter, submitter];
assert.equal(creation('submit', name), false);
buttons = [submitter];
dialogOpen = true;
assert.equal(creation('submit', name), false);
dialogOpen = false;
existing = [{ textContent: name.toUpperCase() }];
assert.equal(creation('submit', name), false);
existing = [];
assert.equal(creation('submit', 'a different profile'), false);
assert.equal(creation('submit', name), true);
assert.deepEqual(actions, ['windows', 'choose', 'submit']);
console.log('Proton portal fixtures passed: sign-in, existing profiles, paging, preparation and explicit creation.');
