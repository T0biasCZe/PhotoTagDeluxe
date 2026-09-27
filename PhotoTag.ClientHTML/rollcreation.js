document.addEventListener('DOMContentLoaded', async function() {
	const manufacturerDropdown = document.getElementById('manufacturerDropdown');
	const filmStockDropdown = document.getElementById('filmStockDropdown');

	let manufacturers = await window.PhotoTagAPI.fetchManufacturers();
	console.log('[rollcreation] manufacturers:', manufacturers);
	const manufacturerOptions = manufacturers.map(m => ({
		id: m.id,
		text: m.name,
		img: m.logoFileName ? 'images/' + m.logoFileName : 'images/default.png'
	}));
	createMetroDropdown('manufacturerDropdown', manufacturerOptions, onManufacturerSelect, manufacturerOptions[0]?.id);

	async function updateFilmStocks() {
		const selectedId = manufacturerDropdown.dataset.selectedId || manufacturerOptions[0]?.id;
		const stocks = await window.PhotoTagAPI.fetchFilmStocks(selectedId);
		if (!stocks.length) {
			createMetroDropdown('filmStockDropdown', [{ id: '', text: 'No stocks', img: 'images/default.png' }], null, '');
			console.log('[rollcreation] No stocks for manufacturer');
			return;
		}
		const filmStockOptions = stocks.map(s => ({
			id: s.id,
			text: s.nameFullUser || s.nameInternal,
			img: s.iconFileName ? 'images/' + s.iconFileName : 'images/default.png'
		}));
		createMetroDropdown('filmStockDropdown', filmStockOptions, null, filmStockOptions[0]?.id);
		console.log('[rollcreation] filmStockDropdown populated:', filmStockOptions);
	}

	function onManufacturerSelect(opt) {
		manufacturerDropdown.dataset.selectedId = opt.id;
		updateFilmStocks();
	}
	updateFilmStocks();
});
