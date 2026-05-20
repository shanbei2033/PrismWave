/*
	ASIO version of BASS multiple output example
	Copyright (c) 2001-2019 Un4seen Developments Ltd.
*/

#include <windows.h>
#include <stdio.h>

#include "bassasio.h"
#include "bass.h"

HWND win;

DWORD outdev[2];	// output devices
HSTREAM chan[2];	// the streams

// display error messages
void Error(const char *es)
{
	char mes[200];
	sprintf(mes, "%s\n(error code: %d/%d)", es, BASS_ErrorGetCode(), BASS_ASIO_ErrorGetCode());
	MessageBox(win, mes, "Error", 0);
}

#define MESS(id,m,w,l) SendDlgItemMessage(win,id,m,(WPARAM)(w),(LPARAM)(l))

INT_PTR CALLBACK dialogproc(HWND h, UINT m, WPARAM w, LPARAM l)
{
	static OPENFILENAME ofn;

	switch (m) {
		case WM_COMMAND:
			switch (LOWORD(w)) {
				case IDCANCEL:
					DestroyWindow(h);
					break;
				case 10: // open a file to play on device #1
				case 11: // open a file to play on device #2
					{
						int devn = LOWORD(w) - 10;
						BASS_CHANNELINFO info;
						char file[MAX_PATH] = "";
						ofn.lpstrFilter = "streamable files\0*.mp3;*.mp2;*.mp1;*.ogg;*.wav;*.aif\0All files\0*.*\0\0";
						ofn.lpstrFile = file;
						if (GetOpenFileName(&ofn)) {
							BASS_ASIO_SetDevice(outdev[devn]); // set the ASIO device to work with
							BASS_ASIO_Stop(); // stop the device
							BASS_ASIO_ChannelReset(FALSE, -1, BASS_ASIO_RESET_ENABLE | BASS_ASIO_RESET_JOIN); // disable & unjoin all output channels
							BASS_StreamFree(chan[devn]); // free existing stream
							chan[devn] = BASS_StreamCreateFile(0, file, 0, 0, BASS_SAMPLE_LOOP | BASS_SAMPLE_FLOAT | BASS_STREAM_DECODE);
							if (!chan[devn]) {
								MESS(10 + devn, WM_SETTEXT, 0, "click here to open a file...");
								Error("Can't play the file");
								break;
							}
							MESS(10 + devn, WM_SETTEXT, 0, file);
							if (!BASS_ASIO_ChannelEnableBASS(FALSE, 0, chan[devn], TRUE)) // enable ASIO channel(s)
								Error("Can't enable ASIO channel(s)");
							BASS_ChannelGetInfo(chan[devn], &info);
							if (info.chans == 1) BASS_ASIO_ChannelEnableMirror(1, FALSE, 0); // mirror mono channel to form stereo output
							BASS_ASIO_SetRate(info.freq); // try to set the device rate to avoid resampling
							if (!BASS_ASIO_Start(0, 0)) // start the device using default buffer/latency
								Error("Can't start ASIO device");
						}
					}
					break;
			}
			break;

		case WM_INITDIALOG:
			win = h;
			memset(&ofn, 0, sizeof(ofn));
			ofn.lStructSize = sizeof(ofn);
			ofn.hwndOwner = h;
			ofn.nMaxFile = MAX_PATH;
			ofn.Flags = OFN_HIDEREADONLY | OFN_EXPLORER;
			// initialize ASIO devices
			if (!BASS_ASIO_Init(outdev[0], 0)) {
				Error("Can't initialize device 1");
				DestroyWindow(win);
			}
			if (!BASS_ASIO_Init(outdev[1], 0)) {
				Error("Can't initialize device 2");
				DestroyWindow(win);
			}
			{
				// display device names
				BASS_ASIO_DEVICEINFO i;
				BASS_ASIO_GetDeviceInfo(outdev[0], &i);
				MESS(20, WM_SETTEXT, 0, i.name);
				BASS_ASIO_GetDeviceInfo(outdev[1], &i);
				MESS(21, WM_SETTEXT, 0, i.name);
			}
			// initialize BASS "no sound" device
			BASS_Init(0, 48000, 0, 0, NULL);
			return 1;

		case WM_DESTROY:
			// release both ASIO devices and BASS
			BASS_ASIO_Free();
			BASS_ASIO_Free();
			BASS_Free();
			break;
	}
	return 0;
}

// Simple device selector dialog stuff begins here
INT_PTR CALLBACK devicedialogproc(HWND h, UINT m, WPARAM w, LPARAM l)
{
	switch (m) {
		case WM_COMMAND:
			switch (LOWORD(w)) {
				case 10:
					if (HIWORD(w) != LBN_DBLCLK) break;
				case IDOK:
					{
						int device = SendDlgItemMessage(h, 10, LB_GETCURSEL, 0, 0);
						EndDialog(h, device);
					}
					break;
			}
			break;

		case WM_INITDIALOG:
			{
				char text[30];
				BASS_ASIO_DEVICEINFO i;
				int c;
				sprintf(text, "Select output device #%d", l);
				SetWindowText(h, text);
				for (c = 0; BASS_ASIO_GetDeviceInfo(c, &i); c++)
					SendDlgItemMessage(h, 10, LB_ADDSTRING, 0, (LPARAM)i.name);
				SendDlgItemMessage(h, 10, LB_SETCURSEL, 0, 0);
			}
			return 1;
	}
	return 0;
}
// Device selector stuff ends here

int PASCAL WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nCmdShow)
{
	// check the correct BASS was loaded
	if (HIWORD(BASS_GetVersion()) != BASSVERSION) {
		MessageBox(0, "An incorrect version of BASS.DLL was loaded", 0, MB_ICONERROR);
		return 0;
	}

	/* Let the user choose the output devices */
	outdev[0] = DialogBoxParam(hInstance, (char*)2000, win, &devicedialogproc, 1);
	outdev[1] = DialogBoxParam(hInstance, (char*)2000, win, &devicedialogproc, 2);

	/* main dialog */
	DialogBox(hInstance, (char*)1000, 0, &dialogproc);

	return 0;
}
