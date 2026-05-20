object frmMain: TfrmMain
  Left = 0
  Top = 0
  Caption = 'ASIO recording test'
  ClientHeight = 159
  ClientWidth = 530
  Color = clBtnFace
  Font.Charset = DEFAULT_CHARSET
  Font.Color = clWindowText
  Font.Height = -16
  Font.Name = 'Tahoma'
  Font.Style = []
  OldCreateOrder = False
  OnCreate = FormCreate
  OnDestroy = FormDestroy
  PixelsPerInch = 96
  TextHeight = 19
  object Label1: TLabel
    Left = 256
    Top = 48
    Width = 58
    Height = 19
    Caption = 'volume:'
  end
  object gaMicPPM: TGauge
    Left = 503
    Top = 8
    Width = 18
    Height = 140
    ForeColor = clAqua
    Kind = gkVerticalBar
    Progress = 0
    ShowText = False
  end
  object stPos: TStaticText
    Left = 8
    Top = 125
    Width = 345
    Height = 23
    Alignment = taCenter
    AutoSize = False
    BorderStyle = sbsSunken
    TabOrder = 0
  end
  object bSave: TButton
    Left = 256
    Top = 86
    Width = 97
    Height = 33
    Caption = 'Save'
    TabOrder = 1
    OnClick = bSaveClick
  end
  object bPlay: TButton
    Left = 130
    Top = 86
    Width = 97
    Height = 33
    Caption = 'Play'
    TabOrder = 2
    OnClick = bPlayClick
  end
  object bRecord: TButton
    Left = 8
    Top = 86
    Width = 97
    Height = 33
    Caption = 'Record'
    TabOrder = 3
    OnClick = bRecordClick
  end
  object tbMicVolumeLevel: TTrackBar
    Left = 320
    Top = 49
    Width = 177
    Height = 25
    Max = 100
    ShowSelRange = False
    TabOrder = 4
    TickStyle = tsNone
    OnChange = tbMicVolumeLevelChange
  end
  object cbDevices: TComboBox
    Left = 8
    Top = 8
    Width = 489
    Height = 27
    AutoComplete = False
    Style = csDropDownList
    TabOrder = 5
    OnChange = cbDevicesChange
  end
  object Timer1: TTimer
    Interval = 30
    OnTimer = Timer1Timer
    Left = 216
    Top = 66
  end
  object SD: TSaveDialog
    Filter = 'WAV file (*.wav)|*.wav|All files (*.*)|*.*'
    Left = 432
    Top = 72
  end
end
