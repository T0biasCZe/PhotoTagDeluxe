// metroDropdown.js
// Custom Metro-themed dropdown with image and text

function createMetroDropdown(dropdownId, options, onSelect, selectedId) {
  const dropdown = document.getElementById(dropdownId);
  dropdown.classList.remove('open');
  dropdown.innerHTML = '';

  // Selected
  let selected = options.find(opt => opt.id === selectedId) || options[0];
  const selectedDiv = document.createElement('div');
  selectedDiv.className = 'dropdown-selected';
  selectedDiv.innerHTML = `<img src="${selected.img}" alt=""> <span>${selected.text}</span>`;
  dropdown.appendChild(selectedDiv);

  // List
  const listDiv = document.createElement('div');
  listDiv.className = 'dropdown-list';
  options.forEach(opt => {
    const optDiv = document.createElement('div');
    optDiv.className = 'dropdown-option';
    optDiv.innerHTML = `<img src="${opt.img}" alt=""> <span>${opt.text}</span>`;
    optDiv.onclick = () => {
      dropdown.classList.remove('open');
      createMetroDropdown(dropdownId, options, onSelect, opt.id);
      if (onSelect) onSelect(opt);
    };
    listDiv.appendChild(optDiv);
  });
  dropdown.appendChild(listDiv);

  // Toggle
  selectedDiv.onclick = () => {
    dropdown.classList.toggle('open');
  };

  // Close on outside click
  document.addEventListener('mousedown', function handler(e) {
    if (!dropdown.contains(e.target)) {
      dropdown.classList.remove('open');
      document.removeEventListener('mousedown', handler);
    }
  });
}

// Example usage (to be replaced with real data)
// createMetroDropdown('manufacturerDropdown', [
//   { id: 'kodak', text: 'Kodak', img: 'images/kodak.png' },
//   { id: 'foma', text: 'Foma', img: 'images/foma.png' }
// ], opt => console.log('Selected:', opt));
