object Form1: TForm1
  Left = 218
  Top = 115
  Caption = 'LiveFX'
  ClientHeight = 151
  ClientWidth = 390
  Color = clBtnFace
  Font.Charset = DEFAULT_CHARSET
  Font.Color = clWindowText
  Font.Height = -11
  Font.Name = 'Tahoma'
  Font.Style = []
  OldCreateOrder = False
  OnClose = FormClose
  OnCreate = FormCreate
  PixelsPerInch = 96
  TextHeight = 13
  object Label1: TLabel
    Left = 8
    Top = 51
    Width = 48
    Height = 13
    Caption = 'Latency : '
  end
  object Label2: TLabel
    Left = 8
    Top = 5
    Width = 89
    Height = 13
    Caption = 'Choose the input :'
  end
  object Label3: TLabel
    Left = 8
    Top = 115
    Width = 41
    Height = 13
    Caption = 'Volume :'
  end
  object ComboBox1: TComboBox
    Left = 8
    Top = 24
    Width = 377
    Height = 21
    Style = csDropDownList
    TabOrder = 0
    OnChange = ComboBox1Change
  end
  object TrackBar1: TTrackBar
    Left = 55
    Top = 103
    Width = 330
    Height = 33
    Max = 100
    TabOrder = 1
    TickMarks = tmBoth
    OnChange = TrackBar1Change
  end
  object CheckBox1: TCheckBox
    Left = 8
    Top = 80
    Width = 97
    Height = 17
    Caption = 'Gargle'
    TabOrder = 2
    OnClick = CheckBox1Click
  end
  object CheckBox2: TCheckBox
    Left = 103
    Top = 80
    Width = 97
    Height = 17
    Caption = 'Flanger'
    TabOrder = 3
    OnClick = CheckBox2Click
  end
  object CheckBox3: TCheckBox
    Left = 197
    Top = 80
    Width = 85
    Height = 17
    Caption = 'Reverb'
    TabOrder = 4
    OnClick = CheckBox3Click
  end
  object CheckBox4: TCheckBox
    Left = 288
    Top = 80
    Width = 97
    Height = 17
    Caption = 'Chorus'
    TabOrder = 5
    OnClick = CheckBox4Click
  end
end
