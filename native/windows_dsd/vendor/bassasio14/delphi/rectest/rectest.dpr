program rectest;

uses
  Forms,
  arMain in 'arMain.pas' {frmMain};

begin
  Application.Initialize;
  Application.CreateForm(TfrmMain, frmMain);
  Application.Run;
end.
