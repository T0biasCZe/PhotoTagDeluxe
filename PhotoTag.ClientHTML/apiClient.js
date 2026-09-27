// apiClient.js
// Centralized API functions for PhotoTagDeluxe

async function fetchManufacturers() {
  try {
    const resp = await fetch('/api/manufacturers?format=json');
    const data = await resp.json();
    return data.manufacturer || [];
  } catch (err) {
    console.error('[apiClient] fetchManufacturers error:', err);
    return [];
  }
}

async function fetchFilmStocks(manufacturerId) {
  const manufacturers = await fetchManufacturers();
  const m = manufacturers.find(x => x.id === manufacturerId);
  if (!m || !m.filmStocks || !Array.isArray(m.filmStocks.filmStock)) return [];
  return m.filmStocks.filmStock;
}

// Add more API functions as needed

async function encryptMEK(mekBytes, password) {
  const enc = new TextEncoder();
  const passwordKey = await crypto.subtle.importKey(
    "raw", enc.encode(password), {name: "PBKDF2"}, false, ["deriveKey"]
  );
  const salt = crypto.getRandomValues(new Uint8Array(16));
  // PBKDF2-SHA1, 100000 iterations
  const key = await crypto.subtle.deriveKey(
    {
      name: "PBKDF2",
      salt: salt,
      iterations: 100000,
      hash: "SHA-1"
    },
    passwordKey,
    { name: "AES-CBC", length: 256 },
    false,
    ["encrypt", "decrypt"]
  );
  const iv = crypto.getRandomValues(new Uint8Array(16));
  const encryptedMEK = await crypto.subtle.encrypt(
    { name: "AES-CBC", iv: iv },
    key,
    mekBytes
  );
  return {
    encryptedMEK: btoa(String.fromCharCode.apply(null, new Uint8Array(encryptedMEK))),
    salt: btoa(String.fromCharCode.apply(null, salt)),
    iv: btoa(String.fromCharCode.apply(null, iv))
  };
}

async function register(serverUrl, username, password) {
  try {
    const mekBytes = new Uint8Array(32);
    window.crypto.getRandomValues(mekBytes);
    const mekEnc = await encryptMEK(mekBytes, password);
    const url = serverUrl.replace(/\/$/, '') + '/api/users/register';
    const resp = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        username,
        password,
        encryptedMEK: mekEnc.encryptedMEK,
        salt: mekEnc.salt,
        iv: mekEnc.iv
      })
    });
    if (!resp.ok) throw new Error('Registration failed: ' + resp.status);
    // Auto-login after registration
    const loginResp = await login(serverUrl, username, password);
    return loginResp;
  } catch (err) {
    console.error('[apiClient] register error:', err);
    throw err;
  }
}

window.PhotoTagAPI = {
  fetchManufacturers,
  fetchFilmStocks,
  login,
  register,
  encryptMEK
};

async function login(serverUrl, username, password) {
  try {
    const url = serverUrl.replace(/\/$/, '') + '/api/users/login';
    const resp = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password })
    });
    if (!resp.ok) throw new Error('Login failed: ' + resp.status);
    return await resp.json();
  } catch (err) {
    console.error('[apiClient] login error:', err);
    throw err;
  }
}

window.PhotoTagAPI = {
  fetchManufacturers,
  fetchFilmStocks,
  login
};

window.PhotoTagAPI = {
  fetchManufacturers,
  fetchFilmStocks
};
