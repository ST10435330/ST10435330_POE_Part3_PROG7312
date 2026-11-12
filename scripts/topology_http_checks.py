"""Part 3 checks run against the isolated server started by http_checks.py."""
from datetime import datetime, timezone, timedelta
from urllib.parse import urlencode
from uuid import uuid4
import time


def run(request, check, register, send, wait_receipt, packet):
    def route(identifier):
        return request('/api/topology/route?' + urlencode({'identifier': identifier}))

    def node(snapshot, sensor_id):
        return next(n for n in snapshot['nodes'] if n['sensorId'] == sensor_id)

    def values(result):
        return [row['numericValue'] for row in result['records']]

    def history(sensor, **parameters):
        return request('/api/stream/history/' + sensor['id'] + '?' + urlencode(parameters))

    for filename in ['topology.js', 'topology-history.js', 'topology.css']:
        check(len(request('/' + filename)) > 100, filename + ' is served')
    landing = request('/')
    check(
        b'id="topology"' in landing and b'id="meshHistoryRows"' in landing,
        'network view and selected-sensor history are included in the client')

    request('/api/stream/processing', {'running': True})
    seeds = request('/api/sensors?take=8')
    readings = {
        'Environmental': ('environmental', 50),
        'Power Consumption': ('power', 650),
        'Actuator': ('actuator', False),
    }
    for sensor in seeds:
        request('/api/simulator/' + sensor['id'] + '/pause', {'paused': False})
        kind, value = readings[sensor['category']]
        wait_receipt(send(kind, packet(sensor, value)))

    mesh = request('/api/topology')
    names = {n['identifier'] for n in mesh['nodes']}
    reachable = set(mesh['tree']['reachable'])
    unreachable = set(mesh['tree']['unreachable'])
    offline = set(mesh['tree']['offlineNodes'])
    gateway = mesh['gateway']

    check(
        len(mesh['nodes']) == request('/api/summary')['sensors'] + 1 and
        all(node(mesh, sensor['id'])['identifier'] == sensor['identifier'] for sensor in seeds),
        'topology uses registered sensor identities plus one gateway')
    check(
        all(link['from'] in names and link['to'] in names and link['cost'] > 0
            for link in mesh['links']),
        'configured mesh connections reference valid nodes with positive costs')
    check(
        gateway in reachable and set(mesh['traversal']) == reachable and
        not (reachable & unreachable or reachable & offline or unreachable & offline) and
        reachable | unreachable | offline == names,
        'BFS and MST agree on reachability and distinguish offline nodes')
    check(
        len(mesh['tree']['links']) == len(reachable) - 1 and
        sum(link['cost'] for link in mesh['tree']['links']) == mesh['tree']['totalCost'] and
        all(link['from'] in reachable and link['to'] in reachable
            for link in mesh['tree']['links']),
        'MST response has consistent edges, membership and total cost')

    source = seeds[4]
    original = route(source['identifier'])
    configured = {frozenset((link['from'], link['to'])) for link in mesh['links']}

    def valid_path(path):
        return (bool(path) and path[0] == source['identifier'] and path[-1] == gateway and
                len(path) == len(set(path)) and
                all(frozenset((a, b)) in configured for a, b in zip(path, path[1:])))

    check(
        original['sensorId'] == source['id'] and original['online'] and
        valid_path(original['bfsPath']) and valid_path(original['mstPath']),
        'identifier lookup returns valid sensor-to-gateway BFS and tree paths')
    check(
        route(source['identifier'].lower())['identifier'] == source['identifier'],
        'identifier lookup is case insensitive')

    relay_name = original['bfsPath'][1]
    relay = next(sensor for sensor in seeds if sensor['identifier'] == relay_name)
    failure = request('/api/topology/failure', {'sensorId': relay['id'], 'failed': True})
    rerouted = route(source['identifier'])
    check(
        relay_name in failure['tree']['offlineNodes'] and
        node(failure, relay['id'])['status'] == 'Simulated failure' and
        valid_path(rerouted['bfsPath']) and valid_path(rerouted['mstPath']) and
        rerouted['bfsPath'] != original['bfsPath'] and
        relay_name not in rerouted['bfsPath'] and relay_name not in rerouted['mstPath'],
        'relay failure removes it from routing and finds an alternative path')

    before = history(source)['records']
    request('/api/topology/failure', {'sensorId': source['id'], 'failed': True})
    down = route(source['identifier'])
    check(
        not down['online'] and down['bfsPath'] == [] and down['mstPath'] == [] and
        history(source)['records'] == before,
        'failed sensors have no route while their stored history remains readable')

    request('/api/topology/failure', {'sensorId': relay['id'], 'failed': False})
    request('/api/topology/failure', {'sensorId': source['id'], 'failed': False})
    check(
        route(source['identifier'])['bfsPath'] == original['bfsPath'],
        'clearing failures restores the original fewest-hop route')

    request('/api/simulator/' + source['id'] + '/pause', {'paused': True})
    paused = request('/api/topology')
    check(
        node(paused, source['id'])['status'] == 'Paused' and
        route(source['identifier'])['bfsPath'] == [],
        'paused simulator devices are excluded from mesh routes')
    request('/api/simulator/' + source['id'] + '/pause', {'paused': False})

    request('/api/topology/route', expected=400)
    request('/api/topology/route?identifier=UNKNOWN-NODE', expected=404)
    request('/api/topology/failure', {'sensorId': source['id']}, expected=400)
    request('/api/topology/failure', {'failed': True}, expected=400)
    request('/api/topology/failure', {'sensorId': source['id'], 'failed': 'yes'}, expected=400)
    request('/api/topology/failure', {'sensorId': str(uuid4()), 'failed': True}, expected=404)
    check(True, 'topology endpoints reject malformed controls and unknown identities')

    mac = register('AA:BB:CC:DD:EE:03')
    check(
        route('aa:bb:cc:dd:ee:03')['sensorId'] == mac['id'],
        'URL-encoded MAC-address lookup resolves the registered sensor')

    paged = register('HTTP-HISTORY-PAGING')
    start = datetime.now(timezone.utc) - timedelta(seconds=10)
    rows = [{'sensorId': paged['id'],
             'timestamp': (start + timedelta(milliseconds=i * 20)).isoformat(),
             'value': 40 + i} for i in range(7)]
    batch = request('/api/telemetry/environmental/batch', {'batches': [rows[:3], rows[3:]]})
    for receipt in batch['receipts']:
        wait_receipt(receipt)
    check(batch['queued'] == 7 and batch['rejected'] == 0, 'history paging fixture passes through HTTP ingestion')

    latest = history(paged, take=3)
    middle = history(paged, take=3, skip=3)
    oldest = history(paged, take=3, skip=6)
    check(
        values(latest) == [44, 45, 46] and values(middle) == [41, 42, 43] and
        values(oldest) == [40] and latest['stored'] == latest['matched'] == 7 and
        len({record['sequence'] for page in [latest, middle, oldest] for record in page['records']}) == 7,
        'paged history returns every stored reading without overlap')
    check(
        latest['hasOlder'] and not latest['hasNewer'] and
        middle['hasOlder'] and middle['hasNewer'] and
        not oldest['hasOlder'] and oldest['hasNewer'],
        'history paging navigation flags match the page boundaries')

    bounded = history(paged, take=2, **{'from': rows[1]['timestamp'], 'to': rows[5]['timestamp']})
    older = history(paged, take=2, skip=2, **{'from': rows[1]['timestamp'], 'to': rows[5]['timestamp']})
    exact = history(paged, **{'from': rows[3]['timestamp'], 'to': rows[3]['timestamp']})
    check(
        bounded['matched'] == 5 and values(bounded) == [44, 45] and
        values(older) == [42, 43] and values(exact) == [43],
        'inclusive timestamp ranges are filtered before paging')
    check(
        values(history(paged, **{'from': rows[4]['timestamp']})) == [44, 45, 46] and
        values(history(paged, **{'to': rows[2]['timestamp']})) == [40, 41, 42],
        'history supports open-ended date ranges')
    empty = history(paged, **{'from': (start + timedelta(seconds=5)).isoformat()})
    check(
        empty['records'] == [] and empty['matched'] == 0 and empty['stored'] == 7 and
        history(paged, take=3, skip=100)['records'] == [],
        'empty time ranges and offsets beyond the history return empty records')
    request('/api/stream/history/' + paged['id'] + '?' + urlencode({
        'from': rows[5]['timestamp'], 'to': rows[1]['timestamp']}), expected=400)
    request('/api/stream/history/' + paged['id'] + '?from=not-a-date', expected=400)
    request('/api/stream/history/' + str(uuid4()), expected=404)
    check(True, 'history rejects inverted or malformed dates and unknown sensors')

    fast = register('HTTP-MESH-HEALTH', interval=1)
    wait_receipt(send('environmental', packet(fast, 50)))
    check(node(request('/api/topology'), fast['id'])['online'], 'fresh telemetry makes a sensor mesh-eligible')
    time.sleep(3.5)
    expired = node(request('/api/topology'), fast['id'])
    check(
        not expired['online'] and expired['status'] == 'Disconnected',
        'reporting timeout automatically excludes a sensor from the mesh')
    wait_receipt(send('environmental', packet(fast, 50)))
    check(
        node(request('/api/topology'), fast['id'])['online'],
        'fresh telemetry restores mesh eligibility after timeout')
