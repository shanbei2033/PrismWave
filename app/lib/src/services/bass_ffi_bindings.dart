// ignore_for_file: library_private_types_in_public_api

import 'dart:ffi';

import 'package:ffi/ffi.dart';

typedef HStream = int;

const int bassPosByte = 0;
const int bassActiveStopped = 0;
const int bassActivePlaying = 1;
const int bassUnicode = 0x80000000;
const int bassSampleLoop = 4;
const int bassSampleFloat = 0x100;
const int bassStreamDecode = 0x200000;
const int bassDsdRaw = 0x200;
const int bassDsdDop = 0x400;
const int bassAsioResetEnable = 0x4;

typedef _BassErrorGetCodeNative = Int32 Function();
typedef _BassErrorGetCodeDart = int Function();

typedef _BassGetVersionNative = Uint32 Function();
typedef _BassGetVersionDart = int Function();

typedef _BassInitNative = Int32 Function(
  Int32 device,
  Uint32 freq,
  Uint32 flags,
  IntPtr win,
  Pointer<Void> dsguid,
);
typedef _BassInitDart = int Function(
  int device,
  int freq,
  int flags,
  int win,
  Pointer<Void> dsguid,
);

typedef _BassFreeNative = Int32 Function();
typedef _BassFreeDart = int Function();

typedef _BassChannelPlayNative = Int32 Function(Uint32 handle, Int32 restart);
typedef _BassChannelPlayDart = int Function(int handle, int restart);

typedef _BassChannelPauseNative = Int32 Function(Uint32 handle);
typedef _BassChannelPauseDart = int Function(int handle);

typedef _BassChannelStopNative = Int32 Function(Uint32 handle);
typedef _BassChannelStopDart = int Function(int handle);

typedef _BassStreamFreeNative = Int32 Function(Uint32 handle);
typedef _BassStreamFreeDart = int Function(int handle);

typedef _BassChannelIsActiveNative = Uint32 Function(Uint32 handle);
typedef _BassChannelIsActiveDart = int Function(int handle);

final class BassChannelInfo extends Struct {
  @Uint32()
  external int freq;

  @Uint32()
  external int chans;

  @Uint32()
  external int flags;

  @Uint32()
  external int ctype;

  @Uint32()
  external int origres;

  external Pointer<Void> plugin;

  external Pointer<Void> sample;

  external Pointer<Utf8> filename;
}

final class BassAsioDeviceInfo extends Struct {
  external Pointer<Utf8> name;

  external Pointer<Utf8> driver;
}

final class BassAsioInfo extends Struct {
  @Array(32)
  external Array<Uint8> nameRaw;

  @Uint32()
  external int version;

  @Uint32()
  external int inputs;

  @Uint32()
  external int outputs;

  @Uint32()
  external int bufMin;

  @Uint32()
  external int bufMax;

  @Uint32()
  external int bufPref;

  @Int32()
  external int bufGran;

  @Uint32()
  external int initFlags;
}

final class BassAsioChannelInfo extends Struct {
  @Uint32()
  external int group;

  @Uint32()
  external int format;

  @Array(32)
  external Array<Uint8> nameRaw;
}

typedef _BassChannelGetInfoNative = Int32 Function(
  Uint32 handle,
  Pointer<BassChannelInfo> info,
);
typedef _BassChannelGetInfoDart = int Function(
  int handle,
  Pointer<BassChannelInfo> info,
);

typedef _BassChannelGetAttributeNative = Int32 Function(
  Uint32 handle,
  Uint32 attrib,
  Pointer<Float> value,
);
typedef _BassChannelGetAttributeDart = int Function(
  int handle,
  int attrib,
  Pointer<Float> value,
);

typedef _BassChannelGetPositionNative = Uint64 Function(
  Uint32 handle,
  Uint32 mode,
);
typedef _BassChannelGetPositionDart = int Function(int handle, int mode);

typedef _BassChannelGetLengthNative = Uint64 Function(Uint32 handle, Uint32 mode);
typedef _BassChannelGetLengthDart = int Function(int handle, int mode);

typedef _BassChannelSetPositionNative = Int32 Function(
  Uint32 handle,
  Uint64 pos,
  Uint32 mode,
);
typedef _BassChannelSetPositionDart = int Function(int handle, int pos, int mode);

typedef _BassChannelBytes2SecondsNative = Double Function(
  Uint32 handle,
  Uint64 pos,
);
typedef _BassChannelBytes2SecondsDart = double Function(int handle, int pos);

typedef _BassChannelSeconds2BytesNative = Uint64 Function(
  Uint32 handle,
  Double pos,
);
typedef _BassChannelSeconds2BytesDart = int Function(int handle, double pos);

typedef _BassDsdStreamCreateFileNative = Uint32 Function(
  Uint32 filetype,
  Pointer<Utf16> file,
  Uint64 offset,
  Uint64 length,
  Uint32 flags,
  Uint32 freq,
);
typedef _BassDsdStreamCreateFileDart = int Function(
  int filetype,
  Pointer<Utf16> file,
  int offset,
  int length,
  int flags,
  int freq,
);

typedef _BassAsioInitNative = Int32 Function(Int32 device, Uint32 flags);
typedef _BassAsioInitDart = int Function(int device, int flags);

typedef _BassAsioErrorGetCodeNative = Uint32 Function();
typedef _BassAsioErrorGetCodeDart = int Function();

typedef _BassAsioGetDeviceInfoNative = Int32 Function(
  Uint32 device,
  Pointer<BassAsioDeviceInfo> info,
);
typedef _BassAsioGetDeviceInfoDart = int Function(
  int device,
  Pointer<BassAsioDeviceInfo> info,
);

typedef _BassAsioSetDeviceNative = Int32 Function(Uint32 device);
typedef _BassAsioSetDeviceDart = int Function(int device);

typedef _BassAsioGetDeviceNative = Uint32 Function();
typedef _BassAsioGetDeviceDart = int Function();

typedef _BassAsioFreeNative = Int32 Function();
typedef _BassAsioFreeDart = int Function();

typedef _BassAsioGetInfoNative = Int32 Function(Pointer<BassAsioInfo> info);
typedef _BassAsioGetInfoDart = int Function(Pointer<BassAsioInfo> info);

typedef _BassAsioSetRateNative = Int32 Function(Double rate);
typedef _BassAsioSetRateDart = int Function(double rate);

typedef _BassAsioGetRateNative = Double Function();
typedef _BassAsioGetRateDart = double Function();

typedef _BassAsioStartNative = Int32 Function(Uint32 bufferLength, Uint32 threads);
typedef _BassAsioStartDart = int Function(int bufferLength, int threads);

typedef _BassAsioStopNative = Int32 Function();
typedef _BassAsioStopDart = int Function();

typedef _BassAsioSetDsdNative = Int32 Function(Int32 dsd);
typedef _BassAsioSetDsdDart = int Function(int dsd);

typedef _BassAsioChannelEnableBassNative = Int32 Function(
  Int32 input,
  Uint32 channel,
  Uint32 handle,
  Int32 join,
);
typedef _BassAsioChannelEnableBassDart = int Function(
  int input,
  int channel,
  int handle,
  int join,
);

typedef _BassAsioChannelEnableMirrorNative = Int32 Function(
  Uint32 channel,
  Int32 input2,
  Uint32 channel2,
);
typedef _BassAsioChannelEnableMirrorDart = int Function(
  int channel,
  int input2,
  int channel2,
);

typedef _BassAsioChannelGetInfoNative = Int32 Function(
  Int32 input,
  Uint32 channel,
  Pointer<BassAsioChannelInfo> info,
);
typedef _BassAsioChannelGetInfoDart = int Function(
  int input,
  int channel,
  Pointer<BassAsioChannelInfo> info,
);

class BassFfiBindings {
  BassFfiBindings._({
    required this.bass,
    required this.bassDsd,
    required this.bassAsio,
  })  : bassErrorGetCode = bass.lookupFunction<
          _BassErrorGetCodeNative,
          _BassErrorGetCodeDart
        >('BASS_ErrorGetCode'),
        bassGetVersion = bass.lookupFunction<
          _BassGetVersionNative,
          _BassGetVersionDart
        >('BASS_GetVersion'),
        bassInit = bass.lookupFunction<_BassInitNative, _BassInitDart>(
          'BASS_Init',
        ),
        bassFree = bass.lookupFunction<_BassFreeNative, _BassFreeDart>(
          'BASS_Free',
        ),
        bassChannelPlay =
            bass.lookupFunction<_BassChannelPlayNative, _BassChannelPlayDart>(
              'BASS_ChannelPlay',
            ),
        bassChannelPause = bass.lookupFunction<
          _BassChannelPauseNative,
          _BassChannelPauseDart
        >('BASS_ChannelPause'),
        bassChannelStop = bass.lookupFunction<
          _BassChannelStopNative,
          _BassChannelStopDart
        >('BASS_ChannelStop'),
        bassStreamFree = bass.lookupFunction<
          _BassStreamFreeNative,
          _BassStreamFreeDart
        >('BASS_StreamFree'),
        bassChannelIsActive = bass.lookupFunction<
          _BassChannelIsActiveNative,
          _BassChannelIsActiveDart
        >('BASS_ChannelIsActive'),
        bassChannelGetInfo = bass.lookupFunction<
          _BassChannelGetInfoNative,
          _BassChannelGetInfoDart
        >('BASS_ChannelGetInfo'),
        bassChannelGetAttribute = bass.lookupFunction<
          _BassChannelGetAttributeNative,
          _BassChannelGetAttributeDart
        >('BASS_ChannelGetAttribute'),
        bassChannelGetPosition = bass.lookupFunction<
          _BassChannelGetPositionNative,
          _BassChannelGetPositionDart
        >('BASS_ChannelGetPosition'),
        bassChannelGetLength = bass.lookupFunction<
          _BassChannelGetLengthNative,
          _BassChannelGetLengthDart
        >('BASS_ChannelGetLength'),
        bassChannelSetPosition = bass.lookupFunction<
          _BassChannelSetPositionNative,
          _BassChannelSetPositionDart
        >('BASS_ChannelSetPosition'),
        bassChannelBytes2Seconds = bass.lookupFunction<
          _BassChannelBytes2SecondsNative,
          _BassChannelBytes2SecondsDart
        >('BASS_ChannelBytes2Seconds'),
        bassChannelSeconds2Bytes = bass.lookupFunction<
          _BassChannelSeconds2BytesNative,
          _BassChannelSeconds2BytesDart
        >('BASS_ChannelSeconds2Bytes'),
        bassDsdStreamCreateFile = bassDsd.lookupFunction<
          _BassDsdStreamCreateFileNative,
          _BassDsdStreamCreateFileDart
        >('BASS_DSD_StreamCreateFile'),
        bassAsioErrorGetCode = bassAsio.lookupFunction<
          _BassAsioErrorGetCodeNative,
          _BassAsioErrorGetCodeDart
        >('BASS_ASIO_ErrorGetCode'),
        bassAsioGetDeviceInfo = bassAsio.lookupFunction<
          _BassAsioGetDeviceInfoNative,
          _BassAsioGetDeviceInfoDart
        >('BASS_ASIO_GetDeviceInfo'),
        bassAsioSetDevice = bassAsio.lookupFunction<
          _BassAsioSetDeviceNative,
          _BassAsioSetDeviceDart
        >('BASS_ASIO_SetDevice'),
        bassAsioGetDevice = bassAsio.lookupFunction<
          _BassAsioGetDeviceNative,
          _BassAsioGetDeviceDart
        >('BASS_ASIO_GetDevice'),
        bassAsioInit = bassAsio.lookupFunction<
          _BassAsioInitNative,
          _BassAsioInitDart
        >('BASS_ASIO_Init'),
        bassAsioFree = bassAsio.lookupFunction<
          _BassAsioFreeNative,
          _BassAsioFreeDart
        >('BASS_ASIO_Free'),
        bassAsioGetInfo = bassAsio.lookupFunction<
          _BassAsioGetInfoNative,
          _BassAsioGetInfoDart
        >('BASS_ASIO_GetInfo'),
        bassAsioSetRate = bassAsio.lookupFunction<
          _BassAsioSetRateNative,
          _BassAsioSetRateDart
        >('BASS_ASIO_SetRate'),
        bassAsioGetRate = bassAsio.lookupFunction<
          _BassAsioGetRateNative,
          _BassAsioGetRateDart
        >('BASS_ASIO_GetRate'),
        bassAsioStart = bassAsio.lookupFunction<
          _BassAsioStartNative,
          _BassAsioStartDart
        >('BASS_ASIO_Start'),
        bassAsioStop = bassAsio.lookupFunction<
          _BassAsioStopNative,
          _BassAsioStopDart
        >('BASS_ASIO_Stop'),
        bassAsioSetDsd = bassAsio.lookupFunction<
          _BassAsioSetDsdNative,
          _BassAsioSetDsdDart
        >('BASS_ASIO_SetDSD'),
        bassAsioChannelEnableBass = bassAsio.lookupFunction<
          _BassAsioChannelEnableBassNative,
          _BassAsioChannelEnableBassDart
        >('BASS_ASIO_ChannelEnableBASS'),
        bassAsioChannelEnableMirror = bassAsio.lookupFunction<
          _BassAsioChannelEnableMirrorNative,
          _BassAsioChannelEnableMirrorDart
        >('BASS_ASIO_ChannelEnableMirror'),
        bassAsioChannelGetInfo = bassAsio.lookupFunction<
          _BassAsioChannelGetInfoNative,
          _BassAsioChannelGetInfoDart
        >('BASS_ASIO_ChannelGetInfo');

  final DynamicLibrary bass;
  final DynamicLibrary bassDsd;
  final DynamicLibrary bassAsio;

  final _BassErrorGetCodeDart bassErrorGetCode;
  final _BassGetVersionDart bassGetVersion;
  final _BassInitDart bassInit;
  final _BassFreeDart bassFree;
  final _BassChannelPlayDart bassChannelPlay;
  final _BassChannelPauseDart bassChannelPause;
  final _BassChannelStopDart bassChannelStop;
  final _BassStreamFreeDart bassStreamFree;
  final _BassChannelIsActiveDart bassChannelIsActive;
  final _BassChannelGetInfoDart bassChannelGetInfo;
  final _BassChannelGetAttributeDart bassChannelGetAttribute;
  final _BassChannelGetPositionDart bassChannelGetPosition;
  final _BassChannelGetLengthDart bassChannelGetLength;
  final _BassChannelSetPositionDart bassChannelSetPosition;
  final _BassChannelBytes2SecondsDart bassChannelBytes2Seconds;
  final _BassChannelSeconds2BytesDart bassChannelSeconds2Bytes;
  final _BassDsdStreamCreateFileDart bassDsdStreamCreateFile;
  final _BassAsioErrorGetCodeDart bassAsioErrorGetCode;
  final _BassAsioGetDeviceInfoDart bassAsioGetDeviceInfo;
  final _BassAsioSetDeviceDart bassAsioSetDevice;
  final _BassAsioGetDeviceDart bassAsioGetDevice;
  final _BassAsioInitDart bassAsioInit;
  final _BassAsioFreeDart bassAsioFree;
  final _BassAsioGetInfoDart bassAsioGetInfo;
  final _BassAsioSetRateDart bassAsioSetRate;
  final _BassAsioGetRateDart bassAsioGetRate;
  final _BassAsioStartDart bassAsioStart;
  final _BassAsioStopDart bassAsioStop;
  final _BassAsioSetDsdDart bassAsioSetDsd;
  final _BassAsioChannelEnableBassDart bassAsioChannelEnableBass;
  final _BassAsioChannelEnableMirrorDart bassAsioChannelEnableMirror;
  final _BassAsioChannelGetInfoDart bassAsioChannelGetInfo;

  static BassFfiBindings open({
    required String bassPath,
    required String bassDsdPath,
    required String bassAsioPath,
  }) {
    return BassFfiBindings._(
      bass: DynamicLibrary.open(bassPath),
      bassDsd: DynamicLibrary.open(bassDsdPath),
      bassAsio: DynamicLibrary.open(bassAsioPath),
    );
  }
}
