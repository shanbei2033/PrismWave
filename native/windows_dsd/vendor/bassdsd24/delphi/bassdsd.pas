{
  BASSDSD 2.4 Delphi/Pascal unit
  Copyright (c) 2014-2025 Un4seen Developments Ltd.

  See the BASSDSD.CHM file for more detailed documentation
}

unit BassDSD;

interface

{$IFDEF MSWINDOWS}
uses BASS, Windows;
{$ELSE}
uses BASS;
{$ENDIF}

const
  // Additional BASS_SetConfig options
  BASS_CONFIG_DSD_FREQ         = $10800;
  BASS_CONFIG_DSD_GAIN         = $10801;

  // Additional BASS_DSD_StreamCreateFile/etc flags
  BASS_DSD_RAW                 = $200;
  BASS_DSD_DOP                 = $400;
  BASS_DSD_DOP_AA              = $800;

  // BASS_CHANNELINFO type
  BASS_CTYPE_STREAM_DSD        = $11700;

  // Additional tag types
  BASS_TAG_DSD_ARTIST          = $13000; // DSDIFF artist : ASCII string
  BASS_TAG_DSD_TITLE           = $13001; // DSDIFF title : ASCII string
  BASS_TAG_DSD_COMMENT         = $13100; // + index, DSDIFF comment : TAG_DSD_COMMENT structure

  // Additional attributes
  BASS_ATTRIB_DSD_GAIN         = $14000;
  BASS_ATTRIB_DSD_RATE         = $14001;

type
  PTAG_DSD_COMMENT = ^TAG_DSD_COMMENT;
  TAG_DSD_COMMENT = packed record
	timeStampYear: Word; // creation year
	timeStampMonth: Byte; // creation month
	timeStampDay: Byte; // creation day
	timeStampHour: Byte; // creation hour
	timeStampMinutes: Byte; // creation minutes
	cmtType: Word; // comment type
	cmtRef: Word; // comment reference
	count: DWORD; // string length
	commentText: Array[0..maxInt div 2 - 1] of AnsiChar; // text
  end;

const
{$IFDEF MSWINDOWS}
  bassdsddll = 'bassdsd.dll';
{$ENDIF}
{$IFDEF LINUX}
  bassdsddll = 'libbassdsd.so';
{$ENDIF}
{$IFDEF ANDROID}
  bassdsddll = 'libbassdsd.so';
{$ENDIF}
{$IFDEF MACOS}
  {$IFDEF IOS}
    bassdsddll = 'bassdsd.framework/bassdsd';
  {$ELSE}
    bassdsddll = 'libbassdsd.dylib';
  {$ENDIF}
{$ENDIF}

function BASS_DSD_StreamCreateFile(filetype:DWORD; fl:pointer; offset,length:QWORD; flags,freq:DWORD): HSTREAM; {$IFDEF MSWINDOWS}stdcall{$ELSE}cdecl{$ENDIF}; external bassdsddll;
function BASS_DSD_StreamCreateURL(url:PChar; offset:DWORD; flags:DWORD; proc:DOWNLOADPROC; user:Pointer; freq:DWORD): HSTREAM; {$IFDEF MSWINDOWS}stdcall{$ELSE}cdecl{$ENDIF}; external bassdsddll;
function BASS_DSD_StreamCreateFileUser(system,flags:DWORD; var procs:BASS_FILEPROCS; user:Pointer; freq:DWORD): HSTREAM; {$IFDEF MSWINDOWS}stdcall{$ELSE}cdecl{$ENDIF}; external bassdsddll;

implementation

end.