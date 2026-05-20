/*
	ASIO version of BASS multi-speaker example
	Copyright (c) 2003-2019 Un4seen Developments Ltd.
*/

#include <windows.h>
#include <stdio.h>

#include "bassasio.h"
#include "bass.h"

HWND win;

HSTREAM chan[4];
HSTREAM dummy;

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
					return 1;
				case 10: // open a file to play on #1
				case 11: // open a file to play on #2
				case 12: // open a file to play on #3
				case 13: // open a file to play on #4
					{
						int output = LOWORD(w) - 10;
						BASS_CHANNELINFO info;
						char file[MAX_PATH] = "";
						ofn.lpstrFile = file;
						if (GetOpenFileName(&ofn)) {
							BASS_ASIO_ChannelPause(FALSE, output * 2); // pause output channel
							BASS_StreamFree(chan[output]); // free old stream before opening new
							if (!(chan[output] = BASS_StreamCreateFile(0, file, 0, 0, BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT | BASS_SAMPLE_LOOP))) {
								MESS(10 + output, WM_SETTEXT, 0, "click here to open a file...");
								Error("Can't play the file");
							} else {
								BASS_ChannelGetInfo(chan[output], &info);
								if (info.chans != 2) { // only stereo is allowed
									MESS(10 + output, WM_SETTEXT, 0, "click here to open a file...");
									BASS_StreamFree(chan[output]);
									Error("Only stereo files are supported");
									break;
								}
								BASS_ASIO_ChannelEnableBASS(FALSE, output * 2, chan[output], TRUE); // enable ASIO channels
								BASS_ASIO_ChannelReset(FALSE, output * 2, BASS_ASIO_RESET_PAUSE); // unpause the channel
								MESS(10 + output, WM_SETTEXT, 0, file);
							}
						}
					}
					return 1;
				case 20: // swap #1 & #2
				case 21: // swap #2 & #3
				case 22: // swap #3 & #4
					{
						int output = LOWORD(w) - 20;
						// pause the channels while swapping
						BASS_ASIO_ChannelPause(FALSE, output * 2);
						BASS_ASIO_ChannelPause(FALSE, (output + 1) * 2);
						{
							// swap handles
							HSTREAM temp = chan[output];
							chan[output] = chan[output + 1];
							chan[output + 1] = temp;
						}
						// update ASIO channels
						BASS_ASIO_ChannelEnableBASS(FALSE, output * 2, chan[output], TRUE);
						BASS_ASIO_ChannelEnableBASS(FALSE, (output + 1) * 2, chan[output + 1], TRUE);
						// unpause channels
						if (BASS_ChannelIsActive(chan[output]))
							BASS_ASIO_ChannelReset(FALSE, output * 2, BASS_ASIO_RESET_PAUSE);
						if (BASS_ChannelIsActive(chan[output + 1]))
							BASS_ASIO_ChannelReset(FALSE, (output + 1) * 2, BASS_ASIO_RESET_PAUSE);
						{
							// swap text
							char temp1[MAX_PATH], temp2[MAX_PATH];
							MESS(10 + output, WM_GETTEXT, MAX_PATH, temp1);
							MESS(10 + output + 1, WM_GETTEXT, MAX_PATH, temp2);
							MESS(10 + output, WM_SETTEXT, 0, temp2);
							MESS(10 + output + 1, WM_SETTEXT, 0, temp1);
						}
					}
					return 1;
			}
			break;

		case WM_INITDIALOG:
			win = h;
			memset(&ofn, 0, sizeof(ofn));
			ofn.lStructSize = sizeof(ofn);
			ofn.hwndOwner = h;
			ofn.nMaxFile = MAX_PATH;
			ofn.Flags = OFN_HIDEREADONLY | OFN_EXPLORER;
			ofn.lpstrFilter = "Streamable files\0*.mp3;*.mp2;*.mp1;*.ogg;*.wav;*.aif\0All files\0*.*\0\0";
			// initialize first available ASIO device
			if (!BASS_ASIO_Init(-1, 0)) {
				Error("Can't find an available ASIO device");
				DestroyWindow(win);
				return 0;
			}
			// initialize BASS "no sound" device
			BASS_Init(0, 48000, 0, 0, NULL);
			// create a dummy stream for reserving ASIO channels
			dummy = BASS_StreamCreate(2, 48000, BASS_SAMPLE_FLOAT | BASS_STREAM_DECODE, STREAMPROC_DUMMY, NULL);
			{
				// prepare ASIO output channel pairs (up to 4)
				int a;
				BASS_ASIO_INFO i;
				BASS_ASIO_GetInfo(&i);
				for (a = 0; a < 4; a++) {
					BASS_ASIO_CHANNELINFO i, i2;
					if (BASS_ASIO_ChannelGetInfo(FALSE, a * 2, &i) && BASS_ASIO_ChannelGetInfo(FALSE, a * 2 + 1, &i2)) {
						char name[200];
						sprintf(name, "%s + %s", i.name, i2.name);
						MESS(30 + a, WM_SETTEXT, 0, name); // display channel names
						BASS_ASIO_ChannelEnableBASS(FALSE, 0, dummy, TRUE); // enable ASIO channels using the dummy stream
						BASS_ASIO_ChannelPause(FALSE, a * 2); // not playing anything immediately, so pause the channel
					} else { // no more channels
						EnableWindow(GetDlgItem(h, 10 + a), FALSE);
						if (a) EnableWindow(GetDlgItem(h, 19 + a), FALSE);
					}
				}
			}
			// start the device using default buffer/latency and 2 threads for parallel processing
			if (!BASS_ASIO_Start(0, 2)) {
				Error("Can't start device");
				DestroyWindow(win);
			}
			return 1;

		case WM_DESTROY:
			BASS_ASIO_Free();
			BASS_Free();
			break;
	}
	return 0;
}

int PASCAL WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nCmdShow)
{
	// check the correct BASS was loaded
	if (HIWORD(BASS_GetVersion()) != BASSVERSION) {
		MessageBox(0, "An incorrect version of BASS.DLL was loaded", 0, MB_ICONERROR);
		return 0;
	}

	/* main dialog */
	DialogBox(hInstance, (char*)1000, 0, &dialogproc);

	return 0;
}
