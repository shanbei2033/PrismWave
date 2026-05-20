/*
	ASIO version of BASS simple synth
	Copyright (c) 2001-2021 Un4seen Developments Ltd.
*/

#include <stdio.h>
#include <conio.h>
#include <math.h>
#include "bassasio.h"
#include "bass.h"

// display error messages
void Error(const char *text)
{
	printf("Error(%d/%d): %s\n", BASS_ErrorGetCode(), BASS_ASIO_ErrorGetCode(), text);
	BASS_ASIO_Free();
	BASS_Free();
	exit(0);
}

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

#define KEYS 20
const WORD keys[KEYS] = {
	'Q', '2', 'W', '3', 'E', 'R', '5', 'T', '6', 'Y', '7', 'U',
	'I', '9', 'O', '0', 'P', 219, 187, 221
};
#define MAXVOL 0.22
#define DECAY (MAXVOL/4000)
float vol[KEYS], pos[KEYS]; // key volume and position/phase
double samrate;

// ASIO function
DWORD CALLBACK AsioProc(BOOL input, DWORD channel, void *buffer, DWORD length, void *user)
{
	DWORD c = BASS_ChannelGetData((DWORD)user, buffer, length);
	if (c == -1) c = 0; // an error, no data
	return c;
}

DWORD CALLBACK StreamProc(HSTREAM handle, float *buffer, DWORD length, void *user)
{
	int k, c;
	float omega;
	memset(buffer, 0, length);
	for (k = 0; k < KEYS; k++) {
		if (!vol[k]) continue;
		omega = 2 * M_PI * pow(2.0, (k + 3) / 12.0) * 440.0 / samrate;
		for (c = 0; c < length / sizeof(float); c += 2) {
			buffer[c] += sin(pos[k]) * vol[k];
			buffer[c + 1] = buffer[c]; // left and right channels are the same
			pos[k] += omega;
			if (vol[k] < MAXVOL) {
				vol[k] -= DECAY;
				if (vol[k] <= 0) { // faded-out
					vol[k] = 0;
					break;
				}
			}
		}
		pos[k] = fmod(pos[k], 2 * M_PI);
	}
	return length;
}

int main(int argc, char **argv)
{
	BASS_ASIO_INFO info;
	HSTREAM stream; // the stream
	const char *fxname[9] = {
		"CHORUS", "COMPRESSOR", "DISTORTION", "ECHO",
		"FLANGER", "GARGLE", "I3DL2REVERB", "PARAMEQ", "REVERB"
	};
	HFX fx[9] = { 0 }; // effect handles
	INPUT_RECORD keyin;
	DWORD r, buflen;

	printf("BASS+ASIO simple synth\n"
		"----------------------\n");

	// check the correct BASS was loaded
	if (HIWORD(BASS_GetVersion()) != BASSVERSION) {
		printf("An incorrect version of BASS was loaded");
		return 0;
	}

	// initialize first available ASIO device
	if (!BASS_ASIO_Init(-1, BASS_ASIO_THREAD))
		Error("Can't find an available ASIO device");

	// get device info for buffer size range, begin with default
	BASS_ASIO_GetInfo(&info);
	buflen = info.bufpref;

	samrate = BASS_ASIO_GetRate();

	// initialize BASS "no sound" device
	BASS_Init(0, 48000, 0, 0, NULL);

	// create a stream, stereo so that effects sound nice
	stream = BASS_StreamCreate(samrate, 2, BASS_SAMPLE_FLOAT | BASS_STREAM_DECODE, (STREAMPROC*)StreamProc, 0);

	// enable ASIO channels
	if (!BASS_ASIO_ChannelEnableBASS(FALSE, 0, stream, TRUE))
		Error("Can't enable ASIO channel(s)");

	// start the ASIO device
	if (!BASS_ASIO_Start(buflen, 0))
		Error("Can't start ASIO device");

	printf("press these keys to play:\n\n"
		"  2 3  5 6 7  9 0  =\n"
		" Q W ER T Y UI O P[ ]\n\n"
		"press -/+ to de/increase the buffer\n"
		"press spacebar to quit\n\n");
	printf("press F1-F9 to toggle effects\n\n");

	printf("buffer = %d (%.1fms latency)\r", buflen, 1000 * BASS_ASIO_GetLatency(0) / samrate);

	while (ReadConsoleInput(GetStdHandle(STD_INPUT_HANDLE), &keyin, 1, &r)) {
		int key;
		if (keyin.EventType != KEY_EVENT) continue;
		if (keyin.Event.KeyEvent.wVirtualKeyCode == VK_SPACE) break;
		if (keyin.Event.KeyEvent.bKeyDown) {
			if (keyin.Event.KeyEvent.wVirtualKeyCode == VK_SUBTRACT
				|| keyin.Event.KeyEvent.wVirtualKeyCode == VK_ADD) {
				// restart the device with smaller/larger buffer
				DWORD newlen;
				BASS_ASIO_Stop();
				if (keyin.Event.KeyEvent.wVirtualKeyCode == VK_SUBTRACT) {
					if (info.bufgran == -1)
						buflen >>= 1; // halve the buffer
					else
						newlen = buflen - (DWORD)samrate / 1000; // reduce buffer by 1ms
					if (newlen < info.bufmin) newlen = info.bufmin;
				} else {
					if (info.bufgran == -1)
						buflen <<= 1; // double the buffer
					else
						newlen = buflen + (DWORD)samrate / 1000; // increase buffer by 1ms
					if (newlen > info.bufmax) newlen = info.bufmax;
				}
				if (BASS_ASIO_Start(newlen, 0)) { // successfully changed buffer size
					buflen = newlen;
					printf("buffer = %d (%.1fms latency)\t\t\r", buflen, 1000 * BASS_ASIO_GetLatency(0) / samrate);
				} else // failed...
					BASS_ASIO_Start(buflen, 0); // reuse the previous buffer size
			}
			if (keyin.Event.KeyEvent.wVirtualKeyCode >= VK_F1
				&& keyin.Event.KeyEvent.wVirtualKeyCode <= VK_F9) {
				int n = keyin.Event.KeyEvent.wVirtualKeyCode - VK_F1;
				if (fx[n]) {
					BASS_ChannelRemoveFX(stream, fx[n]);
					fx[n] = 0;
					printf("effect %s = OFF\t\t\r", fxname[n]);
				} else {
					// set the effect, not bothering with parameters (use defaults)
					if (fx[n] = BASS_ChannelSetFX(stream, BASS_FX_DX8_CHORUS + n, 0))
						printf("effect %s = ON\t\t\r", fxname[n]);
				}
			}
		}
		for (key = 0; key < KEYS; key++)
			if (keyin.Event.KeyEvent.wVirtualKeyCode == keys[key]) {
				if (keyin.Event.KeyEvent.bKeyDown && vol[key] < MAXVOL) {
					pos[key] = 0;
					vol[key] = MAXVOL + DECAY / 2; // start key (setting "vol" slightly higher than MAXVOL to cover any rounding-down)
				} else if (!keyin.Event.KeyEvent.bKeyDown && vol[key])
					vol[key] -= DECAY; // trigger key fadeout
				break;
			}
	}

	BASS_ASIO_Free();
	BASS_Free();
	return 0;
}
