import { describe, expect, test } from 'bun:test';
import { parseCodelist } from './codelist.mjs';

const entry = (description, name) =>
  `<gml:dictionaryEntry><gml:Definition gml:id="x"><gml:description>${description}</gml:description><gml:name>${name}</gml:name></gml:Definition></gml:dictionaryEntry>`;

describe('parseCodelist', () => {
  test('コードと説明を対応づける（辞書自体の名前は項目に含めない）', () => {
    const xml = `<gml:Dictionary><gml:name>Road_function</gml:name>${entry('一般国道', '2')}${entry('不明', '9020')}</gml:Dictionary>`;

    expect(parseCodelist(xml)).toEqual({ 2: '一般国道', 9020: '不明' });
  });

  test('項目がなければ失敗する', () => {
    expect(() => parseCodelist('<gml:Dictionary/>')).toThrow('項目がありません');
  });
});
