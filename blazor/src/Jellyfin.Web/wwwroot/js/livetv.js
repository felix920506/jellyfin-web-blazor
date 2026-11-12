// The DOM work of the TV guide (components/guide/guide.js): keeping the time header in step with the
// programme grid, sliding programme names into view while the grid scrolls sideways, sizing the
// guide to the window, and the clicks and arrow keys of the programme cells, which are plain markup
// and have no handlers of their own.

const states = new WeakMap();

function now() {
    return Date.now();
}

// The guide fills the window below its top edge; its rows scroll inside it (the original pins the
// tab with position: absolute instead).
function fit(state) {
    const top = state.root.getBoundingClientRect().top + window.scrollY;
    state.root.style.height = Math.max(240, window.innerHeight - top) + 'px';
}

function programCells(state) {
    if (!state.cells) {
        state.cells = Array.from(state.grid.querySelectorAll('[is=emby-programcell]'));
    }
    return state.cells;
}

// updateProgramCellOnScroll: a cell that starts left of the visible part shows its name at the
// left edge of the grid, with a caret that says the programme began earlier.
function updateProgramCell(cell, scrollPct) {
    let left = cell.posLeft;
    if (!left) {
        if (!cell.style.left) {
            return;
        }
        left = parseFloat(cell.style.left.replace('%', ''));
        cell.posLeft = left;
    }
    let width = cell.posWidth;
    if (!width) {
        width = parseFloat(cell.style.width.replace('%', ''));
        cell.posWidth = width;
    }
    const right = left + width;
    const newPct = Math.max(Math.min(scrollPct, right), left);
    const pctOfWidth = ((newPct - left) / width) * 100;

    if (cell.guideProgramName === undefined) {
        cell.guideProgramName = cell.querySelector('.guideProgramName');
        cell.caret = cell.querySelector('.guide-programNameCaret');
    }
    const name = cell.guideProgramName;
    if (name) {
        if (pctOfWidth > 0 && pctOfWidth <= 100) {
            name.style.transform = 'translateX(' + pctOfWidth + '%)';
            cell.caret?.classList.remove('hide');
        } else {
            name.style.transform = 'none';
            cell.caret?.classList.add('hide');
        }
    }
}

function updateProgramCells(state) {
    if (state.updating) {
        return;
    }
    state.updating = true;
    requestAnimationFrame(() => {
        const grid = state.grid;
        const scrollLeft = grid.scrollLeft;
        const scrollPct = scrollLeft ? (scrollLeft / grid.scrollWidth) * 100 : 0;
        for (const cell of programCells(state)) {
            updateProgramCell(cell, scrollPct);
        }
        state.updating = false;
    });
}

function onGridScroll(state) {
    if (now() - state.lastHeaderScroll >= 1000) {
        state.lastGridScroll = now();
        state.headers.scrollLeft = state.grid.scrollLeft;
    }
    updateProgramCells(state);
}

function onHeaderScroll(state) {
    if (now() - state.lastGridScroll >= 1000) {
        state.lastHeaderScroll = now();
        state.grid.scrollLeft = state.headers.scrollLeft;
    }
}

function onClick(state, e) {
    const cell = e.target.closest('.programCell');
    if (cell && state.grid.contains(cell) && cell.dataset.id) {
        state.dotnet.invokeMethodAsync('OnProgramClick', cell.dataset.id);
    }
}

// The cell of a row that covers a horizontal position of the grid (in percent of its width).
function cellAt(row, pct) {
    let best = null;
    for (const cell of row.children) {
        const left = parseFloat(cell.style.left) || 0;
        const width = parseFloat(cell.style.width) || 0;
        if (left <= pct && pct < left + width) {
            return cell;
        }
        if (left <= pct) {
            best = cell;
        }
    }
    return best || row.firstElementChild;
}

// Arrow keys move between programme cells. The original has this for the TV layout only (through
// its focus manager); the port keeps the part that helps a keyboard on the desktop.
function onKeyDown(state, e) {
    const cell = e.target.closest?.('.programCell');
    if (!cell || !state.grid.contains(cell) || e.altKey || e.ctrlKey || e.metaKey) {
        return;
    }
    let target = null;
    switch (e.key) {
        case 'ArrowLeft':
            target = cell.previousElementSibling;
            break;
        case 'ArrowRight':
            target = cell.nextElementSibling;
            break;
        case 'ArrowUp':
        case 'ArrowDown': {
            const row = e.key === 'ArrowUp' ? cell.parentElement.previousElementSibling : cell.parentElement.nextElementSibling;
            if (row) {
                const grid = state.grid;
                const scrollPct = grid.scrollWidth ? (grid.scrollLeft / grid.scrollWidth) * 100 : 0;
                target = cellAt(row, Math.max(parseFloat(cell.style.left) || 0, scrollPct));
            }
            break;
        }
        default:
            return;
    }
    e.preventDefault();
    if (target) {
        target.focus();
    }
}

/** Starts the guide below `root`; `dotnet` receives OnProgramClick(programId). */
export function attach(root, dotnet) {
    detach(root);
    const grid = root.querySelector('.programGrid');
    const headers = root.querySelector('.timeslotHeaders');
    if (!grid || !headers) {
        return false;
    }
    const state = { root, grid, headers, dotnet, cells: null, lastGridScroll: 0, lastHeaderScroll: 0, updating: false, abort: new AbortController() };
    const signal = state.abort.signal;
    grid.addEventListener('scroll', () => onGridScroll(state), { passive: true, signal });
    headers.addEventListener('scroll', () => onHeaderScroll(state), { passive: true, signal });
    root.addEventListener('click', e => onClick(state, e), { signal });
    root.addEventListener('keydown', e => onKeyDown(state, e), { signal });
    window.addEventListener('resize', () => fit(state), { signal });
    states.set(root, state);
    fit(state);
    return true;
}

/** Stops the guide below `root`. */
export function detach(root) {
    const state = states.get(root);
    if (state) {
        state.abort.abort();
        states.delete(root);
    }
}

/**
 * Called after the rows changed: forgets the cells it knew, and with `fraction` (0..1 of the day
 * the grid shows) scrolls the grid there (scrollProgramGridToTimeMs).
 */
export function refresh(root, fraction) {
    const state = states.get(root);
    if (!state) {
        return;
    }
    state.cells = null;
    fit(state);
    if (typeof fraction === 'number') {
        const grid = state.grid;
        state.lastHeaderScroll = 0;
        grid.scrollTop = 0;
        grid.scrollLeft = Math.max(0, fraction) * grid.scrollWidth;
        state.headers.scrollLeft = grid.scrollLeft;
    }
    updateProgramCells(state);
}

/** How far the grid is scrolled sideways, as a fraction of its width. */
export function scrollFraction(root) {
    const grid = states.get(root)?.grid;
    return grid && grid.scrollWidth ? grid.scrollLeft / grid.scrollWidth : 0;
}
