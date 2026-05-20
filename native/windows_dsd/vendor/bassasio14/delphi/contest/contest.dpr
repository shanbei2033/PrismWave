{
BASS Asio Console Test
Delphi version by Chris Troesken
}
program Contest;

{$APPTYPE CONSOLE}

uses
  Windows,
  SysUtils,
  MMSystem,
  Bass,
  Bassasio;

var
  starttime: DWORD;

function IntToFixed(val, digits: Integer): string;
var
  s: string;
begin
  s := IntToStr(val);
  while Length(s) < digits do s := '0' + s;
  Result := s;
end;

// display error messages

procedure Error(text: string);
begin
  WriteLn('Error(' + 'BassError ' + IntToStr(BASS_ErrorGetCode) + ' BassAsioError ' +
    IntToStr(BASS_Asio_ErrorGetCode) + '+): ' + text);
  BASS_ASIO_Free();
  BASS_Free;
  Halt(0);
end;

var
  chan: DWORD;
  time, pos: DWORD;
  a : integer;
  b1,b2 : integer;
  ChanInfo: BASS_CHANNELINFO;
  AValue: Single;
  
begin
  WriteLn('Simple console mode BASSAsio example : MOD/MPx/OGG/WAV player');
  Writeln('---------------------------------------------------------');
	// check the correct BASS was loaded
	if (HIWORD(BASS_GetVersion) <> BASSVERSION) then
	begin
		Writeln('An incorrect version of BASS.DLL was loaded');
		Exit;
	end;

  if (ParamCount <> 1) then
  begin
    WriteLn(#9 + 'usage: contest <file>');
    Exit;
  end;

  BASS_Init(0, 48000, 0, 0, nil);
  chan := BASS_StreamCreateFile(0, PChar(ParamStr(1)), 0, 0,
    BASS_SAMPLE_LOOP or BASS_SAMPLE_FLOAT or BASS_STREAM_DECODE);
  if (chan = 0) then
    chan := BASS_StreamCreateURL(PChar(ParamStr(1)), 0, BASS_SAMPLE_LOOP or BASS_STREAM_DECODE or BASS_SAMPLE_FLOAT, nil, 0);
  if (chan <> 0) then
  begin
    pos := BASS_ChannelGetLength(chan, BASS_POS_BYTE);
    if (BASS_StreamGetFilePosition(chan, BASS_FILEPOS_DOWNLOAD) <> DWORD(-1)) then
    begin
      // streaming from the internet
      if (pos <> QW_ERROR) then
        WriteLn('streaming internet file [' + IntToStr(pos) + ' bytes]')
      else
        WriteLn('streaming internet file');
    end
    else
      WriteLn('streaming file [' + IntToStr(pos) + ' bytes]');

  end
  else
  begin
    // load the MOD (with looping and sensitive ramping)
    chan := BASS_MusicLoad(FALSE, PChar(ParamStr(1)), 0, 0, BASS_SAMPLE_LOOP or BASS_MUSIC_RAMPS or BASS_MUSIC_CALCLEN or BASS_STREAM_DECODE or BASS_SAMPLE_FLOAT, 0);
    if (chan = 0) then
      // not a MOD either
      Error('Can''t play the file');
    // count channels
    a := 0;
    while BASS_ChannelGetAttribute(chan, BASS_ATTRIB_MUSIC_VOL_CHAN + a, AValue) do
      a := a + 1;
    Write('playing MOD music "' + BASS_ChannelGetTags(chan, BASS_TAG_MUSIC_NAME) + '" [' + IntToStr(a) + ' chans, ' + IntToStr(BASS_ChannelGetLength(chan, BASS_POS_MUSIC_ORDER)) + ' orders]');
    pos := BASS_ChannelGetLength(chan, BASS_POS_BYTE);
  end;
  
    
    if not BASS_ASIO_Init(-1,BASS_ASIO_THREAD) then // initialize ASIO device
      Error('Can''t initialize ASIO');
    if not BASS_ASIO_ChannelEnableBASS(false, 0, chan, true) // enable ASIO channel(s)
      Error('Can''t enable ASIO channel(s)');
    BASS_ChannelGetInfo(chan, ChanInfo);
    BASS_ASIO_SetRate(ChanInfo.freq); // try to set the device rate to avoid resampling
    if not BASS_ASIO_Start(0,0) then // start the device using default buffer/latency
      Error('Can''t start ASIO output');
  starttime := timeGetTime;
  while (*not KeyPressed and*) BASS_ChannelIsActive(chan) > 0 do
  begin
    // display some stuff and wait a bit
    pos := BASS_ChannelGetPosition(chan, BASS_POS_BYTE);
    time := BASS_ChannelBytes2Seconds(chan,pos);
    Write('pos ' + IntToFixed(pos, 9));
    Write(' - time ' + IntToStr(time div 60000) + ':' + IntToFixed((time div 1000) mod 60, 2));
    Write(' - CPU-Usage ' + FloatToStrF(BASS_Asio_GetCPU(), ffFixed, 0, 1) + '%  ' + #13);
    Sleep(50);

  end;
  Writeln('                                                                   ');
  
  Bass_Asio_Free();
  BASS_Free();
end.

