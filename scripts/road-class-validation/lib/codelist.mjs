const ENTRY_PATTERN = /<gml:description>([^<]*)<\/gml:description>\s*<gml:name>([^<]*)<\/gml:name>/g;

/**
 * PLATEAU のコードリスト（gml:Dictionary。Road_function.xml など）を「コード → 説明」にする。
 * コードリストは配布物に同梱され、版や自治体によって内容が変わりうるため、ZIP 自身のものを読む。
 * @param {string} xml
 * @returns {Record<string, string>}
 */
export function parseCodelist(xml) {
  const labels = {};
  for (const entry of xml.matchAll(ENTRY_PATTERN)) {
    labels[entry[2].trim()] = entry[1].trim();
  }
  if (Object.keys(labels).length === 0) {
    throw new Error('コードリストに項目がありません');
  }
  return labels;
}
