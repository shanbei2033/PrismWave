enum AudioOutputMode {
  compatibility,
  wasapiShared,
  wasapiExclusive;

  String get id => switch (this) {
    AudioOutputMode.compatibility => 'compatibility',
    AudioOutputMode.wasapiShared => 'wasapi_shared',
    AudioOutputMode.wasapiExclusive => 'wasapi_exclusive',
  };

  String get label => switch (this) {
    AudioOutputMode.compatibility => 'Compatibility (MPV)',
    AudioOutputMode.wasapiShared => 'WASAPI Shared',
    AudioOutputMode.wasapiExclusive => 'WASAPI Exclusive',
  };

  static AudioOutputMode fromId(String? id) {
    return switch (id) {
      'compatibility' => AudioOutputMode.compatibility,
      'wasapi_shared' => AudioOutputMode.wasapiShared,
      'wasapi_exclusive' => AudioOutputMode.wasapiExclusive,
      _ => AudioOutputMode.wasapiExclusive,
    };
  }
}

const String kPrefAudioOutputMode = 'audio.outputMode';
