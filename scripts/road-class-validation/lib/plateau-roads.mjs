const ROAD_PATTERN = /<tran:Road gml:id="[^"]+">([\s\S]*?)<\/tran:Road>/g;
const FUNCTION_PATTERN = /<tran:function[^>]*>([^<]*)</;
const EXTERIOR_PATTERN = /<gml:exterior>\s*<gml:LinearRing>\s*<gml:posList>([^<]*)<\/gml:posList>/g;

/**
 * PLATEAU の道路（tran:Road）の GML から、道路の機能コードと LOD1 の面の外周を取り出す。
 * 座標は EPSG:6697 の「緯度 経度 高さ」の並びで、[緯度, 経度] の配列にして返す。
 * @param {string} gmlText
 * @returns {{ function: string, rings: number[][][] }[]}
 */
export function parseRoads(gmlText) {
  const roads = [];
  for (const road of gmlText.matchAll(ROAD_PATTERN)) {
    const body = road[1];
    const rings = [];
    for (const exterior of body.matchAll(EXTERIOR_PATTERN)) {
      const values = exterior[1].trim().split(/\s+/).map(Number);
      const ring = [];
      for (let i = 0; i + 2 < values.length; i += 3) {
        ring.push([values[i], values[i + 1]]);
      }
      rings.push(ring);
    }
    roads.push({ function: FUNCTION_PATTERN.exec(body)?.[1] ?? '', rings });
  }
  return roads;
}

/** 点が多角形の内側にあるか（偶奇規則）。 */
export function pointInRing(lat, lon, ring) {
  let inside = false;
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
    const [latI, lonI] = ring[i];
    const [latJ, lonJ] = ring[j];
    if (lonI > lon !== lonJ > lon && lat < ((latJ - latI) * (lon - lonI)) / (lonJ - lonI) + latI) {
      inside = !inside;
    }
  }
  return inside;
}

/** 道路の面を格子に載せ、点を含む面の機能コードを引く索引。 */
export class RoadIndex {
  #cell;
  #polygons = [];
  #grid = new Map();
  #extent = { minLat: Infinity, minLon: Infinity, maxLat: -Infinity, maxLon: -Infinity };

  /** @param {number} cellDegrees 格子の大きさ（度）。 */
  constructor(cellDegrees = 0.001) {
    this.#cell = cellDegrees;
  }

  get polygonCount() {
    return this.#polygons.length;
  }

  get extent() {
    return { ...this.#extent };
  }

  /** @param {{ function: string, rings: number[][][] }[]} roads */
  addRoads(roads) {
    for (const road of roads) {
      for (const ring of road.rings) {
        this.#addPolygon(road.function, ring);
      }
    }
  }

  /** その点を含む面の機能コードの集合。 */
  functionsAt(lat, lon) {
    const found = new Set();
    for (const index of this.#grid.get(this.#key(lat, lon)) ?? []) {
      const polygon = this.#polygons[index];
      if (lat < polygon.minLat || lat > polygon.maxLat || lon < polygon.minLon || lon > polygon.maxLon) {
        continue;
      }
      if (pointInRing(lat, lon, polygon.ring)) {
        found.add(polygon.function);
      }
    }
    return found;
  }

  #key(lat, lon) {
    return `${Math.floor(lat / this.#cell)},${Math.floor(lon / this.#cell)}`;
  }

  #addPolygon(fn, ring) {
    let minLat = Infinity;
    let minLon = Infinity;
    let maxLat = -Infinity;
    let maxLon = -Infinity;
    for (const [lat, lon] of ring) {
      minLat = Math.min(minLat, lat);
      minLon = Math.min(minLon, lon);
      maxLat = Math.max(maxLat, lat);
      maxLon = Math.max(maxLon, lon);
    }
    const index = this.#polygons.length;
    this.#polygons.push({ function: fn, ring, minLat, minLon, maxLat, maxLon });
    this.#extent.minLat = Math.min(this.#extent.minLat, minLat);
    this.#extent.minLon = Math.min(this.#extent.minLon, minLon);
    this.#extent.maxLat = Math.max(this.#extent.maxLat, maxLat);
    this.#extent.maxLon = Math.max(this.#extent.maxLon, maxLon);
    for (let a = Math.floor(minLat / this.#cell); a <= Math.floor(maxLat / this.#cell); a++) {
      for (let b = Math.floor(minLon / this.#cell); b <= Math.floor(maxLon / this.#cell); b++) {
        const key = `${a},${b}`;
        const list = this.#grid.get(key);
        if (list) {
          list.push(index);
        } else {
          this.#grid.set(key, [index]);
        }
      }
    }
  }
}
