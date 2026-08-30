let _conditionalAbortController = null;
let _restartConditional = null;

export function initLoginForm(formEl) {
    if (!formEl) return;

    const base = formEl.dataset.pwlBase || '/auth';
    const getEmail = () => formEl.querySelector('#pwl-email')?.value?.trim() ?? '';
    const getReturnUrl = () => formEl.dataset.returnUrl ?? '';
    const msgEl = formEl.querySelector('#pwl-message');

    function showMessage(text, isError) {
        if (!msgEl) return;
        msgEl.textContent = text;
        msgEl.style.color = isError ? '#c00' : '#080';
    }

    function antiforgeryToken(scopeEl) {
        return (scopeEl ?? formEl).querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    }

    async function postJson(url, body, scopeEl) {
        return fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': antiforgeryToken(scopeEl),
            },
            body: JSON.stringify(body),
        });
    }

    // Magic link
    formEl.querySelector('#pwl-btn-magic-link')?.addEventListener('click', async function () {
        const email = getEmail();
        if (!email) { showMessage('Please enter your email address.', true); return; }
        showMessage('');
        this.disabled = true;
        try {
            const resp = await postJson(`${base}/magic-link/request`, { email, returnUrl: getReturnUrl() });
            showMessage(resp.ok ? 'Check your email for a sign-in link.' : 'Something went wrong. Please try again.', !resp.ok);
        } catch {
            showMessage('Network error. Please try again.', true);
        } finally {
            this.disabled = false;
        }
    });

    // OTP request — section lives outside the login form to avoid invalid nested <form> elements
    const otpSection = document.getElementById('pwl-otp-section');
    formEl.querySelector('#pwl-btn-otp')?.addEventListener('click', async function () {
        const email = getEmail();
        if (!email) { showMessage('Please enter your email address.', true); return; }
        showMessage('');
        this.disabled = true;
        try {
            const resp = await postJson(`${base}/otp/request`, { email, returnUrl: getReturnUrl() });
            if (resp.ok) {
                if (otpSection) {
                    otpSection.style.display = '';
                    const otpForm = otpSection.querySelector('#pwl-otp-form');
                    if (otpForm) otpForm.style.display = '';
                }
                showMessage('A one-time code has been sent to your email.');
            } else {
                showMessage('Something went wrong. Please try again.', true);
            }
        } catch {
            showMessage('Network error. Please try again.', true);
        } finally {
            this.disabled = false;
        }
    });

    // OTP verify
    otpSection?.querySelector('#pwl-otp-form')?.addEventListener('submit', async function (e) {
        e.preventDefault();
        const code = this.querySelector('#pwl-otp-code')?.value?.trim() ?? '';
        const otpMsgEl = this.querySelector('#pwl-otp-message');
        const showOtpMsg = (text, isError) => {
            if (!otpMsgEl) return;
            otpMsgEl.textContent = text;
            otpMsgEl.style.color = isError ? '#c00' : '#080';
        };
        if (!code) { showOtpMsg('Please enter the code.', true); return; }
        const submitBtn = this.querySelector('[type="submit"]');
        if (submitBtn) submitBtn.disabled = true;
        try {
            const resp = await postJson(`${base}/otp/verify`,
                { email: getEmail(), code, returnUrl: getReturnUrl() }, this);
            const data = await resp.json();
            if (data.success) {
                window.location.href = data.redirectTo || getReturnUrl() || '/';
            } else {
                showOtpMsg(data.error || 'Invalid code. Please try again.', true);
            }
        } catch {
            showOtpMsg('Network error. Please try again.', true);
        } finally {
            if (submitBtn) submitBtn.disabled = false;
        }
    });

    // Passkey / WebAuthn
    let passkeyAbortController = null;
    formEl.querySelector('#pwl-btn-passkey')?.addEventListener('click', async function () {
        if (!window.PublicKeyCredential) {
            showMessage('Your browser does not support passkeys.', true);
            return;
        }
        // Abort the background conditional (autofill) request and any previous modal request
        _conditionalAbortController?.abort();
        _conditionalAbortController = null;
        passkeyAbortController?.abort();
        passkeyAbortController = new AbortController();
        showMessage('');
        this.disabled = true;
        try {
            const optResp = await fetch(`${base}/webauthn/signin/options`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email: getEmail() || null }),
            });
            if (!optResp.ok) {
                showMessage('Could not start passkey sign-in.', true); return;
            }
            const { ceremonyId, options } = await optResp.json();
            const publicKey = {
                ...options,
                challenge: b64uToBuffer(options.challenge),
                allowCredentials: (options.allowCredentials ?? []).map(c => ({
                    ...c,
                    id: b64uToBuffer(c.id),
                })),
            };

            const credential = await navigator.credentials.get({ publicKey, signal: passkeyAbortController.signal });
            if (!credential) { showMessage('Passkey sign-in was cancelled.', true); return; }

            const completeResp = await fetch(`${base}/webauthn/signin/complete`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    ceremonyId,
                    assertion: {
                        id: bufferToB64u(credential.rawId),
                        rawId: bufferToB64u(credential.rawId),
                        type: credential.type,
                        response: {
                            authenticatorData: bufferToB64u(credential.response.authenticatorData),
                            clientDataJson: bufferToB64u(credential.response.clientDataJSON),
                            signature: bufferToB64u(credential.response.signature),
                            userHandle: credential.response.userHandle
                                ? bufferToB64u(credential.response.userHandle)
                                : null,
                        },
                        extensions: credential.getClientExtensionResults?.() ?? {},
                    },
                }),
            });

            if (completeResp.ok) {
                window.location.href = getReturnUrl() || '/';
            } else {
                showMessage('Passkey verification failed. Please try again.', true);
            }
        } catch (err) {
            if (err.name === 'AbortError') return;
            showMessage(
                err.name === 'NotAllowedError'
                    ? 'Passkey sign-in was cancelled.'
                    : 'Passkey sign-in failed. Please try another method.',
                true);
        } finally {
            this.disabled = false;
            passkeyAbortController = null;
            _restartConditional?.();
        }
    });
}

export function initConditionalUi(containerEl) {
    if (!containerEl) return;
    if (!window.PublicKeyCredential) return;

    const base = containerEl.dataset.pwlBase || '/auth';
    const getReturnUrl = () => containerEl.dataset.returnUrl ?? '';
    const btn = containerEl.querySelector('#pwl-conditional-passkey');

    if (btn) btn.style.display = 'block';

    async function signIn(mediation, signal) {
        const optResp = await fetch(`${base}/webauthn/signin/options`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ email: null }),
        });
        if (!optResp.ok) return false;

        const { ceremonyId, options } = await optResp.json();
        const publicKey = {
            ...options,
            challenge: b64uToBuffer(options.challenge),
            allowCredentials: [],
        };

        const credential = await navigator.credentials.get(
            mediation ? { publicKey, mediation, signal } : { publicKey }
        );
        if (!credential) return false;

        const completeResp = await fetch(`${base}/webauthn/signin/complete`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                ceremonyId,
                assertion: {
                    id: bufferToB64u(credential.rawId),
                    rawId: bufferToB64u(credential.rawId),
                    type: credential.type,
                    response: {
                        authenticatorData: bufferToB64u(credential.response.authenticatorData),
                        clientDataJson: bufferToB64u(credential.response.clientDataJSON),
                        signature: bufferToB64u(credential.response.signature),
                        userHandle: credential.response.userHandle
                            ? bufferToB64u(credential.response.userHandle)
                            : null,
                    },
                    extensions: credential.getClientExtensionResults?.() ?? {},
                },
            }),
        });

        if (completeResp.ok) {
            window.location.href = getReturnUrl() || '/';
            return true;
        }
        return false;
    }

    btn?.addEventListener('click', async function () {
        _conditionalAbortController?.abort();
        _conditionalAbortController = null;
        this.disabled = true;
        try {
            await signIn();
        } catch {
            // user cancelled
        } finally {
            this.disabled = false;
            _restartConditional?.();
        }
    });

    if (typeof PublicKeyCredential.isConditionalMediationAvailable === 'function') {
        PublicKeyCredential.isConditionalMediationAvailable()
            .then(available => {
                if (!available) return;
                _restartConditional = () => {
                    _conditionalAbortController = new AbortController();
                    signIn('conditional', _conditionalAbortController.signal).catch(() => {});
                };
                _restartConditional();
            });
    }
}

export function initPasskeyRegister(containerEl) {
    if (!containerEl) return;

    const base = containerEl.dataset.pwlBase || '/auth';
    const msgEl = containerEl.querySelector('#pwl-register-message');
    const btn = containerEl.querySelector('#pwl-btn-register-passkey');

    function antiforgeryToken() {
        return containerEl.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    }

    function showMessage(text, isError) {
        if (!msgEl) return;
        msgEl.textContent = text;
        msgEl.style.color = isError ? '#c00' : '#080';
    }

    btn?.addEventListener('click', async function () {
        if (!window.PublicKeyCredential) {
            showMessage('Your browser does not support passkeys.', true);
            return;
        }
        const nickname = containerEl.querySelector('#pwl-passkey-nickname')?.value?.trim() || null;
        this.disabled = true;
        showMessage('');
        try {
            const optResp = await fetch(`${base}/webauthn/register/options`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': antiforgeryToken(),
                },
                body: JSON.stringify({ nickname }),
            });
            if (!optResp.ok) { showMessage('Could not start passkey registration.', true); return; }

            const { ceremonyId, options } = await optResp.json();
            const publicKey = {
                ...options,
                challenge: b64uToBuffer(options.challenge),
                user: { ...options.user, id: b64uToBuffer(options.user.id) },
                excludeCredentials: (options.excludeCredentials ?? []).map(c => ({
                    ...c,
                    id: b64uToBuffer(c.id),
                })),
            };

            const credential = await navigator.credentials.create({ publicKey });
            if (!credential) { showMessage('Passkey creation was cancelled.', true); return; }

            const completeResp = await fetch(`${base}/webauthn/register/complete`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': antiforgeryToken(),
                },
                body: JSON.stringify({
                    ceremonyId,
                    attestation: {
                        id: bufferToB64u(credential.rawId),
                        rawId: bufferToB64u(credential.rawId),
                        type: credential.type,
                        response: {
                            attestationObject: bufferToB64u(credential.response.attestationObject),
                            clientDataJSON: bufferToB64u(credential.response.clientDataJSON),
                        },
                        extensions: credential.getClientExtensionResults?.() ?? {},
                    },
                }),
            });

            if (completeResp.ok) {
                showMessage('Passkey added successfully.');
                const nicknameEl = containerEl.querySelector('#pwl-passkey-nickname');
                if (nicknameEl) nicknameEl.value = '';
                containerEl.dispatchEvent(new CustomEvent('pwl:passkey-registered'));
            } else {
                const data = await completeResp.json().catch(() => ({}));
                showMessage(
                    data.error === 'attestation_failed'
                        ? 'Passkey registration failed. Please try again.'
                        : 'Something went wrong. Please try again.',
                    true);
            }
        } catch (err) {
            showMessage(
                err.name === 'NotAllowedError'
                    ? 'Passkey creation was cancelled.'
                    : 'Passkey registration failed.',
                true);
        } finally {
            this.disabled = false;
        }
    });
}

export function initCredentialList(containerEl) {
    if (!containerEl) return;

    const base = containerEl.dataset.pwlBase || '/auth';
    const ulEl = containerEl.querySelector('#pwl-creds-ul');
    const loadingEl = containerEl.querySelector('#pwl-creds-loading');
    const msgEl = containerEl.querySelector('#pwl-creds-message');

    function antiforgeryToken() {
        return containerEl.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    }

    function showMessage(text, isError) {
        if (!msgEl) return;
        msgEl.textContent = text;
        msgEl.style.color = isError ? '#c00' : '#080';
    }

    function renderCredential(c) {
        const li = document.createElement('li');
        li.dataset.id = c.id;
        li.style.cssText = 'display:flex;align-items:center;gap:8px;padding:6px 0;border-bottom:1px solid #eee;';

        const label = document.createElement('span');
        label.style.flex = '1';
        label.textContent = c.nickname || 'Unnamed passkey';
        if (c.lastUsedUtc) {
            const sub = document.createElement('small');
            sub.style.cssText = 'display:block;color:#666;';
            sub.textContent = `Last used: ${new Date(c.lastUsedUtc).toLocaleDateString()}`;
            label.appendChild(sub);
        }

        const delBtn = document.createElement('button');
        delBtn.type = 'button';
        delBtn.textContent = 'Remove';
        delBtn.addEventListener('click', async () => {
            if (!confirm(`Remove passkey "${c.nickname || 'Unnamed passkey'}"?`)) return;
            delBtn.disabled = true;
            const r = await fetch(`${base}/webauthn/credentials/${c.id}`, {
                method: 'DELETE',
                headers: { 'RequestVerificationToken': antiforgeryToken() },
            });
            if (r.ok) {
                li.remove();
                if (ulEl && ulEl.children.length === 0)
                    ulEl.innerHTML = '<li>No passkeys registered.</li>';
            } else if (r.status === 409) {
                showMessage('Cannot remove the last sign-in method.', true);
                delBtn.disabled = false;
            } else {
                showMessage('Could not remove passkey.', true);
                delBtn.disabled = false;
            }
        });

        li.appendChild(label);
        li.appendChild(delBtn);
        return li;
    }

    async function load() {
        if (loadingEl) loadingEl.style.display = '';
        if (ulEl) ulEl.innerHTML = '';
        showMessage('');

        const resp = await fetch(`${base}/webauthn/credentials`);
        if (loadingEl) loadingEl.style.display = 'none';
        if (!resp.ok) { showMessage('Could not load passkeys.', true); return; }

        const creds = await resp.json();
        if (!ulEl) return;
        if (creds.length === 0) {
            ulEl.innerHTML = '<li>No passkeys registered.</li>';
            return;
        }
        for (const c of creds) ulEl.appendChild(renderCredential(c));
    }

    load();
    containerEl.addEventListener('pwl:passkey-registered', load);
}

// Standalone magic-link-only form.
// Expected structure: a <form> containing [name="email"], #pwl-btn-magic-link, #pwl-message.
// data-return-url and data-pwl-base are read from the form element.
export function initMagicLinkForm(formEl) {
    if (!formEl) return;

    const base = formEl.dataset.pwlBase || '/auth';
    const getEmail = () => formEl.querySelector('[name="email"]')?.value?.trim() ?? '';
    const getReturnUrl = () => formEl.dataset.returnUrl ?? '';
    const msgEl = formEl.querySelector('#pwl-message');

    function showMessage(text, isError) {
        if (!msgEl) return;
        msgEl.textContent = text;
        msgEl.style.color = isError ? '#c00' : '#080';
    }

    formEl.querySelector('#pwl-btn-magic-link')?.addEventListener('click', async function () {
        const email = getEmail();
        if (!email) { showMessage('Please enter your email address.', true); return; }
        showMessage('');
        this.disabled = true;
        try {
            const token = formEl.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
            const resp = await fetch(`${base}/magic-link/request`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                body: JSON.stringify({ email, returnUrl: getReturnUrl() }),
            });
            showMessage(resp.ok ? 'Check your email for a sign-in link.' : 'Something went wrong. Please try again.', !resp.ok);
        } catch {
            showMessage('Network error. Please try again.', true);
        } finally {
            this.disabled = false;
        }
    });
}

// Standalone OTP-only login flow.
// Expected structure: a container <div> with data-return-url, data-pwl-base containing:
//   - a <form> with [name="email"], #pwl-btn-otp, #pwl-otp-request-msg
//   - #pwl-otp-section (hidden) containing OtpForm partial (#pwl-otp-form, #pwl-otp-code, #pwl-otp-message)
export function initOtpLoginForm(containerEl) {
    if (!containerEl) return;

    const base = containerEl.dataset.pwlBase || '/auth';
    const getEmail = () => containerEl.querySelector('[name="email"]')?.value?.trim() ?? '';
    const getReturnUrl = () => containerEl.dataset.returnUrl ?? '';
    const requestForm = containerEl.querySelector('form');
    const otpSection = containerEl.querySelector('#pwl-otp-section');
    const requestMsgEl = containerEl.querySelector('#pwl-otp-request-msg');

    function showRequestMsg(text, isError) {
        if (!requestMsgEl) return;
        requestMsgEl.textContent = text;
        requestMsgEl.style.color = isError ? '#c00' : '#080';
    }

    containerEl.querySelector('#pwl-btn-otp')?.addEventListener('click', async function () {
        const email = getEmail();
        if (!email) { showRequestMsg('Please enter your email address.', true); return; }
        showRequestMsg('');
        this.disabled = true;
        try {
            const token = requestForm?.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
            const resp = await fetch(`${base}/otp/request`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                body: JSON.stringify({ email, returnUrl: getReturnUrl() }),
            });
            if (resp.ok) {
                if (otpSection) {
                    otpSection.style.display = '';
                    const otpForm = otpSection.querySelector('#pwl-otp-form');
                    if (otpForm) otpForm.style.display = '';
                }
                showRequestMsg('A one-time code has been sent to your email.');
            } else {
                showRequestMsg('Something went wrong. Please try again.', true);
            }
        } catch {
            showRequestMsg('Network error. Please try again.', true);
        } finally {
            this.disabled = false;
        }
    });

    otpSection?.querySelector('#pwl-otp-form')?.addEventListener('submit', async function (e) {
        e.preventDefault();
        const code = this.querySelector('#pwl-otp-code')?.value?.trim() ?? '';
        const otpMsgEl = this.querySelector('#pwl-otp-message');
        const showOtpMsg = (text, isError) => {
            if (!otpMsgEl) return;
            otpMsgEl.textContent = text;
            otpMsgEl.style.color = isError ? '#c00' : '#080';
        };
        if (!code) { showOtpMsg('Please enter the code.', true); return; }
        const submitBtn = this.querySelector('[type="submit"]');
        if (submitBtn) submitBtn.disabled = true;
        try {
            const token = this.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
            const resp = await fetch(`${base}/otp/verify`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                body: JSON.stringify({ email: getEmail(), code, returnUrl: getReturnUrl() }),
            });
            const data = await resp.json();
            if (data.success) {
                window.location.href = data.redirectTo || getReturnUrl() || '/';
            } else {
                showOtpMsg(data.error || 'Invalid code. Please try again.', true);
            }
        } catch {
            showOtpMsg('Network error. Please try again.', true);
        } finally {
            if (submitBtn) submitBtn.disabled = false;
        }
    });
}

function bufferToB64u(buf) {
    const bytes = new Uint8Array(buf);
    let s = '';
    for (const b of bytes) s += String.fromCharCode(b);
    return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
}

function b64uToBuffer(b64u) {
    const b64 = b64u.replace(/-/g, '+').replace(/_/g, '/');
    const bin = atob(b64);
    const buf = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) buf[i] = bin.charCodeAt(i);
    return buf.buffer;
}
