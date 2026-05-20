/*
	ASIO device list example
	Copyright (c) 2005-2019 Un4seen Developments Ltd.
*/

#include <stdio.h>
#include "bassasio.h"

void main()
{
	BASS_ASIO_DEVICEINFO di;
	int a;
	for (a = 0; BASS_ASIO_GetDeviceInfo(a, &di); a++) {
		printf("dev %d: %s\ndriver: %s\n", a, di.name, di.driver);
		if (!BASS_ASIO_Init(a, BASS_ASIO_THREAD)) {
			printf("\tunavailable\n");
			continue;
		}
		printf("\trate: %g\n", BASS_ASIO_GetRate());
		{
			BASS_ASIO_CHANNELINFO i;
			int b;
			for (b = 0; BASS_ASIO_ChannelGetInfo(TRUE, b, &i); b++)
				printf("\tin %d: %s (group %d, format %d)\n", b, i.name, i.group, i.format);
			for (b = 0; BASS_ASIO_ChannelGetInfo(FALSE, b, &i); b++)
				printf("\tout %d: %s (group %d, format %d)\n", b, i.name, i.group, i.format);
			if (i.format < BASS_ASIO_FORMAT_DSD_LSB && BASS_ASIO_SetDSD(TRUE)) {
				printf("\tDSD:\n");
				for (b = 0; BASS_ASIO_ChannelGetInfo(TRUE, b, &i); b++)
					printf("\tin %d: %s (group %d, format %d)\n", b, i.name, i.group, i.format);
				for (b = 0; BASS_ASIO_ChannelGetInfo(FALSE, b, &i); b++)
					printf("\tout %d: %s (group %d, format %d)\n", b, i.name, i.group, i.format);
			}
		}
		BASS_ASIO_Free();
	}
}
