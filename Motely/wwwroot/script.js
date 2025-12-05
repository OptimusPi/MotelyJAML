// ================================================
// JAML Web UI - JavaScript Functions
// ================================================

// JAML Taglines (from TUI)
const jamlTaglines = [
    "Jetting At Maximum Lightspeed",
    "Jokers Are My Legacy", 
    "Jimbo's Awesome Markup Language",
    "Just Another Markup Language",
    "Jokes And Memes Language",
    "JSON Ain't Markup Language",
    "Jury-rigged And Mostly Legal",
    "Just Absolutely Mental Logic",
    "Janky Ace Markup Language",

    "Jokers And Multiplier Love",
    "Jimbo's Ante-Multiplier Logic",
    "Jackpotting All My Luck",
    "Jokers And Motley Legends",
    "Jimbo's All-in Multiplier Logic",
    "Jokers And Multiplier Loot",
    "Jimbo's Ante-Meme Language",

    "Jimbo's Absurd Meme Lab",
    "Janky API Markup Language",
    "Justice And Machine Learning",
];

// Global State
let isSearching = false;
let searchAborted = false;
let currentSearchId = null;
let currentSearchJaml = null; // The JAML content that started the current search
let currentBatchSize = 1000000;
let isProgrammaticEdit = false; // Flag to ignore programmatic setJamlValue calls
let totalSeedsSearched = 0;
let searchResults = [];
let searchColumns = ['seed', 'score'];
let savedFilters = [];
let sortColumn = 'score';
let sortDirection = 'desc'; // 'asc' or 'desc'
const maxRows = 1000; // Display limit message (API returns up to 1000)

// Sync URL with current search ID (so refresh/bookmark works)
function updateUrlWithSearchId(searchId) {
    const url = new URL(window.location);
    if (searchId) {
        url.searchParams.set('search', searchId);
    } else {
        url.searchParams.delete('search');
    }
    // Update URL without reloading page
    window.history.replaceState({}, '', url);
}

// ================================================
// JAML Editor Helpers (Monaco or textarea fallback)
// ================================================
function getJamlValue() {
    if (window.jamlEditor) {
        return window.jamlEditor.getValue();
    }
    return document.getElementById('filterJaml').value;
}

function setJamlValue(value) {
    isProgrammaticEdit = true; // Mark as programmatic so change events don't invalidate
    document.getElementById('filterJaml').value = value;
    if (window.jamlEditor) {
        window.jamlEditor.setValue(value);
    }
    isProgrammaticEdit = false;
}

// Toggle between Monaco and Plain text editor
let usePlainEditor = false;

function toggleEditorMode() {
    const monacoContainer = document.getElementById('monacoEditor');
    const plainTextarea = document.getElementById('filterJaml');
    const toggleBtn = document.getElementById('editorToggle');

    usePlainEditor = !usePlainEditor;

    if (usePlainEditor) {
        // Switch to plain editor
        // Sync content from Monaco to textarea
        if (window.jamlEditor) {
            plainTextarea.value = window.jamlEditor.getValue();
        }
        monacoContainer.style.display = 'none';
        plainTextarea.style.display = 'block';
        toggleBtn.textContent = 'Monaco';

        // Override getJamlValue/setJamlValue to use textarea
        window.getJamlValue = () => plainTextarea.value;
        window.setJamlValue = (val) => {
            plainTextarea.value = val;
            if (window.jamlEditor) window.jamlEditor.setValue(val);
        };

        // Add change listener to plain textarea
        plainTextarea.oninput = () => onUserJamlEdit();
    } else {
        // Switch to Monaco editor
        // Sync content from textarea to Monaco
        if (window.jamlEditor) {
            window.jamlEditor.setValue(plainTextarea.value);
        }
        plainTextarea.style.display = 'none';
        monacoContainer.style.display = 'block';
        toggleBtn.textContent = 'Plain';

        // Restore Monaco-based getJamlValue/setJamlValue
        window.getJamlValue = () => window.jamlEditor ? window.jamlEditor.getValue() : plainTextarea.value;
        window.setJamlValue = (val) => {
            plainTextarea.value = val;
            if (window.jamlEditor) window.jamlEditor.setValue(val);
        };
    }
}

// ================================================
// JAML Auto-Formatter - keeps arrays inline, NEVER uses {} brackets
// ================================================
function formatJaml() {
    const content = getJamlValue();
    if (!content.trim()) return;

    if (typeof jsyaml === 'undefined') {
        showStatus('YAML library not loaded yet, try again');
        return;
    }

    try {
        const parsed = jsyaml.load(content);
        if (!parsed || typeof parsed !== 'object') {
            showStatus('Invalid JAML - could not parse');
            return;
        }

        // Dump fully expanded first (flowLevel: -1 = no flow syntax at all)
        let formatted = jsyaml.dump(parsed, {
            indent: 2,
            lineWidth: -1,
            noArrayIndent: false,
            sortKeys: false,
            quotingType: "'",
            forceQuotes: false,
            flowLevel: -1  // NEVER use {} or [] flow syntax
        });

        // Post-process: collapse simple number arrays back to inline [1, 2, 3]
        // Match patterns like:
        //   antes:
        //     - 1
        //     - 2
        // And convert to: antes: [1, 2]
        formatted = formatted.replace(
            /^(\s*)(antes|shopSlots|packSlots|sources):\n((?:\1  - \d+\n?)+)/gm,
            (match, indent, key, items) => {
                const values = items.match(/\d+/g);
                if (values) {
                    return `${indent}${key}: [${values.join(', ')}]\n`;
                }
                return match;
            }
        );

        // Post-process: remove "null" from or:/and: shorthand (JAML allows "- or:" without value)
        formatted = formatted.replace(/^(\s*- )(or|and): null$/gm, '$1$2:');

        setJamlValue(formatted);
        showStatus('JAML formatted!');

    } catch (e) {
        showStatus(`Format error: ${e.message}`);
    }
}

// Quick format shortcut: Ctrl+Shift+F
document.addEventListener('keydown', (e) => {
    if (e.ctrlKey && e.shiftKey && e.key === 'F') {
        e.preventDefault();
        formatJaml();
    }
});

// Called when user edits JAML (not programmatic loads) - invalidates current search
// No string comparison needed: ANY user edit means the filter might be different!
function onUserJamlEdit() {
    if (isProgrammaticEdit) return; // Ignore programmatic setJamlValue calls

    // User edited the JAML - reset dropdown to show they're creating something new
    const dropdown = document.getElementById('savedSearches');
    if (dropdown && dropdown.value !== '') {
        dropdown.value = '';
    }

    if (!currentSearchId) return; // No active search to invalidate

    // User edited the JAML - invalidate the search
    currentSearchId = null;
    currentSearchJaml = null;
    updateUrlWithSearchId(null); // Clear URL - filter changed
    searchResults = [];
    updateSearchButton('START', 0);
    showStatus('Filter changed - ready to start new search');

    // Clear batch override - new filter means fresh start
    const batchInput = document.getElementById('batchOverride');
    if (batchInput) {
        batchInput.value = '';
        batchInput.placeholder = 'Batch #';
    }
}

// ================================================
// Initialization
// ================================================
document.addEventListener('DOMContentLoaded', function() {
    loadFilters();
    startTaglineRotation();
    
    
    // Load search ID from URL if present and check its status
    const urlParams = new URLSearchParams(window.location.search);
    const searchId = urlParams.get('search');
    if (searchId) {
        currentSearchId = searchId;
        checkExistingSearchStatus(searchId);
    }
});

// ================================================
// JAML Branding Functions
// ================================================
function startTaglineRotation() {
    const taglineElement = document.getElementById('jaml-tagline');
    let currentIndex = 0;
    
    // Rotate tagline every 5 seconds
    setInterval(() => {
        currentIndex = (currentIndex + 1) % jamlTaglines.length;
        taglineElement.style.opacity = '0';
        
        setTimeout(() => {
            taglineElement.textContent = jamlTaglines[currentIndex];
            taglineElement.style.opacity = '1';
        }, 300);
    }, 5000);
    
    // Click to manually cycle
    taglineElement.addEventListener('click', () => {
        currentIndex = (currentIndex + 1) % jamlTaglines.length;
        taglineElement.textContent = jamlTaglines[currentIndex];
    });
}

// ================================================
// Tab Management
// ================================================
function switchTab(tabName, tabButton) {
    // Hide all tab contents
    const tabs = document.querySelectorAll('.tab-content');
    tabs.forEach(tab => tab.classList.remove('active'));
    
    // Remove active class from all tab buttons
    const tabButtons = document.querySelectorAll('.tab');
    tabButtons.forEach(btn => btn.classList.remove('active'));
    
    // Show selected tab and mark button as active
    document.getElementById(tabName + '-tab').classList.add('active');
    if (tabButton) tabButton.classList.add('active');
}

// ================================================
// JSON to JAML Conversion
// ================================================
async function convertJsonToJaml() {
    const filterContent = getJamlValue().trim();

    if (!filterContent) {
        showStatus('Paste JSON filter content first');
        return;
    }

    // Check if it looks like JSON (starts with { or has "type":)
    if (!filterContent.startsWith('{') && !filterContent.includes('"type"')) {
        showStatus('Content doesn\'t look like JSON - already JAML?');
        return;
    }

    showStatus('🔄 Converting JSON to JAML...');

    try {
        const response = await fetch('/convert', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ jsonContent: filterContent })
        });

        const data = await response.json();

        if (!response.ok) {
            showStatus(`❌ Convert failed: ${data.error || 'Unknown error'}`);
            return;
        }

        setJamlValue(data.jaml);
        showStatus('✅ Converted to JAML! Review and start search.');

    } catch (error) {
        showStatus(`❌ Convert error: ${error.message}`);
    }
}

// ================================================
// Genie Functions
// ================================================
async function generateJAML() {
    const prompt = document.getElementById('geniePrompt').value.trim();
    const statusDiv = document.getElementById('genieStatus');

    if (!prompt) {
        statusDiv.innerHTML = '<div class="status-message error">Please enter a description!</div>';
        return;
    }

    statusDiv.innerHTML = '<div class="status-message loading">🧞 Genie is thinking...</div>';

    try {
        const response = await fetch('/genie', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ prompt })
        });

        const data = await response.json();

        if (!response.ok) {
            statusDiv.innerHTML = `<div class="status-message error">Genie error: ${data.error || 'Failed to generate JAML'}</div>`;
            return;
        }

        const jaml = data.jaml;
        setJamlValue(jaml);

        // Switch to JAML tab
        document.querySelector('.tab:nth-child(2)').click();
        
        statusDiv.innerHTML = '<div class="status-message success">✨ JAML generated! Switched to editor.</div>';
    } catch (error) {
        statusDiv.innerHTML = `<div class="status-message error">Genie error: ${error.message}</div>`;
    }
}

// ================================================
// Search Functions
// ================================================
function toggleSearch() {
    if (isSearching) {
        stopSearch();
    } else {
        const btn = document.getElementById('searchBtn');
        if (btn.textContent.includes('Continue')) {
            continueSearch();
        } else {
            runSearch();
        }
    }
}

async function continueSearch() {
    // Just call runSearch - the server's POST /search already handles resume
    // via bgState.StartBatch which was saved when we stopped
    return runSearch();
}

async function runSearch() {
    let filterJaml = getJamlValue();
    const resultsContainer = document.getElementById('resultsGrid');

    if (!filterJaml.trim()) {
        resultsContainer.innerHTML = '<div class="status-message error">Please enter a filter!</div>';
        return;
    }

    if (isSearching) {
        showStatus('Search already running...');
        return;
    }

    isSearching = true;
    searchAborted = false;
    searchResults = [];

    const searchBtn = document.getElementById('searchBtn');

    // CRITICAL: Disable button during POST to prevent race conditions
    searchBtn.textContent = 'Starting...';
    searchBtn.className = 'button-blue';
    searchBtn.disabled = true;

    try {
        // ONE POST to start search
        showStatus('Starting search...');

        // Check for batch override
        const batchOverrideInput = document.getElementById('batchOverride');
        const batchOverride = batchOverrideInput && batchOverrideInput.value ? parseInt(batchOverrideInput.value) : null;

        const requestBody = { filterJaml, seedCount: 100000000 };
        if (batchOverride !== null && !isNaN(batchOverride)) {
            requestBody.startBatch = batchOverride;
        }

        const response = await fetch('/search', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(requestBody)
        });

        // Re-enable button after POST completes
        searchBtn.disabled = false;

        if (!response.ok) {
            const error = await response.json();
            showStatus(`❌ Error: ${error.error}`);
            isSearching = false;
            updateSearchButton('START', 0);
            return;
        }

        const data = await response.json();
        currentSearchId = data.searchId;
        currentSearchJaml = filterJaml.trim(); // Save JAML that started this search
        updateUrlWithSearchId(currentSearchId); // Sync URL so refresh/bookmark works

        // NOW we can show Stop button - searchId is set!
        searchBtn.textContent = 'Stop';
        searchBtn.className = 'button-danger';

        // Show immediate fertilizer results
        if (data.results && data.results.length > 0) {
            searchResults = data.results;
            displayResults({ results: searchResults, columns: data.columns });
            document.getElementById('shareBtn').disabled = false;
        }

        // POST /search returns isBackgroundRunning=true, so we KNOW it's running
        // Start polling after 2s delay - the search IS running, we just need to wait for results
        showStatus('Search started...');
        await pollSearchStatus(2000);

    } catch (error) {
        showStatus(`❌ Network error: ${error.message}`);
        isSearching = false;
        searchBtn.disabled = false;
        updateSearchButton('START', 0);
    }
}

async function pollSearchStatus(delay = 1000) {
    let pollCount = 0;

    while (isSearching && !searchAborted) {
        try {
            // Always wait at least 1s between polls to avoid overwhelming the search
            await new Promise(r => setTimeout(r, delay));
            
            const response = await fetch(`/search?id=${currentSearchId}`);
            if (!response.ok) {
                showStatus('❌ Error polling search status');
                stopSearch();
                return;
            }

            const data = await response.json();
            const running = data.isBackgroundRunning === true;
            const seedsSearched = data.seedsSearched || 0;
            const seedsPerSecond = data.seedsPerSecond || 0;

            // Format speed nicely (M/s for millions)
            const speedStr = seedsPerSecond >= 1000000
                ? `${(seedsPerSecond / 1000000).toFixed(1)}M/s`
                : seedsPerSecond >= 1000
                    ? `${(seedsPerSecond / 1000).toFixed(0)}K/s`
                    : `${seedsPerSecond.toFixed(0)}/s`;

            // Update batch override field with current position
            const batchInput = document.getElementById('batchOverride');
            if (batchInput && data.currentBatch !== undefined) {
                batchInput.value = data.currentBatch;
                batchInput.placeholder = `Current: ${data.currentBatch}`;
            }

            // Update results from DB FIRST so count is accurate
            if (data.results && data.results.length > 0) {
                mergeResults(data.results);
                displayResults({ results: searchResults, columns: data.columns });
            }

            // Update button and status based on isBackgroundRunning (most reliable)
            // Use data.seedsFound for accurate count (actual DB count, not capped at 1000)
            const foundCount = data.seedsFound || searchResults.length;
            if (running) {
                updateSearchButton('RUNNING', 0);
                showStatus(`Batch ${data.currentBatch || 0} | ${speedStr} | ${(seedsSearched / 1000000).toFixed(1)}M searched | ${foundCount} found`);
            } else {
                updateSearchButton('CONTINUE', 0);
                showStatus(`Stopped at batch ${data.currentBatch || 0} | ${(seedsSearched / 1000000).toFixed(1)}M searched | ${foundCount} found`);
                isSearching = false;
                return;
            }

            // Progressive backoff: 1→2→3→4→5→5→5... (min 1s, max 5s between polls)
            pollCount++;
            delay = Math.min(1000 + pollCount * 1000, 5000);
            
        } catch (error) {
            if (!searchAborted) {
                console.error('Poll error:', error);
                showStatus('⚠️ Connection error, retrying...');
                await new Promise(r => setTimeout(r, 5000));
            } else {
                // If aborted, break out of loop and ensure button is updated
                break;
            }
        }
    }
}

async function stopSearch() {
    // Set flags immediately to stop polling
    isSearching = false;
    searchAborted = true;
    
    if (!currentSearchId) {
        updateSearchButton('START', 0);
        showStatus('No search to stop');
        return;
    }
    
    try {
        const response = await fetch('/search/stop', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ searchId: currentSearchId })
        });

        if (!response.ok) {
            const error = await response.json();
            showStatus(`Error stopping: ${error.error}`);
            updateSearchButton('START', 0);
            return;
        }

        const data = await response.json();
        showStatus(`${data.message} - ${searchResults.length} results`);
        
        // Get current progress from the API to show accurate state
        const statusResponse = await fetch(`/search?id=${currentSearchId}`);
        if (statusResponse.ok) {
            const statusData = await statusResponse.json();
            const progress = statusData.progressPercent || 0;
            updateSearchButton('CONTINUE', progress / 100);

            // Sync batch input with final position
            const batchInput = document.getElementById('batchOverride');
            if (batchInput && statusData.currentBatch !== undefined) {
                batchInput.value = statusData.currentBatch;
                batchInput.placeholder = `Current: ${statusData.currentBatch}`;
            }
        } else {
            updateSearchButton('START', 0);
        }
        
    } catch (error) {
        showStatus(`❌ Network error: ${error.message}`);
        updateSearchButton('START', 0);
    }
}

function shareSearch() {
    if (!currentSearchId) {
        alert('No active search to share');
        return;
    }
    
    const url = new URL(window.location);
    url.searchParams.set('search', currentSearchId);
    
    navigator.clipboard.writeText(url.toString()).then(() => {
        const btn = document.getElementById('shareBtn');
        const originalText = btn.textContent;
        btn.textContent = '✅ Copied!';
        setTimeout(() => btn.textContent = originalText, 2000);
    });
}

function mergeResults(newResults) {
    const existingSeeds = new Set(searchResults.map(r => r.seed));
    for (const result of newResults) {
        if (!existingSeeds.has(result.seed)) {
            searchResults.push(result);
            existingSeeds.add(result.seed);
        }
    }
    
    // Silently re-apply current sort when new results flow in
    applySortToResults();
}

function applySortToResults() {
    searchResults.sort((a, b) => {
        let valueA, valueB;
        
        if (sortColumn === 'seed') {
            valueA = a.seed;
            valueB = b.seed;
        } else if (sortColumn === 'score') {
            valueA = a.score;
            valueB = b.score;
        } else {
            // Tally column
            const colIndex = searchColumns.indexOf(sortColumn);
            if (colIndex >= 2) {
                const tallyIndex = colIndex - 2;
                valueA = a.tallies?.[tallyIndex] || 0;
                valueB = b.tallies?.[tallyIndex] || 0;
            } else {
                return 0;
            }
        }
        
        if (valueA < valueB) return sortDirection === 'asc' ? -1 : 1;
        if (valueA > valueB) return sortDirection === 'asc' ? 1 : -1;
        return 0;
    });
}

// ================================================
// Sorting Functions
// ================================================
function sortResults(column) {
    // Toggle direction if clicking same column
    if (sortColumn === column) {
        sortDirection = sortDirection === 'asc' ? 'desc' : 'asc';
    } else {
        sortColumn = column;
        sortDirection = column === 'seed' ? 'asc' : 'desc'; // Seeds A-Z by default, scores high-low
    }
    
    // Apply sort and re-display
    applySortToResults();
    displayResults({ results: searchResults, columns: searchColumns });
}

// ================================================
// Results Display
// ================================================
function displayResults(data) {
    const container = document.getElementById('resultsGrid');
    
    if (!data.results || data.results.length === 0) {
        container.innerHTML = `
            <div class="no-results">
                <p>🎰 No results yet</p>
                <p class="help-text">Search is running...</p>
            </div>
        `;
        return;
    }

    // Store columns for sorting
    searchColumns = data.columns || ['seed', 'score'];
    
    let html = `
        <table class="results-table">
            <thead>
                <tr>
    `;
    
    // Add clickable headers with sort indicators
    searchColumns.forEach(column => {
        const isCurrentSort = sortColumn === column;
        const arrow = isCurrentSort ? (sortDirection === 'asc' ? ' ↑' : ' ↓') : '';
        const displayName = column === 'seed' ? 'Seed' : 
                           column === 'score' ? 'Score' : column;
        html += `<th onclick="sortResults('${column}')" style="cursor: pointer; user-select: none;">${displayName}${arrow}</th>`;
    });
    
    html += `
                </tr>
            </thead>
            <tbody>
    `;

    // Add result rows (show all 1000 from API)
    const displayResults = data.results;
    
    displayResults.forEach(result => {
        html += `
            <tr onclick="quickAnalyze('${result.seed}')" style="cursor: pointer;" title="Click to analyze this seed">
                <td><code>${result.seed}</code></td>
                <td>${result.score}</td>
        `;
        
        if (result.tallies && result.tallies.length > 0) {
            result.tallies.forEach(tally => {
                html += `<td>${tally}</td>`;
            });
        }
        
        html += '</tr>';
    });

    html += `
            </tbody>
        </table>
    `;

    if (data.results.length > maxRows) {
        html += `<div class="info-text">Showing top ${maxRows} of ${data.results.length} results</div>`;
    }

    container.innerHTML = html;
}

// ================================================
// Filter Management
// ================================================
async function loadFilters() {
    try {
        const response = await fetch('/filters');
        if (response.ok) {
            savedFilters = await response.json();
            const dropdown = document.getElementById('savedSearches');
            dropdown.innerHTML = '<option value="">Select a saved search...</option>';
            savedFilters.forEach((filter, i) => {
                dropdown.innerHTML += `<option value="${i}">${filter.name}</option>`;
            });
        }
    } catch (e) {
        console.error('Failed to load filters:', e);
    }
}

async function loadSavedSearch() {
    const dropdown = document.getElementById('savedSearches');
    const idx = dropdown.value;
    
    // STOP any currently running search first
    if (isSearching && currentSearchId) {
        showStatus('🛑 Stopping current search...');
        searchAborted = true;
        isSearching = false;
        
        try {
            await fetch('/search/stop', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ searchId: currentSearchId })
            });
        } catch (e) {
            console.error('Failed to stop search:', e);
        }
    }
    
    if (idx === '') {
        setJamlValue('');
        updateSearchButton('START', 0);
        currentSearchId = null;
        currentSearchJaml = null;
        updateUrlWithSearchId(null); // Clear URL param
        searchResults = [];
        displayResults({ results: [], columns: ['seed', 'score'] });
        return;
    }

    const filter = savedFilters[parseInt(idx)];
    if (filter && filter.filterJaml) {
        setJamlValue(filter.filterJaml);

        // Use server-provided searchId (guaranteed to match what LoadSavedFilters generated)
        // Fallback to generating our own if server didn't provide one (shouldn't happen)
        const searchId = filter.searchId || generateSearchId(
            filter.name || 'unnamed',
            extractFromJaml(filter.filterJaml, 'deck') || 'Red',
            extractFromJaml(filter.filterJaml, 'stake') || 'White'
        );

        // Check if this search exists and get its status
        try {
            const response = await fetch(`/search?id=${searchId}`);
            if (response.ok) {
                const data = await response.json();
                currentSearchId = searchId;
                updateUrlWithSearchId(currentSearchId); // Sync URL
                // Use server's JAML as source of truth for what built the results
                currentSearchJaml = data.filterJaml ? data.filterJaml.trim() : filter.filterJaml.trim();

                // Show existing results if any
                if (data.results && data.results.length > 0) {
                    searchResults = data.results;
                    displayResults({ results: searchResults, columns: data.columns });
                    document.getElementById('shareBtn').disabled = false;
                }

                // Auto-fill batch override with current batch position (user can still override)
                const batchOverrideInput = document.getElementById('batchOverride');
                if (batchOverrideInput && data.currentBatch !== undefined) {
                    batchOverrideInput.value = data.currentBatch;
                    batchOverrideInput.placeholder = `Current: ${data.currentBatch}`;
                }

                // Update button based on search status
                const status = data.searchStatus || data.status || 'stopped';
                const progress = data.progressPercent || 0;

                if (status === 'running' || data.isBackgroundRunning) {
                    isSearching = true;
                    updateSearchButton('RUNNING', progress / 100);
                    showStatus(`🔍 Search running at batch ${data.currentBatch || 0}`);
                    await pollSearchStatus(0);
                } else if (data.currentBatch > 0) {
                    updateSearchButton('CONTINUE', progress / 100);
                    showStatus(`📊 Loaded existing search - ${searchResults.length} results, batch ${data.currentBatch}`);
                } else {
                    // No existing search - show START button
                    currentSearchJaml = null;
                    updateSearchButton('START', 0);
                    showStatus(`📄 Filter loaded: ${filter.name}`);
                }
            } else {
                // No existing search - show START button
                currentSearchId = null;
                currentSearchJaml = null;
                updateUrlWithSearchId(null); // Clear URL param
                updateSearchButton('START', 0);
                showStatus(`📄 Filter loaded: ${filter.name}`);
                // Clear batch override for new search
                const batchInput = document.getElementById('batchOverride');
                if (batchInput) {
                    batchInput.value = '';
                    batchInput.placeholder = 'Batch #';
                }
            }
        } catch (error) {
            console.error('Failed to check search status:', error);
            currentSearchId = null;
            currentSearchJaml = null;
            updateUrlWithSearchId(null); // Clear URL param
            updateSearchButton('START', 0);
            showStatus(`📄 Filter loaded: ${filter.name}`);
            // Clear batch override on error
            const batchInput = document.getElementById('batchOverride');
            if (batchInput) {
                batchInput.value = '';
                batchInput.placeholder = 'Batch #';
            }
        }
    }
}

async function deleteSelectedSearch() {
    const dropdown = document.getElementById('savedSearches');
    const idx = dropdown.value;
    if (idx === '') {
        alert('No filter selected');
        return;
    }
    
    const filter = savedFilters[parseInt(idx)];
    if (!confirm(`Delete filter ${filter.name}?`)) return;
    
    try {
        const response = await fetch(`/filters/${filter.filePath}`, { method: 'DELETE' });
        if (response.ok) {
            await loadFilters();
            setJamlValue('');
        } else {
            alert('Delete failed');
        }
    } catch (e) {
        console.error('Delete failed:', e);
        alert('Delete failed');
    }
}

async function checkExistingSearchStatus(searchId) {
    try {
        const response = await fetch(`/search?id=${searchId}`);
        if (!response.ok) return;

        const data = await response.json();

        // Populate JAML editor and track the JAML that built these results
        if (data.filterJaml) {
            setJamlValue(data.filterJaml);
            currentSearchJaml = data.filterJaml.trim();
        }

        // Show existing results
        if (data.results && data.results.length > 0) {
            searchResults = data.results;
            displayResults({ results: searchResults, columns: data.columns });
            document.getElementById('shareBtn').disabled = false;
        }
        
        // Check if search is still running - use isBackgroundRunning as primary signal
        const status = data.searchStatus || data.status || 'stopped';
        const progress = data.progressPercent || 0; // Already 0-100
        const running = status === 'running' || data.isBackgroundRunning;

        // Update batch override field with current position
        const batchInput = document.getElementById('batchOverride');
        if (batchInput && data.currentBatch !== undefined) {
            batchInput.value = data.currentBatch;
            batchInput.placeholder = `Current: ${data.currentBatch}`;
        }

        if (running) {
            // Resume polling existing search - search IS RUNNING!
            isSearching = true;
            updateSearchButton('RUNNING', progress / 100);
            showStatus(`🔍 Search running at batch ${data.currentBatch || 0}`);
            await pollSearchStatus(0); // Start polling immediately
        } else if (data.currentBatch > 0) {
            // Stopped search with progress - show Continue button
            updateSearchButton('CONTINUE', progress / 100);
            showStatus(`📊 Loaded existing search - ${searchResults.length} results, batch ${data.currentBatch}`);
        } else {
            // New search
            updateSearchButton('START', 0);
            showStatus(`📊 Loaded existing search - ${searchResults.length} results`);
        }
        
        // Switch to JAML tab
        document.querySelector('.tab:nth-child(2)').click();
        
    } catch (e) {
        console.error('Failed to check search status:', e);
    }
}

// ================================================
// Seed Analysis
// ================================================
async function quickAnalyze(seed) {
    // Switch to analyze tab
    switchTab('analyze', document.querySelector('.tab:nth-child(3)'));
    
    // Populate the seed field
    document.getElementById('analyzeSeed').value = seed;
    
    // Get deck/stake from current filter if available
    const filterJaml = getJamlValue();
    const deckMatch = filterJaml.match(/^deck:\s*(.+)$/m);
    const stakeMatch = filterJaml.match(/^stake:\s*(.+)$/m);
    
    if (deckMatch) {
        document.getElementById('analyzeDeck').value = deckMatch[1].trim();
    }
    if (stakeMatch) {
        document.getElementById('analyzeStake').value = stakeMatch[1].trim();
    }
    
    // Auto-analyze immediately
    await analyzeSeed();
}

async function analyzeSeed() {
    const seed = document.getElementById('analyzeSeed').value.trim().toUpperCase();
    const deck = document.getElementById('analyzeDeck').value;
    const stake = document.getElementById('analyzeStake').value;
    const resultDiv = document.getElementById('analyzeResult');

    if (!seed) {
        resultDiv.innerHTML = '<div class="status-message error">Please enter a seed!</div>';
        return;
    }

    if (seed.length !== 8 || !/^[A-Z0-9]{8}$/.test(seed)) {
        resultDiv.innerHTML = '<div class="status-message error">Seed must be 8 characters (A-Z, 0-9)</div>';
        return;
    }

    resultDiv.innerHTML = '<div class="status-message loading">🔍 Analyzing seed...</div>';

    try {
        const response = await fetch('/analyze', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ seed, deck, stake })
        });

        const data = await response.json();

        if (!response.ok) {
            resultDiv.innerHTML = `<div class="status-message error">Analysis failed: ${data.error}</div>`;
            return;
        }

        // Display analysis results - no fanfare, just the data
        resultDiv.innerHTML = `
            <div class="analyze-output">
                <div class="analyze-header">${seed} | ${deck} | ${stake}</div>
                <pre class="analyze-pre">${data.analysis}</pre>
            </div>
        `;

    } catch (error) {
        resultDiv.innerHTML = `<div class="status-message error">Analysis error: ${error.message}</div>`;
    }
}

// ================================================
// Button State Management
// ================================================
function updateSearchButton(state, progress = 0) {
    const searchBtn = document.getElementById('searchBtn');
    searchBtn.style.background = '';

    switch (state) {
        case 'START':
            searchBtn.textContent = 'Start Search';
            searchBtn.className = 'button-primary';
            break;

        case 'CONTINUE':
            searchBtn.textContent = 'Continue';
            searchBtn.className = 'button-blue';
            break;

        case 'RUNNING':
            searchBtn.textContent = 'Stop';
            searchBtn.className = 'button-danger';
            break;
    }
}

// ================================================
// Utility Functions
// ================================================
function sanitizeSearchId(id) {
    // Match the C# SanitizeSearchId function
    const invalid = ['<', '>', ':', '"', '|', '?', '*', '/', '\\'];
    invalid.forEach(c => {
        id = id.replaceAll(c, '-');
    });
    return id.replace(/,/g, '-').replace(/ /g, '-');
}

function generateSearchId(filterName, deck, stake) {
    // Match the C# API logic exactly
    return sanitizeSearchId(`${filterName}_${deck}_${stake}`);
}

function extractFromJaml(jaml, key) {
    // Extract a top-level key value from JAML (e.g., deck: Ghost -> "Ghost")
    const match = jaml.match(new RegExp(`^${key}:\\s*(.+)$`, 'm'));
    return match ? match[1].trim().replace(/^['"]|['"]$/g, '') : null;
}

function autoRenameFilter(jaml) {
    const nameMatch = jaml.match(/^name:\\s*(.+)$/m);
    if (!nameMatch) return jaml;
    
    const currentName = nameMatch[1].trim();
    const editMatch = currentName.match(/_edit(\\d+)$/);
    
    let newName;
    if (editMatch) {
        const num = parseInt(editMatch[1]) + 1;
        newName = currentName.replace(/_edit\\d+$/, `_edit${num}`);
    } else {
        newName = currentName + '_edit1';
    }
    
    return jaml.replace(/^name:\\s*.+$/m, `name: ${newName}`);
}

function showStatus(message) {
    const statusBar = document.getElementById('status');
    statusBar.textContent = message;
}

function exportResults() {
    if (!searchResults || searchResults.length === 0) {
        alert('No results to export!');
        return;
    }

    // Headers from columns, rows from results - simple array join
    const rows = [
        searchColumns.join(','),
        ...searchResults.map(r => [r.seed, r.score, ...(r.tallies || [])].join(','))
    ];

    // Download
    const blob = new Blob([rows.join('\n')], { type: 'text/csv' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `jaml-${Date.now()}.csv`;
    a.click();
    URL.revokeObjectURL(a.href);
}

// ================================================
// Filter Builder - Item Data
// ================================================
const ITEM_DATA = {
    joker: [
        // Rare
        'DNA', 'Vagabond', 'Baron', 'Obelisk', 'BaseballCard', 'AncientJoker', 'Campfire', 'Blueprint',
        'WeeJoker', 'HitTheRoad', 'TheDuo', 'TheTrio', 'TheFamily', 'TheOrder', 'TheTribe', 'Stuntman',
        'InvisibleJoker', 'Brainstorm', 'DriversLicense', 'BurntJoker',
        // Uncommon
        'JokerStencil', 'FourFingers', 'Mime', 'CeremonialDagger', 'MarbleJoker', 'LoyaltyCard', 'Dusk',
        'Fibonacci', 'SteelJoker', 'Hack', 'Pareidolia', 'SpaceJoker', 'Burglar', 'Blackboard', 'SixthSense',
        'Constellation', 'Hiker', 'CardSharp', 'Madness', 'Seance', 'Vampire', 'Shortcut', 'Hologram',
        'Cloud9', 'Rocket', 'MidasMask', 'Luchador', 'GiftCard', 'TurtleBean', 'Erosion', 'ToTheMoon',
        'StoneJoker', 'LuckyCat', 'Bull', 'DietCola', 'TradingCard', 'FlashCard', 'SpareTrousers', 'Ramen',
        'Seltzer', 'Castle', 'MrBones', 'Acrobat', 'SockAndBuskin', 'Troubadour', 'Certificate', 'SmearedJoker',
        'Throwback', 'RoughGem', 'Bloodstone', 'Arrowhead', 'OnyxAgate', 'GlassJoker', 'Showman', 'FlowerPot',
        'MerryAndy', 'OopsAll6s', 'TheIdol', 'SeeingDouble', 'Matador', 'Satellite', 'Cartomancer', 'Astronomer', 'Bootstraps',
        // Common
        'Joker', 'GreedyJoker', 'LustyJoker', 'WrathfulJoker', 'GluttonousJoker', 'JollyJoker', 'ZanyJoker',
        'MadJoker', 'CrazyJoker', 'DrollJoker', 'SlyJoker', 'WilyJoker', 'CleverJoker', 'DeviousJoker',
        'CraftyJoker', 'HalfJoker', 'CreditCard', 'Banner', 'MysticSummit', 'EightBall', 'Misprint',
        'RaisedFist', 'ChaostheClown', 'ScaryFace', 'AbstractJoker', 'DelayedGratification', 'GrosMichel',
        'EvenSteven', 'OddTodd', 'Scholar', 'BusinessCard', 'Supernova', 'RideTheBus', 'Egg', 'Runner',
        'IceCream', 'Splash', 'BlueJoker', 'FacelessJoker', 'GreenJoker', 'Superposition', 'ToDoList',
        'Cavendish', 'RedCard', 'SquareJoker', 'RiffRaff', 'Photograph', 'ReservedParking', 'MailInRebate',
        'Hallucination', 'FortuneTeller', 'Juggler', 'Drunkard', 'GoldenJoker', 'Popcorn', 'WalkieTalkie',
        'SmileyFace', 'GoldenTicket', 'Swashbuckler', 'HangingChad', 'ShootTheMoon'
    ],
    soulJoker: ['Canio', 'Triboulet', 'Yorick', 'Chicot', 'Perkeo'],
    voucher: [
        'Overstock', 'OverstockPlus', 'ClearanceSale', 'Liquidation', 'Hone', 'GlowUp', 'RerollSurplus',
        'RerollGlut', 'CrystalBall', 'OmenGlobe', 'Telescope', 'Observatory', 'Grabber', 'NachoTong',
        'Wasteful', 'Recyclomancy', 'TarotMerchant', 'TarotTycoon', 'PlanetMerchant', 'PlanetTycoon',
        'SeedMoney', 'MoneyTree', 'Blank', 'Antimatter', 'MagicTrick', 'Illusion', 'Hieroglyph',
        'Petroglyph', 'DirectorsCut', 'Retcon', 'PaintBrush', 'Palette'
    ],
    tag: [
        'UncommonTag', 'RareTag', 'NegativeTag', 'FoilTag', 'HolographicTag', 'PolychromeTag',
        'InvestmentTag', 'VoucherTag', 'BossTag', 'StandardTag', 'CharmTag', 'MeteorTag', 'BuffoonTag',
        'HandyTag', 'GarbageTag', 'EtherealTag', 'CouponTag', 'DoubleTag', 'JuggleTag', 'D6Tag',
        'TopupTag', 'SpeedTag', 'OrbitalTag', 'EconomyTag'
    ],
    tarot: [
        'TheFool', 'TheMagician', 'TheHighPriestess', 'TheEmpress', 'TheEmperor', 'TheHierophant',
        'TheLovers', 'TheChariot', 'Justice', 'TheHermit', 'TheWheelOfFortune', 'Strength',
        'TheHangedMan', 'Death', 'Temperance', 'TheDevil', 'TheTower', 'TheStar', 'TheMoon',
        'TheSun', 'Judgement', 'TheWorld'
    ],
    spectral: [
        'Familiar', 'Grim', 'Incantation', 'Talisman', 'Aura', 'Wraith', 'Sigil', 'Ouija',
        'Ectoplasm', 'Immolate', 'Ankh', 'DejaVu', 'Hex', 'Trance', 'Medium', 'Cryptid', 'Soul', 'BlackHole'
    ],
    planet: ['Mercury', 'Venus', 'Earth', 'Mars', 'Jupiter', 'Saturn', 'Uranus', 'Neptune', 'Pluto', 'PlanetX', 'Ceres', 'Eris'],
    boss: [
        'AmberAcorn', 'CeruleanBell', 'CrimsonHeart', 'VerdantLeaf', 'VioletVessel', 'TheArm', 'TheClub',
        'TheEye', 'TheFish', 'TheFlint', 'TheGoad', 'TheHead', 'TheHook', 'TheHouse', 'TheManacle',
        'TheMark', 'TheMouth', 'TheNeedle', 'TheOx', 'ThePillar', 'ThePlant', 'ThePsychic', 'TheSerpent',
        'TheTooth', 'TheWall', 'TheWater', 'TheWheel', 'TheWindow'
    ]
};

// ================================================
// Filter Builder Functions
// ================================================

// Type-specific field visibility rules
const TYPE_FIELDS = {
    joker:     { edition: true,  shopSlots: true,  packSlots: true,  requireMega: false },
    soulJoker: { edition: true,  shopSlots: false, packSlots: true,  requireMega: true  },
    voucher:   { edition: false, shopSlots: true,  packSlots: false, requireMega: false },
    tag:       { edition: false, shopSlots: false, packSlots: false, requireMega: false },
    tarot:     { edition: false, shopSlots: false, packSlots: true,  requireMega: false },
    spectral:  { edition: false, shopSlots: false, packSlots: true,  requireMega: false },
    planet:    { edition: false, shopSlots: false, packSlots: true,  requireMega: false },
    boss:      { edition: false, shopSlots: false, packSlots: false, requireMega: false }
};

function updateBuilderValues2() {
    const type = document.getElementById('builderType2').value;
    const valueSelect = document.getElementById('builderValue2');
    const items = ITEM_DATA[type] || [];
    valueSelect.innerHTML = items.map(item => `<option value="${item}">${item}</option>`).join('');

    // Update field visibility based on type
    const fields = TYPE_FIELDS[type] || { edition: false, shopSlots: false, packSlots: false, requireMega: false };

    document.getElementById('builderEdition2').parentElement.style.display = fields.edition ? '' : 'none';
    document.getElementById('shopSlotsRow2').style.display = fields.shopSlots ? '' : 'none';
    document.getElementById('packSlotsRow2').style.display = fields.packSlots ? '' : 'none';
    document.getElementById('requireMegaRow2').style.display = fields.requireMega ? '' : 'none';
}

function updateScoreVisibility2() {
    const section = document.getElementById('builderSection2').value;
    document.getElementById('scoreRow2').style.display = section === 'should' ? 'block' : 'none';
}

function newFilterFromBuilder() {
    const name = document.getElementById('builderName').value.trim() || 'My Filter';
    const deck = document.getElementById('builderDeck').value;
    const stake = document.getElementById('builderStake').value;

    const jaml = `name: ${name}
deck: ${deck}
stake: ${stake}
must:
should:
`;

    setJamlValue(jaml);
    switchTab('jaml', document.querySelector('.tab:nth-child(2)'));
    showStatus(`Created new filter: ${name}`);
}

function addClauseFromBuilder() {
    const section = document.getElementById('builderSection2').value;
    const type = document.getElementById('builderType2').value;
    const value = document.getElementById('builderValue2').value;
    const edition = document.getElementById('builderEdition2').value;
    const score = document.getElementById('builderScore2').value;
    const requireMega = document.getElementById('builderRequireMega2').checked;

    // Get antes from the new button-based UI
    const antes = getSelectedAntes();

    const shopCheckboxes = document.querySelectorAll('.shop-cb2:checked');
    const shopSlots = Array.from(shopCheckboxes).map(cb => cb.value);

    const packCheckboxes = document.querySelectorAll('.pack-cb2:checked');
    const packSlots = Array.from(packCheckboxes).map(cb => cb.value);

    if (antes.length === 0) {
        showStatus('Select at least one ante');
        return;
    }

    const fields = TYPE_FIELDS[type] || {};

    let clause = `  - ${type}: ${value}\n`;
    if (fields.edition && edition) clause += `    edition: ${edition}\n`;
    clause += `    antes: [${antes.join(', ')}]\n`;
    if (fields.shopSlots && shopSlots.length > 0) clause += `    shopSlots: [${shopSlots.join(', ')}]\n`;
    if (fields.packSlots && packSlots.length > 0) clause += `    packSlots: [${packSlots.join(', ')}]\n`;
    if (fields.requireMega && requireMega) clause += `    requireMega: true\n`;
    if (section === 'should') clause += `    score: ${score}\n`;

    let jaml = getJamlValue();

    // If JAML is empty or missing name, create base structure from builder inputs
    if (!jaml.trim() || !jaml.match(/^name:/m)) {
        const name = document.getElementById('builderName').value.trim() || 'My Filter';
        const deck = document.getElementById('builderDeck').value;
        const stake = document.getElementById('builderStake').value;
        jaml = `name: ${name}\ndeck: ${deck}\nstake: ${stake}\nmust:\nshould:\nmustNot:\n`;
    }

    // Find where the section is (or where to add it)
    const sectionMatch = jaml.match(new RegExp(`^${section}:`, 'm'));

    if (sectionMatch) {
        // Section exists - insert clause right after the section header
        const sectionStart = jaml.indexOf(sectionMatch[0]);
        const afterHeader = sectionStart + sectionMatch[0].length;
        jaml = jaml.slice(0, afterHeader) + '\n' + clause + jaml.slice(afterHeader).replace(/^\n/, '');
    } else {
        // Section doesn't exist - add before deck/stake or at end
        const deckMatch = jaml.match(/^deck:/m);
        if (deckMatch) {
            const deckIdx = jaml.indexOf(deckMatch[0]);
            jaml = jaml.slice(0, deckIdx) + `${section}:\n${clause}` + jaml.slice(deckIdx);
        } else {
            jaml += `\n${section}:\n${clause}`;
        }
    }

    setJamlValue(jaml.trim());
    switchTab('jaml', document.querySelector('.tab:nth-child(2)'));
    showStatus(`Added ${type}: ${value}`);
}

// ================================================
// Builder: Requirement Button Selection
// ================================================
function setRequirement(type, button) {
    // Update hidden field
    document.getElementById('builderSection2').value = type;

    // Update button active states
    document.querySelectorAll('.req-btn').forEach(btn => btn.classList.remove('active'));
    button.classList.add('active');

    // Show/hide score row based on section type
    document.getElementById('scoreRow2').style.display = type === 'should' ? 'block' : 'none';
}

// ================================================
// Builder: Ante Quick-Select Buttons
// ================================================
function selectAntes(preset) {
    const anteButtons = document.querySelectorAll('.ante-btn');

    anteButtons.forEach(btn => {
        const ante = parseInt(btn.dataset.ante);
        let shouldBeActive = false;

        switch (preset) {
            case 'early':
                shouldBeActive = ante >= 1 && ante <= 3;
                break;
            case 'mid':
                shouldBeActive = ante >= 4 && ante <= 5;
                break;
            case 'late':
                shouldBeActive = ante >= 6 && ante <= 8;
                break;
            case 'all':
                shouldBeActive = true;
                break;
        }

        if (shouldBeActive) {
            btn.classList.add('active');
        } else {
            btn.classList.remove('active');
        }
    });
}

// ================================================
// Builder: Get Selected Antes from Buttons
// ================================================
function getSelectedAntes() {
    const activeButtons = document.querySelectorAll('.ante-btn.active');
    return Array.from(activeButtons).map(btn => btn.dataset.ante);
}

// Initialize builder on page load
document.addEventListener('DOMContentLoaded', () => {
    updateBuilderValues2();

    // Add click handlers for ante buttons
    document.querySelectorAll('.ante-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            btn.classList.toggle('active');
        });
    });
});