import 'package:flutter_test/flutter_test.dart';
import 'package:prismwave_demo/src/domain/playback_strategy.dart';
import 'package:prismwave_demo/src/models/playback_mode.dart';

void main() {
  group('PlaybackStrategy.next', () {
    test('loop mode wraps at boundary', () {
      final next = PlaybackStrategy.resolveNextIndex(
        playlistLength: 3,
        currentIndex: 2,
        mode: PlaybackMode.loop,
        fromAutoEnded: false,
        randomInt: (_) => 0,
      );
      expect(next, 0);
    });

    test('single mode auto-ended repeats current track', () {
      final next = PlaybackStrategy.resolveNextIndex(
        playlistLength: 4,
        currentIndex: 1,
        mode: PlaybackMode.single,
        fromAutoEnded: true,
        randomInt: (_) => 3,
      );
      expect(next, 1);
    });

    test('single mode manual next behaves like loop', () {
      final next = PlaybackStrategy.resolveNextIndex(
        playlistLength: 4,
        currentIndex: 1,
        mode: PlaybackMode.single,
        fromAutoEnded: false,
        randomInt: (_) => 3,
      );
      expect(next, 2);
    });

    test('shuffle mode avoids current index', () {
      final scripted = _ScriptedRandom([2, 2, 1]);
      final next = PlaybackStrategy.resolveNextIndex(
        playlistLength: 3,
        currentIndex: 2,
        mode: PlaybackMode.shuffle,
        fromAutoEnded: false,
        randomInt: scripted.nextInt,
      );
      expect(next, 1);
    });
  });

  group('PlaybackStrategy.previous', () {
    test('loop mode wraps backwards at boundary', () {
      final previous = PlaybackStrategy.resolvePreviousIndex(
        playlistLength: 3,
        currentIndex: 0,
        mode: PlaybackMode.loop,
        randomInt: (_) => 0,
      );
      expect(previous, 2);
    });

    test('single mode manual previous behaves like loop', () {
      final previous = PlaybackStrategy.resolvePreviousIndex(
        playlistLength: 4,
        currentIndex: 0,
        mode: PlaybackMode.single,
        randomInt: (_) => 1,
      );
      expect(previous, 3);
    });

    test('shuffle mode avoids current index', () {
      final scripted = _ScriptedRandom([0, 0, 2]);
      final previous = PlaybackStrategy.resolvePreviousIndex(
        playlistLength: 3,
        currentIndex: 0,
        mode: PlaybackMode.shuffle,
        randomInt: scripted.nextInt,
      );
      expect(previous, 2);
    });
  });
}

class _ScriptedRandom {
  _ScriptedRandom(this._values);

  final List<int> _values;
  int _index = 0;

  int nextInt(int upperBoundExclusive) {
    final value = _values[_index % _values.length];
    _index++;
    return value % upperBoundExclusive;
  }
}
