// Structural checks only; this does not simulate Unity or command outcomes.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

const root = __dirname;
const walk = dir => fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const file = path.join(dir, entry.name);
    return entry.isDirectory() ? walk(file) : [file];
});
const read = file => JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
const files = walk(root).filter(file => file.endsWith('.json'));
const docs = new Map(files.map(file => [file, read(file)]));
const sources = [...docs].filter(([file]) => !file.endsWith('_loc.json'));
const encounters = new Map();
const handlers = new Map();
const executions = event => event.events.flatMap(entry => [
    ...(entry.Results || []), ...(entry.options || []).flatMap(option => option.Results || [])
]);
let checks = 0;
function check(condition, message) { checks++; assert.ok(condition, message); }
function same(actual, expected, message) { checks++; assert.deepEqual(actual, expected, message); }

for (const [file, doc] of sources) {
    const zh = doc.Dictionary?.Entries?.['zh-cn'];
    const en = docs.get(file.replace(/\.json$/, '_loc.json'))?.Dictionary?.Entries?.['en-us'];
    check(zh && en, `Missing localization pair: ${file}`);
    same(Object.keys(zh).sort(), Object.keys(en).sort(), `Locale keys: ${file}`);
    function localizedRefs(value) {
        if (!value || typeof value !== 'object') return;
        for (const [key, item] of Object.entries(value)) {
            if (['line', 'question', 'showEventHeader', 'eventString_Ongoing', 'resultText'].includes(key) && item)
                check(Object.hasOwn(zh, item), `Missing localized ${key}: ${item}`);
            if (key === 'option' && !item.startsWith('_'))
                check(Object.hasOwn(zh, item), `Missing option: ${item}`);
            if (key === 'DescriptionText') for (const id of item)
                check(Object.hasOwn(zh, id), `Missing description: ${id}`);
            localizedRefs(item);
        }
    }
    if (path.basename(file) !== 'clients.json') localizedRefs(doc);
    for (const encounter of doc.ExplorationEvents?.list || []) {
        check(!encounters.has(encounter.eventID), `Duplicate encounter: ${encounter.eventID}`);
        encounters.set(encounter.eventID, encounter);
    }
    for (const event of doc.Events?.list || []) {
        check(!handlers.has(event.ID), `Duplicate handler: ${event.ID}`);
        handlers.set(event.ID, event);
        const labels = new Set(event.events.filter(entry => entry.label).map(entry => entry.label));
        same(labels.size, event.events.filter(entry => entry.label).length, `Duplicate labels: ${event.ID}`);
        for (const entry of event.events) {
            if (entry.nextEntryLabel) check(labels.has(entry.nextEntryLabel), `Missing next label: ${event.ID}/${entry.nextEntryLabel}`);
        }
        for (const exec of executions(event)) if (exec.Type === 'JumpToLabel' && !exec.arguments[0])
            check(labels.has(exec.arguments[1]), `Missing jump label: ${event.ID}/${exec.arguments[1]}`);
        for (const entry of event.events.filter(entry => entry.$type?.includes('EventEntry_Branch'))) {
            for (const option of entry.options) if (option.Results?.some(exec => exec.Type === 'AppendStringEquals'))
                check(option.Results.every(exec => ['AppendStringEquals', 'JumpToLabel'].includes(exec.Type)), `Mutating comparison gate: ${event.ID}`);
        }
    }
}

const shared = docs.get(path.join(root, 'explorations.json'));
same(shared.ExpeditionEntry.list.length, 1, 'One shared definition');
const definition = shared.ExpeditionEntry.list[0];
same(definition.isUnique, false, 'Instances must not be unique');
same(definition.removeOnComplete, true, 'Disposable instances');
same(definition.EventIDs, [], 'Pool must be feature-driven');
check(handlers.has(definition.beginEventID), 'Begin handler must exist');
const registered = shared.ExplorationFeatures.list.flatMap(feature => feature.featureEventIDs || []);
same([...new Set(registered)].sort(), [...encounters.keys()].sort(), 'Feature registration covers encounter pool');
const categories = ['politician', 'producer', 'sm_master', 'sports', 'celeb', 'household', 'gang'];
for (const category of categories) {
    const features = new Set([...definition.FeatureKeywords, `client_${category}`]);
    const modules = shared.ExplorationFeatures.list.filter(feature => feature.requireKeywords.every(key => features.has(key)));
    check(modules.some(feature => feature.requireKeywords.length === 1), `Common atmosphere module: ${category}`);
    check(modules.some(feature => feature.requireKeywords.includes(`client_${category}`)), `Category module: ${category}`);
}

let majorCount = 0;
let minorCount = 0;
let resultCount = 0;
for (const encounter of encounters.values()) {
    check(encounter.baseWeight > 0, `Unselectable encounter: ${encounter.eventID}`);
    check(encounter.possibleResults.length > 0, `Empty results: ${encounter.eventID}`);
    const major = encounter.possibleResults.some(result => result.eventID);
    if (major) {
        majorCount++;
        check(encounter.cooldownHours >= 24, `Major repeat cooldown: ${encounter.eventID}`);
    } else {
        minorCount++;
        check(encounter.possibleResults.length >= 2, `Minor variations: ${encounter.eventID}`);
    }
    for (const result of encounter.possibleResults) {
        if (!major) resultCount++;
        check(result.baseWeight > 0, `Unselectable result: ${encounter.eventID}`);
        check(!result.runImmediate, `Unexpected immediate dispatch: ${encounter.eventID}`);
        if (!result.eventID) continue;
        const handler = handlers.get(result.eventID);
        check(handler, `Missing result handler: ${result.eventID}`);
        check(handler.events.some(entry => entry.$type?.includes('EventEntry_Question')), `Major lacks input: ${handler.ID}`);
        const loans = new Set(executions(handler).filter(exec => exec.Type === 'RequestFallbackWorker').map(exec => exec.arguments[0]));
        for (const terminal of handler.events.filter(entry => entry.isLast)) {
            const effects = terminal.Results || [];
            same(effects.filter(exec => exec.Type === 'ExecuteCallbackPermissive' && exec.arguments[0] === 'ResolveEvent').length, 1, `Terminal resolution: ${handler.ID}/${terminal.label}`);
            for (const scope of loans)
                check(effects.some(exec => exec.Type === 'ReleaseFallbackWorker' && exec.arguments[0] === scope), `Terminal actor return: ${handler.ID}/${scope}`);
        }
        check(!executions(handler).some(exec => exec.arguments?.includes('RetryEvent')), `Retry blocks release: ${handler.ID}`);
    }
}

const arrival = read(path.join(root, '..', 'Events', 'ErAV_NTR_Punishment.json'));
const external = arrival.Events.list;
const calls = external.flatMap(executions).filter(exec => exec.Type === 'CharaMIAWithFeatures');
same(calls.length, categories.length, 'Seven current assignment routes');
for (const category of categories) {
    const call = calls.find(exec => exec.arguments.includes(`client_${category}`));
    check(call, `Missing assignment: ${category}`);
    same(call.arguments[2], definition.ExpeditionID, `Common expedition: ${category}`);
    const budget = ['politician', 'producer'].includes(category) ? 2400 : ['sm_master', 'household'].includes(category) ? 5600 : 4000;
    check(call.arguments.includes(`progressBudget=${budget}`), `Preserved budget: ${category}`);
}
for (const id of [definition.selfReleaseEventID, definition.returnEventID])
    check(external.some(event => event.ID === id), `Missing lifecycle hook: ${id}`);
check(external.flatMap(executions).some(exec => exec.Type === 'CharaMIA' && exec.arguments[2] === 'expedition_erav_ntr_mia'), 'Safe-mode fallback');

console.log(JSON.stringify({ checks, jsonFiles: files.length, majorCount, minorCount, minorResultVariants: resultCount,
    featureModules: shared.ExplorationFeatures.list.length, status: 'passed' }, null, 2));
