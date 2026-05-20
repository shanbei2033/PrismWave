unit arMain;

interface

uses
  Winapi.Windows, Winapi.Messages, System.SysUtils, System.Variants, System.Classes, Vcl.Graphics,
  Vcl.Controls, Vcl.Forms, Vcl.Dialogs, BASS, BASSasio, Vcl.StdCtrls,
  Vcl.ExtCtrls, Vcl.ComCtrls, Vcl.Samples.Gauges;

type
  TfrmMain = class(TForm)
    Timer1: TTimer;
    SD: TSaveDialog;
    stPos: TStaticText;
    bSave: TButton;
    bPlay: TButton;
    bRecord: TButton;
    tbMicVolumeLevel: TTrackBar;
    Label1: TLabel;
    gaMicPPM: TGauge;
    cbDevices: TComboBox;
    procedure FormCreate(Sender: TObject);
    procedure FormDestroy(Sender: TObject);
    procedure tbMicVolumeLevelChange(Sender: TObject);
    procedure cbDevicesChange(Sender: TObject);
    procedure Timer1Timer(Sender: TObject);
    procedure bRecordClick(Sender: TObject);
    procedure bPlayClick(Sender: TObject);
    procedure bSaveClick(Sender: TObject);
  private
    { Private declarations }
    WaveStream: TMemoryStream;
    procedure InitBASS;
    procedure InitASIO;
    procedure ListASIOInputs;
    procedure StartRecording;
    procedure StopRecording;
    procedure WriteToDisk;
  public
    { Public declarations }
    procedure FatalError(Msg : string);
    procedure Error(Msg: string);
    procedure BASSClose;
  end;

type
  WAVHDR = packed record
    riff :			array[0..3] of AnsiChar;
    len  :			DWord;
    cWavFmt:		array[0..7] of AnsiChar;
    dwHdrLen:		DWord;
    wFormat:		Word;
    wNumChannels:	Word;
    dwSampleRate:	DWord;
    dwBytesPerSec:	DWord;
    wBlockAlign:	Word;
    wBitsPerSample:	Word;
    cData  :			array[0..3] of AnsiChar;
    dwDataLen:		DWord;
  end;

var
  frmMain: TfrmMain;

  inputdev : integer;
  recording : boolean;
  level : real;
  // recording
  WaveHdr : WAVHDR;  // WAV header
  chan : HSTREAM;
  reclen : DWORD;

implementation

{$R *.dfm}

function AsioProc(isinput: LongBool; channel: DWORD; buffer: Pointer; length: DWORD; user: Pointer): DWORD; stdcall;
var
  c : integer;
begin
  if isinput then begin  // recording
    // Copy new buffer contents to the memory buffer
	  frmMain.WaveStream.Write(buffer^, length);
    reclen := reclen + length;
    result := 0;
  end
  else begin             // playing
		c := BASS_ChannelGetData(chan, buffer, length); // get data from the decoder
		if c = -1 then c := 0;
		result := c;
  end;
end;

{ TForm1 }

procedure TfrmMain.BASSClose;
begin
  BASS_ASIO_Free();
	BASS_Free();
end;

procedure TfrmMain.bPlayClick(Sender: TObject);
begin
	BASS_ChannelSetPosition(chan, 0, BASS_POS_BYTE); // rewind the playback stream
	if not BASS_ASIO_IsStarted() then begin // need to start the ASIO output...
		BASS_ASIO_ChannelReset(TRUE, -1, BASS_ASIO_RESET_ENABLE); // disable all inputs
		BASS_ASIO_ChannelEnable(FALSE, 0, AsioProc, 0); // enable the 1st output
		BASS_ASIO_ChannelJoin(FALSE, 1, 0); // join the next output for stereo
		BASS_ASIO_ChannelSetFormat(FALSE, 0, BASS_ASIO_FORMAT_16BIT); // playing 16-bit data
		// start the device
		if not BASS_ASIO_Start(0, 0) then
			Error('Can''t start playing');
  end;
end;

procedure TfrmMain.bRecordClick(Sender: TObject);
begin
  if not recording then
    StartRecording
  else
    StopRecording;
end;

procedure TfrmMain.bSaveClick(Sender: TObject);
begin
  WriteToDisk;
end;

procedure TfrmMain.cbDevicesChange(Sender: TObject);
var
  i : integer;
  l : real;
begin
  i := cbDevices.ItemIndex; // get the selected device
  inputdev := integer(cbDevices.Items.Objects[i]) * 2; // get the device #
  l := BASS_ASIO_ChannelGetVolume(TRUE, inputdev);
  tbMicVolumeLevel.Position := Round(l * 100);
  if recording then begin // need to change the enabled inputs on the device...
		BASS_ASIO_Stop(); // stop ASIO processing
		BASS_ASIO_ChannelReset(TRUE, -1, BASS_ASIO_RESET_ENABLE); // disable all inputs, then...
		BASS_ASIO_ChannelEnable(TRUE, inputdev, AsioProc, 0); // enable new inputs
		BASS_ASIO_ChannelSetFormat(TRUE, inputdev, BASS_ASIO_FORMAT_16BIT); // want 16-bit data
		BASS_ASIO_Start(0, 0); // resume ASIO processing
  end;
end;

procedure TfrmMain.Error(Msg: string);
var
	s : string;
begin
	s := Msg + #13#10 + '(Error code: ' + IntToStr(BASS_ASIO_ErrorGetCode) + '/' + IntToStr(BASS_ErrorGetCode) + ')';
	MessageBox(Handle, PChar(s), nil, 0);
end;

procedure TfrmMain.FatalError(Msg: string);
begin
  ShowMessage('Fatal Error: '+ Msg);
  BASSClose;
  Application.Terminate;
end;

procedure TfrmMain.FormCreate(Sender: TObject);
begin
  recording := false;
  InitASIO;
  ListASIOInputs;
  InitBASS;
	WaveStream := TMemoryStream.Create;
end;

procedure TfrmMain.FormDestroy(Sender: TObject);
begin
	WaveStream.Free;
  BASSClose;
end;

procedure TfrmMain.InitASIO;
begin
  if not BASS_ASIO_Init(-1, 0) then
    FatalError('Can''t find an available ASIO device');
end;

procedure TfrmMain.InitBASS;
begin
  // check the correct BASS was loaded
  if (HIWORD(BASS_GetVersion) <> BASSVERSION) then
    FatalError('An incorrect version of BASS.DLL was loaded');

	// initialize output device
  BASS_Init(0, 48000, 0, 0, 0);
end;

// get list of inputs (assuming channels are all ordered in left/right pairs)
procedure TfrmMain.ListASIOInputs;
var
  c : integer;
  i, i2 : BASS_ASIO_CHANNELINFO;
  name : string;
begin
  cbDevices.Clear;
	c := 0;
  while BASS_ASIO_ChannelGetInfo(TRUE, c, i) do begin
	  if not BASS_ASIO_ChannelGetInfo(TRUE, c + 1, &i2) then
      exit; // no "right" channel
    BASS_ASIO_ChannelGetInfo(TRUE, c+1, i2);
    name := String(i.name) + ' + ' + String(i2.name);
    cbDevices.Items.AddObject(name,TObject(c));
		BASS_ASIO_ChannelJoin(TRUE, c + 1, c); // join the pair of channels
    Inc(c,2);
  end;
  if cbDevices.Items.Count > 0 then begin
    cbDevices.ItemIndex := 0;
    cbdevicesChange(nil);
  end;
end;

procedure TfrmMain.StartRecording;
begin
	if WaveStream.Size > 0 then begin
		BASS_ASIO_Stop(); // stop ASIO device in case it was playing
		BASS_ASIO_ChannelReset(FALSE, -1, BASS_ASIO_RESET_ENABLE); // disable outputs in preparation for recording
		BASS_StreamFree(chan);
		chan := 0;
		WaveStream.Clear;
  end;
	with WaveHdr do begin
		riff := 'RIFF';
		len := 36;
		cWavFmt := 'WAVEfmt ';
		dwHdrLen := 16;
		wFormat := 1;
		wNumChannels := 2;
		wBitsPerSample := 16;
		dwSampleRate := Round(BASS_ASIO_GetRate); // using device's current/default sample rate
    if dwSampleRate = 0 then begin
      // the device has no defined rate? try/assume 44100...
      dwSampleRate := 44100;
      BASS_ASIO_SetRate(44100);
    end;
		wBlockAlign := wNumChannels * wBitsPerSample div 8;;
		dwBytesPerSec := dwSampleRate * wBlockAlign;
		cData := 'data';
		dwDataLen := 0;
  end;
	WaveStream.Write(WaveHdr, SizeOf(WAVHDR));
  reclen := 44;

	// enable the selected input and set it to 16-bit
	BASS_ASIO_ChannelReset(TRUE, -1, BASS_ASIO_RESET_ENABLE); // disable all inputs, then...
	BASS_ASIO_ChannelEnable(TRUE, inputdev, AsioProc, 0); // enable the selected
	BASS_ASIO_ChannelSetFormat(TRUE, inputdev, BASS_ASIO_FORMAT_16BIT); // want 16-bit data

  // start the device
  if not BASS_ASIO_Start(0, 0) then begin
    Error('Can''t start recording');
    exit;
  end;
  recording := true;
  bRecord.Caption := 'Stop';
  bPlay.Enabled := false;
  bSave.Enabled := false;
end;

procedure TfrmMain.StopRecording;
var
  i : integer;
begin
  BASS_ASIO_Stop; // stop ASIO device
  recording := false;
  bRecord.Caption := 'Record';
	// complete the WAVE header
	WaveStream.Position := 4;
	i := WaveStream.Size - 8;
	WaveStream.Write(i, 4);
	i := i - $24;
	WaveStream.Position := 40;
	WaveStream.Write(i, 4);
	WaveStream.Position := 0;
  // create a BASS stream from the recording
  chan := BASS_StreamCreateFile(BASS_FILE_MEM, WaveStream.Memory, 0, WaveStream.Size, BASS_STREAM_DECODE);
  if chan = 0 then begin
    Error('Can''t create stream');
    if BASS_ErrorGetCode > 0 then
      exit;
	end;
	// enable "play" and "save" button
  bPlay.Enabled := true;
  bSave.Enabled := true;
end;

procedure TfrmMain.tbMicVolumeLevelChange(Sender: TObject);
var
  l : real;
begin
  l := tbMicVolumeLevel.Position / 100;
  BASS_ASIO_ChannelSetVolume(TRUE, inputdev, l); // set left input level
	BASS_ASIO_ChannelSetVolume(TRUE, inputdev + 1, l); // set right input level
end;

procedure TfrmMain.Timer1Timer(Sender: TObject);
var
  l : single;
  cpos, clen : QWORD;
  dbl : real;
begin
  if level > 0.05 then level := level - 0.05 else level := 0;
  if recording then begin
    l := BASS_ASIO_ChannelGetLevel(TRUE, inputdev);
		if l > level then level := l;
    l := BASS_ASIO_ChannelGetLevel(TRUE, inputdev + 1);
		if l > level then level := l;
    stPos.Caption := IntToStr(reclen);
  end
  else begin
    if chan > 0 then begin
      if BASS_ASIO_IsStarted then begin
        l := BASS_ASIO_ChannelGetLevel(FALSE, 0);
    		if l > level then level := l;
        l := BASS_ASIO_ChannelGetLevel(FALSE, 1);
  		  if l > level then level := l;
      end;
      if BASS_ChannelIsActive(chan) = BASS_ACTIVE_PLAYING then begin  // playing
        cpos := BASS_ChannelGetPosition(chan, BASS_POS_BYTE);
        clen := BASS_ChannelGetLength(chan, BASS_POS_BYTE);
        stPos.Caption := IntToStr(cpos) + ' / ' + IntToStr(clen);
      end
      else begin
        clen := BASS_ChannelGetLength(chan, BASS_POS_BYTE);
        stPos.Caption := IntToStr(clen);
      end;
    end;
  end;
  gaMicPPM.Progress := Round(level * 100);
end;

procedure TfrmMain.WriteToDisk;
begin
  if not SD.Execute then exit;
  WaveStream.SaveToFile(SD.FileName);
end;

end.
