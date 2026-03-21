enum TopBarIdleMode {
  empty,
  custom,
  quote;

  String get id => switch (this) {
    TopBarIdleMode.empty => 'empty',
    TopBarIdleMode.custom => 'custom',
    TopBarIdleMode.quote => 'quote',
  };

  static TopBarIdleMode fromId(String? id) {
    return switch (id) {
      'custom' => TopBarIdleMode.custom,
      'quote' => TopBarIdleMode.quote,
      _ => TopBarIdleMode.empty,
    };
  }
}
