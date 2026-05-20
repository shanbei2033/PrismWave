/*
	ASIO version of the BASS simple console player
	Copyright (c) 1999-2021 Un4seen Developments Ltd.
*/

#include <stdlib.h>
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

void ListDevices()
{
	BASS_ASIO_DEVICEINFO di;
	int a;
	for (a = 0; BASS_ASIO_GetDeviceInfo(a, &di); a++) {
		printf("dev %d: %s\n", a, di.name);
	}
}

int main(int argc, char **argv)
{
	DWORD chan, level;
	BOOL ismod;
	QWORD pos;
	double secs;
	int a, filep, device = -1;
	BASS_CHANNELINFO info;

	printf("BASS+ASIO simple console player\n"
		"-------------------------------\n");

	// check the correct BASS was loaded
	if (HIWORD(BASS_GetVersion()) != BASSVERSION) {
		printf("An incorrect version of BASS was loaded");
		return 0;
	}

	for (filep = 1; filep < argc; filep++) {
		if (!strcmp(argv[filep], "-l")) {
			ListDevices();
			return 0;
		} else if (!strcmp(argv[filep], "-d") && filep + 1 < argc)
			device = atoi(argv[++filep]);
		else
			break;
	}
	if (filep == argc) {
		printf("\tusage: contest [-l] [-d #] <file>\n"
			"\t-l = list devices\n"
			"\t-d = device number\n");
		return 0;
	}

	BASS_SetConfig(BASS_CONFIG_NET_PLAYLIST, 2); // enable playlist processing

	BASS_Init(0, 48000, 0, 0, NULL); // initialize BASS "no sound" device

	ismod = FALSE;
	if (strstr(argv[filep], "://")) {
		// try streaming the URL
		chan = BASS_StreamCreateURL(argv[filep], 0, BASS_SAMPLE_LOOP | BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT, 0, 0);
	} else {
		// try streaming the file
		chan = BASS_StreamCreateFile(0, argv[filep], 0, 0, BASS_SAMPLE_LOOP | BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT);
		if (!chan && BASS_ErrorGetCode() == BASS_ERROR_FILEFORM) {
			// try MOD music formats
			chan = BASS_MusicLoad(0, argv[filep], 0, 0, BASS_SAMPLE_LOOP | BASS_MUSIC_RAMPS | BASS_MUSIC_PRESCAN | BASS_MUSIC_DECODE | BASS_SAMPLE_FLOAT, 1);
			ismod = TRUE;
		}
	}
	if (!chan) Error("Can't play the file");

	BASS_ChannelGetInfo(chan, &info);
	printf("ctype: %x\n", info.ctype);
	if (!ismod) {
		if (info.origres)
			printf("format: %u Hz, %d chan, %d bit\n", info.freq, info.chans, LOWORD(info.origres));
		else
			printf("format: %u Hz, %d chan\n", info.freq, info.chans);
	}
	pos = BASS_ChannelGetLength(chan, BASS_POS_BYTE);
	if (pos != -1) {
		double secs = BASS_ChannelBytes2Seconds(chan, pos);
		if (ismod)
			printf("length: %u:%02u (%llu samples), %u orders\n", (int)secs / 60, (int)secs % 60, (long long)(secs * info.freq), (DWORD)BASS_ChannelGetLength(chan, BASS_POS_MUSIC_ORDER));
		else
			printf("length: %u:%02u (%llu samples)\n", (int)secs / 60, (int)secs % 60, (long long)(secs * info.freq));
	} else if (ismod)
		printf("length: %u orders\n", (DWORD)BASS_ChannelGetLength(chan, BASS_POS_MUSIC_ORDER));

	if (!BASS_ASIO_Init(device, BASS_ASIO_THREAD)) // initialize ASIO device
		Error("Can't initialize ASIO device");
	if (!BASS_ASIO_ChannelEnableBASS(FALSE, 0, chan, TRUE)) // enable ASIO channel(s)
		Error("Can't enable ASIO channel(s)");
	if (info.chans == 1) BASS_ASIO_ChannelEnableMirror(1, FALSE, 0); // mirror mono channel to form stereo output
	BASS_ASIO_SetRate(info.freq); // try to set the device rate to avoid resampling
	if (!BASS_ASIO_Start(0, 0)) // start the device using default buffer/latency
		Error("Can't start ASIO output");

	while (!_kbhit() && BASS_ChannelIsActive(chan)) {
		// display some stuff and wait a bit
		pos = BASS_ChannelGetPosition(chan, BASS_POS_BYTE);
		secs = BASS_ChannelBytes2Seconds(chan, pos);
		printf(" %u:%02u (%08lld)", (int)secs / 60, (int)secs % 60, (long long)(secs * info.freq));
		if (ismod) {
			pos = BASS_ChannelGetPosition(chan, BASS_POS_MUSIC_ORDER);
			printf(" | %03u:%03u", LOWORD(pos), HIWORD(pos));
		}
		printf(" | L ");
		level = BASS_ASIO_ChannelGetLevel(FALSE, 0) * 32768; // left channel level
		for (a = 27204; a > 200; a = a * 2 / 3) putchar(level >= a ? '*' : '-');
		putchar(' ');
		if (BASS_ASIO_ChannelIsActive(FALSE, 1))
			level = BASS_ASIO_ChannelGetLevel(FALSE, 1) * 32768; // right channel level
		for (a = 210; a < 32768; a = a * 3 / 2) putchar(level >= a ? '*' : '-');
		printf(" R - cpu %.2f%%  \r", BASS_ASIO_GetCPU());
		fflush(stdout);
		Sleep(50);
	}
	printf("                                                                             \r");

	BASS_ASIO_Free();
	BASS_Free();
	return 0;
}
