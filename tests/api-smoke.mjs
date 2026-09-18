// Run against a disposable local demo: this test resets its in-memory fleet.
// Start the API, then run: node tests/api-smoke.mjs
import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';

const base = process.env.ELEVATOR_API_URL ?? 'http://localhost:5080';
async function call(method, path, body, expected = 200) {
    const response = await fetch(base + path, {
        method, headers: body === undefined ? {} : { 'content-type': 'application/json' },
        body: body === undefined ? undefined : JSON.stringify(body),
        signal: AbortSignal.timeout(60000)
    });
    const text = await response.text();
    assert.equal(response.status, expected, `${method} ${path}: ${text}`);
    return text ? JSON.parse(text) : null;
}
const submit = (overrides = {}) => call('POST', '/requests', {
    pickupFloor: 1, destinationFloor: 20, kind: 'Passenger', weightKg: 75, ...overrides
}, 201);

const swagger = await call('GET', '/swagger/v1/swagger.json');
assert.ok(swagger.paths['/simulation/process']);
assert.equal(swagger.components.schemas.CreateTripCommand.example.pickupFloor, 3);
assert.deepEqual(swagger.components.schemas.TransportKind.enum, ['Passenger', 'Freight']);
const ui = await fetch(base + '/swagger/index.html');
assert.equal(ui.status, 200);
assert.match(await ui.text(), /SwaggerUIBundle/);

await call('POST', '/simulation/reset');
const trip = await submit({ pickupFloor: 3, destinationFloor: 9 });
assert.equal(trip.state, 'Waiting');
assert.equal((await call('GET', `/trips/${trip.id}`)).id, trip.id);
assert.equal((await call('GET', '/elevators')).length, 3);
await call('POST', '/requests', { pickupFloor: 99, destinationFloor: 2 }, 400);
await call('POST', '/requests', { pickupFloor: 1, destinationFloor: 1 }, 400);
await call('POST', '/requests', { pickupFloor: 1, destinationFloor: 20, allowedFloors: [1, 2], isVip: true }, 403);
await call('POST', '/requests', { pickupFloor: 1, destinationFloor: 10, kind: 'Freight', weightKg: 3001 }, 400);
await call('POST', '/requests', { kind: 'Unknown' }, 400);
await call('POST', '/requests', { kind: 0 }, 400);
await call('POST', '/requests', { allowedFloors: null }, 400);
await call('POST', '/elevators/99/resume', undefined, 404);
await call('POST', '/elevators/0/resume', undefined, 409);
assert.equal((await call('GET', '/analytics')).submitted, 1);
assert.equal((await call('POST', '/simulation/process')).analytics.completed, 1);

await call('POST', '/simulation/reset');
await call('POST', '/elevators/0/maintenance');
await submit({ destinationFloor: 15 });
let status = await call('POST', '/simulation/process');
assert.equal(status.analytics.completed, 1);
assert.equal((await call('GET', '/trips'))[0].elevatorId, 1);
assert.equal(status.elevators[1].floor, 15);

await call('POST', '/simulation/reset');
await submit({ destinationFloor: 10, kind: 'Freight', weightKg: 1500 });
await call('POST', '/simulation/tick');
await call('POST', '/elevators/2/emergency-stop');
status = await call('POST', '/simulation/process');
assert.equal(status.analytics.pending, 1);
assert.equal((await call('GET', '/trips'))[0].state, 'Onboard');
assert.equal(status.elevators[2].mode, 'EmergencyStopped');
await call('POST', '/elevators/2/resume');
assert.equal((await call('POST', '/simulation/process')).analytics.completed, 1);

await call('POST', '/simulation/reset');
const trips = await Promise.all(Array.from({ length: 128 }, (_, i) => submit({ pickupFloor: i % 10 + 1 })));
await Promise.all([call('POST', '/simulation/process'), call('POST', '/simulation/process')]);
const analytics = await call('GET', '/analytics');
assert.equal(analytics.completed, 128);
assert.equal(analytics.pending, 0);
assert.equal(new Set(trips.map(t => t.id)).size, 128);
assert.equal((await call('GET', '/events')).length, 1000);

const directory = new URL('../src/ElevatorSystem.Api/logs/', import.meta.url);
const files = (await readdir(directory)).filter(f => f.endsWith('.txt'));
const log = (await Promise.all(files.map(f => readFile(new URL(f, directory), 'utf8')))).join('\n');
for (const trip of trips) {
    const completed = log.split('\n').filter(line => line.includes('event=PassengerDroppedOff') && line.includes(trip.id));
    assert.equal(completed.length, 1, `Missing or duplicated file event for ${trip.id}`);
}
assert.match(log, /SimulationReset/);
assert.match(log, /Command rejected/);
await call('POST', '/simulation/reset');
console.log('API smoke passed: Swagger, JSON/errors, shared fleet, express, emergency/resume, 128 concurrent submissions, complete TXT logs; fleet reset.');
