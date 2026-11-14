import 'dart:convert';
import 'dart:typed_data';

import 'package:crypto/crypto.dart' as hash;
import 'package:cryptography/cryptography.dart';

/// The shop's rows could not be read with this key: it is the wrong key, or the row was altered or moved.
class RowDecryptException implements Exception {
  RowDecryptException(this.reason);
  final String reason;
  @override
  String toString() => 'RowDecryptException($reason)';
}

/// The shop's end-to-end encryption key. The shop PC makes it and never sends it anywhere; the owner types the printed recovery
/// code once and this phone keeps the key. The cloud holds only ciphertext, so without this key the app shows nothing.
///
/// The formats match the shop PC exactly (`Sync/DataKey.cs`): the recovery code is the 32-byte key plus a 3-byte SHA-256 checksum,
/// written in base32; a row is `e1.` + base64(nonce 12 | ciphertext | tag 16), AES-256-GCM, with `table\nid` as authenticated data.
class ShopKey {
  ShopKey._(this._bytes);

  static const size = 32;
  static const _checksumBytes = 3;
  static const _alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  static const _prefix = 'e1.';
  static const _nonceSize = 12;
  static const _tagSize = 16;

  static final _aes = AesGcm.with256bits();

  final List<int> _bytes;

  factory ShopKey.fromBase64(String text) {
    final bytes = base64.decode(text);
    if (bytes.length != size) throw ArgumentError('A shop key is $size bytes.');
    return ShopKey._(bytes);
  }

  String toBase64() => base64.encode(_bytes);

  /// What a person types: any case, with or without dashes and spaces. Null when it is not a valid code (typo, wrong length).
  ///
  /// The alphabet has no 0, 1 or 8, so those can only be a mistyped O, I or B; they are accepted as such. The checksum still has
  /// the last word, so a wrong guess is refused rather than turned into a different key.
  static ShopKey? parseRecoveryCode(String? code) {
    if (code == null) return null;
    final clean = code
        .toUpperCase()
        .replaceAll(RegExp(r'[\s\-]'), '')
        .replaceAll('0', 'O')
        .replaceAll('1', 'I')
        .replaceAll('8', 'B');
    if (clean.length != (size + _checksumBytes) * 8 ~/ 5) return null;

    final data = _fromBase32(clean);
    if (data == null || data.length != size + _checksumBytes) return null;

    final candidate = data.sublist(0, size);
    final expected = hash.sha256.convert(candidate).bytes.sublist(0, _checksumBytes);
    var difference = 0;
    for (var i = 0; i < _checksumBytes; i++) {
      difference |= data[size + i] ^ expected[i];
    }
    return difference == 0 ? ShopKey._(candidate) : null;
  }

  /// Opens one row. Throws [RowDecryptException] when the key is wrong or the row is not what the shop sent for this table and id.
  Future<Map<String, dynamic>> decryptRow(String table, int id, String payload) async {
    if (!payload.startsWith(_prefix)) throw RowDecryptException('unknown format');
    final Uint8List packed;
    try {
      packed = base64.decode(payload.substring(_prefix.length));
    } on FormatException {
      throw RowDecryptException('not base64');
    }
    if (packed.length < _nonceSize + _tagSize) throw RowDecryptException('too short');

    final box = SecretBox(
      packed.sublist(_nonceSize, packed.length - _tagSize),
      nonce: packed.sublist(0, _nonceSize),
      mac: Mac(packed.sublist(packed.length - _tagSize)),
    );
    final List<int> plain;
    try {
      plain = await _aes.decrypt(box, secretKey: SecretKey(_bytes), aad: utf8.encode('$table\n$id'));
    } on SecretBoxAuthenticationError {
      throw RowDecryptException('wrong key or altered row');
    }

    final decoded = jsonDecode(utf8.decode(plain));
    if (decoded is! Map<String, dynamic>) throw RowDecryptException('not a row');
    return decoded;
  }

  /// Seals bytes for the shop PC (or opens its answer): AES-256-GCM like a row, bound to a [purpose] and [context] instead of a table
  /// and id (`Sync/DataKey.cs`, `RowCipher.Seal`). The phone seals each change it sends (purpose `command`, context the kind of change);
  /// the shop PC seals its answer (purpose `result`, context the command's id). The cloud in between sees only ciphertext.
  Future<String> seal(String purpose, String context, List<int> plain) async {
    final box = await _aes.encrypt(plain, secretKey: SecretKey(_bytes), aad: utf8.encode('$purpose\n$context'));
    return _prefix + base64.encode([...box.nonce, ...box.cipherText, ...box.mac.bytes]);
  }

  /// Opens what [seal] made, or what the shop PC sealed. Throws [RowDecryptException] for the wrong key or altered data.
  Future<List<int>> open(String purpose, String context, String payload) async {
    if (!payload.startsWith(_prefix)) throw RowDecryptException('unknown format');
    final Uint8List packed;
    try {
      packed = base64.decode(payload.substring(_prefix.length));
    } on FormatException {
      throw RowDecryptException('not base64');
    }
    if (packed.length < _nonceSize + _tagSize) throw RowDecryptException('too short');
    final box = SecretBox(
      packed.sublist(_nonceSize, packed.length - _tagSize),
      nonce: packed.sublist(0, _nonceSize),
      mac: Mac(packed.sublist(packed.length - _tagSize)),
    );
    try {
      return await _aes.decrypt(box, secretKey: SecretKey(_bytes), aad: utf8.encode('$purpose\n$context'));
    } on SecretBoxAuthenticationError {
      throw RowDecryptException('wrong key or altered data');
    }
  }

  static List<int>? _fromBase32(String text) {
    final result = <int>[];
    var buffer = 0, bits = 0;
    for (final c in text.split('')) {
      final v = _alphabet.indexOf(c);
      if (v < 0) return null;
      buffer = (buffer << 5) | v;
      bits += 5;
      if (bits >= 8) {
        result.add((buffer >> (bits - 8)) & 255);
        bits -= 8;
      }
      buffer &= (1 << bits) - 1;
    }
    return result;
  }
}

