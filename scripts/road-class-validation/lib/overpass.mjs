export const ROAD_CLASSES = ['motorway', 'trunk', 'primary', 'secondary', 'tertiary'];

const DEFAULT_ENDPOINT = 'https://overpass-api.de/api/interpreter';
const USER_AGENT = 'photo-map-studio-road-class-validation (https://github.com/scottlz0310/photo-map-studio)';

/**
 * bbox を、1 回の問い合わせが重くなりすぎない大きさに分ける。
 * @param {{ south: number, west: number, north: number, east: number }} bbox
 * @param {number} maxSpanDegrees
 */
export function splitBbox(bbox, maxSpanDegrees = 0.25) {
  const rows = Math.ceil((bbox.north - bbox.south) / maxSpanDegrees);
  const columns = Math.ceil((bbox.east - bbox.west) / maxSpanDegrees);
  const latStep = (bbox.north - bbox.south) / rows;
  const lonStep = (bbox.east - bbox.west) / columns;
  const tiles = [];
  for (let r = 0; r < rows; r++) {
    for (let c = 0; c < columns; c++) {
      tiles.push({
        south: bbox.south + r * latStep,
        north: bbox.south + (r + 1) * latStep,
        west: bbox.west + c * lonStep,
        east: bbox.west + (c + 1) * lonStep,
      });
    }
  }
  return tiles;
}

export function buildQuery(bbox) {
  const classes = ROAD_CLASSES.join('|');
  const area = `${bbox.south},${bbox.west},${bbox.north},${bbox.east}`;
  return `[out:json][timeout:120];way["highway"~"^(${classes})$"](${area});out geom tags;`;
}

/**
 * Overpass API から、bbox 内の主要道路（motorway〜tertiary）の線を取得する。
 * 一時的な混雑（429・5xx）は間隔を空けて再試行し、尽きたら失敗として伝える。
 * @param {{ south: number, west: number, north: number, east: number }} bbox
 * @param {{ fetchImpl?: typeof fetch, sleep?: (ms: number) => Promise<void>, endpoint?: string, attempts?: number, retryDelayMs?: number, maxSpanDegrees?: number }} [options]
 */
export async function fetchRoadWays(bbox, options = {}) {
  const {
    fetchImpl = fetch,
    sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
    endpoint = DEFAULT_ENDPOINT,
    attempts = 4,
    retryDelayMs = 20_000,
    maxSpanDegrees = 0.25,
  } = options;

  const ways = new Map();
  for (const tile of splitBbox(bbox, maxSpanDegrees)) {
    const body = new URLSearchParams({ data: buildQuery(tile) });
    let response;
    for (let attempt = 1; attempt <= attempts; attempt++) {
      response = await fetchImpl(endpoint, {
        method: 'POST',
        headers: { 'User-Agent': USER_AGENT },
        body,
      });
      if (response.ok) {
        break;
      }
      const retryable = response.status === 429 || response.status >= 500;
      if (!retryable || attempt === attempts) {
        throw new Error(`Overpass が失敗しました: HTTP ${response.status}（${attempt} 回目）`);
      }
      await sleep(retryDelayMs);
    }
    const json = await response.json();
    for (const way of json.elements) {
      ways.set(way.id, way);
    }
  }
  return [...ways.values()];
}
