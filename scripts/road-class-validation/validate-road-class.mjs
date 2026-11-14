#!/usr/bin/env bun
// OSM の道路種別（highway）が、実際の路線種別（国道・都道府県道など）と合っているかを、
// PLATEAU（3D都市モデル）の道路の機能（tran:function）を正解として照合する。
// 手順と結果の読み方は docs/development/road-class-validation.md を参照。
//
// 使い方（bun または Node 22 以降。外部パッケージなし）:
//   bun scripts/road-class-validation/validate-road-class.mjs \
//     --name kyoto --plateau-zip <CityGML の ZIP の URL> [--cache-dir <DIR>] [--json <出力先>]
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { parseCodelist } from './lib/codelist.mjs';
import { compare, formatTable } from './lib/compare.mjs';
import { fetchRoadWays } from './lib/overpass.mjs';
import { RoadIndex, parseRoads } from './lib/plateau-roads.mjs';
import { httpZipSource, openZip } from './lib/remote-zip.mjs';

const ROAD_ENTRY_PREFIX = 'udx/tran/';
const CODELIST_ENTRY = 'codelists/Road_function.xml';

const { values } = parseArgs({
  options: {
    name: { type: 'string' },
    'plateau-zip': { type: 'string' },
    'cache-dir': { type: 'string', default: path.join(os.tmpdir(), 'pms-road-class-validation') },
    json: { type: 'string' },
  },
});
if (!values.name || !values['plateau-zip']) {
  throw new Error('--name と --plateau-zip を指定してください');
}

const workDir = path.join(values['cache-dir'], values.name);
const roadDir = path.join(workDir, 'tran');
fs.mkdirSync(roadDir, { recursive: true });

// ZIP 全体（数 GB）は取得せず、道路のファイルだけを Range で取る。取得済みは再利用する。
const zip = await openZip(await httpZipSource(values['plateau-zip']));
const roadEntries = zip.entries.filter((e) => e.name.startsWith(ROAD_ENTRY_PREFIX) && e.uncompressedSize > 0);
if (roadEntries.length === 0) {
  throw new Error(`${ROAD_ENTRY_PREFIX} にファイルがありません。道路（tran）を含まない ZIP です。`);
}
console.error(`PLATEAU の道路ファイル: ${roadEntries.length} 件`);
for (const entry of roadEntries) {
  const file = path.join(roadDir, path.basename(entry.name));
  if (!fs.existsSync(file)) {
    fs.writeFileSync(file, await zip.read(entry));
  }
}

// コードリストは配布物に同梱され、内容が版で変わりうるため、この ZIP 自身のものを読む。
const codelistEntry = zip.entries.find((e) => e.name === CODELIST_ENTRY);
if (!codelistEntry) {
  throw new Error(`${CODELIST_ENTRY} が ZIP にありません`);
}
const codelist = parseCodelist((await zip.read(codelistEntry)).toString('utf8'));

const index = new RoadIndex();
for (const file of fs.readdirSync(roadDir).filter((f) => f.endsWith('.gml'))) {
  index.addRoads(parseRoads(fs.readFileSync(path.join(roadDir, file), 'utf8')));
}
const { minLat, minLon, maxLat, maxLon } = index.extent;
console.error(`道路の面: ${index.polygonCount} 件 / 範囲 ${minLat.toFixed(3)},${minLon.toFixed(3)} - ${maxLat.toFixed(3)},${maxLon.toFixed(3)}`);

const osmFile = path.join(workDir, 'osm-roads.json');
let ways;
if (fs.existsSync(osmFile)) {
  ways = JSON.parse(fs.readFileSync(osmFile, 'utf8'));
} else {
  ways = await fetchRoadWays({ south: minLat, west: minLon, north: maxLat, east: maxLon });
  fs.writeFileSync(osmFile, JSON.stringify(ways));
}
console.error(`OSM の道路: ${ways.length} 本`);

const result = compare(ways, index);
console.log(`## ${values.name}（点単位）\n\n${formatTable(result.points, codelist)}\n`);
console.log(`## ${values.name}（線単位。多数決）\n\n${formatTable(result.ways, codelist)}`);
if (values.json) {
  fs.writeFileSync(values.json, JSON.stringify({ name: values.name, codelist, ...result }, null, 2));
}
