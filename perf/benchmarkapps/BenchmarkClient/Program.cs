#region Copyright notice and license

// Copyright 2019 The gRPC Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#endregion

using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using System.Linq;
using Greet;
using System.Collections.Generic;

namespace BenchmarkClient
{
    class Program
    {
        private const int Connections = 1;
        private const int DurationSeconds = 60;
        private const string Target = "127.0.0.1:50051";

        static async Task Main(string[] args)
        {
#if false
            Environment.SetEnvironmentVariable("GRPC_VERBOSITY", "DEBUG");
            Environment.SetEnvironmentVariable("GRPC_TRACE", "all");
            GrpcEnvironment.SetLogger(new ConsoleOutLogger());
#endif

            var runTasks = new List<Task>();
            var channels = new List<Channel>();
            var channelRequests = new List<int>();

            Log($"Target server: {Target}");

            await CreateChannels(channels, channelRequests);

            Log("Starting benchmark");

            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(DurationSeconds));
            cts.Token.Register(() =>
            {
                Log("Benchmark duration complete");
            });

            for (int i = 0; i < Connections; i++)
            {
                var id = i;
                runTasks.Add(Task.Run(async () =>
                {
                    Log($"{id}: Starting");

                    var client = new Greeter.GreeterClient(channels[id]);
                    var requests = 0;

                    while (!cts.IsCancellationRequested)
                    {
                        try
                        {
                            var start = DateTime.UtcNow;
                            var response = await client.SayHelloAsync(new HelloRequest
                            {
                                Name = "World"
                            });
                            var end = DateTime.UtcNow;

                            requests++;
                        }
                        catch (Exception ex)
                        {
                            Log($"{id}: Error message: {ex.Message}");
                            cts.Cancel();
                            break;
                        }
                    }

                    channelRequests[id] = requests;

                    Log($"{id}: Finished");
                }));
            }

            cts.Token.WaitHandle.WaitOne();

            await Task.WhenAll(runTasks);

            await StopChannels(channels);

            var totalRequests = channelRequests.Sum();

            Log($"Requests per second: {totalRequests / DurationSeconds}");
            Log("Shutting down");
            Log("Press any key to exit...");
            Console.ReadKey();
        }

        private static async Task CreateChannels(List<Channel> channels, List<int> requests)
        {
            Log($"Creating channels: {Connections}");

            for (int i = 0; i < Connections; i++)
            {
                var channel = new Channel(Target, ChannelCredentials.Insecure);

                Log($"Connecting channel '{i}'");
                await channel.ConnectAsync();

                channels.Add(channel);
                requests.Add(0);
            }
        }

        private static async Task StopChannels(List<Channel> channels)
        {
            for (int i = 0; i < Connections; i++)
            {
                await channels[i].ShutdownAsync();
            }
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }
    }
}