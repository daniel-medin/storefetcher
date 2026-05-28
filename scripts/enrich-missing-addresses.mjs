import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, extname, resolve } from "node:path";

const DEFAULT_OUTPUT_PATH = "data/address-enriched-import.json";
const DEFAULT_REVIEW_PATH = "data/address-review.json";
const DEFAULT_OVERPASS_ENDPOINT =
  process.env.OVERPASS_ENDPOINT ?? "https://overpass-api.de/api/interpreter";
const DEFAULT_USER_AGENT =
  process.env.STORE_ENRICH_USER_AGENT ??
  "get-stores-test/0.1 address-enrichment (local experiment)";

function readArgs() {
  const args = {
    inputPath: null,
    apiBaseUrl: null,
    outputPath: DEFAULT_OUTPUT_PATH,
    reviewPath: DEFAULT_REVIEW_PATH,
    lantmaterietPath: null,
    overpassEndpoint: DEFAULT_OVERPASS_ENDPOINT,
    useOsm: true,
    radiusMeters: 80,
    acceptDistanceMeters: 35,
    ambiguityGapMeters: 25,
    chunkSize: 25,
    delayMs: 1000,
    maxStores: null,
    overwrite: false,
  };

  for (const arg of process.argv.slice(2)) {
    if (arg.startsWith("--input=")) {
      args.inputPath = arg.slice("--input=".length);
      continue;
    }

    if (arg.startsWith("--api=")) {
      args.apiBaseUrl = arg.slice("--api=".length).replace(/\/+$/, "");
      continue;
    }

    if (arg.startsWith("--output=")) {
      args.outputPath = arg.slice("--output=".length);
      continue;
    }

    if (arg.startsWith("--review=")) {
      args.reviewPath = arg.slice("--review=".length);
      continue;
    }

    if (arg.startsWith("--lantmateriet=")) {
      args.lantmaterietPath = arg.slice("--lantmateriet=".length);
      continue;
    }

    if (arg.startsWith("--overpass-endpoint=")) {
      args.overpassEndpoint = arg.slice("--overpass-endpoint=".length);
      continue;
    }

    if (arg === "--no-osm") {
      args.useOsm = false;
      continue;
    }

    if (arg === "--overwrite") {
      args.overwrite = true;
      continue;
    }

    if (arg.startsWith("--radius-meters=")) {
      args.radiusMeters = readPositiveNumber(arg, "--radius-meters=");
      continue;
    }

    if (arg.startsWith("--accept-distance-meters=")) {
      args.acceptDistanceMeters = readPositiveNumber(
        arg,
        "--accept-distance-meters=",
      );
      continue;
    }

    if (arg.startsWith("--ambiguity-gap-meters=")) {
      args.ambiguityGapMeters = readPositiveNumber(
        arg,
        "--ambiguity-gap-meters=",
      );
      continue;
    }

    if (arg.startsWith("--chunk-size=")) {
      args.chunkSize = readPositiveInteger(arg, "--chunk-size=");
      continue;
    }

    if (arg.startsWith("--delay-ms=")) {
      args.delayMs = readNonNegativeInteger(arg, "--delay-ms=");
      continue;
    }

    if (arg.startsWith("--max-stores=")) {
      args.maxStores = readPositiveInteger(arg, "--max-stores=");
      continue;
    }

    if (arg === "--help" || arg === "-h") {
      printHelp();
      process.exit(0);
    }

    throw new Error(`Unknown argument: ${arg}`);
  }

  readNpmConfigArgs(args);

  if ((args.inputPath && args.apiBaseUrl) || (!args.inputPath && !args.apiBaseUrl)) {
    throw new Error("Pass exactly one source: --input=path or --api=http://localhost:5112");
  }

  return args;
}

function readNpmConfigArgs(args) {
  const env = process.env;

  args.inputPath ??= clean(env.npm_config_input);
  args.apiBaseUrl ??= clean(env.npm_config_api)?.replace(/\/+$/, "");
  args.outputPath = clean(env.npm_config_output) ?? args.outputPath;
  args.reviewPath = clean(env.npm_config_review) ?? args.reviewPath;
  args.lantmaterietPath =
    clean(env.npm_config_lantmateriet) ?? args.lantmaterietPath;
  args.overpassEndpoint =
    clean(env.npm_config_overpass_endpoint) ?? args.overpassEndpoint;
  args.useOsm = envFlag(env.npm_config_no_osm) ? false : args.useOsm;
  args.useOsm =
    env.npm_config_osm === "" || env.npm_config_osm === "false"
      ? false
      : args.useOsm;
  args.overwrite = envFlag(env.npm_config_overwrite) || args.overwrite;
  args.radiusMeters = envNumber(env.npm_config_radius_meters) ?? args.radiusMeters;
  args.acceptDistanceMeters =
    envNumber(env.npm_config_accept_distance_meters) ??
    args.acceptDistanceMeters;
  args.ambiguityGapMeters =
    envNumber(env.npm_config_ambiguity_gap_meters) ?? args.ambiguityGapMeters;
  args.chunkSize = envInteger(env.npm_config_chunk_size, 1) ?? args.chunkSize;
  args.delayMs = envInteger(env.npm_config_delay_ms, 0) ?? args.delayMs;
  args.maxStores = envInteger(env.npm_config_max_stores, 1) ?? args.maxStores;
}

function envFlag(value) {
  return value === "true" || value === "1" || value === "";
}

function envNumber(value) {
  const number = Number.parseFloat(value);
  return Number.isFinite(number) && number > 0 ? number : null;
}

function envInteger(value, minimum) {
  const number = Number.parseInt(value, 10);
  return Number.isInteger(number) && number >= minimum ? number : null;
}

function readPositiveNumber(arg, prefix) {
  const value = Number.parseFloat(arg.slice(prefix.length));
  if (!Number.isFinite(value) || value <= 0) {
    throw new Error(`Invalid ${prefix}${arg.slice(prefix.length)}`);
  }

  return value;
}

function readPositiveInteger(arg, prefix) {
  const value = Number.parseInt(arg.slice(prefix.length), 10);
  if (!Number.isInteger(value) || value <= 0) {
    throw new Error(`Invalid ${prefix}${arg.slice(prefix.length)}`);
  }

  return value;
}

function readNonNegativeInteger(arg, prefix) {
  const value = Number.parseInt(arg.slice(prefix.length), 10);
  if (!Number.isInteger(value) || value < 0) {
    throw new Error(`Invalid ${prefix}${arg.slice(prefix.length)}`);
  }

  return value;
}

function printHelp() {
  console.log(`
Usage:
  node scripts/enrich-missing-addresses.mjs --input=data/place-grocery-stores.json
  node scripts/enrich-missing-addresses.mjs --api=http://localhost:5112

Options:
  --lantmateriet=path              Optional local address file, GeoJSON/JSON/CSV.
  --output=path                    Prepared JSON import output.
  --review=path                    JSON review report for uncertain matches.
  --no-osm                         Skip Overpass fallback.
  --radius-meters=80               Candidate search radius.
  --accept-distance-meters=35      Auto-accept distance for complete addresses.
  --ambiguity-gap-meters=25        Review when another address is this close.
  --chunk-size=25                  Number of stores per Overpass request.
  --delay-ms=1000                  Delay between Overpass requests.
  --max-stores=100                 Limit missing stores for a test run.
  --overwrite                      Replace existing address fields too.
`.trim());
}

async function readPreparedDataset(path) {
  const text = await readFile(path, "utf8");
  const dataset = JSON.parse(text);
  if (!Array.isArray(dataset.stores)) {
    throw new Error("Input JSON must contain a stores array.");
  }

  return {
    source: "file",
    sourcePath: path,
    metadata: dataset.metadata ?? {},
    stores: dataset.stores.map((store) => normalizePreparedStore(store)),
  };
}

async function readApiDataset(apiBaseUrl) {
  const stores = [];
  let page = 1;
  let total = null;

  do {
    const url = `${apiBaseUrl}/api/stores?page=${page}&pageSize=250`;
    const response = await fetch(url, {
      headers: { "user-agent": DEFAULT_USER_AGENT },
    });
    if (!response.ok) {
      const body = await response.text();
      throw new Error(`API request failed: ${response.status} ${response.statusText}\n${body}`);
    }

    const data = await response.json();
    total = data.total ?? total;
    stores.push(...(data.stores ?? []).map((store) => normalizeApiStore(store)));
    page++;
  } while (total === null || stores.length < total);

  return {
    source: "api",
    sourceUrl: apiBaseUrl,
    metadata: {},
    stores,
  };
}

function normalizePreparedStore(store) {
  const address = store.address ?? {};

  return {
    prepared: {
      ...store,
      address: {
        street: clean(address.street),
        house_number: clean(address.house_number),
        postcode: cleanPostcode(address.postcode),
        city: clean(address.city),
        country: clean(address.country) ?? "SE",
      },
    },
    id: store.id ?? null,
    hasCorrection: false,
  };
}

function normalizeApiStore(store) {
  return {
    prepared: {
      name: store.name ?? null,
      address: {
        street: clean(store.street),
        house_number: clean(store.houseNumber),
        postcode: cleanPostcode(store.postcode),
        city: clean(store.city),
        country: clean(store.country) ?? "SE",
      },
      lat: store.latitude ?? null,
      lon: store.longitude ?? null,
      shop: clean(store.shop),
      brand: clean(store.brand),
      website: clean(store.website),
      phone: clean(store.phone),
      opening_hours: clean(store.openingHours),
      osm: {
        type: store.osmType,
        id: store.osmId,
        url: store.osmUrl,
      },
    },
    id: store.id ?? null,
    hasCorrection: Boolean(store.hasCorrection),
  };
}

function clean(value) {
  if (value === null || value === undefined) {
    return null;
  }

  const text = String(value).trim();
  return text.length > 0 ? text : null;
}

function cleanPostcode(value) {
  const text = clean(value);
  if (!text) {
    return null;
  }

  const digits = text.replace(/\D/g, "");
  if (digits.length === 5) {
    return digits;
  }

  return text;
}

function hasMissingAddress(store, overwrite) {
  if (overwrite) {
    return true;
  }

  const address = store.prepared.address ?? {};
  return !address.street || !address.house_number || !address.postcode || !address.city;
}

async function loadAddressSource(path, radiusMeters) {
  if (!path) {
    return null;
  }

  const resolved = resolve(path);
  const text = await readFile(resolved, "utf8");
  const extension = extname(resolved).toLowerCase();
  const candidates =
    extension === ".csv"
      ? parseAddressCsv(text)
      : parseAddressJson(JSON.parse(text));

  const usable = candidates.filter(
    (candidate) =>
      Number.isFinite(candidate.lat) &&
      Number.isFinite(candidate.lon) &&
      candidate.address.street &&
      candidate.address.house_number,
  );

  return new AddressIndex(usable, radiusMeters);
}

function parseAddressJson(data) {
  if (data.type === "FeatureCollection" && Array.isArray(data.features)) {
    return data.features.map(addressCandidateFromFeature).filter(Boolean);
  }

  if (Array.isArray(data)) {
    return data.map(addressCandidateFromPlainObject).filter(Boolean);
  }

  if (Array.isArray(data.addresses)) {
    return data.addresses.map(addressCandidateFromPlainObject).filter(Boolean);
  }

  throw new Error("Address JSON must be GeoJSON, an array, or an object with addresses.");
}

function addressCandidateFromFeature(feature) {
  const coordinates = feature.geometry?.coordinates;
  if (!Array.isArray(coordinates) || coordinates.length < 2) {
    return null;
  }

  const [lon, lat] = coordinates;
  return addressCandidateFromProperties(feature.properties ?? {}, lat, lon);
}

function addressCandidateFromPlainObject(row) {
  const lat = firstNumber(row, ["lat", "latitude", "y"]);
  const lon = firstNumber(row, ["lon", "lng", "longitude", "x"]);
  return addressCandidateFromProperties(row, lat, lon);
}

function parseAddressCsv(text) {
  const rows = parseCsv(text);
  if (rows.length < 2) {
    return [];
  }

  const headers = rows[0].map((header) => header.trim());
  return rows.slice(1).map((row) => {
    const object = {};
    headers.forEach((header, index) => {
      object[header] = row[index] ?? "";
    });
    return addressCandidateFromPlainObject(object);
  }).filter(Boolean);
}

function parseCsv(text) {
  const rows = [];
  let row = [];
  let cell = "";
  let quoted = false;

  for (let index = 0; index < text.length; index++) {
    const char = text[index];
    const next = text[index + 1];

    if (quoted && char === '"' && next === '"') {
      cell += '"';
      index++;
      continue;
    }

    if (char === '"') {
      quoted = !quoted;
      continue;
    }

    if (!quoted && char === ",") {
      row.push(cell);
      cell = "";
      continue;
    }

    if (!quoted && (char === "\n" || char === "\r")) {
      if (char === "\r" && next === "\n") {
        index++;
      }
      row.push(cell);
      if (row.some((value) => value.trim().length > 0)) {
        rows.push(row);
      }
      row = [];
      cell = "";
      continue;
    }

    cell += char;
  }

  row.push(cell);
  if (row.some((value) => value.trim().length > 0)) {
    rows.push(row);
  }

  return rows;
}

function addressCandidateFromProperties(properties, lat, lon) {
  const street =
    firstText(properties, [
      "addr:street",
      "street",
      "gata",
      "adressomrade_faststalltnamn",
      "adressomrade",
    ]);
  const number =
    firstText(properties, ["addr:housenumber", "house_number", "housenumber"]) ??
    buildLantmaterietHouseNumber(properties);
  const postcode = cleanPostcode(firstText(properties, ["addr:postcode", "postcode", "postnummer"]));
  const city =
    firstText(properties, ["addr:city", "city", "postort"]) ??
    firstText(properties, ["kommunnamn"]);

  return {
    source: "lantmateriet",
    lat: Number(lat),
    lon: Number(lon),
    address: {
      street,
      house_number: number,
      postcode,
      city,
      country: firstText(properties, ["addr:country", "country"]) ?? "SE",
    },
    raw: compactObject({
      id: firstText(properties, [
        "belagenhetsadress_objektidentitet",
        "id",
        "objectid",
      ]),
      status: firstText(properties, ["statusforbelagenhetsadress", "status"]),
      municipality: firstText(properties, ["kommunnamn"]),
    }),
  };
}

function buildLantmaterietHouseNumber(properties) {
  const deviating = firstText(properties, ["avvikandeadressplatsbeteckning"]);
  const number = firstText(properties, ["adressplatsnummer"]);
  const letter = firstText(properties, ["bokstavstillagg"]);

  if (number) {
    return `${number}${letter ?? ""}`;
  }

  return deviating;
}

function firstText(object, keys) {
  for (const key of keys) {
    const value = object[key];
    const text = clean(value);
    if (text) {
      return text;
    }
  }

  return null;
}

function firstNumber(object, keys) {
  for (const key of keys) {
    const value = Number.parseFloat(object[key]);
    if (Number.isFinite(value)) {
      return value;
    }
  }

  return null;
}

class AddressIndex {
  constructor(candidates, radiusMeters) {
    this.candidates = candidates;
    this.cellSize = Math.max(radiusMeters / 111_320, 0.0005);
    this.cells = new Map();

    for (const candidate of candidates) {
      const key = this.key(candidate.lat, candidate.lon);
      const cell = this.cells.get(key) ?? [];
      cell.push(candidate);
      this.cells.set(key, cell);
    }
  }

  key(lat, lon) {
    return `${Math.floor(lat / this.cellSize)}:${Math.floor(lon / this.cellSize)}`;
  }

  nearby(lat, lon, radiusMeters) {
    const latCell = Math.floor(lat / this.cellSize);
    const lonCell = Math.floor(lon / this.cellSize);
    const span = Math.ceil(radiusMeters / 111_320 / this.cellSize) + 2;
    const matches = [];

    for (let y = latCell - span; y <= latCell + span; y++) {
      for (let x = lonCell - span; x <= lonCell + span; x++) {
        const cell = this.cells.get(`${y}:${x}`) ?? [];
        for (const candidate of cell) {
          const distanceMeters = distance(lat, lon, candidate.lat, candidate.lon);
          if (distanceMeters <= radiusMeters) {
            matches.push({ ...candidate, distanceMeters });
          }
        }
      }
    }

    return matches.sort(compareCandidates);
  }
}

async function fetchOsmCandidatesByStore(stores, args) {
  const byKey = new Map(stores.map((store) => [storeKey(store), []]));
  const chunks = chunk(stores, args.chunkSize);

  for (let index = 0; index < chunks.length; index++) {
    const batch = chunks[index];
    console.log(`OSM address lookup ${index + 1}/${chunks.length} (${batch.length} stores)...`);
    const candidates = await fetchOsmAddressCandidates(batch, args);

    for (const store of batch) {
      const lat = store.prepared.lat;
      const lon = store.prepared.lon;
      const matches = candidates
        .map((candidate) => ({
          ...candidate,
          distanceMeters: distance(lat, lon, candidate.lat, candidate.lon),
        }))
        .filter((candidate) => candidate.distanceMeters <= args.radiusMeters)
        .sort(compareCandidates);

      byKey.set(storeKey(store), matches);
    }

    if (index < chunks.length - 1 && args.delayMs > 0) {
      await sleep(args.delayMs);
    }
  }

  return byKey;
}

async function fetchOsmAddressCandidates(stores, args) {
  const query = buildOverpassAddressQuery(stores, args.radiusMeters);
  const response = await fetch(args.overpassEndpoint, {
    method: "POST",
    headers: {
      "content-type": "application/x-www-form-urlencoded;charset=UTF-8",
      "user-agent": DEFAULT_USER_AGENT,
    },
    body: new URLSearchParams({ data: query }),
  });

  if (!response.ok) {
    const body = await response.text();
    throw new Error(
      `Overpass request failed: ${response.status} ${response.statusText}\n${body}`,
    );
  }

  const data = await response.json();
  return (data.elements ?? []).map(osmCandidateFromElement).filter(Boolean);
}

function buildOverpassAddressQuery(stores, radiusMeters) {
  const clauses = stores
    .map((store) => {
      const lat = Number(store.prepared.lat).toFixed(7);
      const lon = Number(store.prepared.lon).toFixed(7);
      return [
        `nwr(around:${radiusMeters},${lat},${lon})["addr:street"]["addr:housenumber"];`,
        `nwr(around:${radiusMeters},${lat},${lon})["addr:postcode"];`,
      ].join("\n");
    })
    .join("\n");

  return `
[out:json][timeout:120];
(
${clauses}
);
out tags center;
`.trim();
}

function osmCandidateFromElement(element) {
  const tags = element.tags ?? {};
  const lat = element.lat ?? element.center?.lat;
  const lon = element.lon ?? element.center?.lon;
  const street = clean(tags["addr:street"]);
  const houseNumber = clean(tags["addr:housenumber"]);

  if (!Number.isFinite(lat) || !Number.isFinite(lon) || (!street && !houseNumber)) {
    return null;
  }

  return {
    source: "osm",
    lat,
    lon,
    address: {
      street,
      house_number: houseNumber,
      postcode: cleanPostcode(tags["addr:postcode"]),
      city: clean(tags["addr:city"]),
      country: clean(tags["addr:country"]) ?? "SE",
    },
    raw: {
      osm_type: element.type,
      osm_id: element.id,
      osm_url: `https://www.openstreetmap.org/${element.type}/${element.id}`,
    },
  };
}

function chooseMatch(candidates, args) {
  const usable = candidates
    .filter((candidate) => candidate.address.street && candidate.address.house_number)
    .sort(compareCandidates);

  if (usable.length === 0) {
    return {
      accepted: false,
      reason: candidates.length === 0 ? "no_candidates" : "no_complete_address",
      candidates,
    };
  }

  const best = usable[0];
  const alternative = usable
    .slice(1)
    .find((candidate) => !sameAddress(candidate.address, best.address));

  if (best.distanceMeters > args.acceptDistanceMeters) {
    return {
      accepted: false,
      reason: "too_far",
      match: best,
      candidates: usable,
    };
  }

  if (
    alternative &&
    alternative.distanceMeters - best.distanceMeters < args.ambiguityGapMeters
  ) {
    return {
      accepted: false,
      reason: "ambiguous_nearby_addresses",
      match: best,
      candidates: usable,
    };
  }

  return {
    accepted: true,
    reason: "high_confidence_nearest_address",
    match: best,
    candidates: usable,
  };
}

function compareCandidates(left, right) {
  const sourceOrder = { lantmateriet: 0, osm: 1 };
  return (
    (left.distanceMeters ?? 0) - (right.distanceMeters ?? 0) ||
    (sourceOrder[left.source] ?? 9) - (sourceOrder[right.source] ?? 9)
  );
}

function sameAddress(left, right) {
  return (
    normalizeComparable(left.street) === normalizeComparable(right.street) &&
    normalizeComparable(left.house_number) === normalizeComparable(right.house_number) &&
    normalizeComparable(left.postcode) === normalizeComparable(right.postcode)
  );
}

function normalizeComparable(value) {
  return clean(value)?.toLowerCase().replace(/\s+/g, "") ?? "";
}

function applyAddress(store, address, overwrite) {
  store.prepared.address ??= {};
  const target = store.prepared.address;

  fill(target, "street", address.street, overwrite);
  fill(target, "house_number", address.house_number, overwrite);
  fill(target, "postcode", cleanPostcode(address.postcode), overwrite);
  fill(target, "city", address.city, overwrite);
  fill(target, "country", address.country ?? "SE", overwrite);
}

function fill(target, key, value, overwrite) {
  const cleaned = key === "postcode" ? cleanPostcode(value) : clean(value);
  if (!cleaned) {
    return;
  }

  if (overwrite || !clean(target[key])) {
    target[key] = cleaned;
  }
}

function distance(lat1, lon1, lat2, lon2) {
  const earthRadiusMeters = 6_371_000;
  const phi1 = toRadians(lat1);
  const phi2 = toRadians(lat2);
  const deltaPhi = toRadians(lat2 - lat1);
  const deltaLambda = toRadians(lon2 - lon1);

  const a =
    Math.sin(deltaPhi / 2) ** 2 +
    Math.cos(phi1) * Math.cos(phi2) * Math.sin(deltaLambda / 2) ** 2;
  return earthRadiusMeters * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
}

function toRadians(value) {
  return (value * Math.PI) / 180;
}

function storeKey(store) {
  const osm = store.prepared.osm ?? {};
  return `${osm.type ?? "store"}:${osm.id ?? store.id ?? store.prepared.name}`;
}

function reviewStore(store, decision) {
  return {
    reason: decision.reason,
    store: {
      id: store.id,
      name: store.prepared.name,
      lat: store.prepared.lat,
      lon: store.prepared.lon,
      osm: store.prepared.osm,
      has_correction: store.hasCorrection,
      current_address: store.prepared.address,
    },
    suggested_match: decision.match ? candidateForOutput(decision.match) : null,
    candidates: decision.candidates.slice(0, 8).map(candidateForOutput),
  };
}

function candidateForOutput(candidate) {
  return {
    source: candidate.source,
    distance_meters: Math.round(candidate.distanceMeters * 10) / 10,
    address: candidate.address,
    lat: candidate.lat,
    lon: candidate.lon,
    raw: candidate.raw,
  };
}

function compactObject(object) {
  return Object.fromEntries(
    Object.entries(object).filter(([, value]) => value !== null && value !== undefined),
  );
}

function chunk(items, size) {
  const chunks = [];
  for (let index = 0; index < items.length; index += size) {
    chunks.push(items.slice(index, index + size));
  }
  return chunks;
}

function sleep(ms) {
  return new Promise((resolveSleep) => setTimeout(resolveSleep, ms));
}

async function writeJson(path, data) {
  const resolved = resolve(path);
  await mkdir(dirname(resolved), { recursive: true });
  await writeFile(resolved, `${JSON.stringify(data, null, 2)}\n`);
  return resolved;
}

async function main() {
  const args = readArgs();
  const dataset = args.inputPath
    ? await readPreparedDataset(args.inputPath)
    : await readApiDataset(args.apiBaseUrl);

  const originalStoreCount = dataset.stores.length;
  let missing = dataset.stores.filter((store) => hasMissingAddress(store, args.overwrite));

  if (args.maxStores !== null) {
    missing = missing.slice(0, args.maxStores);
  }

  console.log(`Loaded ${originalStoreCount} stores; ${missing.length} need address enrichment.`);

  const byKey = new Map(missing.map((store) => [storeKey(store), []]));
  const stats = {
    total_stores: originalStoreCount,
    checked_stores: missing.length,
    enriched: 0,
    review: 0,
    no_candidates: 0,
    skipped_corrected_from_api: 0,
  };
  const reviews = [];
  const enrichedStores = [];

  const lantmaterietIndex = await loadAddressSource(
    args.lantmaterietPath,
    args.radiusMeters,
  );

  if (lantmaterietIndex) {
    console.log(`Loaded ${lantmaterietIndex.candidates.length} Lantmateriet address candidates.`);
    for (const store of missing) {
      const candidates = lantmaterietIndex.nearby(
        store.prepared.lat,
        store.prepared.lon,
        args.radiusMeters,
      );
      byKey.set(storeKey(store), candidates);
    }
  }

  const stillMissingAfterLantmateriet = missing.filter((store) => {
    const decision = chooseMatch(byKey.get(storeKey(store)) ?? [], args);
    return !decision.accepted;
  });

  if (args.useOsm && stillMissingAfterLantmateriet.length > 0) {
    const osmByKey = await fetchOsmCandidatesByStore(stillMissingAfterLantmateriet, args);
    for (const store of stillMissingAfterLantmateriet) {
      const combined = [
        ...(byKey.get(storeKey(store)) ?? []),
        ...(osmByKey.get(storeKey(store)) ?? []),
      ].sort(compareCandidates);
      byKey.set(storeKey(store), combined);
    }
  }

  for (const store of missing) {
    if (dataset.source === "api" && store.hasCorrection) {
      stats.skipped_corrected_from_api++;
      reviews.push({
        reason: "has_manual_correction_api_source",
        store: {
          id: store.id,
          name: store.prepared.name,
          lat: store.prepared.lat,
          lon: store.prepared.lon,
          osm: store.prepared.osm,
          has_correction: true,
          current_address: store.prepared.address,
        },
        suggested_match: null,
        candidates: [],
      });
      continue;
    }

    const candidates = byKey.get(storeKey(store)) ?? [];
    const decision = chooseMatch(candidates, args);

    if (decision.accepted) {
      applyAddress(store, decision.match.address, args.overwrite);
      store.prepared.address_enrichment = {
        source: decision.match.source,
        distance_meters: Math.round(decision.match.distanceMeters * 10) / 10,
        enriched_at: new Date().toISOString(),
      };
      enrichedStores.push(store.prepared);
      stats.enriched++;
      continue;
    }

    if (decision.reason === "no_candidates") {
      stats.no_candidates++;
    }
    stats.review++;
    reviews.push(reviewStore(store, decision));
  }

  const output = {
    metadata: {
      generated_at: new Date().toISOString(),
      source: "Local address enrichment",
      input_source: dataset.source,
      input_path: dataset.sourcePath,
      input_api: dataset.sourceUrl,
      lantmateriet_path: args.lantmaterietPath,
      overpass_endpoint: args.useOsm ? args.overpassEndpoint : null,
      radius_meters: args.radiusMeters,
      accept_distance_meters: args.acceptDistanceMeters,
      ambiguity_gap_meters: args.ambiguityGapMeters,
      count: enrichedStores.length,
      note: "This prepared import contains only stores whose missing address fields were enriched automatically.",
    },
    stores: enrichedStores,
  };

  const review = {
    metadata: {
      generated_at: new Date().toISOString(),
      source: "Local address enrichment review",
      count: reviews.length,
      stats,
    },
    reviews,
  };

  const outputPath = await writeJson(args.outputPath, output);
  const reviewPath = await writeJson(args.reviewPath, review);

  console.log(`Enriched ${stats.enriched} store(s).`);
  console.log(`Sent ${reviews.length} store(s) to review.`);
  console.log(`Wrote import file: ${outputPath}`);
  console.log(`Wrote review file: ${reviewPath}`);
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
