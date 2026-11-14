import zlib from 'node:zlib';

const EOCD_SIGNATURE = 0x06054b50;
const ZIP64_LOCATOR_SIGNATURE = 0x07064b50;
const CENTRAL_HEADER_SIGNATURE = 0x02014b50;
const ZIP64_EXTRA_ID = 1;
const UTF8_FLAG = 0x800;
const MAX_COMMENT_LENGTH = 0xffff;
const EOCD_LENGTH = 22;
const ZIP64_LOCATOR_LENGTH = 20;
const ZIP64_END_RECORD_LENGTH = 56;
const LOCAL_HEADER_LENGTH = 30;
const STORED = 0;
const DEFLATED = 8;

/**
 * HTTP の Range で、巨大な ZIP の一部（中央ディレクトリと選んだエントリー）だけを読む取得元。
 * @param {string} url
 * @param {typeof fetch} fetchImpl
 */
export async function httpZipSource(url, fetchImpl = fetch) {
  const head = await fetchImpl(url, { method: 'HEAD' });
  if (!head.ok) {
    throw new Error(`ZIP のサイズを取得できません: HTTP ${head.status} ${url}`);
  }
  const size = Number(head.headers.get('content-length'));
  if (!Number.isSafeInteger(size) || size <= 0) {
    throw new Error(`ZIP の Content-Length が不正です: ${head.headers.get('content-length')} ${url}`);
  }
  return {
    size,
    async readRange(start, end) {
      const res = await fetchImpl(url, { headers: { Range: `bytes=${start}-${end}` } });
      if (res.status !== 206) {
        throw new Error(`Range ${start}-${end} が 206 で返りません: HTTP ${res.status} ${url}`);
      }
      return Buffer.from(await res.arrayBuffer());
    },
  };
}

/**
 * 末尾のバッファから、中央ディレクトリの位置・件数を求める（ZIP64 対応）。
 * 戻り値の `zip64RecordOffset` は、ZIP64 の終端レコードを別途読む必要があるときだけ入る。
 * @param {Buffer} tail ZIP の末尾
 */
export function findEndRecord(tail) {
  let eocd = -1;
  for (let i = tail.length - EOCD_LENGTH; i >= 0; i--) {
    if (tail.readUInt32LE(i) === EOCD_SIGNATURE) {
      eocd = i;
      break;
    }
  }
  if (eocd < 0) {
    throw new Error('ZIP の終端レコード（EOCD）が見つかりません');
  }
  const count = tail.readUInt16LE(eocd + 10);
  const size = tail.readUInt32LE(eocd + 12);
  const offset = tail.readUInt32LE(eocd + 16);
  const needsZip64 = count === 0xffff || size === 0xffffffff || offset === 0xffffffff;
  if (!needsZip64) {
    return { count, size, offset };
  }
  const locator = eocd - ZIP64_LOCATOR_LENGTH;
  if (locator < 0 || tail.readUInt32LE(locator) !== ZIP64_LOCATOR_SIGNATURE) {
    throw new Error('ZIP64 の位置情報が見つかりません');
  }
  return { zip64RecordOffset: Number(tail.readBigUInt64LE(locator + 8)) };
}

/**
 * 中央ディレクトリのバッファを、エントリーの一覧にする。
 * @param {Buffer} directory
 * @param {number} expectedCount
 */
export function parseCentralDirectory(directory, expectedCount) {
  const entries = [];
  let p = 0;
  while (p < directory.length && directory.readUInt32LE(p) === CENTRAL_HEADER_SIGNATURE) {
    const flags = directory.readUInt16LE(p + 8);
    const method = directory.readUInt16LE(p + 10);
    let compressedSize = directory.readUInt32LE(p + 20);
    let uncompressedSize = directory.readUInt32LE(p + 24);
    const nameLength = directory.readUInt16LE(p + 28);
    const extraLength = directory.readUInt16LE(p + 30);
    const commentLength = directory.readUInt16LE(p + 32);
    let localHeaderOffset = directory.readUInt32LE(p + 42);
    const nameStart = p + 46;
    const name = directory.toString(flags & UTF8_FLAG ? 'utf8' : 'latin1', nameStart, nameStart + nameLength);

    let extra = nameStart + nameLength;
    const extraEnd = extra + extraLength;
    while (extra + 4 <= extraEnd) {
      const id = directory.readUInt16LE(extra);
      const length = directory.readUInt16LE(extra + 2);
      if (id === ZIP64_EXTRA_ID) {
        // 0xffffffff だった項目だけが、この順で 8 バイトずつ入る。
        let q = extra + 4;
        if (uncompressedSize === 0xffffffff) {
          uncompressedSize = Number(directory.readBigUInt64LE(q));
          q += 8;
        }
        if (compressedSize === 0xffffffff) {
          compressedSize = Number(directory.readBigUInt64LE(q));
          q += 8;
        }
        if (localHeaderOffset === 0xffffffff) {
          localHeaderOffset = Number(directory.readBigUInt64LE(q));
        }
      }
      extra += 4 + length;
    }
    entries.push({ name, method, compressedSize, uncompressedSize, localHeaderOffset });
    p = nameStart + nameLength + extraLength + commentLength;
  }
  if (entries.length !== expectedCount) {
    throw new Error(`中央ディレクトリの件数が一致しません: 読めた ${entries.length} 件 / 記録 ${expectedCount} 件`);
  }
  return entries;
}

/**
 * ZIP を開き、エントリー一覧と、個別エントリーの読み出しを返す。
 * @param {{ size: number, readRange: (start: number, end: number) => Promise<Buffer> }} source
 */
export async function openZip(source) {
  const tailLength = Math.min(source.size, MAX_COMMENT_LENGTH + EOCD_LENGTH + ZIP64_LOCATOR_LENGTH);
  const tail = await source.readRange(source.size - tailLength, source.size - 1);
  let record = findEndRecord(tail);
  if (record.zip64RecordOffset !== undefined) {
    const z64 = await source.readRange(record.zip64RecordOffset, record.zip64RecordOffset + ZIP64_END_RECORD_LENGTH - 1);
    record = {
      count: Number(z64.readBigUInt64LE(32)),
      size: Number(z64.readBigUInt64LE(40)),
      offset: Number(z64.readBigUInt64LE(48)),
    };
  }
  const directory = await source.readRange(record.offset, record.offset + record.size - 1);
  const entries = parseCentralDirectory(directory, record.count);

  async function read(entry) {
    const header = await source.readRange(entry.localHeaderOffset, entry.localHeaderOffset + LOCAL_HEADER_LENGTH - 1);
    const dataStart = entry.localHeaderOffset + LOCAL_HEADER_LENGTH + header.readUInt16LE(26) + header.readUInt16LE(28);
    const data = await source.readRange(dataStart, dataStart + entry.compressedSize - 1);
    if (entry.method === DEFLATED) {
      return zlib.inflateRawSync(data);
    }
    if (entry.method === STORED) {
      return data;
    }
    throw new Error(`未対応の圧縮方式 ${entry.method}: ${entry.name}`);
  }

  return { entries, read };
}
