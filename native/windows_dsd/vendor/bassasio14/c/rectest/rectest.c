/*
	ASIO version of BASS recording example
	Copyright (c) 2002-2019 Un4seen Developments Ltd.
*/

#include <windows.h>
#include <commctrl.h>
#include <stdlib.h>
#include <stdio.h>
#include "bass.h"
#include "bassasio.h"

HWND win;

#define UM_STOP WM_APP

#define BUFSTEP 200000	// memory allocation unit

int input;			// current input source
char *recbuf;		// recording buffer
DWORD reclen;			// recording length
BOOL recording;
float level;

HSTREAM chan;			// BASS playback/decoding channel

// display error messages
void Error(const char *es)
{
	char mes[200];
	sprintf(mes, "%s\n(error code: %d/%d)", es, BASS_ASIO_ErrorGetCode(), BASS_ErrorGetCode());
	MessageBox(win, mes, 0, 0);
}

// messaging macros
#define MESS(id,m,w,l) SendDlgItemMessage(win,id,m,(WPARAM)(w),(LPARAM)(l))
#define DLGITEM(id) GetDlgItem(win,id)

DWORD CALLBACK AsioProc(BOOL isinput, DWORD channel, void *buffer, DWORD length, void *user)
{
	if (isinput) { // recording
		if (!recbuf) return 0;
		// increase buffer size if needed
		if ((reclen % BUFSTEP) + length >= BUFSTEP) {
			recbuf = realloc(recbuf, ((reclen + length) / BUFSTEP + 1) * BUFSTEP);
			if (!recbuf) {
				PostMessage(win, UM_STOP, 0, 0); // can't stop the ASIO device here, so post a message to do it in the main thread
				return 0;
			}
		}
		// buffer the data
		memcpy(recbuf + reclen, buffer, length);
		reclen += length;
		return 0;
	} else { // playing
		int c = BASS_ChannelGetData(chan, buffer, length); // get data from the decoder
		if (c == -1) c = 0;
		return c;
	}
}

void StartRecording()
{
	WAVEFORMATEX *wf;
	if (recbuf) { // free old recording
		BASS_ASIO_Stop(); // stop ASIO device in case it was playing
		BASS_ASIO_ChannelReset(FALSE, -1, BASS_ASIO_RESET_ENABLE); // disable outputs in preparation for recording
		BASS_StreamFree(chan);
		chan = 0;
		free(recbuf);
		recbuf = NULL;
		EnableWindow(DLGITEM(11), FALSE);
		EnableWindow(DLGITEM(12), FALSE);
	}
	// allocate initial buffer and make space for WAVE header
	recbuf = malloc(BUFSTEP);
	reclen = 44;
	// fill the WAVE header
	memcpy(recbuf, "RIFF\0\0\0\0WAVEfmt \20\0\0\0", 20);
	memcpy(recbuf + 36, "data\0\0\0\0", 8);
	wf = (WAVEFORMATEX*)(recbuf + 20);
	wf->wFormatTag = 1;
	wf->nChannels = 2;
	wf->wBitsPerSample = 16;
	wf->nSamplesPerSec = BASS_ASIO_GetRate(); // using device's current/default sample rate
	if (!wf->nSamplesPerSec) { // the device has no defined rate? try/assume 44100...
		wf->nSamplesPerSec = 44100;
		BASS_ASIO_SetRate(44100);
	}
	wf->nBlockAlign = wf->nChannels * wf->wBitsPerSample / 8;
	wf->nAvgBytesPerSec = wf->nSamplesPerSec * wf->nBlockAlign;
	{
		// enable the selected input and set it to 16-bit
		BASS_ASIO_ChannelReset(TRUE, -1, BASS_ASIO_RESET_ENABLE); // disable all inputs, then...
		BASS_ASIO_ChannelEnable(TRUE, input, AsioProc, 0); // enable the selected
		BASS_ASIO_ChannelSetFormat(TRUE, input, BASS_ASIO_FORMAT_16BIT); // want 16-bit data
	}
	// start the device
	if (!BASS_ASIO_Start(0, 0)) {
		Error("Can't start recording");
		free(recbuf);
		recbuf = 0;
		return;
	}
	recording = TRUE;
	MESS(10, WM_SETTEXT, 0, "Stop");
}

void StopRecording()
{
	BASS_ASIO_Stop(); // stop ASIO device
	recording = 0;
	MESS(10, WM_SETTEXT, 0, "Record");
	if (!recbuf) return;
	// complete the WAVE header
	*(DWORD*)(recbuf + 4) = reclen - 8;
	*(DWORD*)(recbuf + 40) = reclen - 44;
	// create a BASS stream from the recording
	if (chan = BASS_StreamCreateFile(BASS_FILE_MEM, recbuf, 0, reclen, BASS_STREAM_DECODE)) {
		// enable "play" & "save" buttons
		EnableWindow(DLGITEM(11), TRUE);
		EnableWindow(DLGITEM(12), TRUE);
	}
}

// write the recorded data to disk
void WriteToDisk()
{
	FILE *fp;
	char file[MAX_PATH] = "";
	OPENFILENAME ofn = {0};
	ofn.lStructSize = sizeof(ofn);
	ofn.hwndOwner = win;
	ofn.nMaxFile = MAX_PATH;
	ofn.lpstrFile = file;
	ofn.Flags = OFN_HIDEREADONLY | OFN_EXPLORER;
	ofn.lpstrFilter = "WAV files\0*.wav\0All files\0*.*\0\0";
	ofn.lpstrDefExt = "wav";
	if (!GetSaveFileName(&ofn)) return;
	if (!(fp = fopen(file, "wb"))) {
		Error("Can't create the file");
		return;
	}
	fwrite(recbuf, reclen, 1, fp);
	fclose(fp);
}

INT_PTR CALLBACK dialogproc(HWND h, UINT m, WPARAM w, LPARAM l)
{
	switch (m) {
		case WM_TIMER:
			{
				// update the display
				char text[30] = "";
				level = level > 0.05 ? level - 0.05 : 0;
				if (recording) { // recording
					float l;
					if ((l = BASS_ASIO_ChannelGetLevel(TRUE, input)) > level) level = l;
					if ((l = BASS_ASIO_ChannelGetLevel(TRUE, input + 1)) > level) level = l;
					sprintf(text, "%d", reclen);
				} else if (chan) {
					if (BASS_ASIO_IsStarted()) { // ASIO output started, get the level...
						float l;
						if ((l = BASS_ASIO_ChannelGetLevel(FALSE, 0)) > level) level = l;
						if ((l = BASS_ASIO_ChannelGetLevel(FALSE, 1)) > level) level = l;
					}
					if (BASS_ChannelIsActive(chan)) // playing
						sprintf(text, "%I64d / %I64d", BASS_ChannelGetPosition(chan, BASS_POS_BYTE), BASS_ChannelGetLength(chan, BASS_POS_BYTE));
					else
						sprintf(text, "%I64d", BASS_ChannelGetLength(chan, BASS_POS_BYTE));
				}
				MESS(20, WM_SETTEXT, 0, text);
				{
					// draw the level bar
					HWND w = DLGITEM(30);
					HDC dc = GetWindowDC(w);
					RECT r;
					GetClientRect(w, &r);
					InflateRect(&r, -1, -1);
					r.top = r.bottom * (1 - level);
					FillRect(dc, &r, GetStockObject(WHITE_BRUSH));
					r.bottom = r.top;
					r.top = 1;
					FillRect(dc, &r, GetStockObject(LTGRAY_BRUSH));
					ReleaseDC(w, dc);
				}
			}
			return 1;

		case WM_COMMAND:
			switch (LOWORD(w)) {
				case IDCANCEL:
					DestroyWindow(h);
					return 1;
				case 10:
					if (!recording)
						StartRecording();
					else
						StopRecording();
					return 1;
				case 11:
					BASS_ChannelSetPosition(chan, 0, BASS_POS_BYTE); // rewind the playback stream
					if (!BASS_ASIO_IsStarted()) { // need to start the ASIO output...
						BASS_ASIO_ChannelReset(TRUE, -1, BASS_ASIO_RESET_ENABLE); // disable all inputs
						BASS_ASIO_ChannelEnable(FALSE, 0, AsioProc, 0); // enable the 1st output
						BASS_ASIO_ChannelJoin(FALSE, 1, 0); // join the next output for stereo
						BASS_ASIO_ChannelSetFormat(FALSE, 0, BASS_ASIO_FORMAT_16BIT); // playing 16-bit data
						// start the device
						if (!BASS_ASIO_Start(0, 0))
							Error("Can't start playing");
					}
					return 1;
				case 12:
					WriteToDisk();
					return 1;
				case 13:
					if (HIWORD(w) == CBN_SELCHANGE) { // input selection changed
						input = MESS(13, CB_GETCURSEL, 0, 0) * 2; // get the selection
						MESS(14, TBM_SETPOS, 1, BASS_ASIO_ChannelGetVolume(TRUE, input) * 100); // update the level control
						if (recording) { // need to change the enabled inputs on the device...
							BASS_ASIO_Stop(); // stop ASIO processing
							BASS_ASIO_ChannelReset(TRUE, -1, BASS_ASIO_RESET_ENABLE); // disable all inputs, then...
							BASS_ASIO_ChannelEnable(TRUE, input, AsioProc, 0); // enable new inputs
							BASS_ASIO_ChannelSetFormat(TRUE, input, BASS_ASIO_FORMAT_16BIT); // want 16-bit data
							BASS_ASIO_Start(0, 0); // resume ASIO processing
						}
					}
					return 1;
			}
			break;

		case WM_HSCROLL:
			if (l) {
				float level = SendMessage((HWND)l, TBM_GETPOS, 0, 0) / 100.0f; // get level
				BASS_ASIO_ChannelSetVolume(TRUE, input, level); // set left input level
				BASS_ASIO_ChannelSetVolume(TRUE, input + 1, level); // set right input level
			}
			return 1;

		case WM_INITDIALOG:
			win = h;
			// initialize first available ASIO device
			if (!BASS_ASIO_Init(-1, 0)) {
				Error("Can't find an available ASIO device");
				DestroyWindow(win);
				return 0;
			}
			{
				// get list of inputs (assuming channels are all ordered in left/right pairs)
				int c;
				BASS_ASIO_CHANNELINFO i, i2;
				for (c = 0; BASS_ASIO_ChannelGetInfo(TRUE, c, &i); c += 2) {
					char name[200];
					if (!BASS_ASIO_ChannelGetInfo(TRUE, c + 1, &i2)) break; // no "right" channel
					_snprintf(name, sizeof(name), "%s + %s", i.name, i2.name);
					MESS(13, CB_ADDSTRING, 0, name);
					BASS_ASIO_ChannelJoin(TRUE, c + 1, c); // join the pair of channels
				}
				MESS(13, CB_SETCURSEL, input, 0);
			}
			MESS(14, TBM_SETRANGE, FALSE, MAKELONG(0, 100));
			MESS(14, TBM_SETPOS, 1, 100);
			// initialize BASS "no sound" device for "decoding" of the recorded WAVE file
			BASS_Init(0, 48000, 0, 0, 0);
			SetTimer(h, 0, 50, 0);
			return 1;

		case WM_DESTROY:
			// release it all
			BASS_ASIO_Free();
			BASS_Free();
			return 1;

		case UM_STOP:
			StopRecording();
			Error("Out of memory!");
			return 1;
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

	{
		// enable trackbar support (for the level control)
		INITCOMMONCONTROLSEX cc = {sizeof(cc), ICC_BAR_CLASSES};
		InitCommonControlsEx(&cc);
	}

	DialogBox(hInstance, (char*)1000, 0, &dialogproc);

	return 0;
}
