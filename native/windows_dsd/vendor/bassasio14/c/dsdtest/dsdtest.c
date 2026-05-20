/*
	Simple DSD player
	Copyright (c) 2014-2019 Un4seen Developments Ltd.
*/

#include <stdlib.h>
#include <stdio.h>
#include <conio.h>
#include "bassasio.h"
#include "bassdsd.h"
#include "bass.h"

// display error messages
void Error(const char *text)
{
	printf("Error(%d/%d): %s\n", BASS_ErrorGetCode(), BASS_ASIO_ErrorGetCode(), text);
	BASS_ASIO_Free();
	BASS_Free();
	exit(0);
}

void main(int argc, char **argv)
{
	DWORD chan, device = -1, dop = 0;
	BASS_CHANNELINFO ci;
	float rate;
	int a;

	printf("Simple console mode ASIO DSD example : DFF/DSF player\n"
		"-----------------------------------------------------\n");

	// check the correct BASS was loaded
	if (HIWORD(BASS_GetVersion()) != BASSVERSION) {
		printf("An incorrect version of BASS was loaded");
		return;
	}

	for (a = 1; a < argc; a++) {
		if (!strcmp(argv[a], "-d") && a + 1 < argc) device = atoi(argv[++a]);
		else if (!strcmp(argv[a], "-p")) dop = 1;
		else break;
	}
	if (a != argc - 1) {
		printf("\tusage: dsdtest [-d #] [-p] <file>\n"
			"\t-d = device number\n"
			"\t-p = DSD-over-PCM only\n");
		return;
	}

	BASS_Init(0, 48000, 0, 0, NULL); // initialize BASS "no sound" device

	chan = BASS_DSD_StreamCreateFile(0, argv[argc - 1], 0, 0, BASS_DSD_RAW | BASS_STREAM_DECODE | BASS_SAMPLE_LOOP, 0); // open the file in DSD mode
	if (!chan) Error("Can't play the file");
	BASS_ChannelGetInfo(chan, &ci);
	BASS_ChannelGetAttribute(chan, BASS_ATTRIB_DSD_RATE, &rate); // get the DSD rate
	{
		QWORD len = BASS_ChannelGetLength(chan, BASS_POS_BYTE);
		DWORD time = (DWORD)BASS_ChannelBytes2Seconds(chan, len);
		printf("DSD rate: %g MHz\nLength: %I64u samples (%u:%02u)\n", rate / 1000000, len * 8 / ci.chans, time / 60, time % 60);
	}

	if (!BASS_ASIO_Init(device, BASS_ASIO_THREAD)) // initialize ASIO device
		Error("Can't initialize ASIO device");
	if (dop || !BASS_ASIO_SetDSD(TRUE)) { // if DSD-over-PCM is requested or the device doesn't support DSD mode, try DSD-over-PCM...
		BASS_StreamFree(chan);
		chan = BASS_DSD_StreamCreateFile(0, argv[argc - 1], 0, 0, BASS_DSD_DOP | BASS_SAMPLE_FLOAT | BASS_STREAM_DECODE | BASS_SAMPLE_LOOP, 0);
		BASS_ChannelGetInfo(chan, &ci); // refresh info
		rate = ci.freq; // PCM rate
	}
	printf("Output: %s\n", ci.flags & BASS_DSD_RAW ? "DSD" : "DSD-over-PCM");
	if (!BASS_ASIO_SetRate(rate)) // set the device rate
		Error("Can't set ASIO device to required rate");
	if (!BASS_ASIO_ChannelEnableBASS(FALSE, 0, chan, TRUE)) // enable ASIO channel(s)
		Error("Can't enable ASIO channel(s)");
	if (ci.chans == 1) BASS_ASIO_ChannelEnableMirror(1, FALSE, 0); // mirror mono channel to form stereo output
	if (!BASS_ASIO_Start(0, 0)) // start the device using default buffer/latency
		Error("Can't start ASIO output");

	while (!_kbhit() && BASS_ChannelIsActive(chan)) {
		// display some stuff and wait a bit
		QWORD pos = BASS_ChannelGetPosition(chan, BASS_POS_BYTE);
		DWORD time = BASS_ChannelBytes2Seconds(chan, pos);
		pos *= (ci.flags & BASS_DSD_RAW ? 8 : 16 / 4) * ci.chans; // translate bytes to DSD samples
		printf("pos %09I64u - %u:%02u - cpu %.2f%%  \r", pos, time / 60, time % 60, BASS_ASIO_GetCPU());
		fflush(stdout);
		Sleep(50);
	}
	printf("                                                                             \r");

	BASS_ASIO_Free();
	BASS_Free();
}
