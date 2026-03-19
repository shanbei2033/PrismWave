import 'package:flutter_test/flutter_test.dart';
import 'package:prismwave_demo/src/models/playback_mode.dart';

void main() {
  test('playback mode labels remain stable', () {
    expect(PlaybackMode.loop.label, 'Loop');
    expect(PlaybackMode.single.label, 'Single');
    expect(PlaybackMode.shuffle.label, 'Shuffle');
  });
}
