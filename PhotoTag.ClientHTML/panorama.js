var DEBUG = false;

document.addEventListener('DOMContentLoaded', function() {
	const tabs = Array.from(document.querySelectorAll('.panorama-tab-header'));
	const contents = Array.from(document.querySelectorAll('.panorama-content'));
	const contentsContainer = document.querySelector('.panorama-contents');
	const header = document.getElementById('panoramaHeader');
	let currentTab = 0;

	function showTab(idx) {
		if (DEBUG) console.log('[showTab] Switching to tab:', idx);
		currentTab = idx;
		let tabWidth = contentsContainer.offsetWidth * 0.92;
		let scrollOffset = idx * tabWidth;
        if (DEBUG)  console.log('[showTab] Calculated scrollOffset:', scrollOffset);
		contentsContainer.scrollTo({
			left: scrollOffset,
			behavior: 'smooth'
		});
		contents.forEach((c, i) => c.classList.toggle('active', i === idx));
		animateHeader(idx);
	}

	function animateHeader(idx) {
		const tabCount = contents.length;
		const headerWidth = header.offsetWidth;
		const containerWidth = header.parentElement.offsetWidth;
		let maxShift = Math.max(headerWidth - containerWidth, 0);
		if (DEBUG) console.log('[animateHeader] idx:', idx, 'tabCount:', tabCount, 'headerWidth:', headerWidth, 'containerWidth:', containerWidth, 'maxShift:', maxShift);
		if (tabCount <= 1 || maxShift === 0) {
			if (DEBUG) console.log('[animateHeader] No shift needed');
			header.style.transform = 'translateX(0)';
			return;
		}
		let realIdx = idx;
		let realCount = tabCount;
		let shift = 0;
		// If on tabIndex 1 (default), header is at default position
		if (contents[realIdx].dataset.tabindex == "1") {
			if (DEBUG) console.log('[animateHeader] Default tabIndex 1, no shift');
			shift = 0;
		} else if (contents[realIdx].dataset.tabindex == "0") {
			// Tab 0: move header further right
			shift = 0.12 * containerWidth;
			if (DEBUG) console.log('[animateHeader] TabIndex 0, shift right:', shift);
		} else if (realIdx === realCount - 1) {
			// Last tab: move header left
			shift = -maxShift + 0.08 * containerWidth;
			if (DEBUG) console.log('[animateHeader] Last tab, shift left:', shift);
		} else {
			// Other tabs: proportional shift
			shift = -maxShift * (realIdx / (realCount - 1));
			if (DEBUG) console.log('[animateHeader] Calculated shift:', shift);
		}
		header.style.transform = `translateX(${shift}px)`;
	}

	function resizeContents() {
		const width = contentsContainer.offsetWidth;
		contents.forEach(c => {
			c.style.width = (width * 0.92) + 'px';
			c.style.display = 'inline-block';
		});
		contentsContainer.style.overflowX = 'hidden';
		contentsContainer.style.whiteSpace = 'nowrap';
		contentsContainer.style.scrollBehavior = 'smooth';
		contentsContainer.style.alignItems = 'flex-start';
		animateHeader(currentTab);
	}

	window.addEventListener('resize', resizeContents);
	resizeContents();

	// Swipe detection
	let startX = null;
	contentsContainer.addEventListener('touchstart', function(e) {
		startX = e.touches[0].clientX;
	});
	contentsContainer.addEventListener('touchend', function(e) {
		if (startX === null) return;
		const endX = e.changedTouches[0].clientX;
		if (endX - startX > 50 && currentTab > 0) showTab(currentTab - 1);
		else if (startX - endX > 50 && currentTab < contents.length - 1) showTab(currentTab + 1);
		startX = null;
	});

	// Keyboard arrow navigation
	document.addEventListener('keydown', function(e) {
		if (e.key === 'ArrowLeft' && currentTab > 0) showTab(currentTab - 1);
		if (e.key === 'ArrowRight' && currentTab < contents.length - 1) showTab(currentTab + 1);
	});

	// Click on left/right edge
	contentsContainer.addEventListener('click', function(e) {
		const x = e.clientX;
		const width = contentsContainer.offsetWidth;
        
        var leftMargin = Math.min(width * 0.10, 64)
        var rightMargin = Math.min(width * 0.10, 64);
		if (x < leftMargin && currentTab > 0) showTab(currentTab - 1);
		else if (x > width - rightMargin && currentTab < contents.length - 1) showTab(currentTab + 1);
	});

	// Tab header click
	tabs.forEach((tab, idx) => {
		tab.onclick = () => showTab(idx);
	});

    
    showTab(1);

});
