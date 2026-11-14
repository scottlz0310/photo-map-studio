import { describe, expect, test } from 'bun:test';
import { categorize, compare, determinateTotal, formatTable } from './compare.mjs';

describe('categorize', () => {
  test.each([
    ['面なし', [], 'none'],
    ['単一', ['3'], '3'],
    ['不明', ['9020'], '9020'],
    ['重なり', ['2', '3'], 'overlap'],
    ['不明との重なりも重なり', ['9020', '3'], 'overlap'],
  ])('%s', (_name, functions, expected) => {
    expect(categorize(new Set(functions))).toBe(expected);
  });
});

/** 経度 135.00 / 135.01 / 135.02 の 3 区画。区画ごとに機能コードを割り当てる索引。 */
function fakeIndex(byLonCell) {
  return {
    functionsAt: (_lat, lon) => new Set(byLonCell[Math.floor((lon - 135) * 100 + 1e-9)] ?? []),
  };
}

const way = (highway, lons) => ({
  tags: { highway },
  geometry: lons.map((lon) => ({ lat: 35, lon })),
});

describe('compare', () => {
  test('点は線分の中点ごとに、線は多数決で集計する', () => {
    // primary: 中点が 135.005 / 135.015 / 135.025 → 区画 0 / 1 / 2
    const index = fakeIndex({ 0: ['3'], 1: ['3'], 2: ['2'] });
    const ways = [way('primary', [135.0, 135.01, 135.02, 135.03])];

    const result = compare(ways, index);

    expect(result.points).toEqual({ primary: { 3: 2, 2: 1 } });
    expect(result.ways).toEqual({ primary: { 3: 1 } });
  });

  test('面なしと重なりは多数決から除く。実の区分が 1 つもなければ面なし、重なりだけなら重なり', () => {
    const index = fakeIndex({ 0: ['3'], 1: [], 2: ['2', '3'] });

    const result = compare(
      [way('tertiary', [135.0, 135.01, 135.02]), way('trunk', [135.01, 135.02]), way('secondary', [135.02, 135.03])],
      index,
    );

    expect(result.ways).toEqual({ tertiary: { 3: 1 }, trunk: { none: 1 }, secondary: { overlap: 1 } });
    expect(result.points.tertiary).toEqual({ 3: 1, none: 1 });
  });
});

describe('formatTable', () => {
  const codelist = { 2: '一般国道', 3: '都道府県道', 9020: '不明' };

  test('割合の分母は分類が決まった点だけ。面なし・面が重なるは分母に含めず、件数だけを示す', () => {
    const table = formatTable({ primary: { 3: 3, 2: 1, overlap: 5, none: 6 } }, codelist);

    expect(table.split('\n')).toEqual([
      '| OSM の highway | 一般国道 | 都道府県道 | 合計 | 面が重なる | 面なし |',
      '| --- | --- | --- | --- | --- | --- |',
      '| primary | 1（25.0%） | 3（75.0%） | 4 | 5 | 6 |',
    ]);
  });

  test('コードリストにない値も落とさず、その旨を見出しに示す', () => {
    const table = formatTable({ trunk: { 2: 1, 9: 2, overlap: 1 }, tertiary: { 9020: 4 } }, codelist);

    expect(table.split('\n')[0]).toBe(
      '| OSM の highway | 一般国道 | コード 9（コードリスト外） | 不明 | 合計 | 面が重なる | 面なし |',
    );
    expect(table.split('\n')[2]).toBe('| trunk | 1（33.3%） | 2（66.7%） | - | 3 | 1 | 0 |');
  });
});

describe('determinateTotal', () => {
  test.each([
    ['機能コードだけ', { 2: 3, 3: 1 }, 4],
    ['面なしと重なりは除く', { 2: 3, overlap: 10, none: 20 }, 3],
    ['空', {}, 0],
  ])('%s', (_name, row, expected) => {
    expect(determinateTotal(row)).toBe(expected);
  });
});
