import 'dart:convert';
import 'dart:io';

const int _encrypt = 1;
const int _decrypt = 0;
const List<int> _qrcKey = <int>[
  0x21,
  0x40,
  0x23,
  0x29,
  0x28,
  0x2a,
  0x24,
  0x25,
  0x31,
  0x32,
  0x33,
  0x5a,
  0x58,
  0x43,
  0x21,
  0x40,
  0x21,
  0x40,
  0x23,
  0x29,
  0x28,
  0x4e,
  0x48,
  0x4c,
];

const List<List<int>> _sBoxes = <List<int>>[
  <int>[
    14, 4, 13, 1, 2, 15, 11, 8, 3, 10, 6, 12, 5, 9, 0, 7,
    0, 15, 7, 4, 14, 2, 13, 1, 10, 6, 12, 11, 9, 5, 3, 8,
    4, 1, 14, 8, 13, 6, 2, 11, 15, 12, 9, 7, 3, 10, 5, 0,
    15, 12, 8, 2, 4, 9, 1, 7, 5, 11, 3, 14, 10, 0, 6, 13,
  ],
  <int>[
    15, 1, 8, 14, 6, 11, 3, 4, 9, 7, 2, 13, 12, 0, 5, 10,
    3, 13, 4, 7, 15, 2, 8, 15, 12, 0, 1, 10, 6, 9, 11, 5,
    0, 14, 7, 11, 10, 4, 13, 1, 5, 8, 12, 6, 9, 3, 2, 15,
    13, 8, 10, 1, 3, 15, 4, 2, 11, 6, 7, 12, 0, 5, 14, 9,
  ],
  <int>[
    10, 0, 9, 14, 6, 3, 15, 5, 1, 13, 12, 7, 11, 4, 2, 8,
    13, 7, 0, 9, 3, 4, 6, 10, 2, 8, 5, 14, 12, 11, 15, 1,
    13, 6, 4, 9, 8, 15, 3, 0, 11, 1, 2, 12, 5, 10, 14, 7,
    1, 10, 13, 0, 6, 9, 8, 7, 4, 15, 14, 3, 11, 5, 2, 12,
  ],
  <int>[
    7, 13, 14, 3, 0, 6, 9, 10, 1, 2, 8, 5, 11, 12, 4, 15,
    13, 8, 11, 5, 6, 15, 0, 3, 4, 7, 2, 12, 1, 10, 14, 9,
    10, 6, 9, 0, 12, 11, 7, 13, 15, 1, 3, 14, 5, 2, 8, 4,
    3, 15, 0, 6, 10, 10, 13, 8, 9, 4, 5, 11, 12, 7, 2, 14,
  ],
  <int>[
    2, 12, 4, 1, 7, 10, 11, 6, 8, 5, 3, 15, 13, 0, 14, 9,
    14, 11, 2, 12, 4, 7, 13, 1, 5, 0, 15, 10, 3, 9, 8, 6,
    4, 2, 1, 11, 10, 13, 7, 8, 15, 9, 12, 5, 6, 3, 0, 14,
    11, 8, 12, 7, 1, 14, 2, 13, 6, 15, 0, 9, 10, 4, 5, 3,
  ],
  <int>[
    12, 1, 10, 15, 9, 2, 6, 8, 0, 13, 3, 4, 14, 7, 5, 11,
    10, 15, 4, 2, 7, 12, 9, 5, 6, 1, 13, 14, 0, 11, 3, 8,
    9, 14, 15, 5, 2, 8, 12, 3, 7, 0, 4, 10, 1, 13, 11, 6,
    4, 3, 2, 12, 9, 5, 15, 10, 11, 14, 1, 7, 6, 0, 8, 13,
  ],
  <int>[
    4, 11, 2, 14, 15, 0, 8, 13, 3, 12, 9, 7, 5, 10, 6, 1,
    13, 0, 11, 7, 4, 9, 1, 10, 14, 3, 5, 12, 2, 15, 8, 6,
    1, 4, 11, 13, 12, 3, 7, 14, 10, 15, 6, 8, 0, 5, 9, 2,
    6, 11, 13, 8, 1, 4, 10, 7, 9, 5, 0, 15, 14, 2, 3, 12,
  ],
  <int>[
    13, 2, 8, 4, 6, 15, 11, 1, 10, 9, 3, 14, 5, 0, 12, 7,
    1, 15, 13, 8, 10, 3, 7, 4, 12, 5, 6, 11, 0, 14, 9, 2,
    7, 11, 4, 1, 9, 12, 14, 2, 0, 6, 10, 13, 15, 3, 5, 8,
    2, 1, 14, 7, 4, 10, 8, 13, 15, 12, 9, 0, 3, 5, 6, 11,
  ],
];

const List<int> _keyRoundShift = <int>[
  1, 1, 2, 2, 2, 2, 2, 2, 1, 2, 2, 2, 2, 2, 2, 1,
];
const List<int> _keyPermC = <int>[
  56, 48, 40, 32, 24, 16, 8, 0, 57, 49, 41, 33, 25, 17,
  9, 1, 58, 50, 42, 34, 26, 18, 10, 2, 59, 51, 43, 35,
];
const List<int> _keyPermD = <int>[
  62, 54, 46, 38, 30, 22, 14, 6, 61, 53, 45, 37, 29, 21,
  13, 5, 60, 52, 44, 36, 28, 20, 12, 4, 27, 19, 11, 3,
];
const List<int> _keyCompression = <int>[
  13, 16, 10, 23, 0, 4, 2, 27, 14, 5, 20, 9,
  22, 18, 11, 3, 25, 7, 15, 6, 26, 19, 12, 1,
  40, 51, 30, 36, 46, 54, 29, 39, 50, 44, 32, 47,
  43, 48, 38, 55, 33, 52, 45, 41, 49, 35, 28, 31,
];

String? decryptQqMusicLyrics(String encryptedLyrics) {
  final trimmed = encryptedLyrics.trim();
  if (trimmed.isEmpty || trimmed.length.isOdd) return null;

  try {
    final encryptedBytes = _hexStringToBytes(trimmed);
    final schedule = _tripleDesKeySetup(_qrcKey, _decrypt);
    final output = <int>[];

    for (var i = 0; i + 8 <= encryptedBytes.length; i += 8) {
      output.addAll(
        _tripleDesCrypt(
          encryptedBytes.sublist(i, i + 8),
          schedule,
        ),
      );
    }

    final decoded = ZLibDecoder().convert(output);
    return utf8.decode(decoded, allowMalformed: true);
  } catch (_) {
    return null;
  }
}

List<int> _hexStringToBytes(String input) {
  return List<int>.generate(
    input.length ~/ 2,
    (int index) => int.parse(input.substring(index * 2, index * 2 + 2), radix: 16),
    growable: false,
  );
}

int _bitNum(List<int> data, int bitIndex, int shift) {
  final value = (data[(bitIndex ~/ 32) * 4 + 3 - ((bitIndex % 32) ~/ 8)] >>
          (7 - (bitIndex % 8))) &
      0x01;
  return value << shift;
}

int _bitNumIntr(int value, int bitIndex, int shift) {
  return ((value >> (31 - bitIndex)) & 0x01) << shift;
}

int _bitNumIntl(int value, int shiftLeft, int shiftRight) {
  return (((value << shiftLeft) & 0x80000000) >> shiftRight) & 0xffffffff;
}

int _sBoxBit(int value) {
  return (value & 0x20) | ((value & 0x1f) >> 1) | ((value & 0x01) << 4);
}

List<int> _initialPermutation(List<int> input) {
  final left = _bitNum(input, 57, 31) |
      _bitNum(input, 49, 30) |
      _bitNum(input, 41, 29) |
      _bitNum(input, 33, 28) |
      _bitNum(input, 25, 27) |
      _bitNum(input, 17, 26) |
      _bitNum(input, 9, 25) |
      _bitNum(input, 1, 24) |
      _bitNum(input, 59, 23) |
      _bitNum(input, 51, 22) |
      _bitNum(input, 43, 21) |
      _bitNum(input, 35, 20) |
      _bitNum(input, 27, 19) |
      _bitNum(input, 19, 18) |
      _bitNum(input, 11, 17) |
      _bitNum(input, 3, 16) |
      _bitNum(input, 61, 15) |
      _bitNum(input, 53, 14) |
      _bitNum(input, 45, 13) |
      _bitNum(input, 37, 12) |
      _bitNum(input, 29, 11) |
      _bitNum(input, 21, 10) |
      _bitNum(input, 13, 9) |
      _bitNum(input, 5, 8) |
      _bitNum(input, 63, 7) |
      _bitNum(input, 55, 6) |
      _bitNum(input, 47, 5) |
      _bitNum(input, 39, 4) |
      _bitNum(input, 31, 3) |
      _bitNum(input, 23, 2) |
      _bitNum(input, 15, 1) |
      _bitNum(input, 7, 0);

  final right = _bitNum(input, 56, 31) |
      _bitNum(input, 48, 30) |
      _bitNum(input, 40, 29) |
      _bitNum(input, 32, 28) |
      _bitNum(input, 24, 27) |
      _bitNum(input, 16, 26) |
      _bitNum(input, 8, 25) |
      _bitNum(input, 0, 24) |
      _bitNum(input, 58, 23) |
      _bitNum(input, 50, 22) |
      _bitNum(input, 42, 21) |
      _bitNum(input, 34, 20) |
      _bitNum(input, 26, 19) |
      _bitNum(input, 18, 18) |
      _bitNum(input, 10, 17) |
      _bitNum(input, 2, 16) |
      _bitNum(input, 60, 15) |
      _bitNum(input, 52, 14) |
      _bitNum(input, 44, 13) |
      _bitNum(input, 36, 12) |
      _bitNum(input, 28, 11) |
      _bitNum(input, 20, 10) |
      _bitNum(input, 12, 9) |
      _bitNum(input, 4, 8) |
      _bitNum(input, 62, 7) |
      _bitNum(input, 54, 6) |
      _bitNum(input, 46, 5) |
      _bitNum(input, 38, 4) |
      _bitNum(input, 30, 3) |
      _bitNum(input, 22, 2) |
      _bitNum(input, 14, 1) |
      _bitNum(input, 6, 0);

  return <int>[left & 0xffffffff, right & 0xffffffff];
}

List<int> _inversePermutation(int left, int right) {
  return <int>[
    _bitNumIntr(right, 4, 7) |
        _bitNumIntr(left, 4, 6) |
        _bitNumIntr(right, 12, 5) |
        _bitNumIntr(left, 12, 4) |
        _bitNumIntr(right, 20, 3) |
        _bitNumIntr(left, 20, 2) |
        _bitNumIntr(right, 28, 1) |
        _bitNumIntr(left, 28, 0),
    _bitNumIntr(right, 5, 7) |
        _bitNumIntr(left, 5, 6) |
        _bitNumIntr(right, 13, 5) |
        _bitNumIntr(left, 13, 4) |
        _bitNumIntr(right, 21, 3) |
        _bitNumIntr(left, 21, 2) |
        _bitNumIntr(right, 29, 1) |
        _bitNumIntr(left, 29, 0),
    _bitNumIntr(right, 6, 7) |
        _bitNumIntr(left, 6, 6) |
        _bitNumIntr(right, 14, 5) |
        _bitNumIntr(left, 14, 4) |
        _bitNumIntr(right, 22, 3) |
        _bitNumIntr(left, 22, 2) |
        _bitNumIntr(right, 30, 1) |
        _bitNumIntr(left, 30, 0),
    _bitNumIntr(right, 7, 7) |
        _bitNumIntr(left, 7, 6) |
        _bitNumIntr(right, 15, 5) |
        _bitNumIntr(left, 15, 4) |
        _bitNumIntr(right, 23, 3) |
        _bitNumIntr(left, 23, 2) |
        _bitNumIntr(right, 31, 1) |
        _bitNumIntr(left, 31, 0),
    _bitNumIntr(right, 0, 7) |
        _bitNumIntr(left, 0, 6) |
        _bitNumIntr(right, 8, 5) |
        _bitNumIntr(left, 8, 4) |
        _bitNumIntr(right, 16, 3) |
        _bitNumIntr(left, 16, 2) |
        _bitNumIntr(right, 24, 1) |
        _bitNumIntr(left, 24, 0),
    _bitNumIntr(right, 1, 7) |
        _bitNumIntr(left, 1, 6) |
        _bitNumIntr(right, 9, 5) |
        _bitNumIntr(left, 9, 4) |
        _bitNumIntr(right, 17, 3) |
        _bitNumIntr(left, 17, 2) |
        _bitNumIntr(right, 25, 1) |
        _bitNumIntr(left, 25, 0),
    _bitNumIntr(right, 2, 7) |
        _bitNumIntr(left, 2, 6) |
        _bitNumIntr(right, 10, 5) |
        _bitNumIntr(left, 10, 4) |
        _bitNumIntr(right, 18, 3) |
        _bitNumIntr(left, 18, 2) |
        _bitNumIntr(right, 26, 1) |
        _bitNumIntr(left, 26, 0),
    _bitNumIntr(right, 3, 7) |
        _bitNumIntr(left, 3, 6) |
        _bitNumIntr(right, 11, 5) |
        _bitNumIntr(left, 11, 4) |
        _bitNumIntr(right, 19, 3) |
        _bitNumIntr(left, 19, 2) |
        _bitNumIntr(right, 27, 1) |
        _bitNumIntr(left, 27, 0),
  ];
}

int _f(int state, List<int> key) {
  final t1 = _bitNumIntl(state, 31, 0) |
      ((state & 0xf0000000) >> 1) |
      _bitNumIntl(state, 4, 5) |
      _bitNumIntl(state, 3, 6) |
      ((state & 0x0f000000) >> 3) |
      _bitNumIntl(state, 8, 11) |
      _bitNumIntl(state, 7, 12) |
      ((state & 0x00f00000) >> 5) |
      _bitNumIntl(state, 12, 17) |
      _bitNumIntl(state, 11, 18) |
      ((state & 0x000f0000) >> 7) |
      _bitNumIntl(state, 16, 23);

  final t2 = _bitNumIntl(state, 15, 0) |
      ((state & 0x0000f000) << 15) |
      _bitNumIntl(state, 20, 5) |
      _bitNumIntl(state, 19, 6) |
      ((state & 0x00000f00) << 13) |
      _bitNumIntl(state, 24, 11) |
      _bitNumIntl(state, 23, 12) |
      ((state & 0x000000f0) << 11) |
      _bitNumIntl(state, 28, 17) |
      _bitNumIntl(state, 27, 18) |
      ((state & 0x0000000f) << 9) |
      _bitNumIntl(state, 0, 23);

  final lrgState = <int>[
    ((t1 >> 24) & 0xff) ^ key[0],
    ((t1 >> 16) & 0xff) ^ key[1],
    ((t1 >> 8) & 0xff) ^ key[2],
    ((t2 >> 24) & 0xff) ^ key[3],
    ((t2 >> 16) & 0xff) ^ key[4],
    ((t2 >> 8) & 0xff) ^ key[5],
  ];

  final mapped = (_sBoxes[0][_sBoxBit(lrgState[0] >> 2)] << 28) |
      (_sBoxes[1][_sBoxBit(((lrgState[0] & 0x03) << 4) | (lrgState[1] >> 4))]
          << 24) |
      (_sBoxes[2][_sBoxBit(((lrgState[1] & 0x0f) << 2) | (lrgState[2] >> 6))]
          << 20) |
      (_sBoxes[3][_sBoxBit(lrgState[2] & 0x3f)] << 16) |
      (_sBoxes[4][_sBoxBit(lrgState[3] >> 2)] << 12) |
      (_sBoxes[5][_sBoxBit(((lrgState[3] & 0x03) << 4) | (lrgState[4] >> 4))]
          << 8) |
      (_sBoxes[6][_sBoxBit(((lrgState[4] & 0x0f) << 2) | (lrgState[5] >> 6))]
          << 4) |
      _sBoxes[7][_sBoxBit(lrgState[5] & 0x3f)];

  return (_bitNumIntl(mapped, 15, 0) |
          _bitNumIntl(mapped, 6, 1) |
          _bitNumIntl(mapped, 19, 2) |
          _bitNumIntl(mapped, 20, 3) |
          _bitNumIntl(mapped, 28, 4) |
          _bitNumIntl(mapped, 11, 5) |
          _bitNumIntl(mapped, 27, 6) |
          _bitNumIntl(mapped, 16, 7) |
          _bitNumIntl(mapped, 0, 8) |
          _bitNumIntl(mapped, 14, 9) |
          _bitNumIntl(mapped, 22, 10) |
          _bitNumIntl(mapped, 25, 11) |
          _bitNumIntl(mapped, 4, 12) |
          _bitNumIntl(mapped, 17, 13) |
          _bitNumIntl(mapped, 30, 14) |
          _bitNumIntl(mapped, 9, 15) |
          _bitNumIntl(mapped, 1, 16) |
          _bitNumIntl(mapped, 7, 17) |
          _bitNumIntl(mapped, 23, 18) |
          _bitNumIntl(mapped, 13, 19) |
          _bitNumIntl(mapped, 31, 20) |
          _bitNumIntl(mapped, 26, 21) |
          _bitNumIntl(mapped, 2, 22) |
          _bitNumIntl(mapped, 8, 23) |
          _bitNumIntl(mapped, 18, 24) |
          _bitNumIntl(mapped, 12, 25) |
          _bitNumIntl(mapped, 29, 26) |
          _bitNumIntl(mapped, 5, 27) |
          _bitNumIntl(mapped, 21, 28) |
          _bitNumIntl(mapped, 10, 29) |
          _bitNumIntl(mapped, 3, 30) |
          _bitNumIntl(mapped, 24, 31)) &
      0xffffffff;
}

List<int> _crypt(List<int> input, List<List<int>> key) {
  final state = _initialPermutation(input);
  var left = state[0];
  var right = state[1];

  for (var i = 0; i < 15; i++) {
    final previousRight = right;
    right = (_f(right, key[i]) ^ left) & 0xffffffff;
    left = previousRight;
  }

  left = (_f(right, key[15]) ^ left) & 0xffffffff;
  return _inversePermutation(left, right);
}

List<List<int>> _keySchedule(List<int> key, int mode) {
  final schedule = List<List<int>>.generate(
    16,
    (_) => List<int>.filled(6, 0),
    growable: false,
  );

  var c = 0;
  for (var i = 0; i < 28; i++) {
    c |= _bitNum(key, _keyPermC[i], 31 - i);
  }

  var d = 0;
  for (var i = 0; i < 28; i++) {
    d |= _bitNum(key, _keyPermD[i], 31 - i);
  }

  for (var i = 0; i < 16; i++) {
    c = (((c << _keyRoundShift[i]) | (c >> (28 - _keyRoundShift[i]))) &
            0xfffffff0)
        .toUnsigned(32);
    d = (((d << _keyRoundShift[i]) | (d >> (28 - _keyRoundShift[i]))) &
            0xfffffff0)
        .toUnsigned(32);

    final target = mode == _decrypt ? 15 - i : i;

    for (var j = 0; j < 24; j++) {
      schedule[target][j ~/ 8] |=
          _bitNumIntr(c, _keyCompression[j], 7 - (j % 8));
    }

    for (var j = 24; j < 48; j++) {
      schedule[target][j ~/ 8] |=
          _bitNumIntr(d, _keyCompression[j] - 27, 7 - (j % 8));
    }
  }

  return schedule;
}

List<List<List<int>>> _tripleDesKeySetup(List<int> key, int mode) {
  if (mode == _encrypt) {
    return <List<List<int>>>[
      _keySchedule(key.sublist(0), _encrypt),
      _keySchedule(key.sublist(8), _decrypt),
      _keySchedule(key.sublist(16), _encrypt),
    ];
  }

  return <List<List<int>>>[
    _keySchedule(key.sublist(16), _decrypt),
    _keySchedule(key.sublist(8), _encrypt),
    _keySchedule(key.sublist(0), _decrypt),
  ];
}

List<int> _tripleDesCrypt(List<int> input, List<List<List<int>>> key) {
  var data = List<int>.from(input, growable: false);
  for (var i = 0; i < 3; i++) {
    data = _crypt(data, key[i]);
  }
  return data;
}
