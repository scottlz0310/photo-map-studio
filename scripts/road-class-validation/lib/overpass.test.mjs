import { describe, expect, test } from 'bun:test';
import { buildQuery, fetchRoadWays, splitBbox } from './overpass.mjs';

const BBOX = { south: 35.0, west: 135.0, north: 35.1, east: 135.1 };

function response(status, elements = [], remark) {
  return { ok: status >= 200 && status < 300, status, json: async () => ({ elements, remark }) };
}

describe('splitBbox', () => {
  test.each([
    ['上限以内は分けない', { south: 0, west: 0, north: 0.2, east: 0.2 }, 0.25, 1],
    ['緯度方向に分ける', { south: 0, west: 0, north: 0.5, east: 0.2 }, 0.25, 2],
    ['縦横に分ける', { south: 0, west: 0, north: 0.6, east: 0.3 }, 0.25, 6],
  ])('%s', (_name, bbox, span, expected) => {
    const tiles = splitBbox(bbox, span);

    expect(tiles.length).toBe(expected);
    expect(Math.min(...tiles.map((t) => t.south))).toBe(bbox.south);
    expect(Math.max(...tiles.map((t) => t.north))).toBeCloseTo(bbox.north, 10);
    expect(Math.max(...tiles.map((t) => t.east))).toBeCloseTo(bbox.east, 10);
  });
});

describe('buildQuery', () => {
  test('主要道路の種別と bbox（南,西,北,東）を含む', () => {
    const query = buildQuery(BBOX);

    expect(query).toContain('motorway|trunk|primary|secondary|tertiary');
    expect(query).toContain('(35,135,35.1,135.1)');
  });
});

describe('fetchRoadWays', () => {
  const noSleep = async () => {};

  test('混雑（502）は再試行し、複数の問い合わせで重複した線は 1 本にまとめる', async () => {
    const calls = [];
    const fetchImpl = async () => {
      calls.push(1);
      if (calls.length === 1) return response(502);
      return response(200, [{ id: 1 }, { id: 2 }]);
    };
    const sleeps = [];

    const ways = await fetchRoadWays({ south: 0, west: 0, north: 0.5, east: 0.2 }, {
      fetchImpl,
      sleep: async (ms) => sleeps.push(ms),
      retryDelayMs: 5,
      maxSpanDegrees: 0.25,
    });

    expect(ways.map((w) => w.id)).toEqual([1, 2]);
    expect(calls.length).toBe(3);
    expect(sleeps).toEqual([5]);
  });

  test.each([
    ['タイムアウト', 'runtime error: Query timed out in "query" at line 1 after 181 seconds.'],
    ['メモリ不足', 'runtime error: Query run out of memory using about 2048 MB of RAM.'],
  ])('HTTP 200 でも remark の実行時エラー（%s）は失敗にする。分割した問い合わせの一部だけでも全体を失敗にする', async (_name, remark) => {
    const fetchImpl = async (_url, init) => {
      const failing = init.body.get('data').includes('(0.25,0,0.5,0.2)');
      return failing ? response(200, [], remark) : response(200, [{ id: 1 }]);
    };

    await expect(
      fetchRoadWays({ south: 0, west: 0, north: 0.5, east: 0.2 }, { fetchImpl, sleep: noSleep, maxSpanDegrees: 0.25 }),
    ).rejects.toThrow('Overpass の実行時エラー');
  });

  test('実行時エラーではない remark（注記）は受け入れる', async () => {
    const fetchImpl = async () => response(200, [{ id: 1 }], 'attribution notice');

    expect((await fetchRoadWays(BBOX, { fetchImpl, sleep: noSleep })).map((w) => w.id)).toEqual([1]);
  });

  test.each([
    ['再試行しない 4xx', 400, 1],
    ['再試行を使い切る 503', 503, 3],
  ])('%s は失敗として伝える', async (_name, status, expectedCalls) => {
    let calls = 0;
    const fetchImpl = async () => {
      calls++;
      return response(status);
    };

    await expect(fetchRoadWays(BBOX, { fetchImpl, sleep: noSleep, attempts: 3 })).rejects.toThrow(`HTTP ${status}`);
    expect(calls).toBe(expectedCalls);
  });
});
