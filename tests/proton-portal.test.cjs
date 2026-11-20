const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { runInNewContext } = require('node:vm');

const scripts = join(__dirname, '../src/AppTunnel.Proton/Scripts');
const scan = readFileSync(join(scripts, 'read-wireguard.js'), 'utf8');
const create = readFileSync(join(scripts, 'open-create.js'), 'utf8');
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
const form = { closest: () => ({ previousElementSibling: { matches: () => true, querySelector: () => more } }) };
const account = {
    getElementById: () => form,
    querySelectorAll: selector => selector.startsWith('details') ? [entry(old), entry(fresh)] : [{ value: fresh }],
};
const result = inspect(account);
assert.equal(result.ready, true);
assert.equal(result.profiles.length, 2);
assert.equal(result.profiles[0].configuration, old);
assert.equal(result.profiles[1].server, 'NL-FREE#2');
inspect(account);
assert.equal(moreClicks, 1);

const actions = [];
const createPage = {
    getElementById(id) {
        if (id === 'wg-platform-Windows') return { click: () => actions.push('Windows') };
        return { scrollIntoView: () => actions.push('scroll'), focus: () => actions.push('focus') };
    },
};
assert.equal(runInNewContext(create, { document: createPage, location: { origin } }), true);
assert.deepEqual(actions, ['Windows', 'scroll', 'focus']);
console.log('Proton portal fixtures passed: sign-in, old keys, new profiles, paging and creation.');
