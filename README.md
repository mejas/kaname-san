##kaname-san##

A quick-and-dirty implementation of an Audio Discord bot which supports live streams instead of fixed music files or videos.

The name comes from a bot of the same name from Molten Chat.

The back-end currently uses the following tools:

- livestreamer
	- responsible for fetching the stream from the site
- ffmpeg
	- decodes the stream to PCM audio and streams it to Discord

The code is mostly proof-of-concept and is currently in a  misreable state. Cleanup may or may not come soon.

Developed using the following tools:

-	Visual Studio 2015
-	.Net 4.6.1

Note: There is a caveat with libsodium. I added a dependency for it via nuget but there is also the build assembly. I'll probably clean these up in the future.