import test from "node:test";
import assert from "node:assert/strict";
import {
  SaveStorage,
  SaveStore_slots as slots,
  SaveStore_key as key,
  SaveStore_read as read,
  SaveStore_write as write,
} from "../build/SaveStore.js";

const payload = revision => JSON.stringify({ version: 1, revision });
const validate = json => {
  const value = JSON.parse(json);
  return value !== null && value.version === 1 && Number.isSafeInteger(value.revision) && value.revision >= 0;
};

function memory(initial = {}) {
  const data = new Map(Object.entries(initial));
  const reads = [];
  const writes = [];
  const readErrors = new Set();
  const writeErrors = new Set();
  const storage = new SaveStorage(
    storageKey => {
      reads.push(storageKey);
      if (readErrors.has(storageKey)) throw new Error("SecurityError");
      return data.get(storageKey) ?? null;
    },
    (storageKey, json) => {
      writes.push([storageKey, json]);
      if (writeErrors.has(storageKey)) throw new Error("QuotaExceededError");
      data.set(storageKey, json);
    },
  );
  return { storage, data, reads, writes, readErrors, writeErrors };
}

const primary = key("slot1");
const backup = `${primary}.backup`;
const snapshot = store => Array.from(store.data.entries());

test("all four slots round-trip synchronously under separate stable keys", () => {
  assert.deepEqual(slots, ["auto", "slot1", "slot2", "slot3"]);
  const store = memory();
  for (const [index, slot] of slots.entries()) {
    assert.equal(key(slot), `fable-space-engine.save.${slot}`);
    const json = payload(index);
    const result = write(store.storage, slot, json, validate);
    assert.equal(result.Ok, true);
    assert.equal(result.Error, "");
    assert.equal(result.then, undefined);
    assert.equal(store.data.get(key(slot)), json);
    const loaded = read(store.storage, slot, validate);
    assert.equal(loaded.Json, json);
    assert.equal(loaded.FromBackup, false);
    assert.equal(loaded.Error, "");
  }
  assert.equal(store.data.size, 4);
});

test("the previous valid primary is backed up before replacement and recovers without mutation", () => {
  const store = memory();
  assert.equal(write(store.storage, "slot1", payload(1), validate).Ok, true);
  store.writes.length = 0;
  assert.equal(write(store.storage, "slot1", payload(2), validate).Ok, true);
  assert.deepEqual(store.writes, [[backup, payload(1)], [primary, payload(2)]]);
  store.data.set(primary, "broken JSON");
  const before = snapshot(store);
  store.writes.length = 0;
  const recovered = read(store.storage, "slot1", validate);
  assert.equal(recovered.Json, payload(1));
  assert.equal(recovered.FromBackup, true);
  assert.match(recovered.Error, /Sicherung/);
  assert.deepEqual(snapshot(store), before);
  assert.deepEqual(store.writes, []);
});

test("a valid primary wins without accessing an unavailable backup", () => {
  const store = memory({ [primary]: payload(2), [backup]: payload(1) });
  store.readErrors.add(backup);
  const loaded = read(store.storage, "slot1", validate);
  assert.equal(loaded.Json, payload(2));
  assert.equal(loaded.FromBackup, false);
  assert.equal(loaded.Error, "");
  assert.deepEqual(store.reads, [primary]);
  assert.deepEqual(store.writes, []);
});

test("missing primary falls back to backup and a completely empty slot is not an error", () => {
  const store = memory({ [backup]: payload(1) });
  const recovered = read(store.storage, "slot1", validate);
  assert.equal(recovered.Json, payload(1));
  assert.equal(recovered.FromBackup, true);
  assert.match(recovered.Error, /Sicherung/);
  assert.equal(store.data.has(primary), false);
  assert.deepEqual(store.writes, []);
  const empty = read(memory().storage, "slot1", validate);
  assert.equal(empty.Json, undefined);
  assert.equal(empty.FromBackup, false);
  assert.equal(empty.Error, "");
});

test("corrupted or unsupported saves cannot load and are never repaired by a read", () => {
  for (const initial of [
    { [primary]: "broken", [backup]: "also broken" },
    { [primary]: JSON.stringify({ version: 999, revision: 1 }) },
    { [backup]: "broken" },
  ]) {
    const store = memory(initial);
    const before = snapshot(store);
    const result = read(store.storage, "slot1", validate);
    assert.equal(result.Json, undefined);
    assert.equal(result.FromBackup, false);
    assert.ok(result.Error.length > 0);
    assert.deepEqual(snapshot(store), before);
    assert.deepEqual(store.writes, []);
  }
});

test("writing over a corrupt primary preserves an existing valid backup", () => {
  for (const oldPrimary of ["broken", JSON.stringify({ version: 999, revision: 4 })]) {
    const store = memory({ [primary]: oldPrimary, [backup]: payload(1) });
    const result = write(store.storage, "slot1", payload(3), validate);
    assert.equal(result.Ok, true);
    assert.equal(store.data.get(primary), payload(3));
    assert.equal(store.data.get(backup), payload(1));
    assert.deepEqual(store.writes, [[primary, payload(3)]]);
  }
});

test("invalid JSON is rejected even by a permissive validator, before any storage access", () => {
  for (const json of [null, "", "   ", "{bad", '{"a":1} trailing', "NaN"]) {
    const store = memory({ [primary]: payload(1) });
    const before = snapshot(store);
    const result = write(store.storage, "slot1", json, () => true);
    assert.equal(result.Ok, false);
    assert.match(result.Error, /ungültig/);
    assert.deepEqual(store.reads, []);
    assert.deepEqual(store.writes, []);
    assert.deepEqual(snapshot(store), before);
  }
  const store = memory();
  assert.equal(write(store.storage, "slot1", '{"version":999}', validate).Ok, false);
  assert.deepEqual(store.reads, []);
  assert.deepEqual(store.writes, []);
});

test("validator exceptions are contained and cannot overwrite existing data", () => {
  const store = memory({ [primary]: payload(2), [backup]: payload(1) });
  const before = snapshot(store);
  const throws = () => { throw new Error("schema failure"); };
  const load = read(store.storage, "slot1", throws);
  assert.equal(load.Json, undefined);
  assert.ok(load.Error.length > 0);
  store.reads.length = 0;
  const save = write(store.storage, "slot1", payload(3), throws);
  assert.equal(save.Ok, false);
  assert.deepEqual(store.reads, []);
  assert.deepEqual(store.writes, []);
  assert.deepEqual(snapshot(store), before);

  const recovered = read(store.storage, "slot1", json => {
    if (json === payload(2)) throw new Error("bad primary schema");
    return validate(json);
  });
  assert.equal(recovered.Json, payload(1));
  assert.equal(recovered.FromBackup, true);
  assert.deepEqual(snapshot(store), before);
});

test("unknown slots are rejected before validation or storage access", () => {
  const store = memory({ [primary]: payload(1) });
  let validationCalls = 0;
  const validating = () => { validationCalls++; return true; };
  for (const slot of [null, "", "slot4", "../auto", "AUTO", "slot1.backup"]) {
    const load = read(store.storage, slot, validating);
    assert.equal(load.Json, undefined);
    assert.match(load.Error, /Unbekannter Speicherplatz/);
    const save = write(store.storage, slot, payload(2), validating);
    assert.equal(save.Ok, false);
    assert.match(save.Error, /Unbekannter Speicherplatz/);
  }
  assert.equal(validationCalls, 0);
  assert.deepEqual(store.reads, []);
  assert.deepEqual(store.writes, []);
  assert.equal(store.data.get(primary), payload(1));
});

test("a quota failure while backing up leaves the old primary and backup untouched", () => {
  const store = memory({ [primary]: payload(2), [backup]: payload(1) });
  store.writeErrors.add(backup);
  const before = snapshot(store);
  const result = write(store.storage, "slot1", payload(3), validate);
  assert.equal(result.Ok, false);
  assert.match(result.Error, /exportieren/);
  assert.deepEqual(store.writes, [[backup, payload(2)]]);
  assert.deepEqual(snapshot(store), before);
});

test("a quota failure on primary retains its old version and a valid recovery copy", () => {
  const store = memory({ [primary]: payload(2), [backup]: payload(1) });
  store.writeErrors.add(primary);
  const result = write(store.storage, "slot1", payload(3), validate);
  assert.equal(result.Ok, false);
  assert.match(result.Error, /exportieren/);
  assert.deepEqual(store.writes, [[backup, payload(2)], [primary, payload(3)]]);
  assert.equal(store.data.get(primary), payload(2));
  assert.equal(store.data.get(backup), payload(2));
  assert.equal(read(store.storage, "slot1", validate).Json, payload(2));

  const empty = memory();
  empty.writeErrors.add(primary);
  assert.equal(write(empty.storage, "slot1", payload(1), validate).Ok, false);
  assert.equal(empty.data.size, 0);
});

test("storage read errors recover from backup when possible and prohibit unsafe writes", () => {
  const store = memory({ [primary]: payload(2), [backup]: payload(1) });
  store.readErrors.add(primary);
  const before = snapshot(store);
  const recovered = read(store.storage, "slot1", validate);
  assert.equal(recovered.Json, payload(1));
  assert.equal(recovered.FromBackup, true);
  const save = write(store.storage, "slot1", payload(3), validate);
  assert.equal(save.Ok, false);
  assert.match(save.Error, /exportieren/);
  assert.deepEqual(snapshot(store), before);
  assert.deepEqual(store.writes, []);
  store.readErrors.add(backup);
  const unreadable = read(store.storage, "slot1", validate);
  assert.equal(unreadable.Json, undefined);
  assert.equal(unreadable.FromBackup, false);
  assert.match(unreadable.Error, /nicht gelesen/);
  assert.deepEqual(snapshot(store), before);
});
