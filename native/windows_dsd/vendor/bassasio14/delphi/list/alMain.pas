unit alMain;

interface

uses
  Winapi.Windows, Winapi.Messages, System.SysUtils, System.Variants, System.Classes, Vcl.Graphics,
  Vcl.Controls, Vcl.Forms, Vcl.Dialogs, BASS, BASSASIO, Vcl.StdCtrls;

type
  TfrmMain = class(TForm)
    mList: TMemo;
    procedure FormCreate(Sender: TObject);
  private
    { Private declarations }
    di : BASS_ASIO_DEVICEINFO;
    procedure ListDevices;
  public
    { Public declarations }
  end;

var
  frmMain: TfrmMain;

implementation

{$R *.dfm}

{ TForm1 }

procedure TfrmMain.FormCreate(Sender: TObject);
begin
  ListDevices;
end;

procedure TfrmMain.ListDevices;
var
  a,b : integer;
  i : BASS_ASIO_CHANNELINFO;
  s : string;
begin
  a := 0;
  while BASS_ASIO_GetDeviceInfo(a, di) do begin
    s := 'dev ' + IntToStr(a);
    s := s + ': ' + string(di.name);
    s := s + #13#10 + '  driver: ' + string(di.driver);
    mList.Lines.Add(s);
    if not BASS_ASIO_Init(a, BASS_ASIO_THREAD) then begin
      mList.Lines.Add(#9 + 'unavailable');
    end
    else begin
      mList.Lines.Add(#9 + 'rate: ' + IntToStr(Trunc(BASS_ASIO_GetRate)));
      b := 0;
      while BASS_ASIO_ChannelGetInfo(TRUE, b, i) do begin
        s := 'in: ' + string(i.name);
        s := s + ' (group ' + IntToStr(i.group) + ' format ' + IntToStr(i.format) + ')';
        mList.Lines.Add(#9 + s);
        Inc(b);
      end;
      b := 0;
      while BASS_ASIO_ChannelGetInfo(FALSE, b, i) do begin
        s := 'out: ' + string(i.name);
        s := s + ' (group ' + IntToStr(i.group) + ' format ' + IntToStr(i.format) + ')';
        mList.Lines.Add(#9 + s);
        Inc(b);
      end;
      if (i.format < BASS_ASIO_FORMAT_DSD_LSB) and BASS_ASIO_SetDSD(TRUE) then begin
        mList.Lines.Add(#9 + 'DSD:');
        b := 0;
        while BASS_ASIO_ChannelGetInfo(TRUE, b, i) do begin
          s := 'in: ' + string(i.name);
          s := s + ' (group ' + IntToStr(i.group) + ' format ' + IntToStr(i.format) + ')';
          mList.Lines.Add(#9 + s);
          Inc(b);
        end;
        b := 0;
        while BASS_ASIO_ChannelGetInfo(FALSE, b, i) do begin
          s := 'out: ' + string(i.name);
          s := s + ' (group ' + IntToStr(i.group) + ' format ' + IntToStr(i.format) + ')';
          mList.Lines.Add(#9 + s);
          Inc(b);
        end;
      end;
    end;
    BASS_ASIO_Free;
    Inc(a);
  end;
end;

end.
