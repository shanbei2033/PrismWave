unit Unit1;

{Live FX example}

interface

uses
  Windows, Messages, SysUtils, Variants, Classes, Graphics, Controls, Forms,
  Dialogs, Bass, BassAsio, StdCtrls, ComCtrls, ExtCtrls;

type
  TForm1 = class(TForm)
    ComboBox1: TComboBox;
    TrackBar1: TTrackBar;
    CheckBox1: TCheckBox;
    CheckBox2: TCheckBox;
    CheckBox3: TCheckBox;
    Label1: TLabel;
    Label2: TLabel;
    CheckBox4: TCheckBox;
    Label3: TLabel;
    procedure FormCreate(Sender: TObject);
    procedure FormClose(Sender: TObject; var Action: TCloseAction);
    procedure ComboBox1Change(Sender: TObject);
    procedure TrackBar1Change(Sender: TObject);
    procedure CheckBox3Click(Sender: TObject);
    procedure CheckBox1Click(Sender: TObject);
    procedure CheckBox2Click(Sender: TObject);
    procedure CheckBox4Click(Sender: TObject);
  private
    { Private declarations }
  public
    { Public declarations }
  end;

var
  Form1: TForm1;
  fxchan : HSTREAM;
  fx : array [1..4] of HFX;
  input : integer=0;
  buf: array [0..100000] of Single;

implementation

{$R *.dfm}

procedure TForm1.CheckBox1Click(Sender: TObject);
begin
  if CheckBox1.Checked = true then
    begin
      fx[1]:= BASS_ChannelSetFX(fxchan,BASS_FX_DX8_GARGLE,0);
    end
  else
    begin
      BASS_ChannelRemoveFX(fxchan,fx[1]);
      fx[1] := 0;
    end;
end;

procedure TForm1.CheckBox2Click(Sender: TObject);
begin
  if CheckBox2.Checked = true then
    begin
      fx[2]:= BASS_ChannelSetFX(fxchan,BASS_FX_DX8_FLANGER,0);
    end
  else
    begin
      BASS_ChannelRemoveFX(fxchan,fx[2]);
      fx[2] := 0;
    end;
end;

procedure TForm1.CheckBox3Click(Sender: TObject);
begin
  if CheckBox3.Checked = true then
    begin
      fx[3]:= BASS_ChannelSetFX(fxchan,BASS_FX_DX8_REVERB,0);
    end
  else
    begin
      BASS_ChannelRemoveFX(fxchan,fx[3]);
      fx[3] := 0;
    end;
end;

procedure TForm1.CheckBox4Click(Sender: TObject);
begin
  if CheckBox4.Checked = true then
    begin
      fx[4]:= BASS_ChannelSetFX(fxchan,BASS_FX_DX8_CHORUS,0);
    end
  else
    begin
      BASS_ChannelRemoveFX(fxchan,fx[4]);
      fx[4] := 0;
    end;
end;

procedure TForm1.ComboBox1Change(Sender: TObject);
var
  i : integer;
begin
  BASS_ASIO_Stop(); // stop ASIO processing
  BASS_ASIO_ChannelEnable(TRUE,input,nil,0); // disable old inputs
  input := ComboBox1.ItemIndex*2; // get the selection
  BASS_ASIO_ChannelEnableBASS(TRUE, input, fxchan, TRUE); // enable new inputs
  BASS_ASIO_Start(0,0) // resume ASIO processing
end;

procedure TForm1.FormClose(Sender: TObject; var Action: TCloseAction);
begin
  BASS_Asio_Free;
  BASS_Free;
end;

procedure TForm1.FormCreate(Sender: TObject);
var
  c : integer;
  i, i2 : BASS_ASIO_CHANNELINFO;
  name : string;
  s : string;
begin
    // initialize first available ASIO device
    if not (BASS_ASIO_Init(-1,0)) then
    begin
  		BASS_Free();
	  	ShowMessage('Can''t initialize ASIO');
    end;

  // get list of inputs (assuming channels are all ordered in left/right pairs)
  c := 0;
  while BASS_ASIO_ChannelGetInfo(TRUE,c,i) do
    begin
      if not BASS_ASIO_ChannelGetInfo(TRUE,c+1,i2) then // no "right" channel
        Break;
      name := i.name+' - '+i2.name;
      ComboBox1.Items.Add(name);
      c := c+2;
    end;
  ComboBox1.ItemIndex := -1;

	// initialize BASS "no sound" device
	BASS_Init(0, 48000, 0, 0, nil);
	// create a "push" stream to receive and process the input data
	fxchan := BASS_StreamCreate(Trunc(BASS_ASIO_GetRate),2,BASS_SAMPLE_FLOAT or BASS_STREAM_DECODE,STREAMPROC_PUSH,0);

	// enable ASIO channels (using same stream for input and output)
	BASS_ASIO_ChannelEnableBASS(TRUE, 0, chan, TRUE);
	BASS_ASIO_ChannelEnableBASS(FALSE, 0, chan, TRUE);
	// start with output volume at 0 (in case of nasty feedback)
	BASS_ASIO_ChannelSetVolume(FALSE,0,0);
	BASS_ASIO_ChannelSetVolume(FALSE,1,0);
	// start the device using default buffer/latency
	if not (BASS_ASIO_Start(0,0)) then
    begin
		  BASS_ASIO_Free();
	  	BASS_Free();
  		ShowMessage('Can''t initialize recording device');
    end;

  Label1.Caption := 'Latency : '+FloatToStr((BASS_ASIO_GetLatency(FALSE)+BASS_ASIO_GetLatency(TRUE))*1000/BASS_ASIO_GetRate());
end;

procedure TForm1.TrackBar1Change(Sender: TObject);
var
  level : single;
begin
  level := TrackBar1.Position/100; // get level
 	BASS_ASIO_ChannelSetVolume(FALSE,0,level); // set left output level
 	BASS_ASIO_ChannelSetVolume(FALSE,1,level); // set right output level
end;

end.
