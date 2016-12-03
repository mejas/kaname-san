using Discord;
using Discord.Audio;
using Discord.Commands;
using NAudio.Wave;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace kaname_san
{
    class Program
    {
        //Terribly fast implementation just to get everything up and running
        //Target is to fist support a stream

        static DiscordClient _client;
        static bool _streamStarted;

        static void Main(string[] args)
        {
            Console.WriteLine("Startup");

            Trace.WriteLine("Starting bot client");
            Trace.WriteLine("Reading token from app.config");
            var token = ConfigurationManager.AppSettings.Get("token");

            Trace.WriteLine("Read token: " + token);

            _client =
                new DiscordClient(
                    x =>
                    {
                        x.AppName = "discordbot";
                        x.AppUrl = "discord.net";
                        x.MessageCacheSize = 0;
                        x.UsePermissionsCache = true;
                        x.EnablePreUpdateEvents = true;
                        x.LogLevel = LogSeverity.Debug;
                        x.LogHandler = OnLogMessage;
                    });

            _client.UsingCommands(x =>
            {
                x.PrefixChar = '~';
                x.ExecuteHandler = OnCommandExecuted;
                x.ErrorHandler = OnCommandError;
                x.HelpMode = HelpMode.Private;
            });

            _client.UsingAudio(x =>
            {
                x.Mode = AudioMode.Outgoing;
            });

            var cmd = _client.GetService<CommandService>();

            cmd
                .CreateCommand("stop")
                .Do(async (e) =>
                {
                    await e.Channel.SendMessage("Stopping...");

                    _streamStarted = false;
                });

            cmd
                .CreateCommand("shutdown")
                .Do
                (async (e) =>
                {
                    await e.Channel.SendMessage("Shutting down...");

                    await _client.Disconnect();
                });

            cmd
                .CreateCommand("start")
                .Parameter("streamUrl", ParameterType.Required)
                .Parameter("streamQuality", ParameterType.Required)
                .Do(async (e) =>
            {
                if (_streamStarted)
                {
                    return;
                }

                Channel voiceChan = e.User.VoiceChannel;

                if (voiceChan == null)
                {
                    await e.User.SendMessage("Please join a voice channel first.");
                    return;
                }

                _streamStarted = true;

                _client.SetGame(new Game()); //ha, ha. get it?

                await e.Channel.SendMessage("Starting audio stream...");
                await SendAudio(e.GetArg(0), e.GetArg(1), voiceChan);
                await e.Channel.SendMessage("Audio Stream stopped");

                await e.User.VoiceChannel.LeaveAudio();

                _streamStarted = false;
            });

            _client.ExecuteAndWait(async () =>
            {
                try
                {
                    Console.Write("Authorizing bot via token...");

                    await _client.Connect(token, TokenType.Bot);

                    Console.WriteLine("Done.");
                    Console.WriteLine("Connected as " + _client.CurrentUser.Name);

                    _client.SetGame("Discord .Net");
                }
                catch (Exception ex)
                {
                    _client.Log.Error("ServerExec", ex);
                    await Task.Delay(_client.Config.FailedReconnectDelay);
                }
            });
        }

        //this is mostly a shameless copy-paste of the code from the NAudio documentation
        //plus the discord bot audio example courtesy of the good people from Discord API
        private static async Task SendAudio(string streamUrl, string streamQuality, Channel voiceChan)
        {
            var vClient = await _client.GetService<AudioService>().Join(voiceChan);
            Process liveStreamer = null;
            Process ffmpeg = null;

            var channelCount = _client.GetService<AudioService>().Config.Channels; // Get the number of AudioChannels our AudioService has been configured to use.
            var OutFormat = new WaveFormat(48000, 16, channelCount); // Create a new Output Format, using the spec that Discord will accept, and with the number of channels that our client supports.

            try
            {
                //remove naudio as it is not supported in other operating systems without the WMF. which is a pain to install.
                using (var MP3Reader = new Mp3FileReader("test.mp3"))
                using (var resampler = new MediaFoundationResampler(MP3Reader, OutFormat))
                using (liveStreamer = Process.Start(new ProcessStartInfo()
                {
                    FileName = @"C:\Program Files (x86)\Livestreamer\livestreamer.exe",
                    Arguments = $"--player-external-http --player-external-http-port 1627 {streamUrl} {streamQuality} -l none",
                    UseShellExecute = false
                }))
                {
                    //establish connection as fast as possible. should i increment time?
                    Thread.Sleep(TimeSpan.FromSeconds(10));

                    //shamelessly copy-pasted from the sample docs @ Discord.Net
                    using (ffmpeg = Process.Start(new ProcessStartInfo
                    { // FFmpeg requires us to spawn a process and hook into its stdout, so we will create a Process
                        FileName = "ffmpeg",
                        Arguments = $"-i  http://127.0.0.1:1627/ " + // Here we provide a list of arguments to feed into FFmpeg. -i means the location of the file/URL it will read from
                            "-f s16le -ar 48000 -ac 2 pipe:1 -loglevel panic", // Next, we tell it to output 16-bit 48000Hz PCM, over 2 channels, to stdout.
                        UseShellExecute = false,
                        RedirectStandardOutput = true // Capture the stdout of the process
                    }))
                    {

                        Thread.Sleep(2000); // Sleep for a few seconds to FFmpeg can start processing data.

                        int blockSize = 3840; // The size of bytes to read per frame; 1920 for mono
                        byte[] buffer = new byte[blockSize];
                        int byteCount;

                        //resampler.ResamplerQuality = 60; // Set the quality of the resampler to 60, the highest quality

                        while (true) // Loop forever, so data will always be read
                        {
                            if(!_streamStarted)
                            {
                                break;
                            }

                            byteCount = ffmpeg.StandardOutput.BaseStream // Access the underlying MemoryStream from the stdout of FFmpeg
                                    .Read(buffer, 0, blockSize); // Read stdout into the buffer

                            //this is where the handler for the dead stream substitution is, but naudio borked on it so there's that
                            if (byteCount == 0) // FFmpeg did not output anything
                            {
                                byteCount = resampler.Read(buffer, 0, blockSize);

                                if (byteCount == 0)
                                {
                                    MP3Reader.Seek(0, System.IO.SeekOrigin.Begin);
                                }

                                if (byteCount < blockSize)
                                {
                                    // Incomplete Frame
                                    for (int i = byteCount; i < blockSize; i++)
                                        buffer[i] = 0;
                                }
                            }

                            vClient.Send(buffer, 0, byteCount); // Send our data to Discord
                        }

                        liveStreamer.Kill(); //terrible ideas, mk. I
                    }
                }
            }
            catch (Exception ex)
            {
                _client.Log.Error("player", ex);
                Console.WriteLine(ex.Message);
            }
            finally
            {
                vClient.Wait(); // Wait for the Voice Client to finish sending data, as ffMPEG may have already finished buffering out a song, and it is unsafe to return now.
            }
        }

        private static int PermissionResolver(User arg1, Channel arg2)
        {
            throw new NotImplementedException();
        }

        private static void OnCommandError(object sender, CommandErrorEventArgs e)
        {
            e.User.SendMessage(e.ErrorType.ToString());
        }

        private static void OnCommandExecuted(object sender, CommandEventArgs e)
        {
            _client.Log.Info("Command", String.Format("[{0}] {1}", e.User.Name, e.Command.Text));
        }

        private static void OnLogMessage(object sender, LogMessageEventArgs e)
        {
            Console.WriteLine(e.Message);
        }
    }
}
