import assert from 'node:assert/strict';
import test from 'node:test';

import { findAgentTextViolations } from './check-agent-text.mjs';

const separatedI = 'ờ' + ' i';
const uppercaseSeparatedI = 'Ờ' + ' I';

test('phát hiện lỗi tách ký tự cuối sau ờ ở cả chữ thường và chữ hoa', () => {
  const violations = findAgentTextViolations(`th${separatedI} gian\nNGƯ${uppercaseSeparatedI} DÙNG`);

  assert.deepEqual(violations, [
    { line: 1, column: 3, found: separatedI },
    { line: 2, column: 4, found: uppercaseSeparatedI },
  ]);
});

test('chấp nhận từ đúng chính tả và biến i độc lập', () => {
  const correctlyJoinedText = 'thờ' + 'i gian\nngườ' + 'i dùng\nconst i = 1;';
  const violations = findAgentTextViolations(correctlyJoinedText);

  assert.deepEqual(violations, []);
});

test('báo đúng vị trí của nhiều lỗi trên cùng một dòng', () => {
  const violations = findAgentTextViolations(`gi${separatedI}, th${separatedI}`);

  assert.deepEqual(violations, [
    { line: 1, column: 3, found: separatedI },
    { line: 1, column: 10, found: separatedI },
  ]);
});

test('phát hiện lỗi tách ký tự cuối sau các nguyên âm có dấu khác', () => {
  const samples = [
    ['đ', 'ợ'],
    ['ph', 'ả'],
    ['t', 'ố'],
    ['t', 'ạ'],
    ['l', 'ạ'],
    ['cu', 'ố'],
    ['đ', 'ổ'],
    ['m', 'ớ'],
    ['r', 'ồ'],
  ];

  for (const [prefix, vowel] of samples) {
    const invalidEnding = vowel + ' i';
    const violations = findAgentTextViolations(prefix + invalidEnding);

    assert.deepEqual(violations, [
      { line: 1, column: prefix.length + 1, found: invalidEnding },
    ], prefix + vowel + 'i');
  }
});
