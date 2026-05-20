class WindowsDsdDevice {
  const WindowsDsdDevice({
    required this.id,
    required this.name,
    required this.driver,
    required this.inputChannels,
    required this.outputChannels,
    required this.supportsNativeDsd,
  });

  final int id;
  final String name;
  final String driver;
  final int inputChannels;
  final int outputChannels;
  final bool supportsNativeDsd;
}
