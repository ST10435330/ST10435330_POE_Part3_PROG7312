import {request, byId as $, element} from './stream-api.js';

const stateKey = 'smartx-mesh-history';
const limits = [25, 50, 100, 500];
let sensor = null, result = null, serial = 0, pending = false, sampleBusy = false;
let range = {from: '', to: '', take: 50, skip: 0};

try {
    const saved = JSON.parse(sessionStorage.getItem(stateKey) || 'null');
    const validDate = value => typeof value === 'string' && (!value || Number.isFinite(Date.parse(value)));
    if (saved && validDate(saved.from) && validDate(saved.to) &&
        (!saved.from || !saved.to || Date.parse(saved.from) <= Date.parse(saved.to))) {
        range = {from: saved.from, to: saved.to, take: limits.includes(saved.take) ? saved.take : 50, skip: 0};
    }
} catch { /* Invalid saved filters use the default range. */ }

$('meshHistoryFrom').value = range.from;
$('meshHistoryTo').value = range.to;
$('meshHistoryLimit').value = String(range.take);
$('meshHistoryTimezone').textContent = 'Times are shown in ' + Intl.DateTimeFormat().resolvedOptions().timeZone + '. Both search boundaries are inclusive.';

function notify(text, error = false) {
    $('meshHistoryMessage').textContent = text;
    $('meshHistoryMessage').className = error ? 'error' : '';
}

function save() {
    sessionStorage.setItem(stateKey, JSON.stringify({from: range.from, to: range.to, take: range.take}));
}

function controls() {
    $('meshHistoryApply').disabled = !sensor;
    $('meshHistoryRefresh').disabled = !sensor || pending;
    $('meshHistoryOlder').disabled = pending || !result?.hasOlder;
    $('meshHistoryNewer').disabled = pending || !result?.hasNewer;
    $('meshHistoryLatest').disabled = !sensor || pending || range.skip === 0;
    $('meshHistorySample').disabled = !sensor || sampleBusy;
}

export function setHistorySensor(node) {
    const next = node?.sensorId ? node : null;
    if (next?.sensorId === sensor?.sensorId) { sensor = next; return; }
    sensor = next;
    serial++;
    pending = false;
    result = null;
    range.skip = 0;
    $('meshHistorySensor').textContent = sensor ? sensor.identifier + ' · ' + sensor.category : 'Select a sensor to inspect its history.';
    $('meshHistoryRows').replaceChildren();
    $('meshHistoryInfo').textContent = '';
    $('meshHistoryPage').textContent = '';
    $('meshHistoryAlgorithm').textContent = '';
    controls();
    if (sensor) loadHistory(true);
    else notify('Select a sensor to inspect its history.');
}

async function loadHistory(clearRows = false) {
    if (!sensor) return;
    const current = ++serial;
    const id = sensor.sensorId;
    pending = true;
    if (clearRows) {
        result = null;
        $('meshHistoryRows').replaceChildren();
        $('meshHistoryInfo').textContent = '';
        $('meshHistoryPage').textContent = '';
    }
    notify('Loading history for ' + sensor.identifier + '…');
    controls();
    try {
        const query = new URLSearchParams({take: range.take, skip: range.skip});
        if (range.from) query.set('from', new Date(range.from).toISOString());
        if (range.to) query.set('to', new Date(range.to).toISOString());
        const data = await request('/stream/history/' + id + '?' + query);
        if (current !== serial || id !== sensor?.sensorId) return;
        if (data.matched > 0 && range.skip >= data.matched) {
            range.skip = Math.floor((data.matched - 1) / range.take) * range.take;
            return await loadHistory(true);
        }
        if (data.matched === 0) range.skip = 0;
        result = data;
        render();
        notify(data.stored === 0 ?
            'No processed readings yet. Start the sensor demo or send a normal reading below.' :
            data.matched === 0 ? 'No readings match the applied time range.' :
            range.skip === 0 ? 'Latest page refreshes every three seconds.' :
            'Viewing older readings. This page stays still until you refresh or change pages.');
    } catch (error) {
        if (current === serial) notify('History request failed: ' + error.message, true);
    } finally {
        if (current === serial) { pending = false; controls(); }
    }
}

function render() {
    const viewport = $('meshHistoryViewport');
    const scrollTop = viewport.scrollTop;
    $('meshHistoryRows').replaceChildren();
    for (const record of result.records.slice().reverse()) {
        const row = element('tr', '', record.critical ? 'critical-row' : '');
        const timestamp = element('td', new Date(record.timestamp).toLocaleString());
        timestamp.title = record.timestamp;
        const processed = element('td', new Date(record.processedAt).toLocaleString());
        processed.title = record.processedAt;
        row.append(timestamp, element('td', record.value), element('td', record.lane), processed, element('td', record.sequence));
        $('meshHistoryRows').append(row);
    }
    if (!result.records.length) {
        const row = element('tr', '');
        const cell = element('td', result.stored === 0 ? 'No processed readings stored for this sensor.' : 'No readings in this range.');
        cell.colSpan = 5;
        row.append(cell);
        $('meshHistoryRows').append(row);
    }
    viewport.scrollTop = scrollTop;
    $('meshHistoryInfo').textContent = `${result.stored} retained readings · ${result.matched} match the range · ${result.records.length} shown · newest first`;
    $('meshHistoryPage').textContent = `Page ${Math.floor(range.skip / range.take) + 1} of ${Math.max(1, Math.ceil(result.matched / range.take))} · page 1 contains the newest matches`;
    $('meshHistoryAlgorithm').textContent = result.algorithm + ' Query time: ' + Number(result.searchMilliseconds).toFixed(3) + ' ms.';
}

$('meshHistoryFilter').onsubmit = event => {
    event.preventDefault();
    const from = $('meshHistoryFrom').value;
    const to = $('meshHistoryTo').value;
    if ((from && !Number.isFinite(Date.parse(from))) || (to && !Number.isFinite(Date.parse(to)))) {
        notify('Enter valid dates and times. The range has not been applied.', true);
        return;
    }
    if (from && to && Date.parse(from) > Date.parse(to)) {
        notify('From must be before or equal to To. The range has not been applied.', true);
        return;
    }
    const take = Number($('meshHistoryLimit').value);
    range = {from, to, take: limits.includes(take) ? take : 50, skip: 0};
    save();
    loadHistory(true);
};

$('meshHistoryClear').onclick = () => {
    range.from = range.to = '';
    range.skip = 0;
    $('meshHistoryFrom').value = $('meshHistoryTo').value = '';
    save();
    loadHistory(true);
};
$('meshHistoryRefresh').onclick = () => loadHistory();
$('meshHistoryOlder').onclick = () => { range.skip += range.take; return loadHistory(true); };
$('meshHistoryNewer').onclick = () => { range.skip = Math.max(0, range.skip - range.take); return loadHistory(true); };
$('meshHistoryLatest').onclick = () => { range.skip = 0; return loadHistory(true); };
$('meshHistorySample').onclick = async () => {
    if (!sensor || sampleBusy) return;
    const id = sensor.sensorId;
    sampleBusy = true;
    controls();
    try {
        await request('/simulator/' + id + '/sample', {spike: false});
        if (id === sensor?.sensorId) {
            range.skip = 0;
            await loadHistory();
            notify('Normal reading queued through HTTP. It appears after processing if it falls within the applied range.');
        }
    } catch (error) {
        if (id === sensor?.sensorId) notify(error.message, true);
    } finally { sampleBusy = false; controls(); }
};

setInterval(() => {
    if (!$('topology').hidden && sensor && !pending && range.skip === 0) loadHistory();
}, 3000);
window.addEventListener('smartx:navigate', event => {
    if (event.detail === 'topology' && sensor && !pending && range.skip === 0) loadHistory();
});
controls();
