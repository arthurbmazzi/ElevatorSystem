// Run against a fresh disposable API instance with Elevators:StepIntervalMilliseconds=10.
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
assert.ok(Object.keys(swagger.paths).every(path => !path.startsWith('/simulation')));
assert.equal(swagger.components.schemas.CreateTripCommand.example.pickupFloor, 3);
assert.deepEqual(swagger.components.schemas.TransportKind.enum, ['Passenger', 'Freight']);
const ui = await fetch(base + '/swagger/index.html');
assert.equal(ui.status, 200);
assert.match(await ui.text(), /SwaggerUIBundle/);


async function waitUntil(check) {
    const deadline = Date.now() + 120000;
    while (Date.now() < deadline) {
        if (await check()) return;
        await new Promise(resolve => setTimeout(resolve, 50));
    }
    throw new Error('Automatic processing did not reach the expected state.');
}
const initial = await call('GET', '/analytics');
assert.equal(initial.submitted, 0, 'Run this smoke test against a fresh API instance.');
const sampleDirectory = new URL('../samples/', import.meta.url);
const sampleFiles = (await readdir(sampleDirectory)).filter(f => f.endsWith('.json')).sort();
const samples = await Promise.all(sampleFiles.map(f => readFile(new URL(f, sampleDirectory), 'utf8').then(JSON.parse)));
const schemaKeys = Object.keys(swagger.components.schemas.CreateTripCommand.example).sort();
for (const sample of samples) assert.deepEqual(Object.keys(sample).sort(), schemaKeys);
for (const index of [0, 1, 2]) await call('POST', '/requests', samples[index], 201);
await waitUntil(async () => (await call('GET', '/analytics')).completed === 3);
for (const [index, status] of [[4, 400], [5, 400], [6, 403]])
    await call('POST', '/requests', samples[index], status);
assert.equal((await call('GET', '/analytics')).submitted, 3);
await call('POST', '/requests', { kind: 'Unknown' }, 400);
await call('POST', '/elevators/99/resume', undefined, 404);
await call('POST', '/elevators/0/resume', undefined, 409);

await call('POST', '/elevators/0/maintenance');
const express = await call('POST', '/requests', samples[3], 201);
await waitUntil(async () => (await call('GET', '/trips/' + express.id)).state === 'Completed');
assert.equal((await call('GET', '/trips/' + express.id)).elevatorId, 1);
await call('POST', '/elevators/0/resume');

await call('POST', '/elevators/2/emergency-stop');
const cargo = await call('POST', '/requests', samples[2], 201);
await new Promise(resolve => setTimeout(resolve, 100));
assert.equal((await call('GET', '/trips/' + cargo.id)).state, 'Waiting');
await call('POST', '/elevators/2/resume');
await waitUntil(async () => (await call('GET', '/trips/' + cargo.id)).state === 'Completed');

await call('POST', '/elevators/1/maintenance');
const fifoTrips = [];
for (const index of [7, 8]) fifoTrips.push(await call('POST', '/requests', samples[index], 201));
await waitUntil(async () => (await call('GET', '/trips/' + fifoTrips[1].id)).state === 'Completed');
const fifoEvents = (await call('GET', '/events')).filter(e =>
    fifoTrips.some(t => t.id === e.requestId) && ['PassengerPickedUp', 'PassengerDroppedOff'].includes(e.name));
assert.deepEqual(fifoEvents.map(e => e.floor), [1, 12, 1, 3]);
await call('POST', '/elevators/1/resume');

const baseline = (await call('GET', '/analytics')).completed;
const trips = await Promise.all(Array.from({ length: 128 }, (_, i) => submit({ pickupFloor: i % 10 + 1 })));
await waitUntil(async () => (await call('GET', '/analytics')).completed === baseline + 128);
assert.equal((await call('GET', '/analytics')).pending, 0);
const status = await call('GET', '/logs/status');
const files = (await readdir(status.directory)).filter(f => f.endsWith('.txt'));
const path = await import('node:path');
const log = (await Promise.all(files.map(f => readFile(path.join(status.directory, f), 'utf8')))).join('\n');
for (const trip of trips)
    assert.equal(log.split('\n').filter(line => line.includes('event=PassengerDroppedOff') && line.includes(trip.id)).length, 1);
assert.equal(status.lastWriteError, null);
console.log('Passed: all sample schemas/results, no simulation endpoints, automatic processing, Express, emergency recovery, FIFO, 128 concurrent submissions, and TXT logs.');
