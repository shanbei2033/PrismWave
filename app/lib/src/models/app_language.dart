enum AppLanguage {
  zhCn,
  enUs;

  String get id => switch (this) {
    AppLanguage.zhCn => 'zh_cn',
    AppLanguage.enUs => 'en_us',
  };

  static AppLanguage fromId(String? id) {
    return switch (id) {
      'zh_cn' => AppLanguage.zhCn,
      'en_us' => AppLanguage.enUs,
      _ => AppLanguage.zhCn,
    };
  }
}

const String kPrefAppLanguage = 'ui.language';
