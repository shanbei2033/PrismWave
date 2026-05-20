Attribute VB_Name = "BASSDSD"
' BASSDSD 2.4 Visual Basic module
' Copyright (c) 2014-2025 Un4seen Developments Ltd.
'
' See the BASSDSD.CHM file for more detailed documentation

' Additional BASS_SetConfig options
Global Const BASS_CONFIG_DSD_FREQ = &H10800
Global Const BASS_CONFIG_DSD_GAIN = &H10801

' Additional BASS_DSD_StreamCreateFile/etc flags
Global Const BASS_DSD_RAW = &H200
Global Const BASS_DSD_DOP = &H400
Global Const BASS_DSD_DOP_AA = &H800

' BASS_CHANNELINFO type
Global Const BASS_CTYPE_STREAM_DSD = &H11700

' Additional tag types
Global Const BASS_TAG_DSD_ARTIST = &H13000 ' DSDIFF artist : ASCII string
Global Const BASS_TAG_DSD_TITLE = &H13001 ' DSDIFF title : ASCII string
Global Const BASS_TAG_DSD_COMMENT = &H0x13100 ' + index, DSDIFF comment : TAG_DSD_COMMENT structure

Type TAG_DSD_COMMENT
	timeStampYear As Integer ' creation year
	timeStampMonth As Byte ' creation month
	timeStampDay As Byte ' creation day
	timeStampHour As Byte ' creation hour
	timeStampMinutes As Byte ' creation minutes
	cmtType As Integer ' comment type
	cmtRef As Integer ' comment reference
	count As Long ' string length
	commentText() As String ' text
End Type

' Additional attributes
Global Const BASS_ATTRIB_DSD_GAIN = &H14000
Global Const BASS_ATTRIB_DSD_RATE = &H14001

Declare Function BASS_DSD_StreamCreateFile64 Lib "bassdsd.dll" Alias "BASS_DSD_StreamCreateFile" (ByVal filetype As Long, ByVal file As Any, ByVal offset As Long, ByVal offsethigh As Long, ByVal length As Long, ByVal lengthhigh As Long, ByVal flags As Long, ByVal freq As Long) As Long
Declare Function BASS_DSD_StreamCreateURL Lib "bassdsd.dll" (ByVal url As String, ByVal offset As Long, ByVal flags As Long, ByVal proc As Long, ByVal user As Long, ByVal freq As Long) As Long
Declare Function BASS_DSD_StreamCreateFileUser Lib "bassdsd.dll" (ByVal system As Long, ByVal flags As Long, ByVal procs As Long, ByVal user As Long, ByVal freq As Long) As Long

' 32-bit wrapper for 64-bit BASS function
Function BASS_DSD_StreamCreateFile(ByVal filetype As Long, ByVal file As Long, ByVal offset As Long, ByVal length As Long, ByVal flags As Long, ByVal freq As Long) As Long
    BASS_DSD_StreamCreateFile = BASS_DSD_StreamCreateFile64(filetype, file, offset, 0, length, 0, flags Or BASS_UNICODE, freq)
End Function
