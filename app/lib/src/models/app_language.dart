enum AppLanguage {
  zhCn,
  zhTw,
  enUs;

  String get id => switch (this) {
    AppLanguage.zhCn => 'zh_cn',
    AppLanguage.zhTw => 'zh_tw',
    AppLanguage.enUs => 'en_us',
  };

  static AppLanguage fromId(String? id) {
    return switch (id) {
      'zh_cn' => AppLanguage.zhCn,
      'zh_tw' => AppLanguage.zhTw,
      'en_us' => AppLanguage.enUs,
      _ => AppLanguage.zhCn,
    };
  }
}

const String kPrefAppLanguage = 'ui.language';
