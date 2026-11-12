// Browser helpers of the admin dashboard (IDashboardInterop).

/** "5 minutes ago" / "in 2 hours" in the given locale; null when the browser cannot format it. */
export function relativeTime(value, unit, locale) {
    try {
        return new Intl.RelativeTimeFormat(locale || undefined, { numeric: 'always' }).format(value, unit);
    } catch {
        return null;
    }
}

/** "yesterday" / "today" / "tomorrow" (-1, 0, 1) in the given locale; null when the browser cannot format it. */
export function relativeDay(days, locale) {
    try {
        return new Intl.RelativeTimeFormat(locale || undefined, { numeric: 'auto' }).format(days, 'day');
    } catch {
        return null;
    }
}

/** "5 minutes" / "2 hours" in the given locale; null when the browser cannot format it. */
export function duration(value, unit, locale) {
    try {
        return new Intl.NumberFormat(locale || undefined, { style: 'unit', unit, unitDisplay: 'long' }).format(value);
    } catch {
        return null;
    }
}

/** Copies text to the clipboard; resolves to whether it worked. */
export async function copyText(text) {
    try {
        if (navigator.clipboard?.writeText) {
            await navigator.clipboard.writeText(text);
            return true;
        }
    } catch {
        // Fall through to the selection based copy (insecure contexts, denied permission).
    }

    const area = document.createElement('textarea');
    area.value = text;
    area.setAttribute('readonly', '');
    area.style.position = 'fixed';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    let copied = false;
    try {
        copied = document.execCommand('copy');
    } catch {
        copied = false;
    }
    area.remove();
    return copied;
}

/** Offers text as a file download. Port of the download button of routes/logs/file.tsx. */
export function downloadText(text, fileName) {
    const blob = new Blob([text], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(url);
}

/** Whether the window is scrolled to the end of the document (within a few pixels). */
export function isAtBottom() {
    return window.innerHeight + Math.round(window.scrollY) >= document.documentElement.scrollHeight - 5;
}

export function scrollToBottom() {
    window.scrollTo(0, document.documentElement.scrollHeight);
}
