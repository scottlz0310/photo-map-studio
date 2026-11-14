import { ROAD_CLASSES } from './overpass.mjs';

/** 集計で、機能コードとは別に使う区分。 */
const OVERLAP = 'overlap';
const NONE = 'none';
const OVERLAP_LABEL = '面が重なる';
const NONE_LABEL = '面なし';

/**
 * 点を含む面の機能コードの集合から、集計用の区分を決める。
 * 交差点では面が重なり、どちらの道路の分類か決められないため、'overlap' として別に数える。
 * @param {Set<string>} functions
 */
export function categorize(functions) {
  if (functions.size === 0) {
    return NONE;
  }
  if (functions.size > 1) {
    return OVERLAP;
  }
  return [...functions][0];
}

function increment(tally, row, category) {
  tally[row] ??= {};
  tally[row][category] = (tally[row][category] ?? 0) + 1;
}

/**
 * OSM の道路の線を点（隣り合う頂点の中点）に刻み、PLATEAU の道路の面で分類を引いて集計する。
 * 点単位は線の長さに比例した重みになり、線単位は点の多数決（面なし・重なりを除く）で決める。
 * @param {{ tags: { highway: string }, geometry: { lat: number, lon: number }[] }[]} ways
 * @param {{ functionsAt: (lat: number, lon: number) => Set<string> }} index
 */
export function compare(ways, index) {
  const points = {};
  const perWay = {};
  for (const way of ways) {
    const highway = way.tags.highway;
    const votes = {};
    let sawNone = false;
    for (let i = 0; i + 1 < way.geometry.length; i++) {
      const lat = (way.geometry[i].lat + way.geometry[i + 1].lat) / 2;
      const lon = (way.geometry[i].lon + way.geometry[i + 1].lon) / 2;
      const category = categorize(index.functionsAt(lat, lon));
      increment(points, highway, category);
      if (category === NONE) {
        sawNone = true;
      } else if (category !== OVERLAP) {
        votes[category] = (votes[category] ?? 0) + 1;
      }
    }
    const top = Object.keys(votes).sort((a, b) => votes[b] - votes[a])[0];
    increment(perWay, highway, top ?? (sawNone ? NONE : OVERLAP));
  }
  return { points, ways: perWay };
}

function columnLabel(code, codelist) {
  if (code === OVERLAP) {
    return OVERLAP_LABEL;
  }
  return codelist[code] ?? `コード ${code}（コードリスト外）`;
}

/**
 * 割合の分母にする、分類が決まった点の数。
 * 「面なし」（PLATEAU の整備範囲の外）と「面が重なる」（どちらの道路か決められない）は、分類が決まらないため含めない。
 * @param {Record<string, number>} row 区分 → 件数
 */
export function determinateTotal(row) {
  return Object.entries(row)
    .filter(([category]) => category !== NONE && category !== OVERLAP)
    .reduce((sum, [, count]) => sum + count, 0);
}

/**
 * 集計を Markdown の表にする。機能コードの列は「件数（割合）」で、割合の分母は {@link determinateTotal}。
 * 「面が重なる」「面なし」は分母に含めず、件数だけを示す。列の見出しはコードリストの説明を使い、
 * コードリストにない値も落とさずに示す（配布物のデータの不備を見逃さないため）。
 * @param {Record<string, Record<string, number>>} tally
 * @param {Record<string, string>} codelist 機能コード → 説明
 */
export function formatTable(tally, codelist) {
  const present = new Set(Object.values(tally).flatMap((row) => Object.keys(row)));
  const codes = [...present].filter((c) => c !== NONE && c !== OVERLAP).sort((a, b) => Number(a) - Number(b));
  const header = [
    'OSM の highway',
    ...codes.map((c) => columnLabel(c, codelist)),
    '合計',
    OVERLAP_LABEL,
    NONE_LABEL,
  ];
  const lines = [`| ${header.join(' | ')} |`, `| ${header.map(() => '---').join(' | ')} |`];
  for (const highway of ROAD_CLASSES.filter((h) => tally[h])) {
    const row = tally[highway];
    const total = determinateTotal(row);
    const cells = codes.map((c) => (row[c] ? `${row[c]}（${((row[c] / total) * 100).toFixed(1)}%）` : '-'));
    lines.push(`| ${[highway, ...cells, total, row[OVERLAP] ?? 0, row[NONE] ?? 0].join(' | ')} |`);
  }
  return lines.join('\n');
}
