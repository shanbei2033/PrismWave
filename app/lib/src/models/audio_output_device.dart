class AudioOutputDevice {
  const AudioOutputDevice({
    required this.id,
    required this.label,
  });

  final String id;
  final String label;

  static const auto = AudioOutputDevice(
    id: 'auto',
    label: 'Default Device',
  );

  @override
  bool operator ==(Object other) {
    if (other is! AudioOutputDevice) return false;
    return other.id == id;
  }

  @override
  int get hashCode => id.hashCode;
}
