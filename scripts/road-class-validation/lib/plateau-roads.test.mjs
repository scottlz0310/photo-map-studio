import { describe, expect, test } from 'bun:test';
import { RoadIndex, parseRoads, pointInRing } from './plateau-roads.mjs';

// 緯度 35.0〜35.1 / 経度 135.0〜135.1 の正方形（EPSG:6697 は「緯度 経度 高さ」の並び）。
const SQUARE = '35.0 135.0 0 35.0 135.1 0 35.1 135.1 0 35.1 135.0 0 35.0 135.0 0';
const FAR_SQUARE = '36.0 136.0 0 36.0 136.1 0 36.1 136.1 0 36.1 136.0 0 36.0 136.0 0';

function road(id, fn, posList) {
  return `<tran:Road gml:id="${id}"><tran:function codeSpace="x">${fn}</tran:function>
    <tran:lod1MultiSurface><gml:MultiSurface><gml:surfaceMember><gml:Polygon>
    <gml:exterior><gml:LinearRing><gml:posList>${posList}</gml:posList></gml:LinearRing></gml:exterior>
    </gml:Polygon></gml:surfaceMember></gml:MultiSurface></tran:lod1MultiSurface></tran:Road>`;
}

describe('parseRoads', () => {
  test('機能コードと外周を、[緯度, 経度] の並びで取り出す', () => {
    const roads = parseRoads(road('a', '3', SQUARE) + road('b', '9020', FAR_SQUARE));

    expect(roads.map((r) => r.function)).toEqual(['3', '9020']);
    expect(roads[0].rings[0][0]).toEqual([35.0, 135.0]);
    expect(roads[0].rings[0].length).toBe(5);
  });

  test('機能のない道路は空の機能コードにする', () => {
    const gml = '<tran:Road gml:id="c"><core:creationDate>2023-01-01</core:creationDate></tran:Road>';

    expect(parseRoads(gml)).toEqual([{ function: '', rings: [] }]);
  });
});

describe('pointInRing', () => {
  const ring = [
    [35.0, 135.0],
    [35.0, 135.1],
    [35.1, 135.1],
    [35.1, 135.0],
  ];

  test.each([
    ['内側', 35.05, 135.05, true],
    ['緯度が外', 35.2, 135.05, false],
    ['経度が外', 35.05, 135.2, false],
  ])('%s', (_name, lat, lon, expected) => {
    expect(pointInRing(lat, lon, ring)).toBe(expected);
  });
});

describe('RoadIndex', () => {
  test('点を含む面の機能コードを返し、範囲を計算する', () => {
    const index = new RoadIndex();
    index.addRoads(parseRoads(road('a', '3', SQUARE) + road('b', '2', FAR_SQUARE)));

    expect([...index.functionsAt(35.05, 135.05)]).toEqual(['3']);
    expect([...index.functionsAt(36.05, 136.05)]).toEqual(['2']);
    expect(index.functionsAt(37, 137).size).toBe(0);
    expect(index.polygonCount).toBe(2);
    expect(index.extent).toEqual({ minLat: 35.0, minLon: 135.0, maxLat: 36.1, maxLon: 136.1 });
  });

  test('面が重なる点では、複数の機能コードを返す', () => {
    const index = new RoadIndex();
    index.addRoads(parseRoads(road('a', '3', SQUARE) + road('b', '2', SQUARE)));

    expect([...index.functionsAt(35.05, 135.05)].sort()).toEqual(['2', '3']);
  });
});
