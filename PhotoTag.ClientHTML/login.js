document.addEventListener('DOMContentLoaded', function() {
	const loginBtn = document.getElementById('loginBtn');
	const registerBtn = document.getElementById('registerBtn');

	loginBtn.onclick = async function() {
		const serverUrl = document.getElementById('serverUrl').value.trim();
		const username = document.getElementById('username').value.trim();
		const password = document.getElementById('password').value;

		if (!serverUrl || !username || !password) {
			alert('Please fill in all fields.');
			return;
		}

		try {
			const data = await window.PhotoTagAPI.login(serverUrl, username, password);
			alert('Login successful!\nToken: ' + data.token);
			document.cookie = `phototag_token=${data.token}; path=/; SameSite=Strict`;
			if (data.encryptedMEK) document.cookie = `phototag_encryptedMEK=${data.encryptedMEK}; path=/; SameSite=Strict`;
		} catch (err) {
			alert('Error: ' + err);
		}
	};

	registerBtn.onclick = async function() {
		const serverUrl = document.getElementById('serverUrl').value.trim();
		const username = document.getElementById('username').value.trim();
		const password = document.getElementById('password').value;

		if (!serverUrl || !username || !password) {
			alert('Please fill in all fields.');
			return;
		}

		try {
			const data = await window.PhotoTagAPI.register(serverUrl, username, password);
			alert('Registration and login successful!\nToken: ' + data.token);
			document.cookie = `phototag_token=${data.token}; path=/; SameSite=Strict`;
			if (data.encryptedMEK) document.cookie = `phototag_encryptedMEK=${data.encryptedMEK}; path=/; SameSite=Strict`;
		} catch (err) {
			alert('Error: ' + err);
		}
	};
});
