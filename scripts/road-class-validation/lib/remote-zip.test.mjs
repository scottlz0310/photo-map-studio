import { describe, expect, test } from 'bun:test';
import zlib from 'node:zlib';
import { findEndRecord, openZip } from './remote-zip.mjs';

const STORED = 0;
const DEFLATED = 8;

/** テスト用に ZIP（任意で ZIP64 形式）をメモリ上に組み立てる。CRC は検証しないため 0 とする。 */
function buildZip(files, { zip64 }) {
  const locals = [];
  const centrals = [];
  let offset = 0;
  for (const file of files) {
    const name = Buffer.from(file.name, 'utf8');
    const data = file.method === DEFLATED ? zlib.deflateRawSync(file.content) : file.content;
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(0x800, 6);
    local.writeUInt16LE(file.method, 8);
    local.writeUInt32LE(data.length, 18);
    local.writeUInt32LE(file.content.length, 22);
    local.writeUInt16LE(name.length, 26);
    locals.push(local, name, data);

    const extra = zip64 ? Buffer.alloc(28) : Buffer.alloc(0);
    if (zip64) {
      extra.writeUInt16LE(1, 0);
      extra.writeUInt16LE(24, 2);
      extra.writeBigUInt64LE(BigInt(file.content.length), 4);
      extra.writeBigUInt64LE(BigInt(data.length), 12);
      extra.writeBigUInt64LE(BigInt(offset), 20);
    }
    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(0x800, 8);
    central.writeUInt16LE(file.method, 10);
    central.writeUInt32LE(zip64 ? 0xffffffff : data.length, 20);
    central.writeUInt32LE(zip64 ? 0xffffffff : file.content.length, 24);
    central.writeUInt16LE(name.length, 28);
    central.writeUInt16LE(extra.length, 30);
    central.writeUInt32LE(zip64 ? 0xffffffff : offset, 42);
    centrals.push(central, name, extra);
    offset += 30 + name.length + data.length;
  }
  const centralBytes = Buffer.concat(centrals);
  const eocd = Buffer.alloc(22);
  eocd.writeUInt32LE(0x06054b50, 0);
  const parts = [...locals, centralBytes];
  if (zip64) {
    const record = Buffer.alloc(56);
    record.writeUInt32LE(0x06064b50, 0);
    record.writeBigUInt64LE(44n, 4);
    record.writeBigUInt64LE(BigInt(files.length), 24);
    record.writeBigUInt64LE(BigInt(files.length), 32);
    record.writeBigUInt64LE(BigInt(centralBytes.length), 40);
    record.writeBigUInt64LE(BigInt(offset), 48);
    const locator = Buffer.alloc(20);
    locator.writeUInt32LE(0x07064b50, 0);
    locator.writeBigUInt64LE(BigInt(offset + centralBytes.length), 8);
    locator.writeUInt32LE(1, 16);
    eocd.writeUInt16LE(0xffff, 10);
    eocd.writeUInt32LE(0xffffffff, 12);
    eocd.writeUInt32LE(0xffffffff, 16);
    parts.push(record, locator);
  } else {
    eocd.writeUInt16LE(files.length, 10);
    eocd.writeUInt32LE(centralBytes.length, 12);
    eocd.writeUInt32LE(offset, 16);
  }
  parts.push(eocd);
  return Buffer.concat(parts);
}

function bufferSource(bytes) {
  const requests = [];
  return {
    size: bytes.length,
    requests,
    async readRange(start, end) {
      requests.push([start, end]);
      return bytes.subarray(start, end + 1);
    },
  };
}

const files = [
  { name: 'udx/tran/a.gml', content: Buffer.from('<tran>日本語</tran>'.repeat(50)), method: DEFLATED },
  { name: 'codelists/x.xml', content: Buffer.from('plain'), method: STORED },
  { name: 'udx/tran/', content: Buffer.alloc(0), method: STORED },
];

describe('openZip', () => {
  test.each([
    ['通常の ZIP', false],
    ['ZIP64', true],
  ])('%s: 一覧とエントリーの内容を読める', async (_name, zip64) => {
    const source = bufferSource(buildZip(files, { zip64 }));
    const zip = await openZip(source);

    expect(zip.entries.map((e) => e.name)).toEqual(files.map((f) => f.name));
    for (const file of files) {
      const entry = zip.entries.find((e) => e.name === file.name);
      expect(entry.uncompressedSize).toBe(file.content.length);
      if (file.content.length > 0) {
        expect((await zip.read(entry)).equals(file.content)).toBe(true);
      }
    }
  });

  test('個別エントリーの読み出しは、そのエントリーの範囲だけを取得する', async () => {
    const source = bufferSource(buildZip(files, { zip64: false }));
    const zip = await openZip(source);
    source.requests.length = 0;

    await zip.read(zip.entries[1]);

    expect(source.requests.length).toBe(2);
  });

  test('未対応の圧縮方式は失敗する', async () => {
    const bytes = buildZip([{ name: 'a', content: Buffer.from('x'), method: STORED }], { zip64: false });
    const zip = await openZip(bufferSource(bytes));
    zip.entries[0].method = 99;

    await expect(zip.read(zip.entries[0])).rejects.toThrow('未対応の圧縮方式 99');
  });
});

describe('findEndRecord', () => {
  test('終端レコードがなければ失敗する', () => {
    expect(() => findEndRecord(Buffer.alloc(64))).toThrow('EOCD');
  });

  test('ZIP64 なのに位置情報がなければ失敗する', () => {
    const eocd = Buffer.alloc(22);
    eocd.writeUInt32LE(0x06054b50, 0);
    eocd.writeUInt32LE(0xffffffff, 16);

    expect(() => findEndRecord(Buffer.concat([Buffer.alloc(40), eocd]))).toThrow('ZIP64');
  });
});
