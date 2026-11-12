import {request, byId as $, element} from './stream-api.js';

let mesh = null, route = null, page = 0;
let selected = sessionStorage.getItem('smartx-mesh-selected') || '';
let busy = false, queued = false, actionBusy = false, refreshFailed = false;
const pageSize = 25;
const edgeKey = (a, b) => JSON.stringify([a, b].sort());
const visible = () => !$('topology').hidden;

function notify(text, error = false) {
    $('meshMessage').textContent = text;
    $('meshMessage').className = error ? 'error' : '';
}

function selectedNode() {
    return mesh?.nodes.find(node => node.identifier === selected && node.sensorId);
}

function selectedPath() {
    return ($('meshRouteMode').value === 'mst' ? route?.mstPath : route?.bfsPath) || [];
}

function relayNode() {
    const path = selectedPath();
    return path.length > 2 ? mesh.nodes.find(node => node.identifier === path[1]) : null;
}

function choose(identifier) {
    selected = identifier;
    route = null;
    sessionStorage.setItem('smartx-mesh-selected', selected);
    renderRegistry();
    renderSelection();
    drawGraph();
    refresh();
}

async function refresh() {
    if (!visible()) return;
    if (busy) { queued = true; return; }
    busy = true;
    try {
        do {
            queued = false;
            const next = await request('/topology');
            if (!visible()) break;
            mesh = next;
            if (!selectedNode()) {
                selected = mesh.nodes.find(node => node.sensorId && node.online)?.identifier ||
                    mesh.nodes.find(node => node.sensorId)?.identifier || '';
                sessionStorage.setItem('smartx-mesh-selected', selected);
            }
            const current = selected;
            const nextRoute = current ? await request('/topology/route?identifier=' + encodeURIComponent(current)) : null;
            if (current !== selected) { queued = true; continue; }
            if (!visible()) break;
            route = nextRoute;
            renderMetrics();
            renderRegistry();
            renderSelection();
            drawGraph();
            $('meshUpdated').textContent = 'Last updated ' + new Date(mesh.generatedAt).toLocaleTimeString();
            if (refreshFailed) notify('Topology connection restored.');
            refreshFailed = false;
        } while (queued && visible());
    } catch (error) {
        refreshFailed = true;
        notify('Topology refresh failed: ' + error.message, true);
    } finally {
        busy = false;
    }
}

function renderMetrics() {
    const sensors = mesh.nodes.filter(node => node.sensorId);
    $('meshMetrics').replaceChildren();
    for (const [label, value] of [
        ['Registered sensors', sensors.length],
        ['Reachable sensors', sensors.filter(node => node.reachable).length],
        ['Offline sensors', sensors.filter(node => !node.online).length],
        ['Online but unreachable', sensors.filter(node => node.online && !node.reachable).length],
        ['MST total link cost', mesh.tree.totalCost],
        ['Configured mesh links', mesh.links.length]
    ]) {
        const box = element('div', '', 'metric');
        box.append(element('b', Number(value).toLocaleString()), element('span', label));
        $('meshMetrics').append(box);
    }
}

function renderRegistry() {
    if (!mesh) return;
    const search = $('meshSearch').value.trim().toLowerCase();
    const matches = mesh.nodes.filter(node => node.sensorId &&
        node.identifier.toLowerCase().includes(search) &&
        (!$('meshOnlyOnline').checked || node.online));
    page = Math.max(0, Math.min(page, Math.ceil(matches.length / pageSize) - 1));
    const offset = page * pageSize;
    $('meshDevices').replaceChildren();
    for (const node of matches.slice(offset, offset + pageSize)) {
        const button = element('button', node.identifier, node.identifier === selected ? 'selected' : '');
        button.type = 'button';
        button.setAttribute('aria-pressed', String(node.identifier === selected));
        button.append(element('small', node.category + ' · ' + node.status));
        button.onclick = () => choose(node.identifier);
        $('meshDevices').append(button);
    }
    if (!matches.length) $('meshDevices').append(element('p', 'No matching sensors.'));
    $('meshMatchCount').textContent = `${matches.length} matches · page ${page + 1} of ${Math.max(1, Math.ceil(matches.length / pageSize))}`;
    $('meshPrevious').disabled = page === 0;
    $('meshNext').disabled = offset + pageSize >= matches.length;
}

function renderSelection() {
    const node = selectedNode();
    $('meshSelectedTitle').textContent = node?.identifier || 'Select a sensor';
    $('meshSelectedStatus').textContent = node ?
        `${node.category} · Mesh: ${node.status} · Telemetry: ${node.telemetryStatus} · ${node.reachable ? 'Gateway reachable' : 'Gateway unreachable'}` : '';
    $('meshLastReceived').textContent = node?.lastReceived ?
        'Last received ' + new Date(node.lastReceived).toLocaleString() : 'No reception timestamp.';
    $('meshPaths').replaceChildren();
    if (node && !route) $('meshPaths').append(element('p', 'Loading routes…'));
    if (route) {
        for (const [label, path] of [['BFS — fewest hops', route.bfsPath], ['MST — path through the minimum-cost tree', route.mstPath]]) {
            $('meshPaths').append(element('p', path.length ?
                `${label}: ${path.join(' → ')} (${path.length - 1} hops)` :
                `${label}: No available route.`));
        }
    }
    $('meshFail').disabled = actionBusy || !node || node.status === 'Simulated failure';
    $('meshRestore').disabled = actionBusy || !node || node.status !== 'Simulated failure';
    const relay = route ? relayNode() : null;
    $('meshFailRelay').disabled = actionBusy || !relay;
    $('meshFailRelay').textContent = relay ? 'Fail relay ' + relay.identifier : 'Fail next relay';
}

function drawGraph() {
    if (!mesh) return;
    const svg = $('meshGraph');
    const focus = document.activeElement?.getAttribute('data-mesh-node');
    svg.replaceChildren();
    const ns = 'http://www.w3.org/2000/svg';
    function add(tag, attributes, text, parent = svg) {
        const node = document.createElementNS(ns, tag);
        for (const [key, value] of Object.entries(attributes)) node.setAttribute(key, String(value));
        if (text !== undefined) node.textContent = text;
        parent.append(node);
        return node;
    }
    const path = selectedPath();
    const wanted = new Set([mesh.gateway, ...path]);
    if (selected) wanted.add(selected);
    const limit = Math.max(40, wanted.size);
    for (const link of mesh.links) {
        if (wanted.size >= limit) break;
        if (link.from === selected) wanted.add(link.to);
        if (wanted.size >= limit) break;
        if (link.to === selected) wanted.add(link.from);
    }
    for (const node of [...mesh.nodes.filter(node => node.online), ...mesh.nodes]) {
        if (wanted.size >= limit) break;
        wanted.add(node.identifier);
    }
    const nodes = mesh.nodes.filter(node => wanted.has(node.identifier));
    const positions = new Map();
    positions.set(mesh.gateway, {x: 500, y: 55});
    let index = 0;
    for (const node of nodes.filter(node => node.sensorId)) {
        positions.set(node.identifier, {x: 80 + index % 7 * 140, y: 150 + Math.floor(index / 7) * 75});
        index++;
    }
    svg.setAttribute('viewBox', `0 0 1000 ${Math.max(600, 180 + Math.ceil(index / 7) * 75)}`);
    const mst = new Set(mesh.tree.links.map(link => edgeKey(link.from, link.to)));
    const highlighted = new Set(path.slice(1).map((node, i) => edgeKey(path[i], node)));
    const online = new Set(mesh.nodes.filter(node => node.online).map(node => node.identifier));
    const links = mesh.links.filter(link => positions.has(link.from) && positions.has(link.to));
    links.sort((a, b) => Number(highlighted.has(edgeKey(a.from, a.to))) - Number(highlighted.has(edgeKey(b.from, b.to))));
    for (const link of links) {
        const from = positions.get(link.from), to = positions.get(link.to);
        const key = edgeKey(link.from, link.to), chosen = highlighted.has(key);
        const active = online.has(link.from) && online.has(link.to);
        const line = add('line', {x1: from.x, y1: from.y, x2: to.x, y2: to.y,
            stroke: chosen ? '#c45e00' : active && mst.has(key) ? '#26786b' : '#c2cbd5',
            'stroke-width': chosen ? 4 : active && mst.has(key) ? 2 : 1,
            'stroke-dasharray': active ? 'none' : '4 5'});
        add('title', {}, `${link.from} ↔ ${link.to} · cost ${link.cost}`, line);
        if (chosen) add('text', {x: (from.x + to.x) / 2, y: (from.y + to.y) / 2 - 5, class: 'mesh-cost'}, link.cost);
    }
    for (const node of nodes) {
        const point = positions.get(node.identifier);
        const group = add('g', {class: 'mesh-node', 'data-mesh-node': node.identifier});
        if (node.sensorId) {
            group.setAttribute('role', 'button');
            group.setAttribute('tabindex', '0');
            group.setAttribute('aria-label', `Select ${node.identifier}, ${node.status}`);
            group.onclick = () => choose(node.identifier);
            group.onkeydown = event => {
                if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); choose(node.identifier); }
            };
        }
        const colour = !node.sensorId ? '#386c9d' : !node.online ? '#b64034' : node.reachable ? '#26786b' : '#ae7a00';
        add('circle', {cx: point.x, cy: point.y, r: node.sensorId ? 11 : 17, fill: colour,
            stroke: node.identifier === selected ? '#111' : '#fff', 'stroke-width': node.identifier === selected ? 4 : 2}, undefined, group);
        add('text', {x: point.x, y: point.y + 29, 'text-anchor': 'middle', 'font-size': 11, fill: '#222'},
            node.identifier.length > 21 ? node.identifier.slice(0, 19) + '…' : node.identifier, group);
        add('title', {}, `${node.identifier} · ${node.status} · ${node.telemetryStatus}`, group);
        if (focus === node.identifier && node.sensorId) group.focus({preventScroll: true});
    }
    $('meshGraphScope').textContent = `Showing ${nodes.length} of ${mesh.nodes.length} nodes, including the selected route and nearby nodes. Metrics and routing use the complete network. Link labels show simulated costs.`;
}

async function act(operation) {
    if (actionBusy) return;
    actionBusy = true;
    $('meshStart').disabled = $('meshStop').disabled = true;
    renderSelection();
    try { await operation(); }
    catch (error) { notify(error.message, true); }
    finally {
        actionBusy = false;
        $('meshStart').disabled = $('meshStop').disabled = false;
        await refresh();
        renderSelection();
    }
}

async function setFailure(node, failed) {
    if (!node) throw Error('Select a sensor with an available relay.');
    await request('/topology/failure', {sensorId: node.sensorId, failed});
    notify(`${node.identifier}: ${failed ? 'simulated failure enabled' : 'simulated failure cleared'}.`);
}

$('meshSearch').addEventListener('input', () => { page = 0; renderRegistry(); });
$('meshOnlyOnline').onchange = () => { page = 0; renderRegistry(); };
$('meshFind').onsubmit = event => {
    event.preventDefault();
    if (!mesh) return;
    const identifier = $('meshSearch').value.trim().toLowerCase();
    const match = mesh.nodes.find(node => node.sensorId && node.identifier.toLowerCase() === identifier);
    if (match) choose(match.identifier);
    else notify('Select a result below, or enter an exact identifier or MAC address.');
};
$('meshPrevious').onclick = () => { page--; renderRegistry(); };
$('meshNext').onclick = () => { page++; renderRegistry(); };
$('meshRouteMode').onchange = () => { renderSelection(); drawGraph(); };
$('meshRefresh').onclick = () => refresh();
$('meshFail').onclick = () => act(() => setFailure(selectedNode(), true));
$('meshRestore').onclick = () => act(() => setFailure(selectedNode(), false));
$('meshFailRelay').onclick = () => { const relay = relayNode(); return act(() => setFailure(relay, true)); };
$('meshStart').onclick = () => act(async () => {
    await request('/stream/simulation', {running: true, devices: 20});
    notify('The first 20 registered sensors are sending HTTP telemetry. Other sensors time out unless they transmit.');
});
$('meshStop').onclick = () => act(async () => {
    const simulation = await request('/stream/simulation');
    await request('/stream/simulation', {running: false, devices: simulation.devices});
    notify('HTTP simulator stopped. Sensors become disconnected after their reporting timeout.');
});
window.addEventListener('smartx:navigate', event => { if (event.detail === 'topology') refresh(); });
setInterval(() => { if (visible() && !actionBusy) refresh(); }, 3000);
if (visible()) refresh();
